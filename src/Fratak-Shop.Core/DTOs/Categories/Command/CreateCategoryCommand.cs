using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Core.DTOs.Categories.Command;

public class CreateCategoryCommand
{
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Description { get; set; }
    public string? ImageName { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } 
    public SeoMetaData? Seo { get; set; }
}