using System.Globalization;
using Fratak_Shop.Core.DTOs.Payments.Query;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Payments;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Payments;

public class PaymentService(
    FratakShopDbContext context,
    ISiteSettingService settingService,
    INotificationService notificationService,
    ZarinPalGateway zarinPal) : IPaymentService
{
    public async Task<PaymentGatewayProvider> GetActiveProviderAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetStorefrontSettingsAsync();
        return settings.EnableOnlinePayment ? PaymentGatewayProvider.Sandbox : PaymentGatewayProvider.None;
    }

    public async Task<bool> IsOnlinePaymentEnabledAsync(CancellationToken cancellationToken = default) =>
        (await settingService.GetStorefrontSettingsAsync()).EnableOnlinePayment;

    public async Task<OperationResult<PaymentStartResult>> StartAsync(Guid orderId, Guid? userId, string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var order = await context.Orders.AsTracking()
            .FirstOrDefaultAsync(item => item.Id == orderId, cancellationToken);

        if (order is null)
            return OperationResult<PaymentStartResult>.NotFound("سفارش مورد نظر یافت نشد.");

        if (order.PaymentStatus == PaymentStatus.Paid)
            return OperationResult<PaymentStartResult>.Error("این سفارش قبلاً پرداخت شده است.");

        if (order.Status == OrderStatus.Cancelled)
            return OperationResult<PaymentStartResult>.Error("سفارش لغو شده است و قابل پرداخت نیست.");

        var provider = await GetActiveProviderAsync(cancellationToken);
        if (provider == PaymentGatewayProvider.None)
            return OperationResult<PaymentStartResult>.Error("پرداخت آنلاین در حال حاضر غیرفعال است.");

        var merchantId = await settingService.GetValueAsync("Payment.ZarinPalMerchantId");
        var siteUrl = await settingService.GetValueAsync("General.SiteUrl", "https://localhost:5001");
        var callbackUrl = $"{siteUrl.TrimEnd('/')}/payment/zarinpal/callback";
        var authority = $"SBX-{Guid.NewGuid():N}".ToUpperInvariant();
        var redirectUrl = $"/payment/sandbox?authority={authority}";

        if (!string.IsNullOrWhiteSpace(merchantId))
        {
            var zarinPal = await zarinPal.RequestAsync(
                merchantId.Trim(),
                decimal.ToInt64(decimal.Round(order.PayableAmount * 10m, 0)),
                callbackUrl,
                $"پرداخت سفارش {order.OrderNumber}",
                cancellationToken);

            if (zarinPal is null)
                return OperationResult<PaymentStartResult>.Error("ارتباط با درگاه سندباکس زرین‌پال برقرار نشد.");

            authority = zarinPal.Authority;
            redirectUrl = zarinPal.RedirectUrl;
        }

        var transaction = new PaymentTransaction
        {
            OrderId = order.Id,
            Gateway = provider,
            Amount = order.PayableAmount,
            Status = PaymentTransactionStatus.AwaitingCallback,
            Authority = authority,
            RedirectUrl = redirectUrl,
            CreatedByIp = ipAddress,
            RequestPayload = $"{{\"provider\":\"{provider}\",\"orderNumber\":\"{order.OrderNumber}\",\"amount\":{order.PayableAmount.ToString(CultureInfo.InvariantCulture)}}}",
        };

        context.PaymentTransactions.Add(transaction);
        await context.SaveChangesAsync(cancellationToken);

        return OperationResult<PaymentStartResult>.Success(new PaymentStartResult
        {
            TransactionId = transaction.Id,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            Amount = order.PayableAmount,
            Gateway = provider,
            RedirectUrl = transaction.RedirectUrl!,
        }, "به صفحه پرداخت منتقل می‌شوید.");
    }

    public async Task<OperationResult<PaymentResultDto>> HandleCallbackAsync(string authority, bool success,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authority))
            return OperationResult<PaymentResultDto>.Error("اطلاعات بازگشت از درگاه نامعتبر است.");

        var transaction = await context.PaymentTransactions.AsTracking()
            .FirstOrDefaultAsync(item => item.Authority == authority, cancellationToken);

        if (transaction is null)
            return OperationResult<PaymentResultDto>.NotFound("تراکنش مورد نظر یافت نشد.");

        var order = await context.Orders.AsTracking()
            .FirstOrDefaultAsync(item => item.Id == transaction.OrderId, cancellationToken);

        if (order is null)
            return OperationResult<PaymentResultDto>.NotFound("سفارش مرتبط با تراکنش یافت نشد.");

        if (success && !authority.StartsWith("SBX-", StringComparison.OrdinalIgnoreCase))
        {
            var merchantId = await settingService.GetValueAsync("Payment.ZarinPalMerchantId");
            if (string.IsNullOrWhiteSpace(merchantId) || !await zarinPal.VerifyAsync(
                    merchantId.Trim(),
                    decimal.ToInt64(decimal.Round(transaction.Amount * 10m, 0)),
                    authority,
                    cancellationToken))
            {
                success = false;
            }
        }

        if (transaction.Status == PaymentTransactionStatus.Succeeded)
        {
            return OperationResult<PaymentResultDto>.Success(new PaymentResultDto
            {
                IsSuccessful = true,
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                Amount = transaction.Amount,
                ReferenceId = transaction.ReferenceId,
                Message = "این تراکنش قبلاً با موفقیت تأیید شده است.",
            }, "پرداخت قبلاً تأیید شده است.");
        }

        if (!success)
        {
            transaction.Status = PaymentTransactionStatus.Cancelled;
            transaction.ErrorMessage = "کاربر پرداخت را لغو کرد یا درگاه تراکنش را نپذیرفت.";
            transaction.VerifyPayload = "{\"result\":\"cancelled\"}";

            order.PaymentStatus = PaymentStatus.Failed;

            await context.SaveChangesAsync(cancellationToken);

            if (order.UserId.HasValue)
            {
                await notificationService.NotifyUserAsync(order.UserId.Value, "پرداخت ناموفق",
                    $"پرداخت سفارش {order.OrderNumber} انجام نشد. می‌توانید مجدداً تلاش کنید یا روش پرداخت دیگری انتخاب کنید.",
                    NotificationType.Payment, $"/profile/orders/{order.Id}", cancellationToken);
            }

            return OperationResult<PaymentResultDto>.Success(new PaymentResultDto
            {
                IsSuccessful = false,
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                Amount = transaction.Amount,
                Message = "پرداخت ناموفق بود. سفارش شما همچنان قابل پیگیری است.",
            }, "پرداخت ناموفق بود.");
        }

        // Verification of the sandbox gateway: a successful reference is generated
        // and the order is marked as paid, exactly like the real provider would do.
        var referenceId = Random.Shared.Next(10000000, 99999999).ToString();
        var now = DateTimeOffset.UtcNow;

        transaction.Status = PaymentTransactionStatus.Succeeded;
        transaction.ReferenceId = referenceId;
        transaction.CardMask = "6037-****-****-" + Random.Shared.Next(1000, 9999);
        transaction.PaidAt = now;
        transaction.VerifiedAt = now;
        transaction.VerifyPayload = $"{{\"result\":\"success\",\"referenceId\":\"{referenceId}\"}}";

        var previousStatus = order.Status;
        order.PaymentStatus = PaymentStatus.Paid;
        order.PaidAt = now;
        order.TransactionId = referenceId;
        if (order.Status is OrderStatus.None or OrderStatus.Pending)
            order.Status = OrderStatus.Confirmed;

        context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            Status = order.Status,
            PreviousStatus = previousStatus,
            Note = $"پرداخت آنلاین با شماره پیگیری {referenceId} تأیید شد.",
            IsCustomerVisible = true,
        });

        await context.SaveChangesAsync(cancellationToken);

        if (order.UserId.HasValue)
        {
            await notificationService.NotifyUserAsync(order.UserId.Value, "پرداخت موفق",
                $"پرداخت سفارش {order.OrderNumber} با موفقیت انجام شد. شماره پیگیری: {PriceHelper.ToPersianDigits(referenceId)}",
                NotificationType.Payment, $"/profile/orders/{order.Id}", cancellationToken);
        }

        await notificationService.NotifyStaffAsync("سفارش پرداخت‌شده",
            $"سفارش {order.OrderNumber} به مبلغ {order.PayableAmount.ToToman()} پرداخت شد.",
            NotificationType.Order, $"/admin/orders/details/{order.Id}", cancellationToken);

        return OperationResult<PaymentResultDto>.Success(new PaymentResultDto
        {
            IsSuccessful = true,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            Amount = transaction.Amount,
            ReferenceId = referenceId,
            Message = "پرداخت با موفقیت انجام شد.",
        }, "پرداخت با موفقیت تأیید شد.");
    }

    public async Task<OperationResult> MarkAsPaidAsync(Guid orderId, string? referenceId, Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var order = await context.Orders.AsTracking().FirstOrDefaultAsync(item => item.Id == orderId, cancellationToken);
        if (order is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        if (order.PaymentStatus == PaymentStatus.Paid)
            return OperationResult.Error("این سفارش قبلاً پرداخت‌شده ثبت شده است.");

        var now = DateTimeOffset.UtcNow;
        var previousStatus = order.Status;

        order.PaymentStatus = PaymentStatus.Paid;
        order.PaidAt = now;
        if (!string.IsNullOrWhiteSpace(referenceId))
            order.TransactionId = referenceId;
        if (order.Status is OrderStatus.None or OrderStatus.Pending)
            order.Status = OrderStatus.Confirmed;

        context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            Status = order.Status,
            PreviousStatus = previousStatus,
            Note = "پرداخت سفارش توسط کارشناس تأیید شد.",
            ChangedByUserId = userId,
            IsCustomerVisible = true,
        });

        await context.SaveChangesAsync(cancellationToken);

        if (order.UserId.HasValue)
        {
            await notificationService.NotifyUserAsync(order.UserId.Value, "تأیید پرداخت",
                $"پرداخت سفارش {order.OrderNumber} تأیید شد و سفارش در حال آماده‌سازی است.",
                NotificationType.Payment, $"/profile/orders/{order.Id}", cancellationToken);
        }

        return OperationResult.Success("پرداخت سفارش تأیید شد.");
    }

    public async Task<List<PaymentTransactionDto>> GetForOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var transactions = await context.PaymentTransactions
            .Where(item => item.OrderId == orderId)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

        return transactions.Select(Map).ToList();
    }

    public async Task<PaymentTransactionDto?> GetLatestAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var transaction = await context.PaymentTransactions
            .Where(item => item.OrderId == orderId)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return transaction is null ? null : Map(transaction);
    }

    public async Task<PaymentTransaction?> GetByAuthorityAsync(string authority, CancellationToken cancellationToken = default) =>
        await context.PaymentTransactions.FirstOrDefaultAsync(item => item.Authority == authority, cancellationToken);

    private static PaymentTransactionDto Map(PaymentTransaction transaction) => new()
    {
        Id = transaction.Id,
        CreatedAt = transaction.CreatedAt,
        UpdatedAt = transaction.UpdatedAt,
        OrderId = transaction.OrderId,
        Gateway = transaction.Gateway,
        Amount = transaction.Amount,
        Status = transaction.Status,
        Authority = transaction.Authority,
        ReferenceId = transaction.ReferenceId,
        CardMask = transaction.CardMask,
        ErrorMessage = transaction.ErrorMessage,
        PaidAt = transaction.PaidAt,
        VerifiedAt = transaction.VerifiedAt,
    };
}
