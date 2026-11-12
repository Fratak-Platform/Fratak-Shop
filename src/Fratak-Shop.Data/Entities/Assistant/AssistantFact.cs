using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Assistant;

/// <summary>نوع یک «دانستنی» دستیار (دور ۲۸).</summary>
public enum AssistantFactKind : byte
{
    /// <summary>واژه‌نامه: «ANC چیه؟» ← توضیح کوتاه.</summary>
    Glossary = 1,

    /// <summary>راهنمای خرید یک دسته: «برای خرید پاوربانک به چی دقت کنم؟».</summary>
    Guide = 2,

    /// <summary>گپ کوتاه انسانی: «خسته نباشی»، «ربات هستی؟».</summary>
    SmallTalk = 3,

    /// <summary>هم‌معنی کالا برای جست‌وجو: «نوت‌بوک» ← «لپ‌تاپ».</summary>
    Synonym = 4,

    /// <summary>یادگرفته از کارکنان یا پرسش‌های پرتکرار (پنل «دانش دستیار»).</summary>
    Learned = 5,
}

/// <summary>
/// دور ۲۸ – جدول «دانش دستیار» (مثل جدول دانش سئو): هزاران دانستنی بذرشده از فایل جاسازی‌شده + هر چه کارکنان اضافه می‌کنند.
/// <para>یادگیری: هر بار که جوابی از این جدول داده شود Shown یکی بالا می‌رود و 👍/👎 مشتری (AssistantMessage.Helpful با Topic «دانش:شناسه»)
/// هر شب در Helpful/Unhelpful جمع می‌شود. اطمینان = Weight × (H+4) / (H+4+1.5U)؛ دانستنی با اطمینان کمتر از ۰٫۳ (بعد از ۵ رأی) خاموش می‌شود.</para>
/// <para>مثال: Kind = Glossary، Term = «میلی آمپر ساعت»، Aliases = «mah|میلی‌آمپر»، Answer = «ظرفیت باتری…».</para>
/// </summary>
public class AssistantFact : BaseEntity
{
    public AssistantFactKind Kind { get; set; }

    /// <summary>واژه یا عبارت اصلی (شکل نرمال‌شده؛ با Kind یکتا).</summary>
    [Required]
    [MaxLength(120)]
    public string Term { get; set; } = null!;

    /// <summary>شکل‌های دیگر همان واژه، جداشده با «|». مثال: «mah|میلی‌آمپر».</summary>
    [MaxLength(600)]
    public string? Aliases { get; set; }

    /// <summary>متن جواب (ساده، بدون HTML). برای هم‌معنی‌ها = واژهٔ مقصد.</summary>
    [Required]
    [MaxLength(1500)]
    public string Answer { get; set; } = null!;

    /// <summary>لینک داخلی اختیاری زیر جواب (فقط «/…»).</summary>
    [MaxLength(300)]
    public string? Url { get; set; }

    /// <summary>موضوع برای گروه‌بندی پنل. مثال: «باتری»، «صوتی».</summary>
    [MaxLength(60)]
    public string? Topic { get; set; }

    /// <summary>وزن پایه (۰ تا ۱).</summary>
    public double Weight { get; set; } = 1;

    public int Shown { get; set; }

    public int Helpful { get; set; }

    public int Unhelpful { get; set; }

    /// <summary>از فایل بذر آمده (ویرایش کارمند این را false می‌کند تا بذر بعدی رویش ننویسد).</summary>
    public bool IsSeed { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(100)]
    public string? CreatedByName { get; set; }
}
