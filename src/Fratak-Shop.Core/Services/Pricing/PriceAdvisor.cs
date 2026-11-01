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
/// تنظیمات داوری پیشنهادهای رقبا (مرحلهٔ پانزدهم). همه اختیاری‌اند.
/// </summary>
/// <param name="Query">نام کالای ما؛ اگر باشد عنوان هر پیشنهاد با آن سنجیده می‌شود (کالای دیگر/لوازم جانبی/مدل دیگر کنار می‌رود).</param>
/// <param name="Excluded">شناسهٔ پیشنهادهایی که کارمند با دکمهٔ «حذف» فقط برای <b>همین</b> محاسبه کنار گذاشت (ذخیره نمی‌شود).</param>
/// <param name="Included">شناسهٔ پیشنهادهایی که سیستم کنار گذاشته بود ولی کارمند گفت «حساب کن».</param>
/// <param name="OwnShops">نام یا دامنهٔ فروشگاه خودمان (مثل «فراتک» یا «faratek.ir»)؛ قیمت خودمان رقیب حساب نمی‌شود.</param>
public sealed record OfferReviewOptions(
    string? Query = null,
    IReadOnlyCollection<string>? Excluded = null,
    IReadOnlyCollection<string>? Included = null,
    IReadOnlyCollection<string>? OwnShops = null);

/// <summary>کد داوری یک پیشنهاد (برای رنگ و فیلتر در صفحه؛ متن فارسی در <see cref="ReviewedOffer.Reason"/>).</summary>
public static class OfferVerdict
{
    /// <summary>معتبر و در محاسبه حساب شد.</summary>
    public const string Accepted = "ok";

    /// <summary>سیستم کنار گذاشته بود، کارمند گفت حساب کن.</summary>
    public const string Forced = "forced";

    /// <summary>فروشگاه خودمان.</summary>
    public const string Own = "own";

    /// <summary>کارمند برای همین محاسبه حذفش کرد.</summary>
    public const string Excluded = "excluded";

    /// <summary>عنوان شبیه کالای ما نیست.</summary>
    public const string Mismatch = "mismatch";

    /// <summary>مدل/کد کالا فرق دارد.</summary>
    public const string Model = "model";

    /// <summary>لوازم جانبی است (قاب، کاور …).</summary>
    public const string Accessory = "accessory";

    /// <summary>قیمت تکراری همان فروشگاه (ارزان‌ترینش حساب شد).</summary>
    public const string Duplicate = "duplicate";

    /// <summary>خیلی ارزان‌تر از بقیه (احتمالاً کالای دیگر یا قیمت اشتباه).</summary>
    public const string TooLow = "low";

    /// <summary>خیلی گران‌تر از بقیه.</summary>
    public const string TooHigh = "high";
}

/// <summary>
/// یک پیشنهاد رقیب بعد از داوری سیستم.
/// </summary>
/// <param name="Id">شناسهٔ ثابت (۱۲ نویسه) برای دکمه‌های «حذف/حساب کن». مثال: «a1b2c3d4e5f6».</param>
/// <param name="Offer">خود پیشنهاد.</param>
/// <param name="Accepted">در محاسبهٔ قیمت حساب شد؟</param>
/// <param name="Code">کد داوری (<see cref="OfferVerdict"/>).</param>
/// <param name="Reason">توضیح فارسی. مثال: «مدل/کد کالای ما (مثل T15) در عنوان نیست؛ احتمالاً مدل دیگری است.»</param>
/// <param name="Match">شباهت عنوان با نام کالای ما (۰ تا ۱۰۰)؛ null یعنی عنوان یا نام کالا نبود.</param>
public sealed record ReviewedOffer(string Id, CompetitorOffer Offer, bool Accepted, string Code, string Reason, int? Match);

