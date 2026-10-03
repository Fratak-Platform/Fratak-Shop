using Fratak_Shop.Core.DTOs.Blogs.Categories.Query;
using Fratak_Shop.Data.Entities.Blogs;

namespace Fratak_Shop.Core.Mappers.Blog;

public static class CategoryMapper
{
    public static List<BlogCategoryDto> Map(this List<BlogCategory> categories)
    {
        var postsDto = new List<BlogCategoryDto>();
        categories.ForEach(p =>
        {
            postsDto.Add(new BlogCategoryDto
            {
                Id = p.Id,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
                IsDeleted = p.IsDeleted,
                DeletedAt = p.DeletedAt,
                Name = p.Name,
                Slug = p.Slug,
                Description = p.Description,
                DisplayOrder = p.DisplayOrder,
                IsActive = p.IsActive,
                Seo = p.Seo.MapSeoMetaData(),
            });
        });
        return postsDto;
    }
}