using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Services.Seo.Knowledge;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Data.Utilities.SeoManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Seo.Engine;

// ═══════════════════════════════════════════════════════════════════════════
//  موتور سئو – مرحلهٔ شانزدهم
//   • سئوی هوشمند درجا (DraftAsync): دکمهٔ «سئوی هوشمند» در صفحهٔ ساخت/ویرایش محصول، مقاله، دسته، برند،
//     صفحهٔ محتوا و صفحه‌های ثابت؛ روی مقدارهای فعلی فرم کار می‌کند (حتی محصولی که هنوز ذخیره نشده) و چیزی ذخیره نمی‌کند.
//   • صفحه‌های ثابت (صفحهٔ اصلی، همهٔ محصولات، وبلاگ، سؤالات متداول، تماس با ما، باشگاه): موتور خودش پیدا، نمره‌دهی و
//     پیشنهاد می‌سازد؛ تأیید = نوشتن در جدول PageSeo.
//   • تحقیق بیشتر: پیشنهاد جست‌وجوی تکمیلی برای مرتبط‌ترین عبارت + ذخیرهٔ همهٔ عبارت‌های واقعی جست‌وجو در جدول دانش.
//   • یادگیری: هر تأیید/رد کارشناس (کلمهٔ کلیدی و الگوی عنوان) در جدول دانش شمرده می‌شود.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// مقدارهای فعلی فرم برای «سئوی هوشمند درجا». null = این فیلد در فرم نیست (مقدار ذخیره‌شده استفاده می‌شود)؛
/// رشتهٔ خالی = کاربر فیلد را خالی کرده است.
/// <para>مثال (محصول تازه): Kind=Product، Id=null، Name=«هدفون بی‌سیم سونی WH-1000XM5»، CategoryId=…، BodyHtml=«&lt;p&gt;…&lt;/p&gt;».</para>
/// </summary>
public sealed class SeoDraftInput
{
    public SeoEntityKind Kind { get; init; }

    /// <summary>شناسهٔ صفحهٔ ذخیره‌شده (null = صفحهٔ تازه که هنوز ذخیره نشده).</summary>
    public Guid? Id { get; init; }

    /// <summary>فقط برای صفحهٔ ثابت: کدام صفحه (از فیلد «صفحه» فرم سئوی صفحات ثابت).</summary>
    public SitePage? Page { get; init; }

    public string? Name { get; init; }
    public string? EnglishName { get; init; }
    public string? Slug { get; init; }
    public string? ShortText { get; init; }
    public string? BodyHtml { get; init; }
    public Guid? CategoryId { get; init; }
    public Guid? BrandId { get; init; }
    public string? WarrantyTitle { get; init; }
    public int? WarrantyMonths { get; init; }

    public string? SeoTitle { get; init; }
    public string? MetaDescription { get; init; }
    public string? FocusKeyword { get; init; }

    /// <summary>جست‌وجوی اینترنتی مجاز است؟ (کاربر می‌تواند فقط با داده‌های سایت و جدول دانش کار کند.)</summary>
    public bool Research { get; init; } = true;

    /// <summary>شمارهٔ نسخه: «گزینه‌های دیگر» در پنجره، عدد را یکی بالا می‌برد تا متن‌های تازه ساخته شوند.</summary>
    public int Variant { get; init; }
}

/// <summary>یک گزینهٔ متن (عنوان یا توضیحات). FullLength = طول همراه پسوند نام سایت (برای عنوان).</summary>
public sealed record SeoDraftOption(string Text, int Length, int FullLength, string? Note = null);

/// <summary>یک کلمهٔ کلیدی نامزد با دلیل‌هایش.</summary>
public sealed record SeoDraftKeyword(string Phrase, double Score, IReadOnlyList<string> Reasons);

/// <summary>خروجی «سئوی هوشمند درجا» (همه پیشنهادی؛ هیچ چیز ذخیره نشده است).</summary>
public sealed class SeoDraft
{
    public SeoEntityKind Kind { get; init; }
    public string Path { get; set; } = "";
    public int ScoreBefore { get; set; }
    public int ScoreAfter { get; set; }

