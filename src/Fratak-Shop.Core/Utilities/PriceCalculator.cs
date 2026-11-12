using Fratak_Shop.Data.Entities.Products;

namespace Fratak_Shop.Core.Utilities;

/// <summary>
/// محاسبات قیمت محصول؛ تنها مرجع تشخیص «قیمت نهایی با احتساب تخفیف فعال» تا
/// سبد خرید، سفارش و صفحه محصول همیشه یک عدد را نشان دهند.
/// تخفیف زمانی فعال است که مبلغ تخفیف از قیمت اصلی کمتر و تاریخ جاری داخل بازه تخفیف باشد.
/// </summary>
public static class PriceCalculator
{
    public static bool HasActiveDiscount(this Product product, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(product);

        // قیمت تخفیفی منفی (خطای ورود داده یا درون‌ریزی) هرگز اعمال نمی‌شود تا مبلغ سفارش منفی نشود.
        if (!product.DiscountPrice.HasValue || product.DiscountPrice.Value < 0 || product.DiscountPrice.Value >= product.Price)
            return false;

        var moment = now ?? DateTimeOffset.UtcNow;

        if (product.DiscountStartAt.HasValue && product.DiscountStartAt.Value > moment)
            return false;

        if (product.DiscountEndAt.HasValue && product.DiscountEndAt.Value < moment)
            return false;

        return true;
    }

    public static decimal GetFinalPrice(this Product product, DateTimeOffset? now = null) =>
        product.HasActiveDiscount(now) ? product.DiscountPrice!.Value : product.Price;

    public static decimal GetDiscountAmount(this Product product, DateTimeOffset? now = null) =>
        product.Price - product.GetFinalPrice(now);
}
