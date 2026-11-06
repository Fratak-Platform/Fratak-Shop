using Fratak_Shop.Core.DTOs.Engagement;
using Fratak_Shop.Core.DTOs.Notifications;
using Fratak_Shop.Core.DTOs.Wallets;
using Fratak_Shop.Core.Services.Events;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Wallets;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Wallets;
using Fratak_Shop.Data.Utilities;
using Microsoft.EntityFrameworkCore;
using StockLedger = Fratak_Shop.Data.Utilities.StockLedger;
using StockMovementReason = Fratak_Shop.Data.Entities.Products.StockMovementReason;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Orders;

/// <summary>
/// Online returns: the customer picks items of a delivered order (within the return window),
/// a reason and photos; staff approve / reject, mark the parcel received and refund to the
/// wallet. Refunds are proportional: an order-level coupon discount is shared over the items,
/// promotional wallet credit used for the order comes back as promotional credit, and the
/// total refunded can never exceed what the customer paid.
/// </summary>
public interface IReturnService
{
    Task<List<ReturnReasonDto>> GetReasonsAsync();

    Task<ReturnEligibilityDto> GetEligibilityAsync(Guid userId, string orderNumber);

    Task<OperationResult<string>> CreateAsync(CreateReturnCommand command);

    Task<OperationResult> CancelAsync(Guid userId, string requestNumber);

    Task<OperationResult> SetTrackingCodeAsync(Guid userId, string requestNumber, string? trackingCode);

    Task<List<ReturnRequestDto>> GetUserRequestsAsync(Guid userId);

    Task<ReturnRequestDto?> GetUserRequestAsync(Guid userId, string requestNumber);

    Task<List<ReturnRequestDto>> GetByOrderAsync(Guid userId, Guid orderId);

    // ── Staff (admin panel) ───────────────────────────────────────────
    Task<OperationResult> ApproveAsync(Guid requestId, decimal? approvedAmount, string? note);

    Task<OperationResult> RejectAsync(Guid requestId, string note);

    Task<OperationResult> MarkReceivedAsync(Guid requestId, bool restock = true);

    Task<OperationResult> RefundAsync(Guid requestId);
}

