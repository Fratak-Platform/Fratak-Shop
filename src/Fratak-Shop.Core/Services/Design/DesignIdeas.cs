using System.Globalization;

namespace Fratak_Shop.Core.Services.Design;

/// <summary>کالای نامزد ایدهٔ بنر (فقط کالای فعال و موجود با عکس). ImageUrl محلی است؛ ImageWidth/Height برای جا دادن در طرح.</summary>
public sealed record IdeaProduct(Guid Id, string Name, string Slug, string? ImageUrl, int ImageWidth, int ImageHeight,
    decimal Price, decimal FinalPrice, int Stock, int Views, int Sales, string? CategoryName, string? CategorySlug, DateTimeOffset CreatedAt)
{
    /// <summary>درصد تخفیف واقعی (گرد به پایین). مثال: ۱۰۰ → ۷۵ ← ۲۵.</summary>
    public int DiscountPercent => Price > 0 && FinalPrice < Price ? (int)Math.Floor((Price - FinalPrice) * 100 / Price) : 0;
}

/// <summary>دستهٔ نامزد «معرفی دسته» با تعداد کالای موجود و پرفروش‌ترین کالایش.</summary>
public sealed record IdeaCategory(Guid Id, string Name, string Slug, int InStock, IdeaProduct? Hero);

/// <summary>ورودی ایده‌ساز: کالاها و دسته‌ها + نام فروشگاه (برای تیتر مناسبت‌ها).</summary>
public sealed record IdeaInput(IReadOnlyList<IdeaProduct> Products, IReadOnlyList<IdeaCategory> Categories, string SiteName)
{
    /// <summary>دور ۲۶: حداقل خرید برای ارسال رایگان (تومان؛ ۰ = ارسال رایگان نداریم) ← ایدهٔ بنر «ارسال رایگان».</summary>
    public decimal FreeShippingFrom { get; init; }
}

/// <summary>
/// یک ایدهٔ بنر آمادهٔ تأیید. Key یکتاست (همان ایده دوباره پیشنهاد نمی‌شود)، Reason دلیل داده‌محور ایده را می‌گوید.
/// مثال: Key «deal:…:1405-08»، Reason «بیشترین تخفیف موجود (۲۵٪) با ۱۲ عدد موجودی».
/// </summary>
public sealed record DesignIdea(string Key, string Title, string Reason, DesignSpec Spec, string? LinkUrl, DateTimeOffset? ExpiresAt, Guid? ProductId = null);

/// <summary>مناسبت تقویمی فعال (ایران): نام، تیتر، متن، پالت و پایان.</summary>
public sealed record DesignOccasion(string Key, string Name, string Headline, string Subline, string Palette, DateTimeOffset EndsAt);

