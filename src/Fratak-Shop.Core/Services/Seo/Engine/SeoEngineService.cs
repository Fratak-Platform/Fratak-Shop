using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Knowledge;
using Fratak_Shop.Core.Services.Seo.Tools;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Data.Entities.SeoRedirects;
using Fratak_Shop.Data.Utilities.SeoManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Seo.Engine;

// ═══════════════════════════════════════════════════════════════════════════
//  سرویس اصلی موتور سئو (هماهنگ‌کنندهٔ همهٔ اجزا)
//  اجرای خودکار (۱ تا ۶ بار در روز، از Web/Infrastructure/Seo/SeoEngineWorker) یا دستی از پنل:
//   ۱) همهٔ صفحه‌های قابل سئو (محصول، دسته، برند، مقالهٔ منتشرشده، صفحهٔ محتوا) خوانده و نمره‌دهی می‌شوند.
//   ۲) پیشنهادهای در انتظاری که صفحه‌شان در این فاصله دستی ویرایش شده «کهنه» می‌شوند.
//   ۳) صف اولویت: صفحه‌های کم‌نمره و پربازدید اول (فرمول: (۱۰۰ − نمره) × (۱ + log بازدید) × وزن نوع صفحه).
//   ۴) برای هر صفحهٔ صف: تحقیق رقبا (کش‌شده) → انتخاب کلمهٔ کلیدی → نوشتن عنوان/توضیحات/متن/پرسش‌وپاسخ/alt →
//      پیشنهاد نامک → پیش‌بینی نمرهٔ بعد → ذخیرهٔ پیشنهاد فقط اگر واقعاً نمره را بالا ببرد.
//   ۵) به کارشناسان سئو اعلان داده می‌شود. هیچ چیز بدون تأیید انسان روی سایت نمی‌رود.
//  تأیید: بخش‌های انتخاب‌شده روی خود صفحه نوشته می‌شود (+ ریدایرکت ۳۰۱ برای نامک) و نمره تازه می‌شود.
//  رد: نسخهٔ بعدی بلافاصله ساخته می‌شود (متن‌های ردشده دیگر پیشنهاد نمی‌شوند و سبک از دلیل رد خوانده می‌شود).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>نتیجهٔ یک اجرای موتور.</summary>
public sealed record SeoRunResult(bool Ran, int Analyzed, int Suggested, int Failed, string Message);

/// <summary>نمای تحلیل یک صفحه برای صفحهٔ «تحلیل» پنل.</summary>
public sealed class SeoAnalysisView
{
    public required SeoSubject Subject { get; init; }
    public required SeoReport Report { get; init; }
    public IReadOnlyList<KeywordCandidate> Keywords { get; init; } = [];
    public string EditUrl { get; init; } = "";
    public IReadOnlyList<SeoSuggestion> History { get; init; } = [];
    public int TitleSuffixLength { get; init; }
    public string SiteName { get; init; } = "";
}

/// <summary>یک کلمهٔ نامزد با امتیازش (برای نمایش در صفحهٔ بررسی).</summary>
public sealed record SeoKeywordScore(string Phrase, double Score);

/// <summary>
/// خلاصهٔ تحقیقی که پیشنهاد بر اساس آن ساخته شد (ستون ResearchJson). در صفحهٔ بررسی زیر «از رقبا چه یاد گرفتیم» نمایش داده می‌شود.
/// </summary>
public sealed class SeoResearchSummary
{
    public string? Query { get; set; }
    public int Pages { get; set; }
    public List<string> Hosts { get; set; } = [];
    public int MedianWords { get; set; }
    public int AverageTitle { get; set; }
    public double LatinSlugRatio { get; set; }
    public List<string> Suggestions { get; set; } = [];
    public List<string> Terms { get; set; } = [];
    public List<string> Titles { get; set; } = [];
    public List<SeoKeywordScore> Keywords { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}

/// <summary>آمار داشبورد موتور سئو.</summary>
public sealed class SeoDashboard
{
    public int Pages { get; init; }
    public int AverageScore { get; init; }
    public int Weak { get; init; }      // زیر ۵۰
    public int Medium { get; init; }    // ۵۰ تا ۷۹
    public int Strong { get; init; }    // ۸۰ و بیشتر
    public int Pending { get; init; }
    public int AppliedLast30Days { get; init; }
    public int AverageGain { get; init; }
    public IReadOnlyList<SeoEngineRun> Runs { get; init; } = [];
    public IReadOnlyList<SeoScore> Worst { get; init; } = [];
    public IReadOnlyList<(SeoEntityKind Kind, int Pages, int Average)> ByKind { get; init; } = [];
    public IReadOnlyList<(SeoResearchKind Kind, int Ok, int Failed, string? LastSource)> Sources { get; init; } = [];
}

/// <summary>سرویس موتور سئو. پیاده‌سازی: <see cref="SeoEngineService"/>.</summary>
public interface ISeoEngineService
{
    /// <summary>اجرای کامل (نمره‌دهی همه + پیشنهاد برای صف). slotKey برای اجرای زمان‌بندی‌شده (یکتا بین چند سرور).</summary>
    Task<SeoRunResult> RunAsync(SeoRunTrigger trigger, string? slotKey, CancellationToken cancellationToken = default);

    /// <summary>تحلیل زندهٔ یک صفحه (بدون ذخیره، به‌جز به‌روزرسانی نمره).</summary>
    Task<SeoAnalysisView?> AnalyzeAsync(SeoEntityKind kind, Guid id, CancellationToken cancellationToken = default);

    /// <summary>ساخت فوری پیشنهاد برای یک صفحه (پیشنهاد در انتظار قبلی جایگزین می‌شود).</summary>
    Task<OperationResult<Guid>> GenerateAsync(SeoEntityKind kind, Guid id, CancellationToken cancellationToken = default);

    /// <summary>تأیید و اعمال بخش‌های انتخاب‌شدهٔ یک پیشنهاد.</summary>
    Task<OperationResult> ApproveAsync(Guid suggestionId, SeoFields fields, Guid? userId, string? userName, CancellationToken cancellationToken = default);

    /// <summary>رد (کل یا بخشی) و ساخت فوری نسخهٔ بعد. Data = شناسهٔ نسخهٔ تازه (null اگر نسخهٔ بهتری پیدا نشد).</summary>
    Task<OperationResult<Guid?>> RejectAsync(Guid suggestionId, SeoFields fields, string? reason, Guid? userId, string? userName, CancellationToken cancellationToken = default);

    Task<SeoDashboard> GetDashboardAsync(CancellationToken cancellationToken = default);

