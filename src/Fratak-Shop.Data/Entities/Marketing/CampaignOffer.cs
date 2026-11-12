using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Marketing;

/// <summary>وضعیت پیشنهاد کمپین. پیش‌نویس ← (انتشار بنر) فعال ← (پایان زمان) تمام‌شده؛ یا لغوشده.</summary>
public enum CampaignOfferStatus : byte
{
    Draft = 1,
    Active = 2,
    Ended = 3,
    Cancelled = 4,
}

/// <summary>دامنهٔ پیشنهاد: کل فروشگاه، یک دسته، یک برند یا یک کالا.</summary>
public enum CampaignOfferScope : byte
{
    All = 0,
    Category = 1,
    Brand = 2,
    Product = 3,
}

/// <summary>
/// دور ۲۴ (بنر هوشمند): «قاعدهٔ واقعی» پشت بنر کمپین. مدیر در «ایده ← طرح» می‌نویسد مثلاً «۲۰٪ تخفیف هدفون برای یلدا با امتیاز دوبرابر»؛
/// سه طرح بنر ساخته می‌شود و هم‌زمان یک پیش‌نویس از همین قاعده (کد تخفیف غیرفعال + ضریب امتیاز) ذخیره می‌شود.
/// فقط وقتی مدیر یکی از طرح‌ها را منتشر کند، قاعده هم فعال می‌شود (اتمیک، در همان ذخیره) تا ادعای بنر همیشه راست باشد.
/// <para>اتصال به طرح‌ها با <see cref="VariantGroup"/> (همان گروه A/B نسخه‌های یک ایده، مثل «brief:-:1a2b3c4d»).</para>
/// <para>محل استفاده: Core/Services/Marketing/CampaignOfferService.cs، Core/Services/Design/DesignService.PublishBannerAsync،
/// Core/Services/Loyalty/LoyaltyService (ضریب امتیاز خرید)، صفحهٔ «استودیوی طراحی».</para>
/// </summary>
public class CampaignOffer : BaseEntity
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    /// <summary>گروه نسخه‌های طرح (DesignBrief.VariantGroup).</summary>
    [Required]
    [MaxLength(80)]
    public string VariantGroup { get; set; } = null!;

    public CampaignOfferScope Scope { get; set; }

    /// <summary>شناسهٔ دسته/برند/کالا (برای All خالی).</summary>
    public Guid? ScopeId { get; set; }

    /// <summary>نام دامنه برای نمایش (مثلاً «هدفون»).</summary>
    [MaxLength(200)]
    public string? ScopeName { get; set; }

    /// <summary>درصد تخفیف (۱ تا ۵۰)؛ خالی یعنی تخفیف ندارد.</summary>
    public int? DiscountPercent { get; set; }

    /// <summary>ضریب امتیاز باشگاه برای خریدهای این دامنه (۲ تا ۵)؛ خالی یعنی ندارد.</summary>
    public decimal? PointsMultiplier { get; set; }

    /// <summary>کوپن ساخته‌شده برای تخفیف (تا فعال شدن، غیرفعال است).</summary>
    public Guid? CouponId { get; set; }

    [MaxLength(50)]
    public string? CouponCode { get; set; }

    /// <summary>مدت کمپین بعد از فعال شدن (روز).</summary>
    public int DurationDays { get; set; } = 7;

    public DateTimeOffset? StartsAt { get; set; }

    public DateTimeOffset? EndsAt { get; set; }

    public CampaignOfferStatus Status { get; set; } = CampaignOfferStatus.Draft;

    [MaxLength(150)]
    public string? CreatedByName { get; set; }

    [MaxLength(150)]
    public string? ActivatedByName { get; set; }
}
