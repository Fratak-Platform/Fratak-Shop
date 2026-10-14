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
            ProductName = favorite.Product?.Name,
            ProductSlug = favorite.Product?.Slug,
        };
    }

    public static List<FavoriteDto> Map(this List<Favorite> favorites)
    {
        return favorites.Select(favorite => favorite.Map()).ToList();
    }
}