    /// <summary>کلمهٔ کلیدی پیشنهادی/فعلی. KeywordIsNew = فیلد خالی بود و موتور انتخاب کرد.</summary>
    public string? Keyword { get; set; }
    public bool KeywordIsNew { get; set; }
    public List<SeoDraftKeyword> Keywords { get; } = [];

    public List<SeoDraftOption> Titles { get; } = [];
    public List<SeoDraftOption> Metas { get; } = [];
    public string? Slug { get; set; }
    public string? SlugReason { get; set; }

    /// <summary>متن تکمیلی برای صفحهٔ کم‌محتوا (متن فعلی دست‌نخورده در ابتدایش است). null = لازم نیست.</summary>
    public string? BodyHtml { get; set; }
    public List<SeoFaqItem> Faq { get; } = [];

    /// <summary>کارهایی که هنوز مانده (بررسی‌های نه‌چندان خوب بعد از اعمال پیشنهادها).</summary>
    public List<string> Todo { get; } = [];
    public List<string> Reasons { get; } = [];

    /// <summary>هشدارهای خطر جریمه (تراکم کلمه، تکرار، اسپم).</summary>
    public List<SpamIssue> Warnings { get; } = [];
    public SeoResearchSummary Research { get; set; } = new();
    public List<string> Errors { get; } = [];
    public int TitleSuffixLength { get; init; }
}

public sealed partial class SeoEngineService
{
    /// <summary>بودجهٔ درخواست اینترنتی «سئوی هوشمند درجا» (دو برابر ساخت دستی؛ کاربر منتظر همین یک صفحه است).</summary>
    public const int DraftRequests = RequestsPerQuery * 2;

    // ── سئوی هوشمند درجا ────────────────────────────────────────────────

