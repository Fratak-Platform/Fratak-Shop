using Fratak_Shop.Core.DTOs.Notifications;
using Fratak_Shop.Core.Services.Loyalty;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Loyalty;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Orders;

/// <summary>
/// «امتیاز به تجربه خرید»: after delivery the customer rates the whole experience (overall,
/// delivery, packaging, product as described). One rating per order, within the feedback window;
/// optional club points. The admin panel later shows the averages.
/// </summary>
public interface IOrderFeedbackService
{
    Task<OrderFeedbackState> GetStateAsync(Guid userId, Guid orderId);

    Task<OperationResult> SubmitAsync(Guid userId, SubmitOrderFeedbackCommand command);

    /// <summary>Called when an order is delivered: invites the customer to rate it.</summary>
    Task RequestAsync(Guid orderId);

    Task<OrderFeedbackStats> GetStatsAsync(int days = 90);
}

public sealed class SubmitOrderFeedbackCommand
{
    public string OrderNumber { get; set; } = null!;
    public int OverallRating { get; set; }
    public int DeliveryRating { get; set; }
    public int PackagingRating { get; set; }
    public int ProductMatchRating { get; set; }
    public bool? WouldRecommend { get; set; }
    public string? Comment { get; set; }
}

public sealed record OrderFeedbackState(bool CanSubmit, bool AlreadySubmitted, string? Reason, OrderFeedbackView? Feedback, int RewardPoints);

public sealed record OrderFeedbackView(int Overall, int Delivery, int Packaging, int ProductMatch, bool? WouldRecommend, string? Comment, DateTimeOffset CreatedAt);

public sealed record OrderFeedbackStats(int Count, double Overall, double Delivery, double Packaging, double ProductMatch, double RecommendPercent);

