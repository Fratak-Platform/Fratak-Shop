using System.Globalization;
using Fratak_Shop.Core.Services.Categories;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۴ – صفحه‌های «هاب» سئو (پیشنهادهای ۴۲، ۴۳ و ۴۴)
//  • واژه‌نامه (/glossary): اصطلاحات فنی کالاهای همین فروشگاه با توضیح ساده + DefinedTermSet.
//  • راهنمای جامع دسته (/guide/{category}): صفحهٔ ستون (pillar) از دادهٔ واقعی دسته: بازهٔ قیمت، برندها، بازهٔ مشخصات،
//    پرسش‌های متداول، زیردسته‌ها، مقاله‌ها و پرفروش‌ها.
//  • صفحهٔ فرود مناسبت (/occasion/{slug}): نامک ثابت (بدون سال) با کالاهای واقعی موجود و شمارش معکوس.
//  قانون مشترک: هر صفحه‌ای که دادهٔ کافی ندارد noindex است (بدون خطر «محتوای کم‌ارزش» گوگل) و هیچ متنی ادعای ساختگی ندارد.
//  همه با کش حافظه (۳۰ دقیقه تا ۶ ساعت) تا روی هاست ضعیف هم سبک باشند.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>یک اصطلاح واژه‌نامه. مثال: («ip-rating»، «استاندارد IP …»، «کدی دو رقمی …»، ۱۲ کالا، «هدفون بی‌سیم»، «wireless-headphone»).</summary>
public sealed record GlossaryTerm(string Slug, string Name, string Definition, int ProductCount, string? CategoryName, string? CategorySlug);

/// <summary>واژه‌نامه؛ <see cref="Indexable"/> = حداقل ۵ اصطلاح با کالای واقعی.</summary>
public sealed record GlossaryView(IReadOnlyList<GlossaryTerm> Terms)
{
    public const int MinTerms = 5;
    public bool Indexable => Terms.Count >= MinTerms;
}

/// <summary>بازهٔ یک مشخصه در دسته. مثال: («شارژدهی»، «۱۰ ساعت»، «۴۰ ساعت»، ۹ کالا، «battery-life»).</summary>
public sealed record SpecRange(string Name, string Lowest, string Highest, int Products, string? TermSlug);

/// <summary>دادهٔ صفحهٔ راهنمای جامع یک دسته.</summary>
public sealed record CategoryGuideView(
    Guid CategoryId, string Name, string Slug, string? Intro, string? ImageName, CategoryFacts Facts,
    IReadOnlyList<(string Name, string Slug, int Count)> Brands,
    IReadOnlyList<SeoLandingLink> PriceBands,
    IReadOnlyList<SpecRange> Specs,
    IReadOnlyList<CategoryQuestion> Questions,
    IReadOnlyList<(string Name, string Slug)> SubCategories,
    IReadOnlyList<(string Title, string Slug)> Articles,
    IReadOnlyList<Guid> TopProducts)
{
    /// <summary>حداقل کالای فعال برای ایندکس شدن راهنما (دو برابر آستانهٔ دستهٔ کم‌محتوا).</summary>
    public const int MinProducts = 6;

    /// <summary>قابل ایندکس؟ حداقل ۶ کالا و دست‌کم دو بخش محتوایی واقعی (برند، بازهٔ قیمت، مشخصات، مقاله).</summary>
    public bool Indexable => Facts.Count >= MinProducts
                             && new[] { Brands.Count > 1, PriceBands.Count > 0, Specs.Count > 0, Articles.Count > 0 }.Count(x => x) >= 2;
}

/// <summary>دادهٔ صفحهٔ فرود مناسبت.</summary>
public sealed record OccasionView(SeoOccasion Occasion, string DateLabel, string Intro, IReadOnlyList<Guid> Products,
    IReadOnlyList<(string Name, string Slug)> Categories, IReadOnlyList<CategoryQuestion> Questions)
{
    /// <summary>حداقل کالای موجود برای ایندکس شدن صفحهٔ مناسبت.</summary>
    public const int MinProducts = 6;
    public bool Indexable => Products.Count >= MinProducts;
}

