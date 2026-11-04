using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.SeoEngine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>یک نقطهٔ نمودار روزانهٔ گوگل.</summary>
public sealed record SeoTrendPoint(DateOnly Date, int Clicks, int Impressions, double Position);

/// <summary>جمع بازدید یک ربات در ۷ روز.</summary>
public sealed record SeoBotSummary(string Bot, int Hits, int Verified, int Fake, int Errors, double AverageMs);

/// <summary>سرعت واقعی کاربران برای یک معیار (میانگین وزنی p75 هفت روز اخیر).</summary>
public sealed record SeoVitalSummary(string Metric, double P75, int Samples, double GoodShare, int Rating);

/// <summary>
/// گزارش کامل سئوی خودکار (داشبورد صفحهٔ «سئوی خودکار» و گزارش هفتگی؛ موارد ۲۰۶ تا ۲۱۰ پیشنهاد سئو).
/// </summary>
public sealed class SeoAutopilotReport
{
    /// <summary>نمرهٔ سلامت آخرین خزش (۰ تا ۱۰۰). null = هنوز خزشی انجام نشده.</summary>
    public int? Health { get; init; }
    public SeoCrawlRun? LastCrawl { get; init; }

    /// <summary>میانگین نمرهٔ سئوی صفحه‌ها (موتور سئو) و تعداد صفحه‌های نمره‌گرفته.</summary>
    public int AverageScore { get; init; }
    public int ScoredPages { get; init; }

    /// <summary>درصد صفحه‌هایی که عنوان سئو / توضیحات متا / کلمهٔ کلیدی دارند.</summary>
    public int TitleCoverage { get; init; }
    public int MetaCoverage { get; init; }
    public int FocusCoverage { get; init; }

    /// <summary>درصد تصویرهای محصول که Alt دارند.</summary>
    public int AltCoverage { get; init; }

    public int OpenErrors { get; init; }
    public int OpenWarnings { get; init; }
    public int OpenNotices { get; init; }
    public IReadOnlyList<SeoTask> TopTasks { get; init; } = [];
    public IReadOnlyList<SeoRevision> RecentChanges { get; init; } = [];
    public int ChangesThisWeek { get; init; }

    public int TestsRunning { get; init; }
    public int TestsWon { get; init; }
    public int TestsLost { get; init; }

    public IReadOnlyList<SeoTrendPoint> Trend { get; init; } = [];
    public int Clicks28 { get; init; }
    public int ClicksPrevious28 { get; init; }
    public IReadOnlyList<SeoBotSummary> Bots { get; init; } = [];
    public IReadOnlyList<SeoVitalSummary> Vitals { get; init; } = [];
    public IReadOnlyList<SeoOccasion> Calendar { get; init; } = [];
    public IReadOnlyList<CannibalGroup> Cannibalization { get; init; } = [];

    /// <summary>صفحه‌های نزدیک صفحهٔ اول گوگل (جایگاه ۸ تا ۲۰ با نمایش کافی) – بهترین فرصت‌ها.</summary>
    public IReadOnlyList<(string Path, int Impressions, double Position)> NearPageOne { get; init; } = [];

    public DateTimeOffset? LastNightlyRunAt { get; init; }
    public string? LastNightlySummary { get; init; }
}

/// <summary>ساخت گزارش سئو و ارسال گزارش هفتگی به مدیران.</summary>
public interface ISeoReportService
{
    Task<SeoAutopilotReport> BuildAsync(CancellationToken cancellationToken = default);

    /// <summary>اگر هفت روز از گزارش قبلی گذشته باشد، خلاصه را برای دارندگان مجوز سئو اعلان می‌کند. خروجی: ارسال شد؟</summary>
    Task<bool> SendWeeklyIfDueAsync(CancellationToken cancellationToken = default);

    /// <summary>فهرست «کارهای سئو» با فیلتر وضعیت/شدت/جست‌وجو (صفحهٔ /admin/seo/tasks). مهم‌ترها اول.</summary>
    Task<PagedList<SeoTask>> TasksAsync(SeoTaskQuery query, CancellationToken cancellationToken = default);

