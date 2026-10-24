using Fratak_Shop.Core.DTOs.Engagement;
using Fratak_Shop.Core.DTOs.Notifications;
using Fratak_Shop.Core.DTOs.Wallets;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Security;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Wallets;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Marketing;

/// <summary>
/// Gift cards: a customer buys a card (online payment or own wallet money), gives the code to
/// anyone, and whoever enters it gets the amount in their wallet. Codes are random (≈80 bits),
/// stored as a SHA-256 hash for lookup plus an encrypted copy the buyer can view again;
/// redemption is atomic (a card can never be used twice) and failed attempts are limited.
/// </summary>
public interface IGiftCardService
{
    Task<OperationResult<GiftCardPurchaseResult>> PurchaseAsync(PurchaseGiftCardCommand command);

    /// <summary>Links the gateway payment that will pay a pending card.</summary>
    Task AttachPaymentAsync(Guid giftCardId, Guid paymentTransactionId);

    Task<OperationResult<decimal>> RedeemAsync(Guid userId, string? code);

    Task<List<GiftCardDto>> GetPurchasedAsync(Guid userId);

    Task<List<GiftCardDto>> GetRedeemedAsync(Guid userId);

    Task<GiftCardDto?> GetPendingAsync(Guid userId, Guid giftCardId);

    /// <summary>Background job: expires old cards and cancels abandoned unpaid ones.</summary>
    Task<int> MaintainAsync(CancellationToken cancellationToken = default);
}