/// <summary>
/// سرویس صفحه‌های هاب سئو. محل استفاده: Pages/Glossary، Pages/Guide، Pages/Occasion و SitemapService.
/// </summary>
public interface ISeoHubService
{
    /// <summary>واژه‌نامه از مشخصات واقعی کالاهای فعال (کش ۶ ساعت).</summary>
    Task<GlossaryView> GlossaryAsync(CancellationToken cancellationToken = default);

    /// <summary>راهنمای جامع یک دسته یا null (دستهٔ ناموجود/غیرفعال/سیستمی). کش ۳۰ دقیقه.</summary>
    Task<CategoryGuideView?> GuideAsync(string categorySlug, CancellationToken cancellationToken = default);

    /// <summary>صفحهٔ مناسبت با نامک (مثال: «yalda») یا null. کش ۳۰ دقیقه.</summary>
    Task<OccasionView?> OccasionAsync(string slug, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISeoHubService"/>
public sealed class SeoHubService(FratakShopDbContext context, IMemoryCache cache, ICategoryService categories, ISeoLandingService landings) : ISeoHubService
{
    private const string GlossaryKey = "seo:hub:glossary";

    // ── ۴۲: واژه‌نامه ───────────────────────────────────────────────────

    public async Task<GlossaryView> GlossaryAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(GlossaryKey, out GlossaryView? cached) && cached is not null)
            return cached;

        var facts = await ReadFactsAsync(null, cancellationToken);
        var view = new GlossaryView(BuildGlossary(facts));
        cache.Set(GlossaryKey, view, TimeSpan.FromHours(6));
        return view;
    }

    /// <summary>
    /// ساخت اصطلاح‌ها از مشخصات خوانده‌شده (ایستا برای تست): فقط موضوع‌هایی که دست‌کم یک کالای واقعی دارند؛
    /// دستهٔ پیوند = دسته‌ای که بیشترین کالا با این مشخصه را دارد. ترتیب: پرکاربردترین اول.
    /// </summary>
    public static List<GlossaryTerm> BuildGlossary(IReadOnlyList<ProductFact> facts) =>
        facts.GroupBy(f => f.Fact.Topic)
            .Where(g => GlossaryCatalog.Terms.ContainsKey(g.Key))
            .Select(g =>
            {
                var term = GlossaryCatalog.Terms[g.Key];
                var top = g.GroupBy(f => (f.CategoryName, f.CategorySlug)).OrderByDescending(c => c.Select(x => x.ProductId).Distinct().Count()).First().Key;
                return new GlossaryTerm(term.Slug, term.Name, term.Definition, g.Select(f => f.ProductId).Distinct().Count(), top.CategoryName, top.CategorySlug);
            })
            .OrderByDescending(t => t.ProductCount).ThenBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    // ── ۴۳: راهنمای جامع دسته ───────────────────────────────────────────

    public async Task<CategoryGuideView?> GuideAsync(string categorySlug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(categorySlug) || categorySlug.Length > 200)
            return null;
        var key = "seo:hub:guide:" + categorySlug.Trim().ToLowerInvariant();
        if (cache.TryGetValue(key, out CategoryGuideView? cached) && cached is not null)
            return cached;

        var category = await context.Categories.AsNoTracking()
            .Where(c => c.Slug == categorySlug && c.IsActive && !c.IsSystem)
            .Select(c => new { c.Id, c.Name, c.Slug, c.Description, c.ImageName })
            .FirstOrDefaultAsync(cancellationToken);
        if (category is null)
            return null;

        var facts = await categories.GetCategoryFactsAsync(category.Id);
        var ids = await context.Categories.AsNoTracking().Where(c => c.ParentId == category.Id && c.IsActive && !c.IsSystem)
            .OrderBy(c => c.DisplayOrder).Select(c => new { c.Id, c.Name, c.Slug }).Take(20).ToListAsync(cancellationToken);
        var scope = ids.Select(c => c.Id).Append(category.Id).ToList();

