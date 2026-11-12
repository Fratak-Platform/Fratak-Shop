using Fratak_Shop.Data.Entities.Marketing;

namespace Fratak_Shop.Core.DTOs.Marketing;

/// <summary>Coupon as shown to customers (club page, checkout hints).</summary>
public class CouponDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public CouponType Type { get; set; }
    public decimal Value { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public decimal MinOrderAmount { get; set; }
    public decimal? MaxOrderAmount { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public bool FirstOrderOnly { get; set; }
    public bool IsPersonal { get; set; }

    /// <summary>Uses left for the current customer (null = unlimited / unknown).</summary>
    public int? RemainingUses { get; set; }

    /// <summary>"۱۰٪ تا سقف ۵۰۰٬۰۰۰ تومان", "۳۰۰٬۰۰۰ تومان", "ارسال رایگان".</summary>
    public string ValueText => Type switch
    {
        CouponType.Percentage => MaxDiscountAmount is > 0
            ? $"{Value:0.##}٪ تا سقف {MaxDiscountAmount:N0} تومان"
            : $"{Value:0.##}٪",
        CouponType.FixedAmount => $"{Value:N0} تومان",
        CouponType.FreeShipping => "ارسال رایگان",
        _ => string.Empty,
    };
}

/// <summary>Product line of a cart / order for coupon evaluation.</summary>
public record CouponLine(Guid ProductId, int Quantity);

public class CouponEvaluationRequest
{
    public string? Code { get; set; }
    public Guid? UserId { get; set; }
    public List<CouponLine> Lines { get; set; } = [];

    /// <summary>Shipping cost of the selected method (free-shipping coupons); null = not chosen yet.</summary>
    public decimal? ShippingCost { get; set; }

    /// <summary>
    /// Per-user rules (first order, uses per customer) need a signed-in customer. On the
    /// cart page guests may still preview a coupon; checkout always validates them.
    /// </summary>
    public bool RequireUser { get; set; }
}

/// <summary>Result of applying a coupon to a cart.</summary>
public class CouponEvaluation
{
    public Guid CouponId { get; set; }
    public string Code { get; set; } = null!;
    public string Title { get; set; } = null!;
    public CouponType Type { get; set; }

    /// <summary>Part of the cart the coupon applies to (after product discounts).</summary>
    public decimal EligibleAmount { get; set; }

    /// <summary>Discount on the items.</summary>
    public decimal ItemsDiscount { get; set; }

    /// <summary>The coupon makes shipping free.</summary>
    public bool FreeShipping { get; set; }

    /// <summary>Shipping discount for the given shipping cost (0 when unknown).</summary>
    public decimal ShippingDiscount { get; set; }

    public decimal TotalDiscount => ItemsDiscount + ShippingDiscount;
}
