using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Shopping;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Shopping;

/// <summary>
/// «خرید بعدی»: items moved out of the cart but kept next to it. They don't reserve stock and
/// don't count in the cart total; moving back re-checks stock like a normal add to cart.
/// </summary>
public interface ISaveForLaterService
{
    Task<OperationResult> SaveForLaterAsync(Guid cartId, Guid productId);

    Task<OperationResult> MoveToCartAsync(Guid cartId, Guid productId);

    Task<OperationResult> RemoveAsync(Guid cartId, Guid productId);

    Task<List<SavedItemDto>> GetAsync(Guid cartId);
}

public sealed class SavedItemDto
{
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }
    public string ProductName { get; init; } = null!;
    public string ProductSlug { get; init; } = null!;
    public string? ImageName { get; init; }
    public decimal Price { get; init; }
    public decimal FinalPrice { get; init; }
    public bool IsAvailable { get; init; }
    public int StockQuantity { get; init; }
    public DateTimeOffset SavedAt { get; init; }
}

public class SaveForLaterService(FratakShopDbContext context, IStoreSettingService settingService) : ISaveForLaterService
{
    private const int MaxSavedItems = 50;

    public async Task<OperationResult> SaveForLaterAsync(Guid cartId, Guid productId)
    {
        if (!(await settingService.GetAsync()).IsSaveForLaterEnabled)
            return OperationResult.Error("امکان «خرید بعدی» در حال حاضر فعال نیست.");

        await using var transaction = await context.Database.BeginTransactionAsync();

        var item = await context.CartItems.AsTracking().FirstOrDefaultAsync(i => i.CartId == cartId && i.ProductId == productId);
        if (item is null)
            return OperationResult.NotFound("این کالا در سبد خرید شما نیست.");

        var saved = await context.SavedCartItems.AsTracking().FirstOrDefaultAsync(i => i.CartId == cartId && i.ProductId == productId);
        if (saved is null)
        {
            if (await context.SavedCartItems.CountAsync(i => i.CartId == cartId) >= MaxSavedItems)
                return OperationResult.Error($"حداکثر {MaxSavedItems} کالا در «خرید بعدی» نگه داشته می‌شود.");

            context.SavedCartItems.Add(new SavedCartItem { CartId = cartId, ProductId = productId, Quantity = item.Quantity });
        }
        else
        {
            saved.Quantity = item.Quantity;
        }

        context.CartItems.Remove(item);
        await TouchAsync(cartId);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        return OperationResult.Success("کالا به «خرید بعدی» منتقل شد.");
    }

    public async Task<OperationResult> MoveToCartAsync(Guid cartId, Guid productId)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();

        var saved = await context.SavedCartItems.AsTracking().FirstOrDefaultAsync(i => i.CartId == cartId && i.ProductId == productId);
        if (saved is null)
            return OperationResult.NotFound("این کالا در «خرید بعدی» شما نیست.");

        var product = await context.Products
            .Where(p => p.Id == productId && p.IsActive)
            .Select(p => new { p.StockQuantity })
            .FirstOrDefaultAsync();
        if (product is null || product.StockQuantity <= 0)
            return OperationResult.Error("این کالا در حال حاضر موجود نیست؛ در «خرید بعدی» باقی ماند.");

        var item = await context.CartItems.AsTracking().FirstOrDefaultAsync(i => i.CartId == cartId && i.ProductId == productId);
        var quantity = Math.Min((item?.Quantity ?? 0) + saved.Quantity, product.StockQuantity);

        if (item is null)
            context.CartItems.Add(new CartItem { CartId = cartId, ProductId = productId, Quantity = quantity });
        else
            item.Quantity = quantity;

        context.SavedCartItems.Remove(saved);
        await TouchAsync(cartId);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        return OperationResult.Success(quantity < saved.Quantity
            ? $"کالا به سبد منتقل شد (به دلیل موجودی، {quantity} عدد)."
            : "کالا به سبد خرید منتقل شد.");
    }

    public async Task<OperationResult> RemoveAsync(Guid cartId, Guid productId)
    {
        var saved = await context.SavedCartItems.AsTracking().FirstOrDefaultAsync(i => i.CartId == cartId && i.ProductId == productId);
        if (saved is null)
            return OperationResult.NotFound("کالای مورد نظر یافت نشد.");

        context.SavedCartItems.Remove(saved);
        await context.SaveChangesAsync();
        return OperationResult.Success("کالا از «خرید بعدی» حذف شد.");
    }

    public async Task<List<SavedItemDto>> GetAsync(Guid cartId)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = await context.SavedCartItems
            .Where(i => i.CartId == cartId && i.Product.IsActive)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new
            {
                i.ProductId, i.Quantity, i.CreatedAt, i.Product.Name, i.Product.Slug,
                i.Product.Price, i.Product.DiscountPrice, i.Product.DiscountStartAt, i.Product.DiscountEndAt, i.Product.StockQuantity,
                Image = i.Product.Images.OrderByDescending(img => img.IsPrimary).ThenBy(img => img.DisplayOrder).Select(img => img.ImageName).FirstOrDefault(),
            })
            .ToListAsync();

        return rows.Select(r => new SavedItemDto
        {
            ProductId = r.ProductId,
            Quantity = r.Quantity,
            ProductName = r.Name,
            ProductSlug = r.Slug,
            ImageName = r.Image,
            Price = r.Price,
            FinalPrice = FratakShopDbContext.FinalPrice(r.Price, r.DiscountPrice, r.DiscountStartAt, r.DiscountEndAt, now),
            IsAvailable = r.StockQuantity > 0,
            StockQuantity = r.StockQuantity,
            SavedAt = r.CreatedAt,
        }).ToList();
    }

    private async Task TouchAsync(Guid cartId)
    {
        var cart = await context.Carts.AsTracking().FirstOrDefaultAsync(c => c.Id == cartId);
        if (cart is not null)
            cart.LastActivityAt = DateTimeOffset.UtcNow;
    }
}
