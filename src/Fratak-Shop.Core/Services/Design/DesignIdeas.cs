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
public sealed record IdeaInput(IReadOnlyList<IdeaProduct> Products, IReadOnlyList<IdeaCategory> Categories, string SiteName);

/// <summary>
/// یک ایدهٔ بنر آمادهٔ تأیید. Key یکتاست (همان ایده دوباره پیشنهاد نمی‌شود)، Reason دلیل داده‌محور ایده را می‌گوید.
/// مثال: Key «deal:…:1405-08»، Reason «بیشترین تخفیف موجود (۲۵٪) با ۱۲ عدد موجودی».
/// </summary>
public sealed record DesignIdea(string Key, string Title, string Reason, DesignSpec Spec, string? LinkUrl, DateTimeOffset? ExpiresAt, Guid? ProductId = null);

/// <summary>مناسبت تقویمی فعال (ایران): نام، تیتر، متن، پالت و پایان.</summary>
public sealed record DesignOccasion(string Key, string Name, string Headline, string Subline, string Palette, DateTimeOffset EndsAt);

/// <summary>
/// ایده‌ساز بنر بدون هوش مصنوعی (دور ۲۱): از دادهٔ واقعی فروشگاه ایده می‌سازد، نه متن خیالی.
/// <list type="number">
/// <item>مناسبت فعال با تاریخ ثابت (یلدا، نوروز، بازگشایی مدارس، حراج پایان نوامبر) با بیشترین تخفیف واقعی موجود.</item>
/// <item>بیشترین تخفیف واقعی (کالای موجود با ≥۲ عدد موجودی و تخفیف ≥۱۰٪).</item>
/// <item>پرفروش‌ترین کالای موجود.</item>
/// <item>تازه‌رسیده (۲۱ روز اخیر).</item>
/// <item>معرفی دسته با بیشترین کالای موجود.</item>
/// </list>
/// <para>بدون الگوی فریب: تخفیف و قیمت همان قیمت واقعی امروز است؛ «فقط امروز»/شمارش معکوس جعلی ساخته نمی‌شود؛ کالای ناموجود هرگز.</para>
/// <para>هر ایده کلید یکتای هفتگی/ماهانه دارد؛ ایدهٔ ردشده یا منتشرشده با همان کلید دوباره نمی‌آید (DesignService).</para>
/// <para>محل استفاده: Web/Infrastructure/Design/DesignStudio.cs (دکمهٔ «ایدهٔ تازه بده» و رشد خودکار وقتی بازدید افت می‌کند).</para>
/// </summary>
public static class DesignIdeas
{
    private static readonly PersianCalendar Persian = new();
    private static readonly TimeSpan Tehran = TimeSpan.FromHours(3.5);

