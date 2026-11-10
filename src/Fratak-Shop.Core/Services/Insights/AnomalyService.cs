using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Learning;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Insights;

/// <summary>یک شاخص روزانه برای پایش. Watch = کدام جهت مهم است (مثلاً برای لغو فقط افزایش).</summary>
public sealed record DailyMetric(string Key, string Title, double[] History, double Yesterday, int[] Weekdays, AnomalyDirection[] Watch, string Url, string Permission);

/// <summary>
/// دور ۲۴ (پیشنهاد ۵۶): پایش روزانهٔ ناهنجاری‌ها و اعلان به کارکنان.
/// <para>
/// هر روز «دیروز» (به وقت ایران) با ۶۰ روز قبل مقایسه می‌شود (<see cref="AnomalyDetector"/>): تعداد سفارش، فروش (تومان)،
/// لغو سفارش، درخواست مرجوعی و ثبت‌نام. فقط ناهنجاری واقعی (z ≥ ۳) اعلان می‌شود و هر شاخص در هر روز حداکثر یک بار
/// (حتی اگر سایت دوباره راه‌اندازی شود). در فروشگاه کم‌داده (میانگین کمتر از ۳ در روز) هشداری داده نمی‌شود تا مزاحمت نباشد.
/// </para>
/// <para>مثال: دیروز ۲ سفارش در برابر انتظار ۲۰ ← اعلان «افت غیرعادی سفارش‌ها: ۲ (انتظار حدود ۲۰)» با لینک گزارش.</para>
/// <para>محل اجرا: EngagementMaintenanceService (کار روزانه).</para>
/// </summary>
public interface IAnomalyService
{
    /// <summary>بررسی دیروز و ارسال اعلان. خروجی = تعداد ناهنجاری اعلان‌شده.</summary>
    Task<int> CheckAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>شاخص‌های روزانه (برای تست و نمایش).</summary>
    Task<List<DailyMetric>> MetricsAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class AnomalyService(FratakShopDbContext context, IStaffNotificationService notifications) : IAnomalyService
{
    /// <summary>روزهای تاریخچه.</summary>
    public const int HistoryDays = 60;

    /// <inheritdoc />
    public async Task<int> CheckAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var sent = 0;
        var day = PersianDate.Format(IranClock.StartOfDay(IranClock.Today(now).AddDays(-1)));
        foreach (var metric in await MetricsAsync(now, cancellationToken))
        {
            var result = AnomalyDetector.Check(metric.History, metric.Yesterday, metric.Weekdays);
            if (!result.IsAnomaly || !metric.Watch.Contains(result.Direction))
                continue;
            var title = Title(metric, result, day);
            // یک‌بار در روز: اگر همین عنوان در ۳۶ ساعت اخیر فرستاده شده، دوباره نمی‌فرستیم.
            var since = now.AddHours(-36);
            if (await context.StaffNotifications.AnyAsync(n => n.Title == title && n.CreatedAt >= since, cancellationToken))
                continue;
            await notifications.NotifyAsync(new StaffAlert(StaffNotificationKind.Alert, title,
                $"دیروز: {Show(metric, result.Value)} · انتظار حدود {Show(metric, result.Expected)} (تشخیص خودکار از روند ۶۰ روز اخیر)",
                metric.Url, [metric.Permission]));
            sent++;
        }
        return sent;
    }

    /// <summary>عنوان اعلان. مثال: «افت غیرعادی تعداد سفارش (۱۴۰۵/۰۸/۰۱)».</summary>
    public static string Title(DailyMetric metric, AnomalyResult result, string day) =>
        $"{(result.Direction == AnomalyDirection.Drop ? "افت" : "افزایش")} غیرعادی {metric.Title} ({day})";

    private static string Show(DailyMetric metric, double value) =>
        metric.Key == "revenue" ? $"{Math.Round(value):N0} تومان" : Math.Round(value).ToString("N0");

    /// <inheritdoc />
    public async Task<List<DailyMetric>> MetricsAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var today = IranClock.Today(now);
        var first = today.AddDays(-(HistoryDays + 1));
        var from = IranClock.StartOfDay(first);
        var to = IranClock.StartOfDay(today);
        var days = Enumerable.Range(0, HistoryDays + 1).Select(i => first.AddDays(i)).ToList(); // آخرین = دیروز

        var orders = await context.Orders.AsNoTracking()
            .Where(o => o.CreatedAt >= from && o.CreatedAt < to)
            .Select(o => new { o.CreatedAt, o.Status, o.PaymentStatus, o.PaymentMethod, o.PayableAmount, o.CancelledAt })
            .Take(200_000)
            .ToListAsync(cancellationToken);
        var cancelled = await context.Orders.AsNoTracking()
            .Where(o => o.CancelledAt != null && o.CancelledAt >= from && o.CancelledAt < to && (o.PaidAt != null || o.PaymentMethod == PaymentMethod.CashOnDelivery))
            .Select(o => o.CancelledAt!.Value).Take(200_000).ToListAsync(cancellationToken);
        var returns = await context.ReturnRequests.AsNoTracking()
            .Where(r => r.CreatedAt >= from && r.CreatedAt < to).Select(r => r.CreatedAt).Take(200_000).ToListAsync(cancellationToken);
        var signups = await context.Users.AsNoTracking()
            .Where(u => u.CreatedAt >= from && u.CreatedAt < to).Select(u => u.CreatedAt).Take(200_000).ToListAsync(cancellationToken);

        // سفارش واقعی = پرداخت‌شده یا پرداخت در محل (سفارش رهاشدهٔ منتظر پرداخت شمرده نمی‌شود).
        var real = orders.Where(o => o.PaymentStatus == PaymentStatus.Paid || o.PaymentMethod == PaymentMethod.CashOnDelivery).ToList();
        double[] Bucket<T>(IEnumerable<T> items, Func<T, DateTimeOffset> at, Func<T, double> value)
        {
            var map = items.GroupBy(i => IranClock.Today(at(i))).ToDictionary(g => g.Key, g => g.Sum(value));
            return days.Select(d => map.GetValueOrDefault(d)).ToArray();
        }
        var weekdays = days.Select(d => (int)d.DayOfWeek).ToArray();
        DailyMetric Make(string key, string title, double[] series, AnomalyDirection[] watch, string url, string permission) =>
            new(key, title, series[..^1], series[^1], weekdays, watch, url, permission);

        AnomalyDirection[] both = [AnomalyDirection.Drop, AnomalyDirection.Spike];
        AnomalyDirection[] spike = [AnomalyDirection.Spike];
        return
        [
            Make("orders", "تعداد سفارش", Bucket(real, o => o.CreatedAt, _ => 1), both, "/admin/orders", AdminPermissions.OrdersView),
            Make("revenue", "فروش", Bucket(real, o => o.CreatedAt, o => (double)o.PayableAmount), both, "/admin/reports", AdminPermissions.ReportsView),
            Make("cancelled", "لغو سفارش", Bucket(cancelled, c => c, _ => 1), spike, "/admin/orders?status=Cancelled", AdminPermissions.OrdersView),
            Make("returns", "درخواست مرجوعی", Bucket(returns, r => r, _ => 1), spike, "/admin/returns", AdminPermissions.ReturnsManage),
            Make("signups", "ثبت‌نام", Bucket(signups, s => s, _ => 1), both, "/admin/customers", AdminPermissions.CustomersView),
        ];
    }
}
