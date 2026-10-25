using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

public class OrderDetailModel(
    IOrderService orderService,
    IReturnService returnService,
    IOrderFeedbackService feedbackService,
    ISiteContext site,
    IOrderMessageService messageService,
    IOrderSlaService slaService) : ShopPageModel
{
    /// <summary>پیام‌های فروشگاه دربارهٔ این سفارش و پاسخ‌های مشتری (بخش «پیام‌ها»، لنگر #messages).</summary>
    public List<OrderMessageDto> Messages { get; private set; } = [];

    /// <summary>بازهٔ تحویل تخمینی بر اساس تنظیمات زمان‌بندی (مثلاً «۵ تا ۸ آبان»)؛ سفارش تحویل‌شده/لغوشده ← null.</summary>
    public OrderDeliveryEstimate? Estimate { get; private set; }

    /// <summary>مشتری فقط تا پیش از ارسال سفارش می‌تواند پیام بدهد (بعد از آن از تیکت پشتیبانی).</summary>
    public bool CanReply => Order.Status is OrderStatus.Pending or OrderStatus.Confirmed or OrderStatus.Processing && Messages.Count > 0;

    public OrderDto Order { get; private set; } = null!;

    /// <summary>Return requests of this order and whether a new one can be started.</summary>
    public List<Core.DTOs.Engagement.ReturnRequestDto> Returns { get; private set; } = [];
    public Core.DTOs.Engagement.ReturnEligibilityDto? ReturnEligibility { get; private set; }

    /// <summary>Shopping-experience rating (after delivery).</summary>
    public OrderFeedbackState? Feedback { get; private set; }

    [BindProperty]
    public FeedbackInput FeedbackForm { get; set; } = new();

    /// <summary>
    /// Self-cancellation is allowed until the shop starts preparing the order
    /// (and only when the store allows it). A paid amount is refunded to the wallet.
    /// </summary>
    public bool CanCancel => site.Settings.AllowCustomerCancellation && OrderRules.IsCancellableByCustomer(Order.Status);

    public bool CanPay => site.Settings.HasZarinPal
                          && OrderRules.IsPayable(Order.Status, Order.PaymentMethod, Order.PaymentStatus, Order.PaymentDeadline, DateTimeOffset.UtcNow);

    public bool WillRefundToWallet => Order.PaymentStatus == PaymentStatus.Paid;

    [BindProperty]
    [MaxLength(500, ErrorMessage = "دلیل لغو حداکثر ۵۰۰ کاراکتر است.")]
    public string? CancelReason { get; set; }

    public static readonly (OrderStatus Status, string Title)[] Steps =
    [
        (OrderStatus.Pending, "ثبت سفارش"),
        (OrderStatus.Confirmed, "تایید"),
        (OrderStatus.Processing, "آماده سازی"),
        (OrderStatus.Shipped, "ارسال"),
        (OrderStatus.Delivered, "تحویل"),
    ];

    public async Task<IActionResult> OnGetAsync(string orderNumber)
    {
        var order = await orderService.GetOrderByNumberAsync(orderNumber);
        if (order is null || order.UserId != CurrentUserId)
            return NotFound();

        Order = order;
        await LoadEngagementAsync();
        ViewData[PageMetaKeys.Title] = $"سفارش {order.OrderNumber}";
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(string orderNumber)
    {
        var order = await orderService.GetOrderByNumberAsync(orderNumber);
        if (order is null || order.UserId != CurrentUserId)
            return NotFound();

        Order = order;
        // Only the cancel field matters here (the feedback form on the same page is validated by its own handler).
        if (!CanCancel || ModelState.GetFieldValidationState(nameof(CancelReason)) == Microsoft.AspNetCore.Mvc.ModelBinding.ModelValidationState.Invalid)
        {
            ErrorMessage = "این سفارش دیگر قابل لغو نیست؛ برای پیگیری با پشتیبانی تماس بگیرید.";
            return RedirectToPage(new { orderNumber });
        }

        // The service re-checks status and ownership atomically (the shop may have
        // started preparing the order meanwhile) and refunds paid amounts to the wallet.
        var reason = string.IsNullOrWhiteSpace(CancelReason) ? null : CancelReason.Trim();
        var result = await orderService.CancelOrderByCustomerAsync(order.Id, CurrentUserId!.Value, reason);
        ShowResult(result);
        return RedirectToPage(new { orderNumber });
    }

    public async Task<IActionResult> OnPostFeedbackAsync(string orderNumber)
    {
        var order = await orderService.GetOrderByNumberAsync(orderNumber);
        if (order is null || order.UserId != CurrentUserId)
            return NotFound();

        var result = await feedbackService.SubmitAsync(CurrentUserId!.Value, new SubmitOrderFeedbackCommand
        {
            OrderNumber = order.OrderNumber,
            OverallRating = FeedbackForm.Overall,
            DeliveryRating = FeedbackForm.Delivery,
            PackagingRating = FeedbackForm.Packaging,
            ProductMatchRating = FeedbackForm.ProductMatch,
            WouldRecommend = FeedbackForm.WouldRecommend,
            Comment = FeedbackForm.Comment,
        });
        ShowResult(result);
        return Redirect($"/profile/orders/{order.OrderNumber.ToLowerInvariant()}#feedback");
    }

    private async Task LoadEngagementAsync()
    {
        var userId = CurrentUserId!.Value;
        if (site.Settings.IsReturnEnabled)
        {
            Returns = await returnService.GetByOrderAsync(userId, Order.Id);
            if (Order.Status == OrderStatus.Delivered)
                ReturnEligibility = await returnService.GetEligibilityAsync(userId, Order.OrderNumber);
        }

        if (site.Settings.IsOrderFeedbackEnabled || Order.Status == OrderStatus.Delivered)
            Feedback = await feedbackService.GetStateAsync(userId, Order.Id);

        Messages = await messageService.ListAsync(Order.Id);
        if (Messages.Any(m => m.FromStaff && m.ReadByCustomerAt is null))
            await messageService.MarkReadByCustomerAsync(Order.Id, userId);
        (_, Estimate) = await slaService.GetForOrderAsync(Order.Id, HttpContext.RequestAborted);
    }

    /// <summary>پاسخ متنی مشتری به پیام فروشگاه (سقف ۱۰ پیام در ساعت در سرویس).</summary>
    public async Task<IActionResult> OnPostReplyAsync(string orderNumber, string? body)
    {
        var order = await orderService.GetOrderByNumberAsync(orderNumber);
        if (order is null || order.UserId != CurrentUserId)
            return NotFound();

        ShowResult(await messageService.ReplyFromCustomerAsync(order.Id, CurrentUserId!.Value, body));
        return Redirect($"/profile/orders/{order.OrderNumber.ToLowerInvariant()}#messages");
    }

    /// <summary>
    /// تصمیم مشتری دربارهٔ کالای ناموجود: <c>RemoveItem</c> (حذف کالا و ادامه) یا <c>CancelOrder</c> (لغو کل سفارش).
    /// بازمحاسبهٔ مبلغ و بازگشت وجه به کیف پول را سرویس انجام می‌دهد.
    /// </summary>
    public async Task<IActionResult> OnPostDecideAsync(string orderNumber, Guid messageId, OrderMessageDecision decision)
    {
        var order = await orderService.GetOrderByNumberAsync(orderNumber);
        if (order is null || order.UserId != CurrentUserId)
            return NotFound();

        ShowResult(await messageService.DecideAsync(order.Id, messageId, CurrentUserId!.Value, decision));
        return Redirect($"/profile/orders/{order.OrderNumber.ToLowerInvariant()}#messages");
    }

    public class FeedbackInput
    {
        [Range(1, 5)] public int Overall { get; set; }
        [Range(1, 5)] public int Delivery { get; set; }
        [Range(1, 5)] public int Packaging { get; set; }
        [Range(1, 5)] public int ProductMatch { get; set; }
        public bool? WouldRecommend { get; set; }
        [StringLength(1000)] public string? Comment { get; set; }
    }
}