    /// <summary>ساخت حداکثر <paramref name="max"/> ایده، به ترتیب اهمیت. ورودی خالی ← فهرست خالی (ایدهٔ بی‌پشتوانه ساخته نمی‌شود).</summary>
    public static List<DesignIdea> Generate(IdeaInput input, DateTimeOffset now, int max = 6)
    {
        var ideas = new List<DesignIdea>();
        var products = input.Products.Where(p => p.Stock > 0 && !string.IsNullOrWhiteSpace(p.ImageUrl) && p.FinalPrice > 0).ToList();
        var week = WeekKey(now);
        var month = MonthKey(now);
        var palettes = DesignPalettes.All.Select(p => p.Key).Where(k => k != "clean").ToArray();

        var bestDeal = products.Where(p => p.DiscountPercent >= 10 && p.Stock >= 2)
            .OrderByDescending(p => p.DiscountPercent).ThenByDescending(p => p.Views).FirstOrDefault();

        if (Occasion(now) is { } occasion)
        {
            var maxDiscount = products.Where(p => p.Stock >= 2).Select(p => p.DiscountPercent).DefaultIfEmpty(0).Max();
            var hero = bestDeal ?? products.OrderByDescending(p => p.Sales).FirstOrDefault();
            if (hero is not null)
            {
                var badge = maxDiscount >= 10 ? $"تا {DesignTemplates.FaDigits(maxDiscount + "%")} تخفیف" : null;
                ideas.Add(new DesignIdea($"occasion:{occasion.Key}:{Persian.GetYear(Local(now))}", occasion.Name,
                    $"مناسبت «{occasion.Name}» تا {DesignTemplates.FaDigits(DaysLeft(now, occasion.EndsAt).ToString(CultureInfo.InvariantCulture))} روز دیگر؛" +
                    (badge is null ? " بدون ادعای تخفیف (تخفیف ≥۱۰٪ موجود نیست)." : $" بیشترین تخفیف واقعی موجود {DesignTemplates.FaDigits(maxDiscount + "%")}."),
                    new DesignSpec("festive", occasion.Palette, occasion.Headline, occasion.Subline, badge, "مشاهدهٔ پیشنهادها", Image(hero),
                        Kicker: input.SiteName),
                    maxDiscount >= 10 ? "/products?Discounted=true&Sort=MostDiscounted" : "/products?Sort=BestSelling", occasion.EndsAt, hero.Id));
            }
        }

        if (bestDeal is not null)
        {
            ideas.Add(new DesignIdea($"deal:{bestDeal.Id:N}:{week}", "پیشنهاد ویژه: " + DesignTemplates.Shorten(bestDeal.Name, 40),
                $"بیشترین تخفیف واقعی موجود ({DesignTemplates.FaDigits(bestDeal.DiscountPercent + "%")}) با {DesignTemplates.FaDigits(bestDeal.Stock.ToString(CultureInfo.InvariantCulture))} عدد موجودی.",
                new DesignSpec("sale", Pick(palettes, bestDeal.Id, 1), DesignTemplates.Shorten(bestDeal.Name, 46),
                    bestDeal.CategoryName is null ? null : "از دستهٔ " + bestDeal.CategoryName,
                    DesignTemplates.FaDigits(bestDeal.DiscountPercent + "%") + " تخفیف", "خرید با تخفیف", Image(bestDeal),
                    DesignTemplates.Toman(bestDeal.FinalPrice), DesignTemplates.Toman(bestDeal.Price), "پیشنهاد ویژه"),
                "/product/" + bestDeal.Slug, now.AddDays(7), bestDeal.Id));
        }

        var bestSeller = products.Where(p => p.Sales > 0 && p.Id != bestDeal?.Id).OrderByDescending(p => p.Sales).ThenByDescending(p => p.Views).FirstOrDefault();
        if (bestSeller is not null)
        {
            ideas.Add(new DesignIdea($"best:{bestSeller.Id:N}:{month}", "پرفروش: " + DesignTemplates.Shorten(bestSeller.Name, 40),
                $"پرفروش‌ترین کالای موجود ({DesignTemplates.FaDigits(bestSeller.Sales.ToString(CultureInfo.InvariantCulture))} فروش).",
                new DesignSpec("split", Pick(palettes, bestSeller.Id, 2), DesignTemplates.Shorten(bestSeller.Name, 46),
                    "انتخاب اول خریداران " + input.SiteName, null, "مشاهده و خرید", Image(bestSeller),
                    DesignTemplates.Toman(bestSeller.FinalPrice), bestSeller.DiscountPercent > 0 ? DesignTemplates.Toman(bestSeller.Price) : null, "پرفروش‌ترین"),
                "/product/" + bestSeller.Slug, now.AddDays(30), bestSeller.Id));
        }

        var fresh = products.Where(p => p.CreatedAt >= now.AddDays(-21) && p.Id != bestDeal?.Id && p.Id != bestSeller?.Id)
            .OrderByDescending(p => p.CreatedAt).FirstOrDefault();
        if (fresh is not null)
        {
            ideas.Add(new DesignIdea($"new:{fresh.Id:N}", "تازه رسید: " + DesignTemplates.Shorten(fresh.Name, 40),
                "کالای تازه (کمتر از ۳ هفته) که هنوز معرفی نشده است.",
                new DesignSpec("minimal", "clean", DesignTemplates.Shorten(fresh.Name, 46), fresh.CategoryName is null ? null : "تازه در دستهٔ " + fresh.CategoryName,
                    "جدید", "مشاهدهٔ محصول", Image(fresh), DesignTemplates.Toman(fresh.FinalPrice), null, "تازه رسید"),
                "/product/" + fresh.Slug, now.AddDays(21), fresh.Id));
        }

        var category = input.Categories.Where(c => c.InStock >= 3 && c.Hero is { ImageUrl: not null })
            .OrderByDescending(c => c.InStock).FirstOrDefault();
        if (category is not null)
        {
            ideas.Add(new DesignIdea($"cat:{category.Id:N}:{month}", "معرفی دستهٔ " + category.Name,
                $"دستهٔ «{category.Name}» با {DesignTemplates.FaDigits(category.InStock.ToString(CultureInfo.InvariantCulture))} کالای موجود.",
                new DesignSpec("split", Pick(palettes, category.Id, 3), category.Name, $"{DesignTemplates.FaDigits(category.InStock.ToString(CultureInfo.InvariantCulture))} کالای موجود با ارسال سریع",
                    null, "دیدن همه", Image(category.Hero!), Kicker: "دسته‌بندی منتخب"),
                "/category/" + category.Slug, now.AddDays(30)));
        }

        return ideas.Take(Math.Max(0, max)).ToList();
    }

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
