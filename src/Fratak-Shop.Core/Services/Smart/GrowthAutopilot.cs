using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// داوری بازدید یک روز نسبت به روزهای قبل. Low = «کم‌بازدید» (باید کاری کرد)؛ Baseline = میانهٔ ۱۴ روز قبل.
/// </summary>
public sealed record TrafficVerdict(bool Low, int Visitors, int Baseline, string Reason);

/// <summary>خروجی یک اجرای روزانهٔ «رشد خودکار».</summary>
public sealed record GrowthReport(bool Ran, TrafficVerdict Traffic, int SeoLifted, int AverageScore, int ProductsQueued, bool BlogBoosted, string Summary)
{
    /// <summary>چند ایدهٔ بنر امروز ساخته شود (روز کم‌بازدید: دو برابر، حداکثر ۶).</summary>
    public int BannerIdeas(int perDay) => !Ran || perDay <= 0 ? 0 : Math.Min(6, Traffic.Low ? perDay * 2 : perDay);
}

/// <summary>
/// «رشد خودکار» (دور ۲۱، بند ۴): نقش انسان فقط نظارت است؛ سامانه هر روز خودش برای بالا بردن سئو، بازدید و فروش کار می‌کند.
/// <list type="number">
/// <item><b>سنجش بازدید:</b> بازدیدکنندهٔ یکتای دیروز (جدول VisitorDays) با میانهٔ ۱۴ روز قبل مقایسه می‌شود (<see cref="Judge"/>).</item>
/// <item><b>بالا بردن سئو به سمت ۱۰۰:</b> ضعیف‌ترین صفحه‌های پربازدید (نمرهٔ زیر ۹۰) به خلبان خودکار سئو داده می‌شوند (همان مسیر ضدجریمه:
/// فقط پر کردن فیلدهای خالی/خودکار، متن انسانی دست نمی‌خورد) و نمره‌شان همان لحظه دوباره حساب می‌شود. روز عادی ۸ صفحه، روز کم‌بازدید ۲۰.</item>
/// <item><b>تکمیل کالاهای پربازدیدِ ناقص:</b> کالایی که زیاد دیده می‌شود ولی عکس/مشخصات/خلاصه کم دارد، با اولویت «گروهی» در صف هوشمند می‌رود
/// (هر کالا حداکثر یک بار در ۳۰ روز). روز عادی ۳، روز کم‌بازدید ۶.</item>
/// <item><b>روز کم‌بازدید:</b> مقالهٔ بعدی تقویم بلاگ جلو می‌افتد (حداکثر هر ۳ روز یک بار تا محتوای کم‌کیفیت انبوه نشود) و
/// ایدهٔ بنر بیشتری برای تأیید مدیر ساخته می‌شود (<see cref="GrowthReport.BannerIdeas"/>؛ ساخت تصویر در لایهٔ وب).</item>
/// </list>
/// <para>ممنوع‌ها (عمداً): پیامک/اعلان انبوه به مشتری، تخفیف خودکار، شمارش معکوس جعلی یا هر الگوی فریبنده — این‌ها تصمیم انسان‌اند.</para>
/// <para>هر روز فقط یک بار (ردیف DailyRun همان روز ایران) و هر کار جداگانه در try/catch؛ خطای یک بخش بقیه را متوقف نمی‌کند.
/// محل اجرا: SmartWorker (هر ۶ ساعت بررسی می‌کند «امروز اجرا شده؟»). گزارش: پنل ← محتوای هوشمند ← رشد خودکار.</para>
/// </summary>
public interface IGrowthAutopilot
{
    Task<GrowthReport> RunDailyAsync(DateTimeOffset now, bool force = false, CancellationToken cancellationToken = default);

    Task LogAsync(DateOnly day, GrowthActionKind kind, int count, int? metric, string summary, CancellationToken cancellationToken = default);

    Task<List<GrowthAction>> RecentAsync(int days = 30, CancellationToken cancellationToken = default);