        var brands = await context.Products.AsNoTracking()
            .Where(p => p.IsActive && scope.Contains(p.CategoryId) && p.BrandId != null && p.Brand!.IsActive)
            .GroupBy(p => new { p.Brand!.Name, p.Brand.Slug })
            .Select(g => new { g.Key.Name, g.Key.Slug, Count = g.Count() })
            .OrderByDescending(b => b.Count).Take(12)
            .ToListAsync(cancellationToken);

        var bands = await landings.PriceLandingsAsync(category.Id, category.Slug, category.Name, cancellationToken);
        var specFacts = await ReadFactsAsync(scope, cancellationToken);
        var siteName = await context.StoreSettings.AsNoTracking().Select(x => x.SiteName).FirstOrDefaultAsync(cancellationToken) ?? "";
        var questions = facts.Count >= CategoryService.ThinCategoryProducts ? CategoryFaq.Build(category.Name, facts, siteName) : [];

        var now = DateTimeOffset.UtcNow;
        var articles = await context.BlogPlanItems.AsNoTracking()
            .Where(i => i.Status == Fratak_Shop.Data.Entities.Smart.BlogPlanStatus.Published && i.CategoryId != null && scope.Contains(i.CategoryId.Value) && i.PostId != null)
            .Join(context.BlogPosts.Where(p => p.IsPublished && (p.PublishedAt == null || p.PublishedAt <= now)), i => i.PostId!.Value, p => p.Id, (i, p) => new { p.Title, p.Slug, p.PublishedAt })
            .OrderByDescending(p => p.PublishedAt).Take(6)
            .ToListAsync(cancellationToken);

        var top = await context.Products.AsNoTracking()
            .Where(p => p.IsActive && scope.Contains(p.CategoryId) && p.StockQuantity > 0 && (p.PublishedAt == null || p.PublishedAt <= now))
            .OrderByDescending(p => p.SalesCount).ThenByDescending(p => p.ViewCount)
            .Select(p => p.Id).Take(8)
            .ToListAsync(cancellationToken);

