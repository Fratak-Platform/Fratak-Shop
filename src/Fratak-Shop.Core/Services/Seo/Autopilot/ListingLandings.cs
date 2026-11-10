namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>
/// دور ۲۴ (پیشنهاد ۴۰ و ۴۱): قاعده‌ها و متن صفحه‌های فرود «دسته + برند» و «دسته زیر X تومان».
/// <para>
/// هر دو صفحه فقط وقتی <b>قابل ایندکس</b> می‌شوند که محتوای کافی داشته باشند (حداقل <see cref="MinBrandProducts"/> کالا برای برند،
/// <see cref="MinPriceProducts"/> کالا برای بازهٔ قیمت) و هیچ فیلتر دیگری (صفحه ۲، ویژگی، عبارت جست‌وجو، فقط موجود …) نخورده باشد؛
/// وگرنه noindex می‌مانند تا صفحهٔ کم‌محتوا یا تکراری به گوگل نرسد (بدون خطر جریمه).
/// </para>
/// <para>محل استفاده: Pages/Search (عنوان، توضیح، canonical)، نقشهٔ سایت (SitemapService) و بلوک «برندهای …» صفحهٔ دسته.</para>
/// </summary>
public static class ListingLandings
{
    /// <summary>حداقل کالای فعال یک برند در یک دسته برای ساخت صفحهٔ «دسته + برند».</summary>
    public const int MinBrandProducts = 3;

    /// <summary>حداقل کالای موجود یک بازهٔ قیمت برای ایندکس شدن صفحهٔ «زیر X تومان».</summary>
    public const int MinPriceProducts = 6;

    /// <summary>نشانی تمیز صفحهٔ دسته + برند. مثال: ("headphone", "haylou") ← «/category/headphone/brand/haylou».</summary>
    public static string BrandPath(string categorySlug, string brandSlug) =>
        $"/category/{Uri.EscapeDataString(categorySlug.Trim())}/brand/{Uri.EscapeDataString(brandSlug.Trim())}";

    /// <summary>عنوان صفحه. مثال: («هدفون بی‌سیم»، «هایلو») ← «خرید هدفون بی‌سیم هایلو».</summary>
    public static string BrandTitle(string categoryName, string brandName) => $"خرید {categoryName.Trim()} {brandName.Trim()}";

    /// <summary>
    /// توضیح متا از دادهٔ واقعی (بدون ادعای ساختگی). مثال: («هدفون»، «هایلو»، ۷، «فراتک») ←
    /// «خرید هدفون هایلو از فراتک: مقایسهٔ قیمت و مشخصات ۷ مدل هدفون هایلو، با ضمانت اصالت کالا و ارسال به سراسر ایران.»
    /// </summary>
    public static string BrandDescription(string categoryName, string brandName, int count, string? siteName)
    {
        var cat = categoryName.Trim();
        var brand = brandName.Trim();
        var from = string.IsNullOrWhiteSpace(siteName) ? "" : $" از {siteName.Trim()}";
        return $"خرید {cat} {brand}{from}: مقایسهٔ قیمت و مشخصات {Fratak_Shop.Core.Services.Seo.Engine.PersianText.Digits(count)} مدل {cat} {brand}، با ضمانت اصالت کالا و ارسال به سراسر ایران.";
    }

    /// <summary>معرفی کوتاه بالای صفحهٔ دسته + برند (متن ساده).</summary>
    public static string BrandIntro(string categoryName, string brandName, int count) =>
        $"در این صفحه {Fratak_Shop.Core.Services.Seo.Engine.PersianText.Digits(count)} مدل {categoryName.Trim()} برند {brandName.Trim()} را یک‌جا می‌بینید؛ با فیلتر قیمت و مشخصات، گزینهٔ مناسب خودتان را پیدا کنید.";

    /// <summary>آیا صفحهٔ دسته + برند قابل ایندکس است؟ (فقط صفحهٔ اول، بدون فیلتر دیگر، حداقل ۳ کالا)</summary>
    public static bool BrandIndexable(int count, int page, bool otherFilters) => page <= 1 && !otherFilters && count >= MinBrandProducts;

    /// <summary>
    /// بازهٔ قیمتی که دقیقاً با maxPrice صفحه یکی است (همان بازه‌هایی که در صفحهٔ دسته لینک شده‌اند)، یا null.
    /// مثال: لینک «…?maxPrice=5000000» و maxPrice = ۵٬۰۰۰٬۰۰۰ ← همان لینک؛ maxPrice = ۴٬۹۰۰٬۰۰۰ ← null.
    /// </summary>
    public static SeoLandingLink? MatchPriceBand(IReadOnlyList<SeoLandingLink> bands, decimal maxPrice)
    {
        var suffix = "maxPrice=" + (long)maxPrice;
        return maxPrice == Math.Floor(maxPrice)
            ? bands.FirstOrDefault(b => b.Url.EndsWith(suffix, StringComparison.Ordinal))
            : null;
    }

    /// <summary>
    /// توضیح متای صفحهٔ بازهٔ قیمت. مثال: («خرید گوشی زیر ۵ میلیون تومان»، ۱۲، «فراتک») ←
    /// «خرید گوشی زیر ۵ میلیون تومان از فراتک: ۱۲ کالای موجود در این بودجه، مرتب‌شده و قابل مقایسه، با ضمانت اصالت و ارسال سریع.»
    /// </summary>
    public static string PriceDescription(string label, int count, string? siteName)
    {
        var from = string.IsNullOrWhiteSpace(siteName) ? "" : $" از {siteName.Trim()}";
        return $"{label.Trim()}{from}: {Fratak_Shop.Core.Services.Seo.Engine.PersianText.Digits(count)} کالای موجود در این بودجه، مرتب‌شده و قابل مقایسه، با ضمانت اصالت و ارسال سریع.";
    }
}
