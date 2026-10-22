using Fratak_Shop.Core.DTOs.Favorites.Command;
using Fratak_Shop.Core.DTOs.Favorites.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Favorites;

public interface IFavoriteService
{
    Task<OperationResult> AddFavoriteAsync(CreateFavoriteCommand command);
    Task<OperationResult> RemoveFavoriteAsync(Guid id);
    Task<OperationResult> RemoveFavoriteAsync(Guid userId, Guid productId);
    Task<List<FavoriteDto>> GetFavoritesByUserIdAsync(Guid userId);
    Task<bool> IsFavoriteAsync(Guid userId, Guid productId);
    Task<FavoriteDto?> GetFavoriteByIdAsync(Guid id);
}
