using Fratak_Shop.Core.DTOs.Categories.Command;
using Fratak_Shop.Core.DTOs.Categories.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Categories;

public interface ICategoryService
{
    Task<OperationResult> CreateCategoryAsync(CreateCategoryCommand command);
    Task<OperationResult> EditCategoryAsync(EditCategoryCommand command);
    Task<OperationResult> DeleteCategoryAsync(Guid id);
    List<CategoryDto> GetAllCategoriesAsync();
    Task<CategoryDto?> GetCategoryByIdAsync(Guid id);
    Task<CategoryDto?> GetCategoryBySlugAsync(string slug);

    /// <summary>دسته‌بندی‌های ریشه فعال همراه زیردسته‌های فعال؛ برای منو و صفحه نخست.</summary>
    Task<List<CategoryDto>> GetMenuCategoriesAsync();

    /// <summary>دور ۲۳: آمار واقعی یک دسته (با زیردسته‌های مستقیم) برای پرسش‌های متداول پایین صفحهٔ دسته.</summary>
    Task<CategoryFacts> GetCategoryFactsAsync(Guid categoryId);
}
