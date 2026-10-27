using Fratak_Shop.Core.DTOs.Settings;

namespace Fratak_Shop.Core.Services.Pricing;

/// <summary>
/// هزینه‌ها و هدف‌های سود برای «پیشنهاد قیمت فروش» (از تنظیمات فروشگاه، تب «پیشنهاد قیمت فروش»).
/// <para>هزینه‌های ثابت (تومان) به ازای هر عدد کالا جمع می‌شوند؛ هزینه‌های درصدی از «قیمت فروش» کم می‌شوند
/// (کارمزد درگاه از مبلغ پرداختی مشتری کسر می‌شود، نه از قیمت خرید).</para>
/// <para>کجا استفاده می‌شود: <see cref="PriceAdvisor.Suggest"/> و دکمهٔ «پیشنهاد قیمت فروش» در فرم محصول پنل مدیریت.</para>
/// </summary>
/// <param name="PackagingCost">بسته‌بندی هر سفارش (تومان). مثال: ۱۵٬۰۰۰.</param>
/// <param name="ShippingCost">سهم ارسالی که فروشگاه می‌پردازد (تومان)؛ اگر مشتری هزینهٔ ارسال را می‌دهد صفر بگذارید.</param>
/// <param name="OtherCostAmount">سایر هزینه‌های ثابت هر کالا (تومان)، مثل سهم تبلیغات یا انبارداری.</param>
/// <param name="GatewayFeePercent">کارمزد درگاه پرداخت (درصد از مبلغ فروش). مثال: ۱.</param>
/// <param name="OtherCostPercent">سایر هزینه‌های درصدی از فروش (مثلاً پورسانت بازاریاب).</param>
/// <param name="TaxPercent">مالیات/عوارضی که از فروش کم می‌شود (درصد).</param>
/// <param name="TargetMarginPercent">سود هدف (درصد روی کل هزینه). مثال: ۳۰ ← هزینهٔ کل ۱۰۰ هزار = سود ۳۰ هزار.</param>
/// <param name="MinMarginPercent">کمترین سود قابل قبول (درصد روی کل هزینه)؛ پیشنهاد هیچ‌وقت پایین‌تر از آن نمی‌رود.</param>
/// <param name="UndercutPercent">چند درصد ارزان‌تر از ارزان‌ترین رقیب معتبر. مثال: ۲ ← رقیب ۳۰۰ هزار = ۲۹۴ هزار.</param>
/// <param name="Rounding">گرد کردن قیمت (تومان). مثال: ۱۰۰۰.</param>
public sealed record PricingCosts(
    decimal PackagingCost,
    decimal ShippingCost,
    decimal OtherCostAmount,
    decimal GatewayFeePercent,
    decimal OtherCostPercent,
    decimal TaxPercent,
    int TargetMarginPercent,
    int MinMarginPercent,
    decimal UndercutPercent,
    decimal Rounding)
{
    /// <summary>خواندن از تنظیمات فروشگاه.</summary>
    public static PricingCosts From(StoreSettingDto s) => new(
        s.PricingPackagingCost, s.PricingShippingCost, s.PricingOtherCostAmount,
        s.PricingGatewayFeePercent, s.PricingOtherCostPercent, s.PricingTaxPercent,
        s.PricingTargetMarginPercent, s.PricingMinMarginPercent, s.PricingUndercutPercent, s.PricingRounding);

    /// <summary>جمع درصدهایی که از قیمت فروش کم می‌شوند (حداکثر ۹۰٪ تا فرمول بی‌نهایت نشود).</summary>
    public decimal PercentOfSale => Math.Clamp(GatewayFeePercent + OtherCostPercent + TaxPercent, 0, 90);
}

/// <summary>قیمت یک رقیب برای نمایش و محاسبه. مثال: («دیجی‌کالا»، ۳۰۰٬۰۰۰، «ترب»).</summary>
/// <param name="Shop">نام فروشگاه.</param>
/// <param name="Price">قیمت (تومان).</param>
/// <param name="Source">از کجا خوانده شد (ترب، دیجی‌کالا …).</param>
/// <param name="Url">لینک صفحه (برای بررسی کارمند).</param>
/// <param name="Title">عنوان کالا در آن منبع (برای اینکه کارمند ببیند همان کالاست).</param>
public sealed record CompetitorOffer(string Shop, decimal Price, string Source, string? Url = null, string? Title = null);

