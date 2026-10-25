using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Admin;

/// <summary>
/// متن راهنمای یک صفحهٔ پنل مدیریت (دکمهٔ «راهنما» بالای هر صفحه).
/// <para>
/// هر صفحه با آدرسش شناخته می‌شود؛ مثلاً <c>/admin/orders</c>. اگر برای آدرس دقیق راهنما نباشد،
/// نزدیک‌ترین والد استفاده می‌شود (برای <c>/admin/orders/123</c> راهنمای <c>/admin/orders/detail</c> یا <c>/admin/orders</c>).
/// </para>
/// متن‌های پیش‌فرض با <c>Round7DefaultsSeeder</c> ساخته می‌شوند و مدیر از صفحهٔ <c>/admin/help</c> ویرایششان می‌کند.
/// </summary>
public class AdminHelpArticle : BaseEntity
{
    /// <summary>آدرس صفحه با حروف کوچک و بدون «/» انتهایی، مثلاً <c>/admin/products/edit</c>.</summary>
    [Required]
    [MaxLength(150)]
    public string Path { get; set; } = null!;

    /// <summary>عنوان راهنما، مثلاً «مدیریت سفارش‌ها».</summary>
    [Required]
    [MaxLength(150)]
    public string Title { get; set; } = null!;

    /// <summary>متن راهنما به صورت HTML ساده (پاک‌سازی‌شده با HtmlSanitizer).</summary>
    [Required]
    [MaxLength(20000)]
    public string Body { get; set; } = null!;

    /// <summary>آیا مدیر این متن را ویرایش کرده؟ (متن‌های ویرایش‌شده با به‌روزرسانی پیش‌فرض‌ها بازنویسی نمی‌شوند)</summary>
    public bool IsCustomized { get; set; }
}
