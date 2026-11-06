using Fratak_Shop.Core.DTOs.Brands.Query;
using Fratak_Shop.Core.Mappers.Blog;
using Fratak_Shop.Data.Entities.Brands;

namespace Fratak_Shop.Core.Mappers.Brands;

public static class BrandMapper
{
    public static BrandDto Map(this Brand brand)
    {
        return new BrandDto
        {
            Id = brand.Id,
            CreatedAt = brand.CreatedAt,
            UpdatedAt = brand.UpdatedAt,
            IsDeleted = brand.IsDeleted,
            DeletedAt = brand.DeletedAt,
            Name = brand.Name,
            EnglishName = brand.EnglishName,
            Slug = brand.Slug,
            LogoName = brand.LogoName,
            Description = brand.Description,
            DisplayOrder = brand.DisplayOrder,
            IsActive = brand.IsActive,
            Seo = brand.Seo.MapSeoMetaData(),
        };
    }

    public static List<BrandDto> Map(this List<Brand> brands)
    {
        return brands.Select(brand => brand.Map()).ToList();
    }
}
