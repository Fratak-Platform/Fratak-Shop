using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Utilities;
using Fratak_Shop.Data.Utilities.SeoMangement;

namespace Fratak_Shop.Data.Entities.Categories;

public class Category : BaseEntity
{
    public Guid? ParentId { get; set; }

    [Required] 
    [MaxLength(50)]
    public string Name { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string Slug { get; set; } = null!;

    [Required]
    [MaxLength(1000)]
    public string Description { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string ImageName { get; set; } = null!;

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; }

    public SeoMetadata? Seo { get; set; }

    [ForeignKey(nameof(ParentId))]
    public Category? ParentCategory { get; set; }

    public List<Category> SubCategories { get; set; } = [];
}