    public async Task<SeoDraft> DraftAsync(SeoDraftInput input, CancellationToken cancellationToken = default)
    {
        var current = await settings.GetAsync(cancellationToken);
        var store = StoreFactsFrom(current);
        var subject = await DraftSubjectAsync(input, current, cancellationToken);
        var draft = new SeoDraft { Kind = input.Kind, TitleSuffixLength = store.TitleSuffixLength };
        if (subject is null || subject.Name.Length == 0)
        {
            draft.Errors.Add(input.Kind == SeoEntityKind.StaticPage ? "صفحهٔ ثابت را از فهرست «صفحه» انتخاب کنید." : "اول نام یا عنوان صفحه را بنویسید.");
            return draft;
        }
        draft.Path = subject.Path;

        var site = await GetSiteIndexAsync(cancellationToken);
        var models = await BuildModelsAsync(null, cancellationToken);
        var kb = await knowledge.GetAsync(cancellationToken);
        var before = SeoAnalyzer.Analyze(subject, site, store);
        var research = CreateResearch(current, current.SeoCompetitorResearch && input.Research ? DraftRequests : 0, store.SiteName);
        var insight = await ResearchSubjectAsync(subject, research, cancellationToken);

        var candidates = KeywordPlanner.Candidates(subject, insight, site, models.InternalSearches, models.TfIdf,
            models.Google.GetValueOrDefault(SeoPaths.Normalize(subject.Path)), kb);
        var proposal = new SeoProposal();
        var keyword = PersianText.Clean(subject.FocusKeyword);
        KeywordCandidate? chosen = null;
        if (keyword.Length == 0 && KeywordPlanner.Choose(candidates, []) is { } best)
        {
            chosen = best;
            keyword = best.Phrase;
            proposal.FocusKeyword = keyword;
            draft.KeywordIsNew = true;
        }
        draft.Keyword = keyword.Length > 0 ? keyword : null;
        foreach (var candidate in candidates.Take(6))
            draft.Keywords.Add(new SeoDraftKeyword(candidate.Phrase, Math.Round(candidate.Score, 1), candidate.Reasons));

        var withKeyword = subject.With(proposal, SeoFields.FocusKeyword);
        var ctx = new WriterContext
        {
            Subject = withKeyword, Store = store, Keyword = keyword, Insight = insight, Language = models.Language,
            Site = site, Variant = Math.Max(0, input.Variant), Style = WriterStyle.None,
        };

        AddTitles(draft, ctx, kb, PageTypeOf(subject.Kind));
        foreach (var meta in ContentWriter.RankedMetas(ctx, 8).Select(m => SpamGuard.CleanText(m, kb)).Where(m => m.Length >= 50)
                     .Distinct(StringComparer.Ordinal).Take(3))
            draft.Metas.Add(new SeoDraftOption(meta, meta.Length, meta.Length));

        // نامک فقط برای محصول و مقاله (نامک دسته/برند/صفحه در منوها و لینک‌ها به کار رفته است).
        SlugAdvice? slug = null;
        if (subject.Kind is SeoEntityKind.Product or SeoEntityKind.BlogPost)
            slug = string.IsNullOrWhiteSpace(subject.Slug)
                ? SlugAdvisor.ForNewPage(subject, keyword, insight, site)
                : current.SeoAllowSlugChanges ? SlugAdvisor.Advise(subject, keyword, insight, site, DateTimeOffset.UtcNow) : null;
        draft.Slug = slug?.Slug;
        draft.SlugReason = slug?.Reason;

        // متن تکمیلی فقط برای صفحهٔ کم‌محتوا (متن انسانی بلند هرگز بازنویسی نمی‌شود).
        var words = PersianText.WordCount(PersianText.HtmlToText(subject.BodyHtml));
        if (words < SeoLimits.ThinContentWords && ContentWriter.Body(ctx) is { } body && FitsColumn(subject.Kind, body))
            draft.BodyHtml = body;
        if (subject.Kind is SeoEntityKind.Product or SeoEntityKind.Category or SeoEntityKind.BlogPost && subject.Faq.Count < 2)
            draft.Faq.AddRange(ContentWriter.Faq(ctx).Take(6));

        proposal.Title = draft.Titles.FirstOrDefault()?.Text;
        proposal.MetaDescription = draft.Metas.FirstOrDefault()?.Text;
        proposal.Slug = draft.Slug;
        proposal.DescriptionHtml = draft.BodyHtml;
        proposal.Faq = draft.Faq.ToList();
        var fields = proposal.Fields;
        var after = SeoAnalyzer.Analyze(subject.With(proposal, fields), site, store);

        draft.ScoreBefore = before.Score;
        draft.ScoreAfter = Math.Max(before.Score, after.Score);
        draft.Reasons.AddRange(BuildReasons(subject, before, after, chosen, slug, insight, fields));
        draft.Todo.AddRange(after.Checks.Where(c => c.Level == SeoCheckLevel.Bad).Select(c => c.Message).Take(6));
        draft.Warnings.AddRange(SpamGuard.Inspect(proposal.Title ?? subject.EffectiveTitle, proposal.MetaDescription ?? subject.EffectiveDescription,
            proposal.DescriptionHtml ?? subject.BodyHtml, keyword, kb));
        draft.Research = ResearchSummary(insight, candidates);
        return draft;
    }

