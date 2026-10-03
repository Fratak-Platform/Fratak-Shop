using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.DTOs.Shipping.Query;

public class ShippingMethodDto : BaseDto
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public decimal Cost { get; set; }
    public decimal? FreeShippingThreshold { get; set; }
    public int? EstimatedDeliveryDays { get; set; }
    public string? RestrictedToCity { get; set; }
    public string? Icon { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }

    /// <summary>هزینه نهایی برای مبلغ سفارش مشخص (شامل ارسال رایگان).</summary>
    public decimal CalculateCost(decimal orderSubtotal) =>
        FreeShippingThreshold.HasValue && orderSubtotal >= FreeShippingThreshold.Value ? 0 : Cost;

    public string DeliveryTitle => EstimatedDeliveryDays.HasValue
        ? $"تحویل حدود {PriceHelper.ToPersianDigits(EstimatedDeliveryDays.Value.ToString())} روز کاری"
        : "تحویل پس از پردازش سفارش";
}
