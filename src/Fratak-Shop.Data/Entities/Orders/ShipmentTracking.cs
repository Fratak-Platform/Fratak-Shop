using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Orders;

// ═══════════════════════════════════════════════════════════════════════════
//  رهگیری مرسوله (مرحلهٔ هفتم – فاز ۶)
//  وقتی کارمند کد رهگیری را ثبت می‌کند، شرکت حمل را هم انتخاب می‌کند. برای «پست» سیستم چند بار
//  در روز وضعیت مرسوله را از سامانهٔ عمومی رهگیری پست می‌خواند، رویدادها را ذخیره می‌کند و
//  با رسیدن «تحویل به گیرنده» سفارش را خودکار «تحویل‌شده» می‌کند.
//  موتور: Core/Services/Orders/ShipmentTrackingService.cs
//  زمان‌بندی: Web/Infrastructure/Shipping/PostTrackingInfrastructure.cs
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// شرکت حمل مرسوله. فقط «پست» رهگیری خودکار دارد؛ بقیه فقط برای نمایش نام و لینک به مشتری هستند.
/// <para>عدد هر مقدار در پایگاه داده ذخیره می‌شود؛ ترتیب را عوض نکنید و مقدار جدید را با عدد تازه اضافه کنید.</para>
/// </summary>
public enum ShippingCarrier
{
    /// <summary>پست جمهوری اسلامی ایران (رهگیری خودکار).</summary>
    IranPost = 1,

    /// <summary>تیپاکس (فقط نمایش لینک رهگیری سایت تیپاکس).</summary>
    Tipax = 2,

    /// <summary>چاپار.</summary>
    Chapar = 3,

    /// <summary>پیک فروشگاه / پیک موتوری.</summary>
    Courier = 4,

    /// <summary>سایر شرکت‌های حمل.</summary>
    Other = 9,
}

/// <summary>وضعیت کلی رهگیری یک مرسوله.</summary>
public enum ShipmentTrackingState
{
    /// <summary>هنوز بررسی نشده (تازه ثبت شده).</summary>
    Pending = 0,

    /// <summary>مرسوله در مسیر است (حداقل یک رویداد دیده شده).</summary>
    InTransit = 1,

    /// <summary>در حال توزیع (به نامه‌رسان/موزع تحویل شده).</summary>
    OutForDelivery = 2,

    /// <summary>به گیرنده تحویل شده؛ بررسی متوقف می‌شود.</summary>
    Delivered = 3,

    /// <summary>به فرستنده برگشت خورده؛ بررسی متوقف و به کارکنان هشدار داده می‌شود.</summary>
    Returned = 4,

    /// <summary>سامانهٔ پست این کد را نمی‌شناسد (هنوز ثبت نشده یا کد اشتباه است).</summary>
    NotFound = 5,

    /// <summary>بررسی متوقف شد (کهنه شدن، سفارش لغو شد یا رهگیری خودکار ندارد).</summary>
    Stopped = 6,
}

/// <summary>
/// رهگیری یک سفارش (حداکثر یکی برای هر سفارش). کد رهگیری اصلی همچنان در <see cref="Order.TrackingCode"/> است
/// و این جدول اطلاعات رهگیری خودکار را نگه می‌دارد.
/// <para>مثال: سفارش FR-1024، پست، کد ۲۴ رقمی، وضعیت «در مسیر»، آخرین رویداد «ورود به مرکز مبادلات تهران».</para>
/// </summary>
public class ShipmentTracking : BaseEntity
{
    public Guid OrderId { get; set; }

    public Order Order { get; set; } = null!;

    public ShippingCarrier Carrier { get; set; } = ShippingCarrier.IranPost;

    /// <summary>کد رهگیری (برای پست فقط رقم‌ها). مثال: 123456789012345678901234</summary>
    [Required]
    [MaxLength(100)]
    public string Code { get; set; } = null!;

    public ShipmentTrackingState State { get; set; }

    /// <summary>
    /// شروع رهگیری همین کد (با تغییر کد از نو شروع می‌شود). ملاک «حداکثر روزهای رهگیری» در تنظیمات.
    /// </summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>متن آخرین رویداد. مثال: «ورود به مرکز مبادلات»</summary>
    [MaxLength(300)]
    public string? LastStatus { get; set; }

    /// <summary>محل آخرین رویداد. مثال: «مرکز مبادلات تهران»</summary>
    [MaxLength(200)]
    public string? LastLocation { get; set; }

    /// <summary>زمان آخرین رویداد (اگر از متن تاریخ قابل خواندن بود).</summary>
    public DateTimeOffset? LastEventAt { get; set; }

    /// <summary>آخرین باری که سامانهٔ پست خوانده شد (موفق یا ناموفق).</summary>
    public DateTimeOffset? LastCheckedAt { get; set; }

    /// <summary>آخرین باری که پاسخ درست از سامانهٔ پست گرفته شد.</summary>
    public DateTimeOffset? LastSuccessAt { get; set; }

    /// <summary>تعداد کل دفعات بررسی.</summary>
    public int CheckCount { get; set; }

    /// <summary>تعداد خطاهای پشت سر هم (با اولین پاسخ درست صفر می‌شود).</summary>
    public int FailureCount { get; set; }

    /// <summary>آخرین پیام خطا. مثال: «سامانهٔ پست فقط از IP ایران پاسخ می‌دهد».</summary>
    [MaxLength(500)]
    public string? LastError { get; set; }

    /// <summary>زمان تحویل به گیرنده طبق سامانهٔ پست.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }

    /// <summary>آیا سفارش با همین رهگیری خودکار «تحویل‌شده» شد (برای نمایش در پنل).</summary>
    public bool AutoDelivered { get; set; }

    /// <summary>اطلاعات کلی مرسوله از سامانهٔ پست (مبدأ، مقصد، وزن، ...) به صورت «عنوان: مقدار» در هر خط.</summary>
    [MaxLength(2000)]
    public string? ParcelInfo { get; set; }

    public List<ShipmentEvent> Events { get; set; } = [];
}

/// <summary>
/// یک رویداد از تاریخچهٔ مرسوله که از سامانهٔ رهگیری خوانده شده.
/// <para>مثال: «۱۴۰۵/۰۸/۰۳ – ۱۰:۴۵ · قبول مرسوله · باجهٔ معالی‌آباد شیراز».</para>
/// </summary>
public class ShipmentEvent : BaseEntity
{
    public Guid ShipmentTrackingId { get; set; }

    public ShipmentTracking Tracking { get; set; } = null!;

    /// <summary>زمان رویداد (اگر تاریخ و ساعت قابل خواندن بود). برای مرتب‌سازی و نمایش شمسی.</summary>
    public DateTimeOffset? OccurredAt { get; set; }

    /// <summary>متن خام تاریخ/ساعت همان‌طور که سامانه نوشته (اگر تبدیل ممکن نبود همین نمایش داده می‌شود).</summary>
    [MaxLength(100)]
    public string? RawTime { get; set; }

    [Required]
    [MaxLength(300)]
    public string Status { get; set; } = null!;

    [MaxLength(200)]
    public string? Location { get; set; }

    /// <summary>ترتیب رویداد در پاسخ سامانه (۰ = قدیمی‌ترین).</summary>
    public int Sequence { get; set; }

    /// <summary>اثر انگشت رویداد (تاریخ+ساعت+متن+محل) برای جلوگیری از ذخیرهٔ تکراری در بررسی‌های بعدی.</summary>
    [Required]
    [MaxLength(64)]
    public string Fingerprint { get; set; } = null!;
}
