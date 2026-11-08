using Fratak_Shop.Core.DTOs.Products.Command;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Products;

public interface IProductService
{
    Task<OperationResult> CreateProductAsync(CreateProductCommand command);
    Task<OperationResult> EditProductAsync(EditProductCommand command);
    Task<OperationResult> DeleteProductAsync(Guid id);
    Task<OperationResult> ChangeProductActivationAsync(Guid id, bool isActive);
    Task<OperationResult> UpdateStockAsync(Guid id, int stockQuantity);

    /// <summary>دور ۲۲: تغییر گروهی تأمین‌کننده (null = بدون تأمین‌کننده). خروجی: تعداد کالای تغییرکرده.</summary>
    Task<OperationResult<int>> ChangeSupplierAsync(IReadOnlyCollection<Guid> productIds, Guid? supplierId);

    Task<ProductFilterResult> GetProductByFilterAsync(ProductFilterParams filterParams);

    /// <summary>Value counts of filterable specifications for the current listing filters.</summary>
    Task<List<SpecificationFacetDto>> GetSpecificationFacetsAsync(ProductFilterParams filterParams, IReadOnlyList<SpecificationFacetRequest> attributes);

    /// <summary>Brands that have products in the current listing (with counts); selected brands first.</summary>
    Task<List<BrandFacetDto>> GetBrandFacetsAsync(ProductFilterParams filterParams, int take = 50);
    Task<ProductDto?> GetProductByIdAsync(Guid id);
    Task<ProductDto?> GetProductBySlugAsync(string slug);
    Task<List<ProductDto>> GetProductsAsync();

    /// <summary>محصولات فعال با شناسه‌های داده‌شده به همان ترتیب (برای مقایسه محصولات).</summary>
    Task<List<ProductDto>> GetProductsByIdsAsync(IEnumerable<Guid> ids);

    /// <summary>افزایش شمارنده بازدید محصول.</summary>
    Task AddProductVisitAsync(Guid id);

    /// <summary>محصولات فعال و موجودِ هم‌دسته با محصول جاری.</summary>
    Task<List<ProductFilterData>> GetRelatedProductsAsync(Guid productId, int take = 8);

    /// <summary>Product cards of active products in the given order (recommendations, recently viewed).</summary>
    Task<List<ProductFilterData>> GetCardsByIdsAsync(IEnumerable<Guid> ids, bool onlyInStock = false);
}
