using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Searches;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

// ─────────────────────────────────────────────────────────────────────────────
// دور ۲۴ (پیشنهادهای ۴۸ تا ۵۰): «فرصت‌های سئو» برای مدیر، فقط از دادهٔ واقعی خود سایت (بدون سرویس پولی):
//   ۴۸ – صفحه‌های کم‌بازدید: کالا/مقاله‌ای که مدتی منتشر شده ولی بازدید ندارد، همراه دلیل‌های قابل اصلاح.
//   ۴۹ – جست‌وجوهای داخلی بی‌نتیجه: مشتری چه می‌خواهد که پیدا نمی‌کند؟ اگر نزدیک‌ترین کالا/دسته/برند پیدا شود،
//         پیشنهاد «افزودن مترادف» (یک کلیک) و گرنه «کمبود کالا».
//   ۵۰ – هم‌نوع‌خواری کلمه (cannibalization): چند صفحه با یک کلمهٔ کلیدی/عنوان، یا یک عبارت سرچ کنسول که روی چند صفحه
//         پخش شده است.
// صفحهٔ مدیریت: /admin/seo/opportunities. همهٔ منطق تصمیم‌گیری در متدهای ایستا است تا تست واحد داشته باشد.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>یک صفحهٔ کم‌بازدید. مثال: کالا «هدفون X»، ۳ بازدید در ۹۰ روز، دلیل‌ها: «توضیح کوتاه است»، «فقط یک تصویر».</summary>
public sealed record LowTrafficRow(string Kind, string Title, string Url, string AdminUrl, int Views, int AgeDays, IReadOnlyList<string> Reasons);

/// <summary>
/// جست‌وجوی بی‌نتیجهٔ مشتری. <see cref="TargetName"/> نزدیک‌ترین چیز موجود است (اگر پیدا شود) و <see cref="SynonymTerms"/>
/// مترادف پیشنهادی (مثلاً «ایرپاد، هدفون بی سیم»). اگر هیچ چیز نزدیکی نباشد یعنی کالا نداریم.
/// </summary>
public sealed record SearchGapRow(string Query, int Searches, int LastResults, DateTimeOffset LastSearchedAt,
    string? TargetName, string? TargetUrl, string? SynonymTerms, string Advice);

/// <summary>یکی از صفحه‌های درگیر در هم‌نوع‌خواری.</summary>
public sealed record KeywordClashPage(string Kind, string Title, string Url, string? AdminUrl);

/// <summary>گروه صفحه‌هایی که برای یک کلمه با هم رقابت می‌کنند، به همراه منبع («کلمهٔ کلیدی»، «عنوان»، «سرچ کنسول») و توصیه.</summary>
public sealed record KeywordClash(string Keyword, string Source, IReadOnlyList<KeywordClashPage> Pages, string Advice);

/// <summary>ورودی خالص الگوریتم هم‌نوع‌خواری (برای تست): نوع صفحه، عنوان، نشانی، کلمهٔ کلیدی کانونی و عنوان سئو.</summary>
public sealed record SeoPageRef(string Kind, string Title, string Url, string? AdminUrl, string? FocusKeyword, string? SeoTitle);

/// <summary>نامزد مقصد برای مترادف (نام کالا/دسته/برند و نشانی آن).</summary>
public sealed record SynonymCandidate(string Name, string Url, int Weight);

/// <summary>سرویس فرصت‌های سئو (صفحهٔ مدیریت فرصت‌ها از آن استفاده می‌کند).</summary>
public interface ISeoOpportunityService
{
    /// <summary>صفحه‌های کم‌بازدید (کالا و مقاله) با دلیل‌ها؛ کم‌بازدیدترین اول.</summary>
    Task<IReadOnlyList<LowTrafficRow>> LowTrafficAsync(int take = 60, CancellationToken cancellationToken = default);

    /// <summary>جست‌وجوهای داخلی بی‌نتیجه یا کم‌نتیجهٔ ۹۰ روز اخیر با پیشنهاد.</summary>
    Task<IReadOnlyList<SearchGapRow>> SearchGapsAsync(int take = 60, CancellationToken cancellationToken = default);

