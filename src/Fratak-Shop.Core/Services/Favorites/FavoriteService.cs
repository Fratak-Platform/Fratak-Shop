using Fratak_Shop.Core.DTOs.Favorites.Command;
using Fratak_Shop.Core.DTOs.Favorites.Query;
using Fratak_Shop.Core.Mappers.Favorites;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Favorites;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Favorites;

public class FavoriteService(FratakShopDbContext context) : IFavoriteService
{
    public async Task<OperationResult> AddFavoriteAsync(CreateFavoriteCommand command)
    {
        if (command.UserId == Guid.Empty)
            return OperationResult.Error("شناسه کاربر نامعتبر است.");

        if (command.ProductId == Guid.Empty)
            return OperationResult.Error("شناسه محصول نامعتبر است.");

        var userExists = await context.Users.AnyAsync(x => x.Id == command.UserId);
        if (!userExists)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        var productExists = await context.Products.AnyAsync(x => x.Id == command.ProductId);
        if (!productExists)
            return OperationResult.NotFound("محصول مورد نظر یافت نشد.");

        var exists = await context.Favorites.AnyAsync(x => x.UserId == command.UserId && x.ProductId == command.ProductId);
        if (exists)
            return OperationResult.Success("این محصول از قبل به علاقه‌مندی‌های شما اضافه شده است.");

        var model = new Favorite
        {
            UserId = command.UserId,
            ProductId = command.ProductId,
        };

        context.Favorites.Add(model);
        await context.SaveChangesAsync();

        return OperationResult.Success("محصول به علاقه‌مندی‌ها اضافه شد.");
    }

    public async Task<OperationResult> RemoveFavoriteAsync(Guid id)
    {
        var favorite = await context.Favorites.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (favorite is null)
            return OperationResult.NotFound("محصول مورد نظر در علاقه‌مندی‌ها یافت نشد.");

        context.Favorites.Remove(favorite);
        await context.SaveChangesAsync();

        return OperationResult.Success("محصول از علاقه‌مندی‌ها حذف شد.");
    }

    public async Task<OperationResult> RemoveFavoriteAsync(Guid userId, Guid productId)
    {
        if (userId == Guid.Empty)
            return OperationResult.Error("شناسه کاربر نامعتبر است.");

        if (productId == Guid.Empty)
            return OperationResult.Error("شناسه محصول نامعتبر است.");

        var favorite = await context.Favorites
            .AsTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.ProductId == productId);

        if (favorite is null)
            return OperationResult.NotFound("محصول مورد نظر در علاقه‌مندی‌ها یافت نشد.");

        context.Favorites.Remove(favorite);
        await context.SaveChangesAsync();

        return OperationResult.Success("محصول از علاقه‌مندی‌ها حذف شد.");
    }

    public async Task<List<FavoriteDto>> GetFavoritesByUserIdAsync(Guid userId)
    {
        var favorites = await context.Favorites
            .Where(x => x.UserId == userId)
            .Include(x => x.Product)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        return favorites.Map();
    }

    public async Task<FavoriteDto?> GetFavoriteByIdAsync(Guid id)
    {
        var favorite = await context.Favorites
            .Include(x => x.Product)
            .FirstOrDefaultAsync(x => x.Id == id);

        return favorite?.Map();
    }
}
