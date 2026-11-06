using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Shopping;

/// <summary>
/// A signed-in customer's cart that was left without an order. Created by the
/// background job once the cart has been idle for the configured time; reminders
/// are sent by <see cref="AbandonedCartReminderStep"/> and the record is closed
/// when the customer orders (recovered), empties the cart or it expires.
/// </summary>
public class AbandonedCart : BaseEntity
{
    public Guid CartId { get; set; }

    public Guid UserId { get; set; }

    public AbandonedCartStatus Status { get; set; } = AbandonedCartStatus.Open;

    /// <summary>Last cart activity (the moment the cart was left).</summary>
    public DateTimeOffset AbandonedAt { get; set; }

    /// <summary>Items total (final prices) when detected / last refreshed.</summary>
    public decimal CartAmount { get; set; }

    public int ItemCount { get; set; }

    public int RemindersSent { get; set; }

    public DateTimeOffset? LastReminderAt { get; set; }

    /// <summary>Personal discount code sent with a reminder (if any).</summary>
    public Guid? CouponId { get; set; }

    [MaxLength(50)]
    public string? CouponCode { get; set; }

    public Guid? RecoveredOrderId { get; set; }

    /// <summary>Payable amount of the order that recovered the cart.</summary>
    public decimal RecoveredAmount { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    [ForeignKey(nameof(CartId))]
    public Cart Cart { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    [ForeignKey(nameof(CouponId))]
    public Coupon? Coupon { get; set; }
}

public enum AbandonedCartStatus
{
    None = 0,
    Open = 1,       // رها شده – در انتظار بازگشت مشتری
    Recovered = 2,  // به سفارش تبدیل شد
    Expired = 3,    // مهلت پیگیری تمام شد
    Cleared = 4,    // سبد خالی شد
}

/// <summary>
/// One step of the abandoned-cart reminder sequence (e.g. after 1 hour a notice,
/// after 24 hours a notice with a personal discount code).
/// Placeholders: {Name}, {ItemCount}, {CartAmount}, {CouponCode}, {CouponValue}, {SiteName}.
/// </summary>
public class AbandonedCartReminderStep : BaseEntity
{
    /// <summary>Order of the step (1, 2, 3 ...). Steps are sent one after another.</summary>
    public int StepNumber { get; set; }

    /// <summary>Minutes after the cart was abandoned.</summary>
    public int DelayMinutes { get; set; }

    public ReminderChannel Channel { get; set; } = ReminderChannel.Notification;

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    [Required]
    [MaxLength(1000)]
    public string Message { get; set; } = null!;

    [MaxLength(500)]
    public string? SmsText { get; set; }

    /// <summary>Creates a personal discount code for the customer with this step.</summary>
    public bool IncludeCoupon { get; set; }

    public CouponType CouponType { get; set; } = CouponType.Percentage;

    public decimal CouponValue { get; set; }

    public decimal? CouponMaxDiscountAmount { get; set; }

    public decimal CouponMinOrderAmount { get; set; }

    public int CouponValidDays { get; set; } = 3;

    public bool IsActive { get; set; } = true;
}

[Flags]
public enum ReminderChannel
{
    None = 0,
    Notification = 1,  // اعلان داخل سایت
    Sms = 2,           // پیامک
    Both = Notification | Sms,
}
