using Fratak_Shop.Core.DTOs.Orders.Query;

namespace Fratak_Shop.Web.Services.Payments;

/// <summary>
/// Online payment provider abstraction. The storefront only talks to this
/// interface, so a real gateway (ZarinPal, IDPay, ...) can replace the
/// sandbox implementation without touching the checkout pages.
/// </summary>
public interface IPaymentGateway
{
    string Name { get; }

    /// <summary>Starts a payment and returns the URL the customer must be redirected to.</summary>
    Task<PaymentRequestResult> RequestAsync(OrderDto order, string callbackUrl);

    /// <summary>Verifies the gateway callback and returns the transaction reference on success.</summary>
    Task<PaymentVerifyResult> VerifyAsync(OrderDto order, IQueryCollection callbackQuery);
}

public record PaymentRequestResult(bool Succeeded, string? RedirectUrl, string? Error = null);

public record PaymentVerifyResult(bool Succeeded, string? TransactionId, string? Error = null);

/// <summary>
/// Development gateway: shows a fake bank page where the payment can be
/// approved or cancelled. Enabled with <c>Shop:EnableSandboxGateway</c>.
/// </summary>
public class SandboxPaymentGateway : IPaymentGateway
{
    public string Name => "درگاه آزمایشی";

    public Task<PaymentRequestResult> RequestAsync(OrderDto order, string callbackUrl)
    {
        var url = $"/payment/sandbox/{Uri.EscapeDataString(order.OrderNumber)}?callback={Uri.EscapeDataString(callbackUrl)}";
        return Task.FromResult(new PaymentRequestResult(true, url));
    }

    public Task<PaymentVerifyResult> VerifyAsync(OrderDto order, IQueryCollection callbackQuery)
    {
        var status = callbackQuery["status"].ToString();

        if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new PaymentVerifyResult(false, null, "پرداخت توسط کاربر لغو شد."));

        var transactionId = $"SBX-{DateTimeOffset.UtcNow:yyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
        return Task.FromResult(new PaymentVerifyResult(true, transactionId));
    }
}
