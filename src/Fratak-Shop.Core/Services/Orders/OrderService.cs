using Fratak_Shop.Core.DTOs.Orders.Command;
using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.Mappers.Order;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Orders;

public class OrderService(FratakShopDbContext context, INotificationService notificationService) : IOrderService
{
    public async Task<OperationResult> CreateOrderAsync(Order order)
    {
        if (order is null)
            return OperationResult.Error("سفارش نامعتبر است.");

        if (string.IsNullOrWhiteSpace(order.OrderNumber))
            return OperationResult.Error("شماره سفارش را وارد کنید.");

        if (string.IsNullOrWhiteSpace(order.CustomerName))
            return OperationResult.Error("نام مشتری را وارد کنید.");

        if (string.IsNullOrWhiteSpace(order.CustomerPhone))
            return OperationResult.Error("شماره تماس مشتری را وارد کنید.");

        var exists = await context.Orders.AnyAsync(x => x.OrderNumber == order.OrderNumber && !x.IsDeleted);
        if (exists)
            return OperationResult.Error("این شماره سفارش قبلاً ثبت شده است.");

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        return OperationResult.Success("سفارش با موفقیت ثبت شد.");
    }

    public async Task<OperationResult> UpdateOrderAsync(Order order)
    {
        if (order is null)
            return OperationResult.Error("سفارش نامعتبر است.");

        var entity = await context.Orders.AsTracking().FirstOrDefaultAsync(x => x.Id == order.Id && !x.IsDeleted);
        if (entity is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        entity.UserId = order.UserId;
        entity.Status = order.Status;
        entity.PaymentMethod = order.PaymentMethod;
        entity.PaymentStatus = order.PaymentStatus;
        entity.TransactionId = order.TransactionId;
        entity.TrackingCode = order.TrackingCode;
        entity.SubtotalAmount = order.SubtotalAmount;
        entity.DiscountAmount = order.DiscountAmount;
        entity.ShippingAmount = order.ShippingAmount;
        entity.PayableAmount = order.PayableAmount;
        entity.ShippingMethodName = order.ShippingMethodName;
        entity.ShippingMethodId = order.ShippingMethodId;
        entity.RequestedDeliveryDate = order.RequestedDeliveryDate;
        entity.CustomerName = order.CustomerName;
        entity.CustomerPhone = order.CustomerPhone;
        entity.ShippingProvince = order.ShippingProvince;
        entity.ShippingCity = order.ShippingCity;
        entity.ShippingAddress = order.ShippingAddress;
        entity.ShippingBuildingNumber = order.ShippingBuildingNumber;
        entity.ShippingUnit = order.ShippingUnit;
        entity.ShippingPostalCode = order.ShippingPostalCode;
        entity.ShippingPhone = order.ShippingPhone;
        entity.CustomerNote = order.CustomerNote;
        entity.PaidAt = order.PaidAt;

        await context.SaveChangesAsync();
        return OperationResult.Success("سفارش با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteOrderAsync(Guid id)
    {
        var entity = await context.Orders.AsTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (entity is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        context.Orders.Remove(entity);
        await context.SaveChangesAsync();

        return OperationResult.Success("سفارش با موفقیت حذف شد.");
    }

    public async Task<Order?> GetOrderByIdAsync(Guid id)
    {
        return await context.Orders
            .Include(x => x.Items)
            .ThenInclude(x => x.Product)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
    }

    public async Task<List<Order>> GetOrdersByUserAsync(Guid userId)
    {
        return await context.Orders
            .Where(x => x.UserId == userId && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Order>> GetOrdersAsync()
    {
        return await context.Orders
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
    }
    // ================================================================ management

    public async Task<OrderFilterResult> GetFilterAsync(OrderFilterParams filterParams)
    {
        var query = context.Orders
            .Include(order => order.Items)
            .ThenInclude(item => item.Product)
            .ThenInclude(product => product!.Images)
            .Include(order => order.StatusHistory)
            .ThenInclude(history => history.ChangedByUser)
            .Include(order => order.PaymentTransactions)
            .Include(order => order.BankTransferPayment)
            .Include(order => order.Coupon)
            .Include(order => order.User)
            .AsQueryable();

        if (filterParams.Status.HasValue)
            query = query.Where(order => order.Status == filterParams.Status.Value);

        if (filterParams.PaymentStatus.HasValue)
            query = query.Where(order => order.PaymentStatus == filterParams.PaymentStatus.Value);

        if (filterParams.PaymentMethod.HasValue)
            query = query.Where(order => order.PaymentMethod == filterParams.PaymentMethod.Value);

        if (filterParams.UserId.HasValue)
            query = query.Where(order => order.UserId == filterParams.UserId.Value);

        if (filterParams.FromDate.HasValue)
            query = query.Where(order => order.CreatedAt >= filterParams.FromDate.Value);

        if (filterParams.ToDate.HasValue)
            query = query.Where(order => order.CreatedAt <= filterParams.ToDate.Value);

        if (filterParams.PendingReviewOnly == true)
            query = query.Where(order => order.BankTransferPayment != null &&
                                         order.BankTransferPayment.Status == Data.Entities.Payments.BankTransferStatus.AwaitingReview);

        if (!string.IsNullOrWhiteSpace(filterParams.Search))
        {
            var term = filterParams.Search.Trim();
            query = query.Where(order => order.OrderNumber.Contains(term) ||
                                         order.CustomerName.Contains(term) ||
                                         order.CustomerPhone.Contains(term) ||
                                         (order.TrackingCode != null && order.TrackingCode.Contains(term)));
        }

        var total = await query.CountAsync();
        var orders = await query
            .OrderByDescending(order => order.CreatedAt)
            .Skip((filterParams.PageId - 1) * filterParams.Take)
            .Take(filterParams.Take)
            .ToListAsync();

        var result = new OrderFilterResult
        {
            Data = orders.MapDto(),
            FilterParams = filterParams,
        };

        result.GeneratePaging(total, filterParams.Take, filterParams.PageId);
        return result;
    }

    public async Task<OrderDto?> GetDetailAsync(Guid id)
    {
        var order = await BuildDetailQuery().FirstOrDefaultAsync(item => item.Id == id);
        return order?.MapDto();
    }

    public async Task<List<OrderDto>> GetUserOrdersAsync(Guid userId)
    {
        var orders = await BuildDetailQuery()
            .Where(order => order.UserId == userId)
            .OrderByDescending(order => order.CreatedAt)
            .ToListAsync();

        return orders.MapDto();
    }

    public async Task<OrderDto?> TrackAsync(string orderNumber, string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(orderNumber) || string.IsNullOrWhiteSpace(phoneNumber))
            return null;

        var normalized = SlugHelper.ToLatinDigits(orderNumber).Trim();
        var phone = SlugHelper.ToLatinDigits(phoneNumber).Trim();

        var order = await BuildDetailQuery()
            .FirstOrDefaultAsync(item => item.OrderNumber == normalized && item.CustomerPhone == phone);

        return order?.MapDto();
    }

    public async Task<OperationResult> ChangeStatusAsync(ChangeOrderStatusCommand command)
    {
        var order = await context.Orders.AsTracking()
            .Include(item => item.Items)
            .Include(item => item.StatusHistory)
            .FirstOrDefaultAsync(item => item.Id == command.OrderId && !item.IsDeleted);

        if (order is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        if (order.Status == command.Status)
            return OperationResult.Error("سفارش در همین وضعیت قرار دارد.");

        var previousStatus = order.Status;
        order.Status = command.Status;

        if (command.Status == OrderStatus.Cancelled)
        {
            order.CancelledAt = DateTimeOffset.UtcNow;
            order.CancelReason = command.Note;
        }

        // Stock is consumed when the order leaves the «pending» state and returned
        // when a paid order is cancelled or returned.
        if (command.AdjustStock)
        {
            var shouldRestore = command.Status is OrderStatus.Cancelled or OrderStatus.Returned;
            var shouldConsume = !shouldRestore && previousStatus is OrderStatus.None or OrderStatus.Pending;

            if (shouldRestore || shouldConsume)
            {
                foreach (var item in order.Items.Where(item => item.ProductId.HasValue))
                {
                    var product = await context.Products.AsTracking()
                        .FirstOrDefaultAsync(product => product.Id == item.ProductId!.Value);

                    if (product is null)
                        continue;

                    product.StockQuantity = shouldRestore
                        ? product.StockQuantity + item.Quantity
                        : Math.Max(0, product.StockQuantity - item.Quantity);

                    if (shouldRestore && product.StockQuantity > 0 && !product.IsActive)
                        product.IsActive = true;
                }
            }
        }

        context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            Status = command.Status,
            PreviousStatus = previousStatus,
            Note = command.Note,
            ChangedByUserId = command.ChangedByUserId,
            IsCustomerVisible = command.IsCustomerVisible,
        });

        await context.SaveChangesAsync();

        if (command.IsCustomerVisible && order.UserId.HasValue)
        {
            await notificationService.NotifyUserAsync(order.UserId.Value, $"تغییر وضعیت سفارش {order.OrderNumber}",
                $"وضعیت سفارش شما به «{OrderStatusTitle(command.Status)}» تغییر کرد." +
                (string.IsNullOrWhiteSpace(command.Note) ? string.Empty : $" یادداشت: {command.Note}"),
                NotificationType.Order, $"/profile/orders/{order.Id}");
        }

        return OperationResult.Success("وضعیت سفارش با موفقیت تغییر کرد.");
    }

    public async Task<OperationResult> SetTrackingAsync(SetTrackingCommand command)
    {
        var order = await context.Orders.AsTracking()
            .FirstOrDefaultAsync(item => item.Id == command.OrderId && !item.IsDeleted);

        if (order is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        if (string.IsNullOrWhiteSpace(command.TrackingCode))
            return OperationResult.Error("کد رهگیری مرسوله را وارد کنید.");

        order.TrackingCode = command.TrackingCode.Trim();
        order.TrackingUrl = command.TrackingUrl;

        var previousStatus = order.Status;
        if (order.Status is OrderStatus.Pending or OrderStatus.Confirmed or OrderStatus.Processing)
            order.Status = OrderStatus.Shipped;

        context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            Status = order.Status,
            PreviousStatus = previousStatus,
            Note = string.IsNullOrWhiteSpace(command.Note)
                ? $"کد رهگیری مرسوله: {order.TrackingCode}"
                : command.Note,
            ChangedByUserId = command.ChangedByUserId,
            IsCustomerVisible = true,
        });

        await context.SaveChangesAsync();

        if (order.UserId.HasValue)
        {
            await notificationService.NotifyUserAsync(order.UserId.Value, "ارسال سفارش",
                $"سفارش {order.OrderNumber} ارسال شد. کد رهگیری: {order.TrackingCode}",
                NotificationType.Order, $"/profile/orders/{order.Id}");
        }

        return OperationResult.Success("کد رهگیری ثبت و سفارش ارسال‌شده علامت‌گذاری شد.");
    }

    public async Task<OperationResult> SetAdminNoteAsync(Guid orderId, string? note)
    {
        var order = await context.Orders.AsTracking().FirstOrDefaultAsync(item => item.Id == orderId && !item.IsDeleted);
        if (order is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        order.AdminNote = note;
        await context.SaveChangesAsync();

        return OperationResult.Success("یادداشت داخلی سفارش ذخیره شد.");
    }

    public async Task<OperationResult> CancelByCustomerAsync(Guid orderId, Guid userId, string? reason)
    {
        var order = await context.Orders.AsTracking()
            .Include(item => item.Items)
            .FirstOrDefaultAsync(item => item.Id == orderId && item.UserId == userId && !item.IsDeleted);

        if (order is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        if (order.Status is not (OrderStatus.Pending or OrderStatus.Confirmed))
            return OperationResult.Error("این سفارش در وضعیت فعلی قابل لغو نیست؛ با پشتیبانی تماس بگیرید.");

        var previousStatus = order.Status;
        order.Status = OrderStatus.Cancelled;
        order.CancelledAt = DateTimeOffset.UtcNow;
        order.CancelReason = string.IsNullOrWhiteSpace(reason) ? "لغو توسط مشتری" : reason.Trim();

        foreach (var item in order.Items.Where(item => item.ProductId.HasValue))
        {
            var product = await context.Products.AsTracking()
                .FirstOrDefaultAsync(product => product.Id == item.ProductId!.Value);

            if (product is not null)
                product.StockQuantity += item.Quantity;
        }

        context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            Status = OrderStatus.Cancelled,
            PreviousStatus = previousStatus,
            Note = order.CancelReason,
            IsCustomerVisible = true,
        });

        await context.SaveChangesAsync();

        await notificationService.NotifyStaffAsync("لغو سفارش توسط مشتری",
            $"سفارش {order.OrderNumber} توسط مشتری لغو شد.",
            NotificationType.Order, $"/admin/orders/details/{order.Id}");

        return OperationResult.Success("سفارش شما لغو شد.");
    }

    public async Task<OrderStatsDto> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var todayStart = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

        var orders = await context.Orders
            .Select(order => new
            {
                order.Status,
                order.PaymentStatus,
                order.PayableAmount,
                order.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var paid = orders.Where(order => order.PaymentStatus == PaymentStatus.Paid).ToList();

        var statusCounts = orders
            .GroupBy(order => order.Status)
            .Select(group => new OrderStatusCountDto { Status = group.Key, Count = group.Count() })
            .OrderBy(item => item.Status)
            .ToList();

        var dailySales = new List<DailySalesStat>();
        for (var offset = 13; offset >= 0; offset--)
        {
            var day = todayStart.AddDays(-offset);
            var dayOrders = paid.Where(order => order.CreatedAt >= day && order.CreatedAt < day.AddDays(1)).ToList();
            dailySales.Add(new DailySalesStat
            {
                PersianLabel = PriceHelper.ToPersianDigits($"{day.Month}/{day.Day}"),
                Amount = dayOrders.Sum(order => order.PayableAmount),
                Count = dayOrders.Count,
            });
        }

        return new OrderStatsDto
        {
            TotalOrders = orders.Count,
            TodayOrders = orders.Count(order => order.CreatedAt >= todayStart),
            PendingOrders = orders.Count(order => order.Status is OrderStatus.Pending or OrderStatus.Confirmed or OrderStatus.Processing),
            PaidOrders = paid.Count,
            TotalRevenue = paid.Sum(order => order.PayableAmount),
            TodayRevenue = paid.Where(order => order.CreatedAt >= todayStart).Sum(order => order.PayableAmount),
            MonthRevenue = paid.Where(order => order.CreatedAt >= monthStart).Sum(order => order.PayableAmount),
            AwaitingBankTransferReview = await context.BankTransferPayments
                .CountAsync(payment => payment.Status == Data.Entities.Payments.BankTransferStatus.AwaitingReview, cancellationToken),
            StatusCounts = statusCounts,
            DailySales = dailySales,
        };
    }

    /// <summary>کوئری کامل سفارش برای صفحه‌های نمایش جزئیات.</summary>
    private IQueryable<Order> BuildDetailQuery() =>
        context.Orders
            .Include(order => order.Items)
            .ThenInclude(item => item.Product)
            .ThenInclude(product => product!.Images)
            .Include(order => order.StatusHistory)
            .ThenInclude(history => history.ChangedByUser)
            .Include(order => order.PaymentTransactions)
            .Include(order => order.BankTransferPayment)
            .ThenInclude(payment => payment!.Receipts)
            .Include(order => order.BankTransferPayment)
            .ThenInclude(payment => payment!.PaymentCard)
            .Include(order => order.Coupon)
            .Include(order => order.User)
            .AsSplitQuery();

    /// <summary>عنوان فارسی وضعیت سفارش برای پیام‌ها.</summary>
    private static string OrderStatusTitle(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "در انتظار پرداخت",
        OrderStatus.Confirmed => "تأییدشده",
        OrderStatus.Processing => "در حال آماده‌سازی",
        OrderStatus.Shipped => "ارسال‌شده",
        OrderStatus.Delivered => "تحویل‌شده",
        OrderStatus.Cancelled => "لغوشده",
        OrderStatus.Returned => "مرجوع‌شده",
        _ => "نامشخص",
    };
}