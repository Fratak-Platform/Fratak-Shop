using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Orders;

/// <summary>
/// Customer order with a full snapshot of the shipping destination so the
/// history stays intact even if the user or the location dictionary changes.
/// </summary>
public class Order : BaseEntity
{
    public Guid? UserId { get; set; }

    [Required]
    [MaxLength(50)]
    public string OrderNumber { get; set; } = null!;

    public OrderStatus Status { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public PaymentStatus PaymentStatus { get; set; }

    [MaxLength(100)]
    public string? TransactionId { get; set; }

    [MaxLength(100)]
    public string? TrackingCode { get; set; }

    // ── منبع ورودی سفارش (مرحلهٔ هفتم – فاز ۷) ──────────────────────────
    // موقع ثبت سفارش از کوکی آخرین منبع غیرمستقیم (UTM یا ارجاع‌دهنده) پر می‌شود؛ گزارش: /admin/reports/campaigns.

    /// <summary>منبع ورودی خریدار. مثال: «instagram»، «google»، «direct».</summary>
    [MaxLength(60)]
    public string? TrafficSource { get; set; }

    /// <summary>نوع منبع. مثال: «social»، «organic»، «cpc».</summary>
    [MaxLength(30)]
    public string? TrafficMedium { get; set; }

    /// <summary>نام کمپین (utm_campaign). مثال: «yalda-1405».</summary>
    [MaxLength(100)]
    public string? TrafficCampaign { get; set; }

    /// <summary>شناسهٔ تصادفی مرورگر خریدار برای وصل کردن سفارش به قیف بازدید (همان VisitorDay.VisitorKey).</summary>
    [MaxLength(32)]
    public string? VisitorKey { get; set; }

    public decimal SubtotalAmount { get; set; }

    /// <summary>Order level discount (coupon + manual discount).</summary>
    public decimal DiscountAmount { get; set; }

    public Guid? CouponId { get; set; }

    /// <summary>Snapshot of the used coupon code.</summary>
    [MaxLength(50)]
    public string? CouponCode { get; set; }

    /// <summary>Part of <see cref="DiscountAmount"/> that came from the coupon (shipping discount included).</summary>
    public decimal CouponDiscountAmount { get; set; }

    public decimal ShippingAmount { get; set; }

    public decimal PayableAmount { get; set; }

    [MaxLength(100)]
    public string? ShippingMethod { get; set; }

    public DateTimeOffset? RequestedDeliveryDate { get; set; }

    public Guid? ShippingMethodId { get; set; }

    public Guid? DeliveryTimeSlotId { get; set; }

    /// <summary>Snapshot of the chosen delivery time window (e.g. "۹ تا ۱۲").</summary>
    [MaxLength(100)]
    public string? DeliveryTimeSlotTitle { get; set; }

    /// <summary>Unpaid online orders are cancelled automatically after this moment.</summary>
    public DateTimeOffset? PaymentDeadline { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public OrderCancelledBy? CancelledBy { get; set; }

    [MaxLength(500)]
    public string? CancellationReason { get; set; }

    /// <summary>Amount returned to the customer's wallet (cancellation / return).</summary>
    public decimal RefundedAmount { get; set; }

    public DateTimeOffset? RefundedAt { get; set; }

    /// <summary>
    /// Part of a wallet payment that was paid with non-withdrawable (promotional) credit.
    /// Refunds return this part as non-withdrawable credit again.
    /// </summary>
    public decimal WalletBonusAmount { get; set; }

    /// <summary>Non-withdrawable part of <see cref="RefundedAmount"/>.</summary>
    public decimal RefundedBonusAmount { get; set; }

    /// <summary>Serial sales invoice number (e.g. INV-1405-000123), issued once the order is paid.</summary>
    [MaxLength(30)]
    public string? InvoiceNumber { get; set; }

    public DateTimeOffset? InvoiceIssuedAt { get; set; }

    [Required]
    [MaxLength(250)]
    public string CustomerName { get; set; } = null!;

    [Required]
    [MaxLength(20)]
    public string CustomerPhone { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string ShippingProvince { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string ShippingCity { get; set; } = null!;

    [Required]
    [MaxLength(500)]
    public string ShippingAddress { get; set; } = null!;

    [MaxLength(50)]
    public string? ShippingBuildingNumber { get; set; }

    [MaxLength(50)]
    public string? ShippingUnit { get; set; }

    [Required]
    [MaxLength(10)]
    public string ShippingPostalCode { get; set; } = null!;

    [Required]
    [MaxLength(20)]
    public string ShippingPhone { get; set; } = null!;

    [MaxLength(1000)]
    public string? CustomerNote { get; set; }

    public DateTimeOffset? PaidAt { get; set; }

    /// <summary>Moment the order was delivered (return / feedback windows start here).</summary>
    public DateTimeOffset? DeliveredAt { get; set; }

    /// <summary>
    /// زمان ورود سفارش به مرحلهٔ «در حال آماده‌سازی» (یعنی خرید از تأمین‌کننده ثبت شد).
    /// کاربرد: محاسبهٔ زمان‌بندی (SLA) مرحلهٔ «آماده‌سازی تا ارسال» در <c>OrderSla</c>.
    /// </summary>
    public DateTimeOffset? ProcessingAt { get; set; }

    /// <summary>
    /// زمان ارسال سفارش (ثبت کد رهگیری یا تغییر وضعیت به «ارسال‌شده»).
    /// کاربرد: محاسبهٔ بازهٔ تحویل تخمینی برای مشتری و هشدار تأخیر در تأیید تحویل.
    /// </summary>
    public DateTimeOffset? ShippedAt { get; set; }

    /// <summary>
    /// آخرین مرحلهٔ زمان‌بندی که برایش هشدار تأخیر به کارکنان فرستاده شده (عدد <c>OrderSlaStage</c>).
    /// مثال: اگر ۱ باشد یعنی هشدار «ثبت خرید در تأمین‌کننده» قبلاً رفته و دوباره تکرار نمی‌شود.
    /// </summary>
    public int SlaAlertedStage { get; set; }

    /// <summary>
    /// توکن تصادفی لینک پرداخت سفارشی که کارمند در پنل ثبت کرده است (مثلاً <c>/pay/Xk29...</c>).
    /// برای سفارش‌های معمولی سایت خالی است. مشتری با همین لینک بدون ورود هم می‌تواند پرداخت کند.
    /// </summary>
    [MaxLength(64)]
    public string? PaymentToken { get; set; }

    /// <summary>شناسهٔ کارمندی که سفارش را از پنل ثبت کرده است (برای سفارش‌های سایت خالی است).</summary>
    public Guid? CreatedByStaffId { get; set; }

    /// <summary>نام کارمند ثبت‌کننده (اسنپ‌شات، برای نمایش در جزئیات سفارش).</summary>
    [MaxLength(150)]
    public string? CreatedByStaffName { get; set; }

    /// <summary>Snapshot of the address location picked on the map.</summary>
    public decimal? ShippingLatitude { get; set; }

    public decimal? ShippingLongitude { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public List<OrderItem> Items { get; set; } = [];
}

public enum OrderStatus
{
    None = 0,
    Pending = 1,
    Confirmed = 2,
    Processing = 3,
    Shipped = 4,
    Delivered = 5,
    Cancelled = 6,
    Returned = 7,
}

public enum OrderCancelledBy
{
    None = 0,
    Customer = 1,
    Admin = 2,
    System = 3,
}

public enum PaymentMethod
{
    None = 0,
    OnlineGateway = 1,
    CashOnDelivery = 2,
    BankTransfer = 3,
    Wallet = 4,
}

public enum PaymentStatus
{
    None = 0,
    Pending = 1,
    Paid = 2,
    Failed = 3,
    Refunded = 4,
}
