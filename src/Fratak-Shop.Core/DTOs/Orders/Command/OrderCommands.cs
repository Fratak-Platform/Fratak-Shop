using Fratak_Shop.Data.Entities.Orders;

namespace Fratak_Shop.Core.DTOs.Orders.Command;

/// <summary>
/// اطلاعات فرم تسویه حساب. آدرس و مشخصات مشتری در سفارش به‌صورت Snapshot ذخیره
/// می‌شود تا تاریخچه سفارش‌ها به تغییرات بعدی وابسته نباشد.
/// </summary>
public class CheckoutCommand
{
    public Guid? UserId { get; set; }
    public string? SessionKey { get; set; }

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
    public DateTimeOffset? RequestedDeliveryDate { get; set; }

    public Guid? ShippingMethodId { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.OnlineGateway;
    public string? CouponCode { get; set; }

    /// <summary>ذخیره آدرس در پروفایل کاربر (در صورت ورود به حساب).</summary>
    public bool SaveAddress { get; set; }

    public string? AddressTitle { get; set; }
}

public class ChangeOrderStatusCommand
{
    public Guid OrderId { get; set; }
    public OrderStatus Status { get; set; }
    public string? Note { get; set; }
    public bool IsCustomerVisible { get; set; } = true;
    public Guid? ChangedByUserId { get; set; }
    /// <summary>در صورت true، کسر/افزایش موجودی انبار با تغییر وضعیت انجام می‌شود.</summary>
    public bool AdjustStock { get; set; } = true;
}

public class SetTrackingCommand
{
    public Guid OrderId { get; set; }
    public string? TrackingCode { get; set; }
    public string? TrackingUrl { get; set; }
    public string? Note { get; set; }
    public Guid? ChangedByUserId { get; set; }
}
