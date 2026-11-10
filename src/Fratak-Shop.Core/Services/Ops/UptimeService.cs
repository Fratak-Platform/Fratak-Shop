using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Ops;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Ops;

/// <summary>
/// خلاصهٔ دسترس‌پذیری برای صفحهٔ «دسترس‌پذیری» و گزارش امنیتی/عملیاتی.
/// مثال: Day=100، Week=99.86، Month=99.95، DowntimeMinutes30=21، Incidents=[…].
/// </summary>
public sealed record UptimeSummary(double Day, double Week, double Month, int DowntimeMinutes30, int IncidentCount30,
    IReadOnlyList<UptimeIncident> Incidents);

/// <summary>
/// دور ۲۴ (پیشنهاد ۷۳): پایش دسترس‌پذیری داخلی و رایگان.
/// <para>
/// چطور: پایشگر وب (<c>Web/Infrastructure/Ops/UptimeMonitor</c>) هر دقیقه «ضربان» را در فایل می‌نویسد و اتصال پایگاه داده را می‌سنجد.
/// ۱) اگر برنامه ناگهانی متوقف شده باشد (بدون خاموشی عادی) و فاصلهٔ آخرین ضربان تا روشن شدن دوباره ≥ ۳ دقیقه باشد، یک «قطعی»
///    با همان بازه ثبت می‌شود؛ ۲) قطعی پایگاه داده در حافظه نگه داشته و پس از وصل شدن دوباره ثبت می‌شود (هنگام قطعی نوشتن ممکن نیست).
/// پس از هر ثبت، به کارکنان فنی اعلان می‌رود: «سایت از ۰۳:۱۰ تا ۰۳:۲۴ در دسترس نبود (۱۴ دقیقه)».
/// </para>
/// <para>پایش بیرونی (وقتی کل سرور خاموش است) با <c>/healthz</c> و سرویس‌های رایگانی مثل UptimeRobot انجام می‌شود؛ این سرویس مکمل آن است.</para>
/// محل استفاده: UptimeMonitor (ثبت)، <c>/admin/system/uptime</c> (نمایش)، گزارش امنیتی هفتگی.
/// </summary>
public interface IUptimeService
{
    /// <summary>ثبت یک دورهٔ قطعی تمام‌شده و اعلان به کارکنان فنی. دورهٔ کوتاه‌تر از <see cref="UptimeRules.MinIncident"/> ثبت نمی‌شود (null).</summary>
    Task<UptimeIncident?> RecordAsync(UptimeIncidentKind kind, DateTimeOffset startedAt, DateTimeOffset endedAt, string? detail, CancellationToken cancellationToken = default);

    /// <summary>خلاصهٔ ۲۴ ساعت / ۷ روز / ۳۰ روز اخیر.</summary>
    Task<UptimeSummary> SummaryAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>پاک کردن رخدادهای قدیمی‌تر از <paramref name="days"/> روز (پیش‌فرض ۳۶۵). خروجی: تعداد حذف‌شده.</summary>
    Task<int> PruneAsync(DateTimeOffset now, int days = 365, CancellationToken cancellationToken = default);
}

/// <summary>منطق خالص دسترس‌پذیری (بدون دیتابیس) برای تست‌پذیری.</summary>
public static class UptimeRules
{
    /// <summary>کوتاه‌ترین قطعی قابل ثبت (فاصلهٔ ضربان یک دقیقه است؛ کمتر از ۳ دقیقه = ری‌استارت عادی).</summary>
    public static readonly TimeSpan MinIncident = TimeSpan.FromMinutes(3);

    /// <summary>
    /// درصد دسترس‌پذیری بازهٔ [from, to] با ادغام رخدادهای هم‌پوشان (رخداد باز تا <paramref name="to"/> حساب می‌شود).
    /// مثال: بازهٔ ۲۴ ساعته با یک قطعی ۱۴۴ دقیقه‌ای ← ۹۰٪؛ دو رخداد هم‌پوشان ۱۰:۰۰–۱۰:۳۰ و ۱۰:۲۰–۱۰:۴۰ ← ۴۰ دقیقه (نه ۵۰).
    /// </summary>
    public static double Percent(IEnumerable<(DateTimeOffset Start, DateTimeOffset? End)> incidents, DateTimeOffset from, DateTimeOffset to)
    {
        var total = (to - from).TotalSeconds;
        if (total <= 0)
            return 100;
        var down = DownSeconds(incidents, from, to);
        return Math.Round(Math.Clamp(100 * (1 - down / total), 0, 100), 2);
    }

    /// <summary>ثانیه‌های قطعی داخل بازه (ادغام هم‌پوشانی‌ها). مثال: بالا ← ۲۴۰۰ ثانیه.</summary>
    public static double DownSeconds(IEnumerable<(DateTimeOffset Start, DateTimeOffset? End)> incidents, DateTimeOffset from, DateTimeOffset to)
    {
        var clipped = incidents
            .Select(i => (Start: i.Start < from ? from : i.Start, End: (i.End ?? to) > to ? to : i.End ?? to))
            .Where(i => i.End > i.Start)
            .OrderBy(i => i.Start)
            .ToList();
        double seconds = 0;
        DateTimeOffset? runStart = null, runEnd = null;
        foreach (var (start, end) in clipped)
        {
            if (runEnd is { } re && start <= re)
            {
                if (end > re)
                    runEnd = end;
                continue;
            }
            if (runStart is { } rs && runEnd is { } r)
                seconds += (r - rs).TotalSeconds;
            runStart = start;
            runEnd = end;
        }
        if (runStart is { } lastStart && runEnd is { } lastEnd)
            seconds += (lastEnd - lastStart).TotalSeconds;
        return seconds;
    }

