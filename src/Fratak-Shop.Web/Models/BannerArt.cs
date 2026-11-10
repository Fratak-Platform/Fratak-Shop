using Fratak_Shop.Core.DTOs.Banners.Query;

namespace Fratak_Shop.Web.Models;

/// <summary>
/// دور ۲۶: ورودی partial «_BannerArt» (نمایش یک بنر در سایت).
/// <list type="bullet">
/// <item>بنر لینک دارد ← &lt;a&gt;؛ بدون لینک (بنر اطلاع‌رسانی) ← &lt;div&gt; (نه لینک «#» بی‌معنا).</item>
/// <item>نسخهٔ زنده (LiveHtml) دارد ← HTML متحرک + همان تصویر ثابت برای مرورگرهای قدیمی؛ ندارد ← فقط تصویر.</item>
/// </list>
/// مثال: <c>new BannerArt(banner, "swiper-slide", "w-full h-auto", Eager: true, 1920, 420)</c>.
/// </summary>
/// <param name="Banner">بنر.</param>
/// <param name="WrapperClass">کلاس‌های قاب بیرونی (a یا div).</param>
/// <param name="ImageClass">کلاس‌های تصویر ثابت.</param>
/// <param name="Eager">بنر بالای صفحه (اولین اسلاید/سربرگ دسته): تصویر بی‌درنگ و حرکت از همان اول.</param>
/// <param name="Width">عرض طبیعی تصویر (برای جلوگیری از پرش صفحه).</param>
/// <param name="Height">ارتفاع طبیعی تصویر.</param>
/// <param name="Sizes">srcset خودکار بسازد؟ (اسلایدر و بنر عریض بله).</param>
public sealed record BannerArt(BannerDto Banner, string WrapperClass, string ImageClass, bool Eager, int Width, int Height, bool Sizes = true);
