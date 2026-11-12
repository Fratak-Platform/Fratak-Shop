using Fratak_Shop.Core.Services.Events;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Core.DTOs.Wallets;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Fratak_Shop.Core.Services.Wallets;

public class WalletService(FratakShopDbContext context, IStoreSettingService settingService, IShopEvents events) : IWalletService
{
    private const int MaxConcurrencyRetries = 5;

    public async Task<decimal> GetBalanceAsync(Guid userId)
    {
        return await context.Users
            .Where(u => u.Id == userId)
            .Select(u => u.WalletBalance)
            .FirstOrDefaultAsync();
    }

    public async Task<WalletSummaryDto> GetSummaryAsync(Guid userId)
    {
        var balances = await context.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.WalletBalance, u.WalletBonusBalance })
            .FirstOrDefaultAsync();

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

        var balance = balances?.WalletBalance ?? 0;
        return new WalletSummaryDto
        {
            Balance = balance,
            BonusBalance = Math.Clamp(balances?.WalletBonusBalance ?? 0, 0, balance),
            PendingWithdrawals = pending,
            TotalCredits = totals?.Credits ?? 0,
            TotalDebits = Math.Abs(totals?.Debits ?? 0),
        };
    }

    public Task<OperationResult<WalletMovement>> CreditAsync(WalletEntry entry) => ApplyAsync(entry, isCredit: true);

    public Task<OperationResult<WalletMovement>> DebitAsync(WalletEntry entry) => ApplyAsync(entry, isCredit: false);

    /// <summary>
    /// Applies one movement to both parts of the balance (own money / promotional credit).
    /// Optimistic concurrency: the UPDATE only succeeds when the balances are still the
    /// values that were read, so parallel requests can never spend the same money twice.
    /// </summary>
    private async Task<OperationResult<WalletMovement>> ApplyAsync(WalletEntry entry, bool isCredit)
    {
        if (entry.UserId == Guid.Empty)
            return OperationResult<WalletMovement>.Error("کاربر کیف پول مشخص نیست.");

        if (entry.Amount <= 0 || decimal.Truncate(entry.Amount) != entry.Amount)
            return OperationResult<WalletMovement>.Error("مبلغ تراکنش کیف پول معتبر نیست.");

        var nonWithdrawable = isCredit ? entry.NonWithdrawableAmount : 0;
        if (nonWithdrawable < 0 || nonWithdrawable > entry.Amount || decimal.Truncate(nonWithdrawable) != nonWithdrawable)
            return OperationResult<WalletMovement>.Error("مبلغ اعتبار غیرقابل برداشت معتبر نیست.");

        var ownsTransaction = context.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await context.Database.BeginTransactionAsync() : null;

        var amount = entry.Amount;

        for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
        {
            var current = await context.Users
                .Where(u => u.Id == entry.UserId)
                .Select(u => new { u.WalletBalance, u.WalletBonusBalance })
                .FirstOrDefaultAsync();

            if (current is null)
                return await FailAsync(transaction, "کاربر مورد نظر یافت نشد.");

            var balance = current.WalletBalance;
            var bonus = Math.Clamp(current.WalletBonusBalance, 0, balance);
            var withdrawable = balance - bonus;
            decimal bonusChange;

            if (isCredit)
            {
                bonusChange = nonWithdrawable;
            }
            else
            {
                switch (entry.DebitSource)
                {
                    case WalletDebitSource.WithdrawableOnly:
                        if (withdrawable < amount)
                            return await FailAsync(transaction, bonus > 0
                                ? $"موجودی قابل برداشت شما {withdrawable:N0} تومان است؛ اعتبار هدیه و باشگاه مشتریان ({bonus:N0} تومان) فقط برای خرید قابل استفاده است."
                                : "موجودی قابل برداشت کافی نیست.");
                        bonusChange = 0;
                        break;

                    case WalletDebitSource.WithdrawableFirst:
                        if (balance < amount)
                            return await FailAsync(transaction, "موجودی کیف پول کافی نیست.");
                        bonusChange = -(amount - Math.Min(withdrawable, amount));
                        break;

                    default:
                        if (balance < amount)
                            return await FailAsync(transaction, "موجودی کیف پول کافی نیست.");
                        bonusChange = -Math.Min(bonus, amount);
                        break;
                }
            }

            var newBalance = isCredit ? balance + amount : balance - amount;
            var newBonus = bonus + bonusChange;
            var now = DateTimeOffset.UtcNow;
            var readBalance = current.WalletBalance;
            var readBonus = current.WalletBonusBalance;

            var affected = await context.Users
                .Where(u => u.Id == entry.UserId && u.WalletBalance == readBalance && u.WalletBonusBalance == readBonus)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.WalletBalance, newBalance)
                    .SetProperty(u => u.WalletBonusBalance, newBonus)
                    .SetProperty(u => u.UpdatedAt, now));

            if (affected == 0)
                continue; // changed in between – read again

            context.WalletTransactions.Add(new WalletTransaction
            {
                UserId = entry.UserId,
                Type = entry.Type,
                Amount = isCredit ? amount : -amount,
                BalanceAfter = newBalance,
                BonusAmount = bonusChange,
                BonusBalanceAfter = newBonus,
                Description = Truncate(entry.Description, 500),
                OrderId = entry.OrderId,
                PaymentTransactionId = entry.PaymentTransactionId,
                WithdrawalRequestId = entry.WithdrawalRequestId,
            });

            await context.SaveChangesAsync();

            if (transaction is not null)
                await transaction.CommitAsync();

            return OperationResult<WalletMovement>.Success(new WalletMovement(newBalance, newBonus, bonusChange));
        }

        return await FailAsync(transaction, "کیف پول هم‌زمان در حال تغییر است؛ لطفاً دوباره تلاش کنید.");
    }

    private static async Task<OperationResult<WalletMovement>> FailAsync(IDbContextTransaction? transaction, string message)
    {
        if (transaction is not null)
            await transaction.RollbackAsync();

        return OperationResult<WalletMovement>.Error(message);
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
                BonusAmount = t.BonusAmount,
                BonusBalanceAfter = t.BonusBalanceAfter,
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

        if (settings.WalletMaxWithdrawalAmount > 0 && command.Amount > settings.WalletMaxWithdrawalAmount)
            return OperationResult.Error($"حداکثر مبلغ هر درخواست برداشت {settings.WalletMaxWithdrawalAmount:N0} تومان است.");

        var holder = string.Join(' ', (command.AccountHolder ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (!BankAccountValidator.IsValidHolderName(holder))
            return OperationResult.Error("نام و نام خانوادگی صاحب حساب را به صورت کامل و فقط با حروف وارد کنید.");

        string? iban = null;
        if (!string.IsNullOrWhiteSpace(command.Iban))
        {
            iban = IbanValidator.Normalize(command.Iban);
            if (!IbanValidator.IsValid(iban))
                return OperationResult.Error("شماره شبا معتبر نیست (IR و ۲۴ رقم).");
        }

        string? card = null;
        if (!string.IsNullOrWhiteSpace(command.CardNumber))
        {
            card = BankAccountValidator.DigitsOnly(command.CardNumber);
            if (!BankAccountValidator.IsValidCardNumber(card))
                return OperationResult.Error("شماره کارت معتبر نیست (۱۶ رقم).");
        }

        if (iban is null && card is null)
            return OperationResult.Error("برای واریز، شماره کارت یا شماره شبا را وارد کنید.");

        var accountNumber = BankAccountValidator.NormalizeAccountNumber(command.AccountNumber);
        if (!string.IsNullOrEmpty(accountNumber) && !BankAccountValidator.IsValidAccountNumber(accountNumber))
            return OperationResult.Error("شماره حساب معتبر نیست.");

        var maxPending = Math.Max(1, settings.WalletMaxPendingWithdrawals);
        var pendingCount = await context.WithdrawalRequests
            .CountAsync(r => r.UserId == command.UserId && r.Status == WithdrawalStatus.Pending);

        if (pendingCount >= maxPending)
            return OperationResult.Error(maxPending == 1
                ? "یک درخواست برداشت در انتظار بررسی دارید؛ پس از تعیین وضعیت آن می‌توانید درخواست جدید ثبت کنید."
                : $"حداکثر {maxPending} درخواست برداشت در انتظار بررسی مجاز است.");

        await using var transaction = await context.Database.BeginTransactionAsync();

        var request = new WithdrawalRequest
        {
            UserId = command.UserId,
            Amount = command.Amount,
            Iban = iban,
            CardNumber = card,
            AccountNumber = string.IsNullOrEmpty(accountNumber) ? null : accountNumber,
            AccountHolder = holder.ToPlainText(200),
            Status = WithdrawalStatus.Pending,
        };

        context.WithdrawalRequests.Add(request);
        await context.SaveChangesAsync();

        // Only the customer's own money can leave the shop; promotional credit stays.
        var debit = await DebitAsync(new WalletEntry
        {
            UserId = command.UserId,
            Amount = command.Amount,
            Type = WalletTransactionType.Withdrawal,
            Description = "درخواست برداشت از کیف پول",
            WithdrawalRequestId = request.Id,
            DebitSource = WalletDebitSource.WithdrawableOnly,
        });

        if (!debit.IsSuccess())
        {
            await transaction.RollbackAsync();
            context.ChangeTracker.Clear();
            return OperationResult.Error(debit.Message);
        }

        await transaction.CommitAsync();
        await events.WalletChangedAsync(command.UserId, NotificationEvent.WithdrawalRequested, command.Amount);
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
                CardNumber = r.CardNumber,
                AccountNumber = r.AccountNumber,
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

        await events.WalletChangedAsync(request.UserId, command.Status switch
        {
            WithdrawalStatus.Approved => NotificationEvent.WithdrawalApproved,
            WithdrawalStatus.Paid => NotificationEvent.WithdrawalPaid,
            _ => NotificationEvent.WithdrawalRejected,
        }, request.Amount);

        return OperationResult.Success("وضعیت درخواست برداشت به‌روزرسانی شد.");
    }

    public async Task<OperationResult> AdjustBalanceAsync(Guid userId, decimal amount, string description, bool promotional = true)
    {
        if (amount == 0)
            return OperationResult.Error("مبلغ را وارد کنید.");

        if (string.IsNullOrWhiteSpace(description))
            return OperationResult.Error("علت تغییر موجودی را وارد کنید.");

        var value = Math.Abs(amount);
        var entry = new WalletEntry
        {
            UserId = userId,
            Amount = value,
            Type = amount > 0 ? WalletTransactionType.AdminCredit : WalletTransactionType.AdminDebit,
            Description = description.ToPlainText(500),
            // Gifts / compensations are promotional (never withdrawable); real-money corrections are not.
            NonWithdrawableAmount = amount > 0 && promotional ? value : 0,
            DebitSource = promotional ? WalletDebitSource.PromotionalFirst : WalletDebitSource.WithdrawableFirst,
        };

        var result = amount > 0 ? await CreditAsync(entry) : await DebitAsync(entry);
        if (!result.IsSuccess())
            return OperationResult.Error(result.Message);

        await events.WalletChangedAsync(userId, NotificationEvent.WalletAdjusted, amount, entry.Description);
        return OperationResult.Success("موجودی کیف پول به‌روزرسانی شد.");
    }

    public async Task<OperationResult> GrantPromotionalCreditAsync(Guid userId, decimal amount, string description)
    {
        if (amount <= 0)
            return OperationResult.Error("مبلغ اعتبار هدیه معتبر نیست.");

        var result = await CreditAsync(new WalletEntry
        {
            UserId = userId,
            Amount = decimal.Floor(amount),
            Type = WalletTransactionType.PromotionCredit,
            Description = description.ToPlainText(500),
            NonWithdrawableAmount = decimal.Floor(amount),
        });

        if (!result.IsSuccess())
            return OperationResult.Error(result.Message);

        await events.WalletChangedAsync(userId, NotificationEvent.WalletAdjusted, amount, description);
        return OperationResult.Success("اعتبار هدیه به کیف پول اضافه شد.");
    }

    private static string? Truncate(string? value, int length) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= length ? value : value[..length];
}
