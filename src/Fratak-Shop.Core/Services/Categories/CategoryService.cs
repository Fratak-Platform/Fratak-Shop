using Fratak_Shop.Core.DTOs.Categories.Command;
using Fratak_Shop.Core.DTOs.Categories.Query;
using Fratak_Shop.Core.Mappers.Category;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Categories;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Categories;

public class CategoryService(FratakShopDbContext context) : ICategoryService
{
    public async Task<OperationResult> CreateCategoryAsync(CreateCategoryCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            return OperationResult.Error("نام دسته بندی را وارد کنید ");

        if (string.IsNullOrWhiteSpace(command.Slug))
            return OperationResult.Error("را وارد کنید slug");

        var exists = await context.Categories.AnyAsync(x => x.Slug == command.Slug);
        if (exists)
            return OperationResult.Error("دسته بندی مورد نظر تکراری است ");

        var model = new Category
        {
            ParentId = command.ParentId,
            Name = command.Name,
            Slug = command.Slug,
            Description = command.Description,
            ImageName = command.ImageName,
            DisplayOrder = command.DisplayOrder,
            IsActive = command.IsActive,
            Seo = command.Seo,
        };

        context.Categories.Add(model);
        await context.SaveChangesAsync();
        return OperationResult.Success();
    }

    public async Task<OperationResult> EditCategoryAsync(EditCategoryCommand command)
    {
        var category = await context.Categories.AsTracking().FirstOrDefaultAsync(p => p.Id == command.Id);
        if (category == null)
            return OperationResult.NotFound("نام دسته بندی را وارد کنید ");

        if (string.IsNullOrWhiteSpace(command.Name))
            return OperationResult.Error("نام دسته بندی را وارد کنید ");

        if (string.IsNullOrWhiteSpace(command.Slug))
            return OperationResult.Error("را وارد کنید slug");

        var slugExists = await context.Categories
            .AnyAsync(x => x.Id != command.Id && x.Slug == command.Slug);
        if (slugExists)
            return OperationResult.Error("دسته بندی مورد نظر تکراری است");

        category.ParentId = command.ParentId;
        category.Name = command.Name;
        category.Slug = command.Slug;
        category.Description = command.Description;
        category.ImageName = command.ImageName;
        category.DisplayOrder = command.DisplayOrder;
        category.IsActive = command.IsActive;
        category.Seo = command.Seo;

        await context.SaveChangesAsync();
        return OperationResult.Success();
    }

    public async Task<OperationResult> DeleteCategoryAsync(Guid id)
    {
        var category = await context.Categories.AsTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (category == null)
            return OperationResult.NotFound();

        context.Categories.Remove(category);
        await context.SaveChangesAsync();
        return OperationResult.Success();
    }

    public List<CategoryDto> GetAllCategoriesAsync()
    {
        var categories = context.Categories
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToList();

        return categories.Map();
    }

    public async Task<CategoryDto?> GetCategoryByIdAsync(Guid id)
    {
        var category = await context.Categories.Include(s => s.SubCategories)
            .FirstOrDefaultAsync(p => p.Id == id);
        return category?.Map();
    }

    public async Task<CategoryDto?> GetCategoryBySlugAsync(string slug)
    {
        var category = await context.Categories.FirstOrDefaultAsync(p => p.Slug == slug);
        return category?.Map();
    }
}
