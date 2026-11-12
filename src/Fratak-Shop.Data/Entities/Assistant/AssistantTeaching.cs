using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Assistant;

/// <summary>
/// دور ۲۵ – «آموزش دستیار»: جوابی که کارکنان برای یک پرسش پرتکرارِ بی‌جواب (یا پاسخی که مشتری 👎 زده) یاد داده‌اند.
/// <para>مثال: Question = «ارسال به کیش دارید؟»، Answer = «بله، با پست پیشتاز ۵ تا ۷ روز کاری.»، Url = «/page/shipping».
/// از این به بعد هر پیامی که خیلی شبیه این پرسش باشد (شباهت دوطرفه ≥ ۰٫۷۵) همین جواب را می‌گیرد.</para>
/// <para>محل استفاده: Core/Services/Assistant/AssistantTeachings.cs (تطبیق و مدیریت) و پنل /admin/smart/assistant/teach.</para>
/// </summary>
public class AssistantTeaching : BaseEntity
{
    /// <summary>پرسش نمونه (همان جمله‌ای که مشتری نوشته بود یا شکل تمیزشدهٔ آن).</summary>
    [Required]
    [MaxLength(300)]
    public string Question { get; set; } = null!;

    /// <summary>متن جواب (متن ساده، نه HTML). اگر خالی باشد فقط مسیر (Intent) یا لینک استفاده می‌شود.</summary>
    [MaxLength(1000)]
    public string? Answer { get; set; }

    /// <summary>لینک داخلی سایت برای دکمهٔ زیر جواب (فقط مسیرهایی که با «/» شروع می‌شوند).</summary>
    [MaxLength(300)]
    public string? Url { get; set; }

    /// <summary>
    /// به‌جای جواب ثابت، پیام را به این کار دستیار بفرست (مثلاً «سفارشم نرسیده» ← پیگیری سفارش). null = جواب ثابت.
    /// </summary>
    public AssistantIntent? Intent { get; set; }

    /// <summary>خاموش ← در پاسخ‌ها استفاده نمی‌شود (بدون حذف).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>چند بار این آموزش به مشتری جواب داده است.</summary>
    public int HitCount { get; set; }

    /// <summary>آخرین باری که به کار رفت.</summary>
    public DateTimeOffset? LastHitAt { get; set; }

    /// <summary>نام کارمندی که آموزش داده (برای پنل).</summary>
    [MaxLength(100)]
    public string? CreatedByName { get; set; }
}
