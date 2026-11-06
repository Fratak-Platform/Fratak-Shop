using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Core.Utilities;

public static class SlugHelper
{
    /// <summary>
    /// نامک (Slug) تمیز و همیشه انگلیسی برای آدرس صفحه‌ها.
    /// <para>دور سئوی خودکار: قبلاً حروف فارسی در نامک می‌ماند («درباره-ما»)؛ حالا متن فارسی ترجمه/آوانویسی می‌شود
    /// (منطق کامل و واژه‌نامه: <see cref="EnglishSlug"/> در لایهٔ Data، تا قانون ذخیرهٔ DbContext هم از همان استفاده کند).</para>
    /// مثال: <c>"درباره ما!".ToSlug()</c> ← <c>"about-us"</c>، <c>"About Us".ToSlug()</c> ← <c>"about-us"</c>،
    /// <c>"گوشی سامسونگ ۱۲۸".ToSlug()</c> ← <c>"phone-samsung-128"</c>. فقط a-z، 0-9 و خط تیرهٔ تکی می‌ماند.
    /// <para>محل استفاده: سرویس صفحه‌های محتوا و سؤالات متداول، برنامه‌ریز کلمهٔ کلیدی موتور سئو.</para>
    /// </summary>
    public static string ToSlug(this string? value, int maxLength = 200) => EnglishSlug.From(value, maxLength);
}
