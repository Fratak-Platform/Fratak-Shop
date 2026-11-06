using Fratak_Shop.Core.DTOs.Categories.Query;
using Fratak_Shop.Core.Mappers.Blog;
using Fratak_Shop.Data.Entities.Categories;

namespace Fratak_Shop.Core.Mappers.Category;

public static class CategoryMapper
{
    public static CategoryDto Map(this Data.Entities.Categories.Category category)
    {
        return new CategoryDto
        {
            Id = category.Id,
            CreatedAt = category.CreatedAt,
            UpdatedAt = category.UpdatedAt,
            IsDeleted = category.IsDeleted,
            DeletedAt = category.DeletedAt,
            ParentId = category.ParentId,
            ParentName = category.ParentCategory?.Name,
            ParentSlug = category.ParentCategory?.Slug,
            Name = category.Name,
            Slug = category.Slug,
            Description = category.Description,
            ImageName = category.ImageName,
            DisplayOrder = category.DisplayOrder,
            IsActive = category.IsActive,
            Seo = category.Seo.MapSeoMetaData(),
            SubCategories = category.SubCategories.Select(s => new SubCategoryDto
            {
                Id = s.Id,
                ParentId = (Guid)s.ParentId!,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt,
                IsDeleted = s.IsDeleted,
                DeletedAt = s.DeletedAt,
                Name = s.Name,
                Slug = s.Slug,
                Description = s.Description,
                ImageName = s.ImageName,
                DisplayOrder = s.DisplayOrder,
                IsActive = s.IsActive,
                Seo = s.Seo.MapSeoMetaData(),
            }).ToList(),
        };
    }
    public static List<CategoryDto> Map(this List<Data.Entities.Categories.Category> categories)
    {
        var dtoList = new List<CategoryDto>();
        categories.ForEach(category => dtoList.Add(category.Map()));
        return dtoList;
    }
}
