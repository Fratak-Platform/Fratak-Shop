using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.SeoEngine;

// ═══════════════════════════════════════════════════════════════════════════
//  جدول دانش سئو و نگارش (مرحلهٔ شانزدهم)
//  «هوش» رایگان و داخلی موتور سئو و دکمهٔ «پیشنهاد هوشمند»: به‌جای پرداخت به یک API هوش مصنوعی، هزاران ردیف
//  دانش زبانی و سئویی در همین دیتابیس نگه داشته می‌شود و موتور هنگام نوشتن از آن کمک می‌گیرد:
//   • دانش اولیه (Source = Seed، حدود ۳۳ هزار ردیف): واژه‌نامهٔ فارسی با بسامد (برای املا و وزن کلمه)، واژه‌های مرکب
//     نیم‌فاصله‌دار، بن فعل‌ها، غلط‌های رایج، عبارت‌های زائد، نیت جست‌وجو، واژه‌های اسپم که گوگل جریمه می‌کند، واژه‌های ربط،
//     الگوی تیتر و پرسش، هم‌معنی‌ها و … (فایل جاسازی‌شدهٔ Core/Services/Seo/Knowledge/Data/seo-knowledge.tsv.gz).
//   • دانش آموخته (Source = Research یا Learned): عبارت‌هایی که تحقیق اینترنتی موتور پیدا می‌کند (پیشنهادهای جست‌وجو و
//     واژه‌های مشترک رقبا) و کلمه‌های کلیدی/پیشنهادهایی که کارشناس تأیید یا رد می‌کند. هر تأیید Successes و هر رد Rejections
//     را بالا می‌برد و موتور دفعهٔ بعد بر همین اساس انتخاب می‌کند؛ پس جدول با هر بار استفاده بزرگ‌تر و دقیق‌تر می‌شود.
//  کاربرد: Core/Services/Seo/Knowledge (بارگذاری، بذر، یادگیری) و Core/Services/Seo/Smart (سئو و متن هوشمند).
//  پنل: /admin/seo/knowledge (جست‌وجو، افزودن، غیرفعال کردن).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>نوع یک ردیف دانش. عددها در دیتابیس ذخیره می‌شوند؛ تغییر ندهید (فقط اضافه کنید).</summary>
public enum SeoKnowledgeKind : byte
{
    None = 0,
    /// <summary>واژهٔ فارسی شناخته‌شده. Value = نقش دستوری (N، AJ، ADV …)، Weight = لگاریتم بسامد. مثال: «کیفیت».</summary>
    Word = 1,
    /// <summary>واژهٔ مرکب نیم‌فاصله‌دار. Term = شکل جدا یا سرهم، Value = شکل درست. مثال: «نرم افزار» ← «نرم‌افزار».</summary>
    Compound = 2,
    /// <summary>فعل. Term = بن ماضی، Value = بن مضارع. مثال: «خرید» / «خر» (برای تشخیص «می‌خرید»، «نمی‌خرم» …).</summary>
    Verb = 3,
    /// <summary>واژهٔ ایست (بی‌معنی برای کلمهٔ کلیدی). مثال: «از»، «که».</summary>
    StopWord = 4,
    /// <summary>واژهٔ محاوره‌ای ← رسمی. مثال: «واسه» ← «برای».</summary>
    Informal = 5,
    /// <summary>کوته‌نوشت (نقطه‌اش پایان جمله نیست). مثال: «ه.ش».</summary>
    Abbreviation = 6,
    /// <summary>غلط املایی رایج ← درست. Topic = دلیل. مثال: «اطاق» ← «اتاق».</summary>
    Correction = 7,
    /// <summary>عبارت زائد یا سنگین ← ساده (Value خالی = حذف). مثال: «مورد استفاده قرار می‌گیرد» ← «استفاده می‌شود».</summary>
    Style = 8,
    /// <summary>واحد ← شکل استاندارد. مثال: «میلی آمپر ساعت» ← «میلی‌آمپر ساعت».</summary>
    Unit = 9,
    /// <summary>عدد به حروف ← رقم. مثال: «شش» ← «۶» (فقط در عنوان و کنار شمارنده).</summary>
    Number = 10,
    /// <summary>واژهٔ همراه کلمهٔ کلیدی (نیت جست‌وجو). Value = پیش/پس، Topic = نیت. مثال: «راهنمای خرید» (info).</summary>
    Modifier = 11,
    /// <summary>واژهٔ اثرگذار بی‌اغراق. Value = ویژگی‌ای که باید درست بودنش را تأیید کند. مثال: «کم‌مصرف» (power).</summary>
    PowerWord = 12,
    /// <summary>عبارت اسپم یا اغراق‌آمیز (خطر جریمه). Topic = title یا any. مثال: «ارزان‌ترین»، «!!!».</summary>
    Spam = 13,
    /// <summary>واژهٔ ربط برای پیوستگی متن. مثال: «به همین دلیل».</summary>
    Transition = 14,
    /// <summary>دعوت به اقدام. Value = شرط (stock، warranty …)، Topic = نوع صفحه.</summary>
    CallToAction = 15,
    /// <summary>مزیت فروشگاه با جای خالی ({warranty}، {days} …). Value = شرط.</summary>
    Benefit = 16,
    /// <summary>الگوی تیتر H2/H3. Value = نقش (intro، features …)، Topic = نوع صفحه. مثال: «{name} چیست؟».</summary>
    Heading = 17,
    /// <summary>الگوی پرسش پرتکرار خریدار. Value = شرط داده، Topic = نوع صفحه.</summary>
    Question = 18,
    /// <summary>هم‌معنی‌های امن. Value = جایگزین‌ها با «|». مثال: «متنوع» ← «متنوع|گوناگون».</summary>
    Synonym = 19,
    /// <summary>عبارت جست‌وجوی واقعی (از تحقیق اینترنتی). Topic = موضوع نرمال‌شده (مثلاً عبارت تحقیق).</summary>
    Phrase = 30,
    /// <summary>کلمهٔ کلیدی تأییدشده یا ردشده. Topic = گروه (مثلاً «product:هدفون»).</summary>
    Keyword = 31,
    /// <summary>عنوان یا متنی که کارشناس پذیرفت/رد کرد (الگوی موفق). Topic = نوع صفحه.</summary>
    Pattern = 32,
    /// <summary>ردیف فنی (مثل نسخهٔ دادهٔ اولیه).</summary>
    Meta = 99,
}