        var view = new CategoryGuideView(category.Id, category.Name, category.Slug, Intro(category.Description), category.ImageName, facts,
            brands.Select(b => (b.Name, b.Slug, b.Count)).ToList(), bands, BuildSpecRanges(specFacts), questions,
            ids.Select(c => (c.Name, c.Slug)).ToList(), articles.Select(a => (a.Title, a.Slug)).ToList(), top);
        cache.Set(key, view, TimeSpan.FromMinutes(30));
        return view;
    }

    /// <summary>
    /// بازهٔ مشخصات عددی دسته (ایستا برای تست): برای هر موضوع با جهت معنادار (باتری، حافظه، وزن …) و حداقل ۲ کالا،
    /// کمترین و بیشترین مقدار با متن اصلی (همراه واحد). کلیدهای با واحد متفاوت (ساعت/میلی‌آمپر) جدا شمرده می‌شوند.
    /// مثال: [«شارژدهی: 10 ساعت»، «شارژدهی: 40 ساعت»] ← («شارژدهی»، «10 ساعت»، «40 ساعت»، ۲).
    /// </summary>
    public static List<SpecRange> BuildSpecRanges(IReadOnlyList<ProductFact> facts) =>
        facts.Where(f => f.Fact.Number is not null && f.Fact.Topic != SpecTopic.Generic)
            .GroupBy(f => f.Fact.NormalizedKey)
            .Select(g => new { Items = g.DistinctBy(f => f.ProductId).ToList(), g.First().Fact })
            .Where(g => g.Items.Count >= 2 && g.Items.Min(i => i.Fact.Number) < g.Items.Max(i => i.Fact.Number))
            .OrderByDescending(g => SpecKnowledge.Weight(g.Fact.Topic)).ThenByDescending(g => g.Items.Count)
            .Take(8)
            .Select(g =>
            {
                var low = g.Items.MinBy(i => i.Fact.Number)!.Fact;
                var high = g.Items.MaxBy(i => i.Fact.Number)!.Fact;
                var slug = GlossaryCatalog.Terms.TryGetValue(g.Fact.Topic, out var term) ? term.Slug : null;
                return new SpecRange(g.Fact.Key, low.Value, high.Value, g.Items.Count, slug);
            })
            .ToList();

    // ── ۴۴: صفحهٔ مناسبت ────────────────────────────────────────────────

    public async Task<OccasionView?> OccasionAsync(string slug, CancellationToken cancellationToken = default)
    {
        var today = IranClock.Today(DateTimeOffset.UtcNow);
        var occasion = SeoCalendar.Find(today, slug);
        if (occasion is null)
            return null;
        var key = $"seo:hub:occasion:{occasion.Slug}:{today:yyyyMMdd}";
        if (cache.TryGetValue(key, out OccasionView? cached) && cached is not null)
            return cached;

        var now = DateTimeOffset.UtcNow;
        var query = context.Products.AsNoTracking()
            .Where(p => p.IsActive && p.StockQuantity > 0 && (p.PublishedAt == null || p.PublishedAt <= now) && !p.Category.IsSystem);
        // بلک فرایدی فقط کالاهای دارای تخفیف فعال؛ بقیهٔ مناسبت‌ها پرفروش‌ها (کالای تخفیف‌دار جلوتر).
        var discounted = query.Where(p => p.DiscountPrice != null && p.DiscountPrice < p.Price
                                          && (p.DiscountStartAt == null || p.DiscountStartAt <= now) && (p.DiscountEndAt == null || p.DiscountEndAt >= now));
        var source = occasion.Slug == "black-friday" ? discounted : query;
        var rows = await source
            .OrderByDescending(p => p.DiscountPrice != null && p.DiscountPrice < p.Price)
            .ThenByDescending(p => p.SalesCount).ThenByDescending(p => p.ViewCount)
            .Select(p => new { p.Id, p.CategoryId, CategoryName = p.Category.Name, CategorySlug = p.Category.Slug })
            .Take(60)
            .ToListAsync(cancellationToken);
        // تنوع: از هر دسته حداکثر ۴ کالا تا صفحه فقط یک نوع کالا نباشد.
        var picks = rows.GroupBy(r => r.CategoryId).SelectMany(g => g.Take(4)).Take(24).ToList();
        var cats = picks.GroupBy(r => (r.CategoryName, r.CategorySlug)).OrderByDescending(g => g.Count()).Take(6).Select(g => g.Key).ToList();

        var dateLabel = ShamsiLabel(occasion.Date);
        var view = new OccasionView(occasion, dateLabel, OccasionIntro(occasion, dateLabel, picks.Count),
            picks.Select(p => p.Id).ToList(), cats, OccasionQuestions(occasion, dateLabel, picks.Count));
        cache.Set(key, view, TimeSpan.FromMinutes(30));
        return view;
    }

    /// <summary>
    /// معرفی صفحهٔ مناسبت (ایستا برای تست، بدون ادعای ساختگی).
    /// مثال: («شب یلدا»، ۳۰ روز، «۳۰ آذر»، ۱۲) ← «تا شب یلدا (۳۰ آذر) ۳۰ روز مانده است. ۱۲ کالای موجود را برای خرید یا هدیهٔ این مناسبت جمع کرده‌ایم …».
    /// </summary>
    public static string OccasionIntro(SeoOccasion occasion, string dateLabel, int products)
    {
        var when = occasion.DaysLeft switch
        {
            0 => $"امروز {occasion.Name} است.",
            1 => $"فردا {occasion.Name} است ({dateLabel}).",
            _ => $"تا {occasion.Name} ({dateLabel}) {PersianText.Digits(occasion.DaysLeft)} روز مانده است.",
        };
        var list = products > 0
            ? $" {PersianText.Digits(products)} کالای موجود را برای خرید یا هدیهٔ این مناسبت یک‌جا آورده‌ایم؛ قیمت‌ها همان قیمت روز فروشگاه است."
            : " به‌زودی پیشنهادهای این مناسبت در همین صفحه قرار می‌گیرد.";
        return when + list;
    }

    /// <summary>پرسش‌های متداول صفحهٔ مناسبت (فقط پاسخ‌های قطعی از داده).</summary>
    public static List<CategoryQuestion> OccasionQuestions(SeoOccasion occasion, string dateLabel, int products)
    {
        var list = new List<CategoryQuestion>
        {
            new($"{occasion.Name} چه روزی است؟", occasion.DaysLeft == 0
                ? $"{occasion.Name} امروز ({dateLabel}) است."
                : $"{occasion.Name} امسال {dateLabel} است؛ یعنی {PersianText.Digits(occasion.DaysLeft)} روز دیگر."),
            new("زمان ارسال سفارش را از کجا بدانم؟", "زمان تقریبی ارسال در صفحهٔ هر کالا و هنگام ثبت سفارش نمایش داده می‌شود؛ برای رسیدن به‌موقع، چند روز زودتر سفارش دهید."),
        };
        if (products > 0)
            list.Add(new($"چند پیشنهاد برای {occasion.Name} وجود دارد؟", $"در حال حاضر {PersianText.Digits(products)} کالای موجود در این صفحه قرار دارد و فهرست با تغییر موجودی خودکار به‌روز می‌شود."));
        return list;
    }

    /// <summary>برچسب شمسی کوتاه. مثال: 2026-12-21 ← «۳۰ آذر».</summary>
    public static string ShamsiLabel(DateOnly date)
    {
        var calendar = new PersianCalendar();
        var moment = date.ToDateTime(TimeOnly.MinValue);
        string[] months = ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];
        return $"{PersianText.Digits(calendar.GetDayOfMonth(moment))} {months[calendar.GetMonth(moment) - 1]}";
    }

    // ── مشترک ───────────────────────────────────────────────────────────

    /// <summary>یک مشخصهٔ خوانده‌شدهٔ یک کالا به‌همراه دسته‌اش.</summary>
    public sealed record ProductFact(Guid ProductId, string CategoryName, string CategorySlug, SpecFact Fact);

    /// <summary>خواندن مشخصات متنی کوتاه کالاهای فعال (اختیاری: فقط چند دسته)، حداکثر ۲۰٬۰۰۰ ردیف.</summary>
    private async Task<List<ProductFact>> ReadFactsAsync(IReadOnlyCollection<Guid>? scope, CancellationToken cancellationToken)
    {
        var query = context.ProductFeatures.AsNoTracking()
            .Where(f => f.Product.IsActive && !f.Product.Category.IsSystem && f.ValueType != FeatureValueType.Object
                        && f.Key.Length <= 40 && f.Value.Length <= 60);
        if (scope is not null)
            query = query.Where(f => scope.Contains(f.Product.CategoryId));
        var rows = await query
            .Select(f => new { f.ProductId, f.Key, f.Value, CategoryName = f.Product.Category.Name, CategorySlug = f.Product.Category.Slug })
            .Take(20_000)
            .ToListAsync(cancellationToken);
        var list = new List<ProductFact>(rows.Count);
        foreach (var row in rows)
        {
            if (SpecKnowledge.Read(new SeoFeature(row.Key, row.Value)) is { } fact)
                list.Add(new ProductFact(row.ProductId, row.CategoryName, row.CategorySlug, fact));
        }
        return list;
    }

    /// <summary>پاراگراف اول توضیح دسته (متن ساده، حداکثر ۴۰۰ نویسه) یا null.</summary>
    private static string? Intro(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;
        var text = System.Text.RegularExpressions.Regex.Replace(PersianText.HtmlToText(description), @"\s+", " ").Trim();
        return text.Length < 40 ? null : PersianText.Truncate(text, 400);
    }
}

