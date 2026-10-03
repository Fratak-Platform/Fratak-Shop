using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.DTOs.Shopping.Query;

/// <summary>سبد خرید کامل همراه با جمع‌ها و اطلاعات کد تخفیف.</summary>
public class CartDto
{
    public Guid? CartId { get; set; }
    public List<CartItemDto> Items { get; set; } = [];
    public int ItemCount => Items.Sum(item => item.Quantity);
    public decimal Subtotal => Items.Sum(item => item.TotalPrice);
    public decimal ProductDiscount => Items.Sum(item => item.ProductDiscount);
    public decimal CouponDiscount { get; set; }
    public decimal ShippingAmount { get; set; }
    public decimal PayableAmount => Math.Max(0, Subtotal - CouponDiscount) + ShippingAmount;

    public string? CouponCode { get; set; }
    public string? CouponMessage { get; set; }
    public bool IsCouponApplied { get; set; }
    public Guid? ShippingMethodId { get; set; }
    public string? FreeShippingHint { get; set; }

    public bool IsEmpty => Items.Count == 0;
}

public class CartItemDto
{
    public Guid Id { get; set; }
    public ProductCardDto Product { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPrice => Product.FinalPrice;
    public decimal TotalPrice => UnitPrice * Quantity;
    public decimal ProductDiscount => (Product.Price - Product.FinalPrice) * Quantity;
    public int StockQuantity => Product.StockQuantity;
    public bool HasStockWarning => Quantity > Product.StockQuantity;
}
