using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Utilities;
using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Data.Entities.Brands;

/// <summary>
/// Product manufacturer / brand. Used by the storefront brand slider, the
/// brand filter of the product listing and the brand line of a product page.
/// </summary>
public class Brand : BaseEntity
{
    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Latin name of the brand, e.g. "Samsung".
    /// </summary>
    [MaxLength(150)]
    public string? EnglishName { get; set; }

    [Required]
    [MaxLength(150)]
    public string Slug { get; set; } = null!;

    /// <summary>
    /// File name of the brand logo stored on the server.
    /// </summary>
    [MaxLength(250)]
    public string? LogoName { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public SeoMetaData? Seo { get; set; }

    public List<Product> Products { get; set; } = [];
}
