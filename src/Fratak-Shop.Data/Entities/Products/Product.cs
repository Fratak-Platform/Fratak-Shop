using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Favorites;
using Fratak_Shop.Data.Utilities;
using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Data.Entities.Products;

/// <summary>
/// A single (non-variant) product. All sale, stock and warranty data belongs to
/// the product itself; extra specifications are stored in the
/// <see cref="ProductFeature"/> dictionary.
/// </summary>
public class Product : BaseEntity
{
    public Guid CategoryId { get; set; }

    [Required]
    [MaxLength(250)]
    public string Name { get; set; } = null!;

    [Required]
    [MaxLength(250)]
    public string Slug { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string SKU { get; set; } = null!;

    [MaxLength(1000)]
    public string? ShortDescription { get; set; }

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public decimal? DiscountPrice { get; set; }

    public DateTimeOffset? DiscountStartAt { get; set; }

    public DateTimeOffset? DiscountEndAt { get; set; }

    public int StockQuantity { get; set; }

    [MaxLength(250)]
    public string? WarrantyTitle { get; set; }

    public int? WarrantyMonths { get; set; }

    public SeoMetadata? Seo { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? PublishedAt { get; set; }

    [ForeignKey(nameof(CategoryId))]
    public Category Category { get; set; } = null!;

    public List<ProductImage> Images { get; set; } = [];

    public List<ProductFeature> Features { get; set; } = [];

    public List<Comment> Comments { get; set; } = [];

    public List<Favorite> Favorites { get; set; } = [];
}
