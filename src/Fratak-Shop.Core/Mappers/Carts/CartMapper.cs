using Fratak_Shop.Core.DTOs.Shopping.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Shopping;

namespace Fratak_Shop.Core.Mappers.Carts;

public static class CartMapper
{
    public static CartDto Map(this Cart cart)
    {
        return new CartDto
        {
            Id = cart.Id,
            CreatedAt = cart.CreatedAt,
            UpdatedAt = cart.UpdatedAt,
            IsDeleted = cart.IsDeleted,
            DeletedAt = cart.DeletedAt,
            UserId = cart.UserId,
            SessionKey = cart.SessionKey,
            CouponCode = cart.CouponCode,
            Items = cart.Items
                .OrderBy(item => item.CreatedAt)
                .Select(item => item.Map())
                .ToList(),
        };
    }

    public static CartItemDto Map(this CartItem item)
    {
        var product = item.Product;

        var primaryImage = product?.Images
            .OrderByDescending(image => image.IsPrimary)
            .ThenBy(image => image.DisplayOrder)
            .FirstOrDefault();

        var unitPrice = product?.Price ?? 0m;
        var finalPrice = product?.GetFinalPrice() ?? 0m;
        var discountAmount = (unitPrice - finalPrice) * item.Quantity;

        return new CartItemDto
        {
            Id = item.Id,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            IsDeleted = item.IsDeleted,
            DeletedAt = item.DeletedAt,
            CartId = item.CartId,
            ProductId = item.ProductId,
            ProductName = product?.Name,
            ProductSlug = product?.Slug,
            ImageName = primaryImage?.ImageName,
            ImageAlt = primaryImage?.AltText,
            UnitPrice = unitPrice,
            FinalPrice = finalPrice,
            DiscountAmount = discountAmount,
            TotalAmount = finalPrice * item.Quantity,
            Quantity = item.Quantity,
            StockQuantity = product?.StockQuantity ?? 0,
        };
    }

    public static List<CartItemDto> Map(this List<CartItem> items)
    {
        return items.Select(item => item.Map()).ToList();
    }
}
