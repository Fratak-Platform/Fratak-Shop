using Fratak_Shop.Core.DTOs.Shopping.Command;
using Fratak_Shop.Core.DTOs.Shopping.Query;
using Fratak_Shop.Core.Mappers.Carts;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Shopping;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Shopping;

/// <summary>
/// مدیریت سبد خرید کاربران و مهمان‌ها: افزودن/ویرایش/حذف اقلام با کنترل موجودی
/// انبار، جابه‌جایی سبد مهمان پس از ورود و جمع‌های سبد خرید.
/// </summary>
public class CartService(FratakShopDbContext context) : ICartService
{
    public async Task<OperationResult> CreateCartAsync(CreateCartCommand command)
    {
        var sessionKey = command.SessionKey?.Trim();

        if (command.UserId is null && string.IsNullOrWhiteSpace(sessionKey))
            return OperationResult.Error("برای ایجاد سبد خرید، کاربر یا کلید سشن لازم است.");

        if (command.UserId.HasValue)
        {
            if (command.UserId.Value == Guid.Empty)
                return OperationResult.Error("شناسه کاربر معتبر نیست.");

            var userExists = await context.Users.AnyAsync(x => x.Id == command.UserId.Value);
            if (!userExists)
                return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

            var userCartExists = await context.Carts.AnyAsync(x => x.UserId == command.UserId.Value);
            if (userCartExists)
                return OperationResult.Error("برای این کاربر از قبل سبد خرید فعال وجود دارد.");
        }

        if (!string.IsNullOrWhiteSpace(sessionKey))
        {
            var sessionCartExists = await context.Carts.AnyAsync(x => x.SessionKey == sessionKey);
            if (sessionCartExists)
                return OperationResult.Error("برای این سشن از قبل سبد خرید فعال وجود دارد.");
        }

        var cart = new Cart
        {
            UserId = command.UserId,
            SessionKey = command.UserId.HasValue ? null : sessionKey,
        };

        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        return OperationResult.Success("سبد خرید با موفقیت ایجاد شد.");
    }

    public async Task<OperationResult> AddItemAsync(AddCartItemCommand command)
    {
        var error = ValidateItem(command.CartId, command.ProductId, command.Quantity);
        if (error is not null)
            return OperationResult.Error(error);

        var cart = await context.Carts
            .AsTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == command.CartId);

        if (cart is null)
            return OperationResult.NotFound("سبد خرید مورد نظر یافت نشد.");

        var product = await context.Products
            .FirstOrDefaultAsync(x => x.Id == command.ProductId && x.IsActive);

        if (product is null)
            return OperationResult.NotFound("محصول مورد نظر یافت نشد یا غیرفعال است.");

        var item = cart.Items.FirstOrDefault(x => x.ProductId == command.ProductId);
        var newQuantity = (item?.Quantity ?? 0) + command.Quantity;

        if (product.StockQuantity < newQuantity)
            return OperationResult.Error($"موجودی محصول «{product.Name}» کافی نیست.");

        if (item is null)
        {
            cart.Items.Add(new CartItem
            {
                CartId = cart.Id,
                ProductId = product.Id,
                Quantity = command.Quantity,
            });
        }
        else
        {
            item.Quantity = newQuantity;
        }

        await context.SaveChangesAsync();

