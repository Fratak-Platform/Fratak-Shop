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

        // دور ۲۱: «دسته‌بندی نشده» جای کالاهای درون‌ریزی‌شده تا دسته‌بندی هوشمند است؛ حذفش درون‌ریزی را خراب می‌کند.
        if (category.IsSystem)
            return OperationResult.Error("«دسته‌بندی نشده» دستهٔ سیستمی است و حذف نمی‌شود.");

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

    /// <summary>
    /// دستهٔ «کم‌محتوا»: کمتر از این تعداد کالای فعال (دور ۲۳ – مورد ۳۳). صفحه‌اش noindex می‌شود، در نقشهٔ سایت و منو نمی‌آید
    /// (صفحه برای کاربر باز می‌ماند؛ لینک‌های قدیمی خراب نمی‌شوند).
    /// </summary>
    public const int ThinCategoryProducts = 3;

    /// <summary>
    /// دستهٔ کم‌محتوا؟ مثال: ۲ کالا ← true؛ ۳ کالا ← false.
    /// </summary>
    public static bool IsThin(int activeProducts) => activeProducts < ThinCategoryProducts;

    /// <summary>
    /// دسته‌های منو (هدر، مگامنو، صفحهٔ نخست). دور ۲۳:
    /// <list type="bullet">
    /// <item>دستهٔ سیستمی («دسته‌بندی نشده») هرگز.</item>
    /// <item>ترتیب از روی دادهٔ واقعی: فروش کالاهای دسته، بعد تعداد کالا، بعد ترتیب دستی.</item>
    /// <item>دستهٔ کم‌محتوا (کمتر از ۳ کالای فعال) پنهان؛ ولی اگر بعد از حذف کمتر از ۳ دستهٔ اصلی بماند (فروشگاه تازه)، دسته‌های دارای کالا می‌مانند
    /// تا منو خالی نشود. دستهٔ بدون هیچ کالایی هیچ‌وقت در منو نمی‌آید.</item>
    /// </list>
    /// </summary>
    public async Task<List<CategoryDto>> GetMenuCategoriesAsync()
    {
        var categories = await context.Categories
            .Where(x => x.ParentId == null && x.IsActive && !x.IsSystem)
            .Include(x => x.SubCategories
                .Where(s => s.IsActive && !s.IsSystem)
                .OrderBy(s => s.DisplayOrder)
                .ThenBy(s => s.Name))
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToListAsync();

        var stats = await context.Products
            .Where(p => p.IsActive)
            .GroupBy(p => p.CategoryId)
            .Select(g => new { Id = g.Key, Count = g.Count(), Sales = g.Sum(p => p.SalesCount) })
            .ToDictionaryAsync(x => x.Id, x => (x.Count, x.Sales));

        return BuildMenu(categories.Map(), stats);
    }

    /// <summary>
    /// قانون منو روی دادهٔ آماده (بدون پایگاه داده تا آزمون واحد بگیرد). <paramref name="stats"/> = (تعداد کالای فعال، جمع فروش) هر دسته.
    /// </summary>
    public static List<CategoryDto> BuildMenu(List<CategoryDto> roots, IReadOnlyDictionary<Guid, (int Count, int Sales)> stats)
    {
        (int Count, int Sales) Of(Guid id) => stats.TryGetValue(id, out var v) ? v : (0, 0);

        var scored = new List<(CategoryDto Category, int Count, int Sales)>();
        foreach (var root in roots.Where(r => !r.IsSystem))
        {
            foreach (var sub in root.SubCategories)
                sub.ProductCount = Of(sub.Id).Count;
            var own = Of(root.Id);
            var count = own.Count + root.SubCategories.Sum(s => s.ProductCount);
            var sales = own.Sales + root.SubCategories.Sum(s => Of(s.Id).Sales);
            root.ProductCount = count;
            // زیردسته‌های کم‌محتوا پنهان؛ اگر هیچ زیردستهٔ پرمحتوایی نبود، زیردسته‌های دارای کالا می‌مانند (فروشگاه تازه).
            var richSubs = root.SubCategories.Any(s => !IsThin(s.ProductCount));
            root.SubCategories = root.SubCategories
                .Where(s => richSubs ? !IsThin(s.ProductCount) : s.ProductCount > 0)
                .OrderByDescending(s => Of(s.Id).Sales).ThenByDescending(s => s.ProductCount).ThenBy(s => s.DisplayOrder).ThenBy(s => s.Name, StringComparer.Ordinal)
                .ToList();
            if (count > 0)
                scored.Add((root, count, sales));
        }

        var rich = scored.Where(x => !IsThin(x.Count)).ToList();
        var chosen = rich.Count >= 3 ? rich : scored;
        return chosen
            .OrderByDescending(x => x.Sales).ThenByDescending(x => x.Count).ThenBy(x => x.Category.DisplayOrder).ThenBy(x => x.Category.Name, StringComparer.Ordinal)
            .Select(x => x.Category)
            .ToList();
    }

    /// <summary>
    /// آمار واقعی دسته + زیردسته‌های مستقیم (دور ۲۳). قیمت نهایی = قیمت تخفیف‌خورده اگر معتبر باشد. فقط یک بار در هر بار ساخت صفحه
    /// (صفحه کش خروجی دارد) و فقط با ستون‌های لازم.
    /// </summary>
    public async Task<CategoryFacts> GetCategoryFactsAsync(Guid categoryId)
    {
        var ids = await context.Categories.Where(c => c.Id == categoryId || c.ParentId == categoryId).Select(c => c.Id).ToListAsync();
        if (ids.Count == 0)
            return CategoryFacts.Empty;
        var rows = await context.Products
            .Where(p => p.IsActive && ids.Contains(p.CategoryId))
            .Select(p => new
            {
                p.StockQuantity,
                Final = p.DiscountPrice != null && p.DiscountPrice > 0 && p.DiscountPrice < p.Price ? p.DiscountPrice.Value : p.Price,
                Discounted = p.DiscountPrice != null && p.DiscountPrice > 0 && p.DiscountPrice < p.Price,
                BrandName = p.Brand != null ? p.Brand.Name : null,
            })
            .Take(5000)
            .ToListAsync();
        if (rows.Count == 0)
            return CategoryFacts.Empty;
        var inStock = rows.Where(r => r.StockQuantity > 0 && r.Final > 0).ToList();
        var brands = rows.Where(r => !string.IsNullOrWhiteSpace(r.BrandName))
            .GroupBy(r => r.BrandName!.Trim())
            .Select(g => (Name: g.Key, Count: g.Count()))
            .OrderByDescending(b => b.Count).ThenBy(b => b.Name, StringComparer.Ordinal)
            .Take(5).ToList();
        return new CategoryFacts(rows.Count, inStock.Count,
            inStock.Count == 0 ? null : inStock.Min(r => r.Final),
            inStock.Count == 0 ? null : inStock.Max(r => r.Final),
            inStock.Count(r => r.Discounted), brands);
    }

    public async Task<CategoryDto?> GetCategoryBySlugAsync(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        var category = await context.Categories
            .Include(x => x.ParentCategory)
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
