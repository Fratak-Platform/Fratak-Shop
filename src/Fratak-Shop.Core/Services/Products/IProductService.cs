using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Products;

namespace Fratak_Shop.Core.Services.Products;

public interface IProductService
{
    Task<OperationResult> CreateProductAsync(Product product);
    Task<OperationResult> UpdateProductAsync(Product product);
    Task<OperationResult> DeleteProductAsync(Guid id);
    Task<Product?> GetProductByIdAsync(Guid id);
    Task<Product?> GetProductBySlugAsync(string slug);
    Task<List<Product>> GetProductsAsync();
}
