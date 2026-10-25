using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace Fratak_Shop.Web.Infrastructure.Realtime;

/// <summary>
/// هاب زندهٔ سایت و پنل (آدرس <c>/hubs/live</c>).
/// <para>
/// هر مرورگرِ کاربرِ واردشده به این هاب وصل می‌شود (فایل <c>wwwroot/assets/js/live.js</c>) و پیام‌ها را
/// بدون رفرش صفحه دریافت می‌کند: اعلان شخصی مشتری، اعلان داخلی کارکنان و «چه کسی این رکورد را ویرایش می‌کند».
/// </para>
/// گروه‌ها:
/// - <c>users</c>: همهٔ کاربران واردشده (اعلان همگانی)؛
/// - <c>owners</c>: مالک‌های فروشگاه؛
/// - <c>perm:{permission}</c>: کارکنان دارای یک مجوز، مثلاً <c>perm:orders.view</c>؛
/// - <c>edit:{key}</c>: کسانی که صفحهٔ ویرایش یک رکورد را باز کرده‌اند (هشدار ویرایش هم‌زمان).
/// ارسال از سمت سرور با <see cref="SignalRRealtimeNotifier"/> انجام می‌شود.
/// </summary>
public sealed partial class LiveHub(IAdminAccessService accessService, ILogger<LiveHub> logger) : Hub
{
    public const string Path = "/hubs/live";
    public const string UsersGroup = "users";
    public const string OwnersGroup = "owners";

    /// <summary>ویرایشگرهای فعلی هر کلید: key → (connectionId → نفر).</summary>
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, EditorInfo>> Editors = new();

    /// <summary>کلیدهایی که هر اتصال در آن‌ها حضور دارد (برای پاک‌سازی هنگام قطع اتصال).</summary>
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> ConnectionKeys = new();

    public static string PermissionGroup(string permission) => "perm:" + permission;

    public override async Task OnConnectedAsync()
    {
        // فقط کاربر واردشده؛ بازدیدکنندهٔ ناشناس اتصال زنده ندارد.
        var userId = Context.User.GetUserId();
        if (userId is null)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, UsersGroup);

        var access = await accessService.GetAccessAsync(userId.Value);
        if (access.IsOwner)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, OwnersGroup);
        }
        else
        {
            foreach (var permission in access.Permissions)
                await Groups.AddToGroupAsync(Context.ConnectionId, PermissionGroup(permission));
        }

        Context.Items["staff"] = access.IsStaff;
        Context.Items["name"] = access.DisplayName;
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (ConnectionKeys.TryRemove(Context.ConnectionId, out var keys))
        {
            foreach (var key in keys.Keys)
                await RemoveEditorAsync(key);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// مرورگر اعلام می‌کند «من صفحهٔ ویرایش این رکورد را باز کرده‌ام».
    /// کلید نمونه: <c>product:3f2a…</c> یا <c>manage:coupons:9c1d…</c>. فقط کارکنان مجازند.
    /// بقیهٔ ویرایشگرهای همان کلید فهرست به‌روز را دریافت می‌کنند تا هشدار «فلانی هم در حال ویرایش است» ببینند.
    /// </summary>
    public async Task JoinEdit(string key)
    {
        if (!IsStaff() || !IsValidKey(key))
            return;

        var userId = Context.User.GetUserId()!.Value;
        var name = Context.Items["name"] as string ?? "کارمند";
        Editors.GetOrAdd(key, _ => new())[Context.ConnectionId] = new EditorInfo(userId, name, DateTimeOffset.UtcNow);
        ConnectionKeys.GetOrAdd(Context.ConnectionId, _ => new())[key] = 0;

        await Groups.AddToGroupAsync(Context.ConnectionId, EditGroup(key));
        await BroadcastEditorsAsync(key);
    }

    /// <summary>مرورگر صفحهٔ ویرایش را بست یا ذخیره کرد.</summary>
    public async Task LeaveEdit(string key)
    {
        if (!IsValidKey(key))
            return;

        if (ConnectionKeys.TryGetValue(Context.ConnectionId, out var keys))
            keys.TryRemove(key, out _);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, EditGroup(key));
        await RemoveEditorAsync(key);
    }

    /// <summary>
    /// اعلام «ذخیره شد» به بقیهٔ ویرایشگرها تا بدانند نسخهٔ صفحه‌شان قدیمی شده است.
    /// </summary>
    public async Task Saved(string key)
    {
        if (!IsStaff() || !IsValidKey(key))
            return;

        var name = Context.Items["name"] as string ?? "کارمند";
        await Clients.OthersInGroup(EditGroup(key)).SendAsync(RealtimeMethods.Editors, new { key, saved = name });
    }

    private async Task RemoveEditorAsync(string key)
    {
        if (Editors.TryGetValue(key, out var editors))
        {
            editors.TryRemove(Context.ConnectionId, out _);
            if (editors.IsEmpty)
                Editors.TryRemove(key, out _);
        }

        try
        {
            await BroadcastEditorsAsync(key);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Editors broadcast for {Key} failed.", key);
        }
    }

    private Task BroadcastEditorsAsync(string key)
    {
        // هر نفر یک بار (اگر چند تب باز کرده باشد تکراری نشان داده نمی‌شود).
        Editors.TryGetValue(key, out var editors);
        var list = (editors?.Values ?? Enumerable.Empty<EditorInfo>())
            .GroupBy(e => e.UserId)
            .Select(g => new { id = g.Key, name = g.First().Name, since = g.Min(e => e.Since) })
            .ToList();
        return Clients.Group(EditGroup(key)).SendAsync(RealtimeMethods.Editors, new { key, editors = list });
    }

    private bool IsStaff() => Context.Items.TryGetValue("staff", out var value) && value is true;

    private static string EditGroup(string key) => "edit:" + key;

    private static bool IsValidKey(string? key) => !string.IsNullOrEmpty(key) && KeyRegex().IsMatch(key);

    [GeneratedRegex("^[a-z0-9:/_-]{1,120}$")]
    private static partial Regex KeyRegex();

    private sealed record EditorInfo(Guid UserId, string Name, DateTimeOffset Since);
}

/// <summary>
/// شناسهٔ کاربر هر اتصال SignalR = شناسهٔ کاربر در توکن JWT؛ تا <c>Clients.User(id)</c> کار کند.
/// </summary>
public sealed class ShopUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User.GetUserId()?.ToString();
}
