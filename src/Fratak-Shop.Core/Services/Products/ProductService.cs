using Fratak_Shop.Core.DTOs.Products.Command;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Mappers.Products;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Products;

public class ProductService(FratakShopDbContext context) : IProductService
{
    public async Task<OperationResult> CreateProductAsync(CreateProductCommand command)
    {
        var error = Validate(command.Name, command.Slug, command.SKU, command.Price, command.DiscountPrice,
            command.DiscountStartAt, command.DiscountEndAt, command.StockQuantity, command.WarrantyMonths);

        if (error is not null)
            return OperationResult.Error(error);

        var categoryExists = await context.Categories.AnyAsync(x => x.Id == command.CategoryId);
        if (!categoryExists)
            return OperationResult.NotFound("دسته‌بندی مورد نظر یافت نشد.");

        var slug = command.Slug.Trim();
        var sku = command.SKU.Trim();

        var slugExists = await context.Products.AnyAsync(x => x.Slug == slug);
        if (slugExists)
            return OperationResult.Error("این اسلاگ قبلاً برای محصول دیگری ثبت شده است.");

        var skuExists = await context.Products.AnyAsync(x => x.SKU == sku);
        if (skuExists)
            return OperationResult.Error("این کد کالا (SKU) قبلاً ثبت شده است.");

        var childError = ValidateChildren(command.Images, command.Features);
        if (childError is not null)
            return OperationResult.Error(childError);

        var product = new Product
        {
            CategoryId = command.CategoryId,
            Name = command.Name.Trim(),
            Slug = slug,
            SKU = sku,
            ShortDescription = command.ShortDescription,
            Description = command.Description,
            Price = command.Price,
            DiscountPrice = command.DiscountPrice,
            DiscountStartAt = command.DiscountStartAt,
            DiscountEndAt = command.DiscountEndAt,
            StockQuantity = command.StockQuantity,
            WarrantyTitle = command.WarrantyTitle,
            WarrantyMonths = command.WarrantyMonths,
            IsActive = command.IsActive,
            PublishedAt = command.PublishedAt ?? (command.IsActive ? DateTimeOffset.UtcNow : null),
            Seo = command.Seo,
            Images = BuildImages(command.Images),
            Features = BuildFeatures(command.Features),
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        return OperationResult.Success("محصول با موفقیت ایجاد شد.");
    }

    public async Task<OperationResult> EditProductAsync(EditProductCommand command)
    {
        var product = await context.Products
            .AsTracking()
            .Include(x => x.Images)
            .Include(x => x.Features)
            .FirstOrDefaultAsync(x => x.Id == command.Id);

        if (product is null)
            return OperationResult.NotFound("محصول مورد نظر یافت نشد.");

        var error = Validate(command.Name, command.Slug, command.SKU, command.Price, command.DiscountPrice,
            command.DiscountStartAt, command.DiscountEndAt, command.StockQuantity, command.WarrantyMonths);

        if (error is not null)
            return OperationResult.Error(error);

        var categoryExists = await context.Categories.AnyAsync(x => x.Id == command.CategoryId);
        if (!categoryExists)
            return OperationResult.NotFound("دسته‌بندی مورد نظر یافت نشد.");

        var slug = command.Slug.Trim();
        var sku = command.SKU.Trim();

        var slugExists = await context.Products
            .AnyAsync(x => x.Id != command.Id && x.Slug == slug);
        if (slugExists)
            return OperationResult.Error("این اسلاگ قبلاً برای محصول دیگری ثبت شده است.");

        var skuExists = await context.Products
            .AnyAsync(x => x.Id != command.Id && x.SKU == sku);
        if (skuExists)
            return OperationResult.Error("این کد کالا (SKU) قبلاً ثبت شده است.");

        var childError = ValidateChildren(command.Images, command.Features);
        if (childError is not null)
            return OperationResult.Error(childError);

        product.CategoryId = command.CategoryId;
        product.Name = command.Name.Trim();
        product.Slug = slug;
        product.SKU = sku;
        product.ShortDescription = command.ShortDescription;
        product.Description = command.Description;
        product.Price = command.Price;
        product.DiscountPrice = command.DiscountPrice;
        product.DiscountStartAt = command.DiscountStartAt;
        product.DiscountEndAt = command.DiscountEndAt;
        product.StockQuantity = command.StockQuantity;
        product.WarrantyTitle = command.WarrantyTitle;
        product.WarrantyMonths = command.WarrantyMonths;
        product.IsActive = command.IsActive;
        product.PublishedAt = command.PublishedAt
                             ?? product.PublishedAt
                             ?? (command.IsActive ? DateTimeOffset.UtcNow : null);
        product.Seo = command.Seo;

        ReplaceImages(product, command.Images);
        SyncFeatures(product, command.Features);

        await context.SaveChangesAsync();

        return OperationResult.Success("محصول با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteProductAsync(Guid id)
    {
        var product = await context.Products.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (product is null)
            return OperationResult.NotFound("محصول مورد نظر یافت نشد.");

        context.Products.Remove(product);
        await context.SaveChangesAsync();

        return OperationResult.Success("محصول با موفقیت حذف شد.");
    }

    public async Task<OperationResult> ChangeProductActivationAsync(Guid id, bool isActive)
    {
        var product = await context.Products.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (product is null)
            return OperationResult.NotFound("محصول مورد نظر یافت نشد.");

        product.IsActive = isActive;

        if (isActive && product.PublishedAt is null)
            product.PublishedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync();

        return OperationResult.Success(isActive
            ? "محصول فعال شد."
            : "محصول غیرفعال شد.");
    }

    public async Task<OperationResult> UpdateStockAsync(Guid id, int stockQuantity)
    {
        if (stockQuantity < 0)
            return OperationResult.Error("موجودی انبار نمی‌تواند منفی باشد.");

        var product = await context.Products.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (product is null)
            return OperationResult.NotFound("محصول مورد نظر یافت نشد.");

        product.StockQuantity = stockQuantity;
        await context.SaveChangesAsync();

        return OperationResult.Success("موجودی محصول با موفقیت به‌روزرسانی شد.");
    }

    public async Task<ProductFilterResult> GetProductByFilterAsync(ProductFilterParams filterParams)
    {
        var query = context.Products
            .Include(x => x.Category)
            .Include(x => x.Images)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filterParams.Search))
        {
            var search = filterParams.Search.Trim();
            query = query.Where(x => x.Name.Contains(search)
                                     || x.Slug.Contains(search)
                                     || x.SKU.Contains(search));
        }

        if (filterParams.CategoryId.HasValue)
            query = query.Where(x => x.CategoryId == filterParams.CategoryId.Value);

        if (!string.IsNullOrWhiteSpace(filterParams.CategorySlug))
        {
            var categorySlug = filterParams.CategorySlug.Trim();
            query = query.Where(x => x.Category.Slug == categorySlug);
        }

        if (filterParams.MinPrice.HasValue)
            query = query.Where(x => x.Price >= filterParams.MinPrice.Value);

        if (filterParams.MaxPrice.HasValue)
            query = query.Where(x => x.Price <= filterParams.MaxPrice.Value);

        if (filterParams.OnlyAvailable == true)
            query = query.Where(x => x.StockQuantity > 0);

        if (filterParams.OnlyActive == true)
            query = query.Where(x => x.IsActive);

        query = filterParams.SortBy switch
        {
            ProductSortBy.Oldest => query.OrderBy(x => x.CreatedAt),
            ProductSortBy.Cheapest => query.OrderBy(x => x.Price),
            ProductSortBy.MostExpensive => query.OrderByDescending(x => x.Price),
            ProductSortBy.MostStock => query.OrderByDescending(x => x.StockQuantity),
            _ => query.OrderByDescending(x => x.CreatedAt),
        };

        var skip = (filterParams.PageId - 1) * filterParams.Take;

        var products = await query
            .Skip(skip)
            .Take(filterParams.Take)
            .ToListAsync();

        var result = new ProductFilterResult
        {
            Data = products.MapFilterData(),
            FilterParams = filterParams,
        };

        result.GeneratePaging(query, filterParams.Take, filterParams.PageId);

        return result;
    }

    public async Task<ProductDto?> GetProductByIdAsync(Guid id)
    {
        var product = await context.Products
            .Include(x => x.Category)
            .Include(x => x.Images)
            .Include(x => x.Features)
            .FirstOrDefaultAsync(x => x.Id == id);

        return product?.Map();
    }

    public async Task<ProductDto?> GetProductBySlugAsync(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        var product = await context.Products
            .Include(x => x.Category)
            .Include(x => x.Images)
            .Include(x => x.Features)
            .FirstOrDefaultAsync(x => x.Slug == slug.Trim());

        return product?.Map();
    }

    public async Task<List<ProductDto>> GetProductsAsync()
    {
        var products = await context.Products
            .Include(x => x.Category)
            .Include(x => x.Images)
            .Include(x => x.Features)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        return products.Map();
    }

    public async Task<List<ProductFilterData>> GetRelatedProductsAsync(Guid productId, int take = 8)
    {
        if (take <= 0)
            take = 8;

        if (take > 24)
            take = 24;

        var product = await context.Products.FirstOrDefaultAsync(x => x.Id == productId);
        if (product is null)
            return [];

        var products = await context.Products
            .Where(x => x.Id != productId
                        && x.CategoryId == product.CategoryId
                        && x.IsActive
                        && x.StockQuantity > 0)
            .Include(x => x.Category)
            .Include(x => x.Images)
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .ToListAsync();

        return products.MapFilterData();
    }

    private static List<ProductImage> BuildImages(List<ProductImageCommand> images)
    {
        var result = images
            .Select(image => new ProductImage
            {
                ImageName = image.ImageName.Trim(),
                AltText = image.AltText?.Trim(),
                DisplayOrder = image.DisplayOrder,
                IsPrimary = image.IsPrimary,
            })
            .ToList();

        // اگر هیچ تصویری به‌عنوان تصویر اصلی انتخاب نشده باشد، اولین تصویر اصلی می‌شود.
        if (result.Count > 0 && result.All(image => !image.IsPrimary))
            result[0].IsPrimary = true;

        return result;
    }

    private static List<ProductFeature> BuildFeatures(List<ProductFeatureCommand> features)
    {
        return features
            .Select(feature => new ProductFeature
            {
                Key = feature.Key.Trim(),
                Value = feature.Value.Trim(),
                ValueType = feature.ValueType,
                DisplayOrder = feature.DisplayOrder,
            })
            .ToList();
    }

    private static void ReplaceImages(Product product, List<ProductImageCommand> images)
    {
        product.Images.Clear();

        foreach (var image in BuildImages(images))
            product.Images.Add(image);
    }

    /// <summary>
    /// ویژگی‌ها بر اساس کلید همگام می‌شوند (نه حذف و درج مجدد) تا ایندکس یکتای
    /// «محصول + کلید ویژگی» هنگام ویرایش محصول نقض نشود.
    /// </summary>
    private static void SyncFeatures(Product product, List<ProductFeatureCommand> features)
    {
        var keys = features.Select(feature => feature.Key.Trim()).ToList();

        foreach (var feature in product.Features.ToList())
        {
            if (!keys.Contains(feature.Key, StringComparer.OrdinalIgnoreCase))
                product.Features.Remove(feature);
        }

        foreach (var command in features)
        {
            var key = command.Key.Trim();

            var feature = product.Features
                .FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

            if (feature is null)
            {
                product.Features.Add(new ProductFeature
                {
                    Key = key,
                    Value = command.Value.Trim(),
                    ValueType = command.ValueType,
                    DisplayOrder = command.DisplayOrder,
                });

                continue;
            }

            feature.Key = key;
            feature.Value = command.Value.Trim();
            feature.ValueType = command.ValueType;
            feature.DisplayOrder = command.DisplayOrder;
        }
    }

    private static string? Validate(string? name, string? slug, string? sku, decimal price, decimal? discountPrice,
        DateTimeOffset? discountStartAt, DateTimeOffset? discountEndAt, int stockQuantity, int? warrantyMonths)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "نام محصول را وارد کنید.";

        if (string.IsNullOrWhiteSpace(slug))
            return "اسلاگ محصول را وارد کنید.";

        if (string.IsNullOrWhiteSpace(sku))
            return "کد کالا (SKU) را وارد کنید.";

        if (price < 0)
            return "قیمت محصول نمی‌تواند منفی باشد.";

        if (discountPrice is < 0)
            return "مبلغ تخفیف نمی‌تواند منفی باشد.";

        if (discountPrice.HasValue && discountPrice.Value >= price)
            return "مبلغ تخفیف باید از قیمت محصول کمتر باشد.";

        if (discountStartAt.HasValue && discountEndAt.HasValue && discountStartAt > discountEndAt)
            return "تاریخ شروع تخفیف نباید بعد از تاریخ پایان آن باشد.";

        if (stockQuantity < 0)
            return "موجودی انبار نمی‌تواند منفی باشد.";

        if (warrantyMonths is < 0)
            return "مدت گارانتی نمی‌تواند منفی باشد.";

        return null;
    }

    private static string? ValidateChildren(List<ProductImageCommand> images, List<ProductFeatureCommand> features)
    {
        if (images.Any(image => string.IsNullOrWhiteSpace(image.ImageName)))
            return "نام تصویر محصول را وارد کنید.";

        if (features.Any(feature => string.IsNullOrWhiteSpace(feature.Key) || string.IsNullOrWhiteSpace(feature.Value)))
            return "نام و مقدار ویژگی‌های محصول را وارد کنید.";

        var duplicateFeature = features
            .GroupBy(feature => feature.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1);

        if (duplicateFeature)
            return "برای یک محصول نمی‌توان دو ویژگی با نام یکسان ثبت کرد.";

        return null;
    }
}
