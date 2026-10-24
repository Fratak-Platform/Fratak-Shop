using Fratak_Shop.Core.DTOs.Wallets;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Wallets;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Wallets;

public class WalletService(FratakShopDbContext context, IStoreSettingService settingService) : IWalletService
{
    private const int MaxPendingWithdrawals = 1;

    public async Task<decimal> GetBalanceAsync(Guid userId)
    {
        return await context.Users
            .Where(u => u.Id == userId)
            .Select(u => u.WalletBalance)
            .FirstOrDefaultAsync();
    }

    public async Task<WalletSummaryDto> GetSummaryAsync(Guid userId)
    {
        var balance = await GetBalanceAsync(userId);

        var totals = await context.WalletTransactions
            .Where(t => t.UserId == userId)
            .GroupBy(t => 1)
            .Select(g => new
            {
                Credits = g.Where(t => t.Amount > 0).Sum(t => t.Amount),
                Debits = g.Where(t => t.Amount < 0).Sum(t => t.Amount),
            })
            .FirstOrDefaultAsync();

        var pending = await context.WithdrawalRequests
            .Where(r => r.UserId == userId && (r.Status == WithdrawalStatus.Pending || r.Status == WithdrawalStatus.Approved))
            .SumAsync(r => r.Amount);

        return new WalletSummaryDto
        {
            Balance = balance,
            PendingWithdrawals = pending,
            TotalCredits = totals?.Credits ?? 0,
            TotalDebits = Math.Abs(totals?.Debits ?? 0),
        };
    }

    public Task<OperationResult<decimal>> CreditAsync(WalletEntry entry) => ApplyAsync(entry, isCredit: true);

    public Task<OperationResult<decimal>> DebitAsync(WalletEntry entry) => ApplyAsync(entry, isCredit: false);

