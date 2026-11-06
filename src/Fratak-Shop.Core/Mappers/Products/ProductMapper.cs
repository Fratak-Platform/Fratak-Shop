using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Mappers.Blog;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Products;

namespace Fratak_Shop.Core.Mappers.Products;

public static class ProductMapper
{
    public static ProductDto Map(this Product product)
    {
        return new ProductDto
        {
            Id = product.Id,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt,
            IsDeleted = product.IsDeleted,
            DeletedAt = product.DeletedAt,
            CategoryId = product.CategoryId,
            CategoryName = product.Category?.Name,
            CategorySlug = product.Category?.Slug,
            BrandId = product.BrandId,
            BrandName = product.Brand?.Name,
            BrandEnglishName = product.Brand?.EnglishName,
            BrandSlug = product.Brand?.Slug,
            Name = product.Name,
            EnglishName = product.EnglishName,
            Slug = product.Slug,
            SKU = product.SKU,
            Gtin = product.Gtin,
            Mpn = product.Mpn,
            ShortDescription = product.ShortDescription,
            Description = product.Description,
            Price = product.Price,
            DiscountPrice = product.DiscountPrice,
            DiscountStartAt = product.DiscountStartAt,
            DiscountEndAt = product.DiscountEndAt,
            FinalPrice = product.GetFinalPrice(),
            DiscountAmount = product.GetDiscountAmount(),
            HasActiveDiscount = product.HasActiveDiscount(),
            StockQuantity = product.StockQuantity,
            WarrantyTitle = product.WarrantyTitle,
            WarrantyMonths = product.WarrantyMonths,
            IsActive = product.IsActive,
            PublishedAt = product.PublishedAt,
            ViewCount = product.ViewCount,
            SalesCount = product.SalesCount,
            Seo = product.Seo.MapSeoMetaData(),
            Images = product.Images
                .OrderByDescending(image => image.IsPrimary)
                .ThenBy(image => image.DisplayOrder)
                .Select(image => image.Map())
                .ToList(),
            Features = product.Features
                .OrderBy(feature => feature.DisplayOrder)
                .Select(feature => feature.Map())
                .ToList(),
        };
    }

    public static List<ProductDto> Map(this List<Product> products)
    {
        return products.Select(product => product.Map()).ToList();
    }

    public static ProductFilterData MapFilterData(this Product product)
    {
        var primaryImage = product.Images
            .OrderByDescending(image => image.IsPrimary)
            .ThenBy(image => image.DisplayOrder)
            .FirstOrDefault();

        return new ProductFilterData
        {
            Id = product.Id,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt,
            IsDeleted = product.IsDeleted,
            DeletedAt = product.DeletedAt,
            CategoryId = product.CategoryId,
            CategoryName = product.Category?.Name,
            CategorySlug = product.Category?.Slug,
            BrandId = product.BrandId,
            BrandName = product.Brand?.Name,
            BrandSlug = product.Brand?.Slug,
            Name = product.Name,
            EnglishName = product.EnglishName,
            Slug = product.Slug,
            SKU = product.SKU,
            Price = product.Price,
            DiscountPrice = product.DiscountPrice,
            FinalPrice = product.GetFinalPrice(),
            HasActiveDiscount = product.HasActiveDiscount(),
            DiscountEndAt = product.DiscountEndAt,
            PrimaryImageName = primaryImage?.ImageName,
            PrimaryImageAlt = primaryImage?.AltText,
            StockQuantity = product.StockQuantity,
            IsActive = product.IsActive,
            ViewCount = product.ViewCount,
            SalesCount = product.SalesCount,
        };
    }

    public static List<ProductFilterData> MapFilterData(this List<Product> products)
    {
        return products.Select(product => product.MapFilterData()).ToList();
    }

    public static ProductImageDto Map(this ProductImage image)
    {
        return new ProductImageDto
        {
            Id = image.Id,
            CreatedAt = image.CreatedAt,
            UpdatedAt = image.UpdatedAt,
            IsDeleted = image.IsDeleted,
            DeletedAt = image.DeletedAt,
            ProductId = image.ProductId,
            ImageName = image.ImageName,
            AltText = image.AltText,
            DisplayOrder = image.DisplayOrder,
            IsPrimary = image.IsPrimary,
        };
    }

    public static ProductFeatureDto Map(this ProductFeature feature)
    {
        return new ProductFeatureDto
        {
            Id = feature.Id,
            CreatedAt = feature.CreatedAt,
            UpdatedAt = feature.UpdatedAt,
            IsDeleted = feature.IsDeleted,
            DeletedAt = feature.DeletedAt,
            ProductId = feature.ProductId,
            Key = feature.Key,
            Value = feature.Value,
            ValueType = feature.ValueType,
            DisplayOrder = feature.DisplayOrder,
        };
    }
}
