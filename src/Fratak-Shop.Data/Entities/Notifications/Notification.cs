using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Loyalty;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Notifications;

/// <summary>
/// In-site notification. Personal notifications have a <see cref="UserId"/> (order
/// status, wallet, club points, ...); broadcasts have none and target an
/// <see cref="Audience"/> (all customers, customers with/without orders, a club level).
/// Broadcasts are stored once – read state lives in <see cref="NotificationRead"/>.
/// </summary>
public class Notification : BaseEntity
{
    public Guid? UserId { get; set; }

    public NotificationAudience Audience { get; set; } = NotificationAudience.User;

    /// <summary>Club level of <see cref="NotificationAudience.LoyaltyTier"/> broadcasts.</summary>
    public Guid? LoyaltyTierId { get; set; }

    public NotificationCategory Category { get; set; } = NotificationCategory.System;

    /// <summary>Event that produced a personal notification (null for manual messages).</summary>
    public NotificationEvent? Event { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    /// <summary>Plain text body (line breaks are kept when rendered).</summary>
    [Required]
    [MaxLength(2000)]
    public string Body { get; set; } = null!;

    /// <summary>Optional site-relative or https link ("مشاهده جزئیات").</summary>
    [MaxLength(500)]
    public string? LinkUrl { get; set; }

    [MaxLength(100)]
    public string? LinkText { get; set; }

    /// <summary>Highlighted (pinned) broadcast, e.g. a running campaign.</summary>
    public bool IsPinned { get; set; }

    /// <summary>Personal notifications: when the customer read it.</summary>
    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>Broadcasts are visible from this moment (scheduling).</summary>
    public DateTimeOffset? PublishAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public bool IsActive { get; set; } = true;

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [ForeignKey(nameof(LoyaltyTierId))]
    public LoyaltyTier? LoyaltyTier { get; set; }
}

/// <summary>Read state of a broadcast for one customer.</summary>
public class NotificationRead : BaseEntity
{
    public Guid NotificationId { get; set; }

    public Guid UserId { get; set; }

    public DateTimeOffset ReadAt { get; set; }

    [ForeignKey(nameof(NotificationId))]
    public Notification Notification { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}

/// <summary>
/// Text of the automatic notifications, editable in the admin panel. Placeholders
/// like {OrderNumber}, {Amount}, {Points}, {Name} are replaced when sending.
/// SMS sending is prepared (<see cref="SendSms"/>, <see cref="SmsText"/>) for when a
/// provider is connected.
/// </summary>
public class NotificationTemplate : BaseEntity
{
    public NotificationEvent Event { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    [Required]
    [MaxLength(2000)]
    public string Body { get; set; } = null!;

    [MaxLength(100)]
    public string? LinkText { get; set; }

    /// <summary>Create the in-site notification.</summary>
    public bool IsActive { get; set; } = true;

    public bool SendSms { get; set; }

    /// <summary>Also send as a browser push notification to the customer's subscribed devices.</summary>
    public bool SendPush { get; set; } = true;

    /// <summary>
    /// مرحلهٔ دوازدهم: همین عنوان و متن به ایمیل مشتری هم (اگر ایمیل ثبت کرده باشد) با قالب برند فرستاده شود.
    /// پیش‌فرض خاموش؛ مدیر برای هر رویداد جدا روشنش می‌کند. سرویس ایمیل: پنل ← فنی ← سرویس ایمیل.
    /// </summary>
    public bool SendEmail { get; set; }

    [MaxLength(500)]
    public string? SmsText { get; set; }
}

public enum NotificationAudience
{
    User = 0,
    AllCustomers = 1,
    CustomersWithOrders = 2,
    CustomersWithoutOrders = 3,
    LoyaltyTier = 4,
}

public enum NotificationCategory
{
    None = 0,
    System = 1,
    Order = 2,
    Wallet = 3,
    Loyalty = 4,
    Promotion = 5,
    Security = 6,
    Support = 7,
}

public enum NotificationEvent
{
    None = 0,
    Welcome = 1,
    OrderPlaced = 10,
    OrderPaid = 11,
    OrderConfirmed = 12,
    OrderProcessing = 13,
    OrderShipped = 14,
    OrderDelivered = 15,
    OrderCancelled = 16,
    OrderReturned = 17,
    PaymentFailed = 18,
    ReturnRequested = 20,
    ReturnApproved = 21,
    ReturnRejected = 22,
    ReturnRefunded = 23,
    OrderFeedbackRequest = 24,

    /// <summary>لینک پرداخت سفارشی که کارمند در پنل برای مشتری ثبت کرده است (مرحلهٔ ۷).</summary>
    OrderPaymentLink = 25,

    /// <summary>پیام فروشگاه دربارهٔ یک سفارش؛ مثلاً «کالای X ناموجود است، حذف شود؟» (مرحلهٔ ۷).</summary>
    OrderMessage = 26,
    WalletTopUp = 30,
    WalletAdjusted = 31,
    WithdrawalRequested = 32,
    WithdrawalApproved = 33,
    WithdrawalPaid = 34,
    WithdrawalRejected = 35,
    GiftCardPurchased = 36,
    GiftCardRedeemed = 37,
    PointsEarned = 50,
    TierUpgraded = 51,
    RewardRedeemed = 52,
    ReferralJoined = 53,
    PasswordChanged = 70,
    TwoFactorEnabled = 71,
    TwoFactorDisabled = 72,
    NewDeviceLogin = 73,
    AbandonedCartReminder = 90,
    BackInStock = 100,
    PriceDrop = 101,
    TicketCreated = 110,
    TicketReplied = 111,
}