    /// <summary>گروه‌های هم‌نوع‌خواری کلمه.</summary>
    Task<IReadOnlyList<KeywordClash>> CannibalizationAsync(int take = 60, CancellationToken cancellationToken = default);

    /// <summary>
    /// افزودن یک مجموعهٔ مترادف (مثلاً «ایرپاد, هدفون بی سیم») – اگر همین مجموعه قبلاً باشد کاری نمی‌کند.
    /// خروجی: true اگر چیزی اضافه شد. ورودی کوتاه/خطرناک رد می‌شود.
    /// </summary>
    Task<bool> AddSynonymAsync(string terms, CancellationToken cancellationToken = default);
}

/// <summary>پیاده‌سازی <see cref="ISeoOpportunityService"/> روی دیتابیس فروشگاه.</summary>
public class SeoOpportunityService(FratakShopDbContext context) : ISeoOpportunityService
{
    /// <summary>صفحه باید دست‌کم این‌قدر روز منتشر شده باشد تا «کم‌بازدید» حساب شود (صفحهٔ تازه فرصت دیده‌شدن نداشته).</summary>
    public const int MinAgeDays = 30;

    /// <summary>کمتر از این تعداد بازدید (کل عمر صفحه) یعنی کم‌بازدید.</summary>
    public const int LowViews = 20;

    /// <summary>جست‌وجویی که کمتر از این دفعه تکرار شده نویز است (غلط تایپی یک‌باره).</summary>
    public const int MinSearches = 2;

    /// <summary>حداقل نمایش هر صفحه در سرچ کنسول تا در هم‌نوع‌خواری شمرده شود.</summary>
    public const int MinImpressions = 5;

    /// <inheritdoc />
    public async Task<IReadOnlyList<LowTrafficRow>> LowTrafficAsync(int take = 60, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddDays(-MinAgeDays);
        take = Math.Clamp(take, 1, 200);

        var products = await context.Products
            .Where(p => p.IsActive && p.PublishedAt != null && p.PublishedAt <= cutoff && p.ViewCount < LowViews)
            .OrderBy(p => p.ViewCount).ThenBy(p => p.PublishedAt)
            .Take(take)
            .Select(p => new
            {
                p.Id, p.Name, p.Slug, p.ViewCount, p.PublishedAt, p.StockQuantity,
                DescriptionLength = p.Description == null ? 0 : p.Description.Length,
                Images = p.Images.Count,
                SeoTitle = p.Seo == null ? null : p.Seo.Title,
                MetaDescription = p.Seo == null ? null : p.Seo.MetaDescription,
                FocusKeyword = p.Seo == null ? null : p.Seo.FocusKeyword,
                Links = context.Products.Count(o => o.IsActive && o.CategoryId == p.CategoryId) - 1,
            })
            .ToListAsync(cancellationToken);

        var posts = await context.BlogPosts
            .Where(b => b.IsPublished && b.PublishedAt != null && b.PublishedAt <= cutoff && b.ViewCount < LowViews)
            .OrderBy(b => b.ViewCount).ThenBy(b => b.PublishedAt)
            .Take(take)
            .Select(b => new
            {
                b.Id, b.Title, b.Slug, b.ViewCount, b.PublishedAt, ContentLength = b.Content.Length, b.ImageName,
                SeoTitle = b.Seo == null ? null : b.Seo.Title,
                MetaDescription = b.Seo == null ? null : b.Seo.MetaDescription,
                FocusKeyword = b.Seo == null ? null : b.Seo.FocusKeyword,
            })
            .ToListAsync(cancellationToken);

        var rows = new List<LowTrafficRow>(products.Count + posts.Count);
        rows.AddRange(products.Select(p => new LowTrafficRow("کالا", p.Name, $"/product/{p.Slug}", $"/admin/products/edit/{p.Id}",
            p.ViewCount, AgeDays(p.PublishedAt!.Value, now),
            ProductReasons(p.DescriptionLength, p.Images, p.SeoTitle, p.MetaDescription, p.FocusKeyword, p.StockQuantity, p.Links))));
        rows.AddRange(posts.Select(b => new LowTrafficRow("مقاله", b.Title, $"/blog/{b.Slug}", $"/admin/blog/edit/{b.Id}",
            b.ViewCount, AgeDays(b.PublishedAt!.Value, now),
            PostReasons(b.ContentLength, b.ImageName, b.SeoTitle, b.MetaDescription, b.FocusKeyword))));

        return rows.OrderBy(r => r.Views).ThenByDescending(r => r.AgeDays).Take(take).ToList();
    }

