using System.Reflection;
using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Auditing;
using Fratak_Shop.Data.Entities.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fratak_Shop.Web.Infrastructure.Admin;

/// <summary>
/// Permission(s) needed for an admin page (on the page model) or a single handler (on the handler method).
/// One attribute = any of the listed permissions; several attributes = all of them.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class AdminPermissionAttribute(params string[] anyOf) : Attribute
{
    public string[] AnyOf { get; } = anyOf;
}

/// <summary>Only the store owner (e.g. staff and roles of other owners).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AdminOwnerOnlyAttribute : Attribute;

/// <summary>
/// Gatekeeper of the whole Admin area. Signed-out visitors go to the login page; signed-in customers get a
/// plain 404 (the panel is not advertised); staff without the needed permission get the «access denied»
/// page and a security-log entry. Every admin response is private, uncached and noindex, and every change
/// saved during an admin request is audited with the staff member as actor.
/// </summary>
public sealed class AdminPageFilter(IAdminAccessService accessService, IMonitoringService monitoring, ILogger<AdminPageFilter> logger) : IAsyncPageFilter
{
    public const string AccessItemKey = "fratak.admin.access";

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var http = context.HttpContext;
        http.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        http.Response.Headers.Pragma = "no-cache";
        http.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";

        var userId = http.User.GetUserId();
        if (userId is null)
        {
            context.Result = new ChallengeResult();
            return;
        }

        var access = await accessService.GetAccessAsync(userId.Value);
        if (!access.IsStaff)
        {
            context.Result = new NotFoundResult();
            return;
        }

        http.Items[AccessItemKey] = access;

        var pageType = context.HandlerInstance.GetType();
        var method = context.HandlerMethod?.MethodInfo;
        var isDeniedPage = pageType.Name == "DeniedModel";

        if (!isDeniedPage && !IsAllowed(access, pageType, method, out var missing))
        {
            logger.LogInformation("Admin access denied: {User} → {Path} (needs {Permission})", userId, http.Request.Path, missing);
            try
            {
                await monitoring.LogSecurityAsync(SecurityEventType.AdminAccessDenied, userId, Core.Services.Admin.MonitoringService.Mask(access.PhoneNumber),
                    http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers.UserAgent.ToString(), $"{http.Request.Method} {http.Request.Path} – {missing}");
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Writing the access-denied security log failed");
            }

            context.Result = new RedirectToPageResult("/Denied", new { area = "Admin", need = missing });
            return;
        }

        using (AuditContext.Begin(new AuditActor(access.UserId, access.DisplayName, http.Connection.RemoteIpAddress?.ToString(),
                   $"{http.Request.Method} {http.Request.Path}")))
        {
            await next();
        }
    }

    public static bool IsAllowed(AdminAccess access, Type pageType, MethodInfo? method, out string? missing)
    {
        missing = null;
        if (access.IsOwner)
            return true;

        if (pageType.GetCustomAttribute<AdminOwnerOnlyAttribute>() is not null || method?.GetCustomAttribute<AdminOwnerOnlyAttribute>() is not null)
        {
            missing = "owner";
            return false;
        }

        var required = pageType.GetCustomAttributes<AdminPermissionAttribute>(true)
            .Concat(method?.GetCustomAttributes<AdminPermissionAttribute>(true) ?? []);
        foreach (var attribute in required)
        {
            if (!access.HasAny(attribute.AnyOf))
            {
                missing = string.Join("|", attribute.AnyOf);
                return false;
            }
        }

        return true;
    }
}

/// <summary>Base class of the admin pages: current staff member and permission checks for the views.</summary>
public abstract class AdminPageModel : ShopPageModel
{
    private AdminAccess? _access;

    public AdminAccess Access => _access ??= HttpContext.Items[AdminPageFilter.AccessItemKey] as AdminAccess
                                             ?? AdminAccess.None(CurrentUserId ?? Guid.Empty);

    public bool Can(string permission) => Access.Has(permission);

    public bool CanAny(params string[] permissions) => Access.HasAny(permissions);

    protected Guid ActorId => Access.UserId;

    protected string ActorName => Access.DisplayName;

    /// <summary>Redirect back to the current page (keeps the query string: filters and paging).</summary>
    protected IActionResult BackToList() => Redirect(Request.Path + Request.QueryString);

    protected static int ClampPage(int page) => Math.Max(1, page);
}
