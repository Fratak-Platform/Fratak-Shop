using Fratak_Shop.Core.DTOs.Products.Command;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Core.Services.Products;

/// <summary>
/// Everything the shop does with products: the storefront listing/detail queries
/// (filter, sort, related, featured) and the admin CRUD including images, features
/// and SEO data.
/// </summary>
public interface IProductService
{
    // ---------------------------------------------------------------- storefront
    Task<ProductFilterResult> GetFilterAsync(ProductFilterParams filterParams);

    Task<ProductDetailDto?> GetDetailBySlugAsync(string slug);

    Task<ProductDetailDto?> GetDetailByIdAsync(Guid id, bool includeInactive = false);

    Task<List<ProductCardDto>> GetCardsByIdsAsync(IEnumerable<Guid> ids);

    Task<List<ProductCardDto>> GetFeaturedAsync(int take = 8);

    Task<List<ProductCardDto>> GetDiscountedAsync(int take = 8);

    Task<List<ProductCardDto>> GetLatestAsync(int take = 8);

    Task<List<ProductCardDto>> GetBestSellingAsync(int take = 8);

    Task<List<ProductCardDto>> GetRelatedAsync(Guid productId, int take = 8);

    Task<List<ProductCardDto>> SearchAsync(string term, int take = 10);

    /// <summary>ثبت بازدید محصول (شمارنده محصول و آمار روزانه سئو).</summary>
    Task RegisterVisitAsync(Guid productId, CancellationToken cancellationToken = default);

    // ---------------------------------------------------------------- admin
    Task<ProductDto?> GetForEditAsync(Guid id);

    Task<OperationResult> CreateProductAsync(CreateProductCommand command);

    Task<OperationResult> UpdateProductAsync(EditProductCommand command);

    Task<OperationResult> DeleteProductAsync(Guid id);

    Task<OperationResult> ToggleActiveAsync(Guid id);

    Task<OperationResult> ToggleFeaturedAsync(Guid id);

    Task<OperationResult> UpdateStockAsync(Guid id, int stockQuantity);

    /// <summary>ذخیره مستقیم فیلدهای سئو (خروجی سئوی هوشمند پس از تأیید کارشناس).</summary>
    Task<OperationResult> SaveSeoAsync(Guid id, SeoMetaData seo, string? slug = null);

    Task<List<Product>> GetEntitiesForSitemapAsync();

    Task<Product?> GetEntityAsync(Guid id);

    Task<Dictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> ids);
}
