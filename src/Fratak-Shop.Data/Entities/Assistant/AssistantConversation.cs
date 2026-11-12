using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Assistant;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۴ – جدول‌های تازهٔ دستیار فروشگاه
//   • AssistantProfile        : سلیقهٔ گفت‌وگوی هر کاربر واردشده (با چه اسمی صدایش کنیم، رسمی یا خودمانی).
//   • AssistantHandoff        : «گفت‌وگو با کارشناس» – وقتی مشتری می‌خواهد با یک آدم حرف بزند.
//   • AssistantHandoffMessage : پیام‌های همان گفت‌وگو (مشتری یا کارشناس).
//  مهاجرت: Data/Migrations/20261220090000_Round24Assistant.cs
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// لحن گفت‌وگوی دستیار. پیش‌فرض Auto یعنی از سن (تاریخ تولد) و طرز نوشتن خود مشتری حدس زده می‌شود.
/// <para>مثال: مشتری ۲۲ ساله که «سلام، اینو داری؟» می‌نویسد ← Casual؛ مشتری ۶۵ ساله ← Formal.</para>
/// </summary>
public enum AssistantTone : byte
{
    /// <summary>خودکار (سن + طرز نوشتن).</summary>
    Auto = 0,

    /// <summary>کاملاً رسمی: «سلام و وقت بخیر، علی ناظری گرامی».</summary>
    Formal = 1,

    /// <summary>محترمانه: «ناظری گرامی».</summary>
    Respectful = 2,

    /// <summary>دوستانه: «علی عزیز».</summary>
    Friendly = 3,

    /// <summary>خودمانی: «سلام علی! 👋».</summary>
    Casual = 4,
}

/// <summary>
/// سلیقهٔ گفت‌وگوی یک کاربر واردشده با دستیار. وقتی مشتری می‌گوید «منو علی صدا کن» یا «رسمی صحبت کن» اینجا ذخیره می‌شود
/// تا دفعهٔ بعد (حتی روی دستگاه دیگر) همان‌طور با او صحبت شود. مهمان‌ها همین را در حافظهٔ مرورگر (localStorage) نگه می‌دارند.
/// <para>محل استفاده: Core/Services/Assistant/AssistantService.cs (VoiceAsync و PreferenceAsync).</para>
/// </summary>
public class AssistantProfile : BaseEntity
{
    /// <summary>کاربر (یکتا).</summary>
    public Guid UserId { get; set; }

    /// <summary>اسمی که مشتری خواسته با آن صدایش کنیم (حداکثر ۳۰ نویسه؛ فقط حروف). خالی = از نام حساب.</summary>
    [MaxLength(30)]
    public string? CallMe { get; set; }

    /// <summary>لحن انتخابی مشتری (Auto = خودکار).</summary>
    public AssistantTone Tone { get; set; }

    /// <summary>
    /// طرز نوشتن مشاهده‌شده: از ‎-3 (خیلی رسمی) تا ‎+3 (خیلی خودمانی). با هر پیام کمی جابه‌جا می‌شود (بخش‌بندی پویا).
    /// </summary>
    public int Register { get; set; }
}

/// <summary>وضعیت گفت‌وگو با کارشناس.</summary>
public enum AssistantHandoffStatus : byte
{
    /// <summary>مشتری منتظر است؛ هنوز کارشناسی جواب نداده.</summary>
    Waiting = 1,

    /// <summary>کارشناس جواب داده و گفت‌وگو باز است.</summary>
    Active = 2,

    /// <summary>بسته شد (مشتری یا کارشناس، یا خودکار بعد از ۲۴ ساعت بی‌پیامی).</summary>
    Closed = 3,
}

/// <summary>
/// گفت‌وگوی «انتقال به کارشناس». با درخواست مشتری در چت ساخته می‌شود، به کارشناسان پشتیبانی اعلان می‌رود و
/// کارشناس از پنل ‎/admin/smart/assistant/handoffs جواب می‌دهد. ویجت فقط وقتی گفت‌وگوی باز دارد هر چند ثانیه پیام تازه می‌پرسد.
/// <para>مثال: «می‌خوام با یه آدم حرف بزنم» ← یک ردیف Waiting + پیام‌های اخیر چت به‌عنوان پیش‌زمینه.</para>
/// </summary>
public class AssistantHandoff : BaseEntity
{
    /// <summary>شناسهٔ گفت‌وگوی مرورگر (همان SessionKey پیام‌های دستیار).</summary>
    [Required]
    [MaxLength(32)]
    public string SessionKey { get; set; } = null!;

    /// <summary>کاربر واردشده (اختیاری؛ مهمان هم می‌تواند).</summary>
    public Guid? UserId { get; set; }

    /// <summary>نام نمایشی مشتری برای کارشناس (نام حساب یا «مهمان»).</summary>
    [MaxLength(80)]
    public string? DisplayName { get; set; }

    /// <summary>صفحه‌ای که مشتری در آن درخواست داد (مسیر داخلی).</summary>
    [MaxLength(300)]
    public string? PageUrl { get; set; }

    public AssistantHandoffStatus Status { get; set; } = AssistantHandoffStatus.Waiting;

    /// <summary>کارشناسی که جواب داد.</summary>
    public Guid? StaffUserId { get; set; }

    [MaxLength(80)]
    public string? StaffName { get; set; }

    /// <summary>زمان آخرین پیام (برای مرتب‌سازی و بستن خودکار).</summary>
    public DateTimeOffset LastMessageAt { get; set; }

    /// <summary>پیام‌های خوانده‌نشدهٔ مشتری برای کارشناس.</summary>
    public int UnreadForStaff { get; set; }

    public List<AssistantHandoffMessage> Messages { get; set; } = [];
}

/// <summary>یک پیام در گفت‌وگو با کارشناس. متن ساده (HTML نیست)؛ حداکثر ۱۰۰۰ نویسه.</summary>
public class AssistantHandoffMessage : BaseEntity
{
    public Guid HandoffId { get; set; }

    public AssistantHandoff Handoff { get; set; } = null!;

    /// <summary>true = کارشناس، false = مشتری (یا پیش‌زمینهٔ خودکار چت).</summary>
    public bool FromStaff { get; set; }

    /// <summary>نام نویسنده (نام کارشناس یا «مشتری»).</summary>
    [MaxLength(80)]
    public string? AuthorName { get; set; }

    [Required]
    [MaxLength(1000)]
    public string Text { get; set; } = null!;
}
