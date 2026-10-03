using Fratak_Shop.Core.DTOs.Checkout.Query;
using Fratak_Shop.Core.DTOs.Orders.Command;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Checkout;

/// <summary>
/// Checkout orchestration: it turns the cart into an order (price snapshot, stock,
/// coupon redemption, notification) and decides the next step of the customer
/// according to the chosen payment method.
/// </summary>
public interface ICheckoutService
{
    /// <summary>خلاصه سفارش برای صفحه تسویه؛ سبد خالی مقدار null برمی‌گرداند.</summary>
    Task<CheckoutSummaryDto?> GetSummaryAsync(Guid? userId, string? sessionKey, Guid? shippingMethodId,
        string? couponCode, CancellationToken cancellationToken = default);

    /// <summary>فرم تسویه به‌همراه پیش‌فرض‌های پروفایل کاربر.</summary>
    Task<CheckoutFormDto> GetFormAsync(Guid? userId, string? sessionKey, Guid? shippingMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>ثبت نهایی سفارش از سبد خرید.</summary>
    Task<OperationResult<CheckoutResultDto>> PlaceOrderAsync(CheckoutCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>شماره سفارش یکتا.</summary>
    Task<string> GenerateOrderNumberAsync(CancellationToken cancellationToken = default);
}
