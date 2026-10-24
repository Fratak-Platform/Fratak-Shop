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
