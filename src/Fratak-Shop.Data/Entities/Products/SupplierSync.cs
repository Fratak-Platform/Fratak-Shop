using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Products;

// ═══════════════════════════════════════════════════════════════════════════
//  بررسی خودکار لینک تأمین‌کننده (مرحلهٔ هفتم – فاز ۵)
//  سه جدول:
//   ۱) SupplierProductState : آخرین وضعیت خوانده‌شدهٔ هر محصول از سایت منبع (یک ردیف برای هر محصول).
//   ۲) SupplierSyncRun      : خلاصهٔ هر بار اجرا (چند محصول، چند تغییر، چند خطا).
//   ۳) SupplierChange       : تغییرهای پیدا شده؛ یا خودکار اعمال شده‌اند (ناموجودی) یا منتظر تأیید کارمندند.
//  همهٔ این داده‌ها داخلی‌اند و هرگز در سایت نمایش داده نمی‌شوند.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>وضعیت یک کالا در سایت منبع.</summary>
public enum SupplierItemStatus
{
    Unknown = 0,

    /// <summary>موجود است (موجودی عددی ممکن است معلوم باشد یا نه).</summary>
    InStock = 1,

    /// <summary>ناموجود.</summary>
    OutOfStock = 2,

    /// <summary>صفحه یا محصول در منبع پیدا نشد (حذف شده یا لینک عوض شده).</summary>
    NotFound = 3,

    /// <summary>خطای شبکه/خواندن؛ وضعیت واقعی معلوم نیست و هیچ تغییری اعمال نمی‌شود.</summary>
    Error = 4,
}

/// <summary>
/// آخرین وضعیت خوانده‌شدهٔ یک محصول از سایت تأمین‌کننده.
/// <para>کاربرد: صفحهٔ «بررسی تأمین‌کننده» و کارت تأمین‌کننده در ویرایش محصول.</para>
/// </summary>
public class SupplierProductState : BaseEntity
{
    public Guid ProductId { get; set; }

    /// <summary>شناسهٔ محصول در سایت منبع (مثلاً id ووکامرس) تا دفعهٔ بعد بدون جست‌وجو و گروهی خوانده شود.</summary>
    [MaxLength(100)]
    public string? ExternalId { get; set; }

    /// <summary>لینکی که شناسهٔ بالا از آن به دست آمد؛ اگر کارمند لینک را عوض کند، شناسه دوباره پیدا می‌شود.</summary>
    [MaxLength(1000)]
    public string? ResolvedUrl { get; set; }

    /// <summary>نام کالا در سایت منبع (برای اطمینان کارمند از اینکه لینک درست است).</summary>
    [MaxLength(300)]
    public string? SourceName { get; set; }

    public SupplierItemStatus Status { get; set; }

    /// <summary>قیمت فروش منبع به تومان (همان قیمتی که ما می‌خریم).</summary>
    public decimal? SourcePrice { get; set; }

    /// <summary>موجودی عددی منبع اگر سایت اعلام کند (null = معلوم نیست).</summary>
    public int? SourceStock { get; set; }

    public DateTimeOffset? CheckedAt { get; set; }

    /// <summary>آخرین باری که محصول در منبع پیدا و خوانده شد (موجود یا ناموجود).</summary>
    public DateTimeOffset? LastSuccessAt { get; set; }

    /// <summary>تعداد خطاهای پشت‌سرهم؛ از ۳ به بعد در گزارش «مشکل‌ها» نمایش داده می‌شود.</summary>
    public int ConsecutiveErrors { get; set; }

    [MaxLength(500)]
    public string? LastError { get; set; }

    /// <summary>
    /// موجودی این محصول را سیستم (نه کارمند) صفر کرده است؛ پس با برگشت موجودی منبع، پیشنهاد «موجود شد» ساخته می‌شود.
    /// </summary>
    public bool AutoOutOfStock { get; set; }

    public Product Product { get; set; } = null!;
}

/// <summary>چه کسی اجرا را شروع کرد.</summary>
public enum SupplierSyncTrigger
{
    Scheduled = 1,
    Manual = 2,
}

/// <summary>خلاصهٔ یک بار اجرای بررسی تأمین‌کننده.</summary>
public class SupplierSyncRun : BaseEntity
{
    public SupplierSyncTrigger Trigger { get; set; }

