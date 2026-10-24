using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Orders;

namespace Fratak_Shop.Core.DTOs.Orders.Query;

/// <summary>
/// جزئیات کامل سفارش شامل اسنپ‌شات آدرس و اقلام خریداری‌شده.
/// </summary>
public class OrderDto : BaseDto
{
    public Guid? UserId { get; set; }
    public string OrderNumber { get; set; } = null!;

    public OrderStatus Status { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus PaymentStatus { get; set; }

    public string? TransactionId { get; set; }
    public string? TrackingCode { get; set; }

    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ShippingAmount { get; set; }
    public decimal PayableAmount { get; set; }

    public string? ShippingMethod { get; set; }
    public Guid? ShippingMethodId { get; set; }
    public DateTimeOffset? RequestedDeliveryDate { get; set; }
    public Guid? DeliveryTimeSlotId { get; set; }
    public string? DeliveryTimeSlotTitle { get; set; }

    /// <summary>Unpaid online orders are cancelled automatically after this moment.</summary>
    public DateTimeOffset? PaymentDeadline { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }
    public OrderCancelledBy? CancelledBy { get; set; }
    public string? CancellationReason { get; set; }
    public decimal RefundedAmount { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }

    public string CustomerName { get; set; } = null!;
    public string CustomerPhone { get; set; } = null!;

    public string ShippingProvince { get; set; } = null!;
    public string ShippingCity { get; set; } = null!;
    public string ShippingAddress { get; set; } = null!;
    public string? ShippingBuildingNumber { get; set; }
    public string? ShippingUnit { get; set; }
    public string ShippingPostalCode { get; set; } = null!;
    public string ShippingPhone { get; set; } = null!;

    public string? CustomerNote { get; set; }
    public DateTimeOffset? PaidAt { get; set; }

    public List<OrderItemDto> Items { get; set; } = [];

    /// <summary>The order can still be paid online (pending, unpaid and not expired).</summary>
    public bool IsAwaitingPayment(DateTimeOffset now) =>
        OrderRules.IsPayable(Status, PaymentMethod, PaymentStatus, PaymentDeadline, now);
}

public class OrderItemDto : BaseDto
{
    public Guid OrderId { get; set; }
    public Guid? ProductId { get; set; }

    public string ProductName { get; set; } = null!;
    public string SKU { get; set; } = null!;

    /// <summary>اسلاگ محصول؛ اگر محصول حذف شده باشد خالی است.</summary>
    public string? ProductSlug { get; set; }

    /// <summary>تصویر اصلی محصول؛ اگر محصول حذف شده باشد خالی است.</summary>
    public string? ProductImageName { get; set; }

    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public int Quantity { get; set; }
    public decimal TotalAmount { get; set; }
}
