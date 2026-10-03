using Fratak_Shop.Core.DTOs.Categories.Command;
using Fratak_Shop.Core.DTOs.Categories.Query;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Mappers.Category;
using Fratak_Shop.Core.Mappers.Seo;
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

        var slug = string.IsNullOrWhiteSpace(command.Slug)
            ? SlugHelper.Generate(command.Name)
            : SlugHelper.Generate(command.Slug);

        var slugExists = await context.Categories.AnyAsync(category => category.Slug == slug);
        if (slugExists)
            return OperationResult.Error("این اسلاگ قبلاً برای دسته‌بندی دیگری ثبت شده است.");

        if (command.ParentId.HasValue && !await context.Categories.AnyAsync(item => item.Id == command.ParentId))
            return OperationResult.Error("دسته‌بندی والد انتخاب‌شده معتبر نیست.");

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
        var category = await context.Categories.AsTracking().FirstOrDefaultAsync(item => item.Id == command.Id);
        if (category is null)
            return OperationResult.NotFound("دسته‌بندی مورد نظر یافت نشد.");

        if (string.IsNullOrWhiteSpace(command.Name))
            return OperationResult.Error("نام دسته‌بندی را وارد کنید.");

        var slug = string.IsNullOrWhiteSpace(command.Slug)
            ? SlugHelper.Generate(command.Name)
            : SlugHelper.Generate(command.Slug);

        var slugExists = await context.Categories.AnyAsync(item => item.Id != command.Id && item.Slug == slug);
        if (slugExists)
            return OperationResult.Error("این اسلاگ قبلاً برای دسته‌بندی دیگری ثبت شده است.");

        if (command.ParentId == command.Id)
            return OperationResult.Error("دسته‌بندی نمی‌تواند والد خودش باشد.");

        if (command.ParentId.HasValue && await IsDescendantAsync(command.Id, command.ParentId.Value))
            return OperationResult.Error("دسته‌بندی انتخاب‌شده به‌عنوان والد، زیرمجموعه همین دسته است.");

        category.ParentId = command.ParentId;
        category.Name = command.Name.Trim();
        category.Slug = slug;
        category.Description = command.Description;
        category.ImageName = command.ImageName;
        category.DisplayOrder = command.DisplayOrder;
        category.IsActive = command.IsActive;

        if (command.Seo is not null)
            category.Seo = command.Seo;

        await context.SaveChangesAsync();
        return OperationResult.Success("دسته‌بندی با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteCategoryAsync(Guid id)
    {
        var category = await context.Categories.AsTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (category is null)
            return OperationResult.NotFound("دسته‌بندی مورد نظر یافت نشد.");

        var hasProducts = await context.Products.AnyAsync(product => product.CategoryId == id);
        if (hasProducts)
            return OperationResult.Error("این دسته‌بندی محصول دارد؛ ابتدا محصولات را جابه‌جا یا غیرفعال کنید.");

        var hasChildren = await context.Categories.AnyAsync(item => item.ParentId == id);
        if (hasChildren)
            return OperationResult.Error("این دسته‌بندی زیرمجموعه دارد؛ ابتدا زیرمجموعه‌ها را حذف کنید.");

        context.Categories.Remove(category);
        await context.SaveChangesAsync();
        return OperationResult.Success("دسته‌بندی با موفقیت حذف شد.");
    }

    public async Task<OperationResult> ReorderAsync(Guid id, int displayOrder)
    {
        var category = await context.Categories.AsTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (category is null)
            return OperationResult.NotFound("دسته‌بندی مورد نظر یافت نشد.");

        category.DisplayOrder = displayOrder;
        await context.SaveChangesAsync();
        return OperationResult.Success("ترتیب دسته‌بندی ذخیره شد.");
    }

    public List<CategoryDto> GetAllCategoriesAsync()
    {
        var categories = context.Categories
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Name)
            .ToList();

        return categories.Map();
    }

    public async Task<CategoryDto?> GetCategoryByIdAsync(Guid id)
    {
        var category = await context.Categories
            .Include(item => item.ParentCategory)
            .Include(item => item.SubCategories)
            .FirstOrDefaultAsync(item => item.Id == id);

        if (category is null)
            return null;

        var dto = category.Map();
        dto.ParentName = category.ParentCategory?.Name;
        dto.ProductCount = await context.Products.CountAsync(product => product.CategoryId == id);
        return dto;
    }

    public async Task<CategoryDto?> GetCategoryBySlugAsync(string slug)
    {
        var category = await context.Categories
            .Include(item => item.SubCategories)
            .FirstOrDefaultAsync(item => item.Slug == slug);

        if (category is null)
            return null;

        var dto = category.Map();
        dto.ProductCount = await context.Products.CountAsync(product => product.CategoryId == category.Id);
        return dto;
    }

    public async Task<List<CategoryDto>> GetTreeAsync(bool onlyActive = false)
    {
        var query = context.Categories.Include(item => item.SubCategories).AsQueryable();
        if (onlyActive)
            query = query.Where(item => item.IsActive);

        var categories = await query
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Name)
            .ToListAsync();

        var counts = await context.Products
            .GroupBy(product => product.CategoryId)
            .Select(group => new { CategoryId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.CategoryId, item => item.Count);

        var roots = categories.Where(item => item.ParentId is null).ToList();
        return roots.Select(root =>
        {
            var dto = root.Map();
            dto.ProductCount = counts.GetValueOrDefault(root.Id);
            dto.SubCategories = root.SubCategories
                .Where(child => !onlyActive || child.IsActive)
                .OrderBy(child => child.DisplayOrder)
                .Select(child =>
                {
                    var childDto = child.Map();
                    childDto.ProductCount = counts.GetValueOrDefault(child.Id);
                    childDto.ParentId = child.ParentId;
                    return new SubCategoryDto
                    {
                        Id = childDto.Id,
                        ParentId = child.ParentId ?? Guid.Empty,
                        Name = childDto.Name,
                        Slug = childDto.Slug,
                        Description = childDto.Description,
                        ImageName = childDto.ImageName,
                        DisplayOrder = childDto.DisplayOrder,
                        IsActive = childDto.IsActive,
                        Seo = childDto.Seo,
                        CreatedAt = childDto.CreatedAt,
                        UpdatedAt = childDto.UpdatedAt,
                    };
                })
                .ToList();
            return dto;
        }).ToList();
    }

    public async Task<List<CategoryDto>> GetFlatAsync(bool onlyActive = false)
    {
        var query = context.Categories
            .Include(item => item.ParentCategory)
            .AsQueryable();

        if (onlyActive)
            query = query.Where(item => item.IsActive);

        var categories = await query
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Name)
            .ToListAsync();

        return categories.Select(category =>
        {
            var dto = category.Map();
            dto.ParentId = category.ParentId;
            dto.ParentName = category.ParentCategory?.Name;
            dto.Path = category.ParentCategory is null ? category.Name : $"{category.ParentCategory.Name} / {category.Name}";
            return dto;
        }).ToList();
    }

    public async Task<List<CategoryMenuDto>> GetMenuAsync(int take = 12)
    {
        var categories = await context.Categories
            .Include(item => item.SubCategories)
            .Where(item => item.IsActive && item.ParentId == null)
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Name)
            .Take(take)
            .ToListAsync();

        var counts = await context.Products
            .GroupBy(product => product.CategoryId)
            .Select(group => new { CategoryId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.CategoryId, item => item.Count);

        return categories.Select(category => new CategoryMenuDto
        {
            Id = category.Id,
            Name = category.Name,
            Slug = category.Slug,
            ImageName = category.ImageName,
            ProductCount = counts.GetValueOrDefault(category.Id),
            Children = category.SubCategories
                .Where(child => child.IsActive)
                .OrderBy(child => child.DisplayOrder)
                .Select(child => new CategoryMenuDto
                {
                    Id = child.Id,
                    Name = child.Name,
                    Slug = child.Slug,
                    ImageName = child.ImageName,
                    ProductCount = counts.GetValueOrDefault(child.Id),
                })
                .ToList(),
        }).ToList();
    }

    public async Task<CategoryDetailDto?> GetDetailBySlugAsync(string slug)
    {
        var category = await context.Categories
            .Include(item => item.ParentCategory)
            .Include(item => item.SubCategories)
            .FirstOrDefaultAsync(item => item.Slug == slug && item.IsActive);

        if (category is null)
            return null;

        var dto = new CategoryDetailDto
        {
            Id = category.Id,
            CreatedAt = category.CreatedAt,
            UpdatedAt = category.UpdatedAt,
            Name = category.Name,
            Slug = category.Slug,
            Description = category.Description,
            ImageName = category.ImageName,
            DisplayOrder = category.DisplayOrder,
            IsActive = category.IsActive,
            Seo = category.Seo,
            ParentId = category.ParentId,
            ParentName = category.ParentCategory?.Name,
            ProductCount = await context.Products.CountAsync(product => product.CategoryId == category.Id && product.IsActive),
            SubCategories = category.SubCategories
                .Where(child => child.IsActive)
                .OrderBy(child => child.DisplayOrder)
                .Select(child => new SubCategoryDto
                {
                    Id = child.Id,
                    ParentId = child.ParentId ?? category.Id,
                    Name = child.Name,
                    Slug = child.Slug,
                    ImageName = child.ImageName,
                    DisplayOrder = child.DisplayOrder,
                    IsActive = child.IsActive,
                    Seo = child.Seo?.ToDto(),
                })
                .ToList(),
        };

        return dto;
    }

    public async Task<List<BreadcrumbDto>> GetBreadcrumbsAsync(Guid? categoryId)
    {
        var breadcrumbs = new List<BreadcrumbDto> { new() { Title = "فروشگاه", Url = "/products" } };
        if (categoryId is null)
            return breadcrumbs;

        var chain = new List<(string Name, string Slug)>();
        var cursor = await context.Categories
            .Where(item => item.Id == categoryId)
            .Select(item => new { item.Id, item.Name, item.Slug, item.ParentId })
            .FirstOrDefaultAsync();

        var guard = 0;
        while (cursor is not null && guard++ < 5)
        {
            chain.Insert(0, (cursor.Name, cursor.Slug));
            var parentId = cursor.ParentId;
            cursor = parentId is null
                ? null
                : await context.Categories
                    .Where(item => item.Id == parentId)
                    .Select(item => new { item.Id, item.Name, item.Slug, item.ParentId })
                    .FirstOrDefaultAsync();
        }

        breadcrumbs.AddRange(chain.Select((item, index) => new BreadcrumbDto
        {
            Title = item.Name,
            Url = $"/category/{item.Slug}",
            IsCurrent = index == chain.Count - 1,
        }));

        return breadcrumbs;
    }

    public async Task<List<CategoryDto>> GetParentCandidatesAsync(Guid? currentId)
    {
        var categories = await context.Categories
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Name)
            .ToListAsync();

        if (currentId is null)
            return categories.Map();

        var blocked = new HashSet<Guid> { currentId.Value };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var category in categories.Where(item => item.ParentId.HasValue && blocked.Contains(item.ParentId.Value)))
            {
                if (blocked.Add(category.Id))
                    changed = true;
            }
        }

        return categories.Where(item => !blocked.Contains(item.Id)).ToList().Map();
    }

    private async Task<bool> IsDescendantAsync(Guid parentId, Guid candidateId)
    {
        var cursorId = (Guid?)candidateId;
        var guard = 0;

        while (cursorId is not null && guard++ < 10)
        {
            if (cursorId == parentId)
                return true;

            var parent = await context.Categories
                .Where(item => item.Id == cursorId)
                .Select(item => item.ParentId)
                .FirstOrDefaultAsync();

            cursorId = parent;
        }

        return false;
    }
}