    /// <summary>
    /// سه عنوان برتر (پاک‌شده از اسپم و در بودجهٔ طول). عنوان‌هایی که الگویشان بارها رد شده کنار می‌روند و الگوی
    /// عنوانی که کارشناس‌ها بارها تأیید کرده‌اند (جدول دانش) به‌عنوان یک گزینه اضافه می‌شود.
    /// </summary>
    private static void AddTitles(SeoDraft draft, WriterContext ctx, SeoKnowledgeBase kb, string pageType)
    {
        var lead = ctx.Lead;
        var patterns = kb.Patterns.Where(p => p.Topic == pageType).ToList();
        bool Rejected(string title) =>
            PatternOf(title, lead) is { } pattern && patterns.Any(p => p.Term == pattern && p.Rejections >= p.Successes + 2);

        var titles = ContentWriter.RankedTitles(ctx, 10)
            .Select(t => SpamGuard.CleanTitle(t, kb))
            .Where(t => t.Length > 0 && !Rejected(t))
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToList();
        foreach (var title in titles)
            draft.Titles.Add(new SeoDraftOption(title, title.Length, title.Length + ctx.Store.TitleSuffixLength));

        if (lead.Length == 0)
            return;
        foreach (var learned in patterns.Where(p => p.Successes >= 2 && p.IsAuto && p.Term.Contains("{name}", StringComparison.Ordinal)).Take(1))
        {
            var text = SpamGuard.CleanTitle(learned.Term.Replace("{name}", lead, StringComparison.Ordinal), kb);
            if (text.Length > 0 && text.Length <= ctx.Store.TitleBudget && draft.Titles.All(t => t.Text != text))
                draft.Titles.Add(new SeoDraftOption(text, text.Length, text.Length + ctx.Store.TitleSuffixLength,
                    $"الگوی تأییدشده ({PersianText.Digits(learned.Successes)} بار پذیرفته شده)"));
        }
    }

    /// <summary>
    /// الگوی کلی یک عنوان: نام/کلمهٔ صفحه با «{name}» جایگزین می‌شود تا برای صفحه‌های هم‌نوع قابل استفاده باشد.
    /// مثال: («قیمت و خرید هدفون سونی XM5»، «هدفون سونی XM5») → «قیمت و خرید {name}». null = نام در عنوان نیست.
    /// </summary>
    public static string? PatternOf(string title, string? lead)
    {
        var clean = PersianText.Clean(title);
        var name = PersianText.Clean(lead);
        if (name.Length < 2 || clean.Length == 0)
            return null;
        var replaced = SpamGuard.ReplacePhrase(clean, name, "{name}");
        return ReferenceEquals(replaced, clean) || !replaced.Contains("{name}", StringComparison.Ordinal) ? null : replaced;
    }

    /// <summary>
    /// الگو برای صفحه‌های دیگر هم درست است؟ بخش ثابت الگو نباید واژهٔ خاص این صفحه (نام، برند، دسته) یا مدل/عدد داشته باشد.
    /// مثال: «{name} مشکی | قیمت» برای «هدفون سونی XM5 مشکی» → false (مشکی مال همین کالاست)؛ «خرید {name} با ارسال سریع» → true.
    /// </summary>
    public static bool IsGenericPattern(string pattern, SeoSubject subject)
    {
        var rest = pattern.Replace("{name}", " ", StringComparison.Ordinal);
        if (rest.Any(c => char.IsAsciiLetterOrDigit(c) || char.IsDigit(c)) || pattern.Split("{name}").Length != 2)
            return false;
        var specific = PersianText.ContentStems(string.Join(' ', subject.Name, subject.EnglishName, subject.Product?.BrandName,
            subject.Product?.CategoryName)).ToHashSet(StringComparer.Ordinal);
        return !PersianText.ContentStems(rest).Any(specific.Contains);
    }

    /// <summary>نوع صفحه برای جدول دانش (همان کدهای ستون topic دادهٔ اولیه). مثال: Product → «product».</summary>
    public static string PageTypeOf(SeoEntityKind kind) => kind switch
    {
        SeoEntityKind.Product => "product",
        SeoEntityKind.Category => "category",
        SeoEntityKind.Brand => "brand",
        SeoEntityKind.BlogPost => "blog",
        SeoEntityKind.StaticPage => "home",
        _ => "page",
    };

