using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Orders;

namespace Fratak_Shop.Core.DTOs.Orders.Query;

/// <summary>
/// سطر لیست سفارش‌ها (پنل مدیریت و «سفارش‌های من»).
/// </summary>
public class OrderFilterData : BaseDto
{
    public Guid? UserId { get; set; }
    public string OrderNumber { get; set; } = null!;

    public OrderStatus Status { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus PaymentStatus { get; set; }

    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ShippingAmount { get; set; }
    public decimal PayableAmount { get; set; }

    public string CustomerName { get; set; } = null!;
    public string CustomerPhone { get; set; } = null!;

    public string? TrackingCode { get; set; }
    public int ItemCount { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
}

/// <summary>
/// پارامترهای فیلتر و صفحه‌بندی لیست سفارش‌ها.
/// </summary>
public class OrderFilterParams : BaseFilterParam
{
    /// <summary>جستجو در شماره سفارش، نام مشتری و شماره تماس.</summary>
    public string? Search { get; set; }

    public OrderStatus? Status { get; set; }

    public PaymentStatus? PaymentStatus { get; set; }

    public Guid? UserId { get; set; }

    public DateTimeOffset? FromDate { get; set; }

    public DateTimeOffset? ToDate { get; set; }
}

public class OrderFilterResult : BaseFilter<OrderFilterData, OrderFilterParams>;