/// <summary>
/// دور ۲۴ (پیشنهاد ۴۲): تعریف ساده و درست اصطلاحات فنی رایج (دانش عمومی، بدون ادعای تبلیغاتی).
/// کلید = موضوع <see cref="SpecTopic"/> تا اصطلاح فقط وقتی نمایش داده شود که کالای واقعی با آن مشخصه وجود دارد.
/// </summary>
public static class GlossaryCatalog
{
    public sealed record Entry(string Slug, string Name, string Definition);

    public static readonly IReadOnlyDictionary<SpecTopic, Entry> Terms = new Dictionary<SpecTopic, Entry>
    {
        [SpecTopic.Battery] = new("battery-life", "شارژدهی (عمر باتری)",
            "مدت زمانی که دستگاه با یک بار شارژ کامل کار می‌کند؛ برای هدفون و ساعت معمولاً بر حسب ساعت و برای گوشی و پاوربانک با ظرفیت باتری (میلی‌آمپر ساعت، mAh) بیان می‌شود. عدد بزرگ‌تر یعنی نیاز کمتر به شارژ، ولی مصرف واقعی به نوع استفاده هم بستگی دارد."),
        [SpecTopic.Bluetooth] = new("bluetooth-version", "نسخهٔ بلوتوث",
            "استاندارد ارتباط بی‌سیم کوتاه‌برد. نسخه‌های جدیدتر (۵ به بالا) برد بیشتر، اتصال پایدارتر و مصرف انرژی کمتری دارند؛ برای بهره بردن از این مزایا هر دو دستگاه باید نسخهٔ جدید را پشتیبانی کنند."),
        [SpecTopic.NoiseCancel] = new("active-noise-cancelling", "حذف نویز فعال (ANC)",
            "فناوری‌ای که با میکروفون صدای محیط را می‌شنود و موج صوتی مخالف آن را پخش می‌کند تا صداهای یکنواخت مثل موتور و کولر کمتر شنیده شود. با حذف نویز غیرفعال که فقط با پوشش فیزیکی گوش انجام می‌شود فرق دارد."),
        [SpecTopic.Water] = new("ip-rating", "استاندارد IP (مقاومت در برابر آب و گرد و غبار)",
            "کدی دو رقمی مثل IP67: رقم اول میزان محافظت در برابر گرد و غبار (۰ تا ۶) و رقم دوم مقاومت در برابر آب (۰ تا ۸) است. حرف X یعنی آن مورد آزمایش نشده؛ مثلاً IPX4 در برابر پاشش آب و عرق مقاوم است، نه غوطه‌وری."),
        [SpecTopic.Weight] = new("weight", "وزن",
            "وزن خود دستگاه بدون جعبه و لوازم جانبی. برای کالاهایی که همراه دارید یا مدت طولانی استفاده می‌کنید (هدفون، ساعت، گوشی) در راحتی اثر مستقیم دارد."),
        [SpecTopic.Port] = new("charging-port", "درگاه شارژ (USB-C، لایتنینگ، میکرو USB)",
            "نوع ورودی شارژ و انتقال داده. USB-C دوطرفه است، شارژ و انتقال سریع‌تری را پشتیبانی می‌کند و امروزه استاندارد رایج است؛ میکرو USB نسل قدیمی‌تر است."),
        [SpecTopic.WirelessCharge] = new("wireless-charging", "شارژ بی‌سیم",
            "شارژ شدن با گذاشتن دستگاه روی پد شارژر (معمولاً استاندارد Qi) بدون اتصال کابل؛ راحت است ولی معمولاً از شارژ با کابل کندتر است."),
        [SpecTopic.FastCharge] = new("fast-charging", "شارژ سریع",
            "پشتیبانی دستگاه از توان ورودی بالاتر (بر حسب وات) برای پر شدن سریع‌تر باتری. برای استفاده از آن به شارژر و کابل سازگار با همان استاندارد نیاز است."),
        [SpecTopic.Storage] = new("internal-storage", "حافظهٔ داخلی",
            "فضای ذخیرهٔ دائمی برای سیستم‌عامل، برنامه‌ها، عکس و فایل‌ها (بر حسب گیگابایت). بخشی از آن را سیستم‌عامل اشغال می‌کند، پس فضای قابل استفاده کمتر از عدد اسمی است."),
        [SpecTopic.Ram] = new("ram", "حافظهٔ رم (RAM)",
            "حافظهٔ موقتی که برنامه‌های در حال اجرا در آن نگه داشته می‌شوند. رم بیشتر یعنی جابه‌جایی روان‌تر بین برنامه‌ها و بسته نشدن آن‌ها در پس‌زمینه."),
        [SpecTopic.ScreenSize] = new("screen-size", "اندازهٔ صفحه‌نمایش",
            "طول قطر صفحه بر حسب اینچ (هر اینچ حدود ۲٫۵۴ سانتی‌متر). صفحهٔ بزرگ‌تر برای تماشا و کار راحت‌تر است ولی ابعاد و وزن دستگاه را هم بیشتر می‌کند."),
        [SpecTopic.Refresh] = new("refresh-rate", "نرخ نوسازی (رفرش ریت)",
            "تعداد دفعاتی که تصویر صفحه در هر ثانیه تازه می‌شود (هرتز). ۹۰ یا ۱۲۰ هرتز حرکت و پیمایش را نرم‌تر از ۶۰ هرتز نشان می‌دهد و کمی باتری بیشتری مصرف می‌کند."),
        [SpecTopic.Resolution] = new("resolution", "رزولوشن (وضوح تصویر)",
            "تعداد پیکسل‌های تصویر در طول و عرض (مثلاً ۱۹۲۰ در ۱۰۸۰ یا Full HD). وضوح بیشتر در اندازهٔ یکسان یعنی جزئیات دقیق‌تر و متن خواناتر."),
        [SpecTopic.Camera] = new("camera-megapixel", "دوربین (مگاپیکسل)",
            "مگاپیکسل تعداد پیکسل‌های عکس را نشان می‌دهد، ولی کیفیت عکس بیشتر به اندازهٔ حسگر، لنز و پردازش تصویر بستگی دارد تا فقط به عدد مگاپیکسل."),
        [SpecTopic.Processor] = new("processor", "پردازنده (تراشه)",
            "مغز دستگاه که سرعت اجرای برنامه‌ها و بازی‌ها و مصرف انرژی را تعیین می‌کند. تعداد هسته و فرکانس به‌تنهایی معیار کامل نیست؛ نسل و معماری تراشه هم مهم است."),
        [SpecTopic.Os] = new("operating-system", "سیستم‌عامل",
            "نرم‌افزار پایه‌ای که دستگاه را اداره می‌کند (مثل اندروید، iOS یا ویندوز) و تعیین می‌کند چه برنامه‌هایی روی آن نصب می‌شود."),
        [SpecTopic.Microphones] = new("microphone", "میکروفون و کاهش نویز مکالمه",
            "تعداد میکروفون‌ها و پردازش صدا در کیفیت مکالمه اثر دارد؛ چند میکروفون با کاهش نویز مکالمه (ENC) صدای شما را در محیط شلوغ واضح‌تر به طرف مقابل می‌رساند."),
        [SpecTopic.Power] = new("power-watt", "توان (وات)",
            "مقدار انرژی مصرفی یا خروجی دستگاه در واحد زمان. در شارژرها توان بیشتر یعنی شارژ سریع‌تر (اگر دستگاه پشتیبانی کند) و در اسپیکرها یعنی صدای بلندتر."),
        [SpecTopic.Material] = new("body-material", "جنس بدنه",
            "متریال ساخت دستگاه (پلاستیک، آلومینیوم، شیشه، فولاد ضدزنگ و …) که در دوام، وزن و حس در دست اثر دارد."),
        [SpecTopic.Compatibility] = new("compatibility", "سازگاری",
            "دستگاه‌ها یا سیستم‌عامل‌هایی که کالا با آن‌ها کار می‌کند؛ پیش از خرید لوازم جانبی، سازگاری با مدل دستگاه خودتان را بررسی کنید."),
    };
}
