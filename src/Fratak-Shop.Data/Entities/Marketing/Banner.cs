using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Marketing;

public class Banner : BaseEntity
{
    [MaxLength(250)]
    public string? Title { get; set; }

    [MaxLength(500)]
    public string? Subtitle { get; set; }

    /// <summary>
    /// File name of the desktop image stored on the server.
    /// </summary>
    [Required]
    [MaxLength(250)]
    public string ImageName { get; set; } = null!;

    /// <summary>
    /// قدیمی: تصویر موبایل جدا. دور ۲۹ (خواستهٔ صریح کاربر): بنر نسخهٔ موبایل جدا ندارد؛ همین یک تصویر در موبایل بهتر چیده می‌شود.
    /// ستون فقط برای سازگاری پایگاه داده مانده و همیشه خالی است (مهاجرت دور ۲۹ مقدارهای قبلی را پاک کرد).
    /// </summary>
    [MaxLength(250)]
    public string? MobileImageName { get; set; }

    [MaxLength(250)]
    public string? ImageAlt { get; set; }

    [MaxLength(100)]
    public string? ButtonText { get; set; }

    [MaxLength(500)]
    public string? LinkUrl { get; set; }

    /// <summary>
    /// دور ۲۶ – نسخهٔ «زنده» (متحرک) بنر: HTML امن ساخته‌شده از طرح (متن رمزگذاری‌شده، رنگ‌های اعتبارسنجی‌شده، فقط تصویرهای محلی).
    /// null ← همان تصویر WebP نشان داده می‌شود. فقط سامانه (DesignLive) آن را می‌سازد؛ ورودی آزاد کاربر نیست.
    /// </summary>
    public string? LiveHtml { get; set; }

    /// <summary>
    /// Place of the banner on the storefront.
    /// </summary>
    public BannerPosition Position { get; set; } = BannerPosition.HeroSlider;

    public int DisplayOrder { get; set; }

    public DateTimeOffset? StartAt { get; set; }

    public DateTimeOffset? EndAt { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// دور ۲۹ (#۱۶): بنر «سنجاق‌شده» همیشه در جایگاهش دیده می‌شود و در چرخش نوبتی بیرون نمی‌رود.
    /// مثال: سقف اسلایدر ۵ و ۸ بنر فعال (۲ سنجاق) ← ۲ سنجاق همیشه + ۳ تا از ۶ بنر دیگر به نوبت (هر چند ساعت).
    /// </summary>
    public bool IsPinned { get; set; }

    /// <summary>
    /// دور ۲۳: کالای اصلی بنر (اختیاری). کالا ناموجود یا غیرفعال شود ← بنر خودکار نمایش داده نمی‌شود (پنهان، نه حذف) تا مشتری به
    /// کالای ناموجود نرسد. مثال: بنر «پاوربانک ۲۰ هزار با ۱۵٪ تخفیف».
    /// </summary>
    public Guid? ProductId { get; set; }

    /// <summary>دور ۲۳: دستهٔ بنر «سربرگ دسته» (Position = CategoryHeader)؛ بالای صفحهٔ همان دسته.</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>
    /// دور ۲۳ – آزمون A/B: بنرهای هم‌گروه (همان مقدار) در یک جایگاه به نوبت نمایش داده می‌شوند و نرخ کلیک (Clicks ÷ Views) مقایسه می‌شود.
    /// خالی = بنر عادی. مثال: «yalda-1405» برای سه نسخهٔ طراحی یک ایده.
    /// </summary>
    [MaxLength(60)]
    public string? VariantGroup { get; set; }

    /// <summary>دور ۲۳: تعداد نمایش (تقریبی؛ گروهی در حافظه جمع و دوره‌ای در پایگاه داده افزوده می‌شود).</summary>
    public int Views { get; set; }

    /// <summary>دور ۲۳: تعداد کلیک (از مسیر /b/{id} که بعد از شمارش به لینک بنر می‌رود).</summary>
    public int Clicks { get; set; }
}

public enum BannerPosition
{
    None = 0,
    HeroSlider = 1,       // اسلایدر اصلی صفحه نخست
    WideBanner = 2,       // بنر تمام‌عرض میان صفحه نخست
    PromoCard = 3,        // کارت‌های تبلیغاتی دوتایی صفحه نخست
    CategoryHeader = 4,   // دور ۲۳: سربرگ صفحهٔ دسته (۱۹۲۰×۳۰۰)
}