    /// <summary>روزهای گذشته از انتشار (حداقل ۰).</summary>
    private static int AgeDays(DateTimeOffset publishedAt, DateTimeOffset now) => Math.Max(0, (int)(now - publishedAt).TotalDays);

    /// <summary>
    /// دلیل‌های قابل اصلاح کم‌بازدیدی یک کالا. مثال: توضیح ۱۵۰ نویسه‌ای و یک تصویر ←
    /// [«توضیح کالا کوتاه است (۱۵۰ نویسه؛ حداقل ۶۰۰ پیشنهاد می‌شود).»، «فقط ۱ تصویر دارد؛ ۳ تصویر یا بیشتر بهتر است.»].
    /// اگر همه‌چیز خوب باشد یک توصیهٔ عمومی (لینک داخلی/اشتراک) برمی‌گردد تا ردیف هیچ‌وقت بی‌توضیح نماند.
    /// </summary>
    public static IReadOnlyList<string> ProductReasons(int descriptionLength, int images, string? seoTitle, string? metaDescription,
        string? focusKeyword, int stock, int siblings)
    {
        var reasons = new List<string>();
        if (stock <= 0)
            reasons.Add("ناموجود است؛ گوگل و مشتری کالای ناموجود را کمتر نشان می‌دهند/می‌بینند.");
        if (descriptionLength < 600)
            reasons.Add($"توضیح کالا کوتاه است ({Digits(descriptionLength)} نویسه؛ حداقل ۶۰۰ پیشنهاد می‌شود).");
        if (images < 3)
            reasons.Add($"فقط {Digits(images)} تصویر دارد؛ ۳ تصویر یا بیشتر بهتر است.");
        if (string.IsNullOrWhiteSpace(seoTitle))
            reasons.Add("عنوان سئو ندارد (از «پیشنهاد هوشمند» بسازید).");
        if (string.IsNullOrWhiteSpace(metaDescription))
            reasons.Add("توضیح متا ندارد.");
        if (string.IsNullOrWhiteSpace(focusKeyword))
            reasons.Add("کلمهٔ کلیدی کانونی ندارد.");
        if (siblings <= 0)
            reasons.Add("تنها کالای دسته است؛ لینک داخلی کمی به آن می‌رسد.");
        if (reasons.Count == 0)
            reasons.Add("محتوا کامل است؛ از مقاله‌ها و صفحهٔ دسته به آن لینک دهید یا در بنر/خبرنامه معرفی کنید.");
        return reasons;
    }

