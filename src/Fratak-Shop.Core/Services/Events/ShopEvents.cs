using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Core.DTOs.Notifications;
using Fratak_Shop.Core.Services.Loyalty;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Loyalty;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Events;

/// <summary>
/// Side effects of business events – customer notifications and club points – kept
/// out of the order / wallet / payment services. Called after the business transaction
/// has been committed; every handler is best effort and never throws, so a failing
/// notification can't undo an order or a payment.
/// </summary>
public interface IShopEvents
{
    Task OrderPlacedAsync(Guid orderId);

    /// <summary>Online / wallet payment succeeded (or an admin marked the order paid).</summary>
    Task OrderPaidAsync(Guid orderId);

    Task OrderStatusChangedAsync(Guid orderId, OrderStatus status);

    Task OrderPaymentFailedAsync(Guid orderId);

    Task WalletChangedAsync(Guid userId, NotificationEvent @event, decimal amount, string? description = null);

    Task UserRegisteredAsync(Guid userId, string? referralCode);

    Task ProfileUpdatedAsync(Guid userId);

    Task CommentApprovedAsync(Guid commentId);

    Task SecurityEventAsync(Guid userId, NotificationEvent @event);

    Task GiftCardPurchasedAsync(Guid userId, string reference, decimal amount);

    /// <summary>Sign-in from a browser / device the customer never used before.</summary>
    Task NewDeviceSignInAsync(Guid userId, string deviceName, string? ipAddress);
}

