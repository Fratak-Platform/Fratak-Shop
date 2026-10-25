using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Web.Services;

namespace Fratak_Shop.Web.Infrastructure.Admin;

/// <summary>
/// Records login / OTP / lockout events for «رویدادهای امنیتی». Uses its own scope so it never saves
/// unrelated pending changes of the request, and never breaks the login flow when logging fails.
/// </summary>
public sealed class SecurityEventRecorder(IServiceScopeFactory scopeFactory, IHttpContextAccessor accessor, ILogger<SecurityEventRecorder> logger)
{
    public async Task RecordAsync(SecurityEventType type, Guid? userId, string? phone, string? details = null)
    {
        try
        {
            var http = accessor.HttpContext;
            using var scope = scopeFactory.CreateScope();
            var monitoring = scope.ServiceProvider.GetRequiredService<IMonitoringService>();
            await monitoring.LogSecurityAsync(type, userId, MonitoringService.Mask(phone),
                http is null ? null : ClientInfo.IpAddress(http),
                http is null ? null : ClientInfo.UserAgent(http),
                details);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Recording security event {Type} failed", type);
        }
    }
}