/// <summary>
/// جایگاه ما در ترب (مرحلهٔ پانزدهم؛ ترب شریک مهم فروشگاه است و قیمت ما باید از همهٔ فروشندگان ترب کمتر باشد).
/// </summary>
/// <param name="Sellers">تعداد فروشندگان معتبر همین کالا در ترب.</param>
/// <param name="Lowest">ارزان‌ترین فروشندهٔ معتبر ترب (تومان).</param>
/// <param name="Rank">رتبهٔ قیمت پیشنهادی ما بین فروشندگان ترب (۱ = ارزان‌ترین).</param>
/// <param name="CheaperElsewhere">تعداد فروشگاه‌های بیرون از ترب که از قیمت پیشنهادی ما ارزان‌ترند.</param>
public sealed record TorobStanding(int Sellers, decimal? Lowest, int Rank, int CheaperElsewhere);

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
/// <param name="Lowest">ارزان‌ترین قیمت معتبر رقبا (همهٔ منابع).</param>
/// <param name="Median">میانهٔ قیمت رقبا.</param>
/// <param name="Offers">قیمت‌های معتبر (پس از داوری)، از ارزان به گران.</param>
/// <param name="Ignored">قیمت‌های کنارگذاشته (هر دلیلی)، از ارزان به گران.</param>
/// <param name="Reviewed">مرحلهٔ پانزدهم: همهٔ پیشنهادها با داوری و دلیل (برای جدول صفحه و دکمه‌های حذف/حساب کن).</param>
/// <param name="Torob">مرحلهٔ پانزدهم: جایگاه ما در ترب؛ null یعنی فروشندهٔ معتبری از ترب نبود.</param>
/// <param name="Reference">قیمتی که زیرش رفتیم (ارزان‌ترین فروشندهٔ ترب، یا اگر ترب نبود ارزان‌ترین رقیب معتبر).</param>
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
    IReadOnlyList<CompetitorOffer> Ignored,
    IReadOnlyList<ReviewedOffer>? Reviewed = null,
    TorobStanding? Torob = null,
    decimal? Reference = null);

/// <summary>
/// محاسبهٔ خالص قیمت فروش پیشنهادی از روی قیمت خرید، همهٔ هزینه‌ها و قیمت رقبا (بدون دیتابیس و اینترنت؛ کاملاً تست‌پذیر).
/// <para>فرمول: سود = فروش × (۱ − درصدها) − هزینهٔ ثابت؛ پس قیمت با سود m برابر است با
/// هزینهٔ ثابت × (۱ + m) ÷ (۱ − درصدها).</para>
/// <para>راهبرد (مرحلهٔ پانزدهم): کمی ارزان‌تر از <b>ارزان‌ترین فروشندهٔ معتبر ترب</b> (تا در ترب از همه ارزان‌تر باشیم)؛ اگر ترب
/// فروشندهٔ معتبری نداشت، کمی ارزان‌تر از ارزان‌ترین رقیب معتبر بقیهٔ سایت‌ها؛ و هیچ‌وقت پایین‌تر از «کمترین سود».
/// مثال: خرید ۱۰۰ هزار، فروشندگان ترب ۳۰۰/۲۸۰/۲۷۰ هزار، ارزان‌تر ۲٪ ← ۲۶۴ هزار (سود حدود ۱۶۰٪).</para>
/// <para>داوری پیشنهادها (<see cref="Review"/>): فروشگاه خودمان، حذف‌شده توسط کارمند، عنوان نامشابه/مدل دیگر/لوازم جانبی،
/// تکراری‌های یک فروشگاه و قیمت‌های پرت کنار می‌روند و دلیل هر کدام به فارسی نمایش داده می‌شود.</para>
/// </summary>
public static class PriceAdvisor
{
    /// <summary>کمتر از این ضریب میانه = «خیلی ارزان» (معمولاً لوازم جانبی، کالای دست‌دوم یا قیمت اشتباه).</summary>
    public const decimal LowOutlier = 0.4m;

    /// <summary>بیشتر از این ضریب میانه = «خیلی گران» (معمولاً بسته/مدل دیگر).</summary>
    public const decimal HighOutlier = 2.5m;

