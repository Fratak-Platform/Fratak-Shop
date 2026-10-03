using Fratak_Shop.Core.DTOs.Products.Command;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Mappers.Product;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Utilities.SeoManagement;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Products;

public class ProductService(FratakShopDbContext context, ISeoStatisticsService seoStatistics) : IProductService
{
    // ---------------------------------------------------------------- storefront
    public async Task<ProductFilterResult> GetFilterAsync(ProductFilterParams filterParams)
    {
        var query = context.Products
            .Include(product => product.Category)
            .Include(product => product.Images)
            .AsQueryable();

        if (!filterParams.IncludeInactive)
            query = query.Where(product => product.IsActive);

        if (filterParams.IsActive.HasValue)
            query = query.Where(product => product.IsActive == filterParams.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(filterParams.Search))
        {
            var search = filterParams.Search.Trim();
            query = query.Where(product => product.Name.Contains(search) ||
                                           product.SKU.Contains(search) ||
                                           product.Slug.Contains(search) ||
                                           (product.ShortDescription != null && product.ShortDescription.Contains(search)));
        }

        if (filterParams.CategoryId.HasValue)
        {
            var categoryIds = await GetCategoryAndChildrenIdsAsync(filterParams.CategoryId.Value);
            query = query.Where(product => categoryIds.Contains(product.CategoryId));
        }
        else if (!string.IsNullOrWhiteSpace(filterParams.CategorySlug))
        {
            var category = await context.Categories
                .Where(item => item.Slug == filterParams.CategorySlug)
                .Select(item => (Guid?)item.Id)
                .FirstOrDefaultAsync();

            if (category is null)
                return EmptyResult(filterParams);

            var categoryIds = await GetCategoryAndChildrenIdsAsync(category.Value);
            query = query.Where(product => categoryIds.Contains(product.CategoryId));
        }

        if (filterParams.MinPrice.HasValue)
            query = query.Where(product => product.Price >= filterParams.MinPrice.Value);

        if (filterParams.MaxPrice.HasValue)
            query = query.Where(product => product.Price <= filterParams.MaxPrice.Value);

        if (filterParams.OnlyInStock)
            query = query.Where(product => product.StockQuantity > 0);

        if (filterParams.OnlyFeatured)
            query = query.Where(product => product.IsFeatured);

        if (filterParams.OnlyDiscounted)
            query = query.Where(product => product.DiscountPrice != null && product.DiscountPrice < product.Price);

        if (filterParams.LowStock == true)
            query = query.Where(product => product.StockQuantity <= 5);

        if (filterParams.MissingSeo == true)
            query = query.Where(product => product.Seo == null || product.Seo.Title == null || product.Seo.SeoScore == null);

        query = filterParams.SortBy switch
        {
            ProductSortBy.Oldest => query.OrderBy(product => product.CreatedAt),
            ProductSortBy.Cheapest => query.OrderBy(product => product.Price),
            ProductSortBy.MostExpensive => query.OrderByDescending(product => product.Price),
            ProductSortBy.MostViewed => query.OrderByDescending(product => product.ViewCount),
            ProductSortBy.MostDiscounted => query
                .OrderByDescending(product => product.DiscountPrice != null && product.DiscountPrice < product.Price)
                .ThenBy(product => product.DiscountPrice),
            ProductSortBy.BestRated => query.OrderByDescending(product => product.Comments.Average(comment => (double?)comment.Rating) ?? 0),
            ProductSortBy.BestSelling => query.OrderByDescending(product => product.Id),
            _ => query.OrderByDescending(product => product.CreatedAt),
        };

        var total = await query.CountAsync();
        var skip = (filterParams.PageId - 1) * filterParams.Take;

        var products = await query
            .Skip(skip)
            .Take(filterParams.Take)
            .ToListAsync();

        var ids = products.Select(product => product.Id).ToList();
        var ratings = await GetRatingsAsync(ids);
        var soldCounts = await GetSoldCountsAsync(ids);

        var data = products
            .Select(product => product.MapCard(
                ratings.GetValueOrDefault(product.Id).Average,
                ratings.GetValueOrDefault(product.Id).Count,
                soldCounts.GetValueOrDefault(product.Id)))
            .ToList();

        if (filterParams.SortBy == ProductSortBy.BestSelling)
            data = data.OrderByDescending(card => card.SoldCount).ToList();

        var result = new ProductFilterResult
        {
            Data = data,
            FilterParams = filterParams,
        };
        result.GeneratePaging(total, filterParams.Take, filterParams.PageId);

        return result;
    }

