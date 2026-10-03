using Fratak_Shop.Core.DTOs.Blogs.Categories.Command;
using Fratak_Shop.Core.DTOs.Blogs.Categories.Query;
using Fratak_Shop.Core.Mappers.Blog;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Blogs;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Blogs;

public class BlogCategoryService(FratakShopDbContext context) : IBlogCategoryService
{
    public async Task<OperationResult> CreateCategoryAsync(CreateBlogCategoryCommand command)
    {
        var model = new BlogCategory
        {
            Name = command.Name,
            Slug = command.Slug,
            Description = command.Description,
            DisplayOrder = command.DisplayOrder,
            IsActive = command.IsActive,
            Seo = command.Seo,
        };

        context.BlogCategories.Add(model);
        await context.SaveChangesAsync();
        return OperationResult.Success();
    }

    public async Task<OperationResult> EditCategoryAsync(EditBlogCategoryCommand command)
    {
        var category = await context.BlogCategories.AsTracking().FirstOrDefaultAsync(p => p.Id == command.Id);
        if (category == null)
            return OperationResult.NotFound();

        if ((string.IsNullOrWhiteSpace(command.Name)))
            return OperationResult.Error("نام را وارد کنید ");

        if ((string.IsNullOrWhiteSpace(command.Slug)))
            return OperationResult.Error("را وارد کنید slug");

        category.Name = command.Name;
        category.Slug = command.Slug;
        category.Description = command.Description;
        category.DisplayOrder = command.DisplayOrder;
        category.IsActive = command.IsActive;
        category.Seo = command.Seo;

        await context.SaveChangesAsync();
        return OperationResult.Success();
    }

    public async Task<OperationResult> DeleteCategoryAsync(Guid id)
    {
        var category = await context.BlogCategories.AsTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (category == null)
            return OperationResult.NotFound();

        context.BlogCategories.Remove(category);
        await context.SaveChangesAsync();
        return OperationResult.Success();

    }

    public List<BlogCategoryDto> GetAllCategoriesAsync()
    {
        var categories = context.BlogCategories.ToList();
        return categories.Map();
    }

    public async Task<BlogCategoryDto?> GetCategoryByIdAsync(Guid id)
    {
        var category = await context.BlogCategories.FirstOrDefaultAsync(p => p.Id == id);
        if (category == null) return null;
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
            Seo = category.Seo.MapSeoMetaData()
        };
    }
}
