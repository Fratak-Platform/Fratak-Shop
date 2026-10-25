using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Admin;

/// <summary>
/// پیش‌نویس خودکار یک فرم ویرایش در پنل (ذخیرهٔ خودکار هنگام تایپ).
/// <para>
/// هدف: اگر مرورگر بسته شد، اینترنت قطع شد یا کس دیگری هم‌زمان همان مورد را ذخیره کرد، نوشته‌های کارمند از بین نرود.
/// فرم‌هایی که ویژگی <c>data-autosave</c> دارند (ویرایش محصول، مقاله و جدول‌های «مدیریت داده») هر چند ثانیه
/// مقدارهای خود را با <c>admin-edit.js</c> به <c>/admin/ux?handler=Draft</c> می‌فرستند.
/// </para>
/// <para>
/// برای «ادغام نسخه‌ها» دو چیز ذخیره می‌شود: مقدارهای اولیهٔ فرم هنگام باز شدن (base) و مقدارهای فعلی کارمند (mine).
/// اگر بعداً رکورد توسط دیگری تغییر کرده باشد، با مقایسهٔ سه‌طرفه فقط فیلدهایی که هر دو نفر تغییر داده‌اند به‌عنوان تعارض نشان داده می‌شوند.
/// </para>
/// سرویس: <c>EditDraftService</c> (Core). پیش‌نویس بعد از ذخیرهٔ موفق فرم حذف می‌شود و پیش‌نویس‌های قدیمی (۳۰ روز) پاک می‌شوند.
/// </summary>
public class EditDraft : BaseEntity
{
    /// <summary>کارمندی که پیش‌نویس مال اوست (هر کس فقط پیش‌نویس خودش را می‌بیند).</summary>
    public Guid UserId { get; set; }

    /// <summary>کلید فرم، مثلاً <c>product:3f2a…</c>، <c>blog:new</c> یا <c>manage:faq:…</c>.</summary>
    [Required]
    [MaxLength(120)]
    public string FormKey { get; set; } = null!;

    /// <summary>نسخهٔ رکورد هنگام باز شدن فرم (UpdatedAt به تیک)؛ برای فهمیدن «بعد از من کسی ذخیره کرده؟». برای مورد جدید صفر.</summary>
    public long BaseStamp { get; set; }

    /// <summary>عنوانی برای فهرست «پیش‌نویس‌های من»، مثلاً نام محصول.</summary>
    [MaxLength(200)]
    public string? Title { get; set; }

    /// <summary>آدرس صفحهٔ فرم برای برگشتن به آن، مثلاً <c>/admin/products/edit/…</c>.</summary>
    [MaxLength(300)]
    public string? Url { get; set; }

    /// <summary>JSON به شکل <c>{"base":{name:[values]},"mine":{name:[values]}}</c>.</summary>
    [Required]
    public string Data { get; set; } = null!;
}