    [MaxLength(150)]
    public string? StartedByName { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public int Checked { get; set; }
    public int InStock { get; set; }
    public int OutOfStock { get; set; }
    public int NotFound { get; set; }
    public int Errors { get; set; }

    /// <summary>تغییرهایی که خودکار اعمال شد (ناموجود کردن یا کم کردن موجودی).</summary>
    public int AutoApplied { get; set; }

    /// <summary>پیشنهادهای تازه‌ای که منتظر تأیید کارمندند.</summary>
    public int NewProposals { get; set; }

    /// <summary>متن خطای کلی (مثلاً قطع اینترنت) یا توضیح کوتاه.</summary>
    [MaxLength(1000)]
    public string? Message { get; set; }
}

/// <summary>نوع تغییر پیدا شده.</summary>
public enum SupplierChangeKind
{
    /// <summary>منبع ناموجود شد و موجودی ما صفر شد (خودکار).</summary>
    OutOfStock = 1,

    /// <summary>کالای ناموجود ما در منبع موجود شد؛ با تأیید، موجودی پیشنهادی ثبت می‌شود.</summary>
    BackInStock = 2,

    /// <summary>منبع موجودی بیشتری دارد؛ با تأیید، موجودی ما به عدد پیشنهادی می‌رسد.</summary>
    StockIncrease = 3,

    /// <summary>موجودی ما از منبع بیشتر بود و کم شد (خودکار، برای جلوگیری از فروش کالای ناموجود).</summary>
    StockDecrease = 4,

    /// <summary>قیمت منبع با قیمت خرید ثبت‌شدهٔ ما فرق دارد؛ با تأیید، قیمت خرید به‌روز می‌شود.</summary>
    PriceChange = 5,

    /// <summary>محصول در منبع پیدا نشد؛ لینک باید بررسی شود.</summary>
    NotFound = 6,

    /// <summary>چند بار پشت‌سرهم خواندن ناموفق بود.</summary>
    Error = 7,
}

/// <summary>وضعیت رسیدگی به یک تغییر.</summary>
public enum SupplierChangeStatus
{
    /// <summary>منتظر تأیید کارمند.</summary>
    Pending = 1,

    Approved = 2,
    Rejected = 3,

    /// <summary>سیستم خودش اعمال کرد (فقط برای کارهای ایمن: ناموجود کردن و کم کردن موجودی).</summary>
    AutoApplied = 4,

    /// <summary>پیش از رسیدگی، اجرای بعدی وضعیت تازه‌تری پیدا کرد و این مورد کنار رفت.</summary>
    Superseded = 5,
}

/// <summary>
/// یک تغییر پیدا شده در بررسی تأمین‌کننده.
/// <para>
/// برای هر محصول و هر نوع، حداکثر یک ردیف «منتظر تأیید» وجود دارد؛ اجرای بعدی همان ردیف را با عدد تازه به‌روز می‌کند
/// تا فهرست شلوغ نشود. اگر کارمند پیشنهادی را رد کند، همان عدد دوباره پیشنهاد نمی‌شود (فقط اگر عدد عوض شود).
/// </para>
/// مثال قیمت: قیمت خرید ثبت‌شده ۴۰۰٬۰۰۰ و قیمت منبع ۴۱۵٬۰۰۰ → OldValue=400000، NewValue=415000 (یعنی ۱۵٬۰۰۰+).
/// </summary>
public class SupplierChange : BaseEntity
{
    public Guid ProductId { get; set; }

    public Guid? RunId { get; set; }

    public SupplierChangeKind Kind { get; set; }

    public SupplierChangeStatus Status { get; set; } = SupplierChangeStatus.Pending;

    /// <summary>مقدار فعلی ما: موجودی (برای نوع‌های موجودی) یا قیمت خرید (برای تغییر قیمت).</summary>
    public decimal? OldValue { get; set; }

    /// <summary>مقدار پیشنهادی/اعمال‌شده: موجودی پیشنهادی یا قیمت تازهٔ منبع.</summary>
    public decimal? NewValue { get; set; }

    /// <summary>موجودی منبع در زمان پیدا شدن (اگر معلوم بود).</summary>
    public int? SourceStock { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public Guid? DecidedByUserId { get; set; }

    [MaxLength(150)]
    public string? DecidedByName { get; set; }

    public Product Product { get; set; } = null!;
}
