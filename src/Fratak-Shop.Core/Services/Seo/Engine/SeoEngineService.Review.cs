using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Core.Services.Seo.Knowledge;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Core.Services.Seo.Engine;

// ═══════════════════════════════════════════════════════════════════════════
//  موتور سئو – «توضیحات محصول» هوشمند محصول (دور بیستم)
//  این بخش متن را فقط «می‌سازد و می‌سنجد»؛ ذخیره با ISeoAutopilotService.ApplyBodyAsync است (با تاریخچه و امکان برگرداندن).
//  سنجه‌های ایمنی (همه باید قبول شوند تا Safe=true شود و دستیار بتواند خودکار اعمال کند):
//   ۱) نگهبان جریمه (SeoPenaltyGuard): بدون عبارت اسپم، بدون لینک زیاد؛
//   ۲) طبیعی بودن متن (AiPatternGuard) ≥ ۷۰؛
//   ۳) تحلیل‌گر سئو: «محتوای تکراری» و «تراکم کلمهٔ کلیدی» نباید «بد» باشند؛ نمرهٔ صفحه نباید پایین بیاید؛
//   ۴) طول متن در ستون پایگاه داده جا شود و دست‌کم ۱۵۰ واژه باشد.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// خروجی نوشتن «توضیحات محصول». Safe = همهٔ سنجه‌ها قبول شد (اعمال خودکار مجاز است)؛ Problems = دلیل‌های عدم اطمینان به فارسی.
/// <para>مثال: Html=«&lt;p data-fs="review"&gt;…»، Words=420، ScoreBefore=48، ScoreAfter=71، NaturalScore=86، Safe=true.</para>
/// </summary>
public sealed class ProductReviewDraft
{
    public string Html { get; init; } = "";
    public int Words { get; init; }
    public int ScoreBefore { get; init; }
    public int ScoreAfter { get; init; }
    public int NaturalScore { get; init; }
    public bool Safe { get; init; }
    public List<string> Problems { get; init; } = [];
}

public sealed partial class SeoEngineService
{
    /// <summary>کمترین واژهٔ «توضیحات محصول» قابل قبول (کمتر = محتوای کم‌ارزش).</summary>
    public const int ReviewMinWords = 150;

    /// <summary>کمترین نمرهٔ طبیعی بودن متن (AiPatternGuard) برای اعمال خودکار.</summary>
    public const int ReviewMinNatural = 70;

    /// <summary>بررسی‌های تحلیل‌گر که «بد» بودنشان یعنی خطر جریمه (اعمال خودکار ممنوع).</summary>
    private static readonly string[] ReviewBlockingChecks = ["duplicate-content", "keyword-density"];

    /// <inheritdoc cref="ISeoEngineService.WriteProductReviewAsync"/>
    public async Task<OperationResult<ProductReviewDraft>> WriteProductReviewAsync(Guid productId, string? keepHtml, ReviewNotes? notes,
        CancellationToken cancellationToken = default)
    {
        var current = await settings.GetAsync(cancellationToken);
        var store = StoreFactsFrom(current);
        var subject = (await LoadSubjectsAsync(SeoEntityKind.Product, productId, cancellationToken)).FirstOrDefault();
        if (subject is null)
            return OperationResult<ProductReviewDraft>.NotFound("محصول پیدا نشد (حذف یا غیرفعال شده است).");

        var site = await GetSiteIndexAsync(cancellationToken);
        var before = SeoAnalyzer.Analyze(subject, site, store);
        var models = await BuildModelsAsync(null, cancellationToken);
        var keyword = PersianText.Clean(subject.FocusKeyword);
        var kb = await knowledge.GetAsync(cancellationToken);
        var keep = string.IsNullOrWhiteSpace(keepHtml) ? null : keepHtml.Trim();

        // دور ۳۰ (#۱۲) – «هشدار را خود سیستم درست می‌کند»: به‌جای نشان دادن هشدار به ادمین، نسخه‌های دیگر متن (تنوع واژه، کوتاه‌تر،
        // کم‌تبلیغ‌تر، بلندتر) ساخته و سنجیده می‌شوند تا نسخه‌ای بی‌ایراد پیدا شود. فقط اگر هیچ نسخه‌ای سالم نشد، کم‌ایرادترین
        // نسخه با فهرست ایرادها برمی‌گردد (و اعمال خودکار نمی‌شود).
        ProductReviewDraft? best = null;
        foreach (var (variant, style) in ReviewAttempts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ctx = new WriterContext
            {
                Subject = subject, Store = store, Keyword = keyword, Language = models.Language, Site = site, Notes = notes,
                Variant = variant, Style = style,
            };
            var written = ContentWriter.ProductReview(ctx, keep);
            if (written is null)
            {
                if (variant == 0)
                    return OperationResult<ProductReviewDraft>.Error("مشخصات کافی نیست؛ برای «توضیحات محصول» دست‌کم ۲ مشخصهٔ قابل استفاده لازم است.");
                continue;
            }
            var draft = CheckReview(subject, written, keyword, kb, site, store, before.Score);
            if (best is null || draft.Problems.Count < best.Problems.Count
                || (draft.Problems.Count == best.Problems.Count && draft.ScoreAfter > best.ScoreAfter))
                best = draft;
            if (draft.Safe)
                break;
        }
        return best is null
            ? OperationResult<ProductReviewDraft>.Error("مشخصات کافی نیست؛ برای «توضیحات محصول» دست‌کم ۲ مشخصهٔ قابل استفاده لازم است.")
            : OperationResult<ProductReviewDraft>.Success(best);
    }

