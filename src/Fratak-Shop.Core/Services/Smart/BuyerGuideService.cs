using Fratak_Shop.Core.DTOs.Comments.Query;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>میانهٔ زمان رسیدن سفارش به یک استان (C2). مثال: («اصفهان»، ۳، ۲۷ نمونه).</summary>
public sealed record ProvinceDelivery(string Province, int MedianDays, int Samples);

/// <summary>یک ردیف «سازگاری» (C9). مثال: («درگاه شارژ»، «USB-C»، «با بیشتر شارژرهای جدید گوشی سازگار است»).</summary>
public sealed record CompatibilityItem(string Key, string Value, string? Note);

/// <summary>لینک یک مقالهٔ راهنما (C23).</summary>
public sealed record GuideLink(string Title, string Slug);

/// <summary>مقایسهٔ سریع با نزدیک‌ترین محصول مشابه (C7).</summary>
public sealed record QuickCompare(string OtherName, string OtherSlug, decimal OtherPrice, IReadOnlyList<(string Key, string Mine, string Other, int Winner)> Rows);

/// <summary>جمع‌بندی نظرهای خود سایت (C10): توزیع ستاره‌ها و نکته‌های پرتکرار.</summary>
public sealed record ReviewDigest(int Count, double Average, IReadOnlyList<int> Stars, IReadOnlyList<InsightPoint> Pros, IReadOnlyList<InsightPoint> Cons);

/// <summary>خط ترب (C6) – فقط وقتی نمایش داده می‌شود که قیمت ما واقعاً کمترین باشد و داده تازه (≤۴۸ ساعت) باشد.</summary>
public sealed record TorobLine(int SellerCount, DateTimeOffset CheckedAt);

/// <summary>
/// همهٔ «اطلاعات کلیدی خرید» صفحهٔ محصول (C1–C17) در یک بسته؛ هر بخشی که دادهٔ واقعی ندارد خالی می‌ماند و نمایش داده نمی‌شود.
/// </summary>
public sealed class BuyerGuide
{
    public ProductInsightView? Insight { get; init; }

    /// <summary>ویژگی‌های برجسته (C4) – از خلاصهٔ خرید تأییدشده یا مستقیم از مشخصات.</summary>
    public List<string> Highlights { get; init; } = [];

    /// <summary>کمترین قیمت ۳۰ روز اخیر (C5) – فقط اگر دست‌کم ۷ روز سابقه و تغییر قیمت وجود داشته باشد.</summary>
    public decimal? LowestIn30Days { get; init; }

    /// <summary>قیمت فعلی همان کمترین قیمت ۳۰ روز است.</summary>
    public bool IsLowestIn30Days { get; init; }

    public TorobLine? Torob { get; init; }

    public List<ProvinceDelivery> Delivery { get; init; } = [];

    public List<CompatibilityItem> Compatibility { get; init; } = [];

    public List<GuideLink> Guides { get; init; } = [];

    public QuickCompare? Compare { get; init; }

    public ReviewDigest? Digest { get; init; }

    /// <summary>پرسش‌وپاسخ‌های پاسخ‌داده‌شده توسط فروشگاه (C11) – برای بخش «پرسش‌های رایج» و اسکیمای FAQ.</summary>
    public List<InsightFaq> QaFaq { get; init; } = [];

    /// <summary>مهلت بازگشت کالا (C17).</summary>
    public int ReturnDays { get; init; }
}

/// <summary>کارت سادهٔ محصول برای کدهای کوتاه مقاله (B16/C22).</summary>
public sealed record ShortcodeProduct(string Slug, string Name, string? ImageName, decimal Price, decimal FinalPrice, bool InStock, double Rating, int Reviews);

/// <summary>یک گزینهٔ «کمکم کن انتخاب کنم» (C8).</summary>
public sealed record QuizOption(string Key, string Label);

/// <summary>نتیجهٔ پرسشنامهٔ انتخاب (C8).</summary>
public sealed record QuizResult(IReadOnlyList<decimal> Budgets, IReadOnlyList<QuizOption> Priorities, IReadOnlyList<Guid> Picks, string? Explanation);

