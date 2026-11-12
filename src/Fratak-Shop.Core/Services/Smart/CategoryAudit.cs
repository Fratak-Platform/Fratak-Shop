namespace Fratak_Shop.Core.Services.Smart;

/// <summary>دور ۲۹ (#۱۰): یک جابه‌جایی «اصلاح دسته». From/To = نام دستهٔ قبلی و تازه (برای اعلان کارکنان).</summary>
public sealed record CategoryFix(Guid ProductId, string ProductName, string From, string To);

/// <summary>
/// دور ۲۹ (#۱۰): گزارش «بازبینی دستهٔ کالاها». Suspect = کالاهایی که به دستهٔ فعلی نمی‌خورند ولی دستهٔ مطمئنی برایشان پیدا نشد
/// (برای بازبینی کارشناس؛ دست نمی‌خورند).
/// </summary>
public sealed record CategoryAuditReport(int Examined, int Suspect, IReadOnlyList<CategoryFix> Moved, IReadOnlyList<string> SuspectNames)
{
    /// <summary>گزارش خالی.</summary>
    public static readonly CategoryAuditReport Empty = new(0, 0, [], []);

    /// <summary>متن کوتاه اعلان. مثال: «۳ کالا به دستهٔ درست رفت: سرویس ۶ پارچه ← ظروف آشپزخانه؛ …».</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Moved.Count > 0)
                parts.Add($"{Seo.Engine.PersianText.Digits(Moved.Count)} کالا به دستهٔ درست رفت: "
                          + string.Join("؛ ", Moved.Take(5).Select(m => $"«{m.ProductName}» از «{m.From}» به «{m.To}»")));
            if (Suspect > 0)
                parts.Add($"{Seo.Engine.PersianText.Digits(Suspect)} کالا به دستهٔ فعلی نمی‌خورد ولی دستهٔ مطمئنی پیدا نشد"
                          + (SuspectNames.Count > 0 ? " (" + string.Join("، ", SuspectNames.Take(3)) + ")" : "") + "؛ لطفاً بررسی کنید");
            return string.Join(". ", parts) + (parts.Count > 0 ? "." : "");
        }
    }
}

/// <summary>
/// دور ۲۹ (#۱۰) – تشخیص «کالا در دستهٔ اشتباه» (خالص، بدون پایگاه داده؛ آزمون‌پذیر). مثال کاربر: «ظرف شیش‌پارچه» در «چهارراهی برق».
/// <list type="number">
/// <item>نوع کالا از نامش شناخته شد (<see cref="ProductTypes.Detect"/>) و دستهٔ فعلی نوع دیگری است ← اشتباه
/// (مثلاً «کابل شارژ» در «پاوربانک»). دستهٔ کلی (مثل «لوازم جانبی») اشتباه حساب نمی‌شود.</item>
/// <item>نوع کالا نامعلوم: هیچ واژهٔ معناداری با نام دسته و نام کالاهای دیگر همان دسته مشترک ندارد (و دسته دست‌کم ۲ کالای دیگر دارد) ← اشتباه.</item>
/// </list>
/// محتاط است: شک ← «درست» (هیچ جابه‌جایی بی‌دلیل انجام نمی‌شود).
/// </summary>
public static class CategoryAudit
{
    /// <summary>
    /// واژه‌های نام برندها برای پارامتر ignore. مثال: [«Xiaomi»، «شیائومی»] ← {xiaomi، شیائومی}.
    /// </summary>
    public static HashSet<string> BrandTerms(IEnumerable<string> brands) =>
        brands.SelectMany(b => CategoryClassifier.Terms(b)).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// آیا کالا به این دسته نمی‌خورد؟
    /// مثال: («ظرف شیش پارچه پیرکس»، «چندراهی برق»، [«چندراهی برق ۶ خانه»، «سه راهی برق کابل‌دار»]) ← true؛
    /// («چندراهی برق ارت‌دار»، همان دسته) ← false؛ («کابل شارژ تایپ سی»، «لوازم جانبی موبایل») ← false.
    /// </summary>
    /// <param name="productName">نام کالا.</param>
    /// <param name="categoryName">نام دستهٔ فعلی.</param>
    /// <param name="categorySlug">نامک دستهٔ فعلی.</param>
    /// <param name="siblingNames">نام کالاهای دیگر همان دسته (بدون خود کالا).</param>
    /// <param name="ignore">واژه‌هایی که نشانهٔ دسته نیستند (نام برندها: «شیائومی» هم گوشی دارد هم پاوربانک). null = هیچ.</param>
    public static bool IsMisplaced(string? productName, string? categoryName, string? categorySlug, IReadOnlyCollection<string> siblingNames,
        IReadOnlySet<string>? ignore = null)
    {
        var terms = CategoryClassifier.Terms(productName).Where(t => ignore is null || !ignore.Contains(t)).ToList();
        if (terms.Count == 0)
            return false;
        var productType = ProductTypes.Detect(productName);
        var categoryType = ProductTypes.Find(categorySlug) ?? ProductTypes.DetectHead(categoryName);
        if (productType is not null)
        {
            if (ProductTypes.NameMatches(productType, categoryName, categorySlug))
                return false;
            // دستهٔ «نوع‌دار» دیگر ← قطعاً اشتباه؛ دستهٔ کلی ← نامعلوم (شاید والد درست باشد).
            return categoryType is not null && categoryType.Key != productType.Key;
        }
        if (siblingNames.Count < 2 && categoryType is null)
            return false;
        var known = CategoryClassifier.Terms(categoryName).ToHashSet(StringComparer.Ordinal);
        foreach (var name in siblingNames.Take(300))
        {
            foreach (var term in CategoryClassifier.Terms(name))
                known.Add(term);
        }
        return !terms.Any(known.Contains);
    }
}
