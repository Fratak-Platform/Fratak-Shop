namespace Fratak_Shop.Web.Models;

/// <summary>
/// ورودی partial «_BrandLogo» (Pages/Shared/_BrandLogo.cshtml) که لوگوی فروشگاه را با نسخهٔ حالت تیره چاپ می‌کند.
/// <para>
/// <paramref name="CssClass"/>: کلاس‌های اندازه/چینش تصویر (مثلاً <c>"w-32 md:w-44"</c>).
/// <paramref name="Width"/>: عرض پیش‌فرض به پیکسل؛ ارتفاع از نسبت لوگوی برند (۶۲۱٫۹ × ۱۹۲٫۹) حساب می‌شود تا
/// جای تصویر پیش از بارگذاری رزرو شود (بدون پرش صفحه – CLS).
/// </para>
/// مثال: <c>@await Html.PartialAsync("_BrandLogo", new BrandLogoModel("w-36 mx-auto", 144))</c>
/// </summary>
public record BrandLogoModel(string CssClass, int Width)
{
    /// <summary>نسبت عرض به ارتفاع لوگوی افقی برند (viewBox فایل logo.svg).</summary>
    public const double AspectRatio = 621.9 / 192.9;

    /// <summary>ارتفاع متناظر با <see cref="Width"/>.</summary>
    public int Height => (int)Math.Round(Width / AspectRatio);
}
