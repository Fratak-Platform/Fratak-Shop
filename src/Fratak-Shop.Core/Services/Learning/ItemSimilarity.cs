namespace Fratak_Shop.Core.Services.Learning;

/// <summary>
/// دور ۲۴ (پیشنهاد ۵۲): پیشنهاد «معمولاً با هم خریده می‌شوند» با فیلترینگ مشارکتی آیتم‌به‌آیتم (Item-based Collaborative Filtering).
/// به‌جای «پرتکرارترین کالای سفارش‌های مشترک» (که همیشه پرفروش‌ترین کالای سایت را می‌آورد)، شباهت کسینوسی با جریمهٔ داده‌ی کم حساب می‌شود:
/// <c>امتیاز = هم‌خرید ÷ (√(سفارش‌های کالای پایه × سفارش‌های کالای نامزد) + k)</c>.
/// <para>مثال: هدفون در ۱۰۰ سفارش؛ کیف هدفون با آن ۸ بار و خودش ۱۰ بار ← ۸÷(√۱۰۰۰+۲)=۰٫۲۴؛ شارژر پرفروش ۱۲ بار و خودش ۶۰۰ بار ← ۱۲÷(√۶۰۰۰۰+۲)=۰٫۰۵
/// ← کیف هدفون بالاتر می‌آید (ربط واقعی، نه فقط پرفروشی).</para>
/// <para>محل استفاده: Core/Services/Products/RecommendationService (صفحهٔ کالا و سبد خرید).</para>
/// </summary>
public static class ItemSimilarity
{
    /// <summary>ضریب جریمهٔ دادهٔ کم (هرچه بیشتر، کالاهای کم‌داده پایین‌تر).</summary>
    public const double Shrink = 2;

    /// <summary>
    /// مرتب‌سازی نامزدها. <paramref name="coCounts"/>: تعداد سفارش مشترک با کالای پایه؛ <paramref name="totals"/>: کل سفارش‌های هر نامزد؛
    /// <paramref name="baseOrders"/>: سفارش‌های کالای پایه. نامزد بدون آمار کل ← کل = هم‌خرید.
    /// </summary>
    public static List<Guid> Rank(IReadOnlyDictionary<Guid, int> coCounts, IReadOnlyDictionary<Guid, int> totals, int baseOrders, int take)
    {
        baseOrders = Math.Max(1, baseOrders);
        return coCounts
            .Where(c => c.Value > 0)
            .Select(c => (Id: c.Key, Co: c.Value, Score: Score(c.Value, Math.Max(c.Value, totals.GetValueOrDefault(c.Key, c.Value)), baseOrders)))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Co)
            .ThenBy(x => x.Id)
            .Take(Math.Max(0, take))
            .Select(x => x.Id)
            .ToList();
    }

    /// <summary>شباهت کسینوسی با جریمه. مثال: (۸، ۱۰، ۱۰۰) ← ≈۰٫۲۴.</summary>
    public static double Score(int co, int candidateOrders, int baseOrders) =>
        co <= 0 ? 0 : co / (Math.Sqrt((double)Math.Max(1, candidateOrders) * Math.Max(1, baseOrders)) + Shrink);
}