    public async Task<ProductDetailDto?> GetDetailBySlugAsync(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        var product = await context.Products
            .Include(item => item.Category)
            .Include(item => item.Images)
            .Include(item => item.Features)
            .FirstOrDefaultAsync(item => item.Slug == slug);

        if (product is null)
            return null;

        return await BuildDetailAsync(product);
    }

    public async Task<ProductDetailDto?> GetDetailByIdAsync(Guid id, bool includeInactive = false)
    {
        var query = context.Products
            .Include(item => item.Category)
            .Include(item => item.Images)
            .Include(item => item.Features)
            .Where(item => item.Id == id);

        if (!includeInactive)
            query = query.Where(item => item.IsActive);

        var product = await query.FirstOrDefaultAsync();
        return product is null ? null : await BuildDetailAsync(product);
    }

    public async Task<List<ProductCardDto>> GetCardsByIdsAsync(IEnumerable<Guid> ids)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
            return [];

        var products = await context.Products
            .Include(product => product.Category)
            .Include(product => product.Images)
            .Where(product => idList.Contains(product.Id))
            .ToListAsync();

        var ratings = await GetRatingsAsync(idList);
        var soldCounts = await GetSoldCountsAsync(idList);

        return products
            .Select(product => product.MapCard(
                ratings.GetValueOrDefault(product.Id).Average,
                ratings.GetValueOrDefault(product.Id).Count,
                soldCounts.GetValueOrDefault(product.Id)))
            .OrderBy(card => idList.IndexOf(card.Id))
            .ToList();
    }

    public async Task<List<ProductCardDto>> GetFeaturedAsync(int take = 8) =>
        await BuildCardListAsync(context.Products
            .Where(product => product.IsActive && product.IsFeatured)
            .OrderByDescending(product => product.CreatedAt), take);

    public async Task<List<ProductCardDto>> GetDiscountedAsync(int take = 8) =>
        await BuildCardListAsync(context.Products
            .Where(product => product.IsActive && product.DiscountPrice != null && product.DiscountPrice < product.Price)
            .OrderByDescending(product => product.DiscountPrice), take);

    public async Task<List<ProductCardDto>> GetLatestAsync(int take = 8) =>
        await BuildCardListAsync(context.Products
            .Where(product => product.IsActive)
            .OrderByDescending(product => product.CreatedAt), take);

    public async Task<List<ProductCardDto>> GetBestSellingAsync(int take = 8)
    {
        var bestSellingIds = await context.OrderItems
            .Where(item => item.ProductId != null &&
                           item.Order.Status != OrderStatus.Cancelled &&
                           item.Order.Status != OrderStatus.Returned)
            .GroupBy(item => item.ProductId!.Value)
            .OrderByDescending(group => group.Sum(item => item.Quantity))
            .Select(group => group.Key)
            .Take(take)
            .ToListAsync();

        if (bestSellingIds.Count == 0)
            return await GetFeaturedAsync(take);

        var cards = await GetCardsByIdsAsync(bestSellingIds);
        return cards.OrderBy(card => bestSellingIds.IndexOf(card.Id)).ToList();
    }

    public async Task<List<ProductCardDto>> GetRelatedAsync(Guid productId, int take = 8)
    {
        var product = await context.Products
            .Where(item => item.Id == productId)
            .Select(item => new { item.CategoryId, item.Id })
            .FirstOrDefaultAsync();

        if (product is null)
            return [];

        var related = await context.Products
            .Include(item => item.Category)
            .Include(item => item.Images)
            .Where(item => item.IsActive && item.CategoryId == product.CategoryId && item.Id != product.Id)
            .OrderByDescending(item => item.ViewCount)
            .Take(take)
            .ToListAsync();

        if (related.Count < take)
        {
            var ids = related.Select(item => item.Id).Append(product.Id).ToList();
            var extra = await context.Products
                .Include(item => item.Category)
                .Include(item => item.Images)
                .Where(item => item.IsActive && !ids.Contains(item.Id))
                .OrderByDescending(item => item.ViewCount)
                .Take(take - related.Count)
                .ToListAsync();

            related.AddRange(extra);
        }

        var relatedIds = related.Select(item => item.Id).ToList();
        var ratings = await GetRatingsAsync(relatedIds);
        var soldCounts = await GetSoldCountsAsync(relatedIds);

        return related
            .Select(item => item.MapCard(
                ratings.GetValueOrDefault(item.Id).Average,
                ratings.GetValueOrDefault(item.Id).Count,
                soldCounts.GetValueOrDefault(item.Id)))
            .ToList();
    }

    public async Task<List<ProductCardDto>> SearchAsync(string term, int take = 10)
    {
        term = term?.Trim() ?? string.Empty;
        if (term.Length < 2)
            return [];

        return await BuildCardListAsync(context.Products
            .Where(product => product.IsActive &&
                              (product.Name.Contains(term) ||
                               product.SKU.Contains(term) ||
                               (product.ShortDescription != null && product.ShortDescription.Contains(term))))
            .OrderByDescending(product => product.ViewCount), take);
    }

    public async Task RegisterVisitAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var product = await context.Products.AsTracking().FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
        if (product is null)
            return;

        product.ViewCount++;
        await context.SaveChangesAsync(cancellationToken);

        await seoStatistics.RegisterVisitAsync(Data.Entities.Seo.SeoEntityType.Product, productId, cancellationToken);
    }

    // ---------------------------------------------------------------- admin
    public async Task<ProductDto?> GetForEditAsync(Guid id)
    {
        var product = await context.Products
            .Include(item => item.Images)
            .Include(item => item.Features)
            .FirstOrDefaultAsync(item => item.Id == id);

        return product?.MapEditDto();
    }

    public async Task<OperationResult> CreateProductAsync(CreateProductCommand command)
    {
        if (command is null)
            return OperationResult.Error("اطلاعات محصول نامعتبر است.");

        var validation = await ValidateAsync(command, null);
        if (!validation.IsSuccess())
            return validation;

        var slug = string.IsNullOrWhiteSpace(command.Slug)
            ? SlugHelper.Generate(command.Name)
            : SlugHelper.Generate(command.Slug);

        var product = new Product
        {
            CategoryId = command.CategoryId,
            Name = command.Name.Trim(),
            Slug = await MakeSlugUniqueAsync(slug, null),
            SKU = command.SKU.Trim(),
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
            IsFeatured = command.IsFeatured,
            PublishedAt = command.PublishedAt ?? DateTimeOffset.UtcNow,
            Seo = command.Seo ?? new SeoMetaData(),
        };

        ApplyImages(product, command.Images);
        ApplyFeatures(product, command.Features);

        context.Products.Add(product);
        await context.SaveChangesAsync();

        return OperationResult.Success("محصول با موفقیت ایجاد شد.");
    }

    public async Task<OperationResult> UpdateProductAsync(EditProductCommand command)
    {
        var product = await context.Products
            .AsTracking()
            .Include(item => item.Images)
            .Include(item => item.Features)
            .FirstOrDefaultAsync(item => item.Id == command.Id);

        if (product is null)
            return OperationResult.NotFound("محصول مورد نظر یافت نشد.");

        var validation = await ValidateAsync(command, command.Id);
        if (!validation.IsSuccess())
            return validation;

        var slug = string.IsNullOrWhiteSpace(command.Slug)
            ? SlugHelper.Generate(command.Name)
            : SlugHelper.Generate(command.Slug);

        product.CategoryId = command.CategoryId;
        product.Name = command.Name.Trim();
        product.Slug = await MakeSlugUniqueAsync(slug, command.Id);
        product.SKU = command.SKU.Trim();
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
        product.IsFeatured = command.IsFeatured;
        product.PublishedAt = command.PublishedAt ?? product.PublishedAt;
        product.Seo = command.Seo ?? product.Seo ?? new SeoMetaData();

        // images: remove the ones that are no longer submitted, add the new ones
        var submittedImages = command.Images.Select(image => image.ImageName).ToList();
        foreach (var image in product.Images.Where(image => !submittedImages.Contains(image.ImageName)).ToList())
            context.ProductImages.Remove(image);

        ApplyImages(product, command.Images.Where(image => product.Images.All(existing => existing.ImageName != image.ImageName)).ToList());

        var submittedFeatures = command.Features.Select(feature => feature.Key).ToList();
        foreach (var feature in product.Features.Where(feature => !submittedFeatures.Contains(feature.Key)).ToList())
            context.ProductFeatures.Remove(feature);

        foreach (var featureCommand in command.Features)
        {
            var feature = product.Features.FirstOrDefault(item => item.Key == featureCommand.Key);
            if (feature is null)
            {
                product.Features.Add(new ProductFeature
                {
                    Key = featureCommand.Key.Trim(),
                    Value = featureCommand.Value,
                    ValueType = featureCommand.ValueType,
                    DisplayOrder = featureCommand.DisplayOrder,
                });
            }
            else
            {
                feature.Value = featureCommand.Value;
                feature.ValueType = featureCommand.ValueType;
                feature.DisplayOrder = featureCommand.DisplayOrder;
            }
        }

        await context.SaveChangesAsync();
        return OperationResult.Success("محصول با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteProductAsync(Guid id)
    {
        var product = await context.Products.AsTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (product is null)
            return OperationResult.NotFound("محصول مورد نظر یافت نشد.");

        var usedInOrders = await context.OrderItems.AnyAsync(item => item.ProductId == id);
        if (usedInOrders)
        {
            product.IsActive = false;
            await context.SaveChangesAsync();
            return OperationResult.Success("این محصول در سفارش‌های ثبت‌شده استفاده شده است؛ بنابراین به‌جای حذف، غیرفعال شد.");
        }

        context.Products.Remove(product);
        await context.SaveChangesAsync();

        return OperationResult.Success("محصول با موفقیت حذف شد.");
    }

    public async Task<OperationResult> ToggleActiveAsync(Guid id)
    {
        var product = await context.Products.AsTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (product is null)
            return OperationResult.NotFound("محصول مورد نظر یافت نشد.");

        product.IsActive = !product.IsActive;
        await context.SaveChangesAsync();

        return OperationResult.Success(product.IsActive ? "محصول فعال شد." : "محصول غیرفعال شد.");
    }

    public async Task<OperationResult> ToggleFeaturedAsync(Guid id)
    {
        var product = await context.Products.AsTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (product is null)
            return OperationResult.NotFound("محصول مورد نظر یافت نشد.");

        product.IsFeatured = !product.IsFeatured;
        await context.SaveChangesAsync();

        return OperationResult.Success(product.IsFeatured ? "محصول به پیشنهادهای ویژه اضافه شد." : "محصول از پیشنهادهای ویژه حذف شد.");
    }

    public async Task<OperationResult> UpdateStockAsync(Guid id, int stockQuantity)
    {
        if (stockQuantity < 0)
            return OperationResult.Error("موجودی نمی‌تواند منفی باشد.");

        var product = await context.Products.AsTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (product is null)
            return OperationResult.NotFound("محصول مورد نظر یافت نشد.");

        product.StockQuantity = stockQuantity;
        await context.SaveChangesAsync();

        return OperationResult.Success("موجودی محصول به‌روزرسانی شد.");
    }

    public async Task<OperationResult> SaveSeoAsync(Guid id, SeoMetaData seo, string? slug = null)
    {
        var product = await context.Products.AsTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (product is null)
            return OperationResult.NotFound("محصول مورد نظر یافت نشد.");

        seo.SeoScore = seo.SeoScore ?? product.Seo?.SeoScore;
        seo.LastAuditedAt = DateTimeOffset.UtcNow;
        product.Seo = seo;

        if (!string.IsNullOrWhiteSpace(slug))
            product.Slug = await MakeSlugUniqueAsync(SlugHelper.Generate(slug), id);

        await context.SaveChangesAsync();
        return OperationResult.Success("اطلاعات سئو ذخیره شد.");
    }

    public async Task<List<Product>> GetEntitiesForSitemapAsync() =>
        await context.Products
            .Include(product => product.Images)
            .Where(product => product.IsActive)
            .OrderByDescending(product => product.UpdatedAt)
            .ToListAsync();

    public async Task<Product?> GetEntityAsync(Guid id) =>
        await context.Products
            .Include(product => product.Category)
            .Include(product => product.Images)
            .Include(product => product.Features)
            .AsTracking()
            .FirstOrDefaultAsync(product => product.Id == id);

    public async Task<Dictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> ids)
    {
        var idList = ids.Distinct().ToList();
        return await context.Products
            .Where(product => idList.Contains(product.Id))
            .Select(product => new { product.Id, product.Name })
            .ToDictionaryAsync(item => item.Id, item => item.Name);
    }

    // ---------------------------------------------------------------- helpers
    private async Task<ProductDetailDto> BuildDetailAsync(Product product)
    {
        var ratings = await GetRatingsAsync([product.Id]);
        var soldCounts = await GetSoldCountsAsync([product.Id]);
        var detail = product.MapDetail(
            ratings.GetValueOrDefault(product.Id).Average,
            ratings.GetValueOrDefault(product.Id).Count,
            soldCounts.GetValueOrDefault(product.Id));

        var breakdown = await context.Comments
            .Where(comment => comment.ProductId == product.Id && comment.Rating != null)
            .GroupBy(comment => comment.Rating!.Value)
            .Select(group => new { Stars = group.Key, Count = group.Count() })
            .ToListAsync();

        var totalRatings = breakdown.Sum(item => item.Count);
        detail.RatingBreakdown = Enumerable.Range(1, 5)
            .Select(stars =>
            {
                var count = breakdown.FirstOrDefault(item => item.Stars == stars)?.Count ?? 0;
                return new RatingBreakdownDto
                {
                    Stars = (byte)stars,
                    Count = count,
                    Percent = totalRatings == 0 ? 0 : (int)Math.Round(count * 100d / totalRatings),
                };
            })
            .OrderByDescending(item => item.Stars)
            .ToList();

        detail.RelatedProducts = await GetRelatedAsync(product.Id, 8);
        detail.Breadcrumbs = await BuildBreadcrumbsAsync(product.CategoryId, product.Name, product.Slug);

        return detail;
    }

    private async Task<List<BreadcrumbDto>> BuildBreadcrumbsAsync(Guid categoryId, string title, string slug)
    {
        var breadcrumbs = new List<BreadcrumbDto>
        {
            new() { Title = "فروشگاه", Url = "/products" },
        };

        var category = await context.Categories
            .Where(item => item.Id == categoryId)
            .Select(item => new { item.Id, item.Name, item.Slug, item.ParentId })
            .FirstOrDefaultAsync();

        var chain = new List<(string Name, string Slug)>();
        var cursor = category;
        var guard = 0;
        while (cursor is not null && guard++ < 5)
        {
            chain.Insert(0, (cursor.Name, cursor.Slug));
            var parentId = cursor.ParentId;
            cursor = parentId is null
                ? null
                : await context.Categories
                    .Where(item => item.Id == parentId)
                    .Select(item => new { item.Id, item.Name, item.Slug, item.ParentId })
                    .FirstOrDefaultAsync();
        }

        breadcrumbs.AddRange(chain.Select(item => new BreadcrumbDto { Title = item.Name, Url = $"/category/{item.Slug}" }));
        breadcrumbs.Add(new BreadcrumbDto { Title = title, Url = $"/product/{slug}", IsCurrent = true });

        return breadcrumbs;
    }

    private async Task<List<ProductCardDto>> BuildCardListAsync(IQueryable<Product> query, int take)
    {
        var products = await query
            .Include(product => product.Category)
            .Include(product => product.Images)
            .Take(take)
            .ToListAsync();

        var ids = products.Select(product => product.Id).ToList();
        var ratings = await GetRatingsAsync(ids);
        var soldCounts = await GetSoldCountsAsync(ids);

        return products
            .Select(product => product.MapCard(
                ratings.GetValueOrDefault(product.Id).Average,
                ratings.GetValueOrDefault(product.Id).Count,
                soldCounts.GetValueOrDefault(product.Id)))
            .ToList();
    }

    private async Task<List<Guid>> GetCategoryAndChildrenIdsAsync(Guid categoryId)
    {
        var ids = new List<Guid> { categoryId };
        var children = await context.Categories
            .Where(category => category.ParentId == categoryId)
            .Select(category => category.Id)
            .ToListAsync();

        ids.AddRange(children);

        if (children.Count > 0)
        {
            var grandChildren = await context.Categories
                .Where(category => category.ParentId != null && children.Contains(category.ParentId.Value))
                .Select(category => category.Id)
                .ToListAsync();

            ids.AddRange(grandChildren);
        }

        return ids;
    }

    private async Task<Dictionary<Guid, (double Average, int Count)>> GetRatingsAsync(List<Guid> productIds)
    {
        if (productIds.Count == 0)
            return [];

        var ratings = await context.Comments
            .Where(comment => comment.ProductId != null && productIds.Contains(comment.ProductId.Value) && comment.Rating != null)
            .GroupBy(comment => comment.ProductId!.Value)
            .Select(group => new
            {
                ProductId = group.Key,
                Average = group.Average(comment => (double)comment.Rating!.Value),
                Count = group.Count(),
            })
            .ToListAsync();

        return ratings.ToDictionary(
            item => item.ProductId,
            item => (Math.Round(item.Average, 1), item.Count));
    }

    private async Task<Dictionary<Guid, int>> GetSoldCountsAsync(List<Guid> productIds)
    {
        if (productIds.Count == 0)
            return [];

        var sold = await context.OrderItems
            .Where(item => item.ProductId != null &&
                           productIds.Contains(item.ProductId.Value) &&
                           item.Order.Status != OrderStatus.Cancelled &&
                           item.Order.Status != OrderStatus.Returned)
            .GroupBy(item => item.ProductId!.Value)
            .Select(group => new { ProductId = group.Key, Quantity = group.Sum(item => item.Quantity) })
            .ToListAsync();

        return sold.ToDictionary(item => item.ProductId, item => item.Quantity);
    }

    private async Task<OperationResult> ValidateAsync(CreateProductCommand command, Guid? productId)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            return OperationResult.Error("نام محصول را وارد کنید.");

        if (string.IsNullOrWhiteSpace(command.SKU))
            return OperationResult.Error("کد SKU را وارد کنید.");

        if (command.Price < 0)
            return OperationResult.Error("قیمت محصول نمی‌تواند منفی باشد.");

        if (command.DiscountPrice.HasValue && command.DiscountPrice.Value >= command.Price)
            return OperationResult.Error("قیمت با تخفیف باید کمتر از قیمت اصلی باشد.");

        if (command.StockQuantity < 0)
            return OperationResult.Error("موجودی نمی‌تواند منفی باشد.");

        var categoryExists = await context.Categories.AnyAsync(category => category.Id == command.CategoryId);
        if (!categoryExists)
            return OperationResult.Error("دسته‌بندی انتخاب‌شده معتبر نیست.");

        var skuExists = await context.Products
            .AnyAsync(product => product.SKU == command.SKU.Trim() && (productId == null || product.Id != productId));

        if (skuExists)
            return OperationResult.Error("این کد SKU قبلاً برای محصول دیگری ثبت شده است.");

        return OperationResult.Success();
    }

    private async Task<string> MakeSlugUniqueAsync(string slug, Guid? productId)
    {
        if (string.IsNullOrWhiteSpace(slug))
            slug = "product";

        var candidate = slug;
        var counter = 1;

        while (await context.Products.AnyAsync(product =>
                   product.Slug == candidate && (productId == null || product.Id != productId)))
        {
            candidate = $"{slug}-{counter++}";
        }

        return candidate;
    }

    private void ApplyImages(Product product, List<ProductImageCommand> images)
    {
        foreach (var image in images.OrderBy(item => item.DisplayOrder))
        {
            var isPrimary = image.IsPrimary;
            if (isPrimary)
            {
                foreach (var existing in product.Images)
                    existing.IsPrimary = false;
            }

            product.Images.Add(new ProductImage
            {
                ImageName = image.ImageName,
                AltText = image.AltText,
                DisplayOrder = image.DisplayOrder,
                IsPrimary = isPrimary,
            });
        }

        if (product.Images.Count > 0 && product.Images.All(image => !image.IsPrimary))
            product.Images[0].IsPrimary = true;
    }

    private void ApplyFeatures(Product product, List<ProductFeatureCommand> features)
    {
        foreach (var feature in features.Where(item => !string.IsNullOrWhiteSpace(item.Key)))
        {
            product.Features.Add(new ProductFeature
            {
                Key = feature.Key.Trim(),
                Value = feature.Value,
                ValueType = feature.ValueType,
                DisplayOrder = feature.DisplayOrder,
            });
        }
    }

    private static ProductFilterResult EmptyResult(ProductFilterParams filterParams)
    {
        var empty = new ProductFilterResult
        {
            Data = [],
            FilterParams = filterParams,
        };
        empty.GeneratePaging(0, filterParams.Take, filterParams.PageId);
        return empty;
    }
}