    /// <summary>نادیده گرفتن یا بازگشایی یک کار سئو. مثال: «این صفحه عمداً کوتاه است» ← Dismissed.</summary>
    Task<OperationResult> SetTaskStatusAsync(Guid id, SeoTaskStatus status, string? byName, CancellationToken cancellationToken = default);

    /// <summary>تاریخچهٔ تغییرهای خودکار و دستی سئو (جدیدترها اول) برای صفحهٔ /admin/seo/history.</summary>
    Task<PagedList<SeoRevision>> RevisionsAsync(string? search, int page, CancellationToken cancellationToken = default);

    /// <summary>آزمون‌های عنوان (A/B زمانی) – جدیدترها اول.</summary>
    Task<IReadOnlyList<SeoTitleTest>> TitleTestsAsync(int take, CancellationToken cancellationToken = default);

    /// <summary>دادهٔ صفحهٔ «ربات‌ها و سرعت»: پربازدیدترین و خطادارترین مسیرهای ربات‌ها + سرعت به تفکیک نوع صفحه و دستگاه.</summary>
    Task<SeoSignalsView> SignalsAsync(int days, CancellationToken cancellationToken = default);
}

/// <summary>فیلتر فهرست کارهای سئو. Status=null یعنی همهٔ وضعیت‌ها؛ پیش‌فرض صفحه فقط «باز»ها را نشان می‌دهد.</summary>
public sealed record SeoTaskQuery(SeoTaskStatus? Status, SeoTaskSeverity? Severity, string? Search, int Page, int Take = 30);

/// <summary>یک مسیر در گزارش ربات‌ها. مثال: ("Googlebot", "/product/x", 200, 14, 14, 0, 120).</summary>
public sealed record SeoBotPathRow(string Bot, string Path, int Status, int Hits, int Verified, int Fake, double AverageMs);

/// <summary>سرعت یک (نوع صفحه، دستگاه، معیار). Rating: ۱ خوب، ۰ نیاز به بهبود، ۱- ضعیف (همان SeoVitals.Rate).</summary>
public sealed record SeoVitalRow(string PageType, string Device, string Metric, double P75, int Samples, int Rating);

/// <summary>خروجی <see cref="ISeoReportService.SignalsAsync"/>.</summary>
public sealed class SeoSignalsView
{
    public int Days { get; init; }
    public IReadOnlyList<SeoBotSummary> Bots { get; init; } = [];
    public IReadOnlyList<SeoBotPathRow> TopPaths { get; init; } = [];
    public IReadOnlyList<SeoBotPathRow> ErrorPaths { get; init; } = [];
    public IReadOnlyList<SeoVitalRow> Vitals { get; init; } = [];
}

