using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Contents;

/// <summary>Trust seal shown in the footer (e-Namad, Samandehi, union membership, ...).</summary>
public class TrustBadge : BaseEntity
{
    [Required]
    [MaxLength(150)]
    public string Title { get; set; } = null!;

    [Required]
    [MaxLength(500)]
    public string ImageUrl { get; set; } = null!;

    [MaxLength(500)]
    public string? LinkUrl { get; set; }

    /// <summary>
    /// دور ۲۴ (پیشنهاد ۴۶): کد نماد چسبانده‌شده (مثلاً کد اینماد). فقط برای نمایش دوباره در پنل نگه داشته می‌شود و
    /// <b>هرگز خام در سایت چاپ نمی‌شود</b>؛ تصویر و لینک امن از آن برداشته و در <see cref="ImageUrl"/>/<see cref="LinkUrl"/> ذخیره می‌شوند
    /// (Core/Services/Contents/TrustBadgeEmbed).
    /// </summary>
    [MaxLength(2000)]
    public string? EmbedCode { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }
}