/// <summary>منبع ردیف دانش.</summary>
public enum SeoKnowledgeSource : byte
{
    None = 0,
    /// <summary>دادهٔ اولیهٔ جاسازی‌شده (با هر نسخهٔ تازه به‌روز می‌شود؛ شمارنده‌های یادگیری حفظ می‌شوند).</summary>
    Seed = 1,
    /// <summary>از تحقیق اینترنتی موتور (پیشنهاد جست‌وجو، واژه‌های رقبا).</summary>
    Research = 2,
    /// <summary>از تأیید/رد کارشناس یا انتخاب در «پیشنهاد هوشمند».</summary>
    Learned = 3,
    /// <summary>افزوده یا ویرایش‌شده در پنل.</summary>
    Manual = 4,
}

/// <summary>
/// یک ردیف دانش سئو/نگارش. کلید یکتا = (Kind، Term، Topic).
/// مثال: { Kind = Correction، Term = «باطری»، Value = «باتری»، Topic = «املا»، Weight = 1 }.
/// </summary>
public class SeoKnowledgeEntry : BaseEntity
{
    /// <summary>کالیشن باینری ستون‌های Term و Topic (مقایسهٔ حرف‌به‌حرف، مثل string.Equals در C#).</summary>
    public const string ExactCollation = "Latin1_General_100_BIN2";

    public SeoKnowledgeKind Kind { get; set; }

    [MaxLength(200)]
    public string Term { get; set; } = null!;

    [MaxLength(1000)]
    public string? Value { get; set; }

    /// <summary>موضوع/گروه (خالی = عمومی). همیشه مقدار دارد تا ایندکس یکتا درست کار کند.</summary>
    [MaxLength(100)]
    public string Topic { get; set; } = "";

    /// <summary>وزن یا اطمینان (۰ تا حدود ۱۰). اصلاح‌های زیر ۰٫۷ فقط «پیشنهاد» می‌شوند، خودکار اعمال نمی‌شوند.</summary>
    public double Weight { get; set; }

    /// <summary>چند بار در پیشنهادها به کار رفته.</summary>
    public int Uses { get; set; }

    /// <summary>چند بار کارشناس پیشنهاد حاوی این ردیف را پذیرفته.</summary>
    public int Successes { get; set; }

    /// <summary>چند بار رد شده. اگر رد خیلی بیشتر از پذیرش باشد، موتور دیگر از این ردیف استفاده نمی‌کند.</summary>
    public int Rejections { get; set; }

    public SeoKnowledgeSource Source { get; set; }

    /// <summary>خاموش = موتور نادیده‌اش می‌گیرد (ردیف حذف نمی‌شود تا بذر دوباره روشنش نکند).</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset? LastUsedAt { get; set; }
}

/// <summary>
/// بازخورد دکمهٔ «پیشنهاد هوشمند» (مرحلهٔ شانزدهم): کدام نسخه (پاک‌سازی، سئو، جذاب، ساختاریافته) برای کدام نوع فیلد
/// انتخاب یا رد شد. با این آمار، نسخه‌ای که کارمندان بیشتر می‌پسندند بالاتر نشان داده می‌شود.
/// متن خود فیلد ذخیره نمی‌شود (فقط هش و طول) تا دادهٔ حساس در این جدول نماند.
/// </summary>
public class SmartTextFeedback : BaseEntity
{
    /// <summary>نوع فیلد. مثال: product-name، seo-title، meta، short، body، plain.</summary>
    [MaxLength(40)]
    public string Purpose { get; set; } = null!;

    /// <summary>نسخهٔ انتخاب‌شده یا ردشده. مثال: clean، seo، persuasive، structured.</summary>
    [MaxLength(40)]
    public string Variant { get; set; } = null!;

    public bool Accepted { get; set; }

    /// <summary>شناسهٔ قاعده‌هایی که در این نسخه اعمال شده بود (با ویرگول). مثال: «c:باطری,s:لازم به ذکر است».</summary>
    [MaxLength(2000)]
    public string? Rules { get; set; }

    [MaxLength(64)]
    public string? InputHash { get; set; }

    public int InputLength { get; set; }

    public int OutputLength { get; set; }

    public Guid? UserId { get; set; }
}