public class GiftCardService(
    FratakShopDbContext context,
    IStoreSettingService settingService,
    IWalletService walletService,
    ISecretProtector protector,
    INotificationService notifications,
    IMemoryCache cache) : IGiftCardService
{
    private const int MaxFailedRedeemsPerHour = 5;

    public async Task<OperationResult<GiftCardPurchaseResult>> PurchaseAsync(PurchaseGiftCardCommand command)
    {
        var settings = await settingService.GetAsync();
        if (!settings.IsGiftCardEnabled)
            return OperationResult<GiftCardPurchaseResult>.Error("فروش کارت هدیه در حال حاضر فعال نیست.");

        var amount = decimal.Truncate(command.Amount);
        if (amount < settings.GiftCardMinAmount || amount > settings.GiftCardMaxAmount || amount % 1000 != 0)
            return OperationResult<GiftCardPurchaseResult>.Error(
                $"مبلغ کارت هدیه باید مضرب ۱٬۰۰۰ و بین {settings.GiftCardMinAmount:N0} تا {settings.GiftCardMaxAmount:N0} تومان باشد.");

        if (command.PaymentMethod is not (GiftCardPaymentMethod.OnlineGateway or GiftCardPaymentMethod.Wallet))
            return OperationResult<GiftCardPurchaseResult>.Error("روش پرداخت معتبر نیست.");

        if (command.PaymentMethod == GiftCardPaymentMethod.Wallet && !settings.IsWalletEnabled)
            return OperationResult<GiftCardPurchaseResult>.Error("کیف پول در حال حاضر فعال نیست.");

        if (command.PaymentMethod == GiftCardPaymentMethod.OnlineGateway && !settings.HasZarinPal)
            return OperationResult<GiftCardPurchaseResult>.Error("پرداخت اینترنتی در حال حاضر فعال نیست؛ از کیف پول استفاده کنید.");

        // Unpaid cards can't pile up (each one is a pending payment).
        var pending = await context.GiftCards.CountAsync(g => g.PurchaserUserId == command.UserId && g.Status == GiftCardStatus.PendingPayment);
        if (pending >= 3)
            return OperationResult<GiftCardPurchaseResult>.Error("چند کارت هدیه پرداخت‌نشده دارید؛ ابتدا آن‌ها را پرداخت یا رها کنید (پس از یک روز خودکار لغو می‌شوند).");

        var code = GiftCardCode.Normalize(GiftCardCode.Generate())!;
        var now = DateTimeOffset.UtcNow;
        var card = new GiftCard
        {
            Reference = GiftCardCode.NewReference(),
            CodeHash = GiftCardCode.Hash(code),
            CodeLast4 = code[^4..],
            ProtectedCode = protector.Protect(code),
            Amount = amount,
            PurchaserUserId = command.UserId,
            PaymentMethod = command.PaymentMethod,
            RecipientName = Clean(command.RecipientName, 100),
            Message = Clean(command.Message, 500),
            Status = GiftCardStatus.PendingPayment,
            ExpiresAt = now.AddDays(settings.GiftCardValidDays),
        };

        if (command.PaymentMethod == GiftCardPaymentMethod.OnlineGateway)
        {
            context.GiftCards.Add(card);
            await context.SaveChangesAsync();
            return OperationResult<GiftCardPurchaseResult>.Success(new GiftCardPurchaseResult(card.Id, card.Reference, true));
        }

        // Wallet: only the customer's own money (promotional credit can't be turned into a transferable card).
        await using var transaction = await context.Database.BeginTransactionAsync();
        card.Status = GiftCardStatus.Active;
        card.ActivatedAt = now;
        context.GiftCards.Add(card);
        await context.SaveChangesAsync();

        var debit = await walletService.DebitAsync(new WalletEntry
        {
            UserId = command.UserId,
            Amount = amount,
            Type = WalletTransactionType.GiftCardPurchase,
            Description = $"خرید کارت هدیه {card.Reference}",
            DebitSource = WalletDebitSource.WithdrawableOnly,
        });

        if (!debit.IsSuccess())
        {
            await transaction.RollbackAsync();
            context.ChangeTracker.Clear();
            return OperationResult<GiftCardPurchaseResult>.Error(debit.Message);
        }

        await transaction.CommitAsync();
        await NotifyPurchasedAsync(card.PurchaserUserId, card.Reference, card.Amount);
        return OperationResult<GiftCardPurchaseResult>.Success(new GiftCardPurchaseResult(card.Id, card.Reference, false),
            "کارت هدیه با موفقیت صادر شد.");
    }

    public async Task AttachPaymentAsync(Guid giftCardId, Guid paymentTransactionId)
    {
        await context.GiftCards
            .Where(g => g.Id == giftCardId && g.Status == GiftCardStatus.PendingPayment)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.PaymentTransactionId, paymentTransactionId).SetProperty(g => g.UpdatedAt, DateTimeOffset.UtcNow));
    }

    public async Task<OperationResult<decimal>> RedeemAsync(Guid userId, string? code)
    {
        var settings = await settingService.GetAsync();
        if (!settings.IsGiftCardEnabled)
            return OperationResult<decimal>.Error("استفاده از کارت هدیه در حال حاضر فعال نیست.");

        var failKey = $"core:giftcard-fail:{userId:N}";
        var failures = cache.TryGetValue(failKey, out int count) ? count : 0;
        if (failures >= MaxFailedRedeemsPerHour)
            return OperationResult<decimal>.Error("تعداد تلاش‌های ناموفق زیاد بود؛ یک ساعت دیگر دوباره تلاش کنید.");

        var normalized = GiftCardCode.Normalize(code);
        if (normalized is null)
            return Fail(failKey, failures, "کد کارت هدیه معتبر نیست (۱۶ کاراکتر، مثل ABCD-EFGH-JKMN-PQRS).");

        var hash = GiftCardCode.Hash(normalized);
        var card = await context.GiftCards
            .Where(g => g.CodeHash == hash)
            .Select(g => new { g.Id, g.Status, g.Amount, g.ExpiresAt, g.Reference, g.PurchaserUserId })
            .FirstOrDefaultAsync();

        if (card is null)
            return Fail(failKey, failures, "کارت هدیه‌ای با این کد یافت نشد.");

        var now = DateTimeOffset.UtcNow;
        switch (card.Status)
        {
            case GiftCardStatus.Redeemed:
                return OperationResult<decimal>.Error("این کارت هدیه قبلاً استفاده شده است.");
            case GiftCardStatus.PendingPayment:
                return OperationResult<decimal>.Error("این کارت هدیه هنوز پرداخت نشده است.");
            case GiftCardStatus.Cancelled:
                return OperationResult<decimal>.Error("این کارت هدیه باطل شده است.");
            case GiftCardStatus.Expired:
                return OperationResult<decimal>.Error("مهلت استفاده از این کارت هدیه به پایان رسیده است.");
        }

        if (card.ExpiresAt.HasValue && card.ExpiresAt < now)
            return OperationResult<decimal>.Error("مهلت استفاده از این کارت هدیه به پایان رسیده است.");

        var withdrawable = settings.IsGiftCardCreditWithdrawable;

        await using var transaction = await context.Database.BeginTransactionAsync();

        // The conditional update is the lock: of two parallel redeems only one changes the row.
        var claimed = await context.GiftCards
            .Where(g => g.Id == card.Id && g.Status == GiftCardStatus.Active)
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.Status, GiftCardStatus.Redeemed)
                .SetProperty(g => g.RedeemedByUserId, userId)
                .SetProperty(g => g.RedeemedAt, now)
                .SetProperty(g => g.RedeemedAsWithdrawable, withdrawable)
                .SetProperty(g => g.UpdatedAt, now));

        if (claimed == 0)
        {
            await transaction.RollbackAsync();
            return OperationResult<decimal>.Error("این کارت هدیه قبلاً استفاده شده است.");
        }

        var credit = await walletService.CreditAsync(new WalletEntry
        {
            UserId = userId,
            Amount = card.Amount,
            Type = WalletTransactionType.GiftCardRedeem,
            Description = $"کارت هدیه {card.Reference}",
            NonWithdrawableAmount = withdrawable ? 0 : card.Amount,
        });

        if (!credit.IsSuccess())
        {
            await transaction.RollbackAsync();
            return OperationResult<decimal>.Error(credit.Message);
        }

        await transaction.CommitAsync();
        cache.Remove(failKey);

        await notifications.NotifyAsync(new NotificationRequest
        {
            UserId = userId,
            Event = NotificationEvent.GiftCardRedeemed,
            Category = NotificationCategory.Wallet,
            LinkUrl = "/profile/wallet",
            Values = { ["Amount"] = card.Amount.ToString("N0"), ["Reference"] = card.Reference },
        });

        return OperationResult<decimal>.Success(card.Amount,
            $"مبلغ {card.Amount:N0} تومان به کیف پول شما اضافه شد" + (withdrawable ? "." : " (قابل استفاده برای خرید)."));
    }

    public async Task<List<GiftCardDto>> GetPurchasedAsync(Guid userId)
    {
        var rows = await context.GiftCards
            .Where(g => g.PurchaserUserId == userId && g.Status != GiftCardStatus.Cancelled)
            .OrderByDescending(g => g.CreatedAt)
            .Take(100)
            .ToListAsync();

        return rows.Select(g => Map(g, userId, revealCode: g.Status == GiftCardStatus.Active)).ToList();
    }

    public async Task<List<GiftCardDto>> GetRedeemedAsync(Guid userId)
    {
        var rows = await context.GiftCards
            .Where(g => g.RedeemedByUserId == userId)
            .OrderByDescending(g => g.RedeemedAt)
            .Take(100)
            .ToListAsync();

        return rows.Select(g => Map(g, userId, revealCode: false)).ToList();
    }

    public async Task<GiftCardDto?> GetPendingAsync(Guid userId, Guid giftCardId)
    {
        var card = await context.GiftCards.FirstOrDefaultAsync(g => g.Id == giftCardId && g.PurchaserUserId == userId && g.Status == GiftCardStatus.PendingPayment);
        return card is null ? null : Map(card, userId, revealCode: false);
    }

    public async Task<int> MaintainAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expired = await context.GiftCards
            .Where(g => g.Status == GiftCardStatus.Active && g.ExpiresAt != null && g.ExpiresAt < now)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.Status, GiftCardStatus.Expired).SetProperty(g => g.UpdatedAt, now), cancellationToken);

        // Unpaid for a day: the gateway session is long gone. A late successful payment still
        // reaches the customer (the payment service refunds it to the wallet).
        var abandonedBefore = now.AddDays(-1);
        var cancelled = await context.GiftCards
            .Where(g => g.Status == GiftCardStatus.PendingPayment && g.CreatedAt < abandonedBefore)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.Status, GiftCardStatus.Cancelled).SetProperty(g => g.UpdatedAt, now), cancellationToken);

        return expired + cancelled;
    }

    /// <summary>Called by the payment service after a verified gateway payment (inside its transaction).</summary>
    public static async Task<GiftCardActivation?> ActivatePaidAsync(FratakShopDbContext context, Guid paymentTransactionId)
    {
        var now = DateTimeOffset.UtcNow;
        var card = await context.GiftCards
            .Where(g => g.PaymentTransactionId == paymentTransactionId)
            .Select(g => new { g.Id, g.Status, g.Reference, g.Amount, g.PurchaserUserId })
            .FirstOrDefaultAsync();

        if (card is null)
            return null;

        var activated = await context.GiftCards
            .Where(g => g.Id == card.Id && g.Status == GiftCardStatus.PendingPayment)
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.Status, GiftCardStatus.Active)
                .SetProperty(g => g.ActivatedAt, now)
                .SetProperty(g => g.UpdatedAt, now));

        return new GiftCardActivation(card.Id, card.Reference, card.Amount, card.PurchaserUserId, activated > 0);
    }

    public Task NotifyPurchasedAsync(Guid userId, string reference, decimal amount) =>
        notifications.NotifyAsync(new NotificationRequest
        {
            UserId = userId,
            Event = NotificationEvent.GiftCardPurchased,
            Category = NotificationCategory.Wallet,
            LinkUrl = "/profile/gift-cards",
            Values = { ["Amount"] = amount.ToString("N0"), ["Reference"] = reference },
        });

    private GiftCardDto Map(GiftCard g, Guid userId, bool revealCode)
    {
        string? code = null;
        if (revealCode && g.PurchaserUserId == userId && g.ProtectedCode is not null)
        {
            var plain = protector.Unprotect(g.ProtectedCode);
            code = plain is null ? null : GiftCardCode.Format(plain);
        }

        return new GiftCardDto
        {
            Id = g.Id,
            Reference = g.Reference,
            Amount = g.Amount,
            Status = g.Status,
            PaymentMethod = g.PaymentMethod,
            RecipientName = g.RecipientName,
            Message = g.Message,
            CodeLast4 = g.CodeLast4,
            Code = code,
            CreatedAt = g.CreatedAt,
            ActivatedAt = g.ActivatedAt,
            ExpiresAt = g.ExpiresAt,
            RedeemedAt = g.RedeemedAt,
            RedeemedByMe = g.RedeemedByUserId == userId,
            IsPurchasedByMe = g.PurchaserUserId == userId,
        };
    }

    private OperationResult<decimal> Fail(string key, int failures, string message)
    {
        cache.Set(key, failures + 1, TimeSpan.FromHours(1));
        return OperationResult<decimal>.Error(message);
    }

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var text = value.Trim();
        return text.Length <= max ? text : text[..max];
    }
}

public sealed record GiftCardActivation(Guid Id, string Reference, decimal Amount, Guid PurchaserUserId, bool Activated);
