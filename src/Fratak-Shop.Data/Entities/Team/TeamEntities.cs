using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Team;

// ═══════════════════════════════════════════════════════════════════════════
//  امنیت و کار تیمی کارکنان (مرحلهٔ هفتم – فاز ۱۰)
//   • ApprovalRequest : «تأیید دونفره» – کار حساس (مثلاً افزایش ۵ میلیونی کیف پول) اول ثبت می‌شود و فقط بعد از
//                       تأیید یک کارمند دیگر (نه خود درخواست‌دهنده) اجرا می‌شود.
//   • StaffTask       : فهرست کارهای شخصی هر کارمند (با موعد، اولویت، لینک صفحهٔ مرتبط و یادآوری).
//  رمز یک‌بارمصرف زمان‌دار (TOTP) روی خود User ذخیره می‌شود (TotpSecret و …).
//  منطق: Core/Services/Team؛ پنل: /admin/approvals، /admin/tasks، /admin/account.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>نوع کار حساسی که تأیید دونفره لازم دارد. عددها در دیتابیس ذخیره می‌شوند؛ تغییر ندهید.</summary>
public enum ApprovalKind : byte
{
    /// <summary>افزایش یا کاهش دستی موجودی کیف پول مشتری (از سقف تنظیم‌شده به بالا).</summary>
    WalletAdjust = 1,

    /// <summary>علامت «پرداخت شد» برای درخواست برداشت از کیف پول (از سقف تنظیم‌شده به بالا).</summary>
    WithdrawalPaid = 2,

    /// <summary>تغییر نقش‌های یک کارمند یا مالک کردن او.</summary>
    StaffRoles = 3,
}

/// <summary>وضعیت درخواست تأیید.</summary>
public enum ApprovalStatus : byte
{
    Pending = 1,

    /// <summary>تأیید و با موفقیت اجرا شد.</summary>
    Approved = 2,

    Rejected = 3,

    /// <summary>درخواست‌دهنده پیش از تصمیم پس گرفت.</summary>
    Cancelled = 4,

    /// <summary>تأیید شد ولی اجرا خطا داد (مثلاً موجودی کیف پول کافی نبود)؛ پیام در ResultMessage.</summary>
    Failed = 5,

    /// <summary>تا ExpiresAt کسی تصمیم نگرفت.</summary>
    Expired = 6,
}

/// <summary>
/// یک درخواست «تأیید دونفره». مثال: Kind=WalletAdjust، Summary=«افزایش ۵٬۰۰۰٬۰۰۰ تومان کیف پول علی رضایی»،
/// PayloadJson={"userId":"…","amount":5000000,"description":"جبران تأخیر","promotional":true}.
/// محل اجرا: Core/Services/Team/ApprovalService (هر نوع یک اجراکننده دارد).
/// </summary>
public class ApprovalRequest : BaseEntity
{
    public ApprovalKind Kind { get; set; }

    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    /// <summary>خلاصهٔ قابل‌خواندن برای فهرست و اعلان.</summary>
    [MaxLength(500)]
    public string Summary { get; set; } = string.Empty;

    /// <summary>ورودی کار به شکل JSON (فقط سرور می‌خواند؛ هرگز مستقیم به کاربر نشان داده نمی‌شود).</summary>
    public string PayloadJson { get; set; } = "{}";

    /// <summary>مبلغ (تومان) برای نمایش و مرتب‌سازی؛ برای کارهای غیرمالی null.</summary>
    public decimal? Amount { get; set; }

    /// <summary>صفحهٔ مرتبط برای بررسی پیش از تأیید. مثال: «/admin/customers/…#wallet».</summary>
    [MaxLength(300)]
    public string? TargetUrl { get; set; }

    /// <summary>
    /// شناسهٔ موضوع درخواست برای جلوگیری از درخواست تکراری هم‌زمان. مثال: «withdrawal:3f2a…» یا «wallet:7b1c…».
    /// تا وقتی یک درخواست «در انتظار» برای همین موضوع هست، درخواست دوم ثبت نمی‌شود.
    /// </summary>
    [MaxLength(100)]
    public string? TargetKey { get; set; }

    public Guid RequestedById { get; set; }

    [MaxLength(150)]
    public string RequestedByName { get; set; } = string.Empty;

    public Guid? DecidedById { get; set; }

    [MaxLength(150)]
    public string? DecidedByName { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>توضیح تأییدکننده (برای رد کردن الزامی است). مثال: «مبلغ با فاکتور هم‌خوانی ندارد».</summary>
    [MaxLength(500)]
    public string? DecisionNote { get; set; }

    /// <summary>نتیجهٔ اجرای کار بعد از تأیید (پیام موفقیت یا خطا).</summary>
    [MaxLength(1000)]
    public string? ResultMessage { get; set; }

    /// <summary>بعد از این زمان درخواست «منقضی» می‌شود (پیش‌فرض ۷۲ ساعت).</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>اولویت کار شخصی.</summary>
public enum StaffTaskPriority : byte
{
    Low = 1,
    Normal = 2,
    High = 3,
}

/// <summary>
/// یک کار در فهرست شخصی کارمند. مثال: Title=«تماس با مشتری سفارش ۱۰۲۴ دربارهٔ تأخیر»، DueAt=فردا ۱۰ صبح،
/// Url=«/admin/orders/…». کارمند می‌تواند کار را برای همکارش هم بسازد (AssigneeId ≠ CreatedById) که به او اعلان می‌دهد.
/// </summary>
public class StaffTask : BaseEntity
{
    /// <summary>کارمندی که کار در فهرست اوست.</summary>
    public Guid AssigneeId { get; set; }

    public Guid CreatedById { get; set; }

    [MaxLength(150)]
    public string CreatedByName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Notes { get; set; }

    /// <summary>موعد انجام (اختیاری)؛ سر موعد یک یادآوری (اعلان پنل + پوش) فرستاده می‌شود.</summary>
    public DateTimeOffset? DueAt { get; set; }

    public StaffTaskPriority Priority { get; set; } = StaffTaskPriority.Normal;

    public bool IsDone { get; set; }

    public DateTimeOffset? DoneAt { get; set; }

    /// <summary>صفحهٔ مرتبط داخل پنل. مثال: «/admin/orders/3f2a…». فقط آدرس‌های /admin پذیرفته می‌شوند.</summary>
    [MaxLength(400)]
    public string? Url { get; set; }

    /// <summary>زمان ارسال یادآوری موعد (تا دوباره فرستاده نشود؛ با تغییر موعد پاک می‌شود).</summary>
    public DateTimeOffset? ReminderSentAt { get; set; }
}