/// <summary>
/// ایده‌ساز بنر بدون هوش مصنوعی (دور ۲۱؛ بازنویسی دور ۲۶): از دادهٔ واقعی فروشگاه ایده می‌سازد، نه متن خیالی.
/// دور ۲۶ (خواستهٔ کاربر): بیشتر بنرها برای <b>دسته و کمپین</b>اند و بنر تک‌کالا کم و فقط برای کالای واقعاً ویژه. به ترتیب اهمیت:
/// <list type="number">
/// <item>مناسبت فعال (یلدا، نوروز، مدارس، بلک‌فرایدی) با بیشترین تخفیف واقعی موجود.</item>
/// <item>حراج دسته: دسته‌ای که دست‌کم ۳ کالای موجودِ تخفیف‌دار (≥۵٪) دارد ← «حراج هدفون، تا ۳۰٪ تخفیف» با چند کالای کنار هم.</item>
/// <item>تازه‌ها: دست‌کم ۳ کالای تازه در ۱۴ روز اخیر ← «تازه‌های فروشگاه».</item>
/// <item>معرفی دسته با بیشترین کالای موجود (چند کالای کنار هم).</item>
/// <item>ارسال رایگان (اگر در تنظیمات فعال باشد).</item>
/// <item>کالای ویژه: فقط تخفیف ≥۲۵٪، موجودی ≥۲ و بازدید دست‌کم میانهٔ کالاها.</item>
/// <item>پرفروش‌ترین کالا (دست‌کم ۵ فروش).</item>
/// </list>
/// <para>بدون الگوی فریب: تخفیف و قیمت همان قیمت واقعی امروز است؛ «فقط امروز»/شمارش معکوس جعلی ساخته نمی‌شود؛ کالای ناموجود هرگز.</para>
/// <para>هر ایده کلید یکتای هفتگی/ماهانه دارد؛ ایدهٔ ردشده یا منتشرشده با همان کلید دوباره نمی‌آید (DesignService).</para>
/// <para>محل استفاده: Web/Infrastructure/Design/DesignStudio.cs (دکمهٔ «ساخت ایده الان»، کار روزانهٔ SmartWorker و رشد خودکار).</para>
/// </summary>
public static class DesignIdeas
{
    private static readonly PersianCalendar Persian = new();
    private static readonly TimeSpan Tehran = TimeSpan.FromHours(3.5);

    /// <summary>کمترین درصد تخفیف برای بنر تک‌کالا (دور ۲۶: بنر کالا فقط برای کالای واقعاً ویژه).</summary>
    public const int ProductDealPercent = 25;

