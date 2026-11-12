using Fratak_Shop.Core.Services.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace Fratak_Shop.Web.Infrastructure.Realtime;

/// <summary>
/// پیاده‌سازی واقعی <see cref="IRealtimeNotifier"/> با SignalR.
/// <para>
/// سرویس‌های Core (اعلان مشتری، اعلان کارکنان) فقط <see cref="IRealtimeNotifier"/> را صدا می‌زنند و
/// این کلاس پیام را به مرورگرهای باز می‌رساند. خطای ارسال (مثلاً قطع اتصال) فقط لاگ می‌شود و
/// عملیات اصلی را خراب نمی‌کند.
/// </para>
/// ثبت در DI: <c>WebServiceCollectionExtensions</c> (جایگزین NullRealtimeNotifier).
/// </summary>
public sealed class SignalRRealtimeNotifier(IHubContext<LiveHub> hub, ILogger<SignalRRealtimeNotifier> logger) : IRealtimeNotifier
{
    public Task SendToUserAsync(Guid userId, string method, object payload) =>
        SafeAsync(() => hub.Clients.User(userId.ToString()).SendAsync(method, payload));

    public Task SendToAllUsersAsync(string method, object payload) =>
        SafeAsync(() => hub.Clients.Group(LiveHub.UsersGroup).SendAsync(method, payload));

    public Task SendToStaffAsync(string? permission, string method, object payload)
    {
        // مالک‌ها همیشه؛ و اگر مجوز داده شده کارکنان دارای آن مجوز. (مالک عضو گروه‌های مجوز نیست، پس پیام تکراری نمی‌رود.)
        IReadOnlyList<string> groups = string.IsNullOrEmpty(permission)
            ? [LiveHub.OwnersGroup]
            : [LiveHub.OwnersGroup, LiveHub.PermissionGroup(permission)];
        return SafeAsync(() => hub.Clients.Groups(groups).SendAsync(method, payload));
    }

    private async Task SafeAsync(Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Realtime message could not be sent.");
        }
    }
}