    /// <summary>دلیل‌های کم‌بازدیدی یک مقاله (مانند <see cref="ProductReasons"/>؛ حداقل محتوای پیشنهادی ۳۰۰۰ نویسه HTML).</summary>
    public static IReadOnlyList<string> PostReasons(int contentLength, string? imageName, string? seoTitle, string? metaDescription, string? focusKeyword)
    {
        var reasons = new List<string>();
        if (contentLength < 3000)
            reasons.Add($"متن مقاله کوتاه است ({Digits(contentLength)} نویسه).");
        if (string.IsNullOrWhiteSpace(imageName))
            reasons.Add("تصویر شاخص ندارد.");
        if (string.IsNullOrWhiteSpace(seoTitle))
            reasons.Add("عنوان سئو ندارد.");
        if (string.IsNullOrWhiteSpace(metaDescription))
            reasons.Add("توضیح متا ندارد.");
        if (string.IsNullOrWhiteSpace(focusKeyword))
            reasons.Add("کلمهٔ کلیدی کانونی ندارد.");
        if (reasons.Count == 0)
            reasons.Add("محتوا کامل است؛ از صفحهٔ دسته و کالاهای مرتبط به آن لینک دهید یا موضوع را به پرسش رایج مشتری نزدیک کنید.");
        return reasons;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchGapRow>> SearchGapsAsync(int take = 60, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);
        var since = DateTimeOffset.UtcNow.AddDays(-90);
        var logs = await context.SearchQueryLogs
            .Where(l => l.LastSearchedAt >= since && l.SearchCount >= MinSearches && l.LastResultCount <= 1 && !l.IsHidden)
            .OrderByDescending(l => l.SearchCount)
            .Take(take)
            .Select(l => new { l.Query, l.NormalizedQuery, l.SearchCount, l.LastResultCount, l.LastSearchedAt })
            .ToListAsync(cancellationToken);
        if (logs.Count == 0)
            return [];

        var candidates = new List<SynonymCandidate>();
        candidates.AddRange(await context.Categories.Where(c => c.IsActive && !c.IsSystem)
            .Select(c => new SynonymCandidate(c.Name, "/category/" + c.Slug, 3)).ToListAsync(cancellationToken));
        candidates.AddRange(await context.Brands.Where(b => b.IsActive)
            .Select(b => new SynonymCandidate(b.Name, "/brand/" + b.Slug, 2)).ToListAsync(cancellationToken));
        candidates.AddRange(await context.Products.Where(p => p.IsActive && p.PublishedAt != null)
            .OrderByDescending(p => p.SalesCount).ThenByDescending(p => p.ViewCount).Take(3000)
            .Select(p => new SynonymCandidate(p.Name, "/product/" + p.Slug, 1)).ToListAsync(cancellationToken));

        var existing = await context.SearchSynonyms.Where(s => s.IsActive).Select(s => s.Terms).ToListAsync(cancellationToken);
        var known = new HashSet<string>(existing.SelectMany(SplitTerms), StringComparer.OrdinalIgnoreCase);

        return logs.Select(l =>
        {
            var match = BestMatch(l.Query, candidates);
            var normalized = SearchText.Normalize(l.Query).ToLowerInvariant();
            if (match is null)
                return new SearchGapRow(l.Query, l.SearchCount, l.LastResultCount, l.LastSearchedAt, null, null, null,
                    "چیزی شبیه این نداریم؛ اگر بارها جست‌وجو شده، تأمین این کالا را بررسی کنید.");
            var terms = SynonymTerms(l.Query, match.Name);
            return known.Contains(normalized)
                ? new SearchGapRow(l.Query, l.SearchCount, l.LastResultCount, l.LastSearchedAt, match.Name, match.Url, null,
                    "مترادف قبلاً ثبت شده است؛ نتیجهٔ جست‌وجوی بعدی باید بهتر شود.")
                : new SearchGapRow(l.Query, l.SearchCount, l.LastResultCount, l.LastSearchedAt, match.Name, match.Url, terms,
                    $"مشتری احتمالاً «{match.Name}» را می‌خواهد؛ با افزودن مترادف، جست‌وجو آن را پیدا می‌کند.");
        }).ToList();
    }

