namespace Fratak_Shop.Core.Services.Smart;

/// <summary>یک عکس کالا برای بررسی تکراری بودن (فقط داده‌های لازم).</summary>
/// <param name="Hash">dHash ذخیره‌شده؛ null یا ۰ = نامعلوم (هرگز تکراری حساب نمی‌شود).</param>
/// <param name="Flags">SubjectFlags عکس (دور ۳۰): عکس «بدون زمینه» (<see cref="ImageCleanup.CutoutDone"/>) نسخهٔ عمدی عکس دیگری است.</param>
public sealed record ImageFingerprint(Guid Id, Guid ProductId, bool IsPrimary, int DisplayOrder, long? Hash, byte? Flags = null);

/// <summary>
/// دور ۲۳ (مورد ۱۲): عکس‌های تکراری «یک کالا» با اثر انگشت dHash (<see cref="ImageHash"/>).
/// <para>قانون: در هر کالا عکس اصلی و بعد به ترتیب نمایش نگه داشته می‌شوند؛ هر عکسی که با یکی از نگه‌داشته‌ها «همان عکس» باشد
/// (فاصلهٔ همینگ ≤ ۶، یعنی همان تصویر با اندازه یا فشرده‌سازی دیگر) اضافه است. عکس اصلی هرگز اضافه حساب نمی‌شود.
/// عکس‌های دو کالای مختلف هرگز با هم مقایسه نمی‌شوند (ممکن است دو رنگ یک مدل عکس مشترک داشته باشند).</para>
/// <para>مثال: کالا با [اصلی A، B، 'A کوچک‌شده'] ← فقط 'A کوچک‌شده' اضافه است.</para>
/// <para>دور ۳۰ (#۴): عکس «بدون زمینه» و عکس زمینه‌دارِ اصلی آن عمداً دو نسخهٔ یک تصویرند (کاربر می‌خواهد هر دو بمانند)؛ پس عکس‌های
/// دوربری‌شده فقط با هم مقایسه می‌شوند و عکس‌های معمولی فقط با هم. مثال: [برش A (اصلی)، A، B] ← هیچ‌کدام اضافه نیست؛
/// [برش A، برش A دوباره] ← دومی اضافه است.</para>
/// <para>استفاده: Web/Infrastructure/Smart/ImageHashBackfill (هر ساعت) – حذف نرم، پس قابل بازگشت است.</para>
/// </summary>
public static class ImageDuplicates
{
    /// <summary>شناسهٔ عکس‌های اضافه (تکراری) در هر کالا.</summary>
    public static List<Guid> Redundant(IEnumerable<ImageFingerprint> images)
    {
        var result = new List<Guid>();
        foreach (var product in images.GroupBy(i => i.ProductId))
        {
            var keptPlain = new List<ulong>();
            var keptCutouts = new List<ulong>();
            foreach (var image in product.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).ThenBy(i => i.Id))
            {
                if (image.Hash is not { } signed || signed == 0)
                    continue;
                var hash = unchecked((ulong)signed);
                // دوربری‌شده‌ها و معمولی‌ها دو گروه جدا (جفت «برش + اصل» هرگز تکراری حساب نمی‌شود).
                var kept = ((image.Flags ?? 0) & ImageCleanup.CutoutDone) != 0 ? keptCutouts : keptPlain;
                if (!image.IsPrimary && kept.Any(k => ImageHash.IsDuplicate(k, hash)))
                {
                    result.Add(image.Id);
                    continue;
                }
                kept.Add(hash);
            }
        }
        return result;
    }
}
