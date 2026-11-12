using System.Security.Cryptography;
using Fratak_Shop.Core.DTOs.Loyalty;
using Fratak_Shop.Core.DTOs.Notifications;
using Fratak_Shop.Core.DTOs.Wallets;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Wallets;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Loyalty;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Loyalty;

/// <summary>
/// Customer club: points for actions (rules from the database), levels with point
/// multipliers, rewards (wallet credit / personal coupons), referral codes and
/// expiry. Points live in an append-only ledger; the balance on the user row is
/// changed with atomic conditional updates (never negative, never awarded twice).
/// </summary>
public interface ILoyaltyService
{
    Task<LoyaltyProgramDto> GetProgramAsync();

    Task<LoyaltySummaryDto> GetSummaryAsync(Guid userId, int transactionCount = 20);

    /// <summary>Awards the points of an action once per <paramref name="referenceKey"/>. Returns the points (0 = nothing).</summary>
    Task<int> AwardAsync(Guid userId, LoyaltyAction action, string referenceKey, decimal amount = 0, Guid? orderId = null, string? description = null);

    /// <summary>Paid (online / wallet) or delivered (cash on delivery) order: purchase, first-order and referral points.</summary>
    Task OnOrderSettledAsync(Guid orderId);

    /// <summary>Cancelled / returned order: takes back the points earned with it.</summary>
    Task RevertOrderAsync(Guid orderId);

    Task<OperationResult> RedeemAsync(Guid userId, Guid rewardId);

    /// <summary>Links a new customer to the owner of a referral code and gives the sign-up gift.</summary>
    Task ApplyReferralAsync(Guid newUserId, string? referralCode);

    Task<string?> EnsureReferralCodeAsync(Guid userId);

    /// <summary>Invite-friends statistics of a customer.</summary>
    Task<DTOs.Engagement.ReferralStatsDto> GetReferralStatsAsync(Guid userId);

    /// <summary>Validates a referral code (exists and belongs to someone else than <paramref name="userId"/>).</summary>
    Task<bool> IsValidReferralCodeAsync(string? code, Guid? userId = null);

