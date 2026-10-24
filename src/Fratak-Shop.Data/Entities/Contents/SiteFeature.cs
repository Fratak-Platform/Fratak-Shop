using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Contents;

/// <summary>
/// "Why shop with us" item (fast delivery, genuine products, support, ...)
/// shown on the home page and the product page.
/// </summary>
public class SiteFeature : BaseEntity
{
    [Required]
    [MaxLength(150)]
    public string Title { get; set; } = null!;

    [MaxLength(300)]
    public string? Description { get; set; }

    /// <summary>Icon key of the storefront icon set (e.g. Truck, Shield, Headset).</summary>
    [MaxLength(50)]
    public string? IconKey { get; set; }

    [MaxLength(500)]
    public string? LinkUrl { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }
}
