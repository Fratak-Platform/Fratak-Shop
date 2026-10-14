using Fratak_Shop.Core.DTOs.Blogs.Categories.Query;
using Fratak_Shop.Data.Entities.Blogs;

namespace Fratak_Shop.Core.Mappers.Blog;

public static class CategoryMapper
{
    public static BlogCategoryDto Map(this BlogCategory category)
    {
        return new BlogCategoryDto
        {
            Id = category.Id,
            CreatedAt = category.CreatedAt,
            UpdatedAt = category.UpdatedAt,
            IsDeleted = category.IsDeleted,
            DeletedAt = category.DeletedAt,
            Name = category.Name,
            Slug = category.Slug,
            Description = category.Description,
            DisplayOrder = category.DisplayOrder,
            IsActive = category.IsActive,
            Seo = category.Seo.MapSeoMetaData(),
        };
    }

    public static List<BlogCategoryDto> Map(this List<BlogCategory> categories)
    {
        return categories.Select(category => category.Map()).ToList();
    }
}
