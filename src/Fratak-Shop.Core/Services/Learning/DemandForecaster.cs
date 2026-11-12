namespace Fratak_Shop.Core.Services.Learning;

/// <summary>نتیجهٔ پیش‌بینی تقاضا: فروش روزانهٔ پیش‌بینی‌شده، روش انتخاب‌شده و خطای آزمون عقب‌گرد (MAE).</summary>
public sealed record DemandForecast(decimal DailyRate, string Method, decimal Error);

/// <summary>
/// دور ۲۴ (پیشنهاد ۵۱): پیش‌بینی فروش هر کالا با یادگیری ماشین سبک و کاملاً داخلی (C# خالص، بدون سرویس بیرونی).
/// <list type="bullet">
/// <item><b>Holt میرا + فصل هفتگی:</b> سطح و روند فروش روزانه را با هموارسازی نمایی یاد می‌گیرد؛ اگر دست‌کم ۴ هفته داده باشد،
/// ضریب هر روز هفته (مثلاً جمعه‌ها کم‌فروش) هم یاد گرفته می‌شود. روند «میرا» است تا یک جهش کوتاه پیش‌بینی را منفجر نکند.</item>
/// <item><b>Croston/SBA:</b> برای کالاهای کم‌فروش که بیشتر روزها صفر می‌فروشند (تقاضای پراکنده) – اندازهٔ هر فروش و فاصلهٔ بین فروش‌ها جدا یاد گرفته می‌شود.</item>
/// <item><b>انتخاب خودکار مدل:</b> هر دو روش (و میانگین ساده) روی ۱۴ روز آخر «آزمون عقب‌گرد» می‌شوند و روشی که خطای کمتری دارد انتخاب می‌شود.</item>
/// </list>
/// مثال: فروش ۸ هفتهٔ اخیر ۲ تا ۳ عدد در روز با روند صعودی ← حدود ۳٫۲ در روز (روش Holt)؛ کالایی که هر ۱۰ روز یک بار ۲ عدد می‌فروشد ← حدود ۰٫۲ (Croston).
/// <para>محل استفاده: Core/Services/Products/InventoryService (پیش‌بینی موجودی، پیشنهاد خرید و پیش‌نویس سفارش خرید).</para>
/// </summary>
public static class DemandForecaster
{
    /// <summary>افق پیش‌بینی پیش‌فرض (روز).</summary>
    public const int Horizon = 14;

    private const decimal Alpha = 0.3m;
    private const decimal Beta = 0.1m;
    private const decimal Gamma = 0.2m;
    private const decimal Phi = 0.9m;

    /// <summary>
    /// پیش‌بینی میانگین فروش روزانهٔ <paramref name="horizon"/> روز آینده. <paramref name="daily"/> = فروش روزانه از قدیمی به جدید.
    /// سری خالی یا تمام‌صفر ← ۰. خروجی هرگز منفی نیست.
    /// </summary>
    public static DemandForecast Forecast(IReadOnlyList<int> daily, int horizon = Horizon)
    {
        horizon = Math.Clamp(horizon, 1, 90);
        if (daily.Count == 0 || daily.All(d => d <= 0))
            return new DemandForecast(0, "none", 0);
        var series = daily.Select(d => (decimal)Math.Max(0, d)).ToArray();
        var mean = series.Average();
        if (series.Length < 14)
            return new DemandForecast(Round(mean), "mean", 0);

        // آزمون عقب‌گرد: آموزش تا ۱۴ روز آخر، سنجش روی همان ۱۴ روز.
        var test = Math.Min(14, series.Length / 3);
        var train = series[..^test];
        var actual = series[^test..];
        var candidates = new List<(string Name, Func<decimal[], int, decimal[]> Model)>
        {
            ("mean", (s, h) => Enumerable.Repeat(s.Average(), h).ToArray()),
        };
        // تقاضای پراکنده (نیمی از روزها صفر): Holt مناسب نیست (یک فروش تصادفی روز آخر پیش‌بینی را بالا می‌برد) ← Croston.
        var zeros = series.Count(v => v == 0) / (decimal)series.Length;
        if (zeros >= 0.5m)
            candidates.Add(("croston", Croston));
        else
            candidates.Add(("holt", Holt));

        var best = candidates
            .Select(c => (c.Name, c.Model, Error: Mae(c.Model(train, test), actual)))
            .OrderBy(c => c.Error)
            .ThenBy(c => c.Name == "mean" ? 1 : 0)
            .First();
        var forecast = best.Model(series, horizon);
        return new DemandForecast(Round(Math.Max(0, forecast.Average())), best.Name, Round(best.Error));
    }

