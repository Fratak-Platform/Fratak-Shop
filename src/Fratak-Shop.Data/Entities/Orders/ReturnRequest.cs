using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Orders;

/// <summary>
/// Online return request of a delivered order (some or all items). Refunds go to the
/// wallet with the same withdrawable / promotional split as the payment.
/// </summary>
public class ReturnRequest : BaseEntity
{
    /// <summary>Public number (RT-XXXXXXX).</summary>
    [Required]
    [MaxLength(20)]
    public string RequestNumber { get; set; } = null!;

    public Guid OrderId { get; set; }

    public Guid UserId { get; set; }

    public Guid? ReasonId { get; set; }

    /// <summary>Snapshot of the reason title.</summary>
    [Required]
    [MaxLength(150)]
    public string ReasonTitle { get; set; } = null!;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public ReturnStatus Status { get; set; } = ReturnStatus.Pending;

    /// <summary>Refund the customer would get for the requested items (net of discounts).</summary>
    public decimal RequestedAmount { get; set; }

    public decimal? ApprovedAmount { get; set; }

    public decimal RefundedAmount { get; set; }

    /// <summary>Non-withdrawable part of <see cref="RefundedAmount"/>.</summary>
    public decimal RefundedBonusAmount { get; set; }

    [MaxLength(1000)]
    public string? AdminNote { get; set; }

    /// <summary>Courier / post tracking code of the returned parcel (entered by the customer).</summary>
    [MaxLength(100)]
    public string? ReturnTrackingCode { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public DateTimeOffset? ReceivedAt { get; set; }

    public DateTimeOffset? RefundedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    [ForeignKey(nameof(OrderId))]
    public Order Order { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    [ForeignKey(nameof(ReasonId))]
    public ReturnReason? Reason { get; set; }

    public List<ReturnRequestItem> Items { get; set; } = [];

    public List<ReturnRequestImage> Images { get; set; } = [];
}

public class ReturnRequestItem : BaseEntity
{
    public Guid ReturnRequestId { get; set; }

    public Guid OrderItemId { get; set; }

    public int Quantity { get; set; }

    /// <summary>Refund per unit (sale price minus its share of order discounts).</summary>
    public decimal UnitRefundAmount { get; set; }

    [ForeignKey(nameof(ReturnRequestId))]
    public ReturnRequest ReturnRequest { get; set; } = null!;

    [ForeignKey(nameof(OrderItemId))]
    public OrderItem OrderItem { get; set; } = null!;
}

public class ReturnRequestImage : BaseEntity
{
    public Guid ReturnRequestId { get; set; }

    [Required]
    [MaxLength(100)]
    public string FileName { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string ContentType { get; set; } = null!;

    public int SizeBytes { get; set; }

    [ForeignKey(nameof(ReturnRequestId))]
    public ReturnRequest ReturnRequest { get; set; } = null!;
}

/// <summary>Selectable return reasons (managed by the admin).</summary>
public class ReturnReason : BaseEntity
{
    [Required]
    [MaxLength(150)]
    public string Title { get; set; } = null!;

    [MaxLength(300)]
    public string? Hint { get; set; }

    /// <summary>The customer must attach at least one photo (e.g. damaged item).</summary>
    public bool RequiresImage { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

public enum ReturnStatus
{
    None = 0,
    Pending = 1,     // در انتظار بررسی
    Approved = 2,    // تایید شده – ارسال کالا
    Rejected = 3,    // رد شده
    Received = 4,    // کالا دریافت شد
    Refunded = 5,    // وجه برگشت داده شد
    Cancelled = 6,   // لغو توسط مشتری
}
