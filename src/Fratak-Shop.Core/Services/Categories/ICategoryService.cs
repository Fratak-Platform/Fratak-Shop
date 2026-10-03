using Fratak_Shop.Core.DTOs.Categories.Command;
using Fratak_Shop.Core.DTOs.Categories.Query;
using Fratak_Shop.Core.DTOs.Products.Query;
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

    /// <summary>درخت کامل دسته‌بندی‌ها برای پنل مدیریت و منوی فروشگاه.</summary>
    Task<List<CategoryDto>> GetTreeAsync(bool onlyActive = false);

    /// <summary>فهرست تخت دسته‌بندی‌ها برای فیلدهای انتخابی و فیلترها.</summary>
    Task<List<CategoryDto>> GetFlatAsync(bool onlyActive = false);

    /// <summary>دسته‌بندی‌های هدر همراه با فرزندان.</summary>
    Task<List<CategoryMenuDto>> GetMenuAsync(int take = 12);

    /// <summary>صفحه دسته‌بندی به‌همراه محصولات (فیلترشده) و مسیر راهنما.</summary>
    Task<CategoryDetailDto?> GetDetailBySlugAsync(string slug);

    Task<List<BreadcrumbDto>> GetBreadcrumbsAsync(Guid? categoryId);

    /// <summary>دسته‌بندی والد مجاز برای انتخاب در فرم (بدون حلقه).</summary>
    Task<List<CategoryDto>> GetParentCandidatesAsync(Guid? currentId);

    Task<OperationResult> ReorderAsync(Guid id, int displayOrder);
}
