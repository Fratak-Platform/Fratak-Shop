using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Insights;

// ═══════════════════════════════════════════════════════════════════════════
//  گزارش‌های مدیریتی مرحلهٔ هفتم (فاز ۷ب)
//  • Expense              : هزینه‌های جاری فروشگاه (هاست، دامنه، تبلیغات، حقوق …) با تکرار ماهانه/سالانه و یادآوری سررسید.
//  • SalesGoal            : هدف فروش هر ماه شمسی (مبلغ و تعداد سفارش) برای پیش‌بینی و نوار پیشرفت داشبورد.
//  • ScheduledReport      : گزارش خودکار روزانه/هفتگی/ماهانه که به ایمیل، بله یا تلگرام فرستاده می‌شود.
//  • ReportDeliveryConfig : تنظیمات کانال‌های ارسال (SMTP و توکن ربات‌ها، رمزنگاری‌شده) – فقط یک ردیف.
//  منطق: Core/Services/Insights/* ؛ صفحه‌ها: /admin/finance/expenses ، /admin/reports/goals ، /admin/reports/scheduled ، /admin/reports/staff.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>دستهٔ هزینه. برای افزودن دستهٔ تازه فقط یک مقدار جدید اضافه و عنوانش را در ExpenseRules.CategoryTitle بنویسید.</summary>
public enum ExpenseCategory
{
    None = 0,
    Hosting = 1,     // هاست و سرور
    Domain = 2,      // دامنه و SSL
    Marketing = 3,   // تبلیغات و بازاریابی
    Salary = 4,      // حقوق و دستمزد
    Software = 5,    // نرم‌افزار و سرویس‌ها (پیامک، درگاه …)
    Shipping = 6,    // هزینهٔ ارسال پرداخت‌شده توسط فروشگاه
    Packaging = 7,   // بسته‌بندی
    Rent = 8,        // اجاره
    Tax = 9,         // مالیات و عوارض
    BankFees = 10,   // کارمزد بانکی و درگاه
    Other = 99,      // سایر
}

/// <summary>تکرار هزینه. ماهانه یعنی هر ماه شمسی در همان روز (مثلاً هر ۵ام)؛ سالانه یعنی همان روز سال بعد.</summary>
public enum ExpenseRecurrence
{
    None = 0,
    Monthly = 1,
    Yearly = 2,
}

/// <summary>
/// یک هزینهٔ پرداخت‌شده. اگر تکراری باشد، <see cref="NextDueOn"/> تاریخ سررسید بعدی است؛ با «ثبت پرداخت این دوره»
/// یک هزینهٔ تازه (با <see cref="RecurringParentId"/> = همین ردیف) ساخته و سررسید یک دوره جلو می‌رود.
/// <para>مثال: «هاست سالانه» ۴٬۰۰۰٬۰۰۰ تومان، ۱۴۰۵/۰۲/۱۰، سالانه → سررسید بعدی ۱۴۰۶/۰۲/۱۰ و سه روز قبلش یادآوری.</para>
/// </summary>
public class Expense : BaseEntity
{
    [MaxLength(150)]
    public string Title { get; set; } = null!;

    public ExpenseCategory Category { get; set; } = ExpenseCategory.Other;

    /// <summary>مبلغ (تومان).</summary>
    public decimal Amount { get; set; }

    /// <summary>تاریخ پرداخت (روز ایران).</summary>
    public DateOnly SpentOn { get; set; }

    /// <summary>طرف حساب. مثال: «پارس‌پک».</summary>
    [MaxLength(150)]
    public string? Vendor { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }

    public ExpenseRecurrence Recurrence { get; set; }

    /// <summary>سررسید بعدی (فقط هزینه‌های تکراری فعال)؛ null یعنی تکرار متوقف شده یا تکراری نیست.</summary>
    public DateOnly? NextDueOn { get; set; }

    /// <summary>هزینهٔ تکراری که این ردیف از روی آن ثبت شده (برای تاریخچهٔ پرداخت‌های یک اشتراک).</summary>
    public Guid? RecurringParentId { get; set; }

    /// <summary>آخرین روزی که یادآوری سررسید فرستاده شد (تا هر روز فقط یک بار اعلان برود).</summary>
    public DateOnly? LastReminderOn { get; set; }

    public Guid? CreatedByUserId { get; set; }

    [MaxLength(100)]
    public string? CreatedByName { get; set; }
}

/// <summary>هدف فروش یک ماه شمسی. مثال: سال ۱۴۰۵، ماه ۸ (آبان)، ۵۰۰ میلیون تومان و ۳۰۰ سفارش.</summary>
public class SalesGoal : BaseEntity
{
    /// <summary>سال شمسی. مثال: ۱۴۰۵.</summary>
    public int Year { get; set; }

    /// <summary>ماه شمسی ۱ تا ۱۲.</summary>
    public int Month { get; set; }

    /// <summary>هدف مبلغ فروش خالص (تومان).</summary>
    public decimal TargetAmount { get; set; }

    /// <summary>هدف تعداد سفارش قطعی (اختیاری).</summary>
    public int? TargetOrders { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }

    [MaxLength(100)]
    public string? SetByName { get; set; }
}

