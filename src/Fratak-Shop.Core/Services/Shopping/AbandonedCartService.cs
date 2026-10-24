using Fratak_Shop.Core.DTOs.Notifications;
using Fratak_Shop.Core.DTOs.Shopping.Query;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Shopping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Shopping;

/// <summary>
/// Abandoned carts of signed-in customers: detection after the configured idle time,
/// the configurable reminder sequence (optionally with a personal discount code),
/// recovery when the customer orders, and the figures for the reports.
/// Everything is driven by StoreSetting (IsAbandonedCartEnabled, AbandonedCartAfterMinutes,
/// AbandonedCartMinAmount, AbandonedCartExpireDays) and AbandonedCartReminderStep rows.
/// </summary>
public interface IAbandonedCartService
{
    /// <summary>Background job: detect, refresh, remind and expire. Never throws for a single cart.</summary>
    Task<AbandonedCartRunResult> ProcessAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>The customer placed an order: open records become "recovered".</summary>
    Task MarkRecoveredAsync(Guid userId, Guid orderId, decimal orderAmount);

    Task<AbandonedCartStatsDto> GetStatsAsync(DateTimeOffset? from = null, DateTimeOffset? to = null);

    void InvalidateCache();
}

public class AbandonedCartService(
    FratakShopDbContext context,
    IStoreSettingService settingService,
    ICouponService couponService,
    INotificationService notifications,
    IMemoryCache cache,
    ILogger<AbandonedCartService> logger) : IAbandonedCartService
{
    private const int BatchSize = 200;

    /// <summary>Two reminders are never sent closer than this, even after a long pause of the job.</summary>
    private static readonly TimeSpan MinGapBetweenReminders = TimeSpan.FromMinutes(30);

    public async Task<AbandonedCartRunResult> ProcessAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetAsync(cancellationToken);
        if (!settings.IsAbandonedCartEnabled)
            return new AbandonedCartRunResult(0, 0, 0);

        var idle = TimeSpan.FromMinutes(Math.Clamp(settings.AbandonedCartAfterMinutes, 5, 60 * 24 * 7));
        var lifetime = TimeSpan.FromDays(Math.Clamp(settings.AbandonedCartExpireDays, 1, 90));

        var detected = await DetectAsync(now, idle, lifetime, settings.AbandonedCartMinAmount, cancellationToken);
        var (sent, closed) = await FollowUpAsync(now, lifetime, settings.AbandonedCartMinAmount, cancellationToken);

        return new AbandonedCartRunResult(detected, sent, closed);
    }

    private async Task<int> DetectAsync(DateTimeOffset now, TimeSpan idle, TimeSpan lifetime, decimal minAmount, CancellationToken cancellationToken)
    {
        var idleSince = now - idle;
        var notOlderThan = now - lifetime;

        // Idle carts of customers with items, not already followed for this activity.
        var candidates = await context.Carts
            .Where(c => c.UserId != null
                        && c.LastActivityAt != null
                        && c.LastActivityAt <= idleSince
                        && c.LastActivityAt >= notOlderThan
                        && c.Items.Any()
                        && !context.AbandonedCarts.Any(a => a.CartId == c.Id
                                                            && (a.Status == AbandonedCartStatus.Open || a.AbandonedAt >= c.LastActivityAt)))
            .OrderBy(c => c.LastActivityAt)
            .Select(c => new { c.Id, UserId = c.UserId!.Value, LastActivityAt = c.LastActivityAt!.Value })
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            return 0;

        var totals = await CartTotalsAsync(candidates.Select(c => c.Id).ToList(), now, cancellationToken);
        var count = 0;
        foreach (var cart in candidates)
        {
            if (!totals.TryGetValue(cart.Id, out var total) || total.Count == 0 || total.Amount < minAmount)
                continue;

            context.AbandonedCarts.Add(new AbandonedCart
            {
                CartId = cart.Id,
                UserId = cart.UserId,
                AbandonedAt = cart.LastActivityAt,
                CartAmount = total.Amount,
                ItemCount = total.Count,
            });
            count++;
        }

        if (count > 0)
            await context.SaveChangesAsync(cancellationToken);

        return count;
    }

    private async Task<(int Sent, int Closed)> FollowUpAsync(DateTimeOffset now, TimeSpan lifetime, decimal minAmount, CancellationToken cancellationToken)
    {
        var records = await context.AbandonedCarts
            .AsTracking()
            .Include(a => a.Cart)
            .Where(a => a.Status == AbandonedCartStatus.Open)
            .OrderBy(a => a.AbandonedAt)
            .Take(BatchSize * 2)
            .ToListAsync(cancellationToken);

        if (records.Count == 0)
            return (0, 0);

        var steps = await GetStepsAsync();
        var totals = await CartTotalsAsync(records.Select(r => r.CartId).Distinct().ToList(), now, cancellationToken);
        int sent = 0, closed = 0;

        foreach (var record in records)
        {
            totals.TryGetValue(record.CartId, out var total);

            // Emptied (also after a checkout) or the cart moved to another owner.
            if (total.Count == 0 || record.Cart.UserId != record.UserId)
            {
                Close(record, AbandonedCartStatus.Cleared, now);
                closed++;
                continue;
            }

            // The customer came back and changed the cart: the reminders continue from the new moment.
            if (record.Cart.LastActivityAt is { } activity && activity > record.AbandonedAt)
                record.AbandonedAt = activity;

            record.CartAmount = total.Amount;
            record.ItemCount = total.Count;

            if (now - record.AbandonedAt > lifetime)
            {
                Close(record, AbandonedCartStatus.Expired, now);
                closed++;
                continue;
            }

            if (record.CartAmount < minAmount || record.RemindersSent >= steps.Count)
                continue;

            var step = steps[record.RemindersSent];
            if (now < record.AbandonedAt.AddMinutes(step.DelayMinutes))
                continue;
            if (record.LastReminderAt is { } last && now - last < MinGapBetweenReminders)
                continue;

            if (await SendReminderAsync(record, step))
                sent++;
        }

        await context.SaveChangesAsync(cancellationToken);
        return (sent, closed);
    }

    private async Task<bool> SendReminderAsync(AbandonedCart record, ReminderStepData step)
    {
        try
        {
            string? couponValue = null;
            if (step.IncludeCoupon && record.CouponId is null)
            {
                var coupon = await couponService.CreatePersonalCouponAsync(record.UserId, step.Title.ToPlainText(200),
                    step.CouponType, step.CouponValue, step.CouponMaxDiscountAmount, step.CouponMinOrderAmount,
                    step.CouponValidDays, "CART");
                record.CouponId = coupon.Id;
                record.CouponCode = coupon.Code;
                couponValue = coupon.ValueText;
            }

            var request = new NotificationRequest
            {
                UserId = record.UserId,
                Event = NotificationEvent.AbandonedCartReminder,
                Category = NotificationCategory.Promotion,
                LinkUrl = "/cart",
                Content = new NotificationContent
                {
                    Title = step.Title,
                    Body = step.Message,
                    LinkText = "مشاهده سبد خرید",
                    InSite = step.Channel.HasFlag(ReminderChannel.Notification),
                    SmsText = step.Channel.HasFlag(ReminderChannel.Sms) ? step.SmsText : null,
                },
            };
            request.Values["ItemCount"] = record.ItemCount.ToString("N0");
            request.Values["CartAmount"] = $"{record.CartAmount:N0} تومان";
            request.Values["CouponCode"] = record.CouponCode ?? string.Empty;
            request.Values["CouponValue"] = couponValue ?? string.Empty;

            await notifications.NotifyAsync(request);

            record.RemindersSent++;
            record.LastReminderAt = DateTimeOffset.UtcNow;
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Abandoned cart reminder {Step} for cart {CartId} failed", step.StepNumber, record.CartId);
            return false;
        }
    }

    public async Task MarkRecoveredAsync(Guid userId, Guid orderId, decimal orderAmount)
    {
        var now = DateTimeOffset.UtcNow;
        await context.AbandonedCarts
            .Where(a => a.UserId == userId && a.Status == AbandonedCartStatus.Open)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, AbandonedCartStatus.Recovered)
                .SetProperty(a => a.RecoveredOrderId, orderId)
                .SetProperty(a => a.RecoveredAmount, Math.Max(0, orderAmount))
                .SetProperty(a => a.ClosedAt, now)
                .SetProperty(a => a.UpdatedAt, now));
    }

    public async Task<AbandonedCartStatsDto> GetStatsAsync(DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        var query = context.AbandonedCarts.AsQueryable();
        if (from.HasValue)
            query = query.Where(a => a.AbandonedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(a => a.AbandonedAt < to.Value);

        var groups = await query
            .GroupBy(a => a.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count(),
                Amount = g.Sum(a => a.CartAmount),
                Recovered = g.Sum(a => a.RecoveredAmount),
                Reminders = g.Sum(a => a.RemindersSent),
                Coupons = g.Count(a => a.CouponId != null),
            })
            .ToListAsync();

        int CountOf(AbandonedCartStatus status) => groups.Where(g => g.Status == status).Sum(g => g.Count);
        decimal AmountOf(AbandonedCartStatus status) => groups.Where(g => g.Status == status).Sum(g => g.Amount);

        var stats = new AbandonedCartStatsDto
        {
            From = from,
            To = to,
            TotalCount = groups.Sum(g => g.Count),
            TotalAmount = groups.Sum(g => g.Amount),
            OpenCount = CountOf(AbandonedCartStatus.Open),
            OpenAmount = AmountOf(AbandonedCartStatus.Open),
            RecoveredCount = CountOf(AbandonedCartStatus.Recovered),
            RecoveredAmount = groups.Sum(g => g.Recovered),
            ExpiredCount = CountOf(AbandonedCartStatus.Expired),
            LostAmount = AmountOf(AbandonedCartStatus.Expired),
            ClearedCount = CountOf(AbandonedCartStatus.Cleared),
            RemindersSent = groups.Sum(g => g.Reminders),
            CouponsIssued = groups.Sum(g => g.Coupons),
        };

        var closedCount = stats.TotalCount - stats.OpenCount;
        stats.RecoveryRate = closedCount == 0 ? 0 : Math.Round(stats.RecoveredCount * 100m / closedCount, 1);
        return stats;
    }

    public void InvalidateCache() => cache.Remove(CacheKeys.AbandonedCartSteps);

    private static void Close(AbandonedCart record, AbandonedCartStatus status, DateTimeOffset now)
    {
        record.Status = status;
        record.ClosedAt = now;
    }

    /// <summary>Items count and final-price amount of the carts (active products only).</summary>
    private async Task<Dictionary<Guid, (int Count, decimal Amount)>> CartTotalsAsync(List<Guid> cartIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var items = await context.CartItems
            .Where(i => cartIds.Contains(i.CartId) && i.Product.IsActive)
            .Select(i => new { i.CartId, i.Quantity, i.Product })
            .ToListAsync(cancellationToken);

        return items
            .GroupBy(i => i.CartId)
            .ToDictionary(
                g => g.Key,
                g => (g.Sum(i => i.Quantity), g.Sum(i => i.Product.GetFinalPrice(now) * i.Quantity)));
    }

    private async Task<List<ReminderStepData>> GetStepsAsync() =>
        await cache.GetOrCreateAsync(CacheKeys.AbandonedCartSteps, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheKeys.DefaultLifetime;
            return await context.AbandonedCartReminderSteps
                .Where(s => s.IsActive && s.DelayMinutes > 0)
                .OrderBy(s => s.StepNumber)
                .Select(s => new ReminderStepData(s.StepNumber, s.DelayMinutes, s.Channel, s.Title, s.Message, s.SmsText,
                    s.IncludeCoupon, s.CouponType, s.CouponValue, s.CouponMaxDiscountAmount, s.CouponMinOrderAmount, s.CouponValidDays))
                .ToListAsync();
        }) ?? [];

    private sealed record ReminderStepData(
        int StepNumber,
        int DelayMinutes,
        ReminderChannel Channel,
        string Title,
        string Message,
        string? SmsText,
        bool IncludeCoupon,
        Data.Entities.Marketing.CouponType CouponType,
        decimal CouponValue,
        decimal? CouponMaxDiscountAmount,
        decimal CouponMinOrderAmount,
        int CouponValidDays);
}
