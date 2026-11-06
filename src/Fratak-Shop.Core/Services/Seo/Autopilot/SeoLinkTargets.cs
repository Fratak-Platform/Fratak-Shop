using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>
/// مقصدهای لینک داخلی خودکار (موارد ۱۱۶ تا ۱۲۷ پیشنهاد سئو) + تنظیم «سقف لینک» خلبان خودکار، با کش ۱۰ دقیقه‌ای.
/// <para>مقصدها از دادهٔ واقعی سایت ساخته می‌شوند: دسته‌ها (به ترتیب تعداد محصول)، برندها، محصولات پرفروش موجود و مقاله‌هایی
/// که کلمهٔ کلیدی چندکلمه‌ای دارند. عبارت تک‌کلمه‌ای محصول/مقاله لینک نمی‌شود (مثلاً «گوشی» فقط به دستهٔ گوشی لینک می‌شود، نه
/// به یک محصول خاص) تا لینک‌ها دقیق و طبیعی باشند.</para>
/// <para>کجا استفاده می‌شود؟ نمایش صفحهٔ محصول و مقاله (لینک هنگام نمایش، بدون تغییر متن ذخیره‌شده) و پیشنهاد لینک در
/// بررسی زندهٔ ویرایشگر.</para>
/// </summary>
public interface ISeoLinkTargetProvider
{
    /// <summary>همهٔ مقصدها (اولویت بیشتر اول).</summary>
    Task<IReadOnlyList<LinkTarget>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// برای نمایش صفحه: مقصدها و سقف لینک. اگر لینک‌سازی خودکار خاموش باشد MaxLinks = ۰.
    /// مثال: <c>var (targets, max) = await provider.ForRenderAsync(); html = InternalLinker.Link(html, path, targets, max);</c>
    /// </summary>
    Task<(IReadOnlyList<LinkTarget> Targets, int MaxLinks)> ForRenderAsync(CancellationToken cancellationToken = default);

    /// <summary>تنظیمات خلبان خودکار با کش ۱۰ دقیقه‌ای (برای مسیرهای پربازدید: ثبت ربات، سرعت، فید ترب).</summary>
    Task<SeoAutopilotOptions> OptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>پاک کردن کش (بعد از ذخیرهٔ تنظیمات خلبان خودکار).</summary>
    void Invalidate();
}

/// <inheritdoc cref="ISeoLinkTargetProvider"/>
public sealed class SeoLinkTargetProvider(FratakShopDbContext context, IMemoryCache cache) : ISeoLinkTargetProvider
{
    public const string TargetsKey = "seo:link-targets";
    public const string OptionsKey = "seo:autopilot-options";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>سقف تعداد مقصدها (کارایی لینک‌ساز روی متن‌های بلند).</summary>
    public const int MaxTargets = 400;