    /// <summary>
    /// ساخت SeoSubject از فرم: اگر صفحه ذخیره شده باشد، دادهٔ دیتابیس (تصاویر، بازدید، مشخصات، واقعیت‌های دسته) پایه است
    /// و مقدارهای فرم رویش نوشته می‌شوند؛ وگرنه فقط از فرم (+ نام دسته/برند انتخاب‌شده). null = صفحهٔ ثابت نامعتبر.
    /// </summary>
    private async Task<SeoSubject?> DraftSubjectAsync(SeoDraftInput input, StoreSettingDto current, CancellationToken cancellationToken)
    {
        if (input.Kind == SeoEntityKind.StaticPage)
        {
            var page = input.Page is { } p ? StaticSeoPages.Find(p) : input.Id is { } pid ? StaticSeoPages.Find(pid) : null;
            if (page is null)
                return null;
            var stored = (await LoadStaticPagesAsync(StaticSeoPages.IdOf(page.Page), cancellationToken)).FirstOrDefault();
            if (stored is null)
                return null;
            return Overlay(stored, input, stored.Product);
        }

        SeoSubject? existing = null;
        if (input.Id is { } id && id != Guid.Empty)
            existing = (await LoadSubjectsAsync(input.Kind, id, cancellationToken)).FirstOrDefault();

        ProductFacts? product = existing?.Product;
        if (input.Kind == SeoEntityKind.Product)
        {
            var category = input.CategoryId is { } categoryId
                ? await context.Categories.AsNoTracking().Where(c => c.Id == categoryId).Select(c => new { c.Name, c.Slug }).FirstOrDefaultAsync(cancellationToken)
                : null;
            var brand = input.BrandId is { } brandId
                ? await context.Brands.AsNoTracking().Where(b => b.Id == brandId).Select(b => new { b.Name, b.EnglishName, b.Slug }).FirstOrDefaultAsync(cancellationToken)
                : null;
            // فرمی که دسته را می‌فرستد، برند را هم (اگر انتخاب شده) می‌فرستد؛ پس نبودن برند یعنی «بدون برند».
            var fromForm = input.CategoryId is not null || input.BrandId is not null;
            product = new ProductFacts
            {
                CategoryName = category?.Name ?? product?.CategoryName,
                CategorySlug = category?.Slug ?? product?.CategorySlug,
                BrandName = fromForm ? brand?.Name : product?.BrandName,
                BrandEnglishName = fromForm ? brand?.EnglishName : product?.BrandEnglishName,
                BrandSlug = fromForm ? brand?.Slug : product?.BrandSlug,
                Features = product?.Features ?? [],
                WarrantyTitle = input.WarrantyTitle ?? product?.WarrantyTitle,
                WarrantyMonths = input.WarrantyMonths ?? product?.WarrantyMonths,
                InStock = product?.InStock ?? true,
                HasDiscount = product?.HasDiscount ?? false,
            };
        }

        if (existing is not null)
            return Overlay(existing, input, product);

        var name = PersianText.Clean(input.Name);
        var shortText = string.IsNullOrWhiteSpace(input.ShortText) ? null : input.ShortText;
        return new SeoSubject
        {
            Kind = input.Kind,
            EntityId = input.Id ?? Guid.Empty,
            Name = name,
            EnglishName = input.EnglishName,
            Slug = input.Slug?.Trim() ?? "",
            PathPrefix = PathPrefixOf(input.Kind),
            SeoTitle = input.SeoTitle, MetaDescription = input.MetaDescription, FocusKeyword = input.FocusKeyword,
            DefaultTitle = name,
            DefaultDescription = shortText ?? PersianText.Truncate(PersianText.HtmlToText(input.BodyHtml), 160),
            BodyHtml = input.BodyHtml,
            ShortText = shortText,
            CreatedAt = DateTimeOffset.UtcNow,
            Product = product,
        };
    }

    private static SeoSubject Overlay(SeoSubject s, SeoDraftInput input, ProductFacts? product)
    {
        var name = input.Name is null ? s.Name : PersianText.Clean(input.Name);
        return new SeoSubject
        {
            Kind = s.Kind, EntityId = s.EntityId, Name = name, EnglishName = input.EnglishName ?? s.EnglishName,
            Slug = input.Slug?.Trim() ?? s.Slug, PathPrefix = s.PathPrefix,
            SeoTitle = input.SeoTitle ?? s.SeoTitle, MetaDescription = input.MetaDescription ?? s.MetaDescription,
            FocusKeyword = input.FocusKeyword ?? s.FocusKeyword, FaqJson = s.FaqJson,
            NoIndex = s.NoIndex, HasCanonicalElsewhere = s.HasCanonicalElsewhere,
            DefaultTitle = input.Name is null || name == s.Name ? s.DefaultTitle : name,
            DefaultDescription = input.ShortText is { Length: > 0 } shortText ? shortText : s.DefaultDescription,
            BodyHtml = input.BodyHtml ?? s.BodyHtml, ShortText = input.ShortText ?? s.ShortText,
            Images = s.Images, Views = s.Views, CreatedAt = s.CreatedAt,
            Product = product, Listing = s.Listing, RelatedLinks = s.RelatedLinks,
        };
    }

