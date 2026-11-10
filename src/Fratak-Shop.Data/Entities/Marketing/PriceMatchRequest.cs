using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Marketing;

/// <summary>وضعیت درخواست «قیمت کمتر دیدید؟».</summary>
public enum PriceMatchStatus : byte
{
    /// <summary>در انتظار بررسی.</summary>
    Pending = 1,

    /// <summary>تأیید شد و کد تخفیف شخصی ساخته شد.</summary>
    Approved = 2,

    /// <summary>رد شد (مثلاً فروشنده معتبر نبود یا کالا یکی نبود).</summary>
    Rejected = 3,
}

/// <summary>
/// دور ۲۴ (تضمین قیمت، پیشنهاد ۱۱): مشتری می‌گوید همین کالا را جای دیگر ارزان‌تر دیده است.
/// مثال: کالای ۱٬۲۰۰٬۰۰۰ تومانی، قیمت رقیب ۱٬۱۵۰٬۰۰۰ ← اگر مدیر تأیید کند یک کد تخفیف ۵۰٬۰۰۰ تومانیِ شخصی و کوتاه‌مدت ساخته می‌شود.
/// <para>محل استفاده: Core/Services/Marketing/PriceMatchService.cs، صفحهٔ کالا و پنل مدیریت (بازاریابی ← تضمین قیمت).</para>
/// </summary>
public class PriceMatchRequest : BaseEntity
{
    /// <summary>مشتری (فقط کاربر واردشده می‌تواند درخواست بدهد).</summary>
    public Guid UserId { get; set; }

    /// <summary>کالا.</summary>
    public Guid ProductId { get; set; }

    /// <summary>نام کالا در لحظهٔ درخواست (برای نمایش در پنل حتی اگر کالا بعداً عوض شود).</summary>
    [Required]
    [MaxLength(300)]
    public string ProductName { get; set; } = null!;

    /// <summary>نشانی صفحهٔ رقیب (فقط http/https).</summary>
    [Required]
    [MaxLength(500)]
    public string CompetitorUrl { get; set; } = null!;

    /// <summary>قیمتی که مشتری دیده (تومان).</summary>
    public decimal CompetitorPrice { get; set; }

    /// <summary>قیمت نهایی ما در لحظهٔ درخواست (تومان).</summary>
    public decimal OurPrice { get; set; }

    /// <summary>وضعیت.</summary>
    public PriceMatchStatus Status { get; set; } = PriceMatchStatus.Pending;

    /// <summary>یادداشت مدیر (دلیل رد یا توضیح) که به مشتری هم نشان داده می‌شود.</summary>
    [MaxLength(500)]
    public string? AdminNote { get; set; }

    /// <summary>کد تخفیف شخصی ساخته‌شده پس از تأیید.</summary>
    [MaxLength(50)]
    public string? CouponCode { get; set; }

    /// <summary>زمان بررسی.</summary>
    public DateTimeOffset? ReviewedAt { get; set; }

    /// <summary>نام بررسی‌کننده.</summary>
    [MaxLength(80)]
    public string? ReviewerName { get; set; }
}
