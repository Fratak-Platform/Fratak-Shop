using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// دور ۲۲: ابزار فهرست «تصویرهای پیشنهادی دسته» (ستون Category.ImageOptionsJson).
/// <para>
/// خواستهٔ کاربر: برای هر دسته ۴ تصویر پیشنهاد شود، یکی پیش‌فرض تأیید و نمایش داده شود و ادمین بتواند دیگری را انتخاب یا هر کدام/همه را حذف کند.
/// این کلاس فقط خواندن/نوشتن امن فهرست را انجام می‌دهد (نام فایل ساده، حداکثر ۴، بدون تکرار)؛ ساخت تصویرها در SmartImageFinder (لایهٔ وب) است.
/// </para>
/// <para>مثال: Parse("[\"a.webp\",\"b.webp\"]") ← [a.webp، b.webp]؛ Parse(null) ← null (هنوز پیشنهادی ساخته نشده)؛ Parse("[]") ← [] (ادمین همه را حذف کرده).</para>
/// <para>محل استفاده: SmartCatalogService، SmartOrchestrator (کار «تکمیل دسته») و صفحهٔ «تکمیل هوشمند دسته‌ها».</para>
/// </summary>
public static class CategoryImages
{
    /// <summary>تعداد تصویر پیشنهادی هر دسته.</summary>
    public const int Max = 4;

    private static readonly Regex SafeName = new("^[A-Za-z0-9][A-Za-z0-9_\\-\\.]{0,200}\\.(webp|jpg|jpeg|png)\\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    /// <summary>
    /// نام فایل امن است؟ فقط نام ساده (بدون مسیر، «..»، اسلش یا آدرس). مثال: «9f3c.webp» ← بله؛ «../x.webp» یا «https://a/b.webp» ← خیر.
    /// </summary>
    public static bool IsSafeName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && !name.Contains("..", StringComparison.Ordinal) && SafeName.IsMatch(name);

    /// <summary>خواندن فهرست. JSON خراب یا نام ناامن ← نادیده (هرگز خطا نمی‌دهد).</summary>
    public static IReadOnlyList<string>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json);
            return list?.Where(IsSafeName).Distinct(StringComparer.Ordinal).Take(Max).ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>نوشتن فهرست (فقط نام‌های امن، حداکثر ۴). مثال: [a.webp] ← «["a.webp"]».</summary>
    public static string Serialize(IEnumerable<string> names) =>
        JsonSerializer.Serialize(names.Where(IsSafeName).Distinct(StringComparer.Ordinal).Take(Max).ToList());
}