    private static string PathPrefixOf(SeoEntityKind kind) => kind switch
    {
        SeoEntityKind.Product => "/product/",
        SeoEntityKind.Category => "/category/",
        SeoEntityKind.Brand => "/brand/",
        SeoEntityKind.BlogPost => "/blog/",
        SeoEntityKind.ContentPage => "/page/",
        _ => "/",
    };

    // ── تحقیق بیشتر + یادگیری ───────────────────────────────────────────

    /// <summary>
    /// تحقیق اینترنتی یک صفحه (کش‌شده) + «تحقیق بیشتر»: پیشنهادهای جست‌وجوی مرتبط‌ترین عبارت پیشنهادی هم گرفته و به
    /// انتها اضافه می‌شود (یک درخواست بیشتر). همهٔ عبارت‌های واقعی جست‌وجو در جدول دانش ذخیره می‌شوند تا دفعه‌های بعد
    /// (حتی بدون اینترنت) در دسترس باشند. صفحهٔ محتوا و صفحه‌های ثابت غیر از صفحهٔ اصلی تحقیق ندارند.
    /// </summary>
    private async Task<CompetitorInsight?> ResearchSubjectAsync(SeoSubject subject, SeoResearchService research, CancellationToken cancellationToken)
    {
        if (subject.Kind == SeoEntityKind.ContentPage)
            return null;
        if (subject.Kind == SeoEntityKind.StaticPage && StaticSeoPages.Find(subject.EntityId)?.Research != true)
            return null;
        var query = KeywordPlanner.ResearchQuery(subject);
        if (PersianText.Normalize(query).Length < 2)
            return null;
        var commercial = subject.Kind is SeoEntityKind.Product or SeoEntityKind.Category or SeoEntityKind.Brand or SeoEntityKind.StaticPage;
        var insight = await research.ResearchAsync(query, commercial, cancellationToken);

        var key = PersianText.Normalize(query);
        var expand = insight.Suggestions.FirstOrDefault(s => PersianText.Normalize(s) is var n && n != key && n.StartsWith(key, StringComparison.Ordinal));
        if (expand is not null && await research.SuggestAsync(expand, cancellationToken) is { Count: > 0 } more)
            insight.Suggestions = insight.Suggestions.Concat(more).DistinctBy(PersianText.Normalize).Take(20).ToList();

        if (insight.Suggestions.Count > 0)
            await knowledge.RecordResearchAsync(query, insight.Suggestions, cancellationToken);
        return insight;
    }