    /// <summary>ساخت حداکثر <paramref name="max"/> ایده، به ترتیب اهمیت. ورودی خالی ← فهرست خالی (ایدهٔ بی‌پشتوانه ساخته نمی‌شود).</summary>
    public static List<DesignIdea> Generate(IdeaInput input, DateTimeOffset now, int max = 6)
    {
        var ideas = new List<DesignIdea>();
        var products = input.Products.Where(p => p.Stock > 0 && !string.IsNullOrWhiteSpace(p.ImageUrl) && p.FinalPrice > 0).ToList();
        if (products.Count == 0)
            return ideas;
        var week = WeekKey(now);
        var month = MonthKey(now);
        var palettes = DesignPalettes.All.Select(p => p.Key).Where(k => k != "clean").ToArray();
        string Count(int n) => DesignTemplates.FaDigits(n.ToString(CultureInfo.InvariantCulture));

        // ۱) مناسبت
        if (Occasion(now) is { } occasion)
        {
            var maxDiscount = products.Where(p => p.Stock >= 2).Select(p => p.DiscountPercent).DefaultIfEmpty(0).Max();
            var top = products.OrderByDescending(p => p.DiscountPercent).ThenByDescending(p => p.Sales).ThenByDescending(p => p.Views).Take(3).ToList();
            var badge = maxDiscount >= 10 ? $"تا {DesignTemplates.FaDigits(maxDiscount + "%")} تخفیف" : null;
            ideas.Add(new DesignIdea($"occasion:{occasion.Key}:{Persian.GetYear(Local(now))}", occasion.Name,
                $"مناسبت «{occasion.Name}» تا {Count(DaysLeft(now, occasion.EndsAt))} روز دیگر؛" +
                (badge is null ? " بدون ادعای تخفیف (تخفیف ≥۱۰٪ موجود نیست)." : $" بیشترین تخفیف واقعی موجود {DesignTemplates.FaDigits(maxDiscount + "%")}."),
                new DesignSpec("festive", occasion.Palette, occasion.Headline, occasion.Subline, badge, "مشاهدهٔ پیشنهادها", Image(top[0]),
                    Kicker: input.SiteName, Gallery: Images(top)),
                maxDiscount >= 10 ? "/products?Discounted=true&Sort=MostDiscounted" : "/products?Sort=BestSelling", occasion.EndsAt));
        }

        // ۲) حراج دسته (پرتخفیف‌ترین دسته‌ها اول)
        var categoryDeals = products.Where(p => p.DiscountPercent >= 5 && p.CategorySlug is not null && p.CategoryName is not null)
            .GroupBy(p => p.CategorySlug!)
            .Where(g => g.Count() >= 3)
            .Select(g => (Slug: g.Key, Name: g.First().CategoryName!, Items: g.OrderByDescending(p => p.DiscountPercent).ThenByDescending(p => p.Sales).ToList()))
            .OrderByDescending(g => g.Items.Count).ThenByDescending(g => g.Items[0].DiscountPercent)
            .Take(2);
        foreach (var deal in categoryDeals)
        {
            var maxOff = deal.Items[0].DiscountPercent;
            var badge = $"تا {DesignTemplates.FaDigits(maxOff + "%")} تخفیف";
            ideas.Add(new DesignIdea($"catdeal:{deal.Slug}:{week}", "حراج " + deal.Name,
                $"{Count(deal.Items.Count)} کالای موجود دستهٔ «{deal.Name}» تخفیف واقعی دارند (بیشترین {DesignTemplates.FaDigits(maxOff + "%")}).",
                new DesignSpec("collage", Pick(["coral", "sunset", "candy", "night"], deal.Items[0].Id, 1), $"حراج {deal.Name}",
                    $"{Count(deal.Items.Count)} کالای تخفیف‌دار، فقط کالاهای موجود", badge, "دیدن تخفیف‌ها", Image(deal.Items[0]),
                    Kicker: "حراج دسته", Gallery: Images(deal.Items.Take(3))),
                $"/category/{deal.Slug}?Discounted=true", now.AddDays(7)));
        }

        // ۳) تازه‌ها
        var fresh = products.Where(p => p.CreatedAt >= now.AddDays(-14)).OrderByDescending(p => p.CreatedAt).ToList();
        if (fresh.Count >= 3)
        {
            ideas.Add(new DesignIdea($"fresh:{week}", "تازه‌های " + input.SiteName,
                $"{Count(fresh.Count)} کالای تازه در ۲ هفتهٔ اخیر.",
                new DesignSpec("collage", Pick(["mint", "sky", "indigo", "ocean"], fresh[0].Id, 2), $"تازه‌های {input.SiteName}",
                    $"{Count(fresh.Count)} کالای تازه همین حالا موجود است", "جدید", "دیدن تازه‌ها", Image(fresh[0]),
                    Kicker: "تازه رسید", Gallery: Images(fresh.Take(3))),
                "/products?Sort=Newest", now.AddDays(14)));
        }

        // ۴) معرفی دسته
        var category = input.Categories.Where(c => c.InStock >= 3 && c.Hero is { ImageUrl: not null })
            .OrderByDescending(c => c.InStock).FirstOrDefault();
        if (category is not null)
        {
            var members = products.Where(p => p.CategorySlug == category.Slug).OrderByDescending(p => p.Sales).ThenByDescending(p => p.Views).Take(3).ToList();
            if (members.All(p => p.Id != category.Hero!.Id))
                members.Insert(0, category.Hero!);
            var gallery = Images(members.Take(3));
            ideas.Add(new DesignIdea($"cat:{category.Id:N}:{month}", "معرفی دستهٔ " + category.Name,
                $"دستهٔ «{category.Name}» با {Count(category.InStock)} کالای موجود.",
                new DesignSpec(gallery.Count >= 2 ? "collage" : "split", Pick(palettes, category.Id, 3), category.Name,
                    $"{Count(category.InStock)} کالای موجود با ارسال سریع", null, "دیدن همه", Image(category.Hero!), Kicker: "دسته‌بندی منتخب", Gallery: gallery),
                "/category/" + category.Slug, now.AddDays(30)));
        }

        // ۵) ارسال رایگان
        if (input.FreeShippingFrom > 0)
        {
            ideas.Add(new DesignIdea($"freeship:{month}", "ارسال رایگان",
                $"ارسال رایگان برای خرید بالای {DesignTemplates.Toman(input.FreeShippingFrom)} (از تنظیمات فروشگاه).",
                new DesignSpec("pattern", "indigo", "ارسال رایگان", $"برای خریدهای بالای {DesignTemplates.Toman(input.FreeShippingFrom)}", null, "شروع خرید",
                    Kicker: input.SiteName),
                "/products", now.AddDays(30)));
        }

        // ۶) کالای واقعاً ویژه (کم و با شرط سخت)
        var medianViews = products.Select(p => p.Views).OrderBy(v => v).ElementAt((products.Count - 1) / 2);
        var bestDeal = products.Where(p => p.DiscountPercent >= ProductDealPercent && p.Stock >= 2 && p.Views >= medianViews)
            .OrderByDescending(p => p.DiscountPercent).ThenByDescending(p => p.Views).FirstOrDefault();
        if (bestDeal is not null)
        {
            ideas.Add(new DesignIdea($"deal:{bestDeal.Id:N}:{week}", "پیشنهاد ویژه: " + DesignTemplates.Shorten(bestDeal.Name, 40),
                $"تخفیف واقعی {DesignTemplates.FaDigits(bestDeal.DiscountPercent + "%")} روی کالای پربازدید با {Count(bestDeal.Stock)} عدد موجودی.",
                new DesignSpec("sale", Pick(palettes, bestDeal.Id, 1), DesignTemplates.Shorten(bestDeal.Name, 46),
                    bestDeal.CategoryName is null ? null : "از دستهٔ " + bestDeal.CategoryName,
                    DesignTemplates.FaDigits(bestDeal.DiscountPercent + "%") + " تخفیف", "خرید با تخفیف", Image(bestDeal),
                    DesignTemplates.Toman(bestDeal.FinalPrice), DesignTemplates.Toman(bestDeal.Price), "پیشنهاد ویژه"),
                "/product/" + bestDeal.Slug, now.AddDays(7), bestDeal.Id));
        }

        // ۷) پرفروش
        var bestSeller = products.Where(p => p.Sales >= 5 && p.Id != bestDeal?.Id).OrderByDescending(p => p.Sales).ThenByDescending(p => p.Views).FirstOrDefault();
        if (bestSeller is not null)
        {
            ideas.Add(new DesignIdea($"best:{bestSeller.Id:N}:{month}", "پرفروش: " + DesignTemplates.Shorten(bestSeller.Name, 40),
                $"پرفروش‌ترین کالای موجود ({Count(bestSeller.Sales)} فروش).",
                new DesignSpec("split", Pick(palettes, bestSeller.Id, 2), DesignTemplates.Shorten(bestSeller.Name, 46),
                    "انتخاب اول خریداران " + input.SiteName, null, "مشاهده و خرید", Image(bestSeller),
                    DesignTemplates.Toman(bestSeller.FinalPrice), bestSeller.DiscountPercent > 0 ? DesignTemplates.Toman(bestSeller.Price) : null, "پرفروش‌ترین"),
                "/product/" + bestSeller.Slug, now.AddDays(30), bestSeller.Id));
        }

        return ideas.Take(Math.Max(0, max)).ToList();
    }

