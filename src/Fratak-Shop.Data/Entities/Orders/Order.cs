using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Payments;
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

    /// <summary>
    /// Courier tracking URL built from <see cref="TrackingCode"/> and the shipping
    /// company template, shown on the order tracking page.
    /// </summary>
    [MaxLength(1000)]
    public string? TrackingUrl { get; set; }

    public decimal SubtotalAmount { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal ShippingAmount { get; set; }

    public decimal PayableAmount { get; set; }

    /// <summary>
    /// Snapshot of the selected shipping option name at checkout. The related
    /// shipping method can be removed later without changing order history.
    /// </summary>
    [MaxLength(100)]
    public string? ShippingMethodName { get; set; }

    public DateTimeOffset? RequestedDeliveryDate { get; set; }

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

    /// <summary>
    /// Coupon applied to this order (kept for campaign reporting).
    /// </summary>
    public Guid? CouponId { get; set; }

    /// <summary>
    /// Shipping option chosen by the customer.
    /// </summary>
    public Guid? ShippingMethodId { get; set; }

    /// <summary>
    /// Internal note of the staff, never shown to the customer.
    /// </summary>
    [MaxLength(1000)]
    public string? AdminNote { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    [MaxLength(500)]
    public string? CancelReason { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [ForeignKey(nameof(CouponId))]
    public Coupon? Coupon { get; set; }

    [ForeignKey(nameof(ShippingMethodId))]
    public ShippingMethod? ShippingMethod { get; set; }

    public List<OrderItem> Items { get; set; } = [];

    public List<OrderStatusHistory> StatusHistory { get; set; } = [];

    public List<PaymentTransaction> PaymentTransactions { get; set; } = [];

    /// <summary>
    /// Card-to-card payment of this order, when the order was paid that way.
    /// </summary>
    public BankTransferPayment? BankTransferPayment { get; set; }
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
