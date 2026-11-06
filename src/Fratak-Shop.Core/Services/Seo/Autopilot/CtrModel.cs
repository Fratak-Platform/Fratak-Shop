using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>آمار جست‌وجوی یک دوره برای یک صفحه (از سرچ کنسول یا فایل CSV آن).</summary>
public sealed record CtrSample(int Impressions, int Clicks, double Position)
{
    /// <summary>نرخ کلیک (۰ تا ۱).</summary>
    public double Ctr => Impressions <= 0 ? 0 : (double)Clicks / Impressions;
}

/// <summary>
/// مدل نرخ کلیک (موارد ۱۵۲، ۱۵۵، ۱۵۸ و ۲۰۸ پیشنهاد سئو): تصمیم «کدام صفحه بازنویسی شود» و «آزمایش عنوان برد یا باخت»
/// فقط بر اساس دادهٔ واقعی گرفته می‌شود، نه حدس.
/// <para>منحنی CTR بر اساس میانگین مطالعات عمومی (Advanced Web Ranking/Backlinko) است؛ چون CTR به جایگاه وابسته است،
/// همیشه «CTR ÷ CTR مورد انتظار همان جایگاه» مقایسه می‌شود تا تغییر رتبه نتیجه را گمراه نکند.</para>
/// <para>کجا استفاده می‌شود؟ کار شبانهٔ <c>SeoAutopilotService</c> و گزارش‌ها.</para>
/// </summary>
public static class CtrModel
{
    private static readonly double[] Curve = [0.28, 0.155, 0.105, 0.075, 0.057, 0.045, 0.037, 0.031, 0.027, 0.024];

    /// <summary>کمترین نمایش برای هر تصمیم (کمتر از این، داده نویز است).</summary>
    public const int MinImpressions = 150;

    /// <summary>
    /// نرخ کلیک مورد انتظار یک جایگاه. مثال: <c>Expected(1) ≈ 0.28</c>، <c>Expected(15) ≈ 0.01</c>.
    /// جایگاه اعشاری بین دو جایگاه درون‌یابی می‌شود.
    /// </summary>
    public static double Expected(double position)
    {
        if (double.IsNaN(position) || position < 1)
            position = 1;
        if (position >= 30)
            return 0.004;
        if (position > 10)
            return Math.Max(0.004, 0.024 - (position - 10) * 0.001);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(10, lower + 1);
        var fraction = position - lower;
        return Curve[lower - 1] + (Curve[upper - 1] - Curve[lower - 1]) * fraction;
    }

    /// <summary>
    /// عملکرد نسبی: CTR واقعی ÷ CTR مورد انتظار. ۱ = معمولی، زیر ۰٫۶ = ضعیف، بالای ۱٫۳ = خیلی خوب.
    /// </summary>
    public static double Performance(CtrSample sample) => sample.Impressions <= 0 ? 1 : sample.Ctr / Expected(sample.Position);

    /// <summary>
    /// صفحه نرخ کلیک ضعیف دارد و ارزش بازنویسی عنوان/توضیحات دارد؟
    /// شرط: نمایش کافی، جایگاه صفحهٔ اول یا دوم (بالاتر از ۲۰ کلیک معنا ندارد) و عملکرد زیر ۶۰٪.
    /// </summary>
    public static bool IsUnderperforming(CtrSample sample) =>
        sample.Impressions >= MinImpressions && sample.Position <= 20 && Performance(sample) < 0.6;

    /// <summary>
    /// «نزدیک صفحهٔ اول»: جایگاه ۸ تا ۲۰ با نمایش قابل‌توجه — با لینک داخلی بیشتر و محتوای کامل‌تر سریع بالا می‌آید (مورد ۱۵۵).
    /// </summary>
    public static bool IsNearPageOne(CtrSample sample) => sample.Impressions >= 50 && sample.Position is >= 8 and <= 20;

    /// <summary>
    /// داوری آزمایش عنوان: عملکرد نسبی بعد در برابر قبل، با آزمون آماری دو نسبت (z) روی کلیک‌ها.
    /// برد: حداقل ۱۰٪ بهتر و z ≥ ۱٫۶۴ (اطمینان ۹۵٪ یک‌طرفه). باخت: ۱۰٪ بدتر و z ≤ −۱٫۶۴. بقیه: بی‌نتیجه.
    /// داده کم (کمتر از MinImpressions در هر دوره) ← بی‌نتیجه (عنوان تازه می‌ماند تا داده جمع شود).
    /// </summary>
    public static SeoTitleTestStatus Judge(CtrSample before, CtrSample after)
    {
        if (before.Impressions < MinImpressions || after.Impressions < MinImpressions)
            return SeoTitleTestStatus.Inconclusive;

        // CTR دورهٔ بعد به جایگاه دورهٔ قبل «هم‌تراز» می‌شود.
        var adjustedAfter = after.Ctr * Expected(before.Position) / Expected(after.Position);
        var ratio = before.Ctr <= 0 ? (adjustedAfter > 0 ? 2 : 1) : adjustedAfter / before.Ctr;
        var z = ZScore(before.Ctr, before.Impressions, Math.Clamp(adjustedAfter, 0, 1), after.Impressions);
        if (ratio >= 1.1 && z >= 1.64)
            return SeoTitleTestStatus.Won;
        if (ratio <= 0.9 && z <= -1.64)
            return SeoTitleTestStatus.Lost;
        return SeoTitleTestStatus.Inconclusive;
    }

    /// <summary>آمارهٔ z آزمون دو نسبت (p2 − p1). مقدار مثبت = دورهٔ دوم بهتر.</summary>
    public static double ZScore(double p1, int n1, double p2, int n2)
    {
        if (n1 <= 0 || n2 <= 0)
            return 0;
        var pooled = (p1 * n1 + p2 * n2) / (n1 + n2);
        var se = Math.Sqrt(pooled * (1 - pooled) * (1.0 / n1 + 1.0 / n2));
        return se <= 0 ? 0 : (p2 - p1) / se;
    }

    /// <summary>
    /// تشخیص افت ناگهانی (مورد ۱۶۳): میانگین ۳ روز اخیر در برابر ۲۸ روز قبل از آن.
    /// افت بیش از ۴۰٪ با حداقل ۳۰ نمایش روزانه در دورهٔ مرجع ← true. ورودی: سری روزانه (قدیمی به جدید).
    /// </summary>
    public static bool IsSuddenDrop(IReadOnlyList<int> daily)
    {
        if (daily.Count < 14)
            return false;
        var recent = daily.TakeLast(3).Average();
        var reference = daily.SkipLast(3).TakeLast(28).Average();
        return reference >= 30 && recent < reference * 0.6;
    }
}
