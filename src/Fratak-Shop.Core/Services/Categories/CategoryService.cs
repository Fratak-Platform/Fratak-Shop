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
            return OperationResult.Error("نام دسته‌بندی را وارد کنید.");

        if (string.IsNullOrWhiteSpace(command.Slug))
            return OperationResult.Error("اسلاگ دسته‌بندی را وارد کنید.");

        var parentError = await ValidateParentAsync(command.ParentId, Guid.Empty);
        if (parentError is not null)
            return OperationResult.Error(parentError);

        var slug = command.Slug.Trim();

        var slugExists = await context.Categories.AnyAsync(x => x.Slug == slug);
        if (slugExists)
            return OperationResult.Error("این اسلاگ قبلاً برای دسته‌بندی دیگری ثبت شده است.");

        var model = new Category
        {
            ParentId = command.ParentId,
            Name = command.Name.Trim(),
            Slug = slug,
            Description = command.Description,
            ImageName = command.ImageName,
            DisplayOrder = command.DisplayOrder,
            IsActive = command.IsActive,
            Seo = command.Seo,
        };

        context.Categories.Add(model);
        await context.SaveChangesAsync();

        return OperationResult.Success("دسته‌بندی با موفقیت ایجاد شد.");
    }

    public async Task<OperationResult> EditCategoryAsync(EditCategoryCommand command)
    {
        var category = await context.Categories.AsTracking().FirstOrDefaultAsync(p => p.Id == command.Id);
        if (category is null)
            return OperationResult.NotFound("دسته‌بندی مورد نظر یافت نشد.");

        if (string.IsNullOrWhiteSpace(command.Name))
            return OperationResult.Error("نام دسته‌بندی را وارد کنید.");

        if (string.IsNullOrWhiteSpace(command.Slug))
            return OperationResult.Error("اسلاگ دسته‌بندی را وارد کنید.");

        var parentError = await ValidateParentAsync(command.ParentId, command.Id);
        if (parentError is not null)
            return OperationResult.Error(parentError);

        var slug = command.Slug.Trim();

        var slugExists = await context.Categories
            .AnyAsync(x => x.Id != command.Id && x.Slug == slug);

        if (slugExists)
            return OperationResult.Error("این اسلاگ قبلاً برای دسته‌بندی دیگری ثبت شده است.");

        category.ParentId = command.ParentId;
        category.Name = command.Name.Trim();
        category.Slug = slug;
        category.Description = command.Description;
        category.ImageName = command.ImageName;
        category.DisplayOrder = command.DisplayOrder;
        category.IsActive = command.IsActive;
        category.Seo = command.Seo;

        await context.SaveChangesAsync();

        return OperationResult.Success("دسته‌بندی با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteCategoryAsync(Guid id)
    {
        var category = await context.Categories.AsTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (category is null)
            return OperationResult.NotFound("دسته‌بندی مورد نظر یافت نشد.");

        var hasSubCategories = await context.Categories.AnyAsync(x => x.ParentId == id);
        if (hasSubCategories)
            return OperationResult.Error("این دسته‌بندی دارای زیردسته است؛ ابتدا زیردسته‌ها را حذف یا منتقل کنید.");

        var hasProducts = await context.Products.AnyAsync(x => x.CategoryId == id);
        if (hasProducts)
            return OperationResult.Error("این دسته‌بندی محصول دارد؛ ابتدا محصولات آن را منتقل یا حذف کنید.");

        context.Categories.Remove(category);
        await context.SaveChangesAsync();

        return OperationResult.Success("دسته‌بندی با موفقیت حذف شد.");
    }

    public List<CategoryDto> GetAllCategoriesAsync()
    {
        var categories = context.Categories
            .Include(x => x.SubCategories)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToList();

        return categories.Map();
    }

    public async Task<CategoryDto?> GetCategoryByIdAsync(Guid id)
    {
        var category = await context.Categories
            .Include(x => x.SubCategories)
            .FirstOrDefaultAsync(p => p.Id == id);

        return category?.Map();
    }

    public async Task<CategoryDto?> GetCategoryBySlugAsync(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        var category = await context.Categories
            .Include(x => x.SubCategories)
            .FirstOrDefaultAsync(p => p.Slug == slug.Trim());

        return category?.Map();
    }

    /// <summary>
    /// اعتبارسنجی دسته‌بندی والد: باید وجود داشته باشد، خود دسته نباشد و
    /// از زیرمجموعه‌های همان دسته هم نباشد (جلوگیری از حلقه در درخت دسته‌بندی).
    /// </summary>
    private async Task<string?> ValidateParentAsync(Guid? parentId, Guid currentCategoryId)
    {
        if (parentId is null)
            return null;

        if (parentId.Value == Guid.Empty)
            return "دسته‌بندی والد معتبر نیست.";

        if (parentId.Value == currentCategoryId)
            return "دسته‌بندی نمی‌تواند والد خودش باشد.";

        var parentExists = await context.Categories.AnyAsync(x => x.Id == parentId.Value);
        if (!parentExists)
            return "دسته‌بندی والد یافت نشد.";

        if (currentCategoryId == Guid.Empty)
            return null;

        var descendantIds = await GetDescendantIdsAsync(currentCategoryId);

        if (descendantIds.Contains(parentId.Value))
            return "دسته‌بندی والد نمی‌تواند از زیردسته‌های همین دسته باشد.";

        return null;
    }

    private async Task<List<Guid>> GetDescendantIdsAsync(Guid categoryId)
    {
        var descendants = new List<Guid>();
        var queue = new Queue<Guid>();

        queue.Enqueue(categoryId);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();

            var children = await context.Categories
                .Where(x => x.ParentId == currentId)
                .Select(x => x.Id)
                .ToListAsync();

            foreach (var child in children)
            {
                if (descendants.Contains(child))
                    continue;

                descendants.Add(child);
                queue.Enqueue(child);
            }
        }

        return descendants;
    }
}