    /// <summary>
    /// یادگیری از تصمیم کارشناس: کلمهٔ کلیدی (گروه = نوع صفحه) و الگوی عنوان. خطا هرگز به تأیید/رد نمی‌رسد.
    /// مثال: تأیید عنوان «قیمت و خرید هدفون سونی XM5» → الگوی «قیمت و خرید {name}» برای «product» یک موفقیت می‌گیرد.
    /// </summary>
    private async Task LearnAsync(SeoSubject subject, SeoProposal proposal, SeoFields fields, bool accepted, CancellationToken cancellationToken)
    {
        try
        {
            var lead = subject.Kind == SeoEntityKind.Product ? KeywordPlanner.ShortName(subject.Name) : PersianText.Clean(subject.Name);
            var keyword = proposal.FocusKeyword ?? subject.FocusKeyword;
            var patterns = new List<string>();
            if (fields.HasFlag(SeoFields.Title) && proposal.Title is { } title)
            {
                var pattern = PatternOf(title, keyword) ?? PatternOf(title, lead);
                if (pattern is not null && IsGenericPattern(pattern, subject))
                    patterns.Add(pattern);
            }
            await knowledge.RecordOutcomeAsync(new SeoKnowledgeFeedback
            {
                Accepted = accepted,
                Keyword = fields.HasFlag(SeoFields.FocusKeyword) ? proposal.FocusKeyword : null,
                KeywordGroup = PageTypeOf(subject.Kind),
                Patterns = patterns,
                PatternTopic = PageTypeOf(subject.Kind),
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "SEO knowledge learning skipped for {Kind} {Id}", subject.Kind, subject.EntityId);
        }
    }

    // ── صفحه‌های ثابت ───────────────────────────────────────────────────

    /// <summary>
    /// صفحه‌های ثابت با سئوی ذخیره‌شده در PageSeo (اگر ردیفی نباشد، خالی). عنوان/توضیح پیش‌فرض همان مقدار صفحه‌های Razor است.
    /// </summary>
    private async Task<List<SeoSubject>> LoadStaticPagesAsync(Guid? id, CancellationToken cancellationToken)
    {
        var pages = StaticSeoPages.All.Where(p => id is null || StaticSeoPages.IdOf(p.Page) == id).ToList();
        if (pages.Count == 0)
            return [];
        var current = await settings.GetAsync(cancellationToken);
        var keys = pages.Select(p => p.Page).ToList();
        var rows = await context.PageSeos.AsNoTracking()
            .Where(p => keys.Contains(p.Page))
            .Select(p => new
            {
                p.Page, p.CreatedAt,
                SeoTitle = p.Seo!.Title, Meta = p.Seo!.MetaDescription, Focus = p.Seo!.FocusKeyword, Faq = p.Seo!.FaqJson,
                NoIndex = p.Seo!.NoIndex, Canonical = p.Seo!.CanonicalUrl,
            })
            .ToListAsync(cancellationToken);

        return pages.Select(page =>
        {
            var row = rows.FirstOrDefault(r => r.Page == page.Page);
            var (name, title, description) = StaticSeoPages.Defaults(page, current);
            return new SeoSubject
            {
                Kind = SeoEntityKind.StaticPage,
                EntityId = StaticSeoPages.IdOf(page.Page),
                Name = name,
                Slug = "",
                PathPrefix = page.Path,
                SeoTitle = row?.SeoTitle, MetaDescription = row?.Meta, FocusKeyword = row?.Focus, FaqJson = row?.Faq,
                NoIndex = row?.NoIndex == true,
                HasCanonicalElsewhere = CanonicalElsewhere(row?.Canonical, page.Path),
                DefaultTitle = title,
                DefaultDescription = description,
                Views = page.Page == SitePage.Home ? 1000 : 100, // صفحهٔ اصلی پربازدیدترین ورودی است (اولویت صف)
                CreatedAt = row?.CreatedAt ?? DateTimeOffset.UtcNow.AddYears(-1),
            };
        }).ToList();
    }

    /// <summary>نوشتن سئوی تأییدشدهٔ یک صفحهٔ ثابت در PageSeo (اگر ردیف نباشد، ساخته می‌شود).</summary>
    private async Task<string?> ApplyStaticAsync(Guid id, SeoProposal proposal, SeoFields fields, Guid? userId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var page = StaticSeoPages.Find(id);
        if (page is null)
            return "صفحهٔ ثابت پیدا نشد.";
        var row = await context.PageSeos.AsTracking().FirstOrDefaultAsync(p => p.Page == page.Page, cancellationToken);
        if (row is null)
        {
            row = new PageSeo { Page = page.Page };
            context.PageSeos.Add(row);
        }
        row.Seo = ApplySeo(row.Seo, proposal, fields & (SeoFields.Title | SeoFields.MetaDescription | SeoFields.FocusKeyword | SeoFields.Faq), userId, now);
        return null;
    }
}