    /// <summary>بازدید یکتای روزهای اخیر (برای نمودار ساده در پنل). کلید = روز ایران.</summary>
    Task<List<(DateOnly Day, int Visitors)>> VisitorsAsync(DateOnly today, int days = 15, CancellationToken cancellationToken = default);
}

public sealed class GrowthAutopilot(
    FratakShopDbContext context,
    ISeoAutopilotService autopilot,
    ISeoEngineService engine,
    ISmartTaskQueue queue,
    ISmartCatalogService catalog,
    ISmartConfigService config,
    ILogger<GrowthAutopilot> logger) : IGrowthAutopilot
{
    /// <summary>صفحهٔ با نمرهٔ کمتر از این «ضعیف» است و به خلبان سئو داده می‌شود.</summary>
    public const int SeoTarget = 90;

    /// <summary>نام انجام‌دهنده در صف (همان نام دستیار هوشمند لایهٔ وب، تا فهرست کارها یکدست بماند).</summary>
    public const string Actor = "دستیار هوشمند";

    /// <summary>
    /// داوری بازدید (تابع خالص؛ آزمون واحد دارد).
    /// <list type="bullet">
    /// <item>میانهٔ روزهای قبل ≥ ۲۰ و دیروز کمتر از ۷۰٪ آن ← «افت بازدید». مثال: میانه ۲۰۰، دیروز ۱۲۰ ← کم (۶۰٪).</item>
    /// <item>سایت تازه/کم‌بازدید (میانه زیر ۲۰): هر روز زیر ۲۰ بازدید «کم» است تا سامانه برای جذب بازدید فعال بماند.</item>
    /// <item>کمتر از ۳ روز داده ← فقط قاعدهٔ «زیر ۲۰».</item>
    /// </list>
    /// </summary>
    public static TrafficVerdict Judge(int yesterday, IReadOnlyList<int> previousDays)
    {
        yesterday = Math.Max(0, yesterday);
        var history = previousDays.Where(v => v >= 0).ToList();
        var baseline = history.Count >= 3 ? Median(history) : 0;
        if (baseline >= 20 && yesterday < baseline * 0.7)
            return new TrafficVerdict(true, yesterday, baseline, $"بازدید دیروز {PersianText.Digits(yesterday)} بود؛ معمولاً حدود {PersianText.Digits(baseline)}.");
        if (baseline < 20 && yesterday < 20)
            return new TrafficVerdict(true, yesterday, baseline, $"بازدید هنوز کم است ({PersianText.Digits(yesterday)} نفر در روز).");
        return new TrafficVerdict(false, yesterday, baseline, $"بازدید دیروز {PersianText.Digits(yesterday)} نفر؛ عادی.");
    }

    private static int Median(List<int> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    public async Task<GrowthReport> RunDailyAsync(DateTimeOffset now, bool force = false, CancellationToken cancellationToken = default)
    {
        var today = IranClock.Today(now);
        var options = await config.GetAsync(cancellationToken);
        var visitors = await VisitorsAsync(today, 15, cancellationToken);
        var yesterday = visitors.FirstOrDefault(v => v.Day == today.AddDays(-1)).Visitors;
        var traffic = Judge(yesterday, visitors.Where(v => v.Day < today.AddDays(-1)).Select(v => v.Visitors).ToList());

        if (!options.Enabled || !options.GrowthAutopilot)
            return new GrowthReport(false, traffic, 0, 0, 0, false, "رشد خودکار خاموش است.");
        if (!force && await context.GrowthActions.AsNoTracking().AnyAsync(a => a.Day == today && a.Kind == GrowthActionKind.DailyRun, cancellationToken))
            return new GrowthReport(false, traffic, 0, 0, 0, false, "امروز اجرا شده است.");

        // ردیف «امروز اجرا شد» اول ثبت می‌شود تا اگر وسط کار خطا یا ری‌استارت رخ داد، دوباره همان روز تکرار نشود.
        await LogAsync(today, GrowthActionKind.DailyRun, 0, traffic.Visitors, traffic.Reason, cancellationToken);
        if (traffic.Low)
            await LogAsync(today, GrowthActionKind.TrafficDrop, 1, traffic.Visitors, traffic.Reason + " اقدام‌های جذب بازدید فعال شد.", cancellationToken);

        var (lifted, average) = await SafeAsync("seo", () => LiftSeoAsync(today, traffic.Low ? 20 : 8, cancellationToken), (0, 0));
        var queued = await SafeAsync("products", () => RefreshProductsAsync(today, now, traffic.Low ? 6 : 3, cancellationToken), 0);
        var boosted = traffic.Low && options.BlogPlanner && options.BlogPostsPerWeek > 0
                      && await SafeAsync("blog", () => BoostBlogAsync(today, now, cancellationToken), false);

        var parts = new List<string> { traffic.Reason };
        if (lifted > 0)
            parts.Add($"سئوی {PersianText.Digits(lifted)} صفحه بهتر شد (میانگین نمره {PersianText.Digits(average)}).");
        if (queued > 0)
            parts.Add($"{PersianText.Digits(queued)} کالای پربازدیدِ ناقص برای تکمیل در صف رفت.");
        if (boosted)
            parts.Add("مقالهٔ بعدی بلاگ جلو افتاد.");
        return new GrowthReport(true, traffic, lifted, average, queued, boosted, string.Join(" ", parts));
    }

    /// <summary>ضعیف‌ترین صفحه‌های پربازدید ← خلبان سئو (ضدجریمه) ← نمره‌دهی دوباره.</summary>
    private async Task<(int Lifted, int Average)> LiftSeoAsync(DateOnly today, int take, CancellationToken cancellationToken)
    {
        var weak = await context.SeoScores.AsNoTracking()
            .Where(s => s.Score < SeoTarget)
            .OrderByDescending(s => s.Views).ThenBy(s => s.Score)
            .Take(take)
            .Select(s => new { s.Kind, s.EntityId, s.Score })
            .ToListAsync(cancellationToken);
        var lifted = 0;
        foreach (var page in weak)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await autopilot.ProcessAsync(page.Kind, page.EntityId, cancellationToken);
                lifted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Growth SEO lift failed for {Kind} {Id}", page.Kind, page.EntityId);
                context.ChangeTracker.Clear();
            }
        }
        foreach (var group in weak.GroupBy(w => w.Kind))
        {
            try
            {
                await engine.RefreshScoresAsync(group.Key, group.Select(g => g.EntityId).ToList(), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Growth score refresh failed for {Kind}", group.Key);
                context.ChangeTracker.Clear();
            }
        }
        var any = await context.SeoScores.AnyAsync(cancellationToken);
        var average = any ? (int)Math.Round(await context.SeoScores.AverageAsync(s => (double)s.Score, cancellationToken)) : 0;
        if (weak.Count > 0)
        {
            await LogAsync(today, GrowthActionKind.SeoLift, lifted, average,
                $"{PersianText.Digits(lifted)} صفحهٔ کم‌نمره (زیر {PersianText.Digits(SeoTarget)}) بهینه شد؛ میانگین نمرهٔ سایت: {PersianText.Digits(average)} از ۱۰۰.", cancellationToken);
        }
        return (lifted, average);
    }

    /// <summary>کالای پربازدید + ناقص ← تکمیل هوشمند (اولویت گروهی؛ هر کالا حداکثر یک بار در ۳۰ روز).</summary>
    private async Task<int> RefreshProductsAsync(DateOnly today, DateTimeOffset now, int take, CancellationToken cancellationToken)
    {
        var incomplete = (await catalog.ListIncompleteAsync(400, cancellationToken)).ToDictionary(p => p.Id);
        if (incomplete.Count == 0)
            return 0;
        var recent = (await queue.RecentTargetsAsync(SmartTaskKind.ProductComplete, now.AddDays(-30), cancellationToken: cancellationToken)).ToHashSet();
        var ids = incomplete.Keys.ToList();
        var popular = await context.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.IsActive && p.ViewCount > 0)
            .OrderByDescending(p => p.ViewCount)
            .Select(p => p.Id)
            .Take(take * 4)
            .ToListAsync(cancellationToken);
        var count = 0;
        foreach (var id in popular.Where(id => !recent.Contains(id)).Take(take))
        {
            await queue.EnqueueAsync(SmartTaskKind.ProductComplete, SmartTarget.Product, id, incomplete[id].Name, SmartPriority.Batch,
                userName: Actor, cancellationToken: cancellationToken);
            count++;
        }
        if (count > 0)
            await LogAsync(today, GrowthActionKind.ProductRefresh, count, null, $"{PersianText.Digits(count)} کالای پربازدید که عکس یا مشخصات کم داشت برای تکمیل در صف رفت.", cancellationToken);
        return count;
    }

    /// <summary>روز کم‌بازدید: نزدیک‌ترین مقالهٔ برنامه‌ریزی‌شده همین حالا سررسید می‌شود (حداکثر هر ۳ روز یک بار).</summary>
    private async Task<bool> BoostBlogAsync(DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var since = today.AddDays(-2);
        if (await context.GrowthActions.AsNoTracking().AnyAsync(a => a.Kind == GrowthActionKind.BlogBoost && a.Day >= since, cancellationToken))
            return false;
        var next = await context.BlogPlanItems.AsTracking()
            .Where(i => i.Status == BlogPlanStatus.Planned && i.PlannedFor > now)
            .OrderBy(i => i.PlannedFor)
            .FirstOrDefaultAsync(cancellationToken);
        if (next is null)
            return false;
        next.PlannedFor = now;
        await context.SaveChangesAsync(cancellationToken);
        await LogAsync(today, GrowthActionKind.BlogBoost, 1, null, $"مقالهٔ «{SmartTaskQueue.Limit(next.Topic, 120)}» زودتر از برنامه نوشته می‌شود تا بازدید تازه بیاورد.", cancellationToken);
        return true;
    }

    private async Task<T> SafeAsync<T>(string step, Func<Task<T>> run, T fallback)
    {
        try
        {
            return await run();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Growth autopilot step {Step} failed", step);
            context.ChangeTracker.Clear();
            return fallback;
        }
    }

    public async Task LogAsync(DateOnly day, GrowthActionKind kind, int count, int? metric, string summary, CancellationToken cancellationToken = default)
    {
        context.GrowthActions.Add(new GrowthAction
        {
            Day = day,
            Kind = kind,
            Count = Math.Max(0, count),
            Metric = metric,
            Summary = SmartTaskQueue.Limit(summary, 1000) ?? "",
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<List<GrowthAction>> RecentAsync(int days = 30, CancellationToken cancellationToken = default)
    {
        var since = IranClock.Today(DateTimeOffset.UtcNow).AddDays(-Math.Clamp(days, 1, 365));
        return context.GrowthActions.AsNoTracking()
            .Where(a => a.Day >= since)
            .OrderByDescending(a => a.CreatedAt)
            .Take(300)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<(DateOnly Day, int Visitors)>> VisitorsAsync(DateOnly today, int days = 15, CancellationToken cancellationToken = default)
    {
        var since = today.AddDays(-Math.Clamp(days, 1, 90));
        var rows = await context.VisitorDays.AsNoTracking()
            .Where(v => v.Day >= since && v.Day < today)
            .GroupBy(v => v.Day)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var map = rows.ToDictionary(r => r.Day, r => r.Count);
        var result = new List<(DateOnly, int)>();
        for (var d = since; d < today; d = d.AddDays(1))
            result.Add((d, map.GetValueOrDefault(d)));
        return result;
    }
}
