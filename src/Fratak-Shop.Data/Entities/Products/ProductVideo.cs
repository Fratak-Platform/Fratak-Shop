using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Products;

/// <summary>
/// Video of a product shown in the gallery: an uploaded file (/uploads/videos), an Aparat
/// video (embedded) or a direct https link to an mp4/webm file (e.g. a CDN / download host).
/// </summary>
public class ProductVideo : BaseEntity
{
    public Guid ProductId { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }

    public ProductVideoProvider Provider { get; set; }

    /// <summary>
    /// Upload: file name under /uploads/videos. Aparat: video hash or link. DirectUrl: https link.
    /// </summary>
    [Required]
    [MaxLength(1000)]
    public string Source { get; set; } = null!;

    /// <summary>Poster image (file name under /uploads/products or an https link).</summary>
    [MaxLength(1000)]
    public string? PosterImage { get; set; }

    public int? DurationSeconds { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;
}

public enum ProductVideoProvider
{
    None = 0,
    Upload = 1,     // فایل آپلودشده روی سایت
    Aparat = 2,     // آپارات (embed)
    DirectUrl = 3,  // لینک مستقیم mp4 / webm
}
