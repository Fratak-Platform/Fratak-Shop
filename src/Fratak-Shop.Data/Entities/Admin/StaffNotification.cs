using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Admin;

/// <summary>
/// اعلان داخلی پنل مدیریت برای یک کارمند (مرکز اعلان پنل).
/// <para>
/// برای هر گیرنده یک ردیف جدا ساخته می‌شود تا «خوانده شد» هر نفر مستقل باشد.
/// مثال: وقتی سفارش جدیدی ثبت می‌شود برای همهٔ کارکنانی که مجوز «مشاهدهٔ سفارش‌ها» دارند
/// یک ردیف با <see cref="StaffNotificationKind.NewOrder"/> ساخته می‌شود.
/// </para>
/// محل ساخت: <c>Fratak_Shop.Core.Services.Admin.StaffNotificationService</c>.
/// </summary>
public class StaffNotification : BaseEntity
{
    /// <summary>شناسهٔ کارمندی که اعلان را می‌بیند.</summary>
    public Guid RecipientUserId { get; set; }

    /// <summary>نوع اعلان؛ برای آیکون، رنگ و فیلتر استفاده می‌شود.</summary>
    public StaffNotificationKind Kind { get; set; }

    /// <summary>عنوان کوتاه، مثلاً «سفارش جدید ۱۴۰۵۰۰۱۲».</summary>
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    /// <summary>توضیح اختیاری، مثلاً «۲ کالا – ۱٬۲۵۰٬۰۰۰ تومان».</summary>
    [MaxLength(500)]
    public string? Body { get; set; }

    /// <summary>آدرس داخلی پنل که با کلیک باز می‌شود (فقط آدرس نسبی).</summary>
    [MaxLength(300)]
    public string? Url { get; set; }

    /// <summary>زمان خوانده شدن؛ null یعنی هنوز خوانده نشده.</summary>
    public DateTimeOffset? ReadAt { get; set; }

    public User Recipient { get; set; } = null!;
}

/// <summary>نوع اعلان داخلی کارکنان. عدد هر مقدار در دیتابیس ذخیره می‌شود؛ ترتیب را عوض نکنید.</summary>
public enum StaffNotificationKind
{
    System = 0,
    NewOrder = 1,
    OrderPaid = 2,
    NewTicket = 3,
    TicketReply = 4,
    NewMessage = 5,
    NewComment = 6,
    NewReturn = 7,
    NewWithdrawal = 8,
    BlogReview = 9,
    Mention = 10,
    Assignment = 11,
    SupplierReport = 12,
    Alert = 13,
    Task = 14,
    OrderMessageAnswered = 15,
    Approval = 16,

    /// <summary>مشکل مرسوله (برگشت به فرستنده، طولانی شدن رهگیری) – مرحلهٔ ۷ فاز ۶.</summary>
    ShipmentIssue = 17,

    /// <summary>سررسید هزینهٔ تکراری (هاست، دامنه، اشتراک …) – مرحلهٔ ۷ فاز ۷ب.</summary>
    ExpenseDue = 18,

    /// <summary>خطای ارسال گزارش زمان‌بندی‌شده (ایمیل/بله/تلگرام) – مرحلهٔ ۷ فاز ۷ب.</summary>
    ReportFailed = 19,

    /// <summary>پیشنهادهای تازهٔ موتور سئو آمادهٔ بررسی‌اند – مرحلهٔ ۷ فاز ۸.</summary>
    SeoSuggestions = 20,

    /// <summary>
    /// نتیجهٔ کار «دستیار هوشمند» (دور بیستم): عکس/مشخصات/توضیحات محصول آماده شد یا منتظر تأیید است. لینک مستقیم به همان بخش
    /// (مثال: /admin/smart/product/{id}#task-{taskId}).
    /// </summary>
    SmartResult = 21,
}

/// <summary>
/// تنظیمات شخصی هر کارمند در پنل (کلید/مقدار).
/// <para>
/// مثال کلیدها: <c>pins</c> (صفحه‌های سنجاق‌شده)، <c>recent</c> (صفحه‌های اخیر)،
/// <c>filters:/admin/orders</c> (فیلترهای ذخیره‌شدهٔ یک صفحه)، <c>columns:manage/coupons</c> (ستون‌های انتخابی).
/// مقدار معمولاً یک JSON کوچک است.
/// </para>
/// محل استفاده: <c>Fratak_Shop.Core.Services.Admin.AdminPreferenceService</c>.
/// </summary>
public class AdminUserPreference : BaseEntity
{
    public Guid UserId { get; set; }

    [Required]
    [MaxLength(150)]
    public string Key { get; set; } = null!;

    /// <summary>مقدار (حداکثر ۲۰ هزار نویسه، معمولاً JSON).</summary>
    [Required]
    [MaxLength(20000)]
    public string Value { get; set; } = null!;
}