    /// <summary>
    /// قیمت پیشنهادی.
    /// </summary>
    /// <param name="purchasePrice">قیمت خرید هر عدد (تومان).</param>
    /// <param name="extraCost">هزینهٔ اضافهٔ مخصوص همین کالا که در دیالوگ وارد می‌شود (ذخیره نمی‌شود). مثال: بیمهٔ ارسال کالای شکستنی.</param>
    /// <param name="costs">هزینه‌ها و هدف‌ها از تنظیمات.</param>
    /// <param name="competitors">قیمت رقبا (ممکن است خالی باشد).</param>
    /// <param name="review">داوری: نام کالا، حذف/حساب‌کردن‌های کارمند و نام فروشگاه خودمان (اختیاری).</param>
    public static PriceSuggestion Suggest(decimal purchasePrice, decimal extraCost, PricingCosts costs, IReadOnlyList<CompetitorOffer> competitors,
        OfferReviewOptions? review = null)
    {
        var fixedCost = Math.Max(0, purchasePrice) + Math.Max(0, costs.PackagingCost) + Math.Max(0, costs.ShippingCost)
                        + Math.Max(0, costs.OtherCostAmount) + Math.Max(0, extraCost);
        var keep = 1 - costs.PercentOfSale / 100m; // سهمی از فروش که به فروشگاه می‌رسد
        var minMargin = Math.Max(0, costs.MinMarginPercent);
        var target = Math.Max(minMargin, costs.TargetMarginPercent);

        var breakEven = fixedCost / keep;
        var minPrice = RoundUp(fixedCost * (1 + minMargin / 100m) / keep, costs.Rounding);
        var targetPrice = RoundUp(fixedCost * (1 + target / 100m) / keep, costs.Rounding);

        var reviewed = Review(competitors ?? Array.Empty<CompetitorOffer>(), review);
        var offers = reviewed.Where(r => r.Accepted).Select(r => r.Offer).OrderBy(o => o.Price).ToList();
        var ignored = reviewed.Where(r => !r.Accepted).Select(r => r.Offer).OrderBy(o => o.Price).ToList();
        var torob = TorobSellers(reviewed);
        decimal? lowest = offers.Count > 0 ? offers[0].Price : null;
        decimal? median = offers.Count > 0 ? Median(offers.Select(o => o.Price).ToList()) : null;

        // مرحلهٔ پانزدهم: مرجع، ارزان‌ترین فروشندهٔ ترب است (شریک اصلی)؛ اگر ترب نبود، ارزان‌ترین رقیب معتبر.
        decimal? reference = torob.Count > 0 ? torob[0].Price : lowest;
        var referenceName = torob.Count > 0 ? "ارزان‌ترین فروشندهٔ ترب" : "ارزان‌ترین رقیب معتبر";

        decimal suggested;
        string strategy, reason;
        if (reference is null)
        {
            suggested = targetPrice;
            strategy = "target";
            reason = $"قیمت معتبری از رقبا پیدا نشد؛ قیمت با سود هدف {target}٪ پیشنهاد شد.";
        }
        else
        {
            // کمی ارزان‌تر از مرجع؛ به پایین گرد می‌شود تا واقعاً ارزان‌تر بماند.
            var undercut = reference.Value * (1 - Math.Clamp(costs.UndercutPercent, 0, 50) / 100m);
            var candidate = RoundDown(undercut, costs.Rounding);
            if (candidate >= reference.Value && costs.Rounding > 1)
                candidate = reference.Value - costs.Rounding;

            if (candidate >= minPrice)
            {
                suggested = candidate;
                strategy = "undercut";
                reason = candidate >= targetPrice
                    ? $"کمی ارزان‌تر از {referenceName} ({reference.Value:N0} تومان) و سود بیشتر از هدف {target}٪."
                    : $"کمی ارزان‌تر از {referenceName} ({reference.Value:N0} تومان)؛ سود کمتر از هدف {target}٪ ولی بالاتر از کف {minMargin}٪ است.";
            }
            else
            {
                suggested = minPrice;
                strategy = "floor";
                reason = $"{referenceName} ارزان‌تر از هزینهٔ ما می‌فروشد ({reference.Value:N0} تومان)؛ کمتر از کمترین سود {minMargin}٪ پیشنهاد نمی‌شود. " +
                         "قیمت خرید، تأمین‌کننده یا هزینه‌ها را بازبینی کنید.";
            }
        }

        TorobStanding? standing = null;
        if (torob.Count > 0)
        {
            var rank = 1 + torob.Count(o => o.Price < suggested);
            var elsewhere = offers.Count(o => o.Source != CompetitorPriceService.Torob && o.Price < suggested);
            standing = new TorobStanding(torob.Count, torob[0].Price, rank, elsewhere);
        }

        var profit = suggested * keep - fixedCost;
        var profitPercent = fixedCost > 0 ? Math.Round(profit * 100 / fixedCost, 1) : 0;
        return new PriceSuggestion(
            Math.Round(fixedCost), Math.Round(breakEven), minPrice, targetPrice, suggested,
            Math.Round(profit), profitPercent, strategy, reason, lowest, median, offers, ignored,
            reviewed, standing, reference);
    }