    private async Task<OperationResult<decimal>> ApplyAsync(WalletEntry entry, bool isCredit)
    {
        if (entry.UserId == Guid.Empty)
            return OperationResult<decimal>.Error("کاربر کیف پول مشخص نیست.");

        if (entry.Amount <= 0 || decimal.Truncate(entry.Amount) != entry.Amount)
            return OperationResult<decimal>.Error("مبلغ تراکنش کیف پول معتبر نیست.");

        var ownsTransaction = context.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await context.Database.BeginTransactionAsync() : null;

        var amount = entry.Amount;
        var now = DateTimeOffset.UtcNow;
        var users = context.Users.Where(u => u.Id == entry.UserId);

        // Single atomic UPDATE: the balance check and the change happen in the same statement.
        var affected = isCredit
            ? await users.ExecuteUpdateAsync(s => s
                .SetProperty(u => u.WalletBalance, u => u.WalletBalance + amount)
                .SetProperty(u => u.UpdatedAt, now))
            : await users.Where(u => u.WalletBalance >= amount).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.WalletBalance, u => u.WalletBalance - amount)
                .SetProperty(u => u.UpdatedAt, now));

        if (affected == 0)
        {
            if (transaction is not null)
                await transaction.RollbackAsync();

            var userExists = await context.Users.AnyAsync(u => u.Id == entry.UserId);
            return OperationResult<decimal>.Error(userExists
                ? "موجودی کیف پول کافی نیست."
                : "کاربر مورد نظر یافت نشد.");
        }

        var balance = await users.Select(u => u.WalletBalance).FirstAsync();

        context.WalletTransactions.Add(new WalletTransaction
        {
            UserId = entry.UserId,
            Type = entry.Type,
            Amount = isCredit ? amount : -amount,
            BalanceAfter = balance,
            Description = Truncate(entry.Description, 500),
            OrderId = entry.OrderId,
            PaymentTransactionId = entry.PaymentTransactionId,
            WithdrawalRequestId = entry.WithdrawalRequestId,
        });

        await context.SaveChangesAsync();

        if (transaction is not null)
            await transaction.CommitAsync();

        return OperationResult<decimal>.Success(balance);
    }

    public async Task<WalletTransactionFilterResult> GetTransactionsAsync(WalletTransactionFilterParams filterParams)
    {
        var query = context.WalletTransactions.AsQueryable();

        if (filterParams.UserId.HasValue)
            query = query.Where(t => t.UserId == filterParams.UserId.Value);

        if (filterParams.Type.HasValue)
            query = query.Where(t => t.Type == filterParams.Type.Value);

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((filterParams.PageId - 1) * filterParams.Take)
            .Take(filterParams.Take)
            .Select(t => new WalletTransactionDto
            {
                Id = t.Id,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt,
                UserId = t.UserId,
                Type = t.Type,
                Amount = t.Amount,
                BalanceAfter = t.BalanceAfter,
                Description = t.Description,
                OrderId = t.OrderId,
                OrderNumber = t.OrderId == null
                    ? null
                    : context.Orders.Where(o => o.Id == t.OrderId).Select(o => o.OrderNumber).FirstOrDefault(),
            })
            .ToListAsync();

        var result = new WalletTransactionFilterResult { Data = rows, FilterParams = filterParams };
        result.GeneratePaging(total, filterParams.Take, filterParams.PageId);
        return result;
    }

    public async Task<OperationResult> RequestWithdrawalAsync(CreateWithdrawalCommand command)
    {
        var settings = await settingService.GetAsync();
        if (!settings.CanWithdraw)
            return OperationResult.Error("امکان درخواست برداشت از کیف پول در حال حاضر غیرفعال است.");

        if (command.Amount <= 0 || decimal.Truncate(command.Amount) != command.Amount)
            return OperationResult.Error("مبلغ برداشت معتبر نیست.");

        if (settings.WalletMinWithdrawalAmount > 0 && command.Amount < settings.WalletMinWithdrawalAmount)
            return OperationResult.Error($"حداقل مبلغ برداشت {settings.WalletMinWithdrawalAmount:N0} تومان است.");

        var iban = IbanValidator.Normalize(command.Iban);
        if (!IbanValidator.IsValid(iban))
            return OperationResult.Error("شماره شبا معتبر نیست (IR و ۲۴ رقم).");

        var holder = command.AccountHolder?.Trim();
        if (string.IsNullOrWhiteSpace(holder) || holder.Length is < 3 or > 200)
            return OperationResult.Error("نام صاحب حساب را وارد کنید.");

        var pendingCount = await context.WithdrawalRequests
            .CountAsync(r => r.UserId == command.UserId && r.Status == WithdrawalStatus.Pending);

        if (pendingCount >= MaxPendingWithdrawals)
            return OperationResult.Error("یک درخواست برداشت در انتظار بررسی دارید؛ پس از تعیین وضعیت آن می‌توانید درخواست جدید ثبت کنید.");

        await using var transaction = await context.Database.BeginTransactionAsync();

        var request = new WithdrawalRequest
        {
            UserId = command.UserId,
            Amount = command.Amount,
            Iban = iban,
            AccountHolder = holder.ToPlainText(),
            Status = WithdrawalStatus.Pending,
        };

        context.WithdrawalRequests.Add(request);
        await context.SaveChangesAsync();

        var debit = await DebitAsync(new WalletEntry
        {
            UserId = command.UserId,
            Amount = command.Amount,
            Type = WalletTransactionType.Withdrawal,
            Description = "درخواست برداشت از کیف پول",
            WithdrawalRequestId = request.Id,
        });

        if (!debit.IsSuccess())
        {
            await transaction.RollbackAsync();
            context.ChangeTracker.Clear();
            return OperationResult.Error(debit.Message);
        }

        await transaction.CommitAsync();
        return OperationResult.Success("درخواست برداشت ثبت شد و مبلغ آن تا زمان واریز در کیف پول شما مسدود است.");
    }

    public async Task<OperationResult> CancelWithdrawalAsync(Guid userId, Guid requestId)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        var now = DateTimeOffset.UtcNow;

        // Atomic status change protects against double clicks / parallel requests.
        var affected = await context.WithdrawalRequests
            .Where(r => r.Id == requestId && r.UserId == userId && r.Status == WithdrawalStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, WithdrawalStatus.Cancelled)
                .SetProperty(r => r.ProcessedAt, now)
                .SetProperty(r => r.UpdatedAt, now));

        if (affected == 0)
        {
            await transaction.RollbackAsync();
            return OperationResult.Error("این درخواست قابل لغو نیست.");
        }

        var amount = await context.WithdrawalRequests.Where(r => r.Id == requestId).Select(r => r.Amount).FirstAsync();
        var credit = await CreditAsync(new WalletEntry
        {
            UserId = userId,
            Amount = amount,
            Type = WalletTransactionType.WithdrawalReversal,
            Description = "لغو درخواست برداشت",
            WithdrawalRequestId = requestId,
        });

        if (!credit.IsSuccess())
        {
            await transaction.RollbackAsync();
            return OperationResult.Error(credit.Message);
        }

        await transaction.CommitAsync();
        return OperationResult.Success("درخواست برداشت لغو شد و مبلغ آن به کیف پول برگشت.");
    }

    public async Task<WithdrawalFilterResult> GetWithdrawalsAsync(WithdrawalFilterParams filterParams)
    {
        var query = context.WithdrawalRequests.AsQueryable();

        if (filterParams.UserId.HasValue)
            query = query.Where(r => r.UserId == filterParams.UserId.Value);

        if (filterParams.Status.HasValue)
            query = query.Where(r => r.Status == filterParams.Status.Value);

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((filterParams.PageId - 1) * filterParams.Take)
            .Take(filterParams.Take)
            .Select(r => new WithdrawalRequestDto
            {
                Id = r.Id,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                UserId = r.UserId,
                UserPhoneNumber = r.User.PhoneNumber,
                UserFullName = ((r.User.FirstName ?? "") + " " + (r.User.LastName ?? "")).Trim(),
                Amount = r.Amount,
                Iban = r.Iban,
                AccountHolder = r.AccountHolder,
                Status = r.Status,
                AdminNote = r.AdminNote,
                BankTrackingNumber = r.BankTrackingNumber,
                ProcessedAt = r.ProcessedAt,
            })
            .ToListAsync();

        var result = new WithdrawalFilterResult { Data = rows, FilterParams = filterParams };
        result.GeneratePaging(total, filterParams.Take, filterParams.PageId);
        return result;
    }

    public async Task<OperationResult> ChangeWithdrawalStatusAsync(ChangeWithdrawalStatusCommand command)
    {
        if (command.Status is not (WithdrawalStatus.Approved or WithdrawalStatus.Paid or WithdrawalStatus.Rejected))
            return OperationResult.Error("وضعیت انتخاب‌شده معتبر نیست.");

        await using var transaction = await context.Database.BeginTransactionAsync();

        var request = await context.WithdrawalRequests.AsTracking().FirstOrDefaultAsync(r => r.Id == command.Id);
        if (request is null)
            return OperationResult.NotFound("درخواست برداشت یافت نشد.");

        var allowed = request.Status switch
        {
            WithdrawalStatus.Pending => command.Status is WithdrawalStatus.Approved or WithdrawalStatus.Paid or WithdrawalStatus.Rejected,
            WithdrawalStatus.Approved => command.Status is WithdrawalStatus.Paid or WithdrawalStatus.Rejected,
            _ => false,
        };

        if (!allowed)
            return OperationResult.Error("تغییر وضعیت این درخواست امکان‌پذیر نیست.");

        if (command.Status == WithdrawalStatus.Paid && string.IsNullOrWhiteSpace(command.BankTrackingNumber))
            return OperationResult.Error("شماره پیگیری واریز را وارد کنید.");

        request.Status = command.Status;
        request.AdminNote = Truncate(command.AdminNote.SanitizeOrNull(), 1000);
        request.BankTrackingNumber = Truncate(command.BankTrackingNumber.SanitizeOrNull(), 100);
        request.ProcessedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();

        if (command.Status == WithdrawalStatus.Rejected)
        {
            var credit = await CreditAsync(new WalletEntry
            {
                UserId = request.UserId,
                Amount = request.Amount,
                Type = WalletTransactionType.WithdrawalReversal,
                Description = "رد درخواست برداشت" + (string.IsNullOrWhiteSpace(request.AdminNote) ? "" : $" — {request.AdminNote}"),
                WithdrawalRequestId = request.Id,
            });

            if (!credit.IsSuccess())
            {
                await transaction.RollbackAsync();
                return OperationResult.Error(credit.Message);
            }
        }

        await transaction.CommitAsync();
        return OperationResult.Success("وضعیت درخواست برداشت به‌روزرسانی شد.");
    }

    public async Task<OperationResult> AdjustBalanceAsync(Guid userId, decimal amount, string description)
    {
        if (amount == 0)
            return OperationResult.Error("مبلغ را وارد کنید.");

        if (string.IsNullOrWhiteSpace(description))
            return OperationResult.Error("علت تغییر موجودی را وارد کنید.");

        var entry = new WalletEntry
        {
            UserId = userId,
            Amount = Math.Abs(amount),
            Type = amount > 0 ? WalletTransactionType.AdminCredit : WalletTransactionType.AdminDebit,
            Description = description.ToPlainText(),
        };

        var result = amount > 0 ? await CreditAsync(entry) : await DebitAsync(entry);
        return result.IsSuccess()
            ? OperationResult.Success("موجودی کیف پول به‌روزرسانی شد.")
            : OperationResult.Error(result.Message);
    }

    private static string? Truncate(string? value, int length) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= length ? value : value[..length];
}