    /// <summary>Expires points older than the configured lifetime (FIFO). Returns the number of affected customers.</summary>
    Task<int> ExpirePointsAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Birthday gift for customers whose (Persian calendar) birthday is today. Returns the number of awarded customers.</summary>
    Task<int> AwardBirthdaysAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

public class LoyaltyService(
    FratakShopDbContext context,
    IStoreSettingService settingService,
    IWalletService walletService,
    ICouponService couponService,
    INotificationService notifications,
    IMemoryCache cache) : ILoyaltyService
{
    public async Task<LoyaltyProgramDto> GetProgramAsync()
    {
        return await cache.GetOrCreateAsync(CacheKeys.LoyaltyProgram, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheKeys.DefaultLifetime;

            var tiers = await context.LoyaltyTiers
                .Where(t => t.IsActive)
                .OrderBy(t => t.MinLifetimePoints)
                .Select(t => new LoyaltyTierDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    MinLifetimePoints = t.MinLifetimePoints,
                    PointsMultiplier = t.PointsMultiplier,
                    Color = t.Color,
                    Benefits = t.Benefits,
                })
                .ToListAsync();

            return new LoyaltyProgramDto
            {
                Tiers = tiers,
                Rules = await context.LoyaltyRules
                    .Where(r => r.IsActive)
                    .OrderBy(r => r.DisplayOrder)
                    .Select(r => new LoyaltyRuleDto
                    {
                        Id = r.Id,
                        Action = r.Action,
                        Title = r.Title,
                        Description = r.Description,
                        Points = r.Points,
                        PointsPerStep = r.PointsPerStep,
                        AmountStep = r.AmountStep,
                        MinOrderAmount = r.MinOrderAmount,
                        MaxPointsPerEvent = r.MaxPointsPerEvent,
                        MaxTimesPerUser = r.MaxTimesPerUser,
                    })
                    .ToListAsync(),
                Rewards = await context.LoyaltyRewards
                    .Where(r => r.IsActive)
                    .OrderBy(r => r.DisplayOrder).ThenBy(r => r.PointsCost)
                    .Select(r => new LoyaltyRewardDto
                    {
                        Id = r.Id,
                        Title = r.Title,
                        Description = r.Description,
                        Type = r.Type,
                        PointsCost = r.PointsCost,
                        Value = r.Value,
                        MaxDiscountAmount = r.MaxDiscountAmount,
                        MinOrderAmount = r.MinOrderAmount,
                        CouponValidDays = r.CouponValidDays,
                        MinTierId = r.MinTierId,
                        MinTierName = r.MinTier != null ? r.MinTier.Name : null,
                        MinTierPoints = r.MinTier != null ? r.MinTier.MinLifetimePoints : 0,
                    })
                    .ToListAsync(),
            };
        }) ?? new LoyaltyProgramDto();
    }

    public async Task<LoyaltySummaryDto> GetSummaryAsync(Guid userId, int transactionCount = 20)
    {
        var program = await GetProgramAsync();
        var settings = await settingService.GetAsync();

        var user = await context.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.LoyaltyPoints, u.LoyaltyLifetimePoints, u.ReferralCode })
            .FirstOrDefaultAsync();

        if (user is null)
            return new LoyaltySummaryDto { Program = program };

        var tier = program.TierFor(user.LoyaltyLifetimePoints);
        var next = program.NextTier(user.LoyaltyLifetimePoints);
        var start = tier?.MinLifetimePoints ?? 0;
        var progress = next is null
            ? 100
            : (int)Math.Clamp((user.LoyaltyLifetimePoints - start) * 100.0 / Math.Max(1, next.MinLifetimePoints - start), 0, 100);

        var referralCode = user.ReferralCode;
        if (settings.IsReferralEnabled && referralCode is null)
            referralCode = await EnsureReferralCodeAsync(userId);

        var now = DateTimeOffset.UtcNow;
        var soon = now.AddDays(30);
        var expiringEarned = await context.LoyaltyTransactions
            .Where(t => t.UserId == userId && t.Type == LoyaltyTransactionType.Earn && t.ExpiresAt != null && t.ExpiresAt <= soon)
            .SumAsync(t => (int?)t.Points) ?? 0;
        var debits = await context.LoyaltyTransactions
            .Where(t => t.UserId == userId && t.Points < 0)
            .SumAsync(t => (int?)-t.Points) ?? 0;

        return new LoyaltySummaryDto
        {
            Points = user.LoyaltyPoints,
            LifetimePoints = user.LoyaltyLifetimePoints,
            Tier = tier,
            NextTier = next,
            ProgressPercent = progress,
            ReferralCode = settings.IsReferralEnabled ? referralCode : null,
            ReferralCount = await context.Users.CountAsync(u => u.ReferredByUserId == userId),
            ExpiringSoon = Math.Min(user.LoyaltyPoints, Math.Max(0, expiringEarned - debits)),
            Program = program,
            Coupons = (await couponService.GetAvailableCouponsAsync(userId)).Where(c => c.IsPersonal).ToList(),
            Transactions = await context.LoyaltyTransactions
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.CreatedAt)
                .Take(Math.Clamp(transactionCount, 1, 100))
                .Select(t => new LoyaltyTransactionDto
                {
                    Id = t.Id,
                    Type = t.Type,
                    Action = t.Action,
                    Points = t.Points,
                    BalanceAfter = t.BalanceAfter,
                    Description = t.Description,
                    CreatedAt = t.CreatedAt,
                    ExpiresAt = t.ExpiresAt,
                })
                .ToListAsync(),
        };
    }

    public async Task<int> AwardAsync(Guid userId, LoyaltyAction action, string referenceKey, decimal amount = 0,
        Guid? orderId = null, string? description = null)
    {
        var settings = await settingService.GetAsync();
        if (!settings.IsLoyaltyEnabled)
            return 0;

        var program = await GetProgramAsync();
        var rules = program.Rules.Where(r => r.Action == action).ToList();
        if (rules.Count == 0)
            return 0;

        if (await context.LoyaltyTransactions.AnyAsync(t => t.UserId == userId && t.ReferenceKey == referenceKey))
            return 0;

        var user = await context.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.LoyaltyLifetimePoints })
            .FirstOrDefaultAsync();
        if (user is null)
            return 0;

        // Net number of times this action was rewarded (reverted rewards can be earned again).
        var earned = await context.LoyaltyTransactions.CountAsync(t => t.UserId == userId && t.Action == action && t.Type == LoyaltyTransactionType.Earn);
        var reverted = await context.LoyaltyTransactions.CountAsync(t => t.UserId == userId && t.Action == action && t.Type == LoyaltyTransactionType.Revert);
        var times = earned - reverted;

        var points = 0;
        foreach (var rule in rules)
        {
            if (rule.MaxTimesPerUser.HasValue && times >= rule.MaxTimesPerUser)
                continue;

            if (rule.MinOrderAmount > 0 && amount < rule.MinOrderAmount)
                continue;

            var rulePoints = rule.Points;
            if (rule.AmountStep > 0 && rule.PointsPerStep > 0)
                rulePoints += (int)Math.Min(int.MaxValue / 2, decimal.Floor(amount / rule.AmountStep) * rule.PointsPerStep);

            if (rule.MaxPointsPerEvent.HasValue)
                rulePoints = Math.Min(rulePoints, rule.MaxPointsPerEvent.Value);

            points += Math.Max(0, rulePoints);
        }

        // Club levels multiply purchase points.
        var tier = program.TierFor(user.LoyaltyLifetimePoints);
        if (action == LoyaltyAction.Purchase && tier is { PointsMultiplier: > 1 })
            points = (int)decimal.Floor(points * tier.PointsMultiplier);

        // دور ۲۴: کمپین «امتیاز n برابر» (از بنر هوشمند) فقط برای بخش مشمول سفارش.
        if (action == LoyaltyAction.Purchase && orderId is { } campaignOrderId)
            points = await Fratak_Shop.Core.Services.Marketing.CampaignOffers.BoostPurchasePointsAsync(context, campaignOrderId, points);

        if (points <= 0)
            return 0;

        var title = description ?? rules[0].Title;
        var expiresAt = settings.LoyaltyPointsExpireDays is > 0 ? DateTimeOffset.UtcNow.AddDays(settings.LoyaltyPointsExpireDays.Value) : (DateTimeOffset?)null;

        var balance = await ApplyAsync(userId, points, points, LoyaltyTransactionType.Earn, action, referenceKey, orderId, title, expiresAt);
        if (balance is null)
            return 0;

        await notifications.NotifyAsync(new NotificationRequest
        {
            UserId = userId,
            Event = NotificationEvent.PointsEarned,
            Category = NotificationCategory.Loyalty,
            LinkUrl = "/club",
            Values =
            {
                ["Points"] = points.ToString("N0"),
                ["Balance"] = balance.Value.ToString("N0"),
                ["Description"] = title,
            },
        });

        var newTier = program.TierFor(user.LoyaltyLifetimePoints + points);
        if (newTier is not null && tier is not null && newTier.Id != tier.Id && newTier.MinLifetimePoints > tier.MinLifetimePoints)
        {
            await notifications.NotifyAsync(new NotificationRequest
            {
                UserId = userId,
                Event = NotificationEvent.TierUpgraded,
                Category = NotificationCategory.Loyalty,
                LinkUrl = "/club",
                Values = { ["Tier"] = newTier.Name },
            });
        }

        return points;
    }

    public async Task OnOrderSettledAsync(Guid orderId)
    {
        var order = await context.Orders
            .Where(o => o.Id == orderId)
            .Select(o => new { o.Id, o.UserId, o.OrderNumber, o.Status, o.PayableAmount, o.ShippingAmount, o.CreatedAt })
            .FirstOrDefaultAsync();

        // Points are earned only for delivered orders: awarding them at payment would let a
        // customer redeem the points (e.g. as wallet credit) and then cancel the order.
        if (order?.UserId is null || order.Status != OrderStatus.Delivered)
            return;

        var userId = order.UserId.Value;
        var purchaseAmount = Math.Max(0, order.PayableAmount - order.ShippingAmount);

        await AwardAsync(userId, LoyaltyAction.Purchase, $"order:{order.Id:N}", purchaseAmount, order.Id, $"خرید سفارش {order.OrderNumber}");

        // First settled order of the customer (others still open don't count).
        var otherSettled = await context.Orders.AnyAsync(o => o.UserId == userId && o.Id != order.Id
            && o.Status == OrderStatus.Delivered
            && o.CreatedAt < order.CreatedAt);

        if (otherSettled)
            return;

        await AwardAsync(userId, LoyaltyAction.FirstOrder, $"first-order:{order.Id:N}", purchaseAmount, order.Id, "اولین خرید");

        var settings = await settingService.GetAsync();
        if (!settings.IsReferralEnabled)
            return;

        var referrerId = await context.Users.Where(u => u.Id == userId).Select(u => u.ReferredByUserId).FirstOrDefaultAsync();
        if (referrerId.HasValue && referrerId != userId)
            await AwardAsync(referrerId.Value, LoyaltyAction.Referral, $"referral:{userId:N}", purchaseAmount, order.Id, "اولین خرید دوست معرفی‌شده");
    }

    public async Task RevertOrderAsync(Guid orderId)
    {
        var earnings = await context.LoyaltyTransactions
            .Where(t => t.OrderId == orderId && t.Type == LoyaltyTransactionType.Earn)
            .Select(t => new { t.Id, t.UserId, t.Points, t.Action, t.Description })
            .ToListAsync();

        foreach (var earning in earnings)
        {
            var balance = await context.Users.Where(u => u.Id == earning.UserId).Select(u => u.LoyaltyPoints).FirstOrDefaultAsync();
            var take = Math.Min(earning.Points, balance);

            // Lifetime points always go back (levels can't be farmed with cancelled orders);
            // the spendable balance only as far as it is still available.
            if (take > 0)
            {
                await ApplyAsync(earning.UserId, -take, -earning.Points, LoyaltyTransactionType.Revert, earning.Action,
                    $"revert:{earning.Id:N}", orderId, $"برگشت امتیاز «{earning.Description}» (لغو سفارش)", null);
            }
            else
            {
                await context.Users.Where(u => u.Id == earning.UserId).ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.LoyaltyLifetimePoints, u => u.LoyaltyLifetimePoints >= earning.Points ? u.LoyaltyLifetimePoints - earning.Points : 0));
            }
        }
    }

    public async Task<OperationResult> RedeemAsync(Guid userId, Guid rewardId)
    {
        var settings = await settingService.GetAsync();
        if (!settings.IsLoyaltyEnabled)
            return OperationResult.Error("باشگاه مشتریان در حال حاضر غیرفعال است.");

        var program = await GetProgramAsync();
        var reward = program.Rewards.FirstOrDefault(r => r.Id == rewardId);
        if (reward is null)
            return OperationResult.NotFound("جایزه مورد نظر یافت نشد.");

        var user = await context.Users.Where(u => u.Id == userId)
            .Select(u => new { u.LoyaltyPoints, u.LoyaltyLifetimePoints }).FirstOrDefaultAsync();
        if (user is null)
            return OperationResult.NotFound("کاربر یافت نشد.");

        if (reward.MinTierId.HasValue && user.LoyaltyLifetimePoints < reward.MinTierPoints)
            return OperationResult.Error($"این جایزه ویژه سطح «{reward.MinTierName}» و بالاتر است.");

        if (user.LoyaltyPoints < reward.PointsCost)
            return OperationResult.Error($"برای این جایزه {reward.PointsCost:N0} امتیاز لازم است.");

        if (reward.Type == LoyaltyRewardType.WalletCredit && !settings.IsWalletEnabled)
            return OperationResult.Error("کیف پول در حال حاضر غیرفعال است.");

        await using var transaction = await context.Database.BeginTransactionAsync();

        var balance = await ApplyAsync(userId, -reward.PointsCost, 0, LoyaltyTransactionType.Redeem, null, null, null,
            $"دریافت جایزه «{reward.Title}»", null);

        if (balance is null)
        {
            await transaction.RollbackAsync();
            return OperationResult.Error("امتیاز کافی نیست.");
        }

        string detail;
        if (reward.Type == LoyaltyRewardType.WalletCredit)
        {
            var credit = await walletService.CreditAsync(new WalletEntry
            {
                UserId = userId,
                Amount = decimal.Floor(reward.Value),
                Type = WalletTransactionType.LoyaltyReward,
                Description = $"جایزه باشگاه مشتریان: {reward.Title}",
                // Club credit is for purchases only – it can never be withdrawn.
                NonWithdrawableAmount = decimal.Floor(reward.Value),
            });

            if (!credit.IsSuccess())
            {
                await transaction.RollbackAsync();
                context.ChangeTracker.Clear();
                return OperationResult.Error(credit.Message);
            }

            detail = $"{reward.Value:N0} تومان اعتبار خرید به کیف پول شما اضافه شد (فقط برای خرید قابل استفاده است).";
        }
        else
        {
            var type = reward.Type switch
            {
                LoyaltyRewardType.PercentageCoupon => CouponType.Percentage,
                LoyaltyRewardType.FixedCoupon => CouponType.FixedAmount,
                _ => CouponType.FreeShipping,
            };

            var coupon = await couponService.CreatePersonalCouponAsync(userId, reward.Title, type, reward.Value,
                reward.MaxDiscountAmount, reward.MinOrderAmount, reward.CouponValidDays);

            detail = $"کد تخفیف شما: {coupon.Code} (اعتبار تا {reward.CouponValidDays} روز)";
        }

        await transaction.CommitAsync();

        await notifications.NotifyAsync(new NotificationRequest
        {
            UserId = userId,
            Event = NotificationEvent.RewardRedeemed,
            Category = NotificationCategory.Loyalty,
            LinkUrl = "/club",
            Values =
            {
                ["Reward"] = reward.Title,
                ["Points"] = reward.PointsCost.ToString("N0"),
                ["Description"] = detail,
            },
        });

        return OperationResult.Success($"جایزه «{reward.Title}» دریافت شد. {detail}");
    }

    public async Task ApplyReferralAsync(Guid newUserId, string? referralCode)
    {
        var settings = await settingService.GetAsync();
        if (!settings.IsReferralEnabled || string.IsNullOrWhiteSpace(referralCode))
            return;

        var code = referralCode.Trim().ToUpperInvariant();
        if (code.Length > 20)
            return;

        var referrerId = await context.Users
            .Where(u => u.ReferralCode == code && u.Id != newUserId)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync();

        if (referrerId is null)
            return;

        var linked = await context.Users
            .Where(u => u.Id == newUserId && u.ReferredByUserId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.ReferredByUserId, referrerId));

        if (linked == 1)
        {
            await AwardAsync(newUserId, LoyaltyAction.ReferralSignup, "referral-signup", description: "ثبت‌نام با کد معرف");

            var friendName = await context.Users.Where(u => u.Id == newUserId).Select(u => u.FirstName).FirstOrDefaultAsync();
            await notifications.NotifyAsync(new NotificationRequest
            {
                UserId = referrerId.Value,
                Event = NotificationEvent.ReferralJoined,
                Category = NotificationCategory.Loyalty,
                LinkUrl = "/profile/invite",
                Values = { ["FriendName"] = string.IsNullOrWhiteSpace(friendName) ? "یکی از دوستانتان" : friendName },
            });
        }
    }

    public async Task<bool> IsValidReferralCodeAsync(string? code, Guid? userId = null)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 20)
            return false;

        var normalized = code.Trim().ToUpperInvariant();
        return await context.Users.AnyAsync(u => u.ReferralCode == normalized && (userId == null || u.Id != userId));
    }

    public async Task<DTOs.Engagement.ReferralStatsDto> GetReferralStatsAsync(Guid userId)
    {
        var settings = await settingService.GetAsync();
        var code = settings.IsReferralEnabled ? await EnsureReferralCodeAsync(userId) : null;

        var friends = await context.Users
            .Where(u => u.ReferredByUserId == userId)
            .OrderByDescending(u => u.CreatedAt)
            .Take(100)
            .Select(u => new
            {
                u.FirstName,
                u.LastName,
                u.CreatedAt,
                Purchased = context.Orders.Any(o => o.UserId == u.Id && o.Status == Data.Entities.Orders.OrderStatus.Delivered),
            })
            .ToListAsync();

        var invited = await context.Users.CountAsync(u => u.ReferredByUserId == userId);
        var earned = await context.LoyaltyTransactions
            .Where(t => t.UserId == userId && t.Action == LoyaltyAction.Referral)
            .SumAsync(t => (int?)t.Points) ?? 0;

        var program = await GetProgramAsync();
        int RuleOf(LoyaltyAction action) => program.Rules.FirstOrDefault(r => r.Action == action)?.Points ?? 0;

        return new DTOs.Engagement.ReferralStatsDto
        {
            Code = code,
            InvitedCount = invited,
            PurchasedCount = friends.Count(f => f.Purchased),
            PointsEarned = earned,
            RewardPointsPerFriend = RuleOf(LoyaltyAction.Referral),
            SignupBonusPoints = RuleOf(LoyaltyAction.ReferralSignup),
            RewardDescription = program.Rules.FirstOrDefault(r => r.Action == LoyaltyAction.Referral) is { } rule
                ? rule.Description ?? rule.Title
                : null,
            Friends = friends.Select(f => new DTOs.Engagement.ReferredFriend(MaskName(f.FirstName, f.LastName), f.CreatedAt, f.Purchased)).ToList(),
        };
    }

    /// <summary>"علی م." – enough to recognise a friend, nothing more.</summary>
    private static string MaskName(string? first, string? last)
    {
        if (string.IsNullOrWhiteSpace(first))
            return "کاربر جدید";
        return string.IsNullOrWhiteSpace(last) ? first.Trim() : $"{first.Trim()} {last.Trim()[0]}.";
    }

    public async Task<string?> EnsureReferralCodeAsync(Guid userId)
    {
        var existing = await context.Users.Where(u => u.Id == userId).Select(u => new { u.ReferralCode }).FirstOrDefaultAsync();
        if (existing is null)
            return null;

        if (existing.ReferralCode is not null)
            return existing.ReferralCode;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = "FR" + RandomCode(6);
            if (await context.Users.IgnoreQueryFilters().AnyAsync(u => u.ReferralCode == code))
                continue;

            try
            {
                var updated = await context.Users
                    .Where(u => u.Id == userId && u.ReferralCode == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.ReferralCode, code));

                return updated == 1
                    ? code
                    : await context.Users.Where(u => u.Id == userId).Select(u => u.ReferralCode).FirstOrDefaultAsync();
            }
            catch (DbUpdateException)
            {
                // Same code generated in parallel – try another one.
            }
        }

        return null;
    }

    public async Task<int> AwardBirthdaysAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetAsync();
        if (!settings.IsLoyaltyEnabled)
            return 0;

        var program = await GetProgramAsync();
        if (program.Rules.All(r => r.Action != LoyaltyAction.Birthday))
            return 0;

        var persian = new System.Globalization.PersianCalendar();
        var today = IranClock.Today(now).ToDateTime(TimeOnly.MinValue);
        var (month, day, year) = (persian.GetMonth(today), persian.GetDayOfMonth(today), persian.GetYear(today));

        // Gregorian and Persian anniversaries differ by at most a day: narrow down in SQL, decide in memory.
        var windows = new[] { today.AddDays(-1), today, today.AddDays(1) }.Select(d => d.Month * 100 + d.Day).ToList();
        if (windows.Contains(228) || windows.Contains(301))
            windows.Add(229);

        var candidates = await context.Users
            .Where(u => u.BirthDate != null && windows.Contains(u.BirthDate.Value.Month * 100 + u.BirthDate.Value.Day))
            .Select(u => new { u.Id, BirthDate = u.BirthDate!.Value })
            .ToListAsync(cancellationToken);

        var awarded = 0;
        foreach (var candidate in candidates)
        {
            var birth = candidate.BirthDate.ToDateTime(TimeOnly.MinValue);
            var (birthMonth, birthDay) = (persian.GetMonth(birth), persian.GetDayOfMonth(birth));

            // Born on 30 Esfand: celebrated on the last day of Esfand in common years.
            var isToday = birthMonth == month && (birthDay == day
                || (birthMonth == 12 && birthDay == 30 && day == 29 && !persian.IsLeapYear(year)));
            if (!isToday)
                continue;

            if (await AwardAsync(candidate.Id, LoyaltyAction.Birthday, $"birthday:{year}", description: "هدیه تولد") > 0)
                awarded++;
        }

        return awarded;
    }

    public async Task<int> ExpirePointsAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetAsync();
        if (!settings.IsLoyaltyEnabled)
            return 0;

        var userIds = await context.LoyaltyTransactions
            .Where(t => t.Type == LoyaltyTransactionType.Earn && t.ExpiresAt != null && t.ExpiresAt <= now && t.User.LoyaltyPoints > 0)
            .Select(t => t.UserId)
            .Distinct()
            .Take(200)
            .ToListAsync(cancellationToken);

        var count = 0;
        foreach (var userId in userIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // FIFO: spent / reverted / expired points always consume the oldest points first,
            // so whatever of the expired earnings exceeds all debits so far is still unspent.
            var expiredEarned = await context.LoyaltyTransactions
                .Where(t => t.UserId == userId && t.Type == LoyaltyTransactionType.Earn && t.ExpiresAt != null && t.ExpiresAt <= now)
                .SumAsync(t => (int?)t.Points, cancellationToken) ?? 0;
            var debits = await context.LoyaltyTransactions
                .Where(t => t.UserId == userId && t.Points < 0)
                .SumAsync(t => (int?)-t.Points, cancellationToken) ?? 0;
            var balance = await context.Users.Where(u => u.Id == userId).Select(u => u.LoyaltyPoints).FirstAsync(cancellationToken);

            var expire = Math.Min(balance, expiredEarned - debits);
            if (expire <= 0)
                continue;

            var result = await ApplyAsync(userId, -expire, 0, LoyaltyTransactionType.Expire, null,
                $"expire:{now:yyyyMMddHHmm}", null, "انقضای امتیازهای قدیمی", null);

            if (result is not null)
                count++;
        }

        return count;
    }

    /// <summary>
    /// Changes the balance atomically (never below zero) and writes the ledger row.
    /// Returns the new balance, or null when the change was not possible / already done.
    /// </summary>
    private async Task<int?> ApplyAsync(Guid userId, int delta, int lifetimeDelta, LoyaltyTransactionType type,
        LoyaltyAction? action, string? referenceKey, Guid? orderId, string? description, DateTimeOffset? expiresAt)
    {
        if (delta == 0)
            return null;

        var ambient = context.Database.CurrentTransaction;
        await using var own = ambient is null ? await context.Database.BeginTransactionAsync() : null;
        const string savepoint = "loyalty_points";
        if (ambient is not null)
            await ambient.CreateSavepointAsync(savepoint);

        try
        {
            var now = DateTimeOffset.UtcNow;
            var affected = await context.Users
                .Where(u => u.Id == userId && u.LoyaltyPoints + delta >= 0)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.LoyaltyPoints, u => u.LoyaltyPoints + delta)
                    .SetProperty(u => u.LoyaltyLifetimePoints, u => u.LoyaltyLifetimePoints + lifetimeDelta >= 0 ? u.LoyaltyLifetimePoints + lifetimeDelta : 0)
                    .SetProperty(u => u.UpdatedAt, now));

            if (affected == 0)
            {
                await RollbackAsync(own, ambient, savepoint);
                return null;
            }

            var balance = await context.Users.Where(u => u.Id == userId).Select(u => u.LoyaltyPoints).FirstAsync();

            context.LoyaltyTransactions.Add(new LoyaltyTransaction
            {
                UserId = userId,
                Type = type,
                Action = action,
                Points = delta,
                BalanceAfter = balance,
                Description = description is { Length: > 500 } ? description[..500] : description,
                ReferenceKey = referenceKey,
                OrderId = orderId,
                ExpiresAt = expiresAt,
            });

            await context.SaveChangesAsync();

            if (own is not null)
                await own.CommitAsync();

            return balance;
        }
        catch (DbUpdateException)
        {
            // Unique reference: the same event was rewarded by a parallel request.
            context.ChangeTracker.Clear();
            await RollbackAsync(own, ambient, savepoint);
            return null;
        }
    }

    private static async Task RollbackAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? own,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? ambient, string savepoint)
    {
        if (own is not null)
            await own.RollbackAsync();
        else if (ambient is not null)
            await ambient.RollbackToSavepointAsync(savepoint);
    }

    private static string RandomCode(int length)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return string.Create(length, alphabet, (span, chars) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
        });
    }
}