    public async Task<IReadOnlyList<LinkTarget>> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(TargetsKey, out IReadOnlyList<LinkTarget>? cached) && cached is not null)
            return cached;
        var built = await BuildAsync(cancellationToken);
        cache.Set(TargetsKey, built, Lifetime);
        return built;
    }

    public async Task<SeoAutopilotOptions> OptionsAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(OptionsKey, out SeoAutopilotOptions? options) && options is not null)
            return options;
        var json = await context.SeoAutopilotConfigs.AsNoTracking().OrderBy(c => c.CreatedAt).Select(c => c.OptionsJson).FirstOrDefaultAsync(cancellationToken);
        options = SeoAutopilotOptions.Parse(json);
        cache.Set(OptionsKey, options, Lifetime);
        return options;
    }

    public async Task<(IReadOnlyList<LinkTarget> Targets, int MaxLinks)> ForRenderAsync(CancellationToken cancellationToken = default)
    {
        var options = await OptionsAsync(cancellationToken);
        if (!options.Can(options.InternalLinks) || options.MaxAutoLinks <= 0)
            return ([], 0);
        return (await GetAsync(cancellationToken), options.MaxAutoLinks);
    }

    public void Invalidate()
    {
        cache.Remove(TargetsKey);
        cache.Remove(OptionsKey);
    }

    private async Task<IReadOnlyList<LinkTarget>> BuildAsync(CancellationToken cancellationToken)
    {
        var counts = await context.Products.AsNoTracking().Where(p => p.IsActive)
            .GroupBy(p => p.CategoryId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var brandCounts = await context.Products.AsNoTracking().Where(p => p.IsActive && p.BrandId != null)
            .GroupBy(p => p.BrandId!.Value).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

        var categories = await context.Categories.AsNoTracking().Where(c => c.IsActive)
            .Select(c => new { c.Id, c.Name, c.Slug, Focus = c.Seo != null ? c.Seo.FocusKeyword : null }).ToListAsync(cancellationToken);
        var brands = await context.Brands.AsNoTracking().Where(b => b.IsActive)
            .Select(b => new { b.Id, b.Name, b.EnglishName, b.Slug }).ToListAsync(cancellationToken);
        var products = await context.Products.AsNoTracking().Where(p => p.IsActive && p.StockQuantity > 0)
            .OrderByDescending(p => p.SalesCount).ThenByDescending(p => p.ViewCount).Take(300)
            .Select(p => new { p.Name, p.Slug, p.SalesCount, Focus = p.Seo != null ? p.Seo.FocusKeyword : null }).ToListAsync(cancellationToken);
        var posts = await context.BlogPosts.AsNoTracking().Where(b => b.IsPublished)
            .OrderByDescending(b => b.PublishedAt).Take(150)
            .Select(b => new { b.Slug, Focus = b.Seo != null ? b.Seo.FocusKeyword : null }).ToListAsync(cancellationToken);

        var targets = new List<LinkTarget>();
        foreach (var c in categories)
            targets.Add(new LinkTarget("/category/" + Uri.EscapeDataString(c.Slug), Phrases([c.Focus, c.Name], minWords: 1), 100 + counts.GetValueOrDefault(c.Id)));
        foreach (var b in brands)
            targets.Add(new LinkTarget("/brand/" + Uri.EscapeDataString(b.Slug), Phrases([b.Name, b.EnglishName], minWords: 1), 60 + brandCounts.GetValueOrDefault(b.Id)));
        foreach (var p in products)
            targets.Add(new LinkTarget("/product/" + Uri.EscapeDataString(p.Slug), Phrases([KeywordPlanner.ShortName(p.Name), p.Focus], minWords: 2), 20 + Math.Min(p.SalesCount, 500) / 10.0));
        foreach (var post in posts)
            targets.Add(new LinkTarget("/blog/" + Uri.EscapeDataString(post.Slug), Phrases([post.Focus], minWords: 2), 10));

        return targets.Where(t => t.Phrases.Count > 0)
            .OrderByDescending(t => t.Priority)
            .Take(MaxTargets)
            .ToList();
    }

    /// <summary>
    /// عبارت‌های قابل‌لینک: تمیز، حداقل ۳ نویسه، حداقل <paramref name="minWords"/> واژه، بدون واژهٔ ایست تنها، یکتا.
    /// مثال: <c>Phrases(["هدفون بی‌سیم", "هدفون"], 2) == ["هدفون بی‌سیم"]</c>.
    /// </summary>
    public static List<string> Phrases(IEnumerable<string?> values, int minWords)
    {
        var result = new List<string>();
        foreach (var raw in values)
        {
            var phrase = PersianText.Clean(raw);
            if (phrase.Length < 3 || phrase.Length > 80)
                continue;
            var tokens = PersianText.Tokenize(phrase);
            if (tokens.Count < minWords || tokens.All(PersianText.IsStopWord))
                continue;
            if (!result.Contains(phrase, StringComparer.OrdinalIgnoreCase))
                result.Add(phrase);
        }

        return result;
    }
}
