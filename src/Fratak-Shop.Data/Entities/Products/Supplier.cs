using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Products;

/// <summary>Store / website the shop buys products from. Internal data, never shown on the storefront.</summary>
public class Supplier : BaseEntity
{
    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Website { get; set; }

    [MaxLength(150)]
    public string? ContactName { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }

    [MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    public List<Product> Products { get; set; } = [];
}
