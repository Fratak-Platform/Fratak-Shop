using System.Collections.Concurrent;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Orders;

/// <summary>
/// دور ۲۴ (پیشنهاد ۵۹): زمان تحویل یادگرفته از تاریخچهٔ واقعی سفارش‌ها (ارسال ← تحویل) به تفکیک استان و روش ارسال.
/// <para>
/// به‌جای عدد ثابت «۳ تا ۴ روز»، از سفارش‌های تحویل‌شدهٔ ۱۸۰ روز اخیر صدک ۲۰ و ۸۰ مدت ارسال تا تحویل حساب می‌شود
/// (یعنی ۶۰٪ وسط سفارش‌ها در این بازه رسیده‌اند). اولویت: همان استان + همان روش (≥۸ نمونه) ← همان روش (≥۱۵) ← همهٔ سفارش‌ها (≥۲۰)
/// ← اگر دادهٔ کافی نیست، همان روزهای تنظیم‌شدهٔ روش ارسال (قانون ثابت، بدون تغییر رفتار).
/// </para>
/// <para>مثال: ۱۲ سفارش پیشتاز به اصفهان با ۲۸ تا ۷۰ ساعت ← بازهٔ «۲ تا ۳ روز» به‌جای «۳ تا ۴ روز» تنظیمات.</para>
/// <para>محل استفاده: <see cref="OrderSlaService.GetForOrderAsync"/> (تاریخ تحویل تخمینی در صفحهٔ سفارش مشتری و پنل).</para>
/// </summary>
public static class DeliveryEta
{
    /// <summary>کمترین نمونه برای استان + روش.</summary>
    public const int MinProvinceSamples = 8;

    /// <summary>کمترین نمونه برای روش ارسال.</summary>
    public const int MinMethodSamples = 15;

    /// <summary>کمترین نمونه برای همهٔ سفارش‌ها.</summary>
    public const int MinAllSamples = 20;

    /// <summary>
    /// بازهٔ روز (کمینه، بیشینه) از مدت‌های ارسال تا تحویل (ساعت). مدت‌های نامعقول (منفی یا بیش از ۶۰ روز) کنار گذاشته می‌شوند.
    /// کمتر از <paramref name="minSamples"/> نمونه ← null. مثال: [24, 30, 48, 50, 70, 72, 90, 96] ← (۲، ۴).
    /// </summary>
    public static (int Min, int Max)? FromHistory(IReadOnlyList<double> hours, int minSamples)
    {
        var valid = hours.Where(h => h > 0 && h <= 24 * 60).OrderBy(h => h).ToList();
        if (valid.Count < Math.Max(1, minSamples))
            return null;
        var low = Quantile(valid, 0.2) / 24;
        var high = Quantile(valid, 0.8) / 24;
        var min = Math.Max(1, (int)Math.Round(low, MidpointRounding.AwayFromZero));
        var max = Math.Max(min, (int)Math.Ceiling(high - 0.15));
        return (min, Math.Min(max, 60));
    }

    /// <summary>صدک با درون‌یابی خطی روی فهرست مرتب. مثال: Quantile([1,2,3,4], 0.5) = ۲٫۵.</summary>
    public static double Quantile(IReadOnlyList<double> sorted, double q)
    {
        if (sorted.Count == 0)
            return 0;
        var position = (sorted.Count - 1) * Math.Clamp(q, 0, 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(sorted.Count - 1, lower + 1);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    /// <summary>نتیجهٔ انتخاب از سه سطح داده (استان+روش، روش، همه). مثال: استان کم‌داده ولی روش پرداده ← بازهٔ روش.</summary>
    public static (int Min, int Max)? Pick(IReadOnlyList<double> provinceAndMethod, IReadOnlyList<double> method, IReadOnlyList<double> all) =>
        FromHistory(provinceAndMethod, MinProvinceSamples) ?? FromHistory(method, MinMethodSamples) ?? FromHistory(all, MinAllSamples);

    private static readonly ConcurrentDictionary<string, ((int Min, int Max)? Value, DateTimeOffset At)> Cache = new(StringComparer.Ordinal);

    /// <summary>عمر نتیجه در حافظه (داده آهسته تغییر می‌کند).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);

    /// <summary>
    /// بازهٔ یادگرفته برای یک سفارش (با حافظهٔ ۶ ساعته به ازای استان و روش). حداکثر ۲۰۰۰ سفارش تحویل‌شدهٔ اخیر خوانده می‌شود.
    /// </summary>
    public static async Task<(int Min, int Max)?> LearnAsync(FratakShopDbContext context, Guid? methodId, string? province, CancellationToken cancellationToken)
    {
        var key = $"{methodId:N}|{province}";
        if (Cache.TryGetValue(key, out var hit) && DateTimeOffset.UtcNow - hit.At < Lifetime)
            return hit.Value;

        var since = DateTimeOffset.UtcNow.AddDays(-180);
        var rows = await context.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Delivered && o.ShippedAt != null && o.DeliveredAt != null && o.DeliveredAt >= since)
            .OrderByDescending(o => o.DeliveredAt)
            .Take(2000)
            .Select(o => new { o.ShippingMethodId, o.ShippingProvince, o.ShippedAt, o.DeliveredAt })
            .ToListAsync(cancellationToken);
        var all = rows.Select(r => new { r.ShippingMethodId, r.ShippingProvince, Hours = (r.DeliveredAt!.Value - r.ShippedAt!.Value).TotalHours }).ToList();
        var sameMethod = all.Where(r => methodId is not null && r.ShippingMethodId == methodId).ToList();
        var sameProvince = sameMethod.Where(r => !string.IsNullOrWhiteSpace(province) && r.ShippingProvince == province).Select(r => r.Hours).ToList();
        var value = Pick(sameProvince, sameMethod.Select(r => r.Hours).ToList(), all.Select(r => r.Hours).ToList());

        if (Cache.Count > 500)
            Cache.Clear();
        Cache[key] = (value, DateTimeOffset.UtcNow);
        return value;
    }
}