public class OrderFeedbackService(
    FratakShopDbContext context,
    IStoreSettingService settingService,
    ILoyaltyService loyalty,
    INotificationService notifications) : IOrderFeedbackService
{
    public async Task<OrderFeedbackState> GetStateAsync(Guid userId, Guid orderId)
    {
        var settings = await settingService.GetAsync();
        var points = await RewardPointsAsync();

        var feedback = await context.OrderFeedbacks
            .Where(f => f.OrderId == orderId && f.UserId == userId)
            .Select(f => new OrderFeedbackView(f.OverallRating, f.DeliveryRating, f.PackagingRating, f.ProductMatchRating, f.WouldRecommend, f.Comment, f.CreatedAt))
            .FirstOrDefaultAsync();
        if (feedback is not null)
            return new OrderFeedbackState(false, true, null, feedback, points);

        if (!settings.IsOrderFeedbackEnabled)
            return new OrderFeedbackState(false, false, "ثبت امتیاز تجربه خرید فعال نیست.", null, points);

        var order = await context.Orders
            .Where(o => o.Id == orderId && o.UserId == userId)
            .Select(o => new { o.Status, o.DeliveredAt })
            .FirstOrDefaultAsync();

        if (order is null || order.Status != OrderStatus.Delivered || order.DeliveredAt is null)
            return new OrderFeedbackState(false, false, "پس از تحویل سفارش می‌توانید به تجربه خرید امتیاز دهید.", null, points);

        if (order.DeliveredAt.Value.AddDays(settings.OrderFeedbackWindowDays) < DateTimeOffset.UtcNow)
            return new OrderFeedbackState(false, false, "مهلت ثبت امتیاز برای این سفارش به پایان رسیده است.", null, points);

        return new OrderFeedbackState(true, false, null, null, points);
    }

    public async Task<OperationResult> SubmitAsync(Guid userId, SubmitOrderFeedbackCommand command)
    {
        var number = (command.OrderNumber ?? string.Empty).Trim().ToUpperInvariant();
        var orderId = await context.Orders.Where(o => o.OrderNumber == number && o.UserId == userId).Select(o => (Guid?)o.Id).FirstOrDefaultAsync();
        if (orderId is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        var state = await GetStateAsync(userId, orderId.Value);
        if (state.AlreadySubmitted)
            return OperationResult.Error("برای این سفارش قبلاً امتیاز ثبت کرده‌اید.");
        if (!state.CanSubmit)
            return OperationResult.Error(state.Reason ?? "ثبت امتیاز برای این سفارش ممکن نیست.");

        int[] ratings = [command.OverallRating, command.DeliveryRating, command.PackagingRating, command.ProductMatchRating];
        if (ratings.Any(r => r is < 1 or > 5))
            return OperationResult.Error("به همه موارد از ۱ تا ۵ ستاره امتیاز دهید.");

        var comment = command.Comment?.Trim();
        if (comment is { Length: > 1000 })
            comment = comment[..1000];

        context.OrderFeedbacks.Add(new OrderFeedback
        {
            OrderId = orderId.Value,
            UserId = userId,
            OverallRating = (byte)command.OverallRating,
            DeliveryRating = (byte)command.DeliveryRating,
            PackagingRating = (byte)command.PackagingRating,
            ProductMatchRating = (byte)command.ProductMatchRating,
            WouldRecommend = command.WouldRecommend,
            Comment = string.IsNullOrEmpty(comment) ? null : comment,
        });

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            return OperationResult.Error("برای این سفارش قبلاً امتیاز ثبت کرده‌اید.");
        }

        var awarded = await loyalty.AwardAsync(userId, LoyaltyAction.OrderFeedback, $"feedback:{orderId:N}", orderId: orderId, description: $"امتیاز به تجربه خرید سفارش {number}");
        return OperationResult.Success(awarded > 0
            ? $"از نظر شما سپاسگزاریم! {awarded} امتیاز باشگاه مشتریان به حساب شما اضافه شد."
            : "از نظر شما سپاسگزاریم!");
    }

    public async Task RequestAsync(Guid orderId)
    {
        var settings = await settingService.GetAsync();
        if (!settings.IsOrderFeedbackEnabled)
            return;

        var order = await context.Orders
            .Where(o => o.Id == orderId && o.UserId != null && !context.OrderFeedbacks.Any(f => f.OrderId == o.Id))
            .Select(o => new { o.UserId, o.OrderNumber })
            .FirstOrDefaultAsync();
        if (order?.UserId is null)
            return;

        var points = await RewardPointsAsync();
        await notifications.NotifyAsync(new NotificationRequest
        {
            UserId = order.UserId.Value,
            Event = NotificationEvent.OrderFeedbackRequest,
            Category = NotificationCategory.Order,
            LinkUrl = $"/profile/orders/{order.OrderNumber.ToLowerInvariant()}#feedback",
            Values =
            {
                ["OrderNumber"] = order.OrderNumber,
                ["Points"] = points > 0 ? $" و {points} امتیاز باشگاه هدیه بگیرید" : string.Empty,
            },
        });
    }

    public async Task<OrderFeedbackStats> GetStatsAsync(int days = 90)
    {
        var from = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days, 1, 3650));
        var stats = await context.OrderFeedbacks
            .Where(f => f.CreatedAt >= from)
            .GroupBy(f => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Overall = g.Average(f => (double)f.OverallRating),
                Delivery = g.Average(f => (double)f.DeliveryRating),
                Packaging = g.Average(f => (double)f.PackagingRating),
                Match = g.Average(f => (double)f.ProductMatchRating),
                Answered = g.Count(f => f.WouldRecommend != null),
                Yes = g.Count(f => f.WouldRecommend == true),
            })
            .FirstOrDefaultAsync();

        return stats is null
            ? new OrderFeedbackStats(0, 0, 0, 0, 0, 0)
            : new OrderFeedbackStats(stats.Count, stats.Overall, stats.Delivery, stats.Packaging, stats.Match,
                stats.Answered == 0 ? 0 : 100.0 * stats.Yes / stats.Answered);
    }

    private async Task<int> RewardPointsAsync()
    {
        var program = await loyalty.GetProgramAsync();
        return program.Rules.FirstOrDefault(r => r.Action == LoyaltyAction.OrderFeedback)?.Points ?? 0;
    }
}
