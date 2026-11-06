using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Core.Services.Seo.Knowledge;
using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Core.Services.Seo.Engine;

// ═══════════════════════════════════════════════════════════════════════════
//  بررسی زندهٔ ویرایشگر (موارد ۲۱ تا ۳۵ پیشنهاد سئو)
//  هر بار که نویسنده در صفحهٔ ویرایش محصول/مقاله/دسته/برند/صفحه چیزی تایپ می‌کند (با تأخیر ۱ ثانیه)، مقدارهای فعلی فرم
//  به اینجا می‌آید و بدون هیچ ذخیره‌ای این‌ها برمی‌گردد: نمرهٔ سئو، چک‌لیست، تراکم کلمهٔ کلیدی، ساختار تیترها، هشدار کلمهٔ
//  کلیدی تکراری، پیشنهاد لینک داخلی، کم‌محتوایی، الگوهای «متن ماشینی»، خطرهای جریمه و «مانع انتشار».
//  بدون جست‌وجوی اینترنتی (سریع و رایگان)؛ برای «کاملش کن» همان سئوی هوشمند درجا (DraftAsync) استفاده می‌شود.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>یک پیشنهاد لینک داخلی: عبارت داخل متن و صفحه‌ای که بهتر است به آن لینک شود.</summary>
public sealed record SeoLinkSuggestion(string Phrase, string Url);

/// <summary>خروجی بررسی زنده (هیچ چیز ذخیره نشده است).</summary>
public sealed class SeoLiveReport
{
    public int Score { get; set; }
    public IReadOnlyList<SeoCheck> Checks { get; set; } = [];
    public int Words { get; set; }
    public int KeywordCount { get; set; }

    /// <summary>تراکم کلمهٔ کلیدی به درصد (مثال: ۱٫۴).</summary>
    public double Density { get; set; }

    /// <summary>طول عنوان همراه نام سایت و طول توضیحات متا (برای شمارنده و پیش‌نمایش گوگل).</summary>
    public int TitleLength { get; set; }
    public int MetaLength { get; set; }
    public int TitleSuffixLength { get; init; }

    public List<HtmlHeading> Headings { get; } = [];
    public List<string> HeadingIssues { get; } = [];
    public List<SeoLinkSuggestion> Links { get; } = [];

    /// <summary>نمرهٔ «طبیعی بودن متن» (۱۰۰ = کاملاً انسانی) و عبارت‌های کلیشه‌ای/ادعاهای بی‌پشتوانه.</summary>
    public int NaturalScore { get; set; } = 100;
    public List<AiPatternFinding> Patterns { get; } = [];

    /// <summary>خطرهای جریمه (Blocking = موتور سئو چنین متنی را خودکار ذخیره نمی‌کند).</summary>
    public List<PenaltyRisk> Risks { get; } = [];

    /// <summary>کم‌محتوا است؟ (کمتر از حد «نازک» برای این نوع صفحه)</summary>
    public bool Thin { get; set; }

    /// <summary>
    /// «مانع انتشار» (مورد ۳۵): مشکل‌هایی که بهتر است پیش از انتشار حل شوند. فقط هشدار است و ذخیره را نمی‌بندد؛
    /// صفحهٔ ویرایش پیش از انتشار یک پنجرهٔ تأیید نشان می‌دهد.
    /// </summary>
    public List<string> Blockers { get; } = [];

    public List<string> Errors { get; } = [];
}

public sealed partial class SeoEngineService
{
    /// <summary>بیشترین پیشنهاد لینک داخلی در بررسی زنده.</summary>
    public const int LiveLinkSuggestions = 5;

    /// <summary>
    /// بررسی زندهٔ فرم ویرایش. <paramref name="linkTargets"/> = مقصدهای لینک داخلی (از <see cref="ISeoLinkTargetProvider"/>).
    /// <example><code>
    /// var report = await engine.LiveAsync(new SeoDraftInput { Kind = SeoEntityKind.BlogPost, Name = "راهنمای خرید هدفون", BodyHtml = html }, targets);
    /// // report.Score == 58، report.Links[0] == ("هدفون بی‌سیم", "/category/wireless-headphones")
    /// </code></example>
    /// </summary>
    public async Task<SeoLiveReport> LiveAsync(SeoDraftInput input, IReadOnlyList<LinkTarget>? linkTargets, CancellationToken cancellationToken = default)
    {
        var current = await settings.GetAsync(cancellationToken);
        var store = StoreFactsFrom(current);
        var report = new SeoLiveReport { TitleSuffixLength = store.TitleSuffixLength };
        var subject = await DraftSubjectAsync(input, current, cancellationToken);
        if (subject is null || subject.Name.Length == 0)
        {
            report.Errors.Add("اول نام یا عنوان صفحه را بنویسید.");
            return report;
        }

        var site = await GetSiteIndexAsync(cancellationToken);
        var kb = await knowledge.GetAsync(cancellationToken);
        var analysis = SeoAnalyzer.Analyze(subject, site, store);
        report.Score = analysis.Score;
        report.Checks = analysis.Checks;
        report.Words = analysis.Words;
        report.KeywordCount = analysis.KeywordCount;
        report.Density = Math.Round(analysis.Density, 2);
        report.TitleLength = PersianText.Clean(subject.EffectiveTitle).Length + store.TitleSuffixLength;
        report.MetaLength = subject.EffectiveDescription.Length;

        FillLiveExtras(report, subject, linkTargets, kb, site);
        return report;
    }

