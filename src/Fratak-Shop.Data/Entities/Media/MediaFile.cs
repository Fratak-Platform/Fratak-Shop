using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Media;

/// <summary>
/// یک فایل در «کتابخانهٔ رسانه» پنل (تصویرهایی که در متن محصول، مقاله و صفحات استفاده می‌شوند).
/// <para>
/// خود فایل در <c>wwwroot/uploads/media/</c> با نام تصادفی ذخیره می‌شود (بعد از بازسازی تصویر و حذف متادیتا)
/// و این ردیف اطلاعاتش را نگه می‌دارد تا بتوان جست‌وجو، انتخاب و حذف کرد.
/// </para>
/// محل استفاده: صفحهٔ <c>/admin/media</c> و دکمهٔ «تصویر از کتابخانه» در ویرایشگر متن.
/// </summary>
public class MediaFile : BaseEntity
{
    /// <summary>نام فایل روی دیسک، مثلاً <c>3fa2...e1.webp</c>.</summary>
    [Required]
    [MaxLength(100)]
    public string FileName { get; set; } = null!;

    /// <summary>آدرس عمومی، مثلاً <c>/uploads/media/3fa2...e1.webp</c>.</summary>
    [Required]
    [MaxLength(200)]
    public string Url { get; set; } = null!;

    /// <summary>نام اصلی فایل که کارمند بارگذاری کرد (فقط برای جست‌وجو و نمایش؛ هرگز برای ذخیره استفاده نمی‌شود).</summary>
    [MaxLength(200)]
    public string? OriginalName { get; set; }

    /// <summary>متن جایگزین پیش‌فرض (alt) برای سئو و دسترس‌پذیری.</summary>
    [MaxLength(200)]
    public string? AltText { get; set; }

    [MaxLength(50)]
    public string ContentType { get; set; } = "image/webp";

    public int SizeBytes { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    /// <summary>شناسهٔ کارمندی که بارگذاری کرد.</summary>
    public Guid? UploadedByUserId { get; set; }
}