    /// <summary>آیا این نوبت زمان‌بندی‌شده قبلاً اجرا شده؟ (بررسی ارزان سرویس پس‌زمینه)</summary>
    Task<bool> HasRunAsync(string slotKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// نمره‌دهی دوبارهٔ چند صفحهٔ مشخص (بدون ساخت پیشنهاد). محل استفاده: ویرایش گروهی سئو بعد از ذخیره،
    /// تا جدول نمره‌ها همان لحظه درست باشد. خروجی = تعداد صفحه‌های نمره‌گرفته.
    /// </summary>
    Task<int> RefreshScoresAsync(SeoEntityKind kind, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// «سئوی هوشمند درجا» (مرحلهٔ شانزدهم): همان موتور، روی مقدارهای فعلی فرم (حتی صفحه‌ای که هنوز ذخیره نشده).
    /// هیچ چیز ذخیره نمی‌شود؛ چند گزینهٔ عنوان/توضیحات، کلمهٔ کلیدی، نامک، پرسش‌وپاسخ و متن پیشنهادی برمی‌گردد
    /// تا کاربر در همان صفحه انتخاب کند و خودش ذخیره بزند.
    /// </summary>
    Task<SeoDraft> DraftAsync(SeoDraftInput input, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISeoEngineService"/>
public sealed partial class SeoEngineService(
    FratakShopDbContext context,
    IStoreSettingService settings,
    ISeoWebSource web,
    IStaffNotificationService notifications,
    IMemoryCache cache,
    ILoggerFactory loggerFactory,
    ISeoKnowledgeService knowledge) : ISeoEngineService
{
    /// <summary>جلوگیری از دو اجرای هم‌زمان در یک سرور (اجرای دستی + زمان‌بندی‌شده).</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>آیا همین الان اجرایی در این سرور در جریان است؟ (برای دکمهٔ «همین الان اجرا کن»)</summary>
    public static bool IsRunning => Gate.CurrentCount == 0;

    /// <summary>
    /// حداکثر عبارت تازه‌ای که در هر اجرا از اینترنت تحقیق می‌شود (هر عبارت ≈ ۸ درخواست: دو پیشنهاد جست‌وجو، نتایج و
    /// تا ۶ صفحهٔ رقیب). مرحلهٔ شانزدهم: از ۱۵ به ۲۵ رسید و نتایج در جدول دانش هم ذخیره می‌شوند.
    /// </summary>
    public const int ResearchQueriesPerRun = 25;

    /// <summary>بودجهٔ درخواست اینترنتی هر عبارت (پیشنهاد، پیشنهاد تکمیلی، نتایج و صفحه‌های رقیب).</summary>
    public const int RequestsPerQuery = 10;

    /// <summary>حداقل افزایش نمره برای ثبت پیشنهاد (پیشنهادهای بی‌اثر وقت کارشناس را نمی‌گیرند).</summary>
    public const int MinimumGain = 2;

    private const string SiteIndexCacheKey = "seo-engine:site-index";

    private readonly ILogger<SeoEngineService> _logger = loggerFactory.CreateLogger<SeoEngineService>();

    // ── اجرای کامل ───────────────────────────────────────────────────────

    public async Task<SeoRunResult> RunAsync(SeoRunTrigger trigger, string? slotKey, CancellationToken cancellationToken = default)
    {
        var current = await settings.GetAsync(cancellationToken);
        if (trigger == SeoRunTrigger.Scheduled && !current.IsSeoEngineEnabled)
            return new SeoRunResult(false, 0, 0, 0, "موتور سئو در تنظیمات خاموش است.");
        // نوبت زمان‌بندی‌شده‌ای که قبلاً (در این سرور یا سرور دیگر) اجرا شده، دوباره اجرا نمی‌شود.
        if (slotKey is not null && await context.SeoEngineRuns.AnyAsync(r => r.SlotKey == slotKey, cancellationToken))
            return new SeoRunResult(false, 0, 0, 0, "این نوبت قبلاً اجرا شده است.");
        if (!await Gate.WaitAsync(0, cancellationToken))
            return new SeoRunResult(false, 0, 0, 0, "اجرای دیگری در جریان است.");

        try
        {
            var now = DateTimeOffset.UtcNow;
            var run = new SeoEngineRun { SlotKey = slotKey, Trigger = trigger, StartedAt = now };
            context.SeoEngineRuns.Add(run);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // سرور دیگری همین نوبت را گرفته است.
                context.Entry(run).State = EntityState.Detached;
                return new SeoRunResult(false, 0, 0, 0, "این نوبت قبلاً اجرا شده است.");
            }

            try
            {
                var result = await RunCoreAsync(run, current, cancellationToken);
                run.FinishedAt = DateTimeOffset.UtcNow;
                run.Analyzed = result.Analyzed;
                run.Suggested = result.Suggested;
                run.Failed = result.Failed;
                run.Message = Cut(result.Message, 1000);
                await context.SaveChangesAsync(CancellationToken.None);
                return result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "SEO engine run failed");
                context.ChangeTracker.Clear();
                var failed = await context.SeoEngineRuns.AsTracking().FirstAsync(r => r.Id == run.Id, CancellationToken.None);
                failed.FinishedAt = DateTimeOffset.UtcNow;
                failed.Message = Cut("خطا: " + ex.Message, 1000);
                await context.SaveChangesAsync(CancellationToken.None);
                return new SeoRunResult(true, 0, 0, 1, failed.Message!);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<SeoRunResult> RunCoreAsync(SeoEngineRun run, Fratak_Shop.Core.DTOs.Settings.StoreSettingDto current, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var store = StoreFactsFrom(current);
        var subjects = await LoadSubjectsAsync(null, null, cancellationToken);
        // نمای این اجرا جداست (پیشنهادها در آن «رزرو» می‌شوند)؛ کش مشترک فقط پاک می‌شود تا از داده‌های تازه ساخته شود.
        var site = SeoSiteIndex.Build(subjects);
        cache.Remove(SiteIndexCacheKey);

        // ۱) نمره‌دهی همه
        var reports = new Dictionary<(SeoEntityKind, Guid), SeoReport>();
        var scores = await context.SeoScores.AsTracking().ToListAsync(cancellationToken);
        var scoreMap = scores.ToDictionary(s => (s.Kind, s.EntityId));
        foreach (var subject in subjects)
        {
            var report = SeoAnalyzer.Analyze(subject, site, store);
            reports[(subject.Kind, subject.EntityId)] = report;
            Upsert(scoreMap, subject, report, now);
        }
        var alive = subjects.Select(s => (s.Kind, s.EntityId)).ToHashSet();
        foreach (var orphan in scores.Where(s => !alive.Contains((s.Kind, s.EntityId))))
            context.SeoScores.Remove(orphan); // صفحهٔ حذف/غیرفعال‌شده
        await context.SaveChangesAsync(cancellationToken);

        // ۲) پیشنهادهای کهنه
        var bySubject = subjects.ToDictionary(s => (s.Kind, s.EntityId));
        var pending = await context.SeoSuggestions.AsTracking().Where(s => s.Status == SeoSuggestionStatus.Pending).ToListAsync(cancellationToken);
        foreach (var suggestion in pending)
        {
            if (!bySubject.TryGetValue((suggestion.Kind, suggestion.EntityId), out var subject) || subject.ContentHash != suggestion.ContentHash)
                suggestion.Status = SeoSuggestionStatus.Stale;
        }
        await context.SaveChangesAsync(cancellationToken);

        // ۳) صف اولویت
        var since = now.AddDays(-90);
        var history = await context.SeoSuggestions.AsNoTracking()
            .Where(s => s.CreatedAt >= since && (s.Status == SeoSuggestionStatus.Pending || s.Status == SeoSuggestionStatus.Applied || s.Status == SeoSuggestionStatus.Rejected))
            .Select(s => new { s.Kind, s.EntityId, s.ContentHash, s.Status })
            .ToListAsync(cancellationToken);
        var blocked = history
            .Where(h => h.Status == SeoSuggestionStatus.Pending || bySubject.TryGetValue((h.Kind, h.EntityId), out var x) && x.ContentHash == h.ContentHash)
            .Select(h => (h.Kind, h.EntityId))
            .ToHashSet();

        var queue = subjects
            .Where(s => !s.NoIndex && !s.HasCanonicalElsewhere && !blocked.Contains((s.Kind, s.EntityId)))
            .Select(s => (Subject: s, Report: reports[(s.Kind, s.EntityId)]))
            .Where(x => x.Report.Score < 100 && x.Report.WeakFields != SeoFields.None)
            .OrderByDescending(x => Priority(x.Subject, x.Report.Score))
            .Take(Math.Clamp(current.SeoEngineBatchSize, 5, 200))
            .ToList();

        var suggested = 0;
        var failed = 0;
        var researchErrors = new HashSet<string>(StringComparer.Ordinal);
        var stats = new SeoRunStats
        {
            Scored = subjects.Count,
            AverageScore = reports.Count == 0 ? 0 : reports.Values.Average(r => r.Score),
            Perfect = reports.Values.Count(r => r.Score >= 100),
            Queued = queue.Count,
        };
        if (queue.Count > 0)
        {
            var models = await BuildModelsAsync(subjects, cancellationToken);
            var research = CreateResearch(current, current.SeoCompetitorResearch ? ResearchQueriesPerRun * RequestsPerQuery : 0, store.SiteName);
            foreach (var (subject, report) in queue)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var built = await BuildSuggestionAsync(subject, report, store, site, models, research, current.SeoAllowSlugChanges,
                        variant: 0, previous: null, rejectedFields: SeoFields.None, style: WriterStyle.None, avoid: AvoidSet.Empty, cancellationToken);
                    if (built.Insight is { Errors.Count: > 0 } insight)
                        researchErrors.UnionWith(insight.Errors);
                    stats.Observe(built.Insight);
                    if (built.Suggestion is not null)
                    {
                        stats.Gains.Add(built.Suggestion.ScoreAfter - built.Suggestion.ScoreBefore);
                        context.SeoSuggestions.Add(built.Suggestion);
                        await context.SaveChangesAsync(cancellationToken);
                        suggested++;
                        // متن‌های پیشنهادی «رزرو» می‌شوند تا صفحهٔ بعدی همین اجرا عنوان/توضیحات تکراری نگیرد.
                        site.Replace(subject, subject.With(SeoProposal.FromJson(built.Suggestion.ProposalJson), built.Suggestion.Fields));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    _logger.LogWarning(ex, "SEO suggestion failed for {Kind} {Id}", subject.Kind, subject.EntityId);
                    context.ChangeTracker.Clear();
                    // ردیف اجرا هم با Clear جدا شد؛ دوباره وصلش می‌کنیم تا پایان اجرا (FinishedAt و آمار) ذخیره شود.
                    context.SeoEngineRuns.Attach(run);
                }
            }
        }

        stats.Failed = failed;
        stats.ResearchErrors.UnionWith(researchErrors);
        var message = stats.Report();

        // گزارش کوتاه بعد از هر اجرا (مرحلهٔ هفتم – اصلاحیه): اجرای دستی همیشه، اجرای زمان‌بندی‌شده وقتی پیشنهاد تازه یا خطا هست.
        if (suggested > 0 || failed > 0 || run.Trigger == SeoRunTrigger.Manual)
        {
            await notifications.NotifyAsync(new StaffAlert(StaffNotificationKind.SeoSuggestions,
                suggested > 0 ? $"{PersianText.Digits(suggested)} پیشنهاد سئوی تازه آمادهٔ بررسی است" : "گزارش اجرای موتور سئو",
                Cut(message, 480),
                suggested > 0 ? "/admin/seo/review" : "/admin/seo/engine", [AdminPermissions.SeoManage]));
        }

        return new SeoRunResult(true, subjects.Count, suggested, failed, message);
    }

    /// <summary>
    /// اولویت صف: صفحه‌های کم‌نمره و پربازدید اول. وزن نوع: دسته ۱٫۳ (صفحهٔ فرود پرجست‌وجو)، محصول ۱٫۲، مقاله ۱،
    /// برند ۰٫۹، صفحهٔ محتوا ۰٫۵. صفحهٔ تازه (زیر ۱۴ روز) کمی جلو می‌افتد تا از روز اول درست ایندکس شود.
    /// </summary>
    public static double Priority(SeoSubject subject, int score)
    {
        var kindWeight = subject.Kind switch
        {
            SeoEntityKind.StaticPage => 1.4, // صفحهٔ اصلی و صفحه‌های ثابت: پربازدیدترین ورودی‌های سایت
            SeoEntityKind.Category => 1.3,
            SeoEntityKind.Product => 1.2,
            SeoEntityKind.BlogPost => 1.0,
            SeoEntityKind.Brand => 0.9,
            _ => 0.5,
        };
        var fresh = DateTimeOffset.UtcNow - subject.CreatedAt < SlugAdvisor.YoungAge ? 1.2 : 1.0;
        return (100 - score) * (1 + Math.Log10(1 + Math.Max(0, subject.Views))) * kindWeight * fresh;
    }

    // ── ساخت پیشنهاد ────────────────────────────────────────────────────

    private sealed record BuildResult(SeoSuggestion? Suggestion, CompetitorInsight? Insight, string? Reason);

    /// <summary>متن‌هایی که قبلاً برای این صفحه رد شده‌اند.</summary>
    private sealed class AvoidSet
    {
        public static readonly AvoidSet Empty = new();
        public HashSet<string> Texts { get; } = new(StringComparer.Ordinal);
        public List<string> Keywords { get; } = [];
        public HashSet<string> Slugs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> BodyHashes { get; } = new(StringComparer.Ordinal);
        public HashSet<string> FaqHashes { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>مدل‌های زبانی آموزش‌دیده روی متن‌های سایت (یک بار در هر اجرا).</summary>
    /// <summary>Google = عبارت‌های Search Console هر صفحه (کلید: مسیر یکسان‌شده با SeoPaths.Normalize).</summary>
    private sealed record EngineModels(TrigramLanguageModel Language, TfIdf TfIdf, IReadOnlyDictionary<string, int> InternalSearches,
        IReadOnlyDictionary<string, IReadOnlyList<SearchQueryStat>> Google);

    private async Task<BuildResult> BuildSuggestionAsync(SeoSubject subject, SeoReport before, StoreFacts store, SeoSiteIndex site,
        EngineModels models, SeoResearchService research, bool allowSlug, int variant, SeoSuggestion? previous,
        SeoFields rejectedFields, WriterStyle style, AvoidSet avoid, CancellationToken cancellationToken)
    {
        var kb = await knowledge.GetAsync(cancellationToken);
        var insight = await ResearchSubjectAsync(subject, research, cancellationToken);

        // کلمهٔ کلیدی: کلمهٔ انسانی هرگز عوض نمی‌شود؛ فقط وقتی خالی است پیشنهاد می‌شود.
        var proposal = new SeoProposal();
        var keyword = PersianText.Clean(subject.FocusKeyword);
        var candidates = KeywordPlanner.Candidates(subject, insight, site, models.InternalSearches, models.TfIdf,
            models.Google.GetValueOrDefault(SeoPaths.Normalize(subject.Path)), kb);
        KeywordCandidate? chosen = null;
        if (keyword.Length == 0)
        {
            chosen = KeywordPlanner.Choose(candidates, avoid.Keywords);
            if (chosen is not null)
            {
                keyword = chosen.Phrase;
                proposal.FocusKeyword = keyword;
            }
        }

        var withKeyword = subject.With(proposal, SeoFields.FocusKeyword);
        var weak = SeoAnalyzer.Analyze(withKeyword, site, store).WeakFields;

        var ctx = new WriterContext
        {
            Subject = withKeyword, Store = store, Keyword = keyword, Insight = insight, Language = models.Language,
            Site = site, Variant = variant, Style = style, Avoid = avoid.Texts,
        };

        // نگهبان اسپم (مرحلهٔ شانزدهم): عبارت‌های اغراق‌آمیز/جریمه‌دار جدول دانش از عنوان و توضیحات حذف می‌شوند.
        if (weak.HasFlag(SeoFields.Title) && ContentWriter.BestTitle(ctx) is { } rawTitle
            && SpamGuard.CleanTitle(rawTitle, kb) is { Length: > 0 } title && Differs(title, subject.EffectiveTitle))
            proposal.Title = title;
        if (weak.HasFlag(SeoFields.MetaDescription) && ContentWriter.BestMeta(ctx) is { } rawMeta
            && SpamGuard.CleanText(rawMeta, kb) is { Length: > 0 } meta && Differs(meta, subject.EffectiveDescription))
            proposal.MetaDescription = meta;
        if (weak.HasFlag(SeoFields.Description) && ContentWriter.Body(ctx) is { } body && !avoid.BodyHashes.Contains(PersianText.Hash(body))
            && FitsColumn(subject.Kind, body))
            proposal.DescriptionHtml = body;
        if (weak.HasFlag(SeoFields.Faq) && subject.Faq.Count < 2)
        {
            var faq = ContentWriter.Faq(ctx);
            if (faq.Count >= 2 && !avoid.FaqHashes.Contains(PersianText.Hash(SeoFaq.Serialize(faq))))
                proposal.Faq = faq;
        }
        if (weak.HasFlag(SeoFields.ImageAlt))
            proposal.ImageAlts = ContentWriter.ImageAlts(ctx);
        SlugAdvice? slug = null;
        if (allowSlug && weak.HasFlag(SeoFields.Slug))
        {
            slug = SlugAdvisor.Advise(subject, keyword, insight, site, DateTimeOffset.UtcNow);
            if (slug is not null && !avoid.Slugs.Contains(slug.Slug))
                proposal.Slug = slug.Slug;
            else
                slug = null;
        }

        // نسخهٔ بعد از رد: بخش‌هایی که کارشناس رد نکرده، همان متن نسخهٔ قبل می‌ماند.
        if (previous is not null && rejectedFields != SeoFields.None)
            KeepUnrejected(proposal, SeoProposal.FromJson(previous.ProposalJson), previous.Fields & ~rejectedFields);

        // هرس: بخشی که نمره را بالا نمی‌برد، حذف می‌شود (کارشناس فقط تغییرهای مفید را می‌بیند).
        var fields = proposal.Fields;
        var scoreAll = SeoAnalyzer.Analyze(subject.With(proposal, fields), site, store).Score;
        foreach (var field in new[] { SeoFields.Title, SeoFields.MetaDescription, SeoFields.Description, SeoFields.Faq, SeoFields.ImageAlt, SeoFields.Slug })
        {
            if (!fields.HasFlag(field))
                continue;
            var without = SeoAnalyzer.Analyze(subject.With(proposal, fields & ~field), site, store).Score;
            if (without >= scoreAll)
            {
                Clear(proposal, field);
                fields &= ~field;
            }
        }

        if (fields == SeoFields.None)
            return new BuildResult(null, insight, "تغییر مفیدی پیدا نشد.");
        var after = subject.With(proposal, fields);
        var afterReport = SeoAnalyzer.Analyze(after, site, store);
        if (afterReport.Score < before.Score + MinimumGain && !fields.HasFlag(SeoFields.FocusKeyword))
            return new BuildResult(null, insight, "نمره به اندازهٔ کافی بالا نمی‌رود.");

        var reasons = BuildReasons(subject, before, afterReport, chosen, slug, insight, fields);
        var suggestion = new SeoSuggestion
        {
            Kind = subject.Kind,
            EntityId = subject.EntityId,
            EntityTitle = Cut(subject.Name, 200)!,
            Path = Cut(subject.Path, 400)!,
            Status = SeoSuggestionStatus.Pending,
            Fields = fields,
            ScoreBefore = before.Score,
            ScoreAfter = afterReport.Score,
            Variant = variant,
            ProposalJson = proposal.ToJson(),
            CurrentJson = CurrentValues(subject, fields).ToJson(),
            ReasonsJson = SeoJson.Serialize(reasons),
            ResearchJson = SeoJson.Serialize(ResearchSummary(insight, candidates)),
            ContentHash = subject.ContentHash,
        };
        return new BuildResult(suggestion, insight, null);
    }

    private static bool Differs(string a, string b) => PersianText.Normalize(a) != PersianText.Normalize(b);

    private static void KeepUnrejected(SeoProposal target, SeoProposal old, SeoFields keep)
    {
        if (keep.HasFlag(SeoFields.Title)) target.Title = old.Title;
        if (keep.HasFlag(SeoFields.MetaDescription)) target.MetaDescription = old.MetaDescription;
        if (keep.HasFlag(SeoFields.FocusKeyword)) target.FocusKeyword = old.FocusKeyword;
        if (keep.HasFlag(SeoFields.Slug)) target.Slug = old.Slug;
        if (keep.HasFlag(SeoFields.Description)) target.DescriptionHtml = old.DescriptionHtml;
        if (keep.HasFlag(SeoFields.Faq)) target.Faq = old.Faq;
        if (keep.HasFlag(SeoFields.ImageAlt)) target.ImageAlts = old.ImageAlts;
    }

    private static void Clear(SeoProposal proposal, SeoFields field)
    {
        switch (field)
        {
            case SeoFields.Title: proposal.Title = null; break;
            case SeoFields.MetaDescription: proposal.MetaDescription = null; break;
            case SeoFields.Description: proposal.DescriptionHtml = null; break;
            case SeoFields.Faq: proposal.Faq = []; break;
            case SeoFields.ImageAlt: proposal.ImageAlts = []; break;
            case SeoFields.Slug: proposal.Slug = null; break;
        }
    }

    /// <summary>مقدارهای فعلی همان بخش‌ها (برای ستون «فعلی» در صفحهٔ بررسی).</summary>
    private static SeoProposal CurrentValues(SeoSubject subject, SeoFields fields) => new()
    {
        Title = fields.HasFlag(SeoFields.Title) ? subject.EffectiveTitle : null,
        MetaDescription = fields.HasFlag(SeoFields.MetaDescription) ? subject.EffectiveDescription : null,
        FocusKeyword = fields.HasFlag(SeoFields.FocusKeyword) ? subject.FocusKeyword ?? "" : null,
        Slug = fields.HasFlag(SeoFields.Slug) ? subject.Slug : null,
        DescriptionHtml = fields.HasFlag(SeoFields.Description) ? subject.BodyHtml ?? "" : null,
        Faq = fields.HasFlag(SeoFields.Faq) ? subject.Faq.ToList() : [],
        ImageAlts = fields.HasFlag(SeoFields.ImageAlt)
            ? subject.Images.Select((i, index) => new SeoImageAlt(i.Id, index, i.Alt ?? "")).ToList()
            : [],
    };

    /// <summary>
    /// دلیل‌های قابل فهم برای کارشناس: چه چیزهایی درست شد، چه کارهایی فقط از انسان برمی‌آید، و از رقبا چه یاد گرفته شد.
    /// </summary>
    private static List<string> BuildReasons(SeoSubject subject, SeoReport before, SeoReport after, KeywordCandidate? keyword,
        SlugAdvice? slug, CompetitorInsight? insight, SeoFields fields)
    {
        var reasons = new List<string>();
        if (keyword is not null && fields.HasFlag(SeoFields.FocusKeyword))
            reasons.Add($"کلمهٔ کلیدی «{keyword.Phrase}» انتخاب شد" + (keyword.Reasons.Count > 0 ? ": " + string.Join("، ", keyword.Reasons) + "." : "."));

        var old = before.Checks.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().Level);
        foreach (var check in after.Checks.Where(c => c.Level == SeoCheckLevel.Good && (!old.TryGetValue(c.Id, out var level) || level != SeoCheckLevel.Good)))
            reasons.Add("✓ " + check.Message);
        if (slug is not null && fields.HasFlag(SeoFields.Slug))
            reasons.Add("نامک: " + slug.Reason);
        if (fields.HasFlag(SeoFields.Description))
            reasons.Add("متن فعلی دست‌نخورده در ابتدای متن جدید مانده و فقط بخش‌های تکمیلی (بر اساس مشخصات و شرایط فروشگاه) اضافه شده است.");

        if (insight is { PagesAnalyzed: > 0 })
        {
            reasons.Add($"تحلیل {PersianText.Digits(insight.PagesAnalyzed)} صفحهٔ برتر نتایج ({string.Join("، ", insight.Hosts.Take(4))}): میانهٔ متن رقبا {PersianText.Digits(insight.MedianWords)} واژه است.");
            if (insight.MedianWords > after.Words * 1.5 && insight.MedianWords > 200)
                reasons.Add($"⚠ کار انسانی: متن رقبا بلندتر است ({PersianText.Digits(insight.MedianWords)} در برابر {PersianText.Digits(after.Words)} واژه)؛ نوشتن نقد و بررسی یا تجربهٔ استفاده توسط نویسنده پیشنهاد می‌شود.");
            if (insight.CommonTerms.Count > 0)
                reasons.Add("واژه‌های پرتکرار صفحه‌های برتر (برای نویسنده): " + string.Join("، ", insight.CommonTerms.Take(10).Select(t => t.Word)) + ".");
        }
        else if (insight is { SampleTitles.Count: > 0 })
            reasons.Add($"عنوان‌های {PersianText.Digits(insight.SampleTitles.Count)} نتیجهٔ برتر بررسی شد.");

        foreach (var check in after.Checks.Where(c => c.Level == SeoCheckLevel.Bad && c.Fix == SeoFields.None))
            reasons.Add("⚠ کار انسانی: " + check.Message);
        if (insight is { Errors.Count: > 0 })
            reasons.Add($"منبع «{string.Join("، ", insight.Errors)}» در دسترس نبود؛ پیشنهاد با داده‌های خود فروشگاه ساخته شد.");
        return reasons;
    }

    private static SeoResearchSummary ResearchSummary(CompetitorInsight? insight, IReadOnlyList<KeywordCandidate> candidates) => new()
    {
        Query = insight?.Query,
        Pages = insight?.PagesAnalyzed ?? 0,
        Hosts = insight?.Hosts ?? new List<string>(),
        MedianWords = insight?.MedianWords ?? 0,
        AverageTitle = insight is null ? 0 : (int)Math.Round(insight.AverageTitleLength),
        LatinSlugRatio = insight?.LatinSlugRatio ?? 0,
        Suggestions = insight?.Suggestions.Take(8).ToList() ?? new List<string>(),
        Terms = insight?.CommonTerms.Take(12).Select(t => t.Word).ToList() ?? new List<string>(),
        Titles = insight?.SampleTitles ?? new List<string>(),
        Keywords = candidates.Take(6).Select(c => new SeoKeywordScore(c.Phrase, Math.Round(c.Score, 1))).ToList(),
        Errors = insight?.Errors ?? new List<string>(),
    };

    // ── یک صفحه: تحلیل، ساخت، تأیید، رد ───────────────────────────────

    public async Task<SeoAnalysisView?> AnalyzeAsync(SeoEntityKind kind, Guid id, CancellationToken cancellationToken = default)
    {
        var current = await settings.GetAsync(cancellationToken);
        var store = StoreFactsFrom(current);
        var subject = (await LoadSubjectsAsync(kind, id, cancellationToken)).FirstOrDefault();
        if (subject is null)
            return null;
        var site = await GetSiteIndexAsync(cancellationToken);
        var report = SeoAnalyzer.Analyze(subject, site, store);
        await SaveScoreAsync(subject, report, cancellationToken);

        // کلمه‌های نامزد فقط از کش (بدون درخواست اینترنتی تا صفحه سریع باز شود).
        var research = CreateResearch(current, 0, store.SiteName);
        var insight = await research.ResearchAsync(KeywordPlanner.ResearchQuery(subject), kind is not (SeoEntityKind.BlogPost or SeoEntityKind.ContentPage or SeoEntityKind.StaticPage), cancellationToken);
        var kb = await knowledge.GetAsync(cancellationToken);
        var searches = await InternalSearchesAsync(cancellationToken);
        var google = (await GoogleQueriesAsync(SeoPaths.Normalize(subject.Path), cancellationToken)).GetValueOrDefault(SeoPaths.Normalize(subject.Path));
        var history = await context.SeoSuggestions.AsNoTracking()
            .Where(s => s.Kind == kind && s.EntityId == id)
            .OrderByDescending(s => s.CreatedAt)
            .Take(15)
            .ToListAsync(cancellationToken);

        return new SeoAnalysisView
        {
            Subject = subject,
            Report = report,
            Keywords = KeywordPlanner.Candidates(subject, insight, site, searches, gsc: google, knowledge: kb).Take(8).ToList(),
            EditUrl = EditUrl(kind, id),
            History = history,
            TitleSuffixLength = store.TitleSuffixLength,
            SiteName = store.SiteName,
        };
    }

    public async Task<OperationResult<Guid>> GenerateAsync(SeoEntityKind kind, Guid id, CancellationToken cancellationToken = default)
    {
        var current = await settings.GetAsync(cancellationToken);
        var subject = (await LoadSubjectsAsync(kind, id, cancellationToken)).FirstOrDefault();
        if (subject is null)
            return OperationResult<Guid>.NotFound("صفحه پیدا نشد (حذف یا غیرفعال شده است).");
        if (subject.NoIndex)
            return OperationResult<Guid>.Error("این صفحه noindex است؛ اول noindex را از تنظیمات سئوی صفحه بردارید.");

        var previous = await context.SeoSuggestions.AsTracking()
            .Where(s => s.Kind == kind && s.EntityId == id && s.Status == SeoSuggestionStatus.Pending)
            .ToListAsync(cancellationToken);
        var variant = await context.SeoSuggestions.Where(s => s.Kind == kind && s.EntityId == id)
            .Select(s => (int?)s.Variant).MaxAsync(cancellationToken) is { } max ? max + 1 : 0;

        var result = await GenerateCoreAsync(subject, current, variant, null, SeoFields.None, WriterStyle.None, cancellationToken);
        if (result.Suggestion is null)
            return OperationResult<Guid>.Error(result.Reason ?? "پیشنهاد مفیدی پیدا نشد؛ این صفحه از نظر موتور در وضعیت خوبی است.");

        foreach (var old in previous)
            old.Status = SeoSuggestionStatus.Superseded;
        context.SeoSuggestions.Add(result.Suggestion);
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult<Guid>.Success(result.Suggestion.Id, "پیشنهاد تازه ساخته شد.");
    }

    private async Task<BuildResult> GenerateCoreAsync(SeoSubject subject, Fratak_Shop.Core.DTOs.Settings.StoreSettingDto current, int variant,
        SeoSuggestion? previous, SeoFields rejectedFields, WriterStyle style, CancellationToken cancellationToken)
    {
        var store = StoreFactsFrom(current);
        var site = await GetSiteIndexAsync(cancellationToken);
        var before = SeoAnalyzer.Analyze(subject, site, store);
        var models = await BuildModelsAsync(null, cancellationToken);
        // ساخت دستی: حداکثر یک عبارت تازه از اینترنت (همراه پیشنهاد تکمیلی) تا پاسخ سریع بماند.
        var research = CreateResearch(current, current.SeoCompetitorResearch ? RequestsPerQuery : 0, store.SiteName);
        var avoid = await AvoidAsync(subject.Kind, subject.EntityId, cancellationToken);
        return await BuildSuggestionAsync(subject, before, store, site, models, research, current.SeoAllowSlugChanges,
            variant, previous, rejectedFields, style, avoid, cancellationToken);
    }

    public async Task<OperationResult> ApproveAsync(Guid suggestionId, SeoFields fields, Guid? userId, string? userName, CancellationToken cancellationToken = default)
    {
        var suggestion = await context.SeoSuggestions.AsTracking().FirstOrDefaultAsync(s => s.Id == suggestionId, cancellationToken);
        if (suggestion is null)
            return OperationResult.NotFound("پیشنهاد پیدا نشد.");
        if (suggestion.Status != SeoSuggestionStatus.Pending)
            return OperationResult.Error("این پیشنهاد دیگر در انتظار بررسی نیست.");
        fields &= suggestion.Fields;
        if (fields == SeoFields.None)
            return OperationResult.Error("هیچ بخشی برای اعمال انتخاب نشده است.");

        var subject = (await LoadSubjectsAsync(suggestion.Kind, suggestion.EntityId, cancellationToken)).FirstOrDefault();
        if (subject is null || subject.ContentHash != suggestion.ContentHash)
        {
            suggestion.Status = SeoSuggestionStatus.Stale;
            await context.SaveChangesAsync(cancellationToken);
            return OperationResult.Error(subject is null
                ? "صفحه حذف یا غیرفعال شده است؛ پیشنهاد کنار گذاشته شد."
                : "صفحه بعد از ساخت این پیشنهاد ویرایش شده است؛ پیشنهاد کهنه شد. از «ساخت دوباره» نسخهٔ تازه بگیرید.");
        }

        var proposal = SeoProposal.FromJson(suggestion.ProposalJson);
        var now = DateTimeOffset.UtcNow;
        var error = await ApplyAsync(suggestion.Kind, suggestion.EntityId, proposal, fields, subject, userId, now, cancellationToken);
        if (error is not null)
        {
            context.ChangeTracker.Clear(); // تغییرهای نیمه‌کاره در تأیید گروهی با مورد بعدی ذخیره نشوند
            return OperationResult.Error(error);
        }

        suggestion.Status = SeoSuggestionStatus.Applied;
        suggestion.AppliedFields = fields;
        suggestion.ReviewedAt = now;
        suggestion.ReviewedByUserId = userId;
        suggestion.ReviewedByName = Cut(userName, 150);

        // محافظ طول ستون‌ها: قبل از ذخیره، هر متن تغییرکرده با سقف ستونش در پایگاه داده مقایسه می‌شود.
        // قبلاً متن بلند (مثلاً توضیح برند بیش از ۱۰۰۰ نویسه) خطای SQL «String or binary data would be truncated»
        // می‌داد و کل صفحه از کار می‌افتاد؛ حالا پیام فارسی برمی‌گردد و هیچ چیز نیمه‌کاره ذخیره نمی‌شود.
        if (DbLengthGuard.FindOverflow(context) is { } overflow)
        {
            context.ChangeTracker.Clear();
            return OperationResult.Error(overflow + "؛ پیشنهاد اعمال نشد. «ساخت دوباره» را بزنید یا متن را در صفحهٔ ویرایش کوتاه کنید.");
        }
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // هر خطای دیگر پایگاه داده (تکراری بودن نامک، قطع اتصال و ...) هم صفحه را خراب نمی‌کند.
            // ChangeTracker پاک می‌شود تا تأیید گروهی بعدی‌ها با همین DbContext سالم ادامه پیدا کند.
            context.ChangeTracker.Clear();
            _logger.LogError(ex, "SEO suggestion {Id} could not be applied", suggestionId);
            return OperationResult.Error("ذخیرهٔ پیشنهاد در پایگاه داده ناموفق بود (جزئیات در گزارش خطاهای سرور ثبت شد). صفحه تغییری نکرد.");
        }

        // نمره و نمای سایت را همان لحظه تازه کن.
        var after = (await LoadSubjectsAsync(suggestion.Kind, suggestion.EntityId, cancellationToken)).FirstOrDefault();
        cache.Remove(SiteIndexCacheKey);
        if (after is not null)
        {
            var site = await GetSiteIndexAsync(cancellationToken);
            var store = StoreFactsFrom(await settings.GetAsync(cancellationToken));
            await SaveScoreAsync(after, SeoAnalyzer.Analyze(after, site, store), cancellationToken);
        }
        await LearnAsync(subject, proposal, fields, accepted: true, cancellationToken);
        return OperationResult.Success("پیشنهاد روی صفحه اعمال شد.");
    }

    /// <summary>
    /// آیا متن توضیح ساخته‌شده در ستون «Description» این نوع صفحه جا می‌شود؟ (از روی مدل EF، نه عدد ثابت)
    /// اگر ستون سقف داشته باشد و متن بلندتر باشد، اصلاً پیشنهاد نمی‌شود تا کارشناس پیشنهادی نبیند که قابل اعمال نیست.
    /// </summary>
    private bool FitsColumn(SeoEntityKind kind, string html)
    {
        var clr = kind switch
        {
            SeoEntityKind.Product => typeof(Fratak_Shop.Data.Entities.Products.Product),
            SeoEntityKind.Category => typeof(Fratak_Shop.Data.Entities.Categories.Category),
            SeoEntityKind.Brand => typeof(Fratak_Shop.Data.Entities.Brands.Brand),
            _ => null,
        };
        var max = clr is null ? null : context.Model.FindEntityType(clr)?.FindProperty("Description")?.GetMaxLength();
        return max is not > 0 || html.SanitizeText().Length <= max;
    }

    /// <summary>نوشتن بخش‌های تأییدشده روی خود صفحه. خطا = متن پیام (هیچ چیز ذخیره نمی‌شود).</summary>
    private async Task<string?> ApplyAsync(SeoEntityKind kind, Guid id, SeoProposal proposal, SeoFields fields, SeoSubject subject,
        Guid? userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        string? newSlug = fields.HasFlag(SeoFields.Slug) && !string.IsNullOrWhiteSpace(proposal.Slug) ? proposal.Slug!.Trim() : null;
        switch (kind)
        {
            case SeoEntityKind.Product:
            {
                var product = await context.Products.AsTracking().Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
                if (product is null) return "محصول پیدا نشد.";
                if (newSlug is not null && await context.Products.AnyAsync(p => p.Slug == newSlug && p.Id != id, cancellationToken))
                    return "نامک پیشنهادی را محصول دیگری گرفته است.";
                product.Seo = ApplySeo(product.Seo, proposal, fields, userId, now);
                if (fields.HasFlag(SeoFields.Description) && proposal.DescriptionHtml is not null)
                    product.Description = proposal.DescriptionHtml.SanitizeText();
                if (fields.HasFlag(SeoFields.ImageAlt))
                    foreach (var alt in proposal.ImageAlts)
                        if (product.Images.FirstOrDefault(i => i.Id == alt.ImageId) is { } image && string.IsNullOrWhiteSpace(image.AltText))
                            image.AltText = Cut(alt.Alt, 250);
                if (newSlug is not null)
                {
                    await AddRedirectAsync("/product/", product.Slug, newSlug, cancellationToken);
                    product.Slug = newSlug;
                }
                return null;
            }
            case SeoEntityKind.Category:
            {
                var category = await context.Categories.AsTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
                if (category is null) return "دسته پیدا نشد.";
                category.Seo = ApplySeo(category.Seo, proposal, fields, userId, now);
                if (fields.HasFlag(SeoFields.Description) && proposal.DescriptionHtml is not null)
                    category.Description = proposal.DescriptionHtml.SanitizeText();
                return null;
            }
            case SeoEntityKind.Brand:
            {
                var brand = await context.Brands.AsTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
                if (brand is null) return "برند پیدا نشد.";
                brand.Seo = ApplySeo(brand.Seo, proposal, fields, userId, now);
                if (fields.HasFlag(SeoFields.Description) && proposal.DescriptionHtml is not null)
                    brand.Description = proposal.DescriptionHtml.SanitizeText();
                return null;
            }
            case SeoEntityKind.BlogPost:
            {
                var post = await context.BlogPosts.AsTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
                if (post is null) return "مقاله پیدا نشد.";
                if (newSlug is not null && await context.BlogPosts.AnyAsync(p => p.Slug == newSlug && p.Id != id, cancellationToken))
                    return "نامک پیشنهادی را مقالهٔ دیگری گرفته است.";
                post.Seo = ApplySeo(post.Seo, proposal, fields, userId, now);
                if (fields.HasFlag(SeoFields.ImageAlt) && string.IsNullOrWhiteSpace(post.ImageAlt) && proposal.ImageAlts.FirstOrDefault() is { } alt)
                    post.ImageAlt = Cut(alt.Alt, 250);
                if (newSlug is not null)
                {
                    await AddRedirectAsync("/blog/", post.Slug, newSlug, cancellationToken);
                    post.Slug = newSlug;
                }
                return null;
            }
            case SeoEntityKind.ContentPage:
            {
                var page = await context.ContentPages.AsTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
                if (page is null) return "صفحه پیدا نشد.";
                page.Seo = ApplySeo(page.Seo, proposal, fields, userId, now);
                return null;
            }
            case SeoEntityKind.StaticPage:
                return await ApplyStaticAsync(id, proposal, fields, userId, now, cancellationToken);
            default:
                return "نوع صفحه پشتیبانی نمی‌شود.";
        }
    }

    /// <summary>
    /// نوشتن فیلدهای سئو. یک نمونهٔ تازه از SeoMetaData ساخته و همهٔ فیلدهای قبلی کپی می‌شود (نوع owned در EF با
    /// جایگزینی کامل مطمئن‌تر ذخیره می‌شود) و فقط بخش‌های تأییدشده عوض می‌شوند.
    /// </summary>
    private static SeoMetaData ApplySeo(SeoMetaData? existing, SeoProposal proposal, SeoFields fields, Guid? userId, DateTimeOffset now)
    {
        var seo = new SeoMetaData();
        if (existing is not null)
            foreach (var property in typeof(SeoMetaData).GetProperties().Where(p => p.CanRead && p.CanWrite))
                property.SetValue(seo, property.GetValue(existing));

        if (fields.HasFlag(SeoFields.Title) && proposal.Title is not null) seo.Title = Cut(proposal.Title, 200);
        if (fields.HasFlag(SeoFields.MetaDescription) && proposal.MetaDescription is not null) seo.MetaDescription = Cut(proposal.MetaDescription, 500);
        if (fields.HasFlag(SeoFields.FocusKeyword) && proposal.FocusKeyword is not null) seo.FocusKeyword = Cut(proposal.FocusKeyword, 200);
        if (fields.HasFlag(SeoFields.Faq) && proposal.Faq.Count > 0) seo.FaqJson = SeoFaq.Serialize(proposal.Faq);
        seo.IsReviewed = true;
        seo.ReviewedAt = now;
        seo.ReviewedByUserId = userId;
        return seo;
    }

    /// <summary>
    /// ریدایرکت ۳۰۱ از آدرس قدیم به جدید، بدون زنجیره و حلقه:
    /// • ریدایرکت‌هایی که به آدرس قدیم می‌رفتند، مستقیم به آدرس جدید می‌روند.
    /// • ریدایرکتی که «از» آدرس جدید بود (مثلاً برگشت به نامک قبلی) حذف می‌شود تا صفحه به خودش ریدایرکت نشود.
    /// آدرس مقصد percent-encode می‌شود چون هدر Location فقط ASCII می‌پذیرد.
    /// </summary>
    private async Task AddRedirectAsync(string prefix, string oldSlug, string newSlug, CancellationToken cancellationToken)
    {
        var oldPath = (prefix + oldSlug).TrimEnd('/').ToLowerInvariant();
        var newPath = (prefix + newSlug).TrimEnd('/').ToLowerInvariant();
        var target = prefix + Uri.EscapeDataString(newSlug);
        var oldTarget = prefix + Uri.EscapeDataString(oldSlug);

        var related = await context.SeoRedirects.AsTracking()
            .Where(r => r.FromPath == oldPath || r.FromPath == newPath || r.ToUrl == oldPath || r.ToUrl == oldTarget || r.ToUrl == prefix + oldSlug)
            .ToListAsync(cancellationToken);
        foreach (var redirect in related)
        {
            if (string.Equals(redirect.FromPath, newPath, StringComparison.OrdinalIgnoreCase))
                context.SeoRedirects.Remove(redirect);
            else
                redirect.ToUrl = target;
        }
        if (!related.Any(r => string.Equals(r.FromPath, oldPath, StringComparison.OrdinalIgnoreCase)))
        {
            context.SeoRedirects.Add(new SeoRedirect
            {
                FromPath = oldPath,
                ToUrl = target,
                Type = RedirectType.MovedPermanently,
                Notes = "موتور سئو: تغییر نامک",
                IsActive = true,
            });
        }
    }

    public async Task<OperationResult<Guid?>> RejectAsync(Guid suggestionId, SeoFields fields, string? reason, Guid? userId, string? userName, CancellationToken cancellationToken = default)
    {
        var suggestion = await context.SeoSuggestions.AsTracking().FirstOrDefaultAsync(s => s.Id == suggestionId, cancellationToken);
        if (suggestion is null)
            return OperationResult<Guid?>.NotFound("پیشنهاد پیدا نشد.");
        if (suggestion.Status != SeoSuggestionStatus.Pending)
            return OperationResult<Guid?>.Error("این پیشنهاد دیگر در انتظار بررسی نیست.");

        var rejected = fields & suggestion.Fields;
        if (rejected == SeoFields.None)
            rejected = suggestion.Fields;
        suggestion.Status = SeoSuggestionStatus.Rejected;
        suggestion.RejectedFields = rejected;
        suggestion.RejectReason = Cut(reason?.Trim(), 500);
        suggestion.ReviewedAt = DateTimeOffset.UtcNow;
        suggestion.ReviewedByUserId = userId;
        suggestion.ReviewedByName = Cut(userName, 150);
        await context.SaveChangesAsync(cancellationToken);

        var current = await settings.GetAsync(cancellationToken);
        var subject = (await LoadSubjectsAsync(suggestion.Kind, suggestion.EntityId, cancellationToken)).FirstOrDefault();
        if (subject is not null)
            await LearnAsync(subject, SeoProposal.FromJson(suggestion.ProposalJson), rejected, accepted: false, cancellationToken);
        if (subject is null || subject.ContentHash != suggestion.ContentHash)
            return OperationResult<Guid?>.Success(null, "پیشنهاد رد شد. صفحه تغییر کرده؛ نسخهٔ تازه در اجرای بعدی موتور ساخته می‌شود.");

        var result = await GenerateCoreAsync(subject, current, suggestion.Variant + 1, suggestion, rejected,
            ContentWriter.StyleFromReason(reason), cancellationToken);
        if (result.Suggestion is null)
            return OperationResult<Guid?>.Success(null, "پیشنهاد رد شد. نسخهٔ متفاوت و مفیدتری پیدا نشد؛ می‌توانید بخش‌ها را دستی ویرایش کنید.");

        context.SeoSuggestions.Add(result.Suggestion);
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult<Guid?>.Success(result.Suggestion.Id, "پیشنهاد رد شد و نسخهٔ تازه ساخته شد.");
    }

    /// <summary>همهٔ متن‌های ردشدهٔ قبلی این صفحه (تا دوباره پیشنهاد نشوند).</summary>
    private async Task<AvoidSet> AvoidAsync(SeoEntityKind kind, Guid id, CancellationToken cancellationToken)
    {
        var rejected = await context.SeoSuggestions.AsNoTracking()
            .Where(s => s.Kind == kind && s.EntityId == id && s.Status == SeoSuggestionStatus.Rejected)
            .Select(s => new { s.ProposalJson, s.RejectedFields })
            .ToListAsync(cancellationToken);
        var avoid = new AvoidSet();
        foreach (var item in rejected)
        {
            var proposal = SeoProposal.FromJson(item.ProposalJson);
            if (item.RejectedFields.HasFlag(SeoFields.Title) && proposal.Title is not null) avoid.Texts.Add(PersianText.Normalize(proposal.Title));
            if (item.RejectedFields.HasFlag(SeoFields.MetaDescription) && proposal.MetaDescription is not null) avoid.Texts.Add(PersianText.Normalize(proposal.MetaDescription));
            if (item.RejectedFields.HasFlag(SeoFields.FocusKeyword) && proposal.FocusKeyword is not null) avoid.Keywords.Add(proposal.FocusKeyword);
            if (item.RejectedFields.HasFlag(SeoFields.Slug) && proposal.Slug is not null) avoid.Slugs.Add(proposal.Slug);
            if (item.RejectedFields.HasFlag(SeoFields.Description) && proposal.DescriptionHtml is not null) avoid.BodyHashes.Add(PersianText.Hash(proposal.DescriptionHtml));
            if (item.RejectedFields.HasFlag(SeoFields.Faq) && proposal.Faq.Count > 0) avoid.FaqHashes.Add(PersianText.Hash(SeoFaq.Serialize(proposal.Faq)));
        }
        return avoid;
    }

    // ── داشبورد ─────────────────────────────────────────────────────────

    public Task<bool> HasRunAsync(string slotKey, CancellationToken cancellationToken = default) =>
        context.SeoEngineRuns.AnyAsync(r => r.SlotKey == slotKey, cancellationToken);

    public async Task<SeoDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var scores = await context.SeoScores.AsNoTracking()
            .Select(s => new { s.Kind, s.Score })
            .ToListAsync(cancellationToken);
        var since = DateTimeOffset.UtcNow.AddDays(-30);
        var applied = await context.SeoSuggestions.AsNoTracking()
            .Where(s => s.Status == SeoSuggestionStatus.Applied && s.ReviewedAt >= since)
            .Select(s => new { s.ScoreBefore, s.ScoreAfter })
            .ToListAsync(cancellationToken);
        var pending = await context.SeoSuggestions.CountAsync(s => s.Status == SeoSuggestionStatus.Pending, cancellationToken);
        var runs = await context.SeoEngineRuns.AsNoTracking().OrderByDescending(r => r.StartedAt).Take(8).ToListAsync(cancellationToken);
        var worst = await context.SeoScores.AsNoTracking().OrderBy(s => s.Score).ThenByDescending(s => s.Views).Take(10).ToListAsync(cancellationToken);
        var day = DateTimeOffset.UtcNow.AddDays(-2);
        var sources = await context.SeoResearchCaches.AsNoTracking()
            .Where(c => c.FetchedAt >= day && c.Kind != SeoResearchKind.Link)
            .Select(c => new { c.Kind, c.Ok, c.Source, c.FetchedAt })
            .ToListAsync(cancellationToken);

        return new SeoDashboard
        {
            Pages = scores.Count,
            AverageScore = scores.Count == 0 ? 0 : (int)Math.Round(scores.Average(s => s.Score)),
            Weak = scores.Count(s => s.Score < 50),
            Medium = scores.Count(s => s.Score is >= 50 and < 80),
            Strong = scores.Count(s => s.Score >= 80),
            Pending = pending,
            AppliedLast30Days = applied.Count,
            AverageGain = applied.Count == 0 ? 0 : (int)Math.Round(applied.Average(a => a.ScoreAfter - a.ScoreBefore)),
            Runs = runs,
            Worst = worst,
            ByKind = scores.GroupBy(s => s.Kind).OrderBy(g => g.Key)
                .Select(g => (g.Key, g.Count(), (int)Math.Round(g.Average(x => x.Score)))).ToList(),
            Sources = sources.GroupBy(s => s.Kind).OrderBy(g => g.Key)
                .Select(g => (g.Key, g.Count(x => x.Ok), g.Count(x => !x.Ok), g.OrderByDescending(x => x.FetchedAt).Select(x => x.Source).FirstOrDefault()))
                .ToList(),
        };
    }

    // ── بارگذاری داده ────────────────────────────────────────────────────

    /// <summary>آدرس ویرایش هر نوع صفحه در پنل.</summary>
    public static string EditUrl(SeoEntityKind kind, Guid id) => kind switch
    {
        SeoEntityKind.Product => $"/admin/products/edit/{id}#seo",
        SeoEntityKind.BlogPost => $"/admin/blog/edit/{id}#seo",
        SeoEntityKind.Category => $"/admin/manage/categories/edit/{id}",
        SeoEntityKind.Brand => $"/admin/manage/brands/edit/{id}",
        SeoEntityKind.ContentPage => $"/admin/manage/content-pages/edit/{id}",
        SeoEntityKind.StaticPage => "/admin/manage/page-seo",
        _ => "/admin/seo",
    };

    public static StoreFacts StoreFactsFrom(Fratak_Shop.Core.DTOs.Settings.StoreSettingDto s)
    {
        var separator = string.IsNullOrWhiteSpace(s.TitleSeparator) ? "|" : s.TitleSeparator.Trim();
        return new StoreFacts
        {
            SiteName = s.SiteName ?? "",
            Tagline = s.SiteDescription,
            // قالب عنوان را این‌طور می‌سازد: «عنوان | نام‌سایت» (SiteContext.Title)
            TitleSuffixLength = (s.SiteName ?? "").Length + separator.Length + 2,
            ReturnEnabled = s.IsReturnEnabled,
            ReturnDays = s.ReturnWindowDays,
            DeliveryMinDays = s.SlaDeliveryMinDays,
            DeliveryMaxDays = s.SlaDeliveryMaxDays,
            FreeShippingThreshold = s.FreeShippingThreshold,
            CashOnDelivery = s.IsCashOnDeliveryEnabled,
        };
    }

    private SeoResearchService CreateResearch(Fratak_Shop.Core.DTOs.Settings.StoreSettingDto current, int budget, string siteName) =>
        new(context, current.SeoCompetitorResearch ? web : new OfflineSeoWebSource(), loggerFactory.CreateLogger<SeoResearchService>())
        {
            NetworkBudget = budget,
            OwnSiteName = siteName,
        };

    private async Task<SeoSiteIndex> GetSiteIndexAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(SiteIndexCacheKey, out SeoSiteIndex? index) && index is not null)
            return index;
        index = SeoSiteIndex.Build(await LoadSubjectsAsync(null, null, cancellationToken));
        cache.Set(SiteIndexCacheKey, index, TimeSpan.FromMinutes(15));
        return index;
    }

    /// <summary>
    /// مدل زبانی و TF-IDF روی متن‌های سایت. subjects=null → از دیتابیس (برای ساخت دستی). کش ۳۰ دقیقه‌ای.
    /// </summary>
    private async Task<EngineModels> BuildModelsAsync(IReadOnlyList<SeoSubject>? subjects, CancellationToken cancellationToken)
    {
        if (subjects is null && cache.TryGetValue("seo-engine:models", out EngineModels? cached) && cached is not null)
            return cached;
        subjects ??= await LoadSubjectsAsync(null, null, cancellationToken);
        var language = new TrigramLanguageModel();
        var tfidf = new TfIdf();
        var trainedChars = 0;
        foreach (var subject in subjects)
        {
            var text = PersianText.HtmlToText(subject.BodyHtml);
            tfidf.Add(subject.Name + "\n" + text);
            // سقف ۳ میلیون نویسه برای آموزش (کافی برای سبک نوشتاری و سریع).
            if (trainedChars < 3_000_000 && text.Length > 0)
            {
                language.Train(text);
                trainedChars += text.Length;
            }
        }
        var models = new EngineModels(language, tfidf, await InternalSearchesAsync(cancellationToken), await GoogleQueriesAsync(null, cancellationToken));
        cache.Set("seo-engine:models", models, TimeSpan.FromMinutes(30));
        return models;
    }

    /// <summary>
    /// عبارت‌های گوگل هر صفحه از آخرین همگام‌سازی Search Console (حداکثر ۳۰ عبارت پرنمایش برای هر مسیر).
    /// path = فقط یک صفحه (برای تحلیل زنده). اگر Search Console وصل نباشد جدول خالی است و خروجی هم خالی.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, IReadOnlyList<SearchQueryStat>>> GoogleQueriesAsync(string? path, CancellationToken cancellationToken)
    {
        var query = context.SearchConsoleQueryStats.AsNoTracking();
        if (path is not null)
            query = query.Where(s => s.Path == path);
        var rows = await query
            .Where(s => s.Impressions >= 3)
            .OrderByDescending(s => s.Impressions)
            .Take(path is null ? 60_000 : 30)
            .Select(s => new { s.Path, s.Query, s.Clicks, s.Impressions, s.Position })
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(r => r.Path, StringComparer.Ordinal)
            .ToDictionary(g => g.Key,
                g => (IReadOnlyList<SearchQueryStat>)g.Take(30).Select(r => new SearchQueryStat(r.Query, r.Clicks, r.Impressions, r.Position)).ToList(),
                StringComparer.Ordinal);
    }

    public async Task<int> RefreshScoresAsync(SeoEntityKind kind, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
            return 0;
        // عنوان‌ها عوض شده‌اند؛ نمایهٔ سایت (برای «عنوان تکراری» و «کلمهٔ تکراری») باید تازه ساخته شود.
        cache.Remove(SiteIndexCacheKey);
        var current = await settings.GetAsync(cancellationToken);
        var store = StoreFactsFrom(current);
        var site = await GetSiteIndexAsync(cancellationToken);
        var set = ids.ToHashSet();
        var subjects = ids.Count == 1
            ? await LoadSubjectsAsync(kind, ids.First(), cancellationToken)
            : (await LoadSubjectsAsync(kind, null, cancellationToken)).Where(s => set.Contains(s.EntityId)).ToList();
        var idList = set.ToList();
        var existing = await context.SeoScores.AsTracking()
            .Where(s => s.Kind == kind && idList.Contains(s.EntityId))
            .ToDictionaryAsync(s => (s.Kind, s.EntityId), cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var subject in subjects)
            Upsert(existing, subject, SeoAnalyzer.Analyze(subject, site, store), now);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            context.ChangeTracker.Clear();
            _logger.LogDebug(ex, "SEO score refresh skipped");
            return 0;
        }
        return subjects.Count;
    }