    /// <summary>
    /// داوری همهٔ پیشنهادها (مرحلهٔ پانزدهم): برای هر کدام «حساب شد/کنار رفت» و دلیل فارسی.
    /// <para>ترتیب قانون‌ها: قیمت صفر یا بدون نام فروشگاه ← اصلاً نمایش داده نمی‌شود؛ فروشگاه خودمان ← کنار؛ حذف کارمند ← کنار؛
    /// «حساب کن» کارمند ← حساب؛ عنوان نامشابه (شباهت کمتر از ۶۰٪) ← کنار با دلیل دقیق (مدل دیگر / لوازم جانبی / نامشابه)؛
    /// چند قیمت از یک فروشگاه ← فقط ارزان‌ترین؛ با دست‌کم ۳ قیمت، کمتر از ۴۰٪ یا بیشتر از ۲۵۰٪ میانه ← پرت.</para>
    /// <para>مثال: [ترب «فروشگاه A» ۲۸۰، ترب «فروشگاه A» ۲۹۰، باسلام «قاب هدفون» ۴۵، سایت X ۲۷۰] با نام «هدفون هایلو T15» ←
    /// A۲۸۰ و X۲۷۰ حساب؛ A۲۹۰ تکراری؛ قاب ← لوازم جانبی.</para>
    /// </summary>
    public static List<ReviewedOffer> Review(IReadOnlyList<CompetitorOffer> competitors, OfferReviewOptions? options = null)
    {
        options ??= new OfferReviewOptions();
        var excluded = new HashSet<string>((options.Excluded ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.Ordinal);
        var included = new HashSet<string>((options.Included ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.Ordinal);
        var ownNames = new HashSet<string>(StringComparer.Ordinal);
        var ownHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var own in options.OwnShops ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(own))
                continue;
            var key = ShopKey(own);
            if (key.Length >= 2)
                ownNames.Add(key);
            if (own.Contains('.'))
                ownHosts.Add(HostOf(own.Contains("://", StringComparison.Ordinal) ? own : "https://" + own.Trim()) ?? own.Trim());
        }
        var query = options.Query?.Trim();

        var result = new List<ReviewedOffer>();
        var pending = new List<(CompetitorOffer Offer, string Id, int? Match)>();
        var forced = new List<(CompetitorOffer Offer, string Id, int? Match)>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var offer in (competitors ?? Array.Empty<CompetitorOffer>())
                     .Where(o => o is not null && o.Price > 0 && !string.IsNullOrWhiteSpace(o.Shop))
                     .OrderBy(o => o.Price))
        {
            var id = OfferId(offer);
            for (var n = 2; !ids.Add(id); n++)
                id = OfferId(offer) + "-" + n;

            TitleMatch? check = !string.IsNullOrEmpty(query) && !string.IsNullOrWhiteSpace(offer.Title)
                ? CompetitorParsers.TitleCheck(query, offer.Title)
                : null;
            int? match = check is null ? null : (int)Math.Round(check.Score * 100);

            var host = HostOf(offer.Url);
            if (ownNames.Contains(ShopKey(offer.Shop)) || (host is not null && ownHosts.Contains(host)))
            {
                result.Add(new ReviewedOffer(id, offer, false, OfferVerdict.Own, "فروشگاه خودمان است؛ با قیمت خودمان رقابت نمی‌کنیم.", match));
                continue;
            }
            if (excluded.Contains(id))
            {
                result.Add(new ReviewedOffer(id, offer, false, OfferVerdict.Excluded, "شما این مورد را برای همین محاسبه کنار گذاشتید (دفعهٔ بعد دوباره بررسی می‌شود).", match));
                continue;
            }
            if (included.Contains(id))
            {
                forced.Add((offer, id, match));
                continue;
            }
            if (check is not null && check.Score < CompetitorParsers.MinScore)
            {
                var (code, why) = check.Accessory
                    ? (OfferVerdict.Accessory, "به نظر لوازم جانبی است (قاب، کاور، محافظ …)، نه خود کالا.")
                    // «مدل دیگر» فقط وقتی بقیهٔ نام می‌خواند (امتیاز پیش از جریمهٔ کد مدل دست‌کم ۵۰٪)؛ مثلاً «هدفون هایلو GT1».
                    // عنوان کاملاً بی‌ربط (مثل «اسپیکر انکر») «کالای دیگر» است.
                    : check.MissingCode && check.Score * 2 >= 0.5
                        ? (OfferVerdict.Model, "مدل/کد کالای ما (مثل عدد یا کد مدل) در عنوان نیست؛ احتمالاً مدل دیگری است.")
                        : (OfferVerdict.Mismatch, $"عنوان فقط {match}٪ شبیه نام کالای ماست؛ احتمالاً کالای دیگری است.");
                result.Add(new ReviewedOffer(id, offer, false, code, why, match));
                continue;
            }
            pending.Add((offer, id, match));
        }