    /// <summary>
    /// Holt میرا با فصل هفتگی ضربی (اگر ≥ ۲۸ روز). مثال: [۱،۲،۳،…] ← ادامهٔ روند با شیب کم‌شونده.
    /// </summary>
    public static decimal[] Holt(decimal[] series, int horizon)
    {
        var seasonal = Enumerable.Repeat(1m, 7).ToArray();
        var useSeason = series.Length >= 28 && series.Sum() >= 14;
        if (useSeason)
        {
            var avg = series.Average();
            if (avg > 0)
            {
                for (var d = 0; d < 7; d++)
                {
                    var values = series.Where((_, i) => i % 7 == d).ToArray();
                    seasonal[d] = values.Length == 0 ? 1m : Math.Clamp(values.Average() / avg, 0.2m, 3m);
                }
            }
        }

        decimal Deseason(int i) => series[i] / seasonal[i % 7];
        var level = Deseason(0);
        var trend = series.Length > 7 ? (Enumerable.Range(7, Math.Min(7, series.Length - 7)).Average(i => Deseason(i)) - Enumerable.Range(0, 7).Average(i => Deseason(i))) / 7m : 0m;
        for (var i = 1; i < series.Length; i++)
        {
            var value = Deseason(i);
            var previousLevel = level;
            level = Alpha * value + (1 - Alpha) * (level + Phi * trend);
            trend = Beta * (level - previousLevel) + (1 - Beta) * Phi * trend;
            if (useSeason && level > 0)
            {
                var index = i % 7;
                seasonal[index] = Math.Clamp(Gamma * (series[i] / Math.Max(level, 0.01m)) + (1 - Gamma) * seasonal[index], 0.2m, 3m);
            }
        }

        var result = new decimal[horizon];
        var damp = 0m;
        var power = 1m;
        for (var h = 1; h <= horizon; h++)
        {
            power *= Phi;
            damp += power;
            var index = (series.Length + h - 1) % 7;
            result[h - 1] = Math.Max(0, (level + damp * trend) * seasonal[index]);
        }
        return result;
    }

    /// <summary>
    /// Croston با تصحیح SBA برای تقاضای پراکنده: اندازهٔ فروش ÷ فاصلهٔ فروش‌ها × (۱ − α/۲).
    /// مثال: هر ۵ روز ۲ عدد ← ۰٫۴ × ۰٫۸۵ ≈ ۰٫۳۴ در روز.
    /// </summary>
    public static decimal[] Croston(decimal[] series, int horizon)
    {
        const decimal a = 0.15m;
        decimal? size = null;
        decimal? interval = null;
        var gap = 1;
        foreach (var value in series)
        {
            if (value > 0)
            {
                size = size is null ? value : a * value + (1 - a) * size.Value;
                interval = interval is null ? gap : a * gap + (1 - a) * interval.Value;
                gap = 1;
            }
            else
            {
                gap++;
            }
        }
        var rate = size is null || interval is null || interval.Value == 0 ? 0m : size.Value / interval.Value * (1 - a / 2);
        return Enumerable.Repeat(rate, horizon).ToArray();
    }

    private static decimal Mae(decimal[] predicted, decimal[] actual)
    {
        var n = Math.Min(predicted.Length, actual.Length);
        if (n == 0)
            return 0;
        decimal sum = 0;
        for (var i = 0; i < n; i++)
            sum += Math.Abs(predicted[i] - actual[i]);
        return sum / n;
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2);
}
