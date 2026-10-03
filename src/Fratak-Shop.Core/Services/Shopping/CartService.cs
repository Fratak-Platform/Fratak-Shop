using Fratak_Shop.Core.DTOs.Shopping.Query;
using Fratak_Shop.Core.Mappers.Product;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Shipping;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Shopping;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Shopping;

public class CartService(
    FratakShopDbContext context,
    ICouponService couponService,
    IShippingMethodService shippingMethodService,
    ISiteSettingService settingService) : ICartService
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
    // ======================================================================= shop

    public async Task<Cart> GetOrCreateAsync(Guid? userId, string? sessionKey, CancellationToken cancellationToken = default)
    {
        Cart? cart = null;

        if (userId.HasValue)
        {
            cart = await context.Carts.AsTracking()
                .FirstOrDefaultAsync(item => item.UserId == userId.Value && !item.IsDeleted, cancellationToken);
        }

        if (cart is null && !string.IsNullOrWhiteSpace(sessionKey))
        {
            cart = await context.Carts.AsTracking()
                .FirstOrDefaultAsync(item => item.SessionKey == sessionKey && item.UserId == null && !item.IsDeleted,
                    cancellationToken);
        }

        if (cart is not null)
        {
            if (userId.HasValue && cart.UserId != userId)
            {
                cart.UserId = userId;
                cart.SessionKey = null;
                await context.SaveChangesAsync(cancellationToken);
            }

            return cart;
        }

        cart = new Cart
        {
            UserId = userId,
            SessionKey = userId.HasValue ? null : sessionKey,
        };

        context.Carts.Add(cart);
        await context.SaveChangesAsync(cancellationToken);
        return cart;
    }

    public async Task<CartDto> GetCartDtoAsync(Guid? userId, string? sessionKey, string? couponCode = null,
        Guid? shippingMethodId = null, CancellationToken cancellationToken = default)
    {
        var cart = await GetOrCreateAsync(userId, sessionKey, cancellationToken);
        var settings = await settingService.GetStorefrontSettingsAsync();

        var items = await context.CartItems
            .Include(item => item.Product)
            .ThenInclude(product => product!.Images)
            .Where(item => item.CartId == cart.Id && !item.IsDeleted)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

        var dto = new CartDto
        {
            CartId = cart.Id,
            CouponCode = cart.CouponCode,
        };

        foreach (var item in items)
        {
            if (item.Product is null || item.Product.IsDeleted)
                continue;

            dto.Items.Add(new CartItemDto
            {
                Id = item.Id,
                Quantity = item.Quantity,
                Product = item.Product.MapCard(),
            });
        }

        var code = string.IsNullOrWhiteSpace(couponCode) ? cart.CouponCode : couponCode;
        if (!string.IsNullOrWhiteSpace(code) && dto.Items.Count > 0)
        {
            var validation = await couponService.ValidateAsync(code, dto.Subtotal, userId, cancellationToken);
            dto.CouponCode = validation.Code ?? code;
            dto.CouponDiscount = validation.DiscountAmount;
            dto.CouponMessage = validation.Message;
            dto.IsCouponApplied = validation.IsValid;
        }

        if (shippingMethodId.HasValue)
        {
            var method = await shippingMethodService.GetAsync(shippingMethodId.Value);
            if (method is not null)
            {
                dto.ShippingMethodId = method.Id;
                dto.ShippingAmount = method.CalculateCost(Math.Max(0, dto.Subtotal - dto.CouponDiscount));
            }
        }

        if (settings.FreeShippingThreshold is > 0)
        {
            var remaining = settings.FreeShippingThreshold.Value - dto.Subtotal;
            dto.FreeShippingHint = remaining <= 0
                ? "ارسال این سفارش رایگان است."
                : $"با {remaining.ToToman()} خرید بیشتر، ارسال رایگان می‌شود.";
        }

        return dto;
    }

    public async Task<OperationResult> AddToCartAsync(Guid productId, int quantity, Guid? userId, string? sessionKey,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
            return OperationResult.Error("تعداد مورد نظر باید بزرگ‌تر از صفر باشد.");

        var product = await context.Products
            .FirstOrDefaultAsync(item => item.Id == productId && !item.IsDeleted, cancellationToken);

        if (product is null)
            return OperationResult.NotFound("محصول مورد نظر یافت نشد.");

        if (!product.IsActive)
            return OperationResult.Error("این محصول در حال حاضر قابل خریداری نیست.");

        if (product.StockQuantity <= 0)
            return OperationResult.Error("موجودی این محصول به پایان رسیده است.");

        var cart = await GetOrCreateAsync(userId, sessionKey, cancellationToken);

        var item = await context.CartItems.AsTracking()
            .FirstOrDefaultAsync(cartItem => cartItem.CartId == cart.Id && cartItem.ProductId == productId, cancellationToken);

        var requested = (item?.Quantity ?? 0) + quantity;
        if (requested > product.StockQuantity)
            return OperationResult.Error($"حداکثر موجودی قابل خرید این محصول {PriceHelper.ToPersianDigits(product.StockQuantity.ToString())} عدد است.");

        if (item is null)
        {
            context.CartItems.Add(new CartItem
            {
                CartId = cart.Id,
                ProductId = productId,
                Quantity = requested,
            });
        }
        else
        {
            item.Quantity = requested;
        }

        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success("محصول به سبد خرید اضافه شد.");
    }

    public async Task<OperationResult> SetQuantityAsync(Guid productId, int quantity, Guid? userId, string? sessionKey,
        CancellationToken cancellationToken = default)
    {
        var cart = await GetOrCreateAsync(userId, sessionKey, cancellationToken);
        var item = await context.CartItems.AsTracking()
            .FirstOrDefaultAsync(cartItem => cartItem.CartId == cart.Id && cartItem.ProductId == productId, cancellationToken);

        if (item is null)
            return OperationResult.NotFound("محصولی در سبد خرید یافت نشد.");

        if (quantity <= 0)
            return await RemoveAsync(productId, userId, sessionKey, cancellationToken);

        var stock = await context.Products
            .Where(product => product.Id == productId)
            .Select(product => product.StockQuantity)
            .FirstOrDefaultAsync(cancellationToken);

        if (stock > 0 && quantity > stock)
            return OperationResult.Error($"حداکثر موجودی این محصول {PriceHelper.ToPersianDigits(stock.ToString())} عدد است.");

        item.Quantity = quantity;
        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success("تعداد محصول به‌روزرسانی شد.");
    }

    public async Task<OperationResult> RemoveAsync(Guid productId, Guid? userId, string? sessionKey,
        CancellationToken cancellationToken = default)
    {
        var cart = await GetOrCreateAsync(userId, sessionKey, cancellationToken);
        var item = await context.CartItems.AsTracking()
            .FirstOrDefaultAsync(cartItem => cartItem.CartId == cart.Id && cartItem.ProductId == productId, cancellationToken);

        if (item is null)
            return OperationResult.NotFound("محصولی در سبد خرید یافت نشد.");

        context.CartItems.Remove(item);
        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success("محصول از سبد خرید حذف شد.");
    }

    public async Task<OperationResult> ClearAsync(Guid? userId, string? sessionKey, CancellationToken cancellationToken = default)
    {
        var cart = await GetOrCreateAsync(userId, sessionKey, cancellationToken);

        var items = await context.CartItems.AsTracking()
            .Where(item => item.CartId == cart.Id)
            .ToListAsync(cancellationToken);

        if (items.Count > 0)
        {
            context.CartItems.RemoveRange(items);
            cart.CouponCode = null;
            await context.SaveChangesAsync(cancellationToken);
        }

        return OperationResult.Success("سبد خرید خالی شد.");
    }

    public async Task<OperationResult> ApplyCouponAsync(string code, Guid? userId, string? sessionKey,
        CancellationToken cancellationToken = default)
    {
        var cart = await GetOrCreateAsync(userId, sessionKey, cancellationToken);
        var subtotal = await GetSubtotalAsync(cart.Id, cancellationToken);

        if (subtotal <= 0)
            return OperationResult.Error("سبد خرید شما خالی است.");

        var validation = await couponService.ValidateAsync(code, subtotal, userId, cancellationToken);
        if (!validation.IsValid)
            return OperationResult.Error(validation.Message);

        cart.CouponCode = validation.Code;
        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success(validation.Message);
    }

    public async Task<OperationResult> RemoveCouponAsync(Guid? userId, string? sessionKey,
        CancellationToken cancellationToken = default)
    {
        var cart = await GetOrCreateAsync(userId, sessionKey, cancellationToken);
        cart.CouponCode = null;
        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success("کد تخفیف حذف شد.");
    }

    public async Task<int> GetItemCountAsync(Guid? userId, string? sessionKey, CancellationToken cancellationToken = default)
    {
        Cart? cart = null;

        if (userId.HasValue)
        {
            cart = await context.Carts.FirstOrDefaultAsync(item => item.UserId == userId.Value && !item.IsDeleted, cancellationToken);
        }

        if (cart is null && !string.IsNullOrWhiteSpace(sessionKey))
        {
            cart = await context.Carts.FirstOrDefaultAsync(item => item.SessionKey == sessionKey && !item.IsDeleted, cancellationToken);
        }

        if (cart is null)
            return 0;

        return await context.CartItems
            .Where(item => item.CartId == cart.Id && !item.IsDeleted)
            .SumAsync(item => (int?)item.Quantity, cancellationToken) ?? 0;
    }

    public async Task<OperationResult> MergeGuestCartAsync(string sessionKey, Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionKey))
            return OperationResult.Success();

        var guestCart = await context.Carts.AsTracking()
            .Include(item => item.Items)
            .FirstOrDefaultAsync(item => item.SessionKey == sessionKey && item.UserId == null, cancellationToken);

        if (guestCart is null || guestCart.Items.Count == 0)
            return OperationResult.Success();

        var userCart = await GetOrCreateAsync(userId, null, cancellationToken);

        var userItems = await context.CartItems.AsTracking()
            .Where(item => item.CartId == userCart.Id)
            .ToListAsync(cancellationToken);

        foreach (var item in guestCart.Items)
        {
            var existing = userItems.FirstOrDefault(userItem => userItem.ProductId == item.ProductId);
            if (existing is null)
            {
                context.CartItems.Add(new CartItem
                {
                    CartId = userCart.Id,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                });
            }
            else
            {
                existing.Quantity += item.Quantity;
            }
        }

        if (string.IsNullOrWhiteSpace(userCart.CouponCode))
            userCart.CouponCode = guestCart.CouponCode;

        context.CartItems.RemoveRange(guestCart.Items);
        context.Carts.Remove(guestCart);

        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success("سبد خرید مهمان با حساب شما ادغام شد.");
    }

    /// <summary>جمع مبلغ اقلام سبد خرید با در نظر گرفتن تخفیف فعال محصولات.</summary>
    private async Task<decimal> GetSubtotalAsync(Guid cartId, CancellationToken cancellationToken)
    {
        var items = await context.CartItems
            .Include(item => item.Product)
            .Where(item => item.CartId == cartId && !item.IsDeleted)
            .ToListAsync(cancellationToken);

        return items
            .Where(item => item.Product is not null)
            .Sum(item => PriceHelper.FinalPrice(item.Product!.Price, item.Product.DiscountPrice,
                item.Product.DiscountStartAt, item.Product.DiscountEndAt) * item.Quantity);
    }
}