        // چند قیمت از یک فروشگاه: فقط ارزان‌ترین (فهرست از ارزان به گران مرتب است). «دیجی‌کالا» و «ديجي كالا» یکی‌اند.
        var kept = new List<(CompetitorOffer Offer, string Id, int? Match)>();
        var seenShops = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var f in forced)
            seenShops.TryAdd(ShopKey(f.Offer.Shop), f.Offer.Price);
        foreach (var p in pending)
        {
            var key = ShopKey(p.Offer.Shop);
            if (seenShops.TryGetValue(key, out var cheaper))
            {
                result.Add(new ReviewedOffer(p.Id, p.Offer, false, OfferVerdict.Duplicate,
                    $"قیمت دیگری از همین فروشگاه ({cheaper:N0} تومان) ارزان‌تر است؛ از هر فروشگاه فقط ارزان‌ترین حساب می‌شود.", p.Match));
                continue;
            }
            seenShops[key] = p.Offer.Price;
            kept.Add(p);
        }

        // قیمت پرت: با کمتر از ۳ قیمت قابل تشخیص نیست.
        var all = kept.Select(k => k.Offer.Price).Concat(forced.Select(f => f.Offer.Price)).ToList();
        var median = all.Count >= 3 ? Median(all) : 0;
        foreach (var k in kept)
        {
            if (median > 0 && k.Offer.Price < median * LowOutlier)
                result.Add(new ReviewedOffer(k.Id, k.Offer, false, OfferVerdict.TooLow,
                    $"خیلی ارزان‌تر از بقیه (کمتر از {LowOutlier * 100:0}٪ میانهٔ {median:N0} تومان)؛ احتمالاً کالای دیگر، دست‌دوم یا قیمت اشتباه.", k.Match));
            else if (median > 0 && k.Offer.Price > median * HighOutlier)
                result.Add(new ReviewedOffer(k.Id, k.Offer, false, OfferVerdict.TooHigh,
                    $"خیلی گران‌تر از بقیه (بیش از {HighOutlier * 100:0}٪ میانهٔ {median:N0} تومان)؛ احتمالاً بسته یا مدل دیگر.", k.Match));
            else
                result.Add(new ReviewedOffer(k.Id, k.Offer, true, OfferVerdict.Accepted, "معتبر؛ در محاسبه حساب شد.", k.Match));
        }
        foreach (var f in forced)
            result.Add(new ReviewedOffer(f.Id, f.Offer, true, OfferVerdict.Forced, "به دستور شما حساب شد (داوری سیستم نادیده گرفته شد).", f.Match));

        return result.OrderBy(r => r.Offer.Price).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// فروشندگان معتبر ترب از نتیجهٔ داوری، از ارزان به گران و هر فروشگاه یک بار.
    /// <para>قیمتی از ترب که فقط به خاطر «تکراری» کنار رفته (همان فروشگاه در منبع دیگری، مثلاً دیجی‌کالا مستقیم، ارزان‌تر بود)
    /// هم حساب می‌شود؛ چون در خود ترب همین قیمت دیده می‌شود و ما باید از آن هم ارزان‌تر باشیم.
    /// مثال: ترب «دیجی‌کالا» ۳۰۰، دیجی‌کالای مستقیم ۲۹۵، ترب «X» ۲۸۰ ← [X ۲۸۰، دیجی‌کالا ۳۰۰].</para>
    /// </summary>
    public static List<CompetitorOffer> TorobSellers(IEnumerable<ReviewedOffer> reviewed)
        => reviewed
            .Where(r => r.Offer.Source == CompetitorPriceService.Torob && (r.Accepted || r.Code == OfferVerdict.Duplicate))
            .Select(r => r.Offer)
            .OrderBy(o => o.Price)
            .DistinctBy(o => ShopKey(o.Shop))
            .ToList();

    /// <summary>
    /// حذف قیمت‌های نامعتبر و پرت: قیمت صفر/منفی، تکراری‌های یک فروشگاه (ارزان‌ترین می‌ماند)، و قیمت‌هایی که از میانه
    /// خیلی دورند (کمتر از ۴۰٪ یا بیشتر از ۲۵۰٪ میانه؛ معمولاً قاب/لوازم جانبی یا مدل دیگر).
    /// <para>مثال: [۳۰۰، ۲۸۰، ۲۷۰، ۴۵] هزار ← ۴۵ کنار می‌رود. (نسخهٔ ساده‌شدهٔ <see cref="Review"/> بدون نام کالا.)</para>
    /// </summary>
    public static (List<CompetitorOffer> Offers, List<CompetitorOffer> Ignored) Clean(IReadOnlyList<CompetitorOffer> competitors)
    {
        var reviewed = Review(competitors);
        var offers = reviewed.Where(r => r.Accepted).Select(r => r.Offer).ToList();
        var ignored = reviewed.Where(r => r.Code is OfferVerdict.TooLow or OfferVerdict.TooHigh).Select(r => r.Offer).ToList();
        return (offers, ignored);
    }

    /// <summary>
    /// شناسهٔ ثابت یک پیشنهاد (۱۲ نویسهٔ هگز) از منبع، فروشگاه، لینک و قیمت؛ با تکرار جست‌وجو (از کش) همان می‌ماند
    /// تا دکمهٔ «حذف» کارمند به همان پیشنهاد بخورد. مثال: («ترب»، «فروشگاه A»، …، ۲۸۰٬۰۰۰) ← «3f9a0c1d2e4b».
    /// </summary>
    public static string OfferId(CompetitorOffer offer)
    {
        var text = $"{offer.Source}|{ShopKey(offer.Shop)}|{offer.Url}|{offer.Price.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
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

    /// <summary>دامنهٔ یک لینک بدون www. مثال: «https://www.Faratek.ir/p/1» ← «faratek.ir»؛ لینک نامعتبر ← null.</summary>
    public static string? HostOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;
        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    // «دیجی‌کالا» و «ديجي كالا» یک فروشگاه‌اند.
    private static string ShopKey(string shop)
        => new string(Seo.Engine.PersianText.Normalize(shop).Where(char.IsLetterOrDigit).ToArray());
}
