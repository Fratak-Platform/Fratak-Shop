using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Analytics;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Reports;

// ═══════════════════════════════════════════════════════════════════════════
//  ذخیرهٔ آمار بازدید (مرحلهٔ هفتم – فاز ۷)
//  بافر وب (Web/Infrastructure/Analytics/AnalyticsBuffer) رویدادها را در حافظه جمع می‌کند و هر دقیقه یک «بسته»
//  (AnalyticsBatch) به SaveAsync می‌دهد؛ این کلاس آن را با ردیف‌های موجود ادغام می‌کند (upsert).
//  این‌طور به‌جای یک INSERT برای هر بازدید، در هر دقیقه چند کوئری کوچک اجرا می‌شود.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// خلاصهٔ یک بازدیدکننده در یک روز که در حافظه جمع شده و باید با دیتابیس ادغام شود.
/// </summary>
/// <param name="Stages">جمع بیتی <see cref="FunnelStage"/>.</param>
/// <param name="PageViews">تعداد صفحهٔ دیده‌شده از آخرین ذخیره (به عدد قبلی اضافه می‌شود).</param>
public sealed record VisitorHit(
    DateOnly Day,
    string VisitorKey,
    Guid? UserId,
    int Stages,
    int PageViews,
    byte FirstHour,
    string Device,
    TrafficTouch Touch,
    string? LandingPath,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);

/// <summary>افزایش یک شمارندهٔ روزانه. مثال: (امروز، «pv»، شناسهٔ محصول، ۳).</summary>
public sealed record CounterHit(DateOnly Day, string Metric, string Key, int Count);

/// <summary>یک بستهٔ ذخیره (خروجی بافر در هر دقیقه).</summary>
public sealed record AnalyticsBatch(IReadOnlyList<VisitorHit> Visitors, IReadOnlyList<CounterHit> Counters)
{
    public bool IsEmpty => Visitors.Count == 0 && Counters.Count == 0;
}

public interface IAnalyticsService
{
    /// <summary>ادغام یک بسته با دیتابیس (ردیف تازه یا افزایش ردیف موجود).</summary>
    Task SaveAsync(AnalyticsBatch batch, CancellationToken cancellationToken = default);

    /// <summary>
    /// ثبت منبع ورودی و شناسهٔ بازدیدکننده روی سفارش تازه (فقط اگر قبلاً ثبت نشده باشد).
    /// استفاده: Pages/Checkout بعد از ثبت موفق سفارش.
    /// </summary>
    Task AttributeOrderAsync(Guid orderId, string? visitorKey, TrafficTouch? touch, CancellationToken cancellationToken = default);

