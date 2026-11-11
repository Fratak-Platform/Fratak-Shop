using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Shopping;

public class CartItem : BaseEntity
{
    public Guid CartId { get; set; }

    public Guid ProductId { get; set; }

    public int Quantity { get; set; }

    /// <summary>
    /// دور ۲۸ (#۸۴): قیمت نهایی‌ای که مشتری هنگام افزودن به سبد دید (تومان). اگر تا تسویه قیمت عوض شود، ثبت سفارش متوقف
    /// و تغییر با پیام روشن نشان داده می‌شود؛ بعد از دیدن، قیمت جدید جای این مقدار می‌نشیند. null = ردیف قدیمی (بدون مقایسه).
    /// </summary>
    public decimal? SeenPrice { get; set; }

    [ForeignKey(nameof(CartId))]
    public Cart Cart { get; set; } = null!;

    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;
}