    /// <summary>
    /// از محتوای فایل ضربان، بازهٔ قطعی را می‌سازد؛ خاموشی عادی (stopped) یا فاصلهٔ کوتاه ← null.
    /// قالب فایل: «beat|2026-10-22T10:00:00+00:00» یا «stopped|…». مثال: («beat|10:00»، اکنون 10:14) ← (10:00، 10:14).
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End)? GapFromHeartbeat(string? content, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;
        var parts = content.Trim().Split('|', 2);
        if (parts.Length != 2 || parts[0] != "beat"
            || !DateTimeOffset.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var last))
            return null;
        if (last > now || now - last < MinIncident || now - last > TimeSpan.FromDays(30))
            return null; // ساعت جابه‌جا شده یا فایل خیلی قدیمی (مثلاً سایت ماه‌ها خاموش بوده) – قابل اعتماد نیست.
        return (last, now);
    }

    /// <summary>متن کوتاه مدت. مثال: ۱۴ ← «۱۴ دقیقه»؛ ۹۵ ← «۱ ساعت و ۳۵ دقیقه».</summary>
    public static string Duration(TimeSpan span)
    {
        var minutes = (int)Math.Round(span.TotalMinutes);
        if (minutes < 60)
            return $"{Persian(minutes)} دقیقه";
        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0 ? $"{Persian(hours)} ساعت" : $"{Persian(hours)} ساعت و {Persian(rest)} دقیقه";
    }

    /// <summary>عنوان اعلان. مثال: «سایت ۱۴ دقیقه در دسترس نبود».</summary>
    public static string AlertTitle(UptimeIncidentKind kind, TimeSpan span) => kind == UptimeIncidentKind.DatabaseDown
        ? $"پایگاه داده {Duration(span)} قطع بود"
        : $"سایت {Duration(span)} در دسترس نبود";

    private static string Persian(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        .Aggregate("", (acc, c) => acc + (char.IsAsciiDigit(c) ? (char)('۰' + (c - '0')) : c));
}

/// <inheritdoc cref="IUptimeService"/>
public sealed class UptimeService(FratakShopDbContext context, IStaffNotificationService notifications) : IUptimeService
{
    /// <inheritdoc />
    public async Task<UptimeIncident?> RecordAsync(UptimeIncidentKind kind, DateTimeOffset startedAt, DateTimeOffset endedAt, string? detail, CancellationToken cancellationToken = default)
    {
        if (endedAt - startedAt < UptimeRules.MinIncident || !Enum.IsDefined(kind))
            return null;
        // هم‌پوشانی با رخداد ثبت‌شده (مثلاً دو پروسهٔ هم‌زمان IIS) دوباره ثبت نمی‌شود.
        if (await context.UptimeIncidents.AnyAsync(i => i.Kind == kind && i.StartedAt < endedAt && i.EndedAt > startedAt, cancellationToken))
            return null;
        var incident = new UptimeIncident
        {
            Kind = kind,
            StartedAt = startedAt,
            EndedAt = endedAt,
            Detail = detail is { Length: > 500 } ? detail[..500] : detail,
        };
        context.UptimeIncidents.Add(incident);
        await context.SaveChangesAsync(cancellationToken);
        try
        {
            var span = endedAt - startedAt;
            await notifications.NotifyAsync(new StaffAlert(StaffNotificationKind.Alert, UptimeRules.AlertTitle(kind, span),
                $"از {PersianDate.Format(startedAt)} تا {PersianDate.Format(endedAt)}. {detail}".Trim(),
                "/admin/system/uptime", [AdminPermissions.MonitoringView]));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // اعلان کمکی است؛ ثبت رخداد مهم‌تر است.
        }
        return incident;
    }

    /// <inheritdoc />
    public async Task<UptimeSummary> SummaryAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var monthAgo = now.AddDays(-30);
        var incidents = await context.UptimeIncidents
            .Where(i => i.EndedAt == null || i.EndedAt > monthAgo)
            .OrderByDescending(i => i.StartedAt).Take(500)
            .ToListAsync(cancellationToken);
        var spans = incidents.Select(i => (i.StartedAt, i.EndedAt)).ToList();
        return new UptimeSummary(
            UptimeRules.Percent(spans, now.AddDays(-1), now),
            UptimeRules.Percent(spans, now.AddDays(-7), now),
            UptimeRules.Percent(spans, monthAgo, now),
            (int)Math.Round(UptimeRules.DownSeconds(spans, monthAgo, now) / 60),
            incidents.Count,
            incidents);
    }

    /// <inheritdoc />
    public Task<int> PruneAsync(DateTimeOffset now, int days = 365, CancellationToken cancellationToken = default)
    {
        var cutoff = now.AddDays(-Math.Max(30, days));
        return context.UptimeIncidents.IgnoreQueryFilters().Where(i => i.StartedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