/// <summary>
/// نتیجهٔ پیشنهاد قیمت.
/// </summary>
/// <param name="TotalCost">کل هزینهٔ ثابت یک عدد (خرید + بسته‌بندی + ارسال + سایر + هزینهٔ اضافهٔ همین کالا).</param>
/// <param name="BreakEven">قیمت سربه‌سر (بدون سود، با کسر درصدها).</param>
/// <param name="MinPrice">کمترین قیمت مجاز (با کمترین سود).</param>
/// <param name="TargetPrice">قیمت با سود هدف.</param>
/// <param name="Suggested">قیمت پیشنهادی نهایی.</param>
/// <param name="Profit">سود خالص هر عدد با قیمت پیشنهادی (بعد از همهٔ هزینه‌ها).</param>
/// <param name="ProfitPercent">درصد سود روی کل هزینه.</param>
/// <param name="Strategy">کد راهبرد: target (بدون رقیب)، undercut (زیر قیمت رقبا با سود خوب)، floor (رقبا خیلی ارزان‌اند؛ کف سود).</param>
/// <param name="Reason">توضیح فارسی برای کارمند.</param>
/// <param name="Lowest">ارزان‌ترین قیمت معتبر رقبا.</param>
/// <param name="Median">میانهٔ قیمت رقبا.</param>
/// <param name="Offers">قیمت‌های معتبر (پس از حذف قیمت‌های پرت)، از ارزان به گران.</param>
/// <param name="Ignored">قیمت‌های پرت کنارگذاشته (احتمالاً لوازم جانبی یا کالای دیگر).</param>
public sealed record PriceSuggestion(
    decimal TotalCost,
    decimal BreakEven,
    decimal MinPrice,
    decimal TargetPrice,
    decimal Suggested,
    decimal Profit,
    decimal ProfitPercent,
    string Strategy,
    string Reason,
    decimal? Lowest,
    decimal? Median,
    IReadOnlyList<CompetitorOffer> Offers,
    IReadOnlyList<CompetitorOffer> Ignored);

/// <summary>
/// محاسبهٔ خالص قیمت فروش پیشنهادی از روی قیمت خرید، همهٔ هزینه‌ها و قیمت رقبا (بدون دیتابیس و اینترنت؛ کاملاً تست‌پذیر).
/// <para>فرمول: سود = فروش × (۱ − درصدها) − هزینهٔ ثابت؛ پس قیمت با سود m برابر است با
/// هزینهٔ ثابت × (۱ + m) ÷ (۱ − درصدها).</para>
/// <para>راهبرد: کمی ارزان‌تر از ارزان‌ترین رقیب معتبر، به شرطی که سود از «کمترین سود» کمتر نشود.
/// مثال: خرید ۱۰۰ هزار، رقبا ۳۰۰/۲۸۰/۲۷۰ هزار، ارزان‌تر ۲٪ ← ۲۶۴ هزار (سود حدود ۱۶۰٪).</para>
/// </summary>
public static class PriceAdvisor
{
    /// <summary>
    /// قیمت پیشنهادی.
    /// </summary>
    /// <param name="purchasePrice">قیمت خرید هر عدد (تومان).</param>
    /// <param name="extraCost">هزینهٔ اضافهٔ مخصوص همین کالا که در دیالوگ وارد می‌شود (ذخیره نمی‌شود). مثال: بیمهٔ ارسال کالای شکستنی.</param>
    /// <param name="costs">هزینه‌ها و هدف‌ها از تنظیمات.</param>
    /// <param name="competitors">قیمت رقبا (ممکن است خالی باشد).</param>
    public static PriceSuggestion Suggest(decimal purchasePrice, decimal extraCost, PricingCosts costs, IReadOnlyList<CompetitorOffer> competitors)
    {
        var fixedCost = Math.Max(0, purchasePrice) + Math.Max(0, costs.PackagingCost) + Math.Max(0, costs.ShippingCost)
                        + Math.Max(0, costs.OtherCostAmount) + Math.Max(0, extraCost);
        var keep = 1 - costs.PercentOfSale / 100m; // سهمی از فروش که به فروشگاه می‌رسد
        var minMargin = Math.Max(0, costs.MinMarginPercent);
        var target = Math.Max(minMargin, costs.TargetMarginPercent);

        var breakEven = fixedCost / keep;
        var minPrice = RoundUp(fixedCost * (1 + minMargin / 100m) / keep, costs.Rounding);
        var targetPrice = RoundUp(fixedCost * (1 + target / 100m) / keep, costs.Rounding);

        var (offers, ignored) = Clean(competitors);
        decimal? lowest = offers.Count > 0 ? offers[0].Price : null;
        decimal? median = offers.Count > 0 ? Median(offers.Select(o => o.Price).ToList()) : null;

        decimal suggested;
        string strategy, reason;
        if (lowest is null)
        {
            suggested = targetPrice;
            strategy = "target";
            reason = $"قیمت معتبری از رقبا پیدا نشد؛ قیمت با سود هدف {target}٪ پیشنهاد شد.";
        }
        else
        {
            // کمی ارزان‌تر از ارزان‌ترین رقیب؛ به پایین گرد می‌شود تا واقعاً ارزان‌تر بماند.
            var undercut = lowest.Value * (1 - Math.Clamp(costs.UndercutPercent, 0, 50) / 100m);
            var candidate = RoundDown(undercut, costs.Rounding);
            if (candidate >= lowest.Value && costs.Rounding > 1)
                candidate = lowest.Value - costs.Rounding;

            if (candidate >= minPrice)
            {
                suggested = candidate;
                strategy = "undercut";
                reason = candidate >= targetPrice
                    ? $"کمی ارزان‌تر از ارزان‌ترین رقیب ({lowest.Value:N0} تومان) و سود بیشتر از هدف {target}٪."
                    : $"کمی ارزان‌تر از ارزان‌ترین رقیب ({lowest.Value:N0} تومان)؛ سود کمتر از هدف {target}٪ ولی بالاتر از کف {minMargin}٪ است.";
            }
            else
            {
                suggested = minPrice;
                strategy = "floor";
                reason = $"رقبا ارزان‌تر از هزینهٔ ما می‌فروشند ({lowest.Value:N0} تومان)؛ کمتر از کمترین سود {minMargin}٪ پیشنهاد نمی‌شود. " +
                         "قیمت خرید، تأمین‌کننده یا هزینه‌ها را بازبینی کنید.";
            }
        }

        var profit = suggested * keep - fixedCost;
        var profitPercent = fixedCost > 0 ? Math.Round(profit * 100 / fixedCost, 1) : 0;
        return new PriceSuggestion(
            Math.Round(fixedCost), Math.Round(breakEven), minPrice, targetPrice, suggested,
            Math.Round(profit), profitPercent, strategy, reason, lowest, median, offers, ignored);
    }

