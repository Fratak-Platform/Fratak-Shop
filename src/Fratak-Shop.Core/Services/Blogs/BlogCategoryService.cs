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
        if (string.IsNullOrWhiteSpace(command.Name))
            return OperationResult.Error("نام دسته‌بندی را وارد کنید.");

        if (string.IsNullOrWhiteSpace(command.Slug))
            return OperationResult.Error("اسلاگ دسته‌بندی را وارد کنید.");

        var slug = command.Slug.Trim();

        var slugExists = await context.BlogCategories.AnyAsync(x => x.Slug == slug);
        if (slugExists)
            return OperationResult.Error("این اسلاگ قبلاً برای دسته‌بندی دیگری ثبت شده است.");

        var model = new BlogCategory
        {
            Name = command.Name.Trim(),
            Slug = slug,
            Description = command.Description,
            DisplayOrder = command.DisplayOrder,
            IsActive = command.IsActive,
            Seo = command.Seo,
        };

        context.BlogCategories.Add(model);
        await context.SaveChangesAsync();

        return OperationResult.Success("دسته‌بندی مقاله با موفقیت ایجاد شد.");
    }

    public async Task<OperationResult> EditCategoryAsync(EditBlogCategoryCommand command)
    {
        var category = await context.BlogCategories.AsTracking().FirstOrDefaultAsync(p => p.Id == command.Id);
        if (category is null)
            return OperationResult.NotFound("دسته‌بندی مقاله مورد نظر یافت نشد.");

        if (string.IsNullOrWhiteSpace(command.Name))
            return OperationResult.Error("نام دسته‌بندی را وارد کنید.");

        if (string.IsNullOrWhiteSpace(command.Slug))
            return OperationResult.Error("اسلاگ دسته‌بندی را وارد کنید.");

        var slug = command.Slug.Trim();

        var slugExists = await context.BlogCategories
            .AnyAsync(x => x.Id != command.Id && x.Slug == slug);

        if (slugExists)
            return OperationResult.Error("این اسلاگ قبلاً برای دسته‌بندی دیگری ثبت شده است.");

        category.Name = command.Name.Trim();
        category.Slug = slug;
        category.Description = command.Description;
        category.DisplayOrder = command.DisplayOrder;
        category.IsActive = command.IsActive;
        category.Seo = command.Seo;

        await context.SaveChangesAsync();

        return OperationResult.Success("دسته‌بندی مقاله با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteCategoryAsync(Guid id)
    {
        var category = await context.BlogCategories.AsTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (category is null)
            return OperationResult.NotFound("دسته‌بندی مقاله مورد نظر یافت نشد.");

        var hasPosts = await context.BlogPosts.AnyAsync(x => x.BlogCategoryId == id);
        if (hasPosts)
            return OperationResult.Error("این دسته‌بندی مقاله دارد؛ ابتدا مقالات آن را منتقل یا حذف کنید.");

        context.BlogCategories.Remove(category);
        await context.SaveChangesAsync();

        return OperationResult.Success("دسته‌بندی مقاله با موفقیت حذف شد.");
    }

    public List<BlogCategoryDto> GetAllCategoriesAsync()
    {
        return context.BlogCategories
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToList()
            .Map();
    }

    public async Task<BlogCategoryDto?> GetCategoryByIdAsync(Guid id)
    {
        var category = await context.BlogCategories.FirstOrDefaultAsync(p => p.Id == id);
        return category?.Map();
    }
}