    /// <summary>
    /// نزدیک‌ترین نامزد به عبارت جست‌وجو. امتیاز = سهم واژه‌های عبارت که در نام نامزد (دقیق یا با غلط تایپی مجاز
    /// <see cref="Fuzzy.Tolerance"/>) پیدا می‌شوند، ضربدر وزن نوع (دسته ۳، برند ۲، کالا ۱). حداقل نیمی از واژه‌ها باید پیدا شوند.
    /// مثال: «هدفن بیسیم» و دستهٔ «هدفون بی سیم» ← دسته؛ «یخچال» وقتی هیچ نامی شبیهش نیست ← null.
    /// </summary>
    public static SynonymCandidate? BestMatch(string query, IEnumerable<SynonymCandidate> candidates)
    {
        var terms = SearchText.Terms(query).Select(t => t.ToLowerInvariant()).Where(t => t.Length >= 2).ToList();
        if (terms.Count == 0)
            return null;
        var joined = string.Concat(terms);

        SynonymCandidate? best = null;
        var bestScore = 0d;
        foreach (var candidate in candidates)
        {
            var words = SearchText.Terms(candidate.Name).Select(w => w.ToLowerInvariant()).ToList();
            if (words.Count == 0)
                continue;
            // «بیسیم» در برابر «بی سیم»: نسخهٔ بدون فاصله هم مقایسه می‌شود.
            var glued = string.Concat(words);
            var hits = terms.Count(t => words.Any(w => Close(t, w)) || (t.Length >= 4 && glued.Contains(t, StringComparison.Ordinal)));
            if (hits == 0 && terms.Count > 1 && Close(joined, glued))
                hits = terms.Count;
            var coverage = hits / (double)terms.Count;
            if (coverage < 0.5)
                continue;
            var score = coverage * candidate.Weight + Math.Min(hits, words.Count) / (double)words.Count * 0.5;
            if (score > bestScore)
                (best, bestScore) = (candidate, score);
        }
        return best;
    }

    /// <summary>دو واژه «نزدیک»اند اگر برابر باشند یا فاصلهٔ ویرایشی‌شان در حد مجاز طول واژه باشد.</summary>
    private static bool Close(string a, string b) =>
        a == b || (Math.Abs(a.Length - b.Length) <= Fuzzy.Tolerance(Math.Min(a.Length, b.Length))
                   && Fuzzy.Distance(a, b, Fuzzy.Tolerance(Math.Min(a.Length, b.Length))) <= Fuzzy.Tolerance(Math.Min(a.Length, b.Length)));

    /// <summary>
    /// متن مجموعهٔ مترادف پیشنهادی: «عبارت جست‌وجو, نام مقصد». نام‌های طولانی (بیش از ۴ واژه، مثل نام کامل کالا) به سه واژهٔ
    /// اول کوتاه می‌شوند تا مترادف عمومی بماند. مثال: («ایرپاد», «هدفون بی سیم») ← «ایرپاد, هدفون بی سیم».
    /// </summary>
    public static string SynonymTerms(string query, string targetName)
    {
        var q = SearchText.Normalize(query);
        var words = SearchText.Normalize(targetName).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var target = words.Length > 4 ? string.Join(' ', words.Take(3)) : string.Join(' ', words);
        return string.Equals(q, target, StringComparison.OrdinalIgnoreCase) ? q : $"{q}, {target}";
    }

