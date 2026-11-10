using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Assistant;

/// <summary>
/// دور ۲۳ – یک پیام مشتری به «دستیار فروشگاه» (چت‌بات بدون هوش مصنوعی بیرونی). فقط متن کوتاه پرسش و نتیجهٔ تشخیص ذخیره می‌شود
/// (نه پاسخ کامل، نه اطلاعات حساس) تا پنل «دستیار فروشگاه» این‌ها را نشان دهد: پرسش‌های پرتکرار، کالاهای درخواستی که نداریم
/// و کالاهایی که مشتری درباره‌شان پرسیده ولی جوابی نبود (کمبود مشخصات ← خوراک «تکمیل هوشمند»).
/// <para>مثال: «پاوربانک ۲۰ هزار شیائومی دارید؟» ← Intent = Search، Answered = true؛ «کنسول PS5 دارید؟» (نداریم) ← Intent = Demand، Query = «کنسول PS5».</para>
/// </summary>
public class AssistantMessage : BaseEntity
{
    /// <summary>شناسهٔ گفت‌وگو (تصادفی، از مرورگر؛ برای شمارش «یک بار برای هر گفت‌وگو»).</summary>
    [Required]
    [MaxLength(32)]
    public string SessionKey { get; set; } = null!;

    /// <summary>کاربر واردشده (اختیاری).</summary>
    public Guid? UserId { get; set; }

    /// <summary>متن پرسش (حداکثر ۵۰۰ نویسه؛ شمارهٔ موبایل/کارت پیش از ذخیره پوشانده می‌شود).</summary>
    [Required]
    [MaxLength(500)]
    public string Text { get; set; } = null!;

    public AssistantIntent Intent { get; set; }

    /// <summary>کالایی که پرسش درباره‌اش بود (صفحهٔ فعلی یا کالای پیداشده).</summary>
    public Guid? ProductId { get; set; }

    /// <summary>دستیار جواب مطمئن داشت؟ false ← در پنل «بی‌جواب‌ها».</summary>
    public bool Answered { get; set; }

    /// <summary>موضوع پرسش دربارهٔ کالا (مثلاً «گارانتی»، «باتری»، «ابعاد»)؛ برای گروه‌بندی پرسش‌های پرتکرار.</summary>
    [MaxLength(60)]
    public string? Topic { get; set; }

    /// <summary>عبارت جست‌وجوشده (برای کالای درخواستیِ ناموجود و پرسش‌های جست‌وجو).</summary>
    [MaxLength(200)]
    public string? Query { get; set; }
}

/// <summary>نوع پیام تشخیص‌داده‌شده.</summary>
public enum AssistantIntent : byte
{
    Unknown = 0,
    Greeting = 1,
    Search = 2,
    ProductQuestion = 3,
    Compare = 4,
    CartAdd = 5,
    CartRemove = 6,
    CartShow = 7,
    Checkout = 8,
    Address = 9,
    Track = 10,
    Help = 11,
    OffTopic = 12,
    Demand = 13,
    Human = 14,
    Thanks = 15,
}
