using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Shopping;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Shopping;

public class CartService(FratakShopDbContext context) : ICartService
{
    public async Task<OperationResult> AddItemAsync(Guid cartId, Guid productId, int quantity = 1)
    {
        if (cartId == Guid.Empty)
            return OperationResult.Error("شناسه سبد خرید معتبر نیست.");

        if (productId == Guid.Empty)
            return OperationResult.Error("شناسه محصول معتبر نیست.");

        if (quantity <= 0)
            return OperationResult.Error("تعداد مورد نظر باید بزرگ‌تر از صفر باشد.");

        var cart = await context.Carts.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == cartId && !x.IsDeleted);
        if (cart is null)
            return OperationResult.NotFound("سبد خرید مورد نظر یافت نشد.");

        var product = await context.Products.FirstOrDefaultAsync(x => x.Id == productId && !x.IsDeleted);
        if (product is null)
            return OperationResult.NotFound("محصول مورد نظر یافت نشد.");

        var item = cart.Items.FirstOrDefault(x => x.ProductId == productId);
        if (item is null)
        {
            cart.Items.Add(new CartItem
            {
                CartId = cart.Id,
                ProductId = productId,
                Quantity = quantity,
            });
        }
        else
        {
            item.Quantity += quantity;
        }

        await context.SaveChangesAsync();
        return OperationResult.Success("محصول به سبد خرید اضافه شد.");
    }

    public async Task<OperationResult> UpdateItemAsync(Guid cartId, Guid productId, int quantity)
    {
        if (cartId == Guid.Empty)
            return OperationResult.Error("شناسه سبد خرید معتبر نیست.");

        if (productId == Guid.Empty)
            return OperationResult.Error("شناسه محصول معتبر نیست.");

        if (quantity <= 0)
            return OperationResult.Error("تعداد مورد نظر باید بزرگ‌تر از صفر باشد.");

        var cart = await context.Carts.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == cartId && !x.IsDeleted);
        if (cart is null)
            return OperationResult.NotFound("سبد خرید مورد نظر یافت نشد.");

        var item = cart.Items.FirstOrDefault(x => x.ProductId == productId);
        if (item is null)
            return OperationResult.NotFound("محصولی در سبد خرید یافت نشد.");

        item.Quantity = quantity;
        await context.SaveChangesAsync();

        return OperationResult.Success("تعداد محصول با موفقیت به‌روزرسانی شد.");
    }

    public async Task<OperationResult> RemoveItemAsync(Guid cartId, Guid productId)
    {
        if (cartId == Guid.Empty)
            return OperationResult.Error("شناسه سبد خرید معتبر نیست.");

        if (productId == Guid.Empty)
            return OperationResult.Error("شناسه محصول معتبر نیست.");

        var cart = await context.Carts.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == cartId && !x.IsDeleted);
        if (cart is null)
            return OperationResult.NotFound("سبد خرید مورد نظر یافت نشد.");

        var item = cart.Items.FirstOrDefault(x => x.ProductId == productId);
        if (item is null)
            return OperationResult.NotFound("محصولی در سبد خرید یافت نشد.");

        cart.Items.Remove(item);
        await context.SaveChangesAsync();

        return OperationResult.Success("محصول از سبد خرید حذف شد.");
    }

    public async Task<Cart?> GetCartByIdAsync(Guid cartId)
    {
        return await context.Carts
            .Include(x => x.Items)
            .ThenInclude(x => x.Product)
            .FirstOrDefaultAsync(x => x.Id == cartId && !x.IsDeleted);
    }

    public async Task<Cart?> GetCartByUserAsync(Guid userId)
    {
        return await context.Carts
            .Include(x => x.Items)
            .ThenInclude(x => x.Product)
            .FirstOrDefaultAsync(x => x.UserId == userId && !x.IsDeleted);
    }

    public async Task<List<CartItem>> GetItemsAsync(Guid cartId)
    {
        return await context.CartItems
            .Where(x => x.CartId == cartId && !x.IsDeleted)
            .Include(x => x.Product)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
    }
}