    /// <summary>
    /// بخش‌های بدون پایگاه داده (تیترها، لینک، متن ماشینی، جریمه، مانع انتشار)؛ جدا شده تا تست واحد داشته باشد.
    /// </summary>
    public static void FillLiveExtras(SeoLiveReport report, SeoSubject subject, IReadOnlyList<LinkTarget>? linkTargets,
        SeoKnowledgeBase? kb, SeoSiteIndex? site)
    {
        var stats = PersianText.AnalyzeHtml(subject.BodyHtml);
        report.Headings.AddRange(stats.Headings);
        if (stats.Headings.Any(h => h.Level == 1))
            report.HeadingIssues.Add("متن تیتر H1 دارد؛ هر صفحه فقط یک H1 دارد (نام صفحه). تیترهای متن از H2 شروع شوند.");
        if (PageAuditor.HeadingSkips(stats.Headings))
            report.HeadingIssues.Add("ترتیب تیترها پرش دارد (مثلاً H2 و بعد مستقیم H4)؛ سطح‌ها را پشت سر هم بگذارید.");
        if (stats.Words >= 600 && stats.Headings.Count(h => h.Level == 2) == 0)
            report.HeadingIssues.Add("متن بلند بدون تیتر H2 است؛ با چند میان‌تیتر خوانا و قابل‌فهرست شود.");
        var duplicateHeadings = stats.Headings.GroupBy(h => PersianText.Normalize(h.Text)).Where(g => g.Key.Length > 0 && g.Count() > 1).Select(g => g.First().Text).ToList();
        if (duplicateHeadings.Count > 0)
            report.HeadingIssues.Add("تیتر تکراری: «" + PersianText.Truncate(duplicateHeadings[0], 60) + "».");

        // پیشنهاد لینک داخلی: عبارت‌هایی که در متن آمده‌اند و هنوز لینک نشده‌اند (فقط پیشنهاد؛ لینک خودکار هنگام نمایش هم ساخته می‌شود).
        if (linkTargets is { Count: > 0 } && stats.Text.Length > 0)
        {
            var linked = stats.Links.Select(l => l.Href.TrimEnd('/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var target in linkTargets)
            {
                if (report.Links.Count >= LiveLinkSuggestions)
                    break;
                if (string.Equals(target.Url, subject.Path, StringComparison.OrdinalIgnoreCase) || linked.Contains(target.Url))
                    continue;
                var phrase = target.Phrases.FirstOrDefault(p => InternalLinker.FindWhole(stats.Text, p) >= 0);
                if (phrase is not null && report.Links.All(l => l.Url != target.Url))
                    report.Links.Add(new SeoLinkSuggestion(phrase, target.Url));
            }
        }

        var natural = AiPatternGuard.Analyze(stats.Text);
        report.NaturalScore = natural.Score;
        report.Patterns.AddRange(natural.Findings.Take(8));

        // متن تقریباً تکراری را خود تحلیل‌گر با اثر انگشت سایت می‌سنجد (چک‌لیست)؛ اینجا فقط خطرهای متن خود صفحه.
        report.Risks.AddRange(SeoPenaltyGuard.Check(subject.EffectiveTitle, subject.EffectiveDescription, subject.BodyHtml, subject.FocusKeyword, kb));

        var thinLimit = subject.Kind switch
        {
            SeoEntityKind.BlogPost => SeoLimits.ThinContentWords * 2,
            SeoEntityKind.ContentPage or SeoEntityKind.StaticPage => 0,
            _ => SeoLimits.ThinContentWords,
        };
        report.Thin = thinLimit > 0 && stats.Words < thinLimit;

        if (report.Thin)
            report.Blockers.Add($"متن کوتاه است ({PersianText.Digits(stats.Words)} واژه)؛ صفحهٔ کم‌محتوا در گوگل رتبه نمی‌گیرد.");
        if (report.Risks.FirstOrDefault(r => r.Blocking) is { } risk)
            report.Blockers.Add(risk.Message);
        if (stats.Images.Any(i => string.IsNullOrWhiteSpace(i.Alt)))
            report.Blockers.Add("تصویری در متن بدون متن جایگزین (Alt) است؛ هنگام ذخیره خودکار Alt می‌گیرد ولی Alt دقیق بهتر است.");
        if (report.HeadingIssues.Count > 0 && stats.Headings.Any(h => h.Level == 1))
            report.Blockers.Add(report.HeadingIssues[0]);
    }
}