    /// <summary>پرجست‌وجوترین عبارت‌های جست‌وجوی داخلی سایت (نرمال‌شده → تعداد).</summary>
    private async Task<IReadOnlyDictionary<string, int>> InternalSearchesAsync(CancellationToken cancellationToken)
    {
        var rows = await context.SearchQueryLogs.AsNoTracking()
            .OrderByDescending(q => q.SearchCount)
            .Take(3000)
            .Select(q => new { q.NormalizedQuery, q.SearchCount })
            .ToListAsync(cancellationToken);
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = PersianText.Normalize(row.NormalizedQuery);
            if (key.Length > 0)
                map[key] = map.GetValueOrDefault(key) + row.SearchCount;
        }
        return map;
    }

    private void Upsert(Dictionary<(SeoEntityKind, Guid), SeoScore> map, SeoSubject subject, SeoReport report, DateTimeOffset now)
    {
        if (!map.TryGetValue((subject.Kind, subject.EntityId), out var score))
        {
            score = new SeoScore { Kind = subject.Kind, EntityId = subject.EntityId };
            context.SeoScores.Add(score);
            map[(subject.Kind, subject.EntityId)] = score;
        }
        score.Title = Cut(subject.Name, 200)!;
        score.Path = Cut(subject.Path, 400)!;
        score.Score = report.Score;
        score.Problems = report.Problems;
        score.FocusKeyword = Cut(subject.FocusKeyword, 200);
        score.ChecksJson = SeoJson.Serialize(report.Checks);
        score.ContentHash = subject.ContentHash;
        score.AnalyzedAt = now;
        score.Views = subject.Views;
    }

