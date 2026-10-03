using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Products;

public class ProductService(FratakShopDbContext context) : IProductService
{
    public async Task<OperationResult> CreateProductAsync(Product product)
    {
        if (product is null)
            return OperationResult.Error("محصول نامعتبر است.");

        if (string.IsNullOrWhiteSpace(product.Name))
            return OperationResult.Error("نام محصول را وارد کنید.");

        if (string.IsNullOrWhiteSpace(product.Slug))
            return OperationResult.Error("اسلاگ محصول را وارد کنید.");

        if (string.IsNullOrWhiteSpace(product.SKU))
            return OperationResult.Error("کد SKU را وارد کنید.");

        var alreadyExists = await context.Products.AnyAsync(x => x.Slug == product.Slug && !x.IsDeleted);
        if (alreadyExists)
            return OperationResult.Error("این اسلاگ قبلاً ثبت شده است.");

        context.Products.Add(product);
        await context.SaveChangesAsync();

        return OperationResult.Success("محصول با موفقیت ایجاد شد.");
    }

    public async Task<OperationResult> UpdateProductAsync(Product product)
    {
        if (product is null)
            return OperationResult.Error("محصول نامعتبر است.");

        var entity = await context.Products.AsTracking().FirstOrDefaultAsync(x => x.Id == product.Id && !x.IsDeleted);
        if (entity is null)
            return OperationResult.NotFound("محصول مورد نظر یافت نشد.");

        if (string.IsNullOrWhiteSpace(product.Name))
            return OperationResult.Error("نام محصول را وارد کنید.");

        if (string.IsNullOrWhiteSpace(product.Slug))
            return OperationResult.Error("اسلاگ محصول را وارد کنید.");

        var slugExists = await context.Products.AnyAsync(x => x.Id != product.Id && x.Slug == product.Slug && !x.IsDeleted);
        if (slugExists)
            return OperationResult.Error("این اسلاگ قبلاً ثبت شده است.");

        entity.CategoryId = product.CategoryId;
        entity.Name = product.Name;
        entity.Slug = product.Slug;
        entity.SKU = product.SKU;
        entity.ShortDescription = product.ShortDescription;
        entity.Description = product.Description;
        entity.Price = product.Price;
        entity.DiscountPrice = product.DiscountPrice;
        entity.DiscountStartAt = product.DiscountStartAt;
        entity.DiscountEndAt = product.DiscountEndAt;
        entity.StockQuantity = product.StockQuantity;
        entity.WarrantyTitle = product.WarrantyTitle;
        entity.WarrantyMonths = product.WarrantyMonths;
        entity.Seo = product.Seo;
        entity.IsActive = product.IsActive;
        entity.PublishedAt = product.PublishedAt;

        await context.SaveChangesAsync();
        return OperationResult.Success("محصول با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteProductAsync(Guid id)
    {
        var entity = await context.Products.AsTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (entity is null)
            return OperationResult.NotFound("محصول مورد نظر یافت نشد.");

        context.Products.Remove(entity);
        await context.SaveChangesAsync();

        return OperationResult.Success("محصول با موفقیت حذف شد.");
    }

    public async Task<Product?> GetProductByIdAsync(Guid id)
    {
        return await context.Products
            .Include(x => x.Category)
            .Include(x => x.Images)
            .Include(x => x.Features)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
    }

    public async Task<Product?> GetProductBySlugAsync(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        return await context.Products
            .Include(x => x.Category)
            .Include(x => x.Images)
            .FirstOrDefaultAsync(x => x.Slug == slug && !x.IsDeleted);
    }

    public async Task<List<Product>> GetProductsAsync()
    {
        return await context.Products
            .Include(x => x.Category)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
    }
}
