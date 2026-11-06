namespace Fratak_Shop.Core.Services.Notifications;

/// <summary>
/// ارسال «زنده» پیام به مرورگرهای باز (سایت و پنل) بدون رفرش صفحه.
/// <para>
/// لایهٔ Core فقط این قرارداد را می‌شناسد؛ پیاده‌سازی واقعی با SignalR در لایهٔ Web است
/// (<c>Fratak_Shop.Web.Infrastructure.Realtime.SignalRRealtimeNotifier</c>). در تست‌ها و هر جا که
/// Web نباشد، <see cref="NullRealtimeNotifier"/> استفاده می‌شود که کاری نمی‌کند.
/// </para>
/// نام متدهای سمت مرورگر در <see cref="RealtimeMethods"/> آمده است.
/// مثال: <c>await realtime.SendToUserAsync(userId, RealtimeMethods.Notification, new { title, body });</c>
/// </summary>
public interface IRealtimeNotifier
{
    /// <summary>ارسال به همهٔ پنجره‌های باز یک کاربر (همهٔ دستگاه‌هایش).</summary>
    Task SendToUserAsync(Guid userId, string method, object payload);

    /// <summary>ارسال به همهٔ کاربران واردشدهٔ سایت (مثلاً اعلان همگانی).</summary>
    Task SendToAllUsersAsync(string method, object payload);

    /// <summary>
    /// ارسال به کارکنانی که پنل یا سایت را باز دارند. اگر <paramref name="permission"/> داده شود
    /// فقط کارکنان دارای آن مجوز (و مالک) دریافت می‌کنند؛ مثلاً <c>"orders.view"</c>.
    /// </summary>
    Task SendToStaffAsync(string? permission, string method, object payload);
}

/// <summary>نام رویدادهایی که مرورگر (فایل live.js) به آن‌ها گوش می‌دهد. با live.js هماهنگ نگه دارید.</summary>
public static class RealtimeMethods
{
    /// <summary>اعلان شخصی مشتری: <c>{ id, title, body, url, unread }</c>.</summary>
    public const string Notification = "notification";

    /// <summary>اعلان داخلی کارمند: <c>{ id, kind, title, body, url, unread }</c>.</summary>
    public const string StaffNotification = "staff";

    /// <summary>«شمارنده‌ها را دوباره بگیر» (مثلاً بعد از اعلان همگانی که مخاطبش گروهی است).</summary>
    public const string Refresh = "refresh";

    /// <summary>فهرست کسانی که هم‌اکنون یک رکورد را ویرایش می‌کنند: <c>{ key, editors: [...] }</c>.</summary>
    public const string Editors = "editors";
}

/// <summary>پیاده‌سازی خالی (پیش‌فرض Core و تست‌ها): هیچ پیامی نمی‌فرستد.</summary>
public sealed class NullRealtimeNotifier : IRealtimeNotifier
{
    public Task SendToUserAsync(Guid userId, string method, object payload) => Task.CompletedTask;

    public Task SendToAllUsersAsync(string method, object payload) => Task.CompletedTask;

    public Task SendToStaffAsync(string? permission, string method, object payload) => Task.CompletedTask;
}