    private async Task SaveScoreAsync(SeoSubject subject, SeoReport report, CancellationToken cancellationToken)
    {
        var existing = await context.SeoScores.AsTracking()
            .Where(s => s.Kind == subject.Kind && s.EntityId == subject.EntityId)
            .ToDictionaryAsync(s => (s.Kind, s.EntityId), cancellationToken);
        Upsert(existing, subject, report, DateTimeOffset.UtcNow);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // دو درخواست هم‌زمان؛ نمره در اجرای بعدی درست می‌شود.
            _logger.LogDebug(ex, "SEO score save skipped");
        }
    }

    /// <summary>
    /// خواندن همهٔ صفحه‌های قابل سئو (یا فقط یکی). فقط صفحه‌های فعال/منتشرشده. خروجی مستقل از EF است.
    /// </summary>
    public async Task<List<SeoSubject>> LoadSubjectsAsync(SeoEntityKind? kind, Guid? id, CancellationToken cancellationToken)
    {
        var list = new List<SeoSubject>();
        if (kind is null or SeoEntityKind.Product)
            list.AddRange(await LoadProductsAsync(id, cancellationToken));
        if (kind is null or SeoEntityKind.Category or SeoEntityKind.Brand)
        {
            var listings = await LoadListingsAsync(cancellationToken);
            list.AddRange(listings.Where(s => (kind is null || s.Kind == kind) && (id is null || s.EntityId == id)));
        }
        if (kind is null or SeoEntityKind.BlogPost)
            list.AddRange(await LoadPostsAsync(id, cancellationToken));
        if (kind is null or SeoEntityKind.ContentPage)
            list.AddRange(await LoadPagesAsync(id, cancellationToken));
        if (kind is null or SeoEntityKind.StaticPage)
            list.AddRange(await LoadStaticPagesAsync(id, cancellationToken));
        return list;
    }

    private async Task<List<SeoSubject>> LoadProductsAsync(Guid? id, CancellationToken cancellationToken)
    {
        var rows = await context.Products.AsNoTracking()
            .Where(p => p.IsActive && (id == null || p.Id == id))
            .Select(p => new
            {
                p.Id, p.Name, p.EnglishName, p.Slug, p.ShortDescription, p.Description, p.CreatedAt, p.ViewCount, p.StockQuantity,
                p.WarrantyTitle, p.WarrantyMonths, p.DiscountPrice,
                CategoryName = p.Category.Name, CategorySlug = p.Category.Slug,
                BrandName = p.Brand != null ? p.Brand.Name : null,
                BrandEnglish = p.Brand != null ? p.Brand.EnglishName : null,
                BrandSlug = p.Brand != null ? p.Brand.Slug : null,
                SeoTitle = p.Seo!.Title, Meta = p.Seo!.MetaDescription, Focus = p.Seo!.FocusKeyword, Faq = p.Seo!.FaqJson,
                NoIndex = p.Seo!.NoIndex, Canonical = p.Seo!.CanonicalUrl,
                Images = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder)
                    .Select(i => new { i.Id, i.AltText, i.IsPrimary }).ToList(),
                Features = p.Features.OrderBy(f => f.DisplayOrder).Select(f => new { f.Key, f.Value }).ToList(),
            })
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return rows.Select(p => new SeoSubject
        {
            Kind = SeoEntityKind.Product,
            EntityId = p.Id,
            Name = p.Name,
            EnglishName = p.EnglishName,
            Slug = p.Slug,
            PathPrefix = "/product/",
            SeoTitle = p.SeoTitle,
            MetaDescription = p.Meta,
            FocusKeyword = p.Focus,
            FaqJson = p.Faq,
            NoIndex = p.NoIndex == true,
            HasCanonicalElsewhere = CanonicalElsewhere(p.Canonical, "/product/" + p.Slug),
            DefaultTitle = p.Name,
            DefaultDescription = p.ShortDescription ?? PersianText.Truncate(PersianText.HtmlToText(p.Description), 160),
            BodyHtml = p.Description,
            ShortText = p.ShortDescription,
            Images = p.Images.Select(i => new SeoImageInfo(i.Id, i.AltText, i.IsPrimary)).ToList(),
            Views = p.ViewCount,
            CreatedAt = p.CreatedAt,
            Product = new ProductFacts
            {
                CategoryName = p.CategoryName,
                CategorySlug = p.CategorySlug,
                BrandName = p.BrandName,
                BrandEnglishName = p.BrandEnglish,
                BrandSlug = p.BrandSlug,
                Features = p.Features.Select(f => new SeoFeature(f.Key, f.Value)).ToList(),
                WarrantyTitle = p.WarrantyTitle,
                WarrantyMonths = p.WarrantyMonths,
                InStock = p.StockQuantity > 0,
                HasDiscount = p.DiscountPrice.HasValue,
            },
        }).ToList();
    }

    /// <summary>
    /// دسته‌ها و برندها با واقعیت‌های جمعی (تعداد محصول با زیردسته‌ها، برندها/دسته‌های پرمحصول، پرفروش‌ها، ویژگی‌های رایج).
    /// </summary>
    private async Task<List<SeoSubject>> LoadListingsAsync(CancellationToken cancellationToken)
    {
        var categories = await context.Categories.AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => new
            {
                c.Id, c.Name, c.Slug, c.Description, c.ParentId, c.CreatedAt,
                SeoTitle = c.Seo!.Title, Meta = c.Seo!.MetaDescription, Focus = c.Seo!.FocusKeyword, Faq = c.Seo!.FaqJson,
                NoIndex = c.Seo!.NoIndex, Canonical = c.Seo!.CanonicalUrl,
            })
            .ToListAsync(cancellationToken);
        var brands = await context.Brands.AsNoTracking()
            .Where(b => b.IsActive)
            .Select(b => new
            {
                b.Id, b.Name, b.EnglishName, b.Slug, b.Description, b.CreatedAt,
                SeoTitle = b.Seo!.Title, Meta = b.Seo!.MetaDescription, Focus = b.Seo!.FocusKeyword, Faq = b.Seo!.FaqJson,
                NoIndex = b.Seo!.NoIndex, Canonical = b.Seo!.CanonicalUrl,
            })
            .ToListAsync(cancellationToken);
        var products = await context.Products.AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new { p.Id, p.Name, p.Slug, p.CategoryId, p.BrandId, p.SalesCount, p.ViewCount, HasWarranty = p.WarrantyMonths > 0 || p.WarrantyTitle != null })
            .ToListAsync(cancellationToken);
        var featureKeys = await context.ProductFeatures.AsNoTracking()
            .Where(f => f.Product.IsActive)
            .Select(f => new { f.ProductId, f.Key })
            .ToListAsync(cancellationToken);

        var categoryById = categories.ToDictionary(c => c.Id);
        var brandById = brands.ToDictionary(b => b.Id);
        var keysByProduct = featureKeys.GroupBy(f => f.ProductId).ToDictionary(g => g.Key, g => g.Select(f => PersianText.Clean(f.Key)).Distinct().ToList());

        // زیردسته‌ها و همهٔ نوادگان هر دسته
        var children = categories.Where(c => c.ParentId.HasValue).GroupBy(c => c.ParentId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        HashSet<Guid> Descendants(Guid root)
        {
            var set = new HashSet<Guid> { root };
            var stack = new Stack<Guid>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                if (!children.TryGetValue(stack.Pop(), out var kids))
                    continue;
                foreach (var child in kids)
                    if (set.Add(child.Id))
                        stack.Push(child.Id);
            }
            return set;
        }

        static List<string> CommonKeys(IEnumerable<List<string>> keyLists, int total)
        {
            if (total < 2) return [];
            return keyLists.SelectMany(k => k).GroupBy(k => k)
                .Where(g => g.Count() >= Math.Max(2, total / 2))
                .OrderByDescending(g => g.Count()).Take(5).Select(g => g.Key).ToList();
        }

        static List<SeoLink> TopProducts<T>(IEnumerable<T> items, Func<T, int> sales, Func<T, int> views, Func<T, string> name, Func<T, string> slug) =>
            items.OrderByDescending(sales).ThenByDescending(views).Take(6)
                .Select(p => new SeoLink(KeywordPlanner.ShortName(name(p)), "/product/" + slug(p))).ToList();

        var list = new List<SeoSubject>();
        foreach (var c in categories)
        {
            var ids = Descendants(c.Id);
            var inCategory = products.Where(p => ids.Contains(p.CategoryId)).ToList();
            var listing = new ListingFacts
            {
                ProductCount = inCategory.Count,
                ParentName = c.ParentId.HasValue && categoryById.TryGetValue(c.ParentId.Value, out var parent) ? parent.Name : null,
                RelatedGroups = inCategory.Where(p => p.BrandId.HasValue && brandById.ContainsKey(p.BrandId.Value))
                    .GroupBy(p => p.BrandId!.Value).OrderByDescending(g => g.Count()).Take(5)
                    .Select(g => new SeoLink(brandById[g.Key].Name, "/brand/" + brandById[g.Key].Slug)).ToList(),
                SubCategories = children.TryGetValue(c.Id, out var subs)
                    ? subs.Select(x => new SeoLink(x.Name, "/category/" + x.Slug)).ToList()
                    : new List<SeoLink>(),
                TopProducts = TopProducts(inCategory, p => p.SalesCount, p => p.ViewCount, p => p.Name, p => p.Slug),
                CommonFeatureKeys = CommonKeys(inCategory.Select(p => keysByProduct.GetValueOrDefault(p.Id) ?? []), inCategory.Count),
                WarrantyPercent = inCategory.Count == 0 ? 0 : 100 * inCategory.Count(p => p.HasWarranty) / inCategory.Count,
            };
            list.Add(new SeoSubject
            {
                Kind = SeoEntityKind.Category,
                EntityId = c.Id,
                Name = c.Name,
                Slug = c.Slug,
                PathPrefix = "/category/",
                SeoTitle = c.SeoTitle, MetaDescription = c.Meta, FocusKeyword = c.Focus, FaqJson = c.Faq,
                NoIndex = c.NoIndex == true,
                HasCanonicalElsewhere = CanonicalElsewhere(c.Canonical, "/category/" + c.Slug),
                DefaultTitle = $"خرید {c.Name}",
                DefaultDescription = PersianText.Truncate(PersianText.HtmlToText(c.Description), 300),
                BodyHtml = c.Description,
                Views = inCategory.Sum(p => p.ViewCount) / Math.Max(1, inCategory.Count),
                CreatedAt = c.CreatedAt,
                Listing = listing,
            });
        }

        foreach (var b in brands)
        {
            var ofBrand = products.Where(p => p.BrandId == b.Id).ToList();
            var listing = new ListingFacts
            {
                ProductCount = ofBrand.Count,
                RelatedGroups = ofBrand.Where(p => categoryById.ContainsKey(p.CategoryId))
                    .GroupBy(p => p.CategoryId).OrderByDescending(g => g.Count()).Take(5)
                    .Select(g => new SeoLink(categoryById[g.Key].Name, "/category/" + categoryById[g.Key].Slug)).ToList(),
                TopProducts = TopProducts(ofBrand, p => p.SalesCount, p => p.ViewCount, p => p.Name, p => p.Slug),
                CommonFeatureKeys = CommonKeys(ofBrand.Select(p => keysByProduct.GetValueOrDefault(p.Id) ?? []), ofBrand.Count),
                WarrantyPercent = ofBrand.Count == 0 ? 0 : 100 * ofBrand.Count(p => p.HasWarranty) / ofBrand.Count,
            };
            list.Add(new SeoSubject
            {
                Kind = SeoEntityKind.Brand,
                EntityId = b.Id,
                Name = b.Name,
                EnglishName = b.EnglishName,
                Slug = b.Slug,
                PathPrefix = "/brand/",
                SeoTitle = b.SeoTitle, MetaDescription = b.Meta, FocusKeyword = b.Focus, FaqJson = b.Faq,
                NoIndex = b.NoIndex == true,
                HasCanonicalElsewhere = CanonicalElsewhere(b.Canonical, "/brand/" + b.Slug),
                DefaultTitle = $"محصولات {b.Name}",
                DefaultDescription = PersianText.Truncate(PersianText.HtmlToText(b.Description), 300),
                BodyHtml = b.Description,
                Views = ofBrand.Sum(p => p.ViewCount) / Math.Max(1, ofBrand.Count),
                CreatedAt = b.CreatedAt,
                Listing = listing,
            });
        }
        return list;
    }

    private async Task<List<SeoSubject>> LoadPostsAsync(Guid? id, CancellationToken cancellationToken)
    {
        var rows = await context.BlogPosts.AsNoTracking()
            .Where(p => p.IsPublished && (id == null || p.Id == id))
            .Select(p => new
            {
                p.Id, p.Title, p.Slug, p.Excerpt, p.Content, p.ImageName, p.ImageAlt, p.ViewCount, p.CreatedAt, p.PublishedAt,
                CategoryName = p.BlogCategory.Name,
                SeoTitle = p.Seo!.Title, Meta = p.Seo!.MetaDescription, Focus = p.Seo!.FocusKeyword, Faq = p.Seo!.FaqJson,
                NoIndex = p.Seo!.NoIndex, Canonical = p.Seo!.CanonicalUrl,
            })
            .ToListAsync(cancellationToken);
        return rows.Select(p => new SeoSubject
        {
            Kind = SeoEntityKind.BlogPost,
            EntityId = p.Id,
            Name = p.Title,
            Slug = p.Slug,
            PathPrefix = "/blog/",
            SeoTitle = p.SeoTitle, MetaDescription = p.Meta, FocusKeyword = p.Focus, FaqJson = p.Faq,
            NoIndex = p.NoIndex == true,
            HasCanonicalElsewhere = CanonicalElsewhere(p.Canonical, "/blog/" + p.Slug),
            DefaultTitle = p.Title,
            DefaultDescription = p.Excerpt ?? PersianText.Truncate(PersianText.HtmlToText(p.Content), 160),
            BodyHtml = p.Content,
            Images = p.ImageName is null ? Array.Empty<SeoImageInfo>() : new[] { new SeoImageInfo(null, p.ImageAlt, true) },
            Views = p.ViewCount,
            CreatedAt = p.PublishedAt ?? p.CreatedAt,
        }).ToList();
    }

    private async Task<List<SeoSubject>> LoadPagesAsync(Guid? id, CancellationToken cancellationToken)
    {
        var rows = await context.ContentPages.AsNoTracking()
            .Where(p => p.IsActive && (id == null || p.Id == id))
            .Select(p => new
            {
                p.Id, p.Title, p.Slug, p.Summary, p.Content, p.CreatedAt,
                SeoTitle = p.Seo!.Title, Meta = p.Seo!.MetaDescription, Focus = p.Seo!.FocusKeyword, Faq = p.Seo!.FaqJson,
                NoIndex = p.Seo!.NoIndex, Canonical = p.Seo!.CanonicalUrl,
            })
            .ToListAsync(cancellationToken);
        return rows.Select(p => new SeoSubject
        {
            Kind = SeoEntityKind.ContentPage,
            EntityId = p.Id,
            Name = p.Title,
            Slug = p.Slug,
            PathPrefix = "/page/",
            SeoTitle = p.SeoTitle, MetaDescription = p.Meta, FocusKeyword = p.Focus, FaqJson = p.Faq,
            NoIndex = p.NoIndex == true,
            HasCanonicalElsewhere = CanonicalElsewhere(p.Canonical, "/page/" + p.Slug),
            DefaultTitle = p.Title,
            DefaultDescription = p.Summary ?? PersianText.Truncate(PersianText.HtmlToText(p.Content), 160),
            BodyHtml = p.Content,
            ShortText = p.Summary,
            Views = 0,
            CreatedAt = p.CreatedAt,
        }).ToList();
    }

    /// <summary>کانونیکال به آدرس دیگری اشاره می‌کند؟ (کانونیکال به خود صفحه یا خالی = خیر)</summary>
    private static bool CanonicalElsewhere(string? canonical, string path)
    {
        if (string.IsNullOrWhiteSpace(canonical))
            return false;
        var value = Uri.UnescapeDataString(canonical.Trim()).TrimEnd('/');
        return !value.EndsWith(path.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    private static string? Cut(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];
}

/// <summary>
/// زمان‌بندی اجرای خودکار موتور سئو: N بار در شبانه‌روز با فاصلهٔ مساوی، شروع از ساعت ۲ بامداد به وقت ایران
/// (کم‌ترافیک‌ترین ساعت سایت). مثال برای ۳ بار: ۰۲:۰۰، ۱۰:۰۰، ۱۸:۰۰. برای ۲ بار: ۰۲:۰۰ و ۱۴:۰۰.
/// <para>کلید نوبت (مثل «2026-10-26#3-1») در جدول SeoEngineRuns یکتاست؛ پس اگر چند سرور هم‌زمان روشن باشند
/// یا سرور ری‌استارت شود، هر نوبت فقط یک بار اجرا می‌شود. استفاده: Web/Infrastructure/SeoEngine/SeoEngineWorker.</para>
/// </summary>
public static class SeoEngineSchedule
{
    /// <summary>فاصلهٔ شروع روز موتور از نیمه‌شب.</summary>
    public static readonly TimeSpan Offset = TimeSpan.FromHours(2);

    /// <summary>نوبت فعلی (آخرین نوبتی که زمانش رسیده) و کلید یکتای آن.</summary>
    public static (string Key, DateTimeOffset Start) CurrentSlot(DateTimeOffset now, int runsPerDay)
    {
        var runs = Math.Clamp(runsPerDay, 1, 6);
        var shifted = now - Offset;
        var latest = Fratak_Shop.Core.Services.Suppliers.SupplierSyncSchedule.LatestSlot(shifted, runs);
        var day = IranClock.Today(shifted);
        var interval = TimeSpan.FromMinutes(24 * 60.0 / runs);
        var index = (int)Math.Round((latest - IranClock.StartOfDay(day)) / interval);
        var key = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "#" + runs + "-" + index;
        return (key, latest + Offset);
    }

    /// <summary>زمان نوبت بعدی (برای نمایش در داشبورد).</summary>
    public static DateTimeOffset NextRun(DateTimeOffset now, int runsPerDay) =>
        Fratak_Shop.Core.Services.Suppliers.SupplierSyncSchedule.NextSlot(now - Offset, Math.Clamp(runsPerDay, 1, 6)) + Offset;
}