    /// <summary>عکس چند کالا برای چیدمان «کنار هم» (فقط دارای عکس).</summary>
    private static List<DesignImage> Images(IEnumerable<IdeaProduct> products) =>
        products.Select(Image).Where(i => i is not null).Select(i => i!).ToList();

    /// <summary>
    /// مناسبت فعال امروز (به وقت ایران). پنجره‌ها از قبل شروع می‌شوند تا بنر سر وقت آماده باشد.
    /// مثال: ۲۵ آذر ← «یلدا» تا پایان ۳۰ آذر؛ ۲۰ اسفند ← «نوروز» تا ۱۳ فروردین؛ ۲۵ نوامبر ← «بلک‌فرایدی» تا پایان نوامبر.
    /// </summary>
    public static DesignOccasion? Occasion(DateTimeOffset now)
    {
        var local = Local(now);
        var year = Persian.GetYear(local);
        var month = Persian.GetMonth(local);
        var day = Persian.GetDayOfMonth(local);

        // یلدا: ۱۵ تا ۳۰ آذر (ماه ۹).
        if (month == 9 && day >= 15)
            return new DesignOccasion("yalda", "جشنوارهٔ یلدا", "جشنوارهٔ یلدا", "بلندترین شب سال، با خریدی به‌یادماندنی", "festive", End(year, 9, 30));
        // نوروز: ۵ اسفند تا ۱۳ فروردین.
        if (month == 12 && day >= 5)
            return new DesignOccasion("nowruz", "جشنوارهٔ نوروز", "جشنوارهٔ نوروزی", "سال نو، وسایل نو؛ پیش از تعطیلات سفارش دهید", "forest", End(year + 1, 1, 13));
        if (month == 1 && day <= 13)
            return new DesignOccasion("nowruz", "جشنوارهٔ نوروز", "جشنوارهٔ نوروزی", "نوروزتان پیروز؛ پیشنهادهای ویژهٔ سال نو", "forest", End(year, 1, 13));
        // بازگشایی مدارس: ۱۵ شهریور تا ۱۰ مهر.
        if ((month == 6 && day >= 15) || (month == 7 && day <= 10))
            return new DesignOccasion("school", "بازگشایی مدارس", "آماده برای مهر", "هر چه برای درس و دانشگاه لازم است", "royal",
                End(year, 7, 10));
        // بلک‌فرایدی (حراج پایان نوامبر): ۲۰ تا ۳۰ نوامبر میلادی.
        if (local.Month == 11 && local.Day >= 20)
        {
            var end = new DateTimeOffset(local.Year, 11, 30, 23, 59, 0, Tehran);
            return new DesignOccasion("blackfriday", "حراج بلک‌فرایدی", "حراج بلک‌فرایدی", "تخفیف‌های پایان پاییز روی کالاهای منتخب", "night", end);
        }
        return null;
    }