    /// <summary>دور ۳۰ (#۱۲): نسخه‌هایی که برای رفع خودکار ایرادها امتحان می‌شوند (به همین ترتیب؛ اولی = همان نسخهٔ قبلی).</summary>
    public static readonly (int Variant, WriterStyle Style)[] ReviewAttempts =
    [
        (0, WriterStyle.None),
        (1, WriterStyle.None),
        (2, WriterStyle.LessPromotional),
        (3, WriterStyle.Shorter),
        (4, WriterStyle.Longer),
        (5, WriterStyle.LessPromotional | WriterStyle.Shorter),
    ];

    /// <summary>
    /// سنجش یک نسخهٔ «توضیحات محصول» با همهٔ سنجه‌های ایمنی (طول، ستون، طبیعی بودن، جریمه، تکرار/تراکم، افت نمره).
    /// قبل از سنجش، دروازهٔ کیفیت (<see cref="Fratak_Shop.Core.Services.Smart.ContentQualityGate.Clean"/>) جمله‌های بی‌جا را حذف می‌کند.
    /// </summary>
    private ProductReviewDraft CheckReview(SeoSubject subject, string written, string keyword, SeoKnowledgeBase kb, SeoSiteIndex site,
        StoreFacts store, int beforeScore)
    {
        // پاک‌سازی امنیتی همان پاک‌سازی ذخیرهٔ دستی است؛ ساختار ما (h2/h3/ul/table/strong/em/class/data-fs) از آن عبور می‌کند.
        var html = Fratak_Shop.Core.Services.Smart.ContentQualityGate.Clean(written.SanitizeText());
        var problems = new List<string>();
        var stats = PersianText.AnalyzeHtml(html);
        if (stats.Words < ReviewMinWords)
            problems.Add($"متن کوتاه است ({PersianText.Digits(stats.Words)} واژه؛ دست‌کم {PersianText.Digits(ReviewMinWords)}).");
        if (!FitsColumn(SeoEntityKind.Product, html))
            problems.Add("متن از سقف ستون توضیحات محصول بلندتر است.");
        foreach (var issue in Fratak_Shop.Core.Services.Smart.ContentQualityGate.Issues(html, style: false))
            problems.Add(issue);

        var natural = AiPatternGuard.Analyze(stats.Text);
        if (natural.Score < ReviewMinNatural)
            problems.Add($"نمرهٔ طبیعی بودن متن پایین است ({PersianText.Digits(natural.Score)} از ۱۰۰).");

        foreach (var risk in SeoPenaltyGuard.Check(subject.EffectiveTitle, subject.EffectiveDescription, html, keyword, kb).Where(r => r.Blocking))
            problems.Add("خطر جریمه: " + risk.Message);

        var after = subject.With(new SeoProposal { DescriptionHtml = html }, SeoFields.Description);
        var report = SeoAnalyzer.Analyze(after, site, store);
        foreach (var check in report.Checks.Where(c => c.Level == SeoCheckLevel.Bad && ReviewBlockingChecks.Contains(c.Id)))
            problems.Add(check.Message);
        if (report.Score < beforeScore)
            problems.Add($"نمرهٔ سئوی صفحه پایین می‌آید ({PersianText.Digits(beforeScore)} ← {PersianText.Digits(report.Score)}).");

        return new ProductReviewDraft
        {
            Html = html,
            Words = stats.Words,
            ScoreBefore = beforeScore,
            ScoreAfter = report.Score,
            NaturalScore = natural.Score,
            Safe = problems.Count == 0,
            Problems = problems,
        };
    }
}
