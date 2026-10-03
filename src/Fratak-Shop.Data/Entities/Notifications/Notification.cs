using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Notifications;

/// <summary>
/// In-app notification (اعلان). Created automatically by the order, payment and
/// bank-transfer flows, or manually by a staff member from the admin panel.
/// A notification without <see cref="UserId"/> is a broadcast shown to every customer.
/// </summary>
public class Notification : BaseEntity
{
    public Guid? UserId { get; set; }

    [Required]
    [MaxLength(250)]
    public string Title { get; set; } = null!;

    [MaxLength(1000)]
    public string? Message { get; set; }

    public NotificationType Type { get; set; }

    public NotificationChannel Channel { get; set; } = NotificationChannel.InApp;

    /// <summary>
    /// Optional deep-link opened when the notification is clicked.
    /// </summary>
    [MaxLength(500)]
    public string? Url { get; set; }

    public bool IsRead { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>
    /// True when the notification was sent to every customer of the shop.
    /// </summary>
    public bool IsBroadcast { get; set; }

    /// <summary>
    /// Staff member who created the notification (null for system notifications).
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [ForeignKey(nameof(CreatedByUserId))]
    public User? CreatedByUser { get; set; }
}

public enum NotificationType
{
    None = 0,
    Info = 1,
    Success = 2,
    Warning = 3,
    Error = 4,
    Order = 5,
    Payment = 6,
    BankTransfer = 7,
    Message = 8,
    Seo = 9,
    System = 10,
}

public enum NotificationChannel
{
    InApp = 1,
    Sms = 2,
    Email = 3,
}