    /// <summary>جدا کردن مجموعهٔ «a, b, c» به واژه‌های نرمال‌شده.</summary>
    private static IEnumerable<string> SplitTerms(string terms) =>
        terms.Split([',', '،'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => SearchText.Normalize(t).ToLowerInvariant()).Where(t => t.Length > 0);

    /// <inheritdoc />
    public async Task<bool> AddSynonymAsync(string terms, CancellationToken cancellationToken = default)
    {
        var clean = CleanSynonym(terms);
        if (clean is null)
            return false;
        var all = await context.SearchSynonyms.Select(s => s.Terms).ToListAsync(cancellationToken);
        var wanted = SplitTerms(clean).OrderBy(t => t, StringComparer.Ordinal).ToList();
        if (all.Any(existing => SplitTerms(existing).OrderBy(t => t, StringComparer.Ordinal).SequenceEqual(wanted)))
            return false;
        context.SearchSynonyms.Add(new SearchSynonym { Terms = clean, IsActive = true });
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// اعتبارسنجی متن مترادف: دست‌کم دو واژهٔ متفاوت، هر کدام حداکثر ۶۰ نویسه، بدون نشانه‌های HTML/اسکریپت، کل متن حداکثر ۳۰۰.
    /// مثال: «ایرپاد, هدفون بی سیم» ← همان؛ «<script>, x» یا «تنها» ← null.
    /// </summary>
    public static string? CleanSynonym(string? terms)
    {
        if (string.IsNullOrWhiteSpace(terms) || terms.Length > 300 || terms.IndexOfAny(['<', '>', '"', '\'', '{', '}', ';']) >= 0)
            return null;
        var parts = terms.Split([',', '،'], StringSplitOptions.RemoveEmptyEntries)
            .Select(SearchText.Normalize).Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (parts.Count < 2 || parts.Any(p => p.Length > 60))
            return null;
        return string.Join(", ", parts);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KeywordClash>> CannibalizationAsync(int take = 60, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);
        var pages = new List<SeoPageRef>();
        pages.AddRange(await context.Categories.Where(c => c.IsActive && !c.IsSystem)
            .Select(c => new SeoPageRef("دسته", c.Name, "/category/" + c.Slug, "/admin/manage/categories/edit/" + c.Id,
                c.Seo == null ? null : c.Seo.FocusKeyword, c.Seo == null ? null : c.Seo.Title))
            .ToListAsync(cancellationToken));
        pages.AddRange(await context.Products.Where(p => p.IsActive && p.PublishedAt != null)
            .Select(p => new SeoPageRef("کالا", p.Name, "/product/" + p.Slug, "/admin/products/edit/" + p.Id,
                p.Seo == null ? null : p.Seo.FocusKeyword, p.Seo == null ? null : p.Seo.Title))
            .ToListAsync(cancellationToken));
        pages.AddRange(await context.BlogPosts.Where(b => b.IsPublished)
            .Select(b => new SeoPageRef("مقاله", b.Title, "/blog/" + b.Slug, "/admin/blog/edit/" + b.Id,
                b.Seo == null ? null : b.Seo.FocusKeyword, b.Seo == null ? null : b.Seo.Title))
            .ToListAsync(cancellationToken));

        var groups = FindCannibalization(pages).ToList();

        // سرچ کنسول (اگر وصل باشد): عبارتی که در آخرین دوره روی چند صفحه نمایش گرفته است.
        var period = await context.SearchConsoleQueryStats.MaxAsync(s => (DateOnly?)s.PeriodEnd, cancellationToken);
        if (period is { } end)
        {
            var stats = await context.SearchConsoleQueryStats
                .Where(s => s.PeriodEnd == end && s.Impressions >= MinImpressions)
                .Select(s => new { s.Query, s.Path, s.Impressions })
                .ToListAsync(cancellationToken);
            var byPath = pages.GroupBy(p => p.Url, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            groups.AddRange(ConsoleGroups(stats.Select(s => (s.Query, s.Path, s.Impressions)), byPath));
        }

        return groups.OrderByDescending(g => g.Pages.Count).ThenBy(g => g.Keyword, StringComparer.Ordinal).Take(take).ToList();
    }

    /// <summary>
    /// گروه‌بندی صفحه‌ها بر اساس کلمهٔ کلیدی کانونی و عنوان سئو (هر دو نرمال‌شده). گروهِ کلمهٔ کلیدی اولویت دارد و
    /// اگر همان صفحه‌ها با عنوان هم گروه شوند تکرار نمی‌شود. فقط گروه‌های ۲ صفحه یا بیشتر.
    /// مثال: دستهٔ «هدفون» و کالای «هدفون سونی» هر دو با کلمهٔ «هدفون» ← یک گروه با توصیهٔ «دسته کلی، کالا دقیق».
    /// </summary>
    public static IReadOnlyList<KeywordClash> FindCannibalization(IEnumerable<SeoPageRef> pages)
    {
        var list = pages.ToList();
        var result = new List<KeywordClash>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (source, selector) in new (string, Func<SeoPageRef, string?>)[] { ("کلمهٔ کلیدی", p => p.FocusKeyword), ("عنوان", p => p.SeoTitle) })
        {
            foreach (var group in list.Select(p => (Key: Key(selector(p)), Page: p)).Where(x => x.Key.Length >= 2)
                         .GroupBy(x => x.Key, StringComparer.Ordinal).Where(g => g.Select(x => x.Page.Url).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
            {
                var members = group.Select(x => x.Page).DistinctBy(p => p.Url, StringComparer.OrdinalIgnoreCase).ToList();
                var signature = string.Join('|', members.Select(p => p.Url.ToLowerInvariant()).Order(StringComparer.Ordinal));
                if (!seen.Add(signature))
                    continue;
                var pagesOut = members.Select(p => new KeywordClashPage(p.Kind, p.Title, p.Url, p.AdminUrl)).ToList();
                result.Add(new KeywordClash(selector(members[0])!.Trim(), source, pagesOut, Advice(pagesOut)));
            }
        }
        return result;
    }

    /// <summary>عبارت‌های سرچ کنسول با نمایش روی دو صفحه یا بیشتر ← گروه با منبع «سرچ کنسول».</summary>
    public static IEnumerable<KeywordClash> ConsoleGroups(IEnumerable<(string Query, string Path, int Impressions)> stats,
        IReadOnlyDictionary<string, SeoPageRef> pagesByPath)
    {
        foreach (var group in stats.GroupBy(s => Key(s.Query), StringComparer.Ordinal).Where(g => g.Key.Length >= 2))
        {
            var paths = group.OrderByDescending(s => s.Impressions).Select(s => s.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (paths.Count < 2)
                continue;
            var members = paths.Select(path => pagesByPath.TryGetValue(path, out var page)
                ? new KeywordClashPage(page.Kind, page.Title, page.Url, page.AdminUrl)
                : new KeywordClashPage("صفحه", path, path, null)).ToList();
            yield return new KeywordClash(group.First().Query, "سرچ کنسول", members, Advice(members));
        }
    }

    /// <summary>
    /// توصیهٔ رفع هم‌نوع‌خواری بر اساس نوع صفحه‌ها: دسته + کالا ← کلمهٔ کلی برای دسته و مدل/برند برای کالا؛
    /// مقاله + دیگران ← مقاله با زاویهٔ «راهنما/پرسش» و لینک به صفحهٔ اصلی؛ فقط کالاها ← تمایز با مدل/ویژگی.
    /// </summary>
    public static string Advice(IReadOnlyList<KeywordClashPage> pages)
    {
        var kinds = pages.Select(p => p.Kind).ToHashSet(StringComparer.Ordinal);
        if (kinds.Contains("دسته") && kinds.Contains("کالا"))
            return "کلمهٔ کلی را فقط برای صفحهٔ دسته نگه دارید و برای هر کالا کلمهٔ دقیق‌تر (برند، مدل یا ویژگی) بگذارید.";
        if (kinds.Contains("مقاله"))
            return "مقاله را با زاویهٔ «راهنما/پرسش» بازنویسی کنید و از آن به صفحهٔ اصلی (دسته یا کالا) لینک دهید.";
        if (kinds.Count == 1 && kinds.Contains("کالا"))
            return "کالاها را با مدل، رنگ یا ویژگی متمایز کنید تا هر صفحه برای جست‌وجوی خودش رتبه بگیرد.";
        return "یکی از صفحه‌ها را صفحهٔ اصلی این کلمه کنید و بقیه را با کلمهٔ متفاوت و لینک به صفحهٔ اصلی تنظیم کنید.";
    }

    /// <summary>کلید مقایسه: نرمال‌سازی فارسی، حروف کوچک، حذف نیم‌فاصله/فاصلهٔ اضافه. null ← رشتهٔ خالی.</summary>
    private static string Key(string? value) => SearchText.Normalize(value).ToLowerInvariant();

    /// <summary>عدد با رقم فارسی (برای متن دلیل‌ها).</summary>
    private static string Digits(int value) => Engine.PersianText.Digits(value);
}