        return OperationResult.Success("محصول به سبد خرید اضافه شد.");
    }

    public async Task<OperationResult> UpdateItemAsync(UpdateCartItemCommand command)
    {
        var error = ValidateItem(command.CartId, command.ProductId, command.Quantity);
        if (error is not null)
            return OperationResult.Error(error);

        var cart = await context.Carts
            .AsTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == command.CartId);

        if (cart is null)
            return OperationResult.NotFound("سبد خرید مورد نظر یافت نشد.");

        var item = cart.Items.FirstOrDefault(x => x.ProductId == command.ProductId);
        if (item is null)
            return OperationResult.NotFound("این محصول در سبد خرید شما وجود ندارد.");

        var product = await context.Products
            .FirstOrDefaultAsync(x => x.Id == command.ProductId && x.IsActive);

        if (product is null)
            return OperationResult.NotFound("محصول مورد نظر یافت نشد یا غیرفعال است.");

        if (product.StockQuantity < command.Quantity)
            return OperationResult.Error($"موجودی محصول «{product.Name}» کافی نیست.");

        item.Quantity = command.Quantity;
        await context.SaveChangesAsync();

        return OperationResult.Success("تعداد محصول در سبد خرید به‌روزرسانی شد.");
    }

    public async Task<OperationResult> RemoveItemAsync(RemoveCartItemCommand command)
    {
        if (command.CartId == Guid.Empty)
            return OperationResult.Error("شناسه سبد خرید معتبر نیست.");

        if (command.ProductId == Guid.Empty)
            return OperationResult.Error("شناسه محصول معتبر نیست.");

        var cart = await context.Carts
            .AsTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == command.CartId);

        if (cart is null)
            return OperationResult.NotFound("سبد خرید مورد نظر یافت نشد.");

        var item = cart.Items.FirstOrDefault(x => x.ProductId == command.ProductId);
        if (item is null)
            return OperationResult.NotFound("این محصول در سبد خرید شما وجود ندارد.");

        cart.Items.Remove(item);
        await context.SaveChangesAsync();

        return OperationResult.Success("محصول از سبد خرید حذف شد.");
    }

    public async Task<OperationResult> ClearCartAsync(Guid cartId)
    {
        if (cartId == Guid.Empty)
            return OperationResult.Error("شناسه سبد خرید معتبر نیست.");

        var cart = await context.Carts
            .AsTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == cartId);

        if (cart is null)
            return OperationResult.NotFound("سبد خرید مورد نظر یافت نشد.");

        cart.CouponCode = null;

        if (cart.Items.Count == 0)
        {
            await context.SaveChangesAsync();
            return OperationResult.Success("سبد خرید شما خالی است.");
        }

        cart.Items.Clear();
        await context.SaveChangesAsync();

        return OperationResult.Success("سبد خرید خالی شد.");
    }

    public async Task<OperationResult> SetCouponAsync(Guid cartId, string? couponCode)
    {
        var code = string.IsNullOrWhiteSpace(couponCode) ? null : couponCode.Trim().ToUpperInvariant();
        if (code is { Length: > 50 })
            return OperationResult.Error("کد تخفیف معتبر نیست.");

        var affected = await context.Carts
            .Where(x => x.Id == cartId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CouponCode, code)
                .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow));

        return affected == 0
            ? OperationResult.NotFound("سبد خرید مورد نظر یافت نشد.")
            : OperationResult.Success(code is null ? "کد تخفیف حذف شد." : "کد تخفیف ثبت شد.");
    }

    public async Task<OperationResult> MergeGuestCartAsync(string sessionKey, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(sessionKey))
            return OperationResult.Error("کلید سشن سبد خرید مهمان معتبر نیست.");

        if (userId == Guid.Empty)
            return OperationResult.Error("شناسه کاربر معتبر نیست.");

        var userExists = await context.Users.AnyAsync(x => x.Id == userId);
        if (!userExists)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        var guestCart = await context.Carts
            .AsTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.SessionKey == sessionKey.Trim() && x.UserId == null);

        if (guestCart is null)
            return OperationResult.NotFound("سبد خرید مهمان مورد نظر یافت نشد.");

        var userCart = await context.Carts
            .AsTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.UserId == userId);

        // کاربر سبدی ندارد؛ همان سبد مهمان به حساب او منتقل می‌شود.
        if (userCart is null)
        {
            guestCart.UserId = userId;
            guestCart.SessionKey = null;
            await context.SaveChangesAsync();

            return OperationResult.Success("سبد خرید مهمان به حساب کاربری شما منتقل شد.");
        }

        // A code entered as guest survives the sign-in.
        userCart.CouponCode ??= guestCart.CouponCode;

        var productIds = guestCart.Items
            .Select(item => item.ProductId)
            .Distinct()
            .ToList();

        var products = await context.Products
            .Where(x => productIds.Contains(x.Id) && x.IsActive)
            .ToListAsync();

        foreach (var item in guestCart.Items)
        {
            var product = products.FirstOrDefault(x => x.Id == item.ProductId);

            // محصولات حذف‌شده یا غیرفعال به سبد کاربر منتقل نمی‌شوند.
            if (product is null)
                continue;

            var existingItem = userCart.Items.FirstOrDefault(x => x.ProductId == item.ProductId);

            var quantity = (existingItem?.Quantity ?? 0) + item.Quantity;

            // بیشتر از موجودی انبار کالا به سبد کاربر اضافه نمی‌شود.
            if (quantity > product.StockQuantity)
                quantity = product.StockQuantity;

            if (quantity <= 0)
                continue;

            if (existingItem is null)
            {
                userCart.Items.Add(new CartItem
                {
                    CartId = userCart.Id,
                    ProductId = item.ProductId,
                    Quantity = quantity,
                });
            }
            else
            {
                existingItem.Quantity = quantity;
            }
        }

        foreach (var item in guestCart.Items.ToList())
            context.CartItems.Remove(item);

        context.Carts.Remove(guestCart);

        await context.SaveChangesAsync();

        return OperationResult.Success("سبد خرید مهمان با سبد خرید شما ادغام شد.");
    }

    public async Task<CartDto?> GetOrCreateCartAsync(Guid? userId, string? sessionKey)
    {
        if (userId.HasValue && userId.Value != Guid.Empty)
        {
            var userExists = await context.Users.AnyAsync(x => x.Id == userId.Value);
            if (!userExists)
                return null;

            var userCart = await FindCartByUserAsync(userId.Value);
            if (userCart is not null)
                return userCart.Map();

            var createdUserCart = new Cart { UserId = userId.Value };

            context.Carts.Add(createdUserCart);
            await context.SaveChangesAsync();

            return createdUserCart.Map();
        }

        if (!string.IsNullOrWhiteSpace(sessionKey))
        {
            var key = sessionKey.Trim();

            var sessionCart = await FindCartBySessionAsync(key);
            if (sessionCart is not null)
                return sessionCart.Map();

            var createdSessionCart = new Cart { SessionKey = key };

            context.Carts.Add(createdSessionCart);
            await context.SaveChangesAsync();

            return createdSessionCart.Map();
        }

        return null;
    }

    public async Task<CartDto?> GetCartByIdAsync(Guid cartId)
    {
        return (await FindCartAsync(cartId))?.Map();
    }

    public async Task<CartDto?> GetCartByUserAsync(Guid userId)
    {
        return (await FindCartByUserAsync(userId))?.Map();
    }

    public async Task<CartDto?> GetCartBySessionAsync(string sessionKey)
    {
        if (string.IsNullOrWhiteSpace(sessionKey))
            return null;

        return (await FindCartBySessionAsync(sessionKey.Trim()))?.Map();
    }

    public async Task<List<CartItemDto>> GetItemsAsync(Guid cartId)
    {
        var items = await context.CartItems
            .Where(x => x.CartId == cartId)
            .Include(x => x.Product)
            .ThenInclude(product => product.Images)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        return items.Map();
    }

    private Task<Cart?> FindCartAsync(Guid cartId) =>
        LoadCartQuery().FirstOrDefaultAsync(x => x.Id == cartId);

    private Task<Cart?> FindCartByUserAsync(Guid userId) =>
        LoadCartQuery().FirstOrDefaultAsync(x => x.UserId == userId);

    private Task<Cart?> FindCartBySessionAsync(string sessionKey) =>
        LoadCartQuery().FirstOrDefaultAsync(x => x.SessionKey == sessionKey);

    private IQueryable<Cart> LoadCartQuery() =>
        context.Carts
            .Include(x => x.Items)
            .ThenInclude(item => item.Product)
            .ThenInclude(product => product.Images);

    private static string? ValidateItem(Guid cartId, Guid productId, int quantity)
    {
        if (cartId == Guid.Empty)
            return "شناسه سبد خرید معتبر نیست.";

        if (productId == Guid.Empty)
            return "شناسه محصول معتبر نیست.";

        if (quantity <= 0)
            return "تعداد مورد نظر باید بزرگ‌تر از صفر باشد.";

        return null;
    }
}