public enum ReportFrequency
{
    None = 0,
    Daily = 1,     // هر روز در ساعت مشخص؛ دربارهٔ «دیروز»
    Weekly = 2,    // هر هفته در روز و ساعت مشخص؛ دربارهٔ ۷ روز گذشته
    Monthly = 3,   // روز اول هر ماه شمسی؛ دربارهٔ ماه قبل
}

/// <summary>کانال‌های ارسال گزارش (پرچم بیتی؛ چند کانال با هم).</summary>
[Flags]
public enum ReportChannels
{
    None = 0,
    Email = 1,
    Bale = 2,
    Telegram = 4,
}

/// <summary>بخش‌های گزارش خودکار (پرچم بیتی).</summary>
[Flags]
public enum ReportSections
{
    None = 0,
    Sales = 1,         // فروش، تعداد سفارش و مقایسه با دورهٔ قبل
    Orders = 2,        // کارهای مانده: سفارش منتظر تأیید، منتظر فاکتور خرید، آمادهٔ ارسال، درخواست برداشت
    Visitors = 4,      // بازدیدکننده، نرخ تبدیل و منبع برتر
    Stock = 8,         // کالاهای ناموجود و رو به اتمام
    Expenses = 16,     // هزینه‌ها و سود خالص
    Goal = 32,         // پیشرفت هدف ماه
    Support = 64,      // تیکت‌ها و پیام‌های بی‌پاسخ
    TopProducts = 128, // پرفروش‌ترین‌ها
    All = Sales | Orders | Visitors | Stock | Expenses | Goal | Support | TopProducts,
}

/// <summary>
/// یک گزارش زمان‌بندی‌شده. مثال: «خلاصهٔ روزانه» هر روز ساعت ۸ صبح به ایمیل مدیر و گروه بله.
/// </summary>
public class ScheduledReport : BaseEntity
{
    [MaxLength(100)]
    public string Title { get; set; } = null!;

    public ReportFrequency Frequency { get; set; } = ReportFrequency.Daily;

    /// <summary>ساعت ارسال به وقت ایران (۰ تا ۲۳).</summary>
    public byte Hour { get; set; } = 8;

    /// <summary>روز هفته برای گزارش هفتگی: ۰ = شنبه … ۶ = جمعه.</summary>
    public byte WeekDay { get; set; }

    public ReportChannels Channels { get; set; }

    public ReportSections Sections { get; set; } = ReportSections.All;

    /// <summary>گیرنده‌های ایمیل جدا با ویرگول. مثال: «owner@shop.ir, acc@shop.ir».</summary>
    [MaxLength(500)]
    public string? EmailTo { get; set; }

    /// <summary>شناسهٔ گفت‌وگوی بله (عدد یا @کانال).</summary>
    [MaxLength(64)]
    public string? BaleChatId { get; set; }

    /// <summary>شناسهٔ گفت‌وگوی تلگرام (عدد یا @کانال).</summary>
    [MaxLength(64)]
    public string? TelegramChatId { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>زمان اجرای بعدی (UTC)؛ بعد از هر اجرا دوباره محاسبه می‌شود.</summary>
    public DateTimeOffset? NextRunAt { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }

    public bool? LastSucceeded { get; set; }

    /// <summary>نتیجهٔ آخرین ارسال برای هر کانال. مثال: «ایمیل: ارسال شد · بله: توکن نامعتبر».</summary>
    [MaxLength(1000)]
    public string? LastStatus { get; set; }

    public Guid? CreatedByUserId { get; set; }
}

/// <summary>
/// تنظیمات کانال‌های ارسال گزارش (فقط یک ردیف). رمز SMTP و توکن ربات‌ها با ISecretProtector رمزنگاری ذخیره می‌شوند
/// و هرگز در فرم نمایش داده نمی‌شوند (فقط «ثبت شده/نشده»).
/// </summary>
public class ReportDeliveryConfig : BaseEntity
{
    [MaxLength(200)]
    public string? SmtpHost { get; set; }

    /// <summary>درگاه SMTP؛ ۴۶۵ = SSL مستقیم، ۵۸۷ = STARTTLS، ۲۵ = بدون رمزنگاری (توصیه نمی‌شود).</summary>
    public int SmtpPort { get; set; } = 587;

    [MaxLength(200)]
    public string? SmtpUserName { get; set; }

    [MaxLength(1000)]
    public string? SmtpPasswordProtected { get; set; }

    [MaxLength(200)]
    public string? FromAddress { get; set; }

    [MaxLength(100)]
    public string? FromName { get; set; }

    [MaxLength(1000)]
    public string? BaleTokenProtected { get; set; }

    [MaxLength(1000)]
    public string? TelegramTokenProtected { get; set; }

    // ── گیرندگان هشدارهای فنی (مرحلهٔ هفتم – فاز ۹؛ صفحهٔ /admin/system/alerts) ──
    // همان SMTP و توکن ربات‌های بالا استفاده می‌شود؛ فقط گیرنده‌ها جداست.

    /// <summary>ایمیل‌های گیرندهٔ هشدار با کاما. مثال: «tech@fratak.ir, owner@fratak.ir».</summary>
    [MaxLength(1000)]
    public string? AlertEmailTo { get; set; }

    /// <summary>شناسهٔ گفت‌وگوی بله برای هشدارها. مثال: «-1001234567890» یا «@fratak_tech».</summary>
    [MaxLength(100)]
    public string? AlertBaleChatId { get; set; }

    [MaxLength(100)]
    public string? AlertTelegramChatId { get; set; }
}
