using Fratak_Shop.Data.Entities.Marketing;

namespace Fratak_Shop.Core.DTOs.Engagement;

public sealed class PurchaseGiftCardCommand
{
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public GiftCardPaymentMethod PaymentMethod { get; set; }
    public string? RecipientName { get; set; }
    public string? Message { get; set; }
}

public sealed record GiftCardPurchaseResult(Guid GiftCardId, string Reference, bool RequiresPayment);

public sealed class GiftCardDto
{
    public Guid Id { get; init; }
    public string Reference { get; init; } = null!;
    public decimal Amount { get; init; }
    public GiftCardStatus Status { get; init; }
    public GiftCardPaymentMethod PaymentMethod { get; init; }
    public string? RecipientName { get; init; }
    public string? Message { get; init; }
    public string CodeLast4 { get; init; } = null!;

    /// <summary>Full code – only for the buyer and only while the card can still be used.</summary>
    public string? Code { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? RedeemedAt { get; init; }
    public bool RedeemedByMe { get; init; }
    public bool IsPurchasedByMe { get; init; }
}

/// <summary>"Invite friends" page of the profile.</summary>
public sealed class ReferralStatsDto
{
    public string? Code { get; init; }
    public int InvitedCount { get; init; }
    public int PurchasedCount { get; init; }
    public int PointsEarned { get; init; }
    public int RewardPointsPerFriend { get; init; }
    public int SignupBonusPoints { get; init; }
    public string? RewardDescription { get; init; }
    public IReadOnlyList<ReferredFriend> Friends { get; init; } = [];
}

/// <summary>An invited friend – name masked, the phone number is never shown.</summary>
public sealed record ReferredFriend(string DisplayName, DateTimeOffset JoinedAt, bool HasPurchased);
