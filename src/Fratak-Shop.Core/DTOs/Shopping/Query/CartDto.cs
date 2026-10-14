using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.DTOs.Shopping.Query;

/// <summary>
/// سبد خرید همراه اقلام و جمع‌های محاسبه‌شده.
/// </summary>
public class CartDto : BaseDto
{
    public Guid? UserId { get; set; }

    public string? SessionKey { get; set; }

    public List<CartItemDto> Items { get; set; } = [];

    /// <summary>تعداد ردیف‌های سبد خرید.</summary>
    public int ItemCount => Items.Count;

    /// <summary>تعداد کل کالاهای سبد خرید.</summary>
    public int TotalQuantity => Items.Sum(item => item.Quantity);

    /// <summary>جمع اقلام قبل از اعمال تخفیف.</summary>
    public decimal SubtotalAmount => Items.Sum(item => item.UnitPrice * item.Quantity);

    /// <summary>مجموع تخفیف اقلام سبد خرید.</summary>
    public decimal TotalDiscountAmount => Items.Sum(item => item.DiscountAmount);

    /// <summary>مبلغ قابل پرداخت سبد خرید (جمع پس از تخفیف).</summary>
    public decimal PayableAmount => Items.Sum(item => item.TotalAmount);
}

/// <summary>
/// یک ردیف سبد خرید. اگر محصول حذف یا غیرفعال شده باشد، اطلاعات محصول خالی
/// برمی‌گردد و <see cref="IsAvailable"/> منفی می‌شود.
/// </summary>
public class CartItemDto : BaseDto
{
    public Guid CartId { get; set; }
    public Guid ProductId { get; set; }

    public string? ProductName { get; set; }
    public string? ProductSlug { get; set; }
    public string? ImageName { get; set; }
    public string? ImageAlt { get; set; }

    /// <summary>قیمت اصلی محصول در لحظه مشاهده سبد.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>قیمت نهایی (با احتساب تخفیف فعال).</summary>
    public decimal FinalPrice { get; set; }

    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }

    public int Quantity { get; set; }
    public int StockQuantity { get; set; }

    public bool HasDiscount => DiscountAmount > 0;

    /// <summary>محصول هنوز در فروشگاه موجود است.</summary>
    public bool IsAvailable => !string.IsNullOrWhiteSpace(ProductName);

    /// <summary>موجودی انبار برای تعداد انتخابی کاربر کافی است.</summary>
    public bool HasEnoughStock => IsAvailable && StockQuantity >= Quantity;
}
