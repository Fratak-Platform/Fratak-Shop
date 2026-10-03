using Fratak_Shop.Core.DTOs.Notifications.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Notifications;

namespace Fratak_Shop.Core.Services.Notifications;

/// <summary>
/// Central notification service. Every flow of the shop (order status, payment
/// verification, bank transfer review, contact form, SEO reminders) notifies the
/// customer and the staff through this service, so the notification rules live in
/// one place instead of being scattered over the controllers.
/// </summary>
public interface INotificationService
{
    /// <summary>اعلان برای یک کاربر مشخص.</summary>
    Task NotifyUserAsync(Guid userId, string title, string? message, NotificationType type,
        string? url = null, CancellationToken cancellationToken = default);

    /// <summary>اعلان برای کاربر بر اساس شماره موبایل (پیام‌های سیستمی/کد تأیید).</summary>
    Task NotifyPhoneAsync(string phoneNumber, string message, NotificationType type,
        CancellationToken cancellationToken = default);

    /// <summary>اعلان برای همه اعضای پنل مدیریت.</summary>
    Task NotifyStaffAsync(string title, string? message, NotificationType type,
        string? url = null, CancellationToken cancellationToken = default);

    /// <summary>اعلان عمومی برای همه مشتریان.</summary>
    Task<OperationResult> BroadcastAsync(string title, string? message, string? url, Guid? createdByUserId,
        CancellationToken cancellationToken = default);

    Task<List<NotificationDto>> GetForUserAsync(Guid userId, int take = 20, bool unreadOnly = false);

    Task<int> GetUnreadCountAsync(Guid userId);

    Task<OperationResult> MarkAsReadAsync(Guid notificationId, Guid? userId);

    Task<OperationResult> MarkAllAsReadAsync(Guid userId);

    /// <summary>فهرست اعلان‌ها برای پنل مدیریت.</summary>
    Task<NotificationFilterResult> GetFilterAsync(NotificationFilterParams filterParams);

    Task<OperationResult> DeleteAsync(Guid id);
}
