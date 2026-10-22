using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Core.DTOs.Brands.Command;

public class CreateBrandCommand
{
    public string Name { get; set; } = null!;
    public string? EnglishName { get; set; }
    public string Slug { get; set; } = null!;
    public string? LogoName { get; set; }
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public SeoMetaData? Seo { get; set; }
}

public class EditBrandCommand
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? EnglishName { get; set; }
    public string Slug { get; set; } = null!;
    public string? LogoName { get; set; }
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public SeoMetaData? Seo { get; set; }
}
