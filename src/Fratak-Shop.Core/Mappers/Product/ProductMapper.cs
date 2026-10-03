using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Products;

namespace Fratak_Shop.Core.Mappers.Product;

/// <summary>
/// Maps product entities to the read models of the storefront. The price fields are
/// always computed here (not in the views) so the discount window is respected in
/// every list, slider and cart line.
/// </summary>
public static class ProductMapper
{
    public static ProductCardDto MapCard(this Data.Entities.Products.Product product,
        double ratingValue = 0, int ratingCount = 0, int soldCount = 0)
    {
        var finalPrice = PriceHelper.FinalPrice(product.Price, product.DiscountPrice, product.DiscountStartAt, product.DiscountEndAt);
        var primaryImage = product.Images
            .OrderByDescending(image => image.IsPrimary)
            .ThenBy(image => image.DisplayOrder)
            .FirstOrDefault();

        return new ProductCardDto
        {
            Id = product.Id,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt,
            IsDeleted = product.IsDeleted,
            DeletedAt = product.DeletedAt,
            Name = product.Name,
            Slug = product.Slug,
            SKU = product.SKU,
            ShortDescription = product.ShortDescription,
            ImageName = primaryImage?.ImageName,
            ImageAlt = primaryImage?.AltText,
            Price = product.Price,
            DiscountPrice = product.DiscountPrice,
            FinalPrice = finalPrice,
            DiscountPercent = PriceHelper.DiscountPercent(product.Price, finalPrice),
            HasDiscount = finalPrice < product.Price,
            StockQuantity = product.StockQuantity,
            IsInStock = product.StockQuantity > 0,
            IsActive = product.IsActive,
            IsFeatured = product.IsFeatured,
            IsNew = product.PublishedAt.HasValue && product.PublishedAt.Value > DateTimeOffset.UtcNow.AddDays(-30),
            ViewCount = product.ViewCount,
            SoldCount = soldCount,
            CategoryId = product.CategoryId,
            CategoryName = product.Category?.Name,
            CategorySlug = product.Category?.Slug,
            WarrantyTitle = product.WarrantyTitle,
            RatingValue = ratingValue,
            RatingCount = ratingCount,
            PublishedAt = product.PublishedAt,
        };
    }

    public static ProductDto MapEditDto(this Data.Entities.Products.Product product) => new()
    {
        Id = product.Id,
        CreatedAt = product.CreatedAt,
        UpdatedAt = product.UpdatedAt,
        IsDeleted = product.IsDeleted,
        DeletedAt = product.DeletedAt,
        CategoryId = product.CategoryId,
        CategoryName = product.Category?.Name,
        Name = product.Name,
        Slug = product.Slug,
        SKU = product.SKU,
        ShortDescription = product.ShortDescription,
        Description = product.Description,
        Price = product.Price,
        DiscountPrice = product.DiscountPrice,
        DiscountStartAt = product.DiscountStartAt,
        DiscountEndAt = product.DiscountEndAt,
        StockQuantity = product.StockQuantity,
        WarrantyTitle = product.WarrantyTitle,
        WarrantyMonths = product.WarrantyMonths,
        IsActive = product.IsActive,
        IsFeatured = product.IsFeatured,
        ViewCount = product.ViewCount,
        PublishedAt = product.PublishedAt,
        Seo = product.Seo,
        Images = product.Images
            .OrderByDescending(image => image.IsPrimary)
            .ThenBy(image => image.DisplayOrder)
            .Select(image => new ProductImageDto
            {
                Id = image.Id,
                ImageName = image.ImageName,
                AltText = image.AltText,
                DisplayOrder = image.DisplayOrder,
                IsPrimary = image.IsPrimary,
                CreatedAt = image.CreatedAt,
                UpdatedAt = image.UpdatedAt,
            })
            .ToList(),
        Features = product.Features
            .OrderBy(feature => feature.DisplayOrder)
            .Select(feature => new ProductFeatureDto
            {
                Id = feature.Id,
                Key = feature.Key,
                Value = feature.Value,
                ValueType = feature.ValueType,
                DisplayOrder = feature.DisplayOrder,
                CreatedAt = feature.CreatedAt,
                UpdatedAt = feature.UpdatedAt,
            })
            .ToList(),
    };

    public static ProductDetailDto MapDetail(this Data.Entities.Products.Product product,
        double ratingValue = 0, int ratingCount = 0, int soldCount = 0)
    {
        var card = product.MapCard(ratingValue, ratingCount, soldCount);

        return new ProductDetailDto
        {
            Id = card.Id,
            CreatedAt = card.CreatedAt,
            UpdatedAt = card.UpdatedAt,
            IsDeleted = card.IsDeleted,
            DeletedAt = card.DeletedAt,
            Name = card.Name,
            Slug = card.Slug,
            SKU = card.SKU,
            ShortDescription = card.ShortDescription,
            ImageName = card.ImageName,
            ImageAlt = card.ImageAlt,
            Price = card.Price,
            DiscountPrice = card.DiscountPrice,
            FinalPrice = card.FinalPrice,
            DiscountPercent = card.DiscountPercent,
            HasDiscount = card.HasDiscount,
            StockQuantity = card.StockQuantity,
            IsInStock = card.IsInStock,
            IsActive = card.IsActive,
            IsFeatured = card.IsFeatured,
            IsNew = card.IsNew,
            ViewCount = card.ViewCount,
            SoldCount = card.SoldCount,
            CategoryId = card.CategoryId,
            CategoryName = card.CategoryName,
            CategorySlug = card.CategorySlug,
            WarrantyTitle = card.WarrantyTitle,
            RatingValue = card.RatingValue,
            RatingCount = card.RatingCount,
            PublishedAt = card.PublishedAt,
            Description = product.Description,
            WarrantyMonths = product.WarrantyMonths,
            DiscountStartAt = product.DiscountStartAt,
            DiscountEndAt = product.DiscountEndAt,
            SeoTitle = product.Seo?.Title,
            Seo = product.Seo,
            Images = product.Images
                .OrderByDescending(image => image.IsPrimary)
                .ThenBy(image => image.DisplayOrder)
                .Select(image => new ProductImageDto
                {
                    Id = image.Id,
                    ImageName = image.ImageName,
                    AltText = image.AltText,
                    DisplayOrder = image.DisplayOrder,
                    IsPrimary = image.IsPrimary,
                    CreatedAt = image.CreatedAt,
                    UpdatedAt = image.UpdatedAt,
                })
                .ToList(),
            Features = product.Features
                .OrderBy(feature => feature.DisplayOrder)
                .ThenBy(feature => feature.Key)
                .Select(feature => new ProductFeatureDto
                {
                    Id = feature.Id,
                    Key = feature.Key,
                    Value = feature.Value,
                    ValueType = feature.ValueType,
                    DisplayOrder = feature.DisplayOrder,
                    CreatedAt = feature.CreatedAt,
                    UpdatedAt = feature.UpdatedAt,
                })
                .ToList(),
        };
    }
}
