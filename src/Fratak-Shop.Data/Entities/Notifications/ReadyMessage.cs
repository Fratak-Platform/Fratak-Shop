using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Notifications;

/// <summary>
/// «پیام آماده»: متن از پیش نوشته‌شده که کارکنان با یک کلیک در فرم‌ها درج می‌کنند.
/// <para>
/// دو کاربرد اصلی دارد:
/// ۱) اعلان آماده (مثلاً «تخفیف آخر هفته») برای ارسال همگانی یا تکی؛
/// ۲) پاسخ آماده برای تیکت، دیدگاه، پیام تماس و پیام سفارش (مثلاً «سفارش شما فردا ارسال می‌شود»).
/// </para>
/// متن می‌تواند برچسب‌هایی مثل <c>{FirstName}</c> یا <c>{OrderNumber}</c> داشته باشد که هنگام ارسال
/// با اطلاعات واقعی گیرنده جایگزین می‌شوند (همان برچسب‌های قالب اعلان).
/// </summary>
public class ReadyMessage : BaseEntity
{
    /// <summary>نام کوتاه برای انتخاب در فهرست، مثلاً «تأخیر ارسال».</summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    /// <summary>عنوان اعلان (فقط برای اعلان‌ها استفاده می‌شود؛ در پاسخ تیکت نادیده گرفته می‌شود).</summary>
    [MaxLength(200)]
    public string? Title { get; set; }

    /// <summary>متن پیام.</summary>
    [Required]
    [MaxLength(2000)]
    public string Body { get; set; } = null!;

    /// <summary>لینک اختیاری اعلان (آدرس نسبی سایت).</summary>
    [MaxLength(300)]
    public string? LinkUrl { get; set; }

    /// <summary>کجاها نمایش داده شود (ترکیبی از چند مقدار).</summary>
    public ReadyMessageScope Scopes { get; set; } = ReadyMessageScope.All;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>تعداد دفعات استفاده؛ پرکاربردها بالاتر نمایش داده می‌شوند.</summary>
    public int UsageCount { get; set; }
}

/// <summary>محل‌های استفادهٔ پیام آماده (Flags: یک پیام می‌تواند در چند جا نمایش داده شود).</summary>
[Flags]
public enum ReadyMessageScope
{
    None = 0,
    Broadcast = 1,
    Direct = 2,
    Ticket = 4,
    Comment = 8,
    Order = 16,
    ContactMessage = 32,
    All = Broadcast | Direct | Ticket | Comment | Order | ContactMessage,
}
