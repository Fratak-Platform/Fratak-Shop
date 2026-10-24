using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Products;

/// <summary>
/// Makes a product specification (<see cref="ProductFeature.Key"/>) filterable on the
/// listing pages, e.g. "رنگ", "حافظه داخلی". The admin decides which specifications are
/// offered as filters, globally or for one category (and its sub categories).
/// </summary>
public class ProductFilterAttribute : BaseEntity
{
    /// <summary>Title of the filter box.</summary>
    [Required]
    [MaxLength(150)]
    public string Title { get; set; } = null!;

    /// <summary>Specification key to filter on (must match <see cref="ProductFeature.Key"/>).</summary>
    [Required]
    [MaxLength(250)]
    public string FeatureKey { get; set; } = null!;

    /// <summary>Latin URL key of the filter (?f-color=…), unique.</summary>
    [Required]
    [MaxLength(50)]
    public string Slug { get; set; } = null!;

    /// <summary>Only offered on this category (and its children); null = everywhere.</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>
    /// Shown on listings without a selected category (only for filters that make sense
    /// for every product, e.g. colour). Category specific filters stay hidden there.
    /// </summary>
    public bool ShowWithoutCategory { get; set; }

    /// <summary>Maximum number of values shown in the box.</summary>
    public int MaxValues { get; set; } = 30;

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    [ForeignKey(nameof(CategoryId))]
    public Category? Category { get; set; }
}