public class ShopEvents(
    IServiceProvider services,
    FratakShopDbContext context,
    ILogger<ShopEvents> logger) : IShopEvents
{
    // Resolved lazily: the loyalty service depends on the wallet service, which raises events itself.
    private INotificationService Notifications => services.GetRequiredService<INotificationService>();
    private ILoyaltyService Loyalty => services.GetRequiredService<ILoyaltyService>();
    private Orders.IInvoiceService Invoices => services.GetRequiredService<Orders.IInvoiceService>();
    private Shopping.IAbandonedCartService AbandonedCarts => services.GetRequiredService<Shopping.IAbandonedCartService>();
    private Orders.IOrderFeedbackService Feedback => services.GetRequiredService<Orders.IOrderFeedbackService>();
    private Admin.IStaffNotificationService Staff => services.GetRequiredService<Admin.IStaffNotificationService>();

    public Task OrderPlacedAsync(Guid orderId) => RunAsync(nameof(OrderPlacedAsync), async () =>
    {
        var order = await LoadOrderAsync(orderId);
        if (order?.UserId is null)
            return;

        await NotifyOrderAsync(order, NotificationEvent.OrderPlaced);
        await AbandonedCarts.MarkRecoveredAsync(order.UserId.Value, order.Id, order.PayableAmount);

        // اعلان کارکنان: سفارش‌های پرداخت در محل / کارت به کارت همین حالا قابل پیگیری‌اند.
        // سفارش‌های آنلاین و کیف پول بعد از پرداخت (OrderPaidAsync) اعلام می‌شوند تا اعلان تکراری نرود.
        var method = await context.Orders.Where(o => o.Id == orderId).Select(o => o.PaymentMethod).FirstOrDefaultAsync();
        if (method is PaymentMethod.CashOnDelivery or PaymentMethod.BankTransfer)
            await NotifyStaffOfOrderAsync(order, "سفارش جدید", StaffNotificationKind.NewOrder);
    });

    public Task OrderPaidAsync(Guid orderId) => RunAsync(nameof(OrderPaidAsync), async () =>
    {
        var order = await LoadOrderAsync(orderId);
        if (order?.UserId is null)
            return;

        await NotifyOrderAsync(order, NotificationEvent.OrderPaid);
        await Invoices.EnsureIssuedAsync(orderId);
        await NotifyStaffOfOrderAsync(order, "سفارش جدید پرداخت شد", StaffNotificationKind.OrderPaid);
    });

    /// <summary>
    /// اعلان «سفارش جدید» به کارکنانی که مجوز مشاهدهٔ سفارش دارند (زنگوله پنل + صدا + پوش).
    /// مثال متن: «سفارش جدید پرداخت شد FR-1024» / «علی رضایی – 1,250,000 تومان».
    /// </summary>
    private Task NotifyStaffOfOrderAsync(OrderSnapshot order, string title, StaffNotificationKind kind) =>
        Staff.NotifyAsync(new Admin.StaffAlert(
            kind,
            $"{title} {order.OrderNumber}",
            $"{order.CustomerName} – {order.PayableAmount:N0} تومان",
            $"/admin/orders/{order.Id}",
            [Admin.AdminPermissions.OrdersView]));

    public Task OrderStatusChangedAsync(Guid orderId, OrderStatus status) => RunAsync(nameof(OrderStatusChangedAsync), async () =>
    {
        var order = await LoadOrderAsync(orderId);
        if (order?.UserId is null)
            return;

        var @event = status switch
        {
            OrderStatus.Confirmed => NotificationEvent.OrderConfirmed,
            OrderStatus.Processing => NotificationEvent.OrderProcessing,
            OrderStatus.Shipped => NotificationEvent.OrderShipped,
            OrderStatus.Delivered => NotificationEvent.OrderDelivered,
            OrderStatus.Cancelled => NotificationEvent.OrderCancelled,
            OrderStatus.Returned => NotificationEvent.OrderReturned,
            _ => NotificationEvent.None,
        };

        if (@event != NotificationEvent.None)
            await NotifyOrderAsync(order, @event);

        if (status == OrderStatus.Delivered)
        {
            await Invoices.EnsureIssuedAsync(orderId);  // cash on delivery: invoice once delivered
            await Loyalty.OnOrderSettledAsync(orderId); // club points only for delivered orders (idempotent)
            await Feedback.RequestAsync(orderId);       // «امتیاز به تجربه خرید»
        }
        else if (status is OrderStatus.Cancelled or OrderStatus.Returned)
            await Loyalty.RevertOrderAsync(orderId);
    });

    public Task OrderPaymentFailedAsync(Guid orderId) => RunAsync(nameof(OrderPaymentFailedAsync), async () =>
    {
        var order = await LoadOrderAsync(orderId);
        if (order?.UserId is null || order.Status != OrderStatus.Pending)
            return;

        await NotifyOrderAsync(order, NotificationEvent.PaymentFailed);
    });

    public Task WalletChangedAsync(Guid userId, NotificationEvent @event, decimal amount, string? description = null) =>
        RunAsync(nameof(WalletChangedAsync), async () =>
        {
            var balance = await context.Users.Where(u => u.Id == userId).Select(u => u.WalletBalance).FirstOrDefaultAsync();

            await Notifications.NotifyAsync(new NotificationRequest
            {
                UserId = userId,
                Event = @event,
                Category = NotificationCategory.Wallet,
                LinkUrl = "/profile/wallet",
                Values =
                {
                    ["Amount"] = Math.Abs(amount).ToString("N0"),
                    ["Balance"] = balance.ToString("N0"),
                    ["Description"] = description,
                },
            });
        });

    public Task UserRegisteredAsync(Guid userId, string? referralCode) => RunAsync(nameof(UserRegisteredAsync), async () =>
    {
        await Notifications.NotifyAsync(new NotificationRequest
        {
            UserId = userId,
            Event = NotificationEvent.Welcome,
            Category = NotificationCategory.System,
            LinkUrl = "/club",
        });

        await Loyalty.AwardAsync(userId, LoyaltyAction.Registration, "registration", description: "عضویت در سایت");
        await Loyalty.ApplyReferralAsync(userId, referralCode);
        await Loyalty.EnsureReferralCodeAsync(userId);
    });

    public Task ProfileUpdatedAsync(Guid userId) => RunAsync(nameof(ProfileUpdatedAsync), async () =>
    {
        var complete = await context.Users.AnyAsync(u => u.Id == userId
            && u.FirstName != null && u.FirstName != ""
            && u.LastName != null && u.LastName != ""
            && u.Email != null && u.Email != "");

        if (complete)
            await Loyalty.AwardAsync(userId, LoyaltyAction.CompleteProfile, "profile-complete", description: "تکمیل اطلاعات حساب");
    });

    public Task CommentApprovedAsync(Guid commentId) => RunAsync(nameof(CommentApprovedAsync), async () =>
    {
        var comment = await context.Comments
            .Where(c => c.Id == commentId && c.Status == CommentStatus.Approved && c.UserId != null && c.ProductId != null && c.ParentId == null)
            .Select(c => new { c.UserId, ProductName = c.Product!.Name })
            .FirstOrDefaultAsync();

        if (comment?.UserId is null)
            return;

        await Loyalty.AwardAsync(comment.UserId.Value, LoyaltyAction.ProductReview, $"review:{commentId:N}",
            description: $"دیدگاه برای «{comment.ProductName}»");
    });

    public Task SecurityEventAsync(Guid userId, NotificationEvent @event) => RunAsync(nameof(SecurityEventAsync), () =>
        Notifications.NotifyAsync(new NotificationRequest
        {
            UserId = userId,
            Event = @event,
            Category = NotificationCategory.Security,
            LinkUrl = "/profile/password",
        }));

    public Task GiftCardPurchasedAsync(Guid userId, string reference, decimal amount) => RunAsync(nameof(GiftCardPurchasedAsync), () =>
        Notifications.NotifyAsync(new NotificationRequest
        {
            UserId = userId,
            Event = NotificationEvent.GiftCardPurchased,
            Category = NotificationCategory.Wallet,
            LinkUrl = "/profile/gift-cards",
            Values = { ["Amount"] = amount.ToString("N0"), ["Reference"] = reference },
        }));

    public Task NewDeviceSignInAsync(Guid userId, string deviceName, string? ipAddress) => RunAsync(nameof(NewDeviceSignInAsync), () =>
        Notifications.NotifyAsync(new NotificationRequest
        {
            UserId = userId,
            Event = NotificationEvent.NewDeviceLogin,
            Category = NotificationCategory.Security,
            LinkUrl = "/profile/sessions",
            Values =
            {
                ["Device"] = deviceName,
                ["Ip"] = string.IsNullOrWhiteSpace(ipAddress) ? "نامشخص" : ipAddress,
                ["Time"] = PersianDate.Format(DateTimeOffset.UtcNow),
            },
        }));

    private async Task NotifyOrderAsync(OrderSnapshot order, NotificationEvent @event)
    {
        var refundText = order.RefundedAmount > 0
            ? $"مبلغ {order.RefundedAmount:N0} تومان به کیف پول شما برگشت داده شد."
            : string.Empty;

        await Notifications.NotifyAsync(new NotificationRequest
        {
            UserId = order.UserId!.Value,
            Event = @event,
            Category = NotificationCategory.Order,
            LinkUrl = $"/profile/orders/{order.OrderNumber.ToLowerInvariant()}",
            Values =
            {
                ["OrderNumber"] = order.OrderNumber,
                ["Amount"] = order.PayableAmount.ToString("N0"),
                ["TrackingCode"] = string.IsNullOrWhiteSpace(order.TrackingCode) ? "—" : order.TrackingCode,
                ["RefundText"] = refundText,
                ["Name"] = order.CustomerName,
            },
        });
    }

    private Task<OrderSnapshot?> LoadOrderAsync(Guid orderId) =>
        context.Orders
            .Where(o => o.Id == orderId)
            .Select(o => new OrderSnapshot(o.Id, o.UserId, o.OrderNumber, o.Status, o.PayableAmount, o.RefundedAmount, o.TrackingCode, o.CustomerName))
            .FirstOrDefaultAsync();

    private async Task RunAsync(string name, Func<Task> handler)
    {
        try
        {
            await handler();
        }
        catch (Exception exception)
        {
            context.ChangeTracker.Clear();
            logger.LogError(exception, "Shop event handler {Handler} failed.", name);
        }
    }

    private sealed record OrderSnapshot(Guid Id, Guid? UserId, string OrderNumber, OrderStatus Status,
        decimal PayableAmount, decimal RefundedAmount, string? TrackingCode, string CustomerName);
}
