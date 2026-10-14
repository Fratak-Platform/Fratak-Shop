using Fratak_Shop.Data.Entities.Orders;

namespace Fratak_Shop.Core.DTOs.Orders.Command;

/// <summary>
/// یک قلم سفارش در فرم ثبت سفارش.
/// </summary>
public class OrderItemRequest
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; } = 1;
}

/// <summary>
/// دستور ثبت سفارش. قیمت‌ها و مشخصات محصولات از خود محصول خوانده می‌شود و
/// مبالغ سفارش (جمع کل، تخفیف، قابل پرداخت) در سرویس محاسبه می‌شود.
/// </summary>
public class CreateOrderCommand
{
    public Guid? UserId { get; set; }

    public PaymentMethod PaymentMethod { get; set; }
    public string? ShippingMethod { get; set; }
    public DateTimeOffset? RequestedDeliveryDate { get; set; }

    /// <summary>تخفیف دستی سفارش (جدا از تخفیف قیمت هر محصول).</summary>
    public decimal DiscountAmount { get; set; }

    public decimal ShippingAmount { get; set; }

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

    public List<OrderItemRequest> Items { get; set; } = [];
}

/// <summary>
/// دستور ویرایش اطلاعات سفارش. اقلام سفارش (OrderItem) از طریق سرویس
/// <c>IOrderItemService</c> مدیریت می‌شوند و اسنپ‌شات خرید دست‌نخورده می‌ماند.
/// </summary>
public class EditOrderCommand
{
    public Guid Id { get; set; }

    public OrderStatus Status { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus PaymentStatus { get; set; }

    public string? TransactionId { get; set; }
    public string? TrackingCode { get; set; }

    public string? ShippingMethod { get; set; }
    public DateTimeOffset? RequestedDeliveryDate { get; set; }
    public DateTimeOffset? PaidAt { get; set; }

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
}
