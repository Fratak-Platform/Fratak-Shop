using Fratak_Shop.Core.DTOs.Brands.Command;
using Fratak_Shop.Core.DTOs.Brands.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Brands;

public interface IBrandService
{
    Task<OperationResult> CreateBrandAsync(CreateBrandCommand command);
    Task<OperationResult> EditBrandAsync(EditBrandCommand command);
    Task<OperationResult> DeleteBrandAsync(Guid id);

    Task<List<BrandDto>> GetAllBrandsAsync();

    /// <summary>برندهای فعال به ترتیب نمایش همراه تعداد محصولات فعال هر برند.</summary>
    Task<List<BrandDto>> GetActiveBrandsAsync();

    Task<BrandDto?> GetBrandByIdAsync(Guid id);
    Task<BrandDto?> GetBrandBySlugAsync(string slug);
}
