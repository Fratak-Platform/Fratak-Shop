using Fratak_Shop.Core.DTOs.Payments;
using Fratak_Shop.Core.DTOs.Wallets;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Wallets;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Payments;
using Fratak_Shop.Data.Entities.Wallets;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Payments;

public class PaymentTransactionService(
    FratakShopDbContext context,
    IOrderService orderService,
    IWalletService walletService) : IPaymentTransactionService
{
    public async Task<OperationResult<Guid>> CreateAsync(CreatePaymentCommand command)
    {
        if (command.Amount < 1000 || decimal.Truncate(command.Amount) != command.Amount)
            return OperationResult<Guid>.Error("مبلغ پرداخت معتبر نیست (حداقل ۱٬۰۰۰ تومان).");

        if (command.Purpose == PaymentPurpose.OrderPayment && command.OrderId is null)
            return OperationResult<Guid>.Error("سفارش پرداخت مشخص نیست.");

        if (command.Purpose == PaymentPurpose.WalletTopUp && command.UserId is null)
            return OperationResult<Guid>.Error("برای شارژ کیف پول وارد حساب کاربری شوید.");

        var transaction = new PaymentTransaction
        {
            Purpose = command.Purpose,
            UserId = command.UserId,
            OrderId = command.OrderId,
            Amount = command.Amount,
            Gateway = command.Gateway,
            Status = PaymentTransactionStatus.Pending,
        };

        context.PaymentTransactions.Add(transaction);
        await context.SaveChangesAsync();

        return OperationResult<Guid>.Success(transaction.Id);
    }

    public async Task SetAuthorityAsync(Guid id, string authority)
    {
        var now = DateTimeOffset.UtcNow;
        await context.PaymentTransactions
            .Where(t => t.Id == id && t.Status == PaymentTransactionStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Authority, authority)
                .SetProperty(t => t.UpdatedAt, now));
    }

    public async Task<PaymentTransactionDto?> GetByIdAsync(Guid id)
    {
        return await Project(context.PaymentTransactions.Where(t => t.Id == id)).FirstOrDefaultAsync();
    }

    public async Task<PaymentTransactionDto?> GetByAuthorityAsync(string authority)
    {
        if (string.IsNullOrWhiteSpace(authority) || authority.Length > 100)
            return null;

        return await Project(context.PaymentTransactions.Where(t => t.Authority == authority)).FirstOrDefaultAsync();
    }

    public async Task<bool> TryBeginVerifyAsync(Guid id)
    {
        var now = DateTimeOffset.UtcNow;
        var affected = await context.PaymentTransactions
            .Where(t => t.Id == id && t.Status == PaymentTransactionStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, PaymentTransactionStatus.Verifying)
                .SetProperty(t => t.UpdatedAt, now));

        return affected == 1;
    }

    public async Task ReleaseVerifyAsync(Guid id, string message)
    {
        var now = DateTimeOffset.UtcNow;
        await context.PaymentTransactions
            .Where(t => t.Id == id && t.Status == PaymentTransactionStatus.Verifying)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, PaymentTransactionStatus.Pending)
                .SetProperty(t => t.Message, Limit(message, 500))
                .SetProperty(t => t.UpdatedAt, now));
    }

    public async Task MarkFailedAsync(Guid id, PaymentTransactionStatus status, int? code, string message)
    {
        if (status is not (PaymentTransactionStatus.Failed or PaymentTransactionStatus.Cancelled))
            status = PaymentTransactionStatus.Failed;

        var now = DateTimeOffset.UtcNow;
        await context.PaymentTransactions
            .Where(t => t.Id == id && (t.Status == PaymentTransactionStatus.Pending || t.Status == PaymentTransactionStatus.Verifying))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, status)
                .SetProperty(t => t.ResultCode, code)
                .SetProperty(t => t.Message, Limit(message, 500))
                .SetProperty(t => t.UpdatedAt, now));
    }

    public async Task<PaymentCompletionResult> CompleteAsync(Guid id, string refId, string? cardPan, int code)
    {
        await using var dbTransaction = await context.Database.BeginTransactionAsync();
        var now = DateTimeOffset.UtcNow;

        var affected = await context.PaymentTransactions
            .Where(t => t.Id == id && t.Status == PaymentTransactionStatus.Verifying)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, PaymentTransactionStatus.Succeeded)
                .SetProperty(t => t.RefId, Limit(refId, 100))
                .SetProperty(t => t.CardPan, Limit(cardPan, 50))
                .SetProperty(t => t.ResultCode, code)
                .SetProperty(t => t.VerifiedAt, now)
                .SetProperty(t => t.Message, "پرداخت تایید شد")
                .SetProperty(t => t.UpdatedAt, now));

        var payment = await GetByIdAsync(id);
        if (payment is null)
            return new PaymentCompletionResult { Succeeded = false, Message = "تراکنش یافت نشد." };

        if (affected == 0)
        {
            // Another request already completed (or failed) this payment.
            await dbTransaction.RollbackAsync();
            return new PaymentCompletionResult
            {
                Succeeded = payment.Status == PaymentTransactionStatus.Succeeded,
                Purpose = payment.Purpose,
                OrderNumber = payment.OrderNumber,
                RefId = payment.RefId,
                Message = payment.Status == PaymentTransactionStatus.Succeeded
                    ? "این پرداخت قبلاً تایید و ثبت شده است."
                    : "این تراکنش قابل تایید نیست.",
            };
        }

        var result = new PaymentCompletionResult
        {
            Succeeded = true,
            Purpose = payment.Purpose,
            OrderNumber = payment.OrderNumber,
            RefId = refId,
        };

        if (payment.Purpose == PaymentPurpose.WalletTopUp)
        {
            var credit = await walletService.CreditAsync(new WalletEntry
            {
                UserId = payment.UserId!.Value,
                Amount = payment.Amount,
                Type = WalletTransactionType.TopUp,
                Description = $"شارژ کیف پول — کد پیگیری {refId}",
                PaymentTransactionId = payment.Id,
            });

            if (!credit.IsSuccess())
                throw new InvalidOperationException($"Wallet top-up {payment.Id} could not be credited: {credit.Message}");

            result.Message = $"کیف پول شما به مبلغ {payment.Amount:N0} تومان شارژ شد.";
        }
        else
        {
            var outcome = await orderService.ApplyOnlinePaymentAsync(payment.OrderId!.Value, payment.Amount, refId);

            if (outcome == OrderPaymentOutcome.NotPayable)
            {
                if (payment.UserId.HasValue)
                {
                    var credit = await walletService.CreditAsync(new WalletEntry
                    {
                        UserId = payment.UserId.Value,
                        Amount = payment.Amount,
                        Type = WalletTransactionType.OrderRefund,
                        Description = $"مبلغ پرداختی سفارش {payment.OrderNumber} (لغو یا منقضی شده) — کد پیگیری {refId}",
                        OrderId = payment.OrderId,
                        PaymentTransactionId = payment.Id,
                    });

                    if (!credit.IsSuccess())
                        throw new InvalidOperationException($"Payment {payment.Id} could not be refunded to the wallet: {credit.Message}");

                    result.CreditedToWallet = true;
                    result.Message = "مهلت پرداخت این سفارش به پایان رسیده یا سفارش لغو شده بود؛ مبلغ پرداختی به کیف پول شما واریز شد.";
                }
                else
                {
                    await context.PaymentTransactions
                        .Where(t => t.Id == id)
                        .ExecuteUpdateAsync(s => s.SetProperty(t => t.Message, "سفارش قابل پرداخت نبود؛ نیاز به بازگشت وجه دستی"));

                    result.Message = "پرداخت انجام شد اما سفارش دیگر قابل پرداخت نبود؛ مبلغ توسط پشتیبانی به حساب شما برگشت داده می‌شود.";
                }
            }
            else
            {
                result.Message = "پرداخت با موفقیت انجام شد و سفارش شما تایید شد.";
            }
        }

        await dbTransaction.CommitAsync();
        return result;
    }

    public async Task<List<PaymentTransactionDto>> GetByOrderAsync(Guid orderId)
    {
        return await Project(context.PaymentTransactions.Where(t => t.OrderId == orderId))
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<PaymentTransactionDto>> GetByUserAsync(Guid userId, int take = 50)
    {
        return await Project(context.PaymentTransactions.Where(t => t.UserId == userId))
            .OrderByDescending(t => t.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync();
    }

    private static IQueryable<PaymentTransactionDto> Project(IQueryable<PaymentTransaction> query)
    {
        return query.Select(t => new PaymentTransactionDto
        {
            Id = t.Id,
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt,
            UserId = t.UserId,
            OrderId = t.OrderId,
            OrderNumber = t.Order != null ? t.Order.OrderNumber : null,
            Purpose = t.Purpose,
            Gateway = t.Gateway,
            Amount = t.Amount,
            Authority = t.Authority,
            RefId = t.RefId,
            CardPan = t.CardPan,
            Status = t.Status,
            ResultCode = t.ResultCode,
            Message = t.Message,
            VerifiedAt = t.VerifiedAt,
        });
    }

    private static string? Limit(string? value, int length) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= length ? value : value[..length];
}
