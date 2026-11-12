using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Products;

// ═══════════════════════════════════════════════════════════════════════════
//  ابزارهای کاتالوگ (مرحلهٔ هفتم – فاز ۵)
//   ۱) StockMovement      : دفتر گردش موجودی؛ هر کم و زیاد شدن موجودی یک ردیف (چه کسی، چرا، چقدر، مانده).
//   ۲) PriceChangeBatch   : هر بار «تغییر گروهی قیمت» یا «ورود از اکسل» یک دسته است تا بتوان کل آن را برگرداند.
//   ۳) PriceChangeItem    : مقدار قبلی و جدید هر محصول در آن دسته.
//  همهٔ این‌ها داخلی‌اند (فقط پنل مدیریت).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// دلیل تغییر موجودی. عددها ثابت‌اند چون در دیتابیس ذخیره می‌شوند؛ فقط به انتها اضافه کنید.
/// </summary>
public enum StockMovementReason
{
    /// <summary>تغییر خودکار سیستم بدون دلیل مشخص (مثلاً اسکریپت یا کار پس‌زمینه).</summary>
    System = 0,

    /// <summary>کارمند در پنل عدد موجودی را عوض کرده (فرم ویرایش، ویرایش سریع جدول).</summary>
    Manual = 1,

    /// <summary>ثبت سفارش (کم شدن موجودی).</summary>
    Sale = 2,

    /// <summary>لغو/حذف سفارش و برگشت موجودی رزروشده.</summary>
    OrderCancelled = 3,

    /// <summary>دریافت کالای مرجوعی و برگشت به انبار.</summary>
    Return = 4,

    /// <summary>بررسی خودکار تأمین‌کننده (ناموجود شدن خودکار یا تأیید پیشنهاد موجودی).</summary>
    Supplier = 5,

    /// <summary>ورود گروهی از فایل اکسل/CSV.</summary>
    Import = 6,

    /// <summary>ساخت محصول تازه با موجودی اولیه.</summary>
    Initial = 7,

    /// <summary>کالای سفارش ناموجود اعلام شد و موجودی سایت صفر شد.</summary>
    ItemUnavailable = 8,

    /// <summary>ویرایش اقلام یک سفارش توسط کارمند (افزودن/کم کردن تعداد).</summary>
    OrderEdit = 9,
}

/// <summary>
/// یک ردیف از دفتر گردش موجودی.
/// <para>
/// چطور پر می‌شود؟ دو راه: (۱) هر بار SaveChanges موجودی یک محصول را عوض کند، DbContext خودش ردیف می‌سازد
/// (دلیل را از <see cref="StockContext"/> یا وجود کارمند می‌فهمد)؛ (۲) جاهایی که با ExecuteUpdate مستقیم در SQL
/// موجودی را عوض می‌کنند (ثبت سفارش، لغو، مرجوعی) با <c>StockLedger.RecordAsync</c> ردیف را همان لحظه می‌نویسند.
/// </para>
/// کاربرد: صفحهٔ «انبار و موجودی» و کارت گردش موجودی در ویرایش محصول.
/// </summary>
public class StockMovement : BaseEntity
{
    public Guid ProductId { get; set; }

    /// <summary>مقدار تغییر؛ مثبت = اضافه شد، منفی = کم شد. مثال: فروش ۲ عدد → ‎-2.</summary>
    public int Delta { get; set; }

    /// <summary>موجودی بعد از این تغییر (اگر معلوم باشد).</summary>
    public int? BalanceAfter { get; set; }

    public StockMovementReason Reason { get; set; }

    /// <summary>سفارش مربوط (برای فروش/لغو/مرجوعی)؛ کلید خارجی ندارد تا حذف سفارش دفتر را خراب نکند.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>متن مرجع کوتاه؛ مثلاً شمارهٔ سفارش «FR-1405-00012» یا نام فایل ورودی.</summary>
    [MaxLength(100)]
    public string? Reference { get; set; }

    public Guid? UserId { get; set; }

    [MaxLength(200)]
    public string? UserName { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public Product Product { get; set; } = null!;
}

/// <summary>منبع یک دستهٔ تغییر قیمت.</summary>
public enum PriceChangeSource
{
    /// <summary>ابزار «تغییر گروهی قیمت».</summary>
    BulkTool = 0,

    /// <summary>ورود از فایل اکسل/CSV.</summary>
    Import = 1,
}

/// <summary>
/// یک دستهٔ تغییر قیمت (مثلاً «۱۰٪ افزایش قیمت دستهٔ هدفون»).
/// <para>کاربرد: تاریخچهٔ ابزار تغییر گروهی قیمت و دکمهٔ «برگرداندن کل دسته».</para>
/// </summary>
public class PriceChangeBatch : BaseEntity
{
    /// <summary>خلاصهٔ خوانا از عملیات؛ مثال: «افزایش ۱۰٪ قیمت اصلی – گرد کردن به ۱٬۰۰۰ تومان».</summary>
    [MaxLength(500)]
    public string Title { get; set; } = null!;

    public PriceChangeSource Source { get; set; }

    /// <summary>تعداد محصولاتی که واقعاً عوض شدند.</summary>
    public int AffectedCount { get; set; }

    public Guid? CreatedByUserId { get; set; }

    [MaxLength(200)]
    public string? CreatedByName { get; set; }

    /// <summary>زمان برگرداندن (null یعنی هنوز برنگشته).</summary>
    public DateTimeOffset? RevertedAt { get; set; }

    [MaxLength(200)]
    public string? RevertedByName { get; set; }

    /// <summary>خلاصهٔ نتیجهٔ برگرداندن؛ مثال: «۴۸ محصول برگشت، ۲ محصول بعداً دستی ویرایش شده بود».</summary>
    [MaxLength(500)]
    public string? RevertNote { get; set; }

    public List<PriceChangeItem> Items { get; set; } = [];
}

/// <summary>
/// مقدار قبلی/جدید یک محصول در یک دستهٔ تغییر قیمت. فیلدی که عوض نشده، قبلی و جدیدش برابر است.
/// </summary>
public class PriceChangeItem : BaseEntity
{
    public Guid BatchId { get; set; }

    public Guid ProductId { get; set; }

    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }

    public decimal? OldDiscountPrice { get; set; }
    public decimal? NewDiscountPrice { get; set; }

    public DateTimeOffset? OldDiscountStartAt { get; set; }
    public DateTimeOffset? NewDiscountStartAt { get; set; }

    public DateTimeOffset? OldDiscountEndAt { get; set; }
    public DateTimeOffset? NewDiscountEndAt { get; set; }

    public decimal? OldPurchasePrice { get; set; }
    public decimal? NewPurchasePrice { get; set; }

    public PriceChangeBatch Batch { get; set; } = null!;

    public Product Product { get; set; } = null!;
}
