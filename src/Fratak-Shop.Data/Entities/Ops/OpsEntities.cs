using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Ops;

// ═══════════════════════════════════════════════════════════════════════════
//  عملیات فنی سایت (مرحلهٔ هفتم – فاز ۹)
//   • BackupRecord          : هر نسخهٔ پشتیبان (پایگاه داده یا فایل‌های آپلود) با وضعیت، حجم و مسیر فایل.
//   • IpRule                : مسدود یا مجاز کردن یک IP یا بازه (CIDR)، دستی یا خودکار (تلاش‌های ناموفق ورود).
//   • BotRule               : مسدود کردن ربات‌ها بر اساس بخشی از User-Agent (مثلاً «sqlmap»).
//   • BackgroundJobSetting  : توقف/ادامهٔ هر کار پس‌زمینه (نام کار همان نام صفحهٔ «کارهای پس‌زمینه» است).
//   • AlertRule / AlertEvent: قانون‌های هشدار (مثلاً «بیش از ۲۰ خطا در ساعت») و تاریخچهٔ هشدارهای ارسال‌شده.
//  منطق: Core/Services/Ops؛ زمان‌بندی، middleware و لاگ: Web/Infrastructure/Ops؛ پنل: /admin/system/*.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>نوع نسخهٔ پشتیبان. عددها در دیتابیس ذخیره می‌شوند؛ تغییر ندهید.</summary>
public enum BackupKind : byte
{
    /// <summary>فایل .bak کامل پایگاه داده (دستور BACKUP DATABASE خود SQL Server).</summary>
    Database = 1,

    /// <summary>فایل zip پوشهٔ wwwroot/uploads (تصویرها و فایل‌های بارگذاری‌شده).</summary>
    Uploads = 2,
}

/// <summary>وضعیت یک نسخهٔ پشتیبان.</summary>
public enum BackupStatus : byte
{
    Running = 1,
    Succeeded = 2,
    Failed = 3,

    /// <summary>طبق «تعداد نسخه‌های نگه‌داشته» کنار گذاشته و فایلش پاک شد.</summary>
    Removed = 4,
}

/// <summary>
/// یک نسخهٔ پشتیبان. مثال: Kind=Database، FilePath=«/var/opt/mssql/backups/fratak-db-20261026-0330.bak»، SizeBytes=52428800.
/// مسیر فایل پایگاه داده روی سرور SQL Server است (ممکن است با سرور سایت یکی نباشد)؛ IsLocal می‌گوید سایت به فایل دسترسی دارد یا نه.
/// </summary>
public class BackupRecord : BaseEntity
{
    public BackupKind Kind { get; set; }

    public BackupStatus Status { get; set; } = BackupStatus.Running;

    /// <summary>true = اجرای دستی از پنل؛ false = نوبت زمان‌بندی‌شده.</summary>
    public bool IsManual { get; set; }

    /// <summary>نام کارمندی که اجرای دستی را زد. مثال: «مریم احمدی».</summary>
    [MaxLength(150)]
    public string? StartedBy { get; set; }

    /// <summary>کلید نوبت زمان‌بندی (برای جلوگیری از اجرای تکراری با چند سرور). مثال: «2026-10-26-03».</summary>
    [MaxLength(40)]
    public string? SlotKey { get; set; }

    [MaxLength(600)]
    public string? FilePath { get; set; }

    public long? SizeBytes { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>صحت فایل با RESTORE VERIFYONLY بررسی شد.</summary>
    public bool IsVerified { get; set; }

    /// <summary>سرور سایت به فایل دسترسی مستقیم دارد (دانلود و پاک کردن از پنل ممکن است).</summary>
    public bool IsLocal { get; set; }

    /// <summary>علت خطا یا توضیح. مثال: «پوشه وجود ندارد یا SQL Server اجازهٔ نوشتن ندارد».</summary>
    [MaxLength(1000)]
    public string? Message { get; set; }
}

/// <summary>کار قانون IP.</summary>
public enum IpRuleAction : byte
{
    /// <summary>همهٔ درخواست‌های این IP با خطای ۴۰۳ رد می‌شوند.</summary>
    Block = 1,

    /// <summary>هرگز مسدود نشود (حتی با مسدودسازی خودکار)؛ مثلاً IP دفتر فروشگاه.</summary>
    Allow = 2,
}

/// <summary>
/// قانون یک IP یا بازه. مثال: Value=«185.10.20.0/24»، Action=Block، Reason=«حملهٔ حدس رمز»، ExpiresAt=فردا.
/// قانون منقضی‌شده دیگر اثری ندارد و روزانه پاک می‌شود.
/// </summary>
public class IpRule : BaseEntity
{
    /// <summary>IP تکی (IPv4 یا IPv6) یا بازهٔ CIDR. مثال: «5.6.7.8» یا «2a01:4f8::/32».</summary>
    [MaxLength(64)]
    public string Value { get; set; } = null!;

    public IpRuleAction Action { get; set; } = IpRuleAction.Block;

    [MaxLength(300)]
    public string? Reason { get; set; }

    /// <summary>خالی = دائمی.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>با مسدودسازی خودکار (تلاش‌های ناموفق ورود) ساخته شده است.</summary>
    public bool IsAutomatic { get; set; }

    [MaxLength(150)]
    public string? CreatedBy { get; set; }