/// <summary>
/// سرویس «نگاه خریدار» (C1–C17، C22، C23): هر اطلاعاتی که خریدار برای تصمیم لازم دارد را از دادهٔ واقعی فروشگاه می‌سازد.
/// <para>بودجهٔ کارایی: صفحهٔ محصول سقف ۴۵ پرس‌وجو دارد؛ این سرویس حداکثر ۵ پرس‌وجوی کوچک اضافه می‌کند و آمار سنگین
/// (زمان تحویل استان‌ها، نقشهٔ مقاله‌های راهنما) را برای همهٔ محصولات یک‌جا و در کش سراسری نگه می‌دارد.</para>
/// <para>بدون الگوی فریبنده (C18): هیچ شمارش معکوس جعلی، «فقط ۲ نفر دیگر…» یا قیمت خط‌خوردهٔ ساختگی ساخته نمی‌شود؛ هر عدد واقعی است.</para>
/// <para>محل استفاده: صفحهٔ محصول (Product.cshtml)، صفحهٔ مقاله (کدهای کوتاه)، صفحهٔ دسته (پرسشنامهٔ انتخاب).</para>
/// </summary>
public interface IBuyerGuideService
{
    Task<BuyerGuide> ForProductAsync(ProductDto product, IReadOnlyList<CommentDto> reviews, IReadOnlyList<CommentDto> questions, IReadOnlyList<ProductFilterData> related, CancellationToken cancellationToken = default);

    Task<List<ShortcodeProduct>> CardsBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken = default);

    Task<QuizResult> QuizAsync(Guid categoryId, decimal? maxPrice, string? priority, CancellationToken cancellationToken = default);

    /// <summary>میانهٔ زمان تحویل همهٔ استان‌ها (کش ۶ ساعته).</summary>
    Task<List<ProvinceDelivery>> DeliveryStatsAsync(CancellationToken cancellationToken = default);
}

public sealed class BuyerGuideService(FratakShopDbContext context, IMemoryCache cache, Settings.IStoreSettingService settings) : IBuyerGuideService
{
    private const string DeliveryKey = "smart:delivery-stats";
    private const string GuidesKey = "smart:guides-map";

    public async Task<BuyerGuide> ForProductAsync(ProductDto product, IReadOnlyList<CommentDto> reviews, IReadOnlyList<CommentDto> questions,
        IReadOnlyList<ProductFilterData> related, CancellationToken cancellationToken = default)
    {
        var insightRow = await context.ProductInsights
            .FirstOrDefaultAsync(i => i.ProductId == product.Id && i.Status == ProductInsightStatus.Approved, cancellationToken);
        var insight = insightRow is null ? null : ProductInsightService.ToView(insightRow);
        var features = product.Features.Select(f => new SeoFeature(f.Key, f.Value)).ToList();
        var facts = features.Select(SpecKnowledge.Read).Where(f => f is not null).Select(f => f!).ToList();

        var highlights = insight?.Highlights is { Count: > 0 } h ? h : Highlights(facts);
        var (lowest, isLowest) = await LowestIn30DaysAsync(product, cancellationToken);
        var torob = await TorobAsync(product, cancellationToken);
        var delivery = await DeliveryStatsAsync(cancellationToken);
        var guides = await GuidesAsync(product, cancellationToken);
        var compare = await CompareAsync(product, features, related, cancellationToken);
        var store = await settings.GetAsync(cancellationToken);

        return new BuyerGuide
        {
            Insight = insight is { HasContent: true } ? insight : null,
            Highlights = highlights,
            LowestIn30Days = lowest,
            IsLowestIn30Days = isLowest,
            Torob = torob,
            Delivery = delivery,
            Compatibility = Compatibility(facts),
            Guides = guides,
            Compare = compare,
            Digest = Digest(reviews),
            QaFaq = QaFaq(questions),
            ReturnDays = Math.Max(0, store.ReturnWindowDays),
        };
    }