/// <inheritdoc cref="ISeoReportService"/>
public sealed class SeoReportService(
    FratakShopDbContext context,
    IStaffNotificationService notifications,
    ILogger<SeoReportService> logger) : ISeoReportService
{
    public async Task<SeoAutopilotReport> BuildAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var weekAgo = now.AddDays(-7);
        var lastCrawl = await context.SeoCrawlRuns.AsNoTracking().Where(r => r.FinishedAt != null)
            .OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(cancellationToken);

        var scores = await context.SeoScores.AsNoTracking().Select(s => s.Score).ToListAsync(cancellationToken);

        // پوشش فیلدها روی صفحه‌های زنده
        var productSeo = await context.Products.AsNoTracking().Where(p => p.IsActive)
            .Select(p => new { T = p.Seo != null && p.Seo.Title != null && p.Seo.Title != "", M = p.Seo != null && p.Seo.MetaDescription != null && p.Seo.MetaDescription != "", F = p.Seo != null && p.Seo.FocusKeyword != null && p.Seo.FocusKeyword != "" })
            .ToListAsync(cancellationToken);
        var postSeo = await context.BlogPosts.AsNoTracking().Where(p => p.IsPublished)
            .Select(p => new { T = p.Seo != null && p.Seo.Title != null && p.Seo.Title != "", M = p.Seo != null && p.Seo.MetaDescription != null && p.Seo.MetaDescription != "", F = p.Seo != null && p.Seo.FocusKeyword != null && p.Seo.FocusKeyword != "" })
            .ToListAsync(cancellationToken);
        var categorySeo = await context.Categories.AsNoTracking().Where(p => p.IsActive)
            .Select(p => new { T = p.Seo != null && p.Seo.Title != null && p.Seo.Title != "", M = p.Seo != null && p.Seo.MetaDescription != null && p.Seo.MetaDescription != "", F = p.Seo != null && p.Seo.FocusKeyword != null && p.Seo.FocusKeyword != "" })
            .ToListAsync(cancellationToken);
        var coverage = productSeo.Concat(postSeo).Concat(categorySeo).ToList();
        int Percent(int part, int total) => total == 0 ? 100 : (int)Math.Round(part * 100.0 / total);

        var images = await context.Set<Data.Entities.Products.ProductImage>().AsNoTracking().CountAsync(cancellationToken);
        var imagesWithAlt = await context.Set<Data.Entities.Products.ProductImage>().AsNoTracking()
            .CountAsync(i => i.AltText != null && i.AltText != "", cancellationToken);

        var openTasks = context.SeoTasks.AsNoTracking().Where(t => t.Status == SeoTaskStatus.Open);
        var severities = await openTasks.GroupBy(t => t.Severity).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var topTasks = await openTasks.OrderByDescending(t => t.Impact).ThenByDescending(t => t.LastSeenAt).Take(8).ToListAsync(cancellationToken);
        var recent = await context.SeoRevisions.AsNoTracking().OrderByDescending(r => r.CreatedAt).Take(10).ToListAsync(cancellationToken);
        var changesWeek = await context.SeoRevisions.AsNoTracking().CountAsync(r => r.CreatedAt >= weekAgo, cancellationToken);

        var tests = await context.SeoTitleTests.AsNoTracking().GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var from = today.AddDays(-56);
        var daily = await context.SearchConsoleDailyStats.AsNoTracking().Where(d => d.Date >= from).OrderBy(d => d.Date)
            .Select(d => new SeoTrendPoint(d.Date, d.Clicks, d.Impressions, d.Position)).ToListAsync(cancellationToken);
        var split = today.AddDays(-28);

        var botFrom = DateOnly.FromDateTime(weekAgo.UtcDateTime);
        var bots = (await context.SeoBotVisits.AsNoTracking().Where(v => v.Date >= botFrom)
                .GroupBy(v => v.Bot)
                .Select(g => new { Bot = g.Key, Hits = g.Sum(v => v.Hits), Verified = g.Sum(v => v.VerifiedHits), Fake = g.Sum(v => v.FakeHits), Errors = g.Where(v => v.Status >= 400).Sum(v => v.Hits), Ms = g.Sum(v => v.TotalMs) })
                .ToListAsync(cancellationToken))
            .Select(b => new SeoBotSummary(b.Bot, b.Hits, b.Verified, b.Fake, b.Errors, b.Hits == 0 ? 0 : Math.Round(b.Ms / (double)b.Hits)))
            .OrderByDescending(b => b.Hits).ToList();

        var vitals = (await context.SeoVitalStats.AsNoTracking().Where(v => v.Date >= botFrom)
                .Select(v => new { v.Metric, v.P75, v.Samples, v.Good })
                .ToListAsync(cancellationToken))
            .GroupBy(v => v.Metric)
            .Select(g =>
            {
                var samples = g.Sum(v => v.Samples);
                var p75 = samples == 0 ? 0 : g.Sum(v => v.P75 * v.Samples) / samples;
                return new SeoVitalSummary(g.Key, Math.Round(p75, g.Key == "CLS" ? 3 : 0), samples, samples == 0 ? 0 : Math.Round(g.Sum(v => v.Good) * 100.0 / samples), SeoVitals.Rate(g.Key, p75));
            })
            .OrderBy(v => v.Metric).ToList();

        var categoryNames = await context.Categories.AsNoTracking().Where(c => c.IsActive && c.ParentId == null)
            .OrderBy(c => c.DisplayOrder).Select(c => c.Name).Take(20).ToListAsync(cancellationToken);
        var tehranToday = DateOnly.FromDateTime(now.UtcDateTime.AddHours(3.5));

        var queryRows = await context.SearchConsoleQueryStats.AsNoTracking()
            .Where(s => s.Impressions >= 20)
            .OrderByDescending(s => s.Impressions).Take(3000)
            .Select(s => new { s.Query, s.Path, s.Impressions, s.Position })
            .ToListAsync(cancellationToken);
        var cannibal = KeywordIntel.Cannibalization(queryRows.Select(r => (r.Query, r.Path, r.Impressions, r.Position))).Take(10).ToList();
        var pageStats = queryRows.GroupBy(r => r.Path)
            .Select(g => (Path: g.Key, Impressions: g.Sum(r => r.Impressions), Position: g.Sum(r => r.Position * r.Impressions) / Math.Max(1, g.Sum(r => r.Impressions))))
            .Where(p => CtrModel.IsNearPageOne(new CtrSample(p.Impressions, 0, p.Position)))
            .OrderByDescending(p => p.Impressions).Take(10).ToList();

        var config = await context.SeoAutopilotConfigs.AsNoTracking().OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(cancellationToken);

        return new SeoAutopilotReport
        {
            Health = lastCrawl?.HealthScore,
            LastCrawl = lastCrawl,
            AverageScore = scores.Count == 0 ? 0 : (int)Math.Round(scores.Average()),
            ScoredPages = scores.Count,
            TitleCoverage = Percent(coverage.Count(c => c.T), coverage.Count),
            MetaCoverage = Percent(coverage.Count(c => c.M), coverage.Count),
            FocusCoverage = Percent(coverage.Count(c => c.F), coverage.Count),
            AltCoverage = Percent(imagesWithAlt, images),
            OpenErrors = severities.Where(s => s.Key == SeoTaskSeverity.Critical).Sum(s => s.Count),
            OpenWarnings = severities.Where(s => s.Key == SeoTaskSeverity.Warning).Sum(s => s.Count),
            OpenNotices = severities.Where(s => s.Key == SeoTaskSeverity.Info).Sum(s => s.Count),
            TopTasks = topTasks,
            RecentChanges = recent,
            ChangesThisWeek = changesWeek,
            TestsRunning = tests.Where(t => t.Key == SeoTitleTestStatus.Running).Sum(t => t.Count),
            TestsWon = tests.Where(t => t.Key == SeoTitleTestStatus.Won).Sum(t => t.Count),
            TestsLost = tests.Where(t => t.Key == SeoTitleTestStatus.Lost).Sum(t => t.Count),
            Trend = daily.Where(d => d.Date >= split).ToList(),
            Clicks28 = daily.Where(d => d.Date >= split).Sum(d => d.Clicks),
            ClicksPrevious28 = daily.Where(d => d.Date < split).Sum(d => d.Clicks),
            Bots = bots,
            Vitals = vitals,
            Calendar = SeoCalendar.Upcoming(tehranToday, categoryNames),
            Cannibalization = cannibal,
            NearPageOne = pageStats,
            LastNightlyRunAt = config?.LastNightlyRunAt,
            LastNightlySummary = config?.LastNightlySummary,
        };
    }

    public async Task<PagedList<SeoTask>> TasksAsync(SeoTaskQuery query, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(query.Take, 5, 100);
        var tasks = context.SeoTasks.AsNoTracking();
        if (query.Status is { } status)
            tasks = tasks.Where(t => t.Status == status);
        if (query.Severity is { } severity)
            tasks = tasks.Where(t => t.Severity == severity);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            tasks = tasks.Where(t => t.Title.Contains(term) || (t.Path != null && t.Path.Contains(term)) || t.Code.Contains(term));
        }

        var total = await tasks.CountAsync(cancellationToken);
        var page = Math.Clamp(query.Page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)take)));
        var items = await tasks.OrderByDescending(t => t.Severity).ThenByDescending(t => t.Impact).ThenByDescending(t => t.LastSeenAt)
            .Skip((page - 1) * take).Take(take).ToListAsync(cancellationToken);
        return new PagedList<SeoTask>(items, total, page, take);
    }

    public async Task<OperationResult> SetTaskStatusAsync(Guid id, SeoTaskStatus status, string? byName, CancellationToken cancellationToken = default)
    {
        if (status is not (SeoTaskStatus.Open or SeoTaskStatus.Dismissed or SeoTaskStatus.Fixed))
            return OperationResult.Error("وضعیت نامعتبر است.");
        var task = await context.SeoTasks.AsTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (task is null)
            return OperationResult.NotFound("این کار سئو پیدا نشد.");

        task.Status = status;
        task.ResolvedAt = status == SeoTaskStatus.Open ? null : DateTimeOffset.UtcNow;
        task.ResolvedBy = status == SeoTaskStatus.Open ? null : Trim(byName, 150);
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success(status switch
        {
            SeoTaskStatus.Open => "کار دوباره باز شد.",
            SeoTaskStatus.Dismissed => "نادیده گرفته شد؛ در بررسی‌های بعدی هم نشان داده نمی‌شود.",
            _ => "به‌عنوان «انجام‌شده» علامت خورد؛ اگر مشکل باقی باشد، بررسی شبانه دوباره بازش می‌کند.",
        });
    }

    public async Task<PagedList<SeoRevision>> RevisionsAsync(string? search, int page, CancellationToken cancellationToken = default)
    {
        const int take = 30;
        var revisions = context.SeoRevisions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            revisions = revisions.Where(r => r.Path.Contains(term) || r.PageTitle.Contains(term) || r.Summary.Contains(term));
        }

        var total = await revisions.CountAsync(cancellationToken);
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)take)));
        var items = await revisions.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * take).Take(take).ToListAsync(cancellationToken);
        return new PagedList<SeoRevision>(items, total, page, take);
    }

    public async Task<IReadOnlyList<SeoTitleTest>> TitleTestsAsync(int take, CancellationToken cancellationToken = default) =>
        await context.SeoTitleTests.AsNoTracking().OrderByDescending(t => t.StartedAt).Take(Math.Clamp(take, 1, 200)).ToListAsync(cancellationToken);

    public async Task<SeoSignalsView> SignalsAsync(int days, CancellationToken cancellationToken = default)
    {
        days = Math.Clamp(days, 1, 90);
        var from = DateOnly.FromDateTime(DateTimeOffset.UtcNow.AddDays(-days).UtcDateTime);
        var rows = (await context.SeoBotVisits.AsNoTracking().Where(v => v.Date >= from)
                .GroupBy(v => new { v.Bot, v.Path, v.Status })
                .Select(g => new { g.Key.Bot, g.Key.Path, g.Key.Status, Hits = g.Sum(v => v.Hits), Verified = g.Sum(v => v.VerifiedHits), Fake = g.Sum(v => v.FakeHits), Ms = g.Sum(v => v.TotalMs) })
                .OrderByDescending(r => r.Hits).Take(2000)
                .ToListAsync(cancellationToken))
            .Select(r => new SeoBotPathRow(r.Bot, r.Path, r.Status, r.Hits, r.Verified, r.Fake, r.Hits == 0 ? 0 : Math.Round(r.Ms / (double)r.Hits)))
            .ToList();

        var bots = rows.GroupBy(r => r.Bot)
            .Select(g => new SeoBotSummary(g.Key, g.Sum(r => r.Hits), g.Sum(r => r.Verified), g.Sum(r => r.Fake), g.Where(r => r.Status >= 400).Sum(r => r.Hits),
                g.Sum(r => r.Hits) == 0 ? 0 : Math.Round(g.Sum(r => r.AverageMs * r.Hits) / g.Sum(r => r.Hits))))
            .OrderByDescending(b => b.Hits).ToList();

        var vitals = (await context.SeoVitalStats.AsNoTracking().Where(v => v.Date >= from)
                .Select(v => new { v.PageType, v.Device, v.Metric, v.P75, v.Samples })
                .ToListAsync(cancellationToken))
            .GroupBy(v => (v.PageType, v.Device, v.Metric))
            .Select(g =>
            {
                var samples = g.Sum(v => v.Samples);
                var p75 = samples == 0 ? 0 : g.Sum(v => v.P75 * v.Samples) / samples;
                return new SeoVitalRow(g.Key.PageType, g.Key.Device, g.Key.Metric, Math.Round(p75, g.Key.Metric == "CLS" ? 3 : 0), samples, SeoVitals.Rate(g.Key.Metric, p75));
            })
            .OrderBy(v => v.PageType).ThenBy(v => v.Device).ThenBy(v => v.Metric).ToList();

        return new SeoSignalsView
        {
            Days = days,
            Bots = bots,
            TopPaths = rows.Where(r => r.Status < 400).Take(40).ToList(),
            ErrorPaths = rows.Where(r => r.Status >= 400).Take(40).ToList(),
            Vitals = vitals,
        };
    }

    private static string? Trim(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim() is var t && t.Length > max ? t[..max] : value.Trim();

    public async Task<bool> SendWeeklyIfDueAsync(CancellationToken cancellationToken = default)
    {
        var config = await context.SeoAutopilotConfigs.AsTracking().OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (config is null || !SeoAutopilotOptions.Parse(config.OptionsJson).WeeklyReport)
            return false;
        if (config.LastWeeklyReportAt is { } last && DateTimeOffset.UtcNow - last < TimeSpan.FromDays(7))
            return false;

        var report = await BuildAsync(cancellationToken);
        config.LastWeeklyReportAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        try
        {
            await notifications.NotifyAsync(new StaffAlert(StaffNotificationKind.SeoSuggestions, "گزارش هفتگی سئو", WeeklyText(report),
                "/admin/seo/autopilot", [AdminPermissions.SeoManage, AdminPermissions.SeoView]));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Weekly SEO report notification failed");
        }

        return true;
    }

    /// <summary>
    /// متن کوتاه گزارش هفتگی (اعلان پنل). مثال: «سلامت سایت ۸۷ از ۱۰۰ · ۱۲ تغییر خودکار · کلیک گوگل ۴۳۰ (+۱۲٪) · ۳ خطای باز».
    /// </summary>
    public static string WeeklyText(SeoAutopilotReport report)
    {
        var parts = new List<string>();
        if (report.Health is { } health)
            parts.Add($"سلامت سایت {PersianText.Digits(health)} از ۱۰۰");
        parts.Add($"نمرهٔ میانگین صفحه‌ها {PersianText.Digits(report.AverageScore)}");
        parts.Add($"{PersianText.Digits(report.ChangesThisWeek)} بهبود خودکار در این هفته");
        if (report.Clicks28 > 0 || report.ClicksPrevious28 > 0)
        {
            var change = report.ClicksPrevious28 == 0 ? 0 : (int)Math.Round((report.Clicks28 - report.ClicksPrevious28) * 100.0 / report.ClicksPrevious28);
            parts.Add($"کلیک گوگل ۲۸ روز: {PersianText.Digits(report.Clicks28)} ({(change >= 0 ? "+" : "−")}{PersianText.Digits(Math.Abs(change))}٪)");
        }
        if (report.OpenErrors > 0)
            parts.Add($"{PersianText.Digits(report.OpenErrors)} خطای باز");
        if (report.Calendar.FirstOrDefault() is { } occasion)
            parts.Add($"مناسبت پیش رو: {occasion.Name} ({PersianText.Digits(occasion.DaysLeft)} روز دیگر)");
        return string.Join(" · ", parts);
    }
}