    /// <summary>تعداد درخواست‌های ردشده (تقریبی؛ هر چند دقیقه یک بار ذخیره می‌شود).</summary>
    public long Hits { get; set; }

    public DateTimeOffset? LastHitAt { get; set; }
}

/// <summary>
/// قانون ربات بر اساس User-Agent. مثال: Pattern=«sqlmap»، IsActive=true → هر درخواستی که User-Agent آن «sqlmap» دارد ۴۰۳ می‌گیرد.
/// موتورهای جست‌وجوی معتبر (گوگل، بینگ) را هرگز اینجا وارد نکنید.
/// </summary>
public class BotRule : BaseEntity
{
    /// <summary>بخشی از User-Agent (بدون حساسیت به حروف بزرگ و کوچک). حداقل ۳ نویسه.</summary>
    [MaxLength(120)]
    public string Pattern { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    [MaxLength(300)]
    public string? Reason { get; set; }

    public long Hits { get; set; }

    public DateTimeOffset? LastHitAt { get; set; }
}

/// <summary>
/// تنظیم یک کار پس‌زمینه. مثال: JobName=«موتور هوشمند سئو»، IsPaused=true → اجرای زمان‌بندی‌شده انجام نمی‌شود تا «ادامه» زده شود.
/// </summary>
public class BackgroundJobSetting : BaseEntity
{
    [MaxLength(150)]
    public string JobName { get; set; } = null!;

    public bool IsPaused { get; set; }

    public DateTimeOffset? PausedAt { get; set; }

    [MaxLength(150)]
    public string? PausedBy { get; set; }
}

/// <summary>شاخصی که قانون هشدار می‌سنجد. عددها در دیتابیس ذخیره می‌شوند؛ تغییر ندهید.</summary>
public enum AlertMetric : byte
{
    /// <summary>تعداد خطاهای ثبت‌شده در یک ساعت اخیر.</summary>
    ErrorsPerHour = 1,

    /// <summary>درصد پاسخ‌های ۵۰۰ در ۵ دقیقهٔ اخیر.</summary>
    ServerErrorPercent = 2,

    /// <summary>درصد فضای آزاد دیسک (هشدار وقتی کمتر شود).</summary>
    DiskFreePercent = 3,

    /// <summary>زمان پاسخ پایگاه داده (میلی‌ثانیه).</summary>
    DatabaseLatencyMs = 4,

    /// <summary>تعداد کارهای پس‌زمینه‌ای که آخرین اجرایشان ناموفق بوده یا گیر کرده‌اند.</summary>
    FailingJobs = 5,

    /// <summary>ساعت‌های گذشته از آخرین پشتیبان موفق پایگاه داده.</summary>
    HoursSinceBackup = 6,

    /// <summary>تراکنش‌های پرداختی که بیش از ۲ ساعت معلق مانده‌اند.</summary>
    StuckPayments = 7,

    /// <summary>مصرف حافظهٔ برنامه (مگابایت).</summary>
    MemoryMb = 8,

    /// <summary>تلاش‌های ناموفق ورود در یک ساعت اخیر (نشانهٔ حملهٔ حدس رمز).</summary>
    FailedLoginsPerHour = 9,

    /// <summary>میانگین زمان پاسخ صفحه‌ها در ۵ دقیقهٔ اخیر (میلی‌ثانیه).</summary>
    AverageResponseMs = 10,
}

/// <summary>
/// قانون هشدار. مثال: Metric=ErrorsPerHour، Threshold=20 → وقتی در یک ساعت ۲۰ خطا یا بیشتر ثبت شود، به کارکنان فنی اعلان (پنل + پوش) می‌رود.
/// برای DiskFreePercent جهت برعکس است (کمتر از آستانه = هشدار). بعد از هر هشدار تا CooldownMinutes دوباره هشدار نمی‌دهد.
/// </summary>
public class AlertRule : BaseEntity
{
    public AlertMetric Metric { get; set; }

    public double Threshold { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>فاصلهٔ حداقل بین دو هشدار همین قانون (دقیقه). مثال: ۶۰.</summary>
    public int CooldownMinutes { get; set; } = 60;

    /// <summary>هشدار ایمیلی/بله هم از طریق کانال‌های «گزارش خودکار» ارسال شود.</summary>
    public bool SendExternal { get; set; }

    public DateTimeOffset? LastTriggeredAt { get; set; }

    /// <summary>آخرین مقدار سنجیده‌شده (برای نمایش در پنل).</summary>
    public double? LastValue { get; set; }

    public DateTimeOffset? LastCheckedAt { get; set; }

    /// <summary>الان در وضعیت هشدار است (برای اعلان «برطرف شد»).</summary>
    public bool IsFiring { get; set; }
}

/// <summary>یک هشدار ارسال‌شده یا برطرف‌شده. مثال: «۲۷ خطا در یک ساعت اخیر (آستانه: ۲۰)».</summary>
public class AlertEvent : BaseEntity
{
    public Guid RuleId { get; set; }

    public AlertMetric Metric { get; set; }

    public double Value { get; set; }

    public double Threshold { get; set; }

    /// <summary>true = برطرف شد؛ false = هشدار.</summary>
    public bool IsResolved { get; set; }

    [MaxLength(500)]
    public string Message { get; set; } = null!;
}