    /// <summary>ویژگی‌های برجسته از مشخصات (حداکثر ۵، پرارزش‌ترین اول).</summary>
    public static List<string> Highlights(IReadOnlyList<SpecFact> facts) =>
        facts.OrderByDescending(f => SpecKnowledge.Weight(f.Topic))
            .Select(SpecKnowledge.Highlight).Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h!)
            .Distinct(StringComparer.Ordinal).Take(5).ToList();

    /// <summary>
    /// کمترین قیمت ۳۰ روز (C5). قانون صداقت: فقط اگر دست‌کم ۷ روز سابقه هست و قیمت در این بازه واقعاً تغییر کرده؛
    /// وگرنه «کمترین قیمت ۳۰ روز» گمراه‌کننده است و نمایش داده نمی‌شود.
    /// </summary>
    private async Task<(decimal? Lowest, bool IsLowest)> LowestIn30DaysAsync(ProductDto product, CancellationToken cancellationToken)
    {
        if (!product.IsInStock)
            return (null, false);
        var since = DateTimeOffset.UtcNow.AddDays(-30);
        var stats = await context.ProductPriceHistories
            .Where(h => h.ProductId == product.Id && h.CreatedAt >= since && h.IsInStock)
            .GroupBy(h => h.ProductId)
            .Select(g => new { Min = g.Min(h => h.FinalPrice), Max = g.Max(h => h.FinalPrice), First = g.Min(h => h.CreatedAt) })
            .FirstOrDefaultAsync(cancellationToken);
        return Evaluate(product.FinalPrice, stats?.Min, stats?.Max, stats?.First, DateTimeOffset.UtcNow);
    }

    /// <summary>منطق خالص C5 (آزمون‌پذیر). مثال: سابقه از ۱۰ روز پیش، کمینه ۹۰۰ و بیشینه ۱۰۰۰، قیمت فعلی ۹۰۰ ← (۹۰۰، true).</summary>
    public static (decimal? Lowest, bool IsLowest) Evaluate(decimal current, decimal? min, decimal? max, DateTimeOffset? first, DateTimeOffset now)
    {
        if (min is null || max is null || first is null || now - first.Value < TimeSpan.FromDays(7) || max.Value <= min.Value)
            return (null, false);
        var lowest = Math.Min(min.Value, current);
        return (lowest, current <= lowest);
    }

    /// <summary>
    /// خط ترب (C6): فقط وقتی قیمت ما ≤ کمترین قیمت فروشندگان ترب است و بررسی حداکثر ۴۸ ساعت پیش انجام شده.
    /// (هرگز «ارزان‌تر از ترب» دروغ گفته نمی‌شود.)
    /// </summary>
    private async Task<TorobLine?> TorobAsync(ProductDto product, CancellationToken cancellationToken)
    {
        if (!product.IsInStock)
            return null;
        var snap = await context.ProductCompetitorSnapshots
            .Where(s => s.ProductId == product.Id)
            .OrderByDescending(s => s.UpdatedAt)
            .Select(s => new { s.TorobLowestPrice, s.TorobSellerCount, s.UpdatedAt })
            .FirstOrDefaultAsync(cancellationToken);
        return snap is null ? null : TorobRule(product.FinalPrice, snap.TorobLowestPrice, snap.TorobSellerCount, snap.UpdatedAt, DateTimeOffset.UtcNow);
    }

    /// <summary>منطق خالص C6. مثال: قیمت ما ۹۵۰، کمترین ترب ۱۰۰۰، ۶ فروشنده، بررسی ۳ ساعت پیش ← نمایش.</summary>
    public static TorobLine? TorobRule(decimal ourPrice, decimal? torobLowest, int sellers, DateTimeOffset checkedAt, DateTimeOffset now)
    {
        if (torobLowest is not > 0 || sellers < 2 || now - checkedAt > TimeSpan.FromHours(48) || ourPrice <= 0 || ourPrice > torobLowest.Value)
            return null;
        return new TorobLine(sellers, checkedAt);
    }

    public async Task<List<ProvinceDelivery>> DeliveryStatsAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(DeliveryKey, out List<ProvinceDelivery>? cached) && cached is not null)
            return cached;
        var since = DateTimeOffset.UtcNow.AddDays(-180);
        var rows = await context.Orders
            .Where(o => o.Status == OrderStatus.Delivered && o.PaidAt != null && o.DeliveredAt != null && o.DeliveredAt >= since)
            .OrderByDescending(o => o.DeliveredAt)
            .Select(o => new { o.ShippingProvince, PaidAt = o.PaidAt!.Value, DeliveredAt = o.DeliveredAt!.Value })
            .Take(6000)
            .ToListAsync(cancellationToken);
        var result = Medians(rows.Select(r => (r.ShippingProvince, r.PaidAt, r.DeliveredAt)));
        cache.Set(DeliveryKey, result, TimeSpan.FromHours(6));
        return result;
    }

    /// <summary>
    /// منطق خالص C2: میانهٔ روزهای بین پرداخت و تحویل برای هر استان با دست‌کم ۵ نمونه (داده‌های پرت بالای ۶۰ روز حذف).
    /// مثال: تهران [۱، ۲، ۲، ۳، ۹] ← ۲ روز.
    /// </summary>
    public static List<ProvinceDelivery> Medians(IEnumerable<(string Province, DateTimeOffset PaidAt, DateTimeOffset DeliveredAt)> rows) =>
        rows.Where(r => !string.IsNullOrWhiteSpace(r.Province) && r.DeliveredAt >= r.PaidAt && (r.DeliveredAt - r.PaidAt).TotalDays <= 60)
            .GroupBy(r => PersianText.Clean(r.Province), StringComparer.Ordinal)
            .Select(g =>
            {
                var days = g.Select(r => (int)Math.Ceiling((r.DeliveredAt - r.PaidAt).TotalDays)).OrderBy(d => d).ToList();
                return new ProvinceDelivery(g.Key, Math.Max(0, days[days.Count / 2]), days.Count);
            })
            .Where(p => p.Samples >= 5)
            .OrderBy(p => p.Province, StringComparer.Ordinal)
            .ToList();

    /// <summary>مشخصات «سازگاری» (C9): درگاه، بلوتوث، سیستم‌عامل، و ردیف‌هایی که خودشان «سازگار با …» هستند.</summary>
    public static List<CompatibilityItem> Compatibility(IReadOnlyList<SpecFact> facts) =>
        facts.Where(f => f.Topic is SpecTopic.Compatibility or SpecTopic.Port or SpecTopic.Bluetooth or SpecTopic.Os)
            .Select(f => new CompatibilityItem(f.Key, f.Value, f.Topic switch
            {
                SpecTopic.Port when f.Value.Contains("type-c", StringComparison.OrdinalIgnoreCase) || f.Value.Contains("usb-c", StringComparison.OrdinalIgnoreCase)
                    => "با شارژر و کابل‌های USB-C (بیشتر گوشی‌ها و لپ‌تاپ‌های جدید) کار می‌کند.",
                SpecTopic.Bluetooth when f.Number is >= 5 => "با گوشی‌ها و لپ‌تاپ‌هایی که بلوتوث دارند وصل می‌شود؛ نسخهٔ ۵ به بعد پایدارتر است.",
                SpecTopic.Bluetooth => "با دستگاه‌های دارای بلوتوث وصل می‌شود.",
                _ => null,
            }))
            .DistinctBy(c => c.Key).Take(6).ToList();

    /// <summary>
    /// جمع‌بندی نظرهای خود سایت (C10) – فقط با دست‌کم ۳ نظر امتیازدار. نکته‌ها با همان تحلیل خلاصهٔ خرید و حداقل ۲ تکرار.
    /// </summary>
    public static ReviewDigest? Digest(IReadOnlyList<CommentDto> reviews)
    {
        var rated = reviews.Where(r => r.Rating is >= 1 and <= 5).ToList();
        if (rated.Count < 3)
            return null;
        var stars = Enumerable.Range(1, 5).Select(s => rated.Count(r => r.Rating == s)).ToList();
        var sources = reviews.Select(r => new SourceReview("site", r.Title, r.Content, r.Rating, [], [], r.IsRecommended)).ToList();
        var (pros, cons) = ReviewSynthesis.ExtractPoints(sources, 2, 4);
        return new ReviewDigest(rated.Count, Math.Round(rated.Average(r => (double)r.Rating!.Value), 1), stars, pros, cons);
    }

    /// <summary>پرسش‌هایی که فروشگاه پاسخ داده (C11) – حداکثر ۶، پاسخ کوتاه‌شده.</summary>
    public static List<InsightFaq> QaFaq(IReadOnlyList<CommentDto> questions) =>
        questions
            .Select(q => (Q: q, A: q.Replies.Where(r => r.IsStaff).OrderBy(r => r.CreatedAt).FirstOrDefault()))
            .Where(x => x.A is not null && x.Q.Content.Length >= 5)
            .OrderByDescending(x => x.Q.HelpfulCount).ThenByDescending(x => x.Q.CreatedAt)
            .Take(6)
            .Select(x => new InsightFaq(PersianText.Truncate(PersianText.Clean(x.Q.Content), 200), PersianText.Truncate(PersianText.Clean(x.A!.Content), 500)))
            .ToList();

    private async Task<List<GuideLink>> GuidesAsync(ProductDto product, CancellationToken cancellationToken)
    {
        var map = await cache.GetOrCreateAsync(GuidesKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            var now = DateTimeOffset.UtcNow;
            var posts = await context.BlogPosts
                .Where(p => p.IsPublished && (p.PublishedAt == null || p.PublishedAt <= now) && p.Content.Contains("[[product:"))
                .OrderByDescending(p => p.PublishedAt).Take(150)
                .Select(p => new { p.Id, p.Title, p.Slug, p.Content })
                .ToListAsync(cancellationToken);
            var bySlug = new Dictionary<string, List<GuideLink>>(StringComparer.OrdinalIgnoreCase);
            foreach (var post in posts)
            {
                foreach (var slug in BlogShortcodes.ProductSlugs(post.Content))
                {
                    if (!bySlug.TryGetValue(slug, out var list))
                        bySlug[slug] = list = [];
                    if (list.Count < 3 && list.All(l => l.Slug != post.Slug))
                        list.Add(new GuideLink(post.Title, post.Slug));
                }
            }
            var planned = await context.BlogPlanItems
                .Where(i => i.Status == BlogPlanStatus.Published && i.CategoryId != null && i.PostId != null)
                .Join(context.BlogPosts.Where(p => p.IsPublished), i => i.PostId!.Value, p => p.Id, (i, p) => new { i.CategoryId, p.Title, p.Slug })
                .Take(300).ToListAsync(cancellationToken);
            var byCategory = planned.GroupBy(p => p.CategoryId!.Value)
                .ToDictionary(g => g.Key, g => g.Select(p => new GuideLink(p.Title, p.Slug)).Take(3).ToList());
            return (bySlug, byCategory);
        });
        var result = new List<GuideLink>();
        if (map.bySlug.TryGetValue(product.Slug, out var direct))
            result.AddRange(direct);
        if (map.byCategory.TryGetValue(product.CategoryId, out var cat))
            result.AddRange(cat.Where(c => result.All(r => r.Slug != c.Slug)));
        return result.Take(3).ToList();
    }

    private async Task<QuickCompare?> CompareAsync(ProductDto product, List<SeoFeature> features, IReadOnlyList<ProductFilterData> related, CancellationToken cancellationToken)
    {
        if (features.Count < 2)
            return null;
        var candidates = related.Where(r => r.CategoryId == product.CategoryId && r.IsInStock && r.Id != product.Id).Take(3).Select(r => r.Id).ToList();
        if (candidates.Count == 0)
            return null;
        var otherFeatures = await context.ProductFeatures.Where(f => candidates.Contains(f.ProductId))
            .Select(f => new { f.ProductId, f.Key, f.Value }).ToListAsync(cancellationToken);
        var mine = new BlogProduct(product.Id, product.Name, product.Slug, product.BrandName, product.FinalPrice, true, 0, 0, 0, features, [], [], []);
        QuickCompare? best = null;
        foreach (var id in candidates)
        {
            var other = related.First(r => r.Id == id);
            var theirs = otherFeatures.Where(f => f.ProductId == id).Select(f => new SeoFeature(f.Key, f.Value)).ToList();
            var otherProduct = new BlogProduct(other.Id, other.Name, other.Slug, other.BrandName, other.FinalPrice, true, 0, 0, 0, theirs, [], [], []);
            var rows = BlogWriter.SideBySide(mine, otherProduct).Take(6).ToList();
            if (rows.Count >= 2 && (best is null || rows.Count > best.Rows.Count))
                best = new QuickCompare(other.Name, other.Slug, other.FinalPrice, rows);
        }
        return best;
    }

    public async Task<List<ShortcodeProduct>> CardsBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken = default)
    {
        if (slugs.Count == 0)
            return [];
        var list = slugs.Take(30).ToList();
        var now = DateTimeOffset.UtcNow;
        var rows = await context.Products
            .Where(p => list.Contains(p.Slug) && p.IsActive)
            .Select(p => new
            {
                p.Slug, p.Name, p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt, p.StockQuantity,
                Image = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).FirstOrDefault(),
                Rating = p.Comments.Where(c => c.Type == CommentType.Review && c.Status == CommentStatus.Approved && c.Rating != null).Average(c => (double?)c.Rating) ?? 0,
                Reviews = p.Comments.Count(c => c.Type == CommentType.Review && c.Status == CommentStatus.Approved),
            })
            .ToListAsync(cancellationToken);
        return rows.Select(r =>
        {
            var discounted = r.DiscountPrice is > 0 && r.DiscountPrice < r.Price
                             && (r.DiscountStartAt == null || r.DiscountStartAt <= now) && (r.DiscountEndAt == null || r.DiscountEndAt >= now);
            return new ShortcodeProduct(r.Slug, r.Name, r.Image, r.Price, discounted ? r.DiscountPrice!.Value : r.Price, r.StockQuantity > 0, r.Rating, r.Reviews);
        }).ToList();
    }

    public async Task<QuizResult> QuizAsync(Guid categoryId, decimal? maxPrice, string? priority, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var products = await context.Products
            .Where(p => p.CategoryId == categoryId && p.IsActive && p.StockQuantity > 0)
            .OrderByDescending(p => p.SalesCount).Take(60)
            .Select(p => new { p.Id, p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt, p.SalesCount })
            .ToListAsync(cancellationToken);
        if (products.Count < 3)
            return new QuizResult([], [], [], null);
        var priced = products.Select(p => new
        {
            p.Id,
            Price = p.DiscountPrice is > 0 && p.DiscountPrice < p.Price && (p.DiscountStartAt == null || p.DiscountStartAt <= now) && (p.DiscountEndAt == null || p.DiscountEndAt >= now)
                ? p.DiscountPrice.Value : p.Price,
            p.SalesCount,
        }).ToList();
        var ids = priced.Select(p => p.Id).ToList();
        var features = await context.ProductFeatures.Where(f => ids.Contains(f.ProductId))
            .Select(f => new { f.ProductId, f.Key, f.Value }).ToListAsync(cancellationToken);
        var facts = features.Select(f => (f.ProductId, Fact: SpecKnowledge.Read(new SeoFeature(f.Key, f.Value))))
            .Where(x => x.Fact is { Number: not null, Direction: not 0 }).ToList();

        var budgets = Budgets(priced.Select(p => p.Price).ToList());
        var priorities = facts.GroupBy(x => SpecMerge.NormalizeKey(x.Fact!.Key))
            .Where(g => g.Select(x => x.ProductId).Distinct().Count() >= 3)
            .OrderByDescending(g => SpecKnowledge.Weight(g.First().Fact!.Topic)).ThenByDescending(g => g.Count())
            .Take(5).Select(g => new QuizOption(g.Key, g.Key)).ToList();
        priorities.Insert(0, new QuizOption("popular", "محبوب‌ترین"));
        priorities.Add(new QuizOption("cheap", "ارزان‌ترین"));

        var pool = priced.Where(p => maxPrice is not > 0 || p.Price <= maxPrice.Value).ToList();
        string? explanation = null;
        List<Guid> picks;
        var key = string.IsNullOrWhiteSpace(priority) ? "popular" : priority;
        if (key == "cheap")
        {
            picks = pool.OrderBy(p => p.Price).Take(3).Select(p => p.Id).ToList();
            explanation = "ارزان‌ترین مدل‌های موجود در بودجهٔ شما.";
        }
        else if (key != "popular" && priorities.Any(o => o.Key == key))
        {
            var scores = facts.Where(x => SpecMerge.NormalizeKey(x.Fact!.Key) == key)
                .GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.First().Fact!);
            picks = pool.Where(p => scores.ContainsKey(p.Id))
                .OrderByDescending(p => scores[p.Id].Number!.Value * scores[p.Id].Direction)
                .ThenBy(p => p.Price).Take(3).Select(p => p.Id).ToList();
            explanation = $"مدل‌هایی که در «{key}» بهترین عدد را دارند (در بودجهٔ شما).";
        }
        else
        {
            picks = pool.OrderByDescending(p => p.SalesCount).Take(3).Select(p => p.Id).ToList();
            explanation = "پرفروش‌ترین مدل‌های موجود در بودجهٔ شما.";
        }
        if (picks.Count == 0)
            explanation = "در این بودجه کالای موجودی پیدا نشد؛ بودجه را کمی بالاتر انتخاب کنید.";
        return new QuizResult(budgets, priorities, picks, explanation);
    }

    /// <summary>
    /// گزینه‌های بودجه: چارک‌های قیمت گرد‌شده به ۱۰۰ هزار تومان (یا ۱۰ هزار برای کالای ارزان). مثال: [۸۰۰ هزار، ۱٫۵ میلیون، ۳ میلیون].
    /// </summary>
    public static List<decimal> Budgets(IReadOnlyList<decimal> prices)
    {
        if (prices.Count < 3)
            return [];
        var sorted = prices.OrderBy(p => p).ToList();
        var round = sorted[^1] >= 1_000_000 ? 100_000m : 10_000m;
        return new[] { 0.34, 0.67, 1.0 }
            .Select(q => sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(q * sorted.Count) - 1)])
            .Select(p => Math.Ceiling(p / round) * round)
            .Distinct().ToList();
    }
}