    private static DateTime Local(DateTimeOffset now) => now.ToOffset(Tehran).DateTime;

    private static DateTimeOffset End(int year, int month, int day)
    {
        var date = Persian.ToDateTime(year, month, Math.Min(day, Persian.GetDaysInMonth(year, month)), 23, 59, 0, 0);
        return new DateTimeOffset(date, Tehran);
    }

    private static int DaysLeft(DateTimeOffset now, DateTimeOffset end) => Math.Max(0, (int)Math.Ceiling((end - now).TotalDays));

    /// <summary>کلید هفته (شمسی). مثال: «1405-w33».</summary>
    public static string WeekKey(DateTimeOffset now)
    {
        var local = Local(now);
        return $"{Persian.GetYear(local)}-w{(Persian.GetDayOfYear(local) - 1) / 7 + 1}";
    }

    /// <summary>کلید ماه (شمسی). مثال: «1405-08».</summary>
    public static string MonthKey(DateTimeOffset now)
    {
        var local = Local(now);
        return $"{Persian.GetYear(local)}-{Persian.GetMonth(local):00}";
    }

    private static DesignImage? Image(IdeaProduct product) =>
        string.IsNullOrWhiteSpace(product.ImageUrl) ? null : new DesignImage(product.ImageUrl!, Math.Max(1, product.ImageWidth), Math.Max(1, product.ImageHeight));

    /// <summary>پالت ثابت برای هر شناسه (هر بار همان رنگ، ولی ایده‌های مختلف رنگ‌های مختلف).</summary>
    private static string Pick(string[] palettes, Guid id, int salt)
    {
        var bytes = id.ToByteArray();
        return palettes[(bytes[0] + bytes[5] * 3 + salt) % palettes.Length];
    }
}
