using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// نتیجهٔ رأی دسته: نام دسته (از دروازهٔ کیفیت <see cref="CategoryNames.Proper"/> گذشته)، تعداد سایت‌های موافق و نام آن‌ها.
/// مثال: («هدفون هدست و هندزفری»، 3، [دیجی‌کالا، ترب، باسلام]).
/// </summary>
public sealed record MarketVote(string Name, int Sites, IReadOnlyList<string> Agreeing);

/// <summary>
/// دور ۲۶ – «دسته‌بندی خودکار با رأی رقبا» (خواستهٔ کاربر: ببین دیجی‌کالا، ترب، باسلام و… این کالا را در چه دسته‌ای گذاشته‌اند،
/// اکثریت را بگیر و همان دسته را بساز یا استفاده کن).
/// <para>
/// هر سایت یک «برگ» دسته می‌دهد. نام‌ها در سایت‌ها کمی فرق دارند («هدفون، هدست و هندزفری» در برابر «هدفون و هندزفری بی سیم و بلوتوثی»)،
/// پس دو نام وقتی «هم‌معنی» حساب می‌شوند که دست‌کم نیمی از واژه‌های اصلیِ نام کوتاه‌تر در دیگری هم باشد (واژه‌های کلی مثل «لوازم» و
/// «محصولات» حساب نمی‌شوند). گروهی که دست‌کم ۲ سایت متفاوت در آن موافق‌اند برنده است؛ وگرنه رأی نداریم (null).
/// </para>
/// <para>نام نهایی: پرتکرارترین نامی که از دروازهٔ کیفیت بگذرد (ویرگول حذف می‌شود)؛ در تساوی، کوتاه‌تر.</para>
/// <para>محل استفاده: SmartSources.MarketCategoryAsync (ورود هوشمند کالا) ← ستون Product.MarketCategory ← SmartCategoryService.</para>
/// </summary>
public static class CategoryVote
{
    /// <summary>کمترین تعداد سایت موافق.</summary>
    public const int MinSites = 2;

    // واژه‌های خیلی کلی که به‌تنهایی شباهت نمی‌سازند («لوازم خانگی» با «لوازم جانبی موبایل» یکی نیست).
    private static readonly HashSet<string> Generic = new(StringComparer.Ordinal)
    {
        "لوازم", "محصولات", "محصول", "کالا", "کالاهای", "انواع", "سایر", "متفرقه", "مجموعه", "ست", "جانبی", "ابزار", "وسایل", "تجهیزات",
    };

    // همان واژه‌ها بعد از یکسان‌سازی و ریشه‌یابی (Terms واژه‌ها را ریشه‌یابی می‌کند).
    private static readonly HashSet<string> GenericStems = Generic.Select(g => PersianText.Stem(PersianText.Normalize(g)))
        .Concat(Generic).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// تصمیم از روی رأی سایت‌ها. ورودی: (نام سایت، برگ دسته) – برگ خالی نادیده. یک سایت فقط یک رأی دارد (اولین رأی همان سایت).
    /// مثال: [(دیجی‌کالا، «هدفون، هدست و هندزفری»)، (ترب، «هدفون، هدست و هندزفری»)، (باسلام، «هدفون و هندزفری بی سیم و بلوتوثی»)]
    /// ← MarketVote(«هدفون هدست و هندزفری»، 3، …). [(دیجی‌کالا، «ماساژور»)، (ترب، «کفش ورزشی»)] ← null (اکثریت نیست).
    /// </summary>
    public static MarketVote? Decide(IEnumerable<(string Site, string? Title)> votes, IEnumerable<string>? brands = null)
    {
        var clean = votes
            .Where(v => !string.IsNullOrWhiteSpace(v.Site) && !string.IsNullOrWhiteSpace(v.Title))
            .DistinctBy(v => v.Site, StringComparer.Ordinal)
            .Select(v => (v.Site, Title: v.Title!.Trim(), Terms: KeyTerms(v.Title)))
            .Where(v => v.Terms.Count > 0)
            .ToList();
        if (clean.Count < MinSites)
            return null;

        var brandList = brands?.ToList();
        MarketVote? best = null;
        foreach (var seed in clean)
        {
            var group = clean.Where(v => Similar(seed.Terms, v.Terms)).ToList();
            var sites = group.Select(g => g.Site).Distinct(StringComparer.Ordinal).ToList();
            if (sites.Count < MinSites || best is not null && sites.Count <= best.Sites)
                continue;
            var name = group
                .Select(g => CategoryNames.Proper(g.Title.Replace('،', ' ').Replace(',', ' '), brandList))
                .OfType<string>()
                .GroupBy(n => PersianText.Normalize(n), StringComparer.Ordinal)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Length)
                .Select(g => CategoryNames.FixSpacing(g.First()))
                .FirstOrDefault();
            if (name is not null)
                best = new MarketVote(name, sites.Count, sites);
        }
        return best;
    }

    /// <summary>
    /// دو نام دسته هم‌معنی‌اند؟ دست‌کم نیمی از واژه‌های اصلی نام کوتاه‌تر در دیگری باشد.
    /// مثال: {هدفون، هدست، هندزفری} و {هدفون، هندزفری، بی، سیم، بلوتوث} ← ۲ از ۳ ← بله؛ {خانگی} و {موبایل} ← خیر.
    /// </summary>
    public static bool Similar(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
            return false;
        var shared = a.Count(b.Contains);
        return shared > 0 && shared * 2 >= Math.Min(a.Count, b.Count);
    }

    /// <summary>واژه‌های اصلی یک نام دسته (ریشه‌یابی‌شده، بدون واژه‌های ربط و کلی). مثال: «لوازم خانگی برقی» ← {خانگی، برقی}.</summary>
    public static HashSet<string> KeyTerms(string? title) =>
        CategoryClassifier.Terms((title ?? "").Replace('،', ' ').Replace(',', ' '))
            .Where(t => !GenericStems.Contains(t))
            .ToHashSet(StringComparer.Ordinal);
}
