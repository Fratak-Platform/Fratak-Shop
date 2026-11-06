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

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }
}
