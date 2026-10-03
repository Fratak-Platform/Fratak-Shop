using Fratak_Shop.Core.DTOs.Settings.Query;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// Base page model of the admin area. It guards every page behind the staff policy,
/// exposes the role of the current user (for menu and inline permissions) and loads
/// the counters shown as badges in the admin navigation.
/// </summary>
[Authorize(Policy = PolicyNames.StaffOnly)]
public abstract class AdminPageModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender) : PageModel
{
    public StorefrontSettings Site { get; private set; } = new();

    public LayoutViewModel Layout { get; private set; } = new();

    public List<AdminNavItem> Navigation { get; private set; } = [];

    public int PendingBankTransfers { get; private set; }

    public int PendingSeoAudits { get; private set; }

    public int UnreadNotifications { get; private set; }

    public Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirst(FratakClaims.UserId)?.Value, out var id) ? id : null;

    public string CurrentUserName => User.FindFirst(FratakClaims.FullName)?.Value ?? "کاربر پنل";

    public string CurrentRoleTitle => Roles.Count == 0 ? "کاربر پنل" : Roles[0].GetTitle();

    public List<UserRole> Roles => User.Claims
        .Where(claim => claim.Type == System.Security.Claims.ClaimTypes.Role)
        .Select(claim => Enum.TryParse<UserRole>(claim.Value, out var role) ? role : UserRole.None)
        .Where(role => role != UserRole.None)
        .Distinct()
        .ToList();

    public bool IsSuperAdmin => User.IsInRole(nameof(UserRole.SuperAdmin));

    public bool HasRole(UserRole role) => Roles.Contains(role);

    /// <summary>آیا کاربر می‌تواند محتوای سئو را ویرایش کند؟ (کارشناس سئو بله، ناظر خیر)</summary>
    public bool CanEdit => IsSuperAdmin || HasRole(UserRole.Admin) || HasRole(UserRole.ProductManager) ||
                           HasRole(UserRole.ContentManager) || HasRole(UserRole.OrderManager);

    public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context,
        PageHandlerExecutionDelegate next)
    {
        Site = await settings.GetStorefrontSettingsAsync();

        Layout = new LayoutViewModel
        {
            Site = Site,
            CurrentPath = HttpContext.Request.Path.Value ?? "/admin",
            IsAuthenticated = true,
            UserFullName = CurrentUserName,
            IsStaff = true,
        };

        // Counters are only loaded for the roles that actually see the badge,
        // so a product manager never pays for the SEO dashboard query.
        var roles = Roles;

        if (roles.Any(role => role is UserRole.SuperAdmin or UserRole.Admin or UserRole.OrderManager or UserRole.SupportAgent))
            PendingBankTransfers = await bankTransfers.GetPendingCountAsync();

        if (roles.Any(role => role is UserRole.SuperAdmin or UserRole.Admin or UserRole.SeoManager))
            PendingSeoAudits = (await seoAudits.GetDashboardAsync()).PendingAudits;

        if (CurrentUserId.HasValue)
            UnreadNotifications = await notifications.GetUnreadCountAsync(CurrentUserId.Value);

        Navigation = AdminNavigation.Build(roles, new AdminNavCounters
        {
            PendingBankTransfers = PendingBankTransfers,
            PendingSeoAudits = PendingSeoAudits,
            UnreadNotifications = UnreadNotifications,
        });

        ViewData[ViewDataKeys.Layout] = Layout;
        ViewData[ViewDataKeys.AdminNavigation] = Navigation;
        await next();
    }

    protected void Notify(string message, string type = "success")
    {
        TempData[ViewDataKeys.Message] = message;
        TempData[ViewDataKeys.MessageType] = type;
    }

    /// <summary>درگاه پیامک فعال؛ در حالت توسعه پیامک واقعی ارسال نمی‌شود.</summary>
    protected ISmsSender SmsSender { get; } = smsSender;

    protected IMenuService MenuService { get; } = menus;
}

public class AdminNavItem
{
    public string Title { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Icon { get; set; } = "home";
    public int Badge { get; set; }
    public string? Group { get; set; }
    public bool Exact { get; set; }
}

public class AdminNavCounters
{
    public int PendingBankTransfers { get; set; }
    public int PendingSeoAudits { get; set; }
    public int UnreadNotifications { get; set; }
}