    /// <summary>
    /// حذف قیمت‌های نامعتبر و پرت: قیمت صفر/منفی، تکراری‌های یک فروشگاه (ارزان‌ترین می‌ماند)، و قیمت‌هایی که از میانه
    /// خیلی دورند (کمتر از ۴۰٪ یا بیشتر از ۲۵۰٪ میانه؛ معمولاً قاب/لوازم جانبی یا مدل دیگر).
    /// <para>مثال: [۳۰۰، ۲۸۰، ۲۷۰، ۴۵] هزار ← ۴۵ کنار می‌رود.</para>
    /// </summary>
    public static (List<CompetitorOffer> Offers, List<CompetitorOffer> Ignored) Clean(IReadOnlyList<CompetitorOffer> competitors)
    {
        var valid = competitors
            .Where(o => o.Price > 0 && !string.IsNullOrWhiteSpace(o.Shop))
            .GroupBy(o => ShopKey(o.Shop))
            .Select(g => g.OrderBy(o => o.Price).First())
            .OrderBy(o => o.Price)
            .ToList();
        if (valid.Count < 3)
            return (valid, []); // با کمتر از ۳ قیمت، «پرت» قابل تشخیص نیست.

        var median = Median(valid.Select(o => o.Price).ToList());
        var kept = valid.Where(o => o.Price >= median * 0.4m && o.Price <= median * 2.5m).ToList();
        var ignored = valid.Except(kept).ToList();
        return (kept, ignored);
    }

    /// <summary>میانهٔ یک فهرست مرتب‌نشده. مثال: [۳، ۱، ۲] ← ۲؛ [۱، ۲، ۳، ۴] ← ۲٫۵.</summary>
    public static decimal Median(List<decimal> values)
    {
        if (values.Count == 0)
            return 0;
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>گرد کردن به بالا به مضرب step. مثال: (۲۶۳٬۲۰۰، ۱۰۰۰) ← ۲۶۴٬۰۰۰.</summary>
    public static decimal RoundUp(decimal value, decimal step)
        => step <= 1 ? Math.Ceiling(value) : Math.Ceiling(value / step) * step;

    /// <summary>گرد کردن به پایین به مضرب step. مثال: (۲۶۴٬۶۰۰، ۱۰۰۰) ← ۲۶۴٬۰۰۰.</summary>
    public static decimal RoundDown(decimal value, decimal step)
        => step <= 1 ? Math.Floor(value) : Math.Floor(value / step) * step;

    // «دیجی‌کالا» و «ديجي كالا» یک فروشگاه‌اند.
    private static string ShopKey(string shop)
        => new string(Seo.Engine.PersianText.Normalize(shop).Where(char.IsLetterOrDigit).ToArray());
}
