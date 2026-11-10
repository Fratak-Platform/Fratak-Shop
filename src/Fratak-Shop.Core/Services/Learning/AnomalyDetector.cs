namespace Fratak_Shop.Core.Services.Learning;

/// <summary>جهت ناهنجاری.</summary>
public enum AnomalyDirection
{
    None = 0,
    Spike = 1,
    Drop = 2,
}

/// <summary>نتیجهٔ بررسی یک روز: مقدار، مقدار مورد انتظار، امتیاز z و جهت.</summary>
public sealed record AnomalyResult(double Value, double Expected, double ZScore, AnomalyDirection Direction)
{
    /// <summary>ناهنجار است؟</summary>
    public bool IsAnomaly => Direction != AnomalyDirection.None;
}

/// <summary>
/// دور ۲۴ (پیشنهاد ۵۶): تشخیص ناهنجاری سری روزانه با میانگین و واریانس متحرک نمایی (EWMA) و امتیاز z.
/// <para>
/// برای هر روز تاریخچه، «انتظار» با وزن بیشتر به روزهای اخیر به‌روز می‌شود؛ اگر روز بررسی‌شده بیش از <see cref="Threshold"/> انحراف معیار
/// از انتظار فاصله داشته باشد، ناهنجار است. اثر روزهای هفته (جمعه‌های کم‌فروش) با ضریب هر روز هفته اصلاح می‌شود تا جمعه همیشه «افت» نباشد.
/// کف انحراف معیار (۱۵٪ میانگین و حداقل ۱) جلوی هشدار بی‌مورد سری‌های خیلی آرام را می‌گیرد.
/// </para>
/// <para>مثال: ۳۰ روز حدود ۲۰ سفارش در روز و دیروز ۲ سفارش ← افت با z≈−۴ (هشدار «افت غیرعادی سفارش‌ها»: شاید درگاه خراب است).</para>
/// <para>محل استفاده: Insights/AnomalyService (بررسی روزانه و اعلان به کارکنان).</para>
/// </summary>
public static class AnomalyDetector
{
    /// <summary>آستانهٔ z.</summary>
    public const double Threshold = 3.0;

    /// <summary>کمترین روز تاریخچه.</summary>
    public const int MinHistory = 14;

    /// <summary>ضریب هموارسازی EWMA.</summary>
    public const double Alpha = 0.25;

    /// <summary>
    /// بررسی <paramref name="value"/> در برابر تاریخچه (قدیمی ← جدید). <paramref name="weekdays"/> (اختیاری، هم‌طول تاریخچه + ۱ برای روز بررسی)
    /// روز هفتهٔ هر نقطه است (۰ تا ۶). <paramref name="minMean"/>: اگر میانگین کمتر باشد، داده برای قضاوت کم است ← None.
    /// </summary>
    public static AnomalyResult Check(IReadOnlyList<double> history, double value, IReadOnlyList<int>? weekdays = null, double minMean = 3)
    {
        if (history.Count < MinHistory)
            return new AnomalyResult(value, value, 0, AnomalyDirection.None);

        var overall = history.Average();
        if (overall < minMean)
            return new AnomalyResult(value, overall, 0, AnomalyDirection.None);

        // ضریب روز هفته = میانگین آن روز ÷ میانگین کل (حداقل ۲ نمونه؛ محدود به ۰٫۵ تا ۱٫۵ تا داده کم اغراق نکند).
        var factors = Enumerable.Repeat(1.0, 7).ToArray();
        if (weekdays is not null && weekdays.Count == history.Count + 1 && overall > 0)
        {
            for (var day = 0; day < 7; day++)
            {
                var points = history.Where((_, i) => weekdays[i] == day).ToList();
                if (points.Count >= 2)
                    factors[day] = Math.Clamp(points.Average() / overall, 0.5, 1.5);
            }
        }
        double Factor(int index) => weekdays is not null && weekdays.Count == history.Count + 1 ? factors[Math.Clamp(weekdays[index], 0, 6)] : 1;

        var mean = history[0] / Factor(0);
        var variance = 0.0;
        for (var i = 1; i < history.Count; i++)
        {
            var x = history[i] / Factor(i);
            var diff = x - mean;
            mean += Alpha * diff;
            variance = (1 - Alpha) * (variance + Alpha * diff * diff);
        }

        var factor = Factor(history.Count);
        var expected = mean * factor;
        var deviation = Math.Max(Math.Sqrt(variance), Math.Max(1, 0.15 * mean)) * factor;
        var z = (value - expected) / deviation;
        var direction = z >= Threshold ? AnomalyDirection.Spike : z <= -Threshold ? AnomalyDirection.Drop : AnomalyDirection.None;
        return new AnomalyResult(value, Math.Round(expected, 2), Math.Round(z, 2), direction);
    }
}