    /// <summary>پاک کردن ردیف‌های روزانهٔ بازدیدکننده‌ها قدیمی‌تر از مدت نگهداری. خروجی: تعداد حذف‌شده.</summary>
    Task<int> PurgeAsync(int retentionDays, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public class AnalyticsService(FratakShopDbContext context) : IAnalyticsService
{
    /// <summary>اندازهٔ هر دسته در ادغام (تا شرط IN کوئری خیلی بزرگ نشود).</summary>
    private const int ChunkSize = 300;

    public async Task SaveAsync(AnalyticsBatch batch, CancellationToken cancellationToken = default)
    {
        if (batch.IsEmpty)
            return;

        // هر دسته جدا ذخیره می‌شود؛ اگر دو نسخهٔ برنامه هم‌زمان یک ردیف تازه بسازند، ایندکس یکتا یکی را رد می‌کند و
        // همان دسته (نه کل بسته، تا دسته‌های ذخیره‌شده دوباره جمع نشوند) یک بار دیگر با داده‌های تازهٔ دیتابیس ادغام می‌شود.
        foreach (var chunk in Normalize(batch.Visitors).Chunk(ChunkSize))
            await WithRetryAsync(() => MergeVisitorsAsync(chunk, cancellationToken));
        foreach (var chunk in Normalize(batch.Counters).Chunk(ChunkSize))
            await WithRetryAsync(() => MergeCountersAsync(chunk, cancellationToken));
    }

    private async Task WithRetryAsync(Func<Task> save)
    {
        try
        {
            await save();
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            await save();
        }
        finally
        {
            context.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// ادغام بازدیدکننده‌ها: مراحل با OR بیتی جمع می‌شوند، تعداد صفحه‌ها اضافه و «آخرین بازدید» به‌روز می‌شود.
    /// منبع، صفحهٔ ورود و ساعت اول همان مقدار اولین ذخیرهٔ روز می‌ماند.
    /// </summary>
    private async Task MergeVisitorsAsync(VisitorHit[] hits, CancellationToken cancellationToken)
    {
        var days = hits.Select(h => h.Day).Distinct().ToList();
        var keys = hits.Select(h => h.VisitorKey).Distinct().ToList();
        var existing = await context.VisitorDays.AsTracking()
            .Where(v => days.Contains(v.Day) && keys.Contains(v.VisitorKey))
            .ToDictionaryAsync(v => (v.Day, v.VisitorKey), cancellationToken);

        foreach (var hit in hits)
        {
            if (existing.TryGetValue((hit.Day, hit.VisitorKey), out var row))
            {
                row.Stages |= hit.Stages;
                row.PageViews = SafeAdd(row.PageViews, hit.PageViews);
                row.UserId ??= hit.UserId;
                if (hit.LastSeenAt > row.LastSeenAt)
                    row.LastSeenAt = hit.LastSeenAt;
                continue;
            }

            context.VisitorDays.Add(new VisitorDay
            {
                Day = hit.Day,
                VisitorKey = hit.VisitorKey,
                UserId = hit.UserId,
                Stages = hit.Stages,
                PageViews = hit.PageViews,
                FirstHour = (byte)Math.Clamp((int)hit.FirstHour, 0, 23),
                Device = Cut(hit.Device, 10),
                Source = Cut(hit.Touch.Source, 60),
                Medium = Cut(hit.Touch.Medium, 30),
                Campaign = hit.Touch.Campaign is null ? null : Cut(hit.Touch.Campaign, 100),
                LandingPath = hit.LandingPath is null ? null : Cut(hit.LandingPath, 300),
                FirstSeenAt = hit.FirstSeenAt,
                LastSeenAt = hit.LastSeenAt,
            });
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task MergeCountersAsync(CounterHit[] hits, CancellationToken cancellationToken)
    {
        var days = hits.Select(h => h.Day).Distinct().ToList();
        var metrics = hits.Select(h => h.Metric).Distinct().ToList();
        var keys = hits.Select(h => h.Key).Distinct().ToList();
        var existing = await context.AnalyticsCounters.AsTracking()
            .Where(c => days.Contains(c.Day) && metrics.Contains(c.Metric) && keys.Contains(c.Key))
            .ToDictionaryAsync(c => (c.Day, c.Metric, c.Key), cancellationToken);

        foreach (var hit in hits)
        {
            if (existing.TryGetValue((hit.Day, hit.Metric, hit.Key), out var row))
                row.Count = SafeAdd(row.Count, hit.Count);
            else
                context.AnalyticsCounters.Add(new AnalyticsCounter { Day = hit.Day, Metric = Cut(hit.Metric, 20), Key = Cut(hit.Key, 64), Count = hit.Count });
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>ردیف‌های تکراری یک بسته (اگر بافر دو بار یک کلید داد) پیش از ادغام یکی می‌شوند.</summary>
    internal static IEnumerable<VisitorHit> Normalize(IEnumerable<VisitorHit> hits) =>
        hits.Where(h => TrafficRules.IsValidVisitorKey(h.VisitorKey))
            .GroupBy(h => (h.Day, h.VisitorKey))
            .Select(g =>
            {
                var first = g.OrderBy(h => h.FirstSeenAt).First();
                return first with
                {
                    Stages = g.Aggregate(0, (acc, h) => acc | h.Stages),
                    PageViews = g.Sum(h => h.PageViews),
                    UserId = g.Select(h => h.UserId).FirstOrDefault(u => u.HasValue),
                    LastSeenAt = g.Max(h => h.LastSeenAt),
                };
            });

    internal static IEnumerable<CounterHit> Normalize(IEnumerable<CounterHit> hits) =>
        hits.Where(h => h.Count > 0 && !string.IsNullOrEmpty(h.Metric) && !string.IsNullOrEmpty(h.Key))
            .GroupBy(h => (h.Day, h.Metric, h.Key))
            .Select(g => new CounterHit(g.Key.Day, g.Key.Metric, g.Key.Key, g.Sum(h => h.Count)));

    public async Task AttributeOrderAsync(Guid orderId, string? visitorKey, TrafficTouch? touch, CancellationToken cancellationToken = default)
    {
        var key = TrafficRules.IsValidVisitorKey(visitorKey) ? visitorKey : null;
        var source = touch ?? TrafficTouch.Direct;
        if (key is null && source.IsDirect && source.Campaign is null)
        {
            // فقط «مستقیم» را ثبت می‌کنیم تا گزارش کمپین‌ها سفارش بی‌منبع نداشته باشد.
            await context.Orders.Where(o => o.Id == orderId && o.TrafficSource == null)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.TrafficSource, TrafficRules.DirectSource)
                    .SetProperty(o => o.TrafficMedium, TrafficRules.DirectSource), cancellationToken);
            return;
        }

        var sourceValue = Cut(source.Source, 60);
        var mediumValue = Cut(source.Medium, 30);
        var campaignValue = source.Campaign is null ? null : Cut(source.Campaign, 100);
        await context.Orders.Where(o => o.Id == orderId && o.TrafficSource == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.TrafficSource, sourceValue)
                .SetProperty(o => o.TrafficMedium, mediumValue)
                .SetProperty(o => o.TrafficCampaign, campaignValue)
                .SetProperty(o => o.VisitorKey, key), cancellationToken);
    }

    public async Task<int> PurgeAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        var limit = IranClock.Today(DateTimeOffset.UtcNow).AddDays(-Math.Clamp(retentionDays, 30, 1500));
        // حذف واقعی (نه نرم): این ردیف‌ها دادهٔ آماری هستند و نیازی به بازگردانی ندارند.
        return await context.VisitorDays.IgnoreQueryFilters().Where(v => v.Day < limit).ExecuteDeleteAsync(cancellationToken);
    }

    private static int SafeAdd(int a, int b) => (int)Math.Min(int.MaxValue, (long)a + b);

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max];
}
