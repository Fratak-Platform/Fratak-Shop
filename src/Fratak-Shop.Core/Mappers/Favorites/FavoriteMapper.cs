using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Core.DTOs.Favorites.Query;
using Fratak_Shop.Data.Entities.Favorites;

namespace Fratak_Shop.Core.Mappers.Favorites;

public static class FavoriteMapper
{
    public static FavoriteDto Map(this Favorite favorite)
    {
        return new FavoriteDto
        {
            Id = favorite.Id,
            CreatedAt = favorite.CreatedAt,
            UpdatedAt = favorite.UpdatedAt,
            IsDeleted = favorite.IsDeleted,
            DeletedAt = favorite.DeletedAt,
            UserId = favorite.UserId,
            ProductId = favorite.ProductId,
            ProductName = favorite.Product.Name,
            ProductSlug = favorite.Product.Slug,
            ProductImageName = favorite.Product.Images
                .OrderByDescending(image => image.IsPrimary)
                .ThenBy(image => image.DisplayOrder)
                .Select(image => image.ImageName)
                .FirstOrDefault(),
            Price = favorite.Product.Price,
            FinalPrice = favorite.Product.GetFinalPrice(),
            HasActiveDiscount = favorite.Product.HasActiveDiscount(),
            IsInStock = favorite.Product.StockQuantity > 0,
        };
    }

    public static List<FavoriteDto> Map(this List<Favorite> favorites)
    {
        return favorites.Select(favorite => favorite.Map()).ToList();
    }
}
