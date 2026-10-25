using Fratak_Shop.Core.DTOs.Products.Command;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Mappers.Products;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Comments;
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

        if (command.PurchasePrice is < 0)
            return OperationResult.Error("قیمت خرید نمی‌تواند منفی باشد.");

        var categoryExists = await context.Categories.AnyAsync(x => x.Id == command.CategoryId);
        if (!categoryExists)
            return OperationResult.NotFound("دسته‌بندی مورد نظر یافت نشد.");

        if (command.BrandId.HasValue && !await context.Brands.AnyAsync(x => x.Id == command.BrandId.Value))
            return OperationResult.NotFound("برند مورد نظر یافت نشد.");

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

        var supplierError = await ValidateSupplierAsync(command.SupplierId, command.SupplierUrl, command.SupplierSku, command.SupplierNote, command.Gtin, command.Mpn);
        if (supplierError is not null)
            return OperationResult.Error(supplierError);

        var product = new Product
        {
            CategoryId = command.CategoryId,
            BrandId = command.BrandId,
            Name = command.Name.Trim(),
            EnglishName = command.EnglishName?.Trim(),
            Slug = slug,
            SKU = sku,
            ShortDescription = command.ShortDescription,
            Description = command.Description,
            Price = command.Price,
            DiscountPrice = command.DiscountPrice,
            PurchasePrice = command.PurchasePrice,
            DiscountStartAt = command.DiscountStartAt,
            DiscountEndAt = command.DiscountEndAt,
            StockQuantity = command.StockQuantity,
            WarrantyTitle = command.WarrantyTitle,
            WarrantyMonths = command.WarrantyMonths,
            IsActive = command.IsActive,
            PublishedAt = command.PublishedAt ?? (command.IsActive ? DateTimeOffset.UtcNow : null),
            Seo = command.Seo,
            Gtin = Clean(command.Gtin),
            Mpn = Clean(command.Mpn),
            SupplierId = command.SupplierId,
            SupplierUrl = Clean(command.SupplierUrl),
            SupplierSku = Clean(command.SupplierSku),
            SupplierNote = Clean(command.SupplierNote),
            ExcludeFromSupplierSync = command.ExcludeFromSupplierSync,
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

        if (command.PurchasePrice is < 0)
            return OperationResult.Error("قیمت خرید نمی‌تواند منفی باشد.");

        var categoryExists = await context.Categories.AnyAsync(x => x.Id == command.CategoryId);
        if (!categoryExists)
            return OperationResult.NotFound("دسته‌بندی مورد نظر یافت نشد.");

        if (command.BrandId.HasValue && !await context.Brands.AnyAsync(x => x.Id == command.BrandId.Value))
            return OperationResult.NotFound("برند مورد نظر یافت نشد.");

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

        var supplierError = await ValidateSupplierAsync(command.SupplierId, command.SupplierUrl, command.SupplierSku, command.SupplierNote, command.Gtin, command.Mpn);
        if (supplierError is not null)
            return OperationResult.Error(supplierError);

        product.CategoryId = command.CategoryId;
        product.BrandId = command.BrandId;
        product.Name = command.Name.Trim();
        product.EnglishName = command.EnglishName?.Trim();
        product.Slug = slug;
        product.SKU = sku;
        product.ShortDescription = command.ShortDescription;
        product.Description = command.Description;
        product.Price = command.Price;
        product.DiscountPrice = command.DiscountPrice;
        product.PurchasePrice = command.PurchasePrice;
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
        product.Gtin = Clean(command.Gtin);
        product.Mpn = Clean(command.Mpn);
        product.SupplierId = command.SupplierId;
        product.SupplierUrl = Clean(command.SupplierUrl);
        product.SupplierSku = Clean(command.SupplierSku);
        product.SupplierNote = Clean(command.SupplierNote);
        product.ExcludeFromSupplierSync = command.ExcludeFromSupplierSync;

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

    /// <summary>
    /// Filtered products with their final price (same rule as PriceCalculator, in SQL).
    /// <paramref name="skipSpecificationKey"/> leaves one specification out (facet counts).
    /// </summary>
    private IQueryable<PricedProduct> BuildFilterQuery(ProductFilterParams filterParams, string? skipSpecificationKey, bool skipBrands = false)
    {
        var now = DateTimeOffset.UtcNow;

        var query = context.Products.AsQueryable();

        // Every word (or one of its synonyms) has to match somewhere (name, English name, slug, SKU, brand, category).
        query = ProductSearch.Apply(query, filterParams.SearchTermGroups ?? ProductSearch.PlainGroups(filterParams.Search));

        if (filterParams.CategoryId.HasValue)
            query = query.Where(x => x.CategoryId == filterParams.CategoryId.Value
                                     || x.Category.ParentId == filterParams.CategoryId.Value);

        if (!string.IsNullOrWhiteSpace(filterParams.CategorySlug))
        {
            var categorySlug = filterParams.CategorySlug.Trim();
            query = query.Where(x => x.Category.Slug == categorySlug
                                     || (x.Category.ParentCategory != null && x.Category.ParentCategory.Slug == categorySlug));
        }

        if (filterParams.BrandId.HasValue)
            query = query.Where(x => x.BrandId == filterParams.BrandId.Value);

        var brandSlugs = filterParams.BrandSlugs
            .Where(slug => !string.IsNullOrWhiteSpace(slug))
            .Select(slug => slug.Trim())
            .Distinct()
            .ToList();

        if (brandSlugs.Count > 0 && !skipBrands)
            query = query.Where(x => x.Brand != null && brandSlugs.Contains(x.Brand.Slug));

        if (filterParams.ExcludeProductId.HasValue)
            query = query.Where(x => x.Id != filterParams.ExcludeProductId.Value);

        // Specification filters: OR inside one specification, AND between specifications.
        foreach (var (key, values) in NormalizeSpecifications(filterParams.Specifications))
        {
            if (key == skipSpecificationKey)
                continue;

            query = query.Where(x => x.Features.Any(f => f.Key == key && values.Contains(f.Value)));
        }

        // قیمت نهایی؛ همان قاعده PriceCalculator به زبان SQL.
        var withFinalPrice = query.Select(x => new
        {
            Product = x,
            HasDiscount = x.DiscountPrice != null
                          && x.DiscountPrice < x.Price
                          && (x.DiscountStartAt == null || x.DiscountStartAt <= now)
                          && (x.DiscountEndAt == null || x.DiscountEndAt >= now),
        }).Select(x => new PricedProduct
        {
            Product = x.Product,
            HasDiscount = x.HasDiscount,
            FinalPrice = x.HasDiscount ? x.Product.DiscountPrice!.Value : x.Product.Price,
        });

        if (filterParams.MinPrice.HasValue)
            withFinalPrice = withFinalPrice.Where(x => x.FinalPrice >= filterParams.MinPrice.Value);

        if (filterParams.MaxPrice.HasValue)
            withFinalPrice = withFinalPrice.Where(x => x.FinalPrice <= filterParams.MaxPrice.Value);

        if (filterParams.OnlyDiscounted == true)
            withFinalPrice = withFinalPrice.Where(x => x.HasDiscount);

        if (filterParams.OnlyAvailable == true)
            withFinalPrice = withFinalPrice.Where(x => x.Product.StockQuantity > 0);

        if (filterParams.OnlyActive == true)
            withFinalPrice = withFinalPrice.Where(x => x.Product.IsActive);

        return withFinalPrice;
    }

    public async Task<List<SpecificationFacetDto>> GetSpecificationFacetsAsync(ProductFilterParams filterParams,
        IReadOnlyList<SpecificationFacetRequest> attributes)
    {
        var selected = NormalizeSpecifications(filterParams.Specifications);
        var facets = new List<SpecificationFacetDto>();

        foreach (var attribute in attributes)
        {
            var productIds = BuildFilterQuery(filterParams, attribute.FeatureKey).Select(x => x.Product.Id);
            var featureKey = attribute.FeatureKey;

            var values = await context.ProductFeatures
                .Where(f => f.Key == featureKey && f.ValueType != FeatureValueType.Object && productIds.Contains(f.ProductId))
                .GroupBy(f => f.Value)
                .Select(g => new { Value = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(Math.Clamp(attribute.MaxValues, 1, 100))
                .ToListAsync();

            selected.TryGetValue(featureKey, out var chosen);
            if (values.Count == 0 && chosen is null)
                continue;

            facets.Add(new SpecificationFacetDto
            {
                Slug = attribute.Slug,
                Title = attribute.Title,
                FeatureKey = featureKey,
                Values = values
                    .Select(v => new SpecificationFacetValue { Value = v.Value, Count = v.Count, IsSelected = chosen?.Contains(v.Value) == true })
                    .Concat((chosen ?? []).Where(c => values.All(v => v.Value != c))
                        .Select(c => new SpecificationFacetValue { Value = c, Count = 0, IsSelected = true }))
                    .OrderByDescending(v => v.IsSelected).ThenBy(v => v.Value, StringComparer.CurrentCulture)
                    .ToList(),
            });
        }

        return facets;
    }

    public async Task<List<BrandFacetDto>> GetBrandFacetsAsync(ProductFilterParams filterParams, int take = 50)
    {
        var selected = filterParams.BrandSlugs.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToHashSet();

        // Brands of the current result set (brand selection itself left out, so others stay selectable).
        var brands = await BuildFilterQuery(filterParams, null, skipBrands: true)
            .Where(x => x.Product.Brand != null && x.Product.Brand.IsActive)
            .GroupBy(x => new { x.Product.Brand!.Slug, x.Product.Brand.Name, x.Product.Brand.EnglishName })
            .Select(g => new { g.Key.Slug, g.Key.Name, g.Key.EnglishName, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync();

        var result = brands
            .Select(b => new BrandFacetDto(b.Slug, b.Name, b.EnglishName, b.Count, selected.Contains(b.Slug)))
            .ToList();

        // Selected brands without products in this listing stay visible so they can be unchecked.
        var missing = selected.Where(slug => result.All(b => b.Slug != slug)).ToList();
        if (missing.Count > 0)
        {
            var extra = await context.Brands
                .Where(b => missing.Contains(b.Slug))
                .Select(b => new BrandFacetDto(b.Slug, b.Name, b.EnglishName, 0, true))
                .ToListAsync();
            result.AddRange(extra);
        }

        return result
            .OrderByDescending(b => b.IsSelected)
            .ThenByDescending(b => b.Count)
            .ThenBy(b => b.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    private static Dictionary<string, List<string>> NormalizeSpecifications(Dictionary<string, List<string>>? specifications)
    {
        var result = new Dictionary<string, List<string>>();
        if (specifications is null)
            return result;

        foreach (var (key, values) in specifications.Take(8))
        {
            var cleaned = values
                .Where(v => !string.IsNullOrWhiteSpace(v) && v.Length <= 200)
                .Select(v => v.Trim())
                .Distinct()
                .Take(10)
                .ToList();

            if (!string.IsNullOrWhiteSpace(key) && cleaned.Count > 0)
                result[key.Trim()] = cleaned;
        }

        return result;
    }

    private sealed class PricedProduct
    {
        public Product Product { get; set; } = null!;
        public bool HasDiscount { get; set; }
        public decimal FinalPrice { get; set; }
    }

    public async Task<ProductFilterResult> GetProductByFilterAsync(ProductFilterParams filterParams)
    {
        var withFinalPrice = BuildFilterQuery(filterParams, skipSpecificationKey: null);

        withFinalPrice = filterParams.SortBy switch
        {
            ProductSortBy.Oldest => withFinalPrice.OrderBy(x => x.Product.CreatedAt),
            ProductSortBy.Cheapest => withFinalPrice.OrderBy(x => x.FinalPrice),
            ProductSortBy.MostExpensive => withFinalPrice.OrderByDescending(x => x.FinalPrice),
            ProductSortBy.MostStock => withFinalPrice.OrderByDescending(x => x.Product.StockQuantity),
            ProductSortBy.MostViewed => withFinalPrice.OrderByDescending(x => x.Product.ViewCount)
                .ThenByDescending(x => x.Product.CreatedAt),
            ProductSortBy.BestSelling => withFinalPrice.OrderByDescending(x => x.Product.SalesCount)
                .ThenByDescending(x => x.Product.CreatedAt),
            ProductSortBy.MostDiscounted => withFinalPrice.OrderByDescending(x => x.Product.Price - x.FinalPrice)
                .ThenByDescending(x => x.Product.CreatedAt),
            _ => withFinalPrice.OrderByDescending(x => x.Product.CreatedAt),
        };

        // Include ها پس از پروجکشن دوباره اعمال می‌شوند تا قطعاً در نتیجه نهایی بارگذاری شوند.
        var productsQuery = withFinalPrice
            .Select(x => x.Product)
            .Include(x => x.Category)
            .Include(x => x.Brand)
            .Include(x => x.Images)
            .AsSplitQuery();

        var skip = (filterParams.PageId - 1) * filterParams.Take;

        var products = await productsQuery
            .Skip(skip)
            .Take(filterParams.Take)
            .ToListAsync();

        var data = products.MapFilterData();
        await FillRatingsAsync(data);

        var result = new ProductFilterResult
        {
            Data = data,
            FilterParams = filterParams,
        };

        result.GeneratePaging(await withFinalPrice.CountAsync(), filterParams.Take, filterParams.PageId);

        return result;
    }

    public async Task<ProductDto?> GetProductByIdAsync(Guid id)
    {
        var product = await ProductDetailsQuery().FirstOrDefaultAsync(x => x.Id == id);
        return await MapWithRatingAsync(product);
    }

    public async Task<ProductDto?> GetProductBySlugAsync(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        var normalizedSlug = slug.Trim();

        var product = await ProductDetailsQuery().FirstOrDefaultAsync(x => x.Slug == normalizedSlug);
        return await MapWithRatingAsync(product);
    }

    public async Task<List<ProductDto>> GetProductsAsync()
    {
        var products = await ProductDetailsQuery()
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        return products.Map();
    }

    public async Task<List<ProductDto>> GetProductsByIdsAsync(IEnumerable<Guid> ids)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
            return [];

        var products = await ProductDetailsQuery()
            .Where(x => idList.Contains(x.Id) && x.IsActive)
            .ToListAsync();

        var ratings = await GetRatingsAsync(idList);

        // ترتیب درخواستی حفظ می‌شود (مثلاً ترتیب افزودن به لیست مقایسه).
        return idList
            .Select(id => products.FirstOrDefault(p => p.Id == id))
            .Where(p => p is not null)
            .Select(p =>
            {
                var dto = p!.Map();
                ApplyRating(dto, ratings);
                return dto;
            })
            .ToList();
    }

    public async Task<List<ProductFilterData>> GetCardsByIdsAsync(IEnumerable<Guid> ids, bool onlyInStock = false)
    {
        var idList = ids.Distinct().Take(48).ToList();
        if (idList.Count == 0)
            return [];

        var products = await context.Products
            .Where(x => idList.Contains(x.Id) && x.IsActive && (!onlyInStock || x.StockQuantity > 0))
            .Include(x => x.Category)
            .Include(x => x.Brand)
            .Include(x => x.Images)
            .AsSplitQuery()
            .ToListAsync();

        var data = idList
            .Select(id => products.FirstOrDefault(p => p.Id == id))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList()
            .MapFilterData();

        await FillRatingsAsync(data);
        return data;
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
            .Include(x => x.Brand)
            .Include(x => x.Images)
            .OrderByDescending(x => x.SalesCount)
            .ThenByDescending(x => x.CreatedAt)
            .Take(take)
            .ToListAsync();

        var data = products.MapFilterData();
        await FillRatingsAsync(data);

        return data;
    }

    public async Task AddProductVisitAsync(Guid id)
    {
        // به‌روزرسانی مستقیم در دیتابیس تا بازدیدهای هم‌زمان از دست نروند.
        await context.Products
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ViewCount, x => x.ViewCount + 1));
    }

    private IQueryable<Product> ProductDetailsQuery()
    {
        return context.Products
            .Include(x => x.Category)
            .Include(x => x.Brand)
            .Include(x => x.Images)
            .Include(x => x.Features)
            .AsSplitQuery();
    }

    private async Task<ProductDto?> MapWithRatingAsync(Product? product)
    {
        if (product is null)
            return null;

        var dto = product.Map();
        var ratings = await GetRatingsAsync([product.Id]);
        ApplyRating(dto, ratings);

        return dto;
    }

    /// <summary>
    /// میانگین امتیاز و تعداد دیدگاه‌های تاییدشده هر محصول با یک کوئری گروه‌بندی‌شده.
    /// </summary>
    private async Task<Dictionary<Guid, (double Average, int Count)>> GetRatingsAsync(List<Guid> productIds)
    {
        if (productIds.Count == 0)
            return [];

        var rows = await context.Comments
            .Where(c => c.ProductId != null
                        && productIds.Contains(c.ProductId.Value)
                        && c.ParentId == null
                        && c.Status == CommentStatus.Approved)
            .GroupBy(c => c.ProductId!.Value)
            .Select(g => new
            {
                ProductId = g.Key,
                Count = g.Count(),
                Average = g.Where(c => c.Rating != null).Average(c => (double?)c.Rating) ?? 0,
            })
            .ToListAsync();

        return rows.ToDictionary(r => r.ProductId, r => (r.Average, r.Count));
    }

    private async Task FillRatingsAsync(List<ProductFilterData> products)
    {
        var ratings = await GetRatingsAsync(products.Select(p => p.Id).ToList());

        foreach (var product in products)
        {
            if (!ratings.TryGetValue(product.Id, out var rating))
                continue;

            product.AverageRating = Math.Round(rating.Average, 1);
            product.ReviewCount = rating.Count;
        }
    }

    private static void ApplyRating(ProductDto product, Dictionary<Guid, (double Average, int Count)> ratings)
    {
        if (!ratings.TryGetValue(product.Id, out var rating))
            return;

        product.AverageRating = Math.Round(rating.Average, 1);
        product.ReviewCount = rating.Count;
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

    /// <summary>
    /// The shop buys every product from a supplier: the exact source page is required (internal data,
    /// never shown on the storefront) so whoever handles an order knows where to buy each item.
    /// </summary>
    private async Task<string?> ValidateSupplierAsync(Guid? supplierId, string? supplierUrl, string? supplierSku, string? supplierNote, string? gtin, string? mpn)
    {
        if (string.IsNullOrWhiteSpace(supplierUrl))
            return "لینک منبع خرید (صفحه محصول در سایت تأمین‌کننده) الزامی است.";

        if (supplierUrl.Trim().Length > 1000
            || !Uri.TryCreate(supplierUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return "لینک منبع خرید باید یک آدرس کامل http یا https باشد.";

        if (supplierSku?.Trim().Length > 100 || supplierNote?.Trim().Length > 500)
            return "کد کالا یا یادداشت تأمین‌کننده بیش از حد طولانی است.";

        if (gtin?.Trim().Length > 14 || (gtin is { Length: > 0 } && !gtin.Trim().All(char.IsAsciiDigit)))
            return "شناسه GTIN/بارکد باید فقط عدد و حداکثر ۱۴ رقم باشد.";

        if (mpn?.Trim().Length > 50)
            return "شناسه MPN حداکثر ۵۰ نویسه است.";

        if (supplierId is { } id && !await context.Suppliers.AnyAsync(x => x.Id == id))
            return "تأمین‌کننده انتخاب‌شده یافت نشد.";

        return null;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