public class ReturnService(
    FratakShopDbContext context,
    IStoreSettingService settingService,
    IWalletService walletService,
    INotificationService notifications,
    IShopEvents events,
    IMemoryCache cache) : IReturnService
{
    private static readonly ReturnStatus[] OpenStatuses = [ReturnStatus.Pending, ReturnStatus.Approved, ReturnStatus.Received, ReturnStatus.Refunded];

    public async Task<List<ReturnReasonDto>> GetReasonsAsync() =>
        await cache.GetOrCreateAsync(CacheInvalidation.ReturnReasonsKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheKeys.DefaultLifetime;
            return await context.ReturnReasons
                .Where(r => r.IsActive)
                .OrderBy(r => r.DisplayOrder)
                .Select(r => new ReturnReasonDto(r.Id, r.Title, r.Hint, r.RequiresImage))
                .ToListAsync();
        }) ?? [];

    public async Task<ReturnEligibilityDto> GetEligibilityAsync(Guid userId, string orderNumber)
    {
        var settings = await settingService.GetAsync();
        var number = (orderNumber ?? string.Empty).Trim().ToUpperInvariant();

        var order = await context.Orders
            .Include(o => o.Items).ThenInclude(i => i.Product!).ThenInclude(p => p.Images)
            .FirstOrDefaultAsync(o => o.OrderNumber == number && o.UserId == userId);

        if (order is null)
            return new ReturnEligibilityDto { OrderNumber = number, Reason = "سفارش مورد نظر یافت نشد." };

        var deadline = order.DeliveredAt?.AddDays(settings.ReturnWindowDays);
        string? reason = null;
        if (!settings.IsReturnEnabled)
            reason = "ثبت مرجوعی آنلاین در حال حاضر فعال نیست؛ با پشتیبانی تماس بگیرید.";
        else if (order.Status != OrderStatus.Delivered || order.DeliveredAt is null)
            reason = "فقط سفارش‌های تحویل‌شده قابل مرجوع کردن هستند.";
        else if (order.PaymentStatus != PaymentStatus.Paid)
            reason = "پرداخت این سفارش تکمیل نشده است.";
        else if (deadline < DateTimeOffset.UtcNow)
            reason = $"مهلت {settings.ReturnWindowDays} روزه مرجوع کردن این سفارش به پایان رسیده است.";

        var requested = await RequestedQuantitiesAsync(order.Id);
        var unitRefunds = await UnitRefundsAsync(order);

        var items = order.Items.Select(item => new ReturnableItemDto(
            item.Id,
            item.ProductId,
            item.ProductName,
            item.Product?.Slug,
            item.Product?.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).FirstOrDefault(),
            item.Quantity,
            Math.Max(0, item.Quantity - requested.GetValueOrDefault(item.Id)),
            unitRefunds.GetValueOrDefault(item.Id))).ToList();

        if (reason is null && items.All(i => i.ReturnableQuantity == 0))
            reason = "برای همه اقلام این سفارش قبلاً درخواست مرجوعی ثبت شده است.";

        return new ReturnEligibilityDto
        {
            IsEligible = reason is null,
            Reason = reason,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            DeliveredAt = order.DeliveredAt,
            Deadline = deadline,
            Items = items,
        };
    }

    public async Task<OperationResult<string>> CreateAsync(CreateReturnCommand command)
    {
        var settings = await settingService.GetAsync();
        var eligibility = await GetEligibilityAsync(command.UserId, command.OrderNumber);
        if (!eligibility.IsEligible)
            return OperationResult<string>.Error(eligibility.Reason);

        var reason = (await GetReasonsAsync()).FirstOrDefault(r => r.Id == command.ReasonId);
        if (reason is null)
            return OperationResult<string>.Error("علت مرجوعی را انتخاب کنید.");

        var lines = command.Quantities.Where(q => q.Value > 0).ToList();
        if (lines.Count == 0)
            return OperationResult<string>.Error("حداقل یک کالا و تعداد آن را برای مرجوعی انتخاب کنید.");

        foreach (var (itemId, quantity) in lines)
        {
            var item = eligibility.Items.FirstOrDefault(i => i.OrderItemId == itemId);
            if (item is null || quantity > item.ReturnableQuantity)
                return OperationResult<string>.Error($"تعداد انتخاب‌شده برای «{item?.ProductName ?? "کالا"}» بیشتر از تعداد قابل مرجوع است.");
        }

        if (reason.RequiresImage && command.Images.Count == 0)
            return OperationResult<string>.Error($"برای علت «{reason.Title}» ارسال حداقل یک تصویر لازم است.");

        if (command.Images.Count > settings.ReturnMaxImages)
            return OperationResult<string>.Error($"حداکثر {settings.ReturnMaxImages} تصویر قابل ارسال است.");

        var description = command.Description?.Trim();
        if (description is { Length: > 2000 })
            description = description[..2000];

        var request = new ReturnRequest
        {
            RequestNumber = await NewNumberAsync(),
            OrderId = eligibility.OrderId,
            UserId = command.UserId,
            ReasonId = reason.Id,
            ReasonTitle = reason.Title,
            Description = string.IsNullOrEmpty(description) ? null : description,
            Status = ReturnStatus.Pending,
        };

        foreach (var (itemId, quantity) in lines)
        {
            var item = eligibility.Items.First(i => i.OrderItemId == itemId);
            request.Items.Add(new ReturnRequestItem { OrderItemId = itemId, Quantity = quantity, UnitRefundAmount = item.UnitRefundAmount });
        }

        request.RequestedAmount = request.Items.Sum(i => i.UnitRefundAmount * i.Quantity);

        foreach (var image in command.Images)
            request.Images.Add(new ReturnRequestImage { FileName = image.FileName, ContentType = image.ContentType, SizeBytes = image.SizeBytes });

        context.ReturnRequests.Add(request);
        await context.SaveChangesAsync();

        await NotifyAsync(request.UserId, NotificationEvent.ReturnRequested, request.RequestNumber, eligibility.OrderNumber, request.RequestedAmount);
        return OperationResult<string>.Success(request.RequestNumber, $"درخواست مرجوعی {request.RequestNumber} ثبت شد و پس از بررسی نتیجه به شما اطلاع داده می‌شود.");
    }

    public async Task<OperationResult> CancelAsync(Guid userId, string requestNumber)
    {
        var number = (requestNumber ?? string.Empty).Trim().ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;
        var affected = await context.ReturnRequests
            .Where(r => r.RequestNumber == number && r.UserId == userId && r.Status == ReturnStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ReturnStatus.Cancelled)
                .SetProperty(r => r.ClosedAt, now)
                .SetProperty(r => r.UpdatedAt, now));

        return affected == 0
            ? OperationResult.Error("این درخواست قابل لغو نیست (فقط درخواست‌های در انتظار بررسی).")
            : OperationResult.Success("درخواست مرجوعی لغو شد.");
    }

    public async Task<OperationResult> SetTrackingCodeAsync(Guid userId, string requestNumber, string? trackingCode)
    {
        var code = trackingCode?.Trim();
        if (string.IsNullOrEmpty(code) || code.Length > 100)
            return OperationResult.Error("کد رهگیری مرسوله را وارد کنید (حداکثر ۱۰۰ کاراکتر).");

        var number = (requestNumber ?? string.Empty).Trim().ToUpperInvariant();
        var affected = await context.ReturnRequests
            .Where(r => r.RequestNumber == number && r.UserId == userId && r.Status == ReturnStatus.Approved)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReturnTrackingCode, code).SetProperty(r => r.UpdatedAt, DateTimeOffset.UtcNow));

        return affected == 0
            ? OperationResult.Error("ثبت کد رهگیری فقط برای درخواست‌های تاییدشده امکان‌پذیر است.")
            : OperationResult.Success("کد رهگیری مرسوله ثبت شد.");
    }

    public async Task<List<ReturnRequestDto>> GetUserRequestsAsync(Guid userId) =>
        await Project(context.ReturnRequests.Where(r => r.UserId == userId))
            .OrderByDescending(r => r.CreatedAt)
            .Take(100)
            .ToListAsync();

    public async Task<ReturnRequestDto?> GetUserRequestAsync(Guid userId, string requestNumber)
    {
        var number = (requestNumber ?? string.Empty).Trim().ToUpperInvariant();
        return await Project(context.ReturnRequests.Where(r => r.UserId == userId && r.RequestNumber == number)).FirstOrDefaultAsync();
    }

    public async Task<List<ReturnRequestDto>> GetByOrderAsync(Guid userId, Guid orderId) =>
        await Project(context.ReturnRequests.Where(r => r.UserId == userId && r.OrderId == orderId))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

    // ── Staff ─────────────────────────────────────────────────────────

    public async Task<OperationResult> ApproveAsync(Guid requestId, decimal? approvedAmount, string? note)
    {
        var request = await context.ReturnRequests.AsTracking().Include(r => r.Order).FirstOrDefaultAsync(r => r.Id == requestId);
        if (request is null)
            return OperationResult.NotFound("درخواست مرجوعی یافت نشد.");
        if (request.Status != ReturnStatus.Pending)
            return OperationResult.Error("فقط درخواست‌های در انتظار بررسی قابل تایید هستند.");

        var amount = decimal.Truncate(approvedAmount ?? request.RequestedAmount);
        if (amount < 0 || amount > request.RequestedAmount)
            return OperationResult.Error("مبلغ تاییدشده نمی‌تواند منفی یا بیشتر از مبلغ درخواستی باشد.");

        request.Status = ReturnStatus.Approved;
        request.ApprovedAmount = amount;
        request.ApprovedAt = DateTimeOffset.UtcNow;
        request.AdminNote = Limit(note, 1000);
        await context.SaveChangesAsync();

        await NotifyAsync(request.UserId, NotificationEvent.ReturnApproved, request.RequestNumber, request.Order.OrderNumber, amount, note);
        return OperationResult.Success("درخواست مرجوعی تایید شد.");
    }

    public async Task<OperationResult> RejectAsync(Guid requestId, string note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return OperationResult.Error("علت رد درخواست را بنویسید.");

        var request = await context.ReturnRequests.AsTracking().Include(r => r.Order).FirstOrDefaultAsync(r => r.Id == requestId);
        if (request is null)
            return OperationResult.NotFound("درخواست مرجوعی یافت نشد.");
        if (request.Status is not (ReturnStatus.Pending or ReturnStatus.Approved))
            return OperationResult.Error("این درخواست دیگر قابل رد نیست.");

        request.Status = ReturnStatus.Rejected;
        request.AdminNote = Limit(note, 1000);
        request.ClosedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();

        await NotifyAsync(request.UserId, NotificationEvent.ReturnRejected, request.RequestNumber, request.Order.OrderNumber, 0, note);
        return OperationResult.Success("درخواست مرجوعی رد شد.");
    }

    public async Task<OperationResult> MarkReceivedAsync(Guid requestId, bool restock = true)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        var now = DateTimeOffset.UtcNow;

        var affected = await context.ReturnRequests
            .Where(r => r.Id == requestId && r.Status == ReturnStatus.Approved)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReturnStatus.Received).SetProperty(r => r.ReceivedAt, now).SetProperty(r => r.UpdatedAt, now));
        if (affected == 0)
            return OperationResult.Error("فقط درخواست‌های تاییدشده قابل ثبت دریافت هستند.");

        if (restock)
        {
            var lines = await context.ReturnRequestItems
                .Where(i => i.ReturnRequestId == requestId && i.OrderItem.ProductId != null)
                .Select(i => new { ProductId = i.OrderItem.ProductId!.Value, i.Quantity })
                .ToListAsync();

            foreach (var line in lines.GroupBy(l => l.ProductId))
            {
                var quantity = line.Sum(l => l.Quantity);
                await context.Products.IgnoreQueryFilters()
                    .Where(p => p.Id == line.Key)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity + quantity));
                await StockLedger.RecordAsync(context, line.Key, quantity, StockMovementReason.Return, note: "دریافت کالای مرجوعی");
            }
        }

        await transaction.CommitAsync();
        if (restock)
            DataChangeNotifier.Publish(typeof(Data.Entities.Products.Product));

        return OperationResult.Success("دریافت کالای مرجوعی ثبت شد.");
    }

    public async Task<OperationResult> RefundAsync(Guid requestId)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();

        var request = await context.ReturnRequests
            .Include(r => r.Order).ThenInclude(o => o.Items)
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == requestId);
        if (request is null)
            return OperationResult.NotFound("درخواست مرجوعی یافت نشد.");
        if (request.Status is not (ReturnStatus.Approved or ReturnStatus.Received))
            return OperationResult.Error("فقط درخواست‌های تاییدشده قابل بازپرداخت هستند.");

        var order = request.Order;
        var remaining = Math.Max(0, order.PayableAmount - order.RefundedAmount);
        var amount = Math.Min(request.ApprovedAmount ?? request.RequestedAmount, remaining);

        // Promotional credit used for the order comes back as promotional credit (proportionally).
        var bonusLeft = Math.Max(0, order.WalletBonusAmount - order.RefundedBonusAmount);
        var bonus = order.PayableAmount <= 0
            ? 0
            : Math.Min(bonusLeft, Math.Min(amount, decimal.Ceiling(amount * order.WalletBonusAmount / order.PayableAmount)));

        var now = DateTimeOffset.UtcNow;
        var claimed = await context.ReturnRequests
            .Where(r => r.Id == requestId && (r.Status == ReturnStatus.Approved || r.Status == ReturnStatus.Received))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ReturnStatus.Refunded)
                .SetProperty(r => r.RefundedAmount, amount)
                .SetProperty(r => r.RefundedBonusAmount, bonus)
                .SetProperty(r => r.RefundedAt, now)
                .SetProperty(r => r.ClosedAt, now)
                .SetProperty(r => r.UpdatedAt, now));
        if (claimed == 0)
            return OperationResult.Error("این درخواست هم‌زمان در حال پردازش است.");

        if (amount > 0)
        {
            var credit = await walletService.CreditAsync(new WalletEntry
            {
                UserId = request.UserId,
                Amount = amount,
                NonWithdrawableAmount = bonus,
                Type = WalletTransactionType.ReturnRefund,
                Description = $"بازپرداخت مرجوعی {request.RequestNumber} (سفارش {order.OrderNumber})",
                OrderId = order.Id,
            });
            if (!credit.IsSuccess())
            {
                await transaction.RollbackAsync();
                return OperationResult.Error(credit.Message);
            }
        }

        // Everything of the order returned → the order itself becomes "returned".
        var returnedQuantities = await context.ReturnRequestItems
            .Where(i => i.ReturnRequest.OrderId == order.Id && i.ReturnRequest.Status == ReturnStatus.Refunded)
            .GroupBy(i => i.OrderItemId)
            .Select(g => new { g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToListAsync();
        var fullyReturned = order.Items.All(item => returnedQuantities.FirstOrDefault(r => r.Key == item.Id)?.Quantity >= item.Quantity);

        await context.Orders
            .Where(o => o.Id == order.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.RefundedAmount, o => o.RefundedAmount + amount)
                .SetProperty(o => o.RefundedBonusAmount, o => o.RefundedBonusAmount + bonus)
                .SetProperty(o => o.RefundedAt, now)
                .SetProperty(o => o.Status, o => fullyReturned ? OrderStatus.Returned : o.Status)
                .SetProperty(o => o.UpdatedAt, now));

        await transaction.CommitAsync();

        await NotifyAsync(request.UserId, NotificationEvent.ReturnRefunded, request.RequestNumber, order.OrderNumber, amount);
        if (fullyReturned)
            await events.OrderStatusChangedAsync(order.Id, OrderStatus.Returned);

        return OperationResult.Success($"مبلغ {amount:N0} تومان به کیف پول مشتری برگشت داده شد.");
    }

    // ── Helpers ───────────────────────────────────────────────────────

    /// <summary>Quantities already in open (not rejected / cancelled) requests, per order item.</summary>
    private async Task<Dictionary<Guid, int>> RequestedQuantitiesAsync(Guid orderId) =>
        await context.ReturnRequestItems
            .Where(i => i.ReturnRequest.OrderId == orderId && OpenStatuses.Contains(i.ReturnRequest.Status))
            .GroupBy(i => i.OrderItemId)
            .Select(g => new { g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity);

    /// <summary>
    /// Refund per unit: what the customer actually paid for it – the item total minus its share
    /// of the order-level discount (a free-shipping coupon's discount belongs to shipping, not to items).
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> UnitRefundsAsync(Order order)
    {
        var orderLevelDiscount = order.DiscountAmount;
        if (order.CouponId.HasValue && order.CouponDiscountAmount > 0)
        {
            var type = await context.Coupons.IgnoreQueryFilters().Where(c => c.Id == order.CouponId).Select(c => (CouponType?)c.Type).FirstOrDefaultAsync();
            if (type == CouponType.FreeShipping)
                orderLevelDiscount -= Math.Min(order.CouponDiscountAmount, order.ShippingAmount);
        }

        var itemsTotal = order.Items.Sum(i => i.TotalAmount);
        orderLevelDiscount = Math.Clamp(orderLevelDiscount, 0, itemsTotal);

        return order.Items.ToDictionary(item => item.Id, item =>
        {
            if (item.Quantity <= 0)
                return 0m;
            var share = itemsTotal <= 0 ? 0 : orderLevelDiscount * item.TotalAmount / itemsTotal;
            return Math.Max(0, decimal.Floor((item.TotalAmount - share) / item.Quantity));
        });
    }

    private async Task<string> NewNumberAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var number = "RT-" + System.Security.Cryptography.RandomNumberGenerator.GetInt32(10_000_000, 99_999_999);
            if (!await context.ReturnRequests.IgnoreQueryFilters().AnyAsync(r => r.RequestNumber == number))
                return number;
        }
        return "RT-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    }

    private static IQueryable<ReturnRequestDto> Project(IQueryable<ReturnRequest> query) =>
        query.Select(r => new ReturnRequestDto
        {
            Id = r.Id,
            RequestNumber = r.RequestNumber,
            OrderNumber = r.Order.OrderNumber,
            Status = r.Status,
            ReasonTitle = r.ReasonTitle,
            Description = r.Description,
            RequestedAmount = r.RequestedAmount,
            ApprovedAmount = r.ApprovedAmount,
            RefundedAmount = r.RefundedAmount,
            AdminNote = r.AdminNote,
            ReturnTrackingCode = r.ReturnTrackingCode,
            CreatedAt = r.CreatedAt,
            ApprovedAt = r.ApprovedAt,
            ReceivedAt = r.ReceivedAt,
            RefundedAt = r.RefundedAt,
            ClosedAt = r.ClosedAt,
            Items = r.Items.Select(i => new ReturnRequestItemDto(i.OrderItem.ProductName, i.OrderItem.Product != null ? i.OrderItem.Product.Slug : null, i.Quantity, i.UnitRefundAmount)).ToList(),
            Images = r.Images.Select(i => i.FileName).ToList(),
        });

    private Task NotifyAsync(Guid userId, NotificationEvent @event, string requestNumber, string orderNumber, decimal amount, string? note = null) =>
        notifications.NotifyAsync(new NotificationRequest
        {
            UserId = userId,
            Event = @event,
            Category = NotificationCategory.Order,
            LinkUrl = "/profile/returns/" + requestNumber.ToLowerInvariant(),
            Values =
            {
                ["RequestNumber"] = requestNumber,
                ["OrderNumber"] = orderNumber,
                ["Amount"] = amount.ToString("N0"),
                ["Note"] = note ?? string.Empty,
            },
        });

    private static string? Limit(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var text = value.Trim();
        return text.Length <= max ? text : text[..max];
    }
}
