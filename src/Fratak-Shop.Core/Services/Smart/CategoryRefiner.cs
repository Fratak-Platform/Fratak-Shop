using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// دور ۳۱ (#۱) – «دستهٔ تأمین‌کننده کلی است؟ خودت دقیق‌ترش کن».
/// <para>
/// قانون پروژه: اگر دسته‌ای که تأمین‌کننده (یا سایت مبدأ) گذاشته کلی باشد (مثلاً فقط «لوازم آشپزخانه»)، سیستم خودش از روی نام کالا
/// دستهٔ دقیق‌تر را پیدا می‌کند (مثلاً «کتری برقی») و آن را زیر همان دستهٔ کلی می‌گذارد. همه‌چیز خالص و بدون شبکه است.
/// </para>
/// <para>محل استفاده: <see cref="SmartCategoryService"/> (مرحلهٔ «مسیر دستهٔ سایت دیگر/تأمین‌کننده»).</para>
/// </summary>
public static class CategoryRefiner
{
    /// <summary>پیشوندهایی که نشان می‌دهند دسته «گروهی از چند نوع کالا» است، نه یک نوع مشخص.</summary>
    private static readonly string[] CoarsePrefixes =
    [
        "لوازم", "محصولات", "کالای", "کالاهای", "تجهیزات", "اقلام", "ملزومات", "وسایل", "ابزار و", "سایر", "انواع", "مجموعه",
    ];

    /// <summary>
    /// واژه‌هایی که بعد از «نوع کالا» در نام می‌آیند و جزو دسته نیستند (مدل، رنگ، سایز، ظرفیت…). نام کالا از اولین همین واژه بریده می‌شود.
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "مدل", "برند", "طرح", "رنگ", "سایز", "کد", "ظرفیت", "حجم", "بسته", "سری", "نسخه", "همراه", "با", "به", "برای", "مناسب", "مخصوص",
        "اصل", "اورجینال", "اصلی", "جدید", "عددی", "تایی", "گرمی", "لیتری", "وات", "اینچ", "میلی", "سانتی", "متری", "کیلویی", "از", "در",
        "و", "یا", "بدون", "ساخت", "شرکت", "مارک", "تولید", "پک", "عدد", "جفت", "دست",
    };

    /// <summary>
    /// آیا این مسیر دسته «کلی» است؟ کلی یعنی: فقط یک بخش دارد، یا برگش با «لوازم/محصولات/تجهیزات…» شروع می‌شود، یا دو چیز را با «و» کنار
    /// هم گذاشته («کتری و قوری»)، یا نام یکی از خانواده‌های بزرگ فروشگاه است («لوازم خانگی»). مسیر خالی ← خیر (چیزی برای دقیق‌کردن نیست).
    /// مثال: [«لوازم آشپزخانه»] ← بله؛ [«لوازم خانگی»، «کتری برقی»] ← خیر.
    /// </summary>
    public static bool IsCoarse(IReadOnlyList<string> path)
    {
        if (path.Count == 0)
            return false;
        var leaf = PersianText.Normalize(path[^1]);
        if (leaf.Length == 0)
            return false;
        if (path.Count == 1)
            return true;
        if (CoarsePrefixes.Any(p => leaf.StartsWith(PersianText.Normalize(p) + " ", StringComparison.Ordinal) || leaf == PersianText.Normalize(p)))
            return true;
        if (leaf.Contains(" و ", StringComparison.Ordinal))
            return true;
        return CategoryFamilies.Match(path[^1]) is not null || CategoryClassifier.IsCatchAll(path[^1]);
    }

    /// <summary>
    /// نام دستهٔ دقیق‌تر از روی نام کالا: «نوع کالا» = واژه‌های اول نام تا پیش از برند، مدل، عدد، حروف لاتین یا واژه‌های توصیفی
    /// (حداکثر ۲ واژه؛ اگر دو واژه نام درستی نشد، ۱ واژه). نام باید از دروازهٔ کیفیت <see cref="CategoryNames.Proper"/> بگذرد و با دستهٔ
    /// کلی یکی نباشد. پیدا نشد ← null (کالا همان دستهٔ کلی را می‌گیرد).
    /// <para>مثال: («کتری برقی پارس خزر مدل KT-2000»، «لوازم آشپزخانه»، [پارس خزر]) ← «کتری برقی».</para>
    /// <para>مثال: («ماگ سرامیکی طرح گل»، «ظروف») ← «ماگ سرامیکی»؛ («Xiaomi Mi Band 8»، …) ← null.</para>
    /// </summary>
    /// <param name="productName">نام کالا.</param>
    /// <param name="coarse">نام دستهٔ کلی (برای اینکه همان دوباره پیشنهاد نشود).</param>
    /// <param name="brands">نام برندهای فروشگاه (فارسی و انگلیسی) تا برند جزو نام دسته نشود.</param>
    public static string? FinerName(string? productName, string? coarse, IEnumerable<string>? brands = null)
    {
        var clean = PersianText.Clean(productName ?? "");
        if (clean.Length < 3)
            return null;
        var brandList = (brands ?? []).Where(b => !string.IsNullOrWhiteSpace(b)).ToList();
        var brandWords = brandList.SelectMany(b => PersianText.Normalize(b).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(w => w.Length >= 3).ToHashSet(StringComparer.Ordinal);

        var head = new List<string>();
        foreach (var raw in clean.Split([' ', '-', '،', ',', '(', ')', '/', '|', '+'], StringSplitOptions.RemoveEmptyEntries))
        {
            var word = raw.Trim('.', ':', '«', '»', '"', '\'');
            if (word.Length == 0)
                continue;
            var normal = PersianText.Normalize(word);
            // اولین واژهٔ «غیرنوع» (عدد، لاتین، برند، واژهٔ توصیفی) ← پایان نوع کالا.
            if (word.Any(char.IsDigit) || word.Any(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z') || StopWords.Contains(normal)
                || brandWords.Contains(normal) || !word.All(c => PersianText.IsPersianLetter(c) || c == '\u200c'))
                break;
            head.Add(word);
            if (head.Count == 2)
                break;
        }

        for (var take = head.Count; take >= 1; take--)
        {
            var name = CategoryNames.Proper(string.Join(' ', head.Take(take)), brandList);
            if (name is null)
                continue;
            // یک واژهٔ خیلی کوتاه («ست»، «پک») به‌تنهایی دسته نیست.
            if (take == 1 && PersianText.Normalize(name).Length < 3)
                continue;
            if (!string.IsNullOrWhiteSpace(coarse) && CategoryNames.Same(name, coarse))
                return null;
            return name;
        }
        return null;
    }
}
