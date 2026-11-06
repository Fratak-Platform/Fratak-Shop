using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.DTOs.Payments;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Payments;

namespace Fratak_Shop.Web.Services.Payments;

/// <summary>
/// Glue between the storefront and the payment gateway: starts payments for orders
/// and wallet top-ups, and handles the gateway callback (verify + apply, exactly once).
/// </summary>
public interface IPaymentFlowService
{
    Task<PaymentStartResult> StartOrderPaymentAsync(OrderDto order, Guid userId);
    Task<PaymentStartResult> StartWalletTopUpAsync(Guid userId, decimal amount);
    Task<PaymentStartResult> StartGiftCardPaymentAsync(Guid userId, Guid giftCardId);
    Task<PaymentCallbackResult> HandleCallbackAsync(string? authority, string? status);
}

public record PaymentStartResult(bool Succeeded, string? RedirectUrl, string? Error);

public class PaymentCallbackResult
{
    public bool Succeeded { get; init; }
    public PaymentPurpose Purpose { get; init; }
    public string Message { get; init; } = null!;
    public string? OrderNumber { get; init; }
    public string? RefId { get; init; }

    /// <summary>The verification could not be completed; refreshing the callback retries it.</summary>
    public bool IsPending { get; init; }
}

public class PaymentFlowService(
    IPaymentGateway gateway,
    IPaymentTransactionService transactions,
    IStoreSettingService settingService,
    IUserService userService,
    Fratak_Shop.Core.Services.Marketing.IGiftCardService giftCards,
    LinkGenerator links,
    IHttpContextAccessor httpContextAccessor,
    ILogger<PaymentFlowService> logger) : IPaymentFlowService
{
    public async Task<PaymentStartResult> StartOrderPaymentAsync(OrderDto order, Guid userId)
    {
        var settings = await settingService.GetAsync();
        if (!settings.HasZarinPal)
            return new PaymentStartResult(false, null, "پرداخت اینترنتی در حال حاضر فعال نیست.");

        if (order.UserId != userId)
            return new PaymentStartResult(false, null, "سفارش مورد نظر یافت نشد.");

        if (!order.IsAwaitingPayment(DateTimeOffset.UtcNow))
            return new PaymentStartResult(false, null, "این سفارش در حال حاضر قابل پرداخت نیست.");

        return await StartAsync(new CreatePaymentCommand
        {
            Purpose = PaymentPurpose.OrderPayment,
            UserId = userId,
            OrderId = order.Id,
            Amount = order.PayableAmount,
            Gateway = "ZarinPal",
        }, $"پرداخت سفارش {order.OrderNumber} - {settings.SiteName}", userId);
    }

    public async Task<PaymentStartResult> StartWalletTopUpAsync(Guid userId, decimal amount)
    {
        var settings = await settingService.GetAsync();
        if (!settings.CanTopUpWallet)
            return new PaymentStartResult(false, null, "شارژ کیف پول در حال حاضر فعال نیست.");

        if (amount < settings.WalletMinTopUpAmount || amount > settings.WalletMaxTopUpAmount)
            return new PaymentStartResult(false, null,
                $"مبلغ شارژ باید بین {settings.WalletMinTopUpAmount:N0} تا {settings.WalletMaxTopUpAmount:N0} تومان باشد.");

        return await StartAsync(new CreatePaymentCommand
        {
            Purpose = PaymentPurpose.WalletTopUp,
            UserId = userId,
            Amount = decimal.Truncate(amount),
            Gateway = "ZarinPal",
        }, $"شارژ کیف پول - {settings.SiteName}", userId);
    }

    public async Task<PaymentStartResult> StartGiftCardPaymentAsync(Guid userId, Guid giftCardId)
    {
        var settings = await settingService.GetAsync();
        if (!settings.IsGiftCardEnabled || !settings.HasZarinPal)
            return new PaymentStartResult(false, null, "پرداخت اینترنتی کارت هدیه در حال حاضر فعال نیست.");

        // Only the buyer's own unpaid card; the amount always comes from the database.
        var card = await giftCards.GetPendingAsync(userId, giftCardId);
        if (card is null)
            return new PaymentStartResult(false, null, "کارت هدیه پرداخت‌نشده‌ای با این مشخصات یافت نشد.");

        return await StartAsync(new CreatePaymentCommand
        {
            Purpose = PaymentPurpose.GiftCard,
            UserId = userId,
            Amount = card.Amount,
            Gateway = "ZarinPal",
        }, $"خرید کارت هدیه {card.Reference} - {settings.SiteName}", userId,
            paymentId => giftCards.AttachPaymentAsync(card.Id, paymentId));
    }

    private async Task<PaymentStartResult> StartAsync(CreatePaymentCommand command, string description, Guid userId, Func<Guid, Task>? onCreated = null)
    {
        var created = await transactions.CreateAsync(command);
        if (!created.IsSuccess())
            return new PaymentStartResult(false, null, created.Message);

        if (onCreated is not null)
            await onCreated(created.Data);

        var settings = await settingService.GetAsync();
        var user = await userService.GetUserByIdAsync(userId);
        var callbackUrl = CallbackUrl();

        var request = await gateway.RequestAsync(settings,
            new GatewayPaymentRequest(command.Amount, description, callbackUrl, user?.PhoneNumber, user?.Email));

        if (!request.Succeeded || request.Authority is null || request.RedirectUrl is null)
        {
            await transactions.MarkFailedAsync(created.Data, PaymentTransactionStatus.Failed, request.Code, request.Error ?? "request failed");
            return new PaymentStartResult(false, null, request.Error ?? "اتصال به درگاه پرداخت برقرار نشد.");
        }

        await transactions.SetAuthorityAsync(created.Data, request.Authority);
        return new PaymentStartResult(true, request.RedirectUrl, null);
    }

    public async Task<PaymentCallbackResult> HandleCallbackAsync(string? authority, string? status)
    {
        if (string.IsNullOrWhiteSpace(authority))
            return new PaymentCallbackResult { Message = "اطلاعات بازگشت از درگاه ناقص است." };

        var payment = await transactions.GetByAuthorityAsync(authority.Trim());
        if (payment is null)
            return new PaymentCallbackResult { Message = "تراکنش پرداخت یافت نشد." };

        if (payment.Status == PaymentTransactionStatus.Succeeded)
            return Result(payment, true, "این پرداخت قبلاً با موفقیت ثبت شده است.");

        if (payment.Status is PaymentTransactionStatus.Failed or PaymentTransactionStatus.Cancelled)
            return Result(payment, false, payment.Message ?? "پرداخت ناموفق بود.");

        if (!string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
        {
            await transactions.MarkFailedAsync(payment.Id, PaymentTransactionStatus.Cancelled, null, "پرداخت توسط کاربر لغو شد یا ناموفق بود.");
            return Result(payment, false, "پرداخت انجام نشد یا توسط شما لغو شد.");
        }

        // Only one request may verify a transaction (double clicks, refreshes, parallel callbacks).
        if (!await transactions.TryBeginVerifyAsync(payment.Id))
            return Result(payment, false, "پرداخت در حال بررسی است؛ چند لحظه بعد صفحه سفارش را بررسی کنید.", pending: true);

        var settings = await settingService.GetAsync();
        var verify = await gateway.VerifyAsync(settings, payment.Authority!, payment.Amount);

        if (!verify.Succeeded)
        {
            if (verify.IsTransient)
            {
                await transactions.ReleaseVerifyAsync(payment.Id, verify.Error ?? "verify unavailable");
                return Result(payment, false, "تایید پرداخت با خطای موقت مواجه شد؛ صفحه را دوباره بارگذاری کنید. در صورت کسر وجه، مبلغ از بین نمی‌رود.", pending: true);
            }

            await transactions.MarkFailedAsync(payment.Id, PaymentTransactionStatus.Failed, verify.Code, verify.Error ?? "verify failed");
            return Result(payment, false, verify.Error ?? "پرداخت تایید نشد.");
        }

        try
        {
            var completion = await transactions.CompleteAsync(payment.Id, verify.RefId!, verify.CardPan, verify.Code ?? 100);
            return new PaymentCallbackResult
            {
                Succeeded = completion.Succeeded && !completion.CreditedToWallet,
                Purpose = payment.Purpose,
                OrderNumber = payment.OrderNumber,
                RefId = verify.RefId,
                Message = completion.Message,
            };
        }
        catch (Exception exception)
        {
            // Verified at the gateway but not applied: keep it pending so a retry can finish it.
            logger.LogCritical(exception, "Verified payment {PaymentId} ({RefId}) could not be applied.", payment.Id, verify.RefId);
            await transactions.ReleaseVerifyAsync(payment.Id, "apply failed after verification");
            return Result(payment, false, "پرداخت شما تایید شد اما ثبت آن با خطا مواجه شد؛ صفحه را دوباره بارگذاری کنید یا با پشتیبانی تماس بگیرید.", pending: true);
        }
    }

    private static PaymentCallbackResult Result(PaymentTransactionDto payment, bool succeeded, string message, bool pending = false) => new()
    {
        Succeeded = succeeded,
        Purpose = payment.Purpose,
        OrderNumber = payment.OrderNumber,
        RefId = payment.RefId,
        Message = message,
        IsPending = pending,
    };

    private string CallbackUrl()
    {
        var httpContext = httpContextAccessor.HttpContext ?? throw new InvalidOperationException("No active HTTP context.");
        return links.GetUriByPage(httpContext, "/Payment/Callback")
               ?? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/payment/callback";
    }
}
