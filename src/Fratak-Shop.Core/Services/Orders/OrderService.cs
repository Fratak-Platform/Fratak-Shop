using Fratak_Shop.Core.DTOs.Orders.Command;
using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.DTOs.Shipping;
using Fratak_Shop.Core.DTOs.Wallets;
using Fratak_Shop.Core.DTOs.Marketing;
using Fratak_Shop.Core.Mappers.Orders;
using Fratak_Shop.Core.Services.Events;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shipping;
using Fratak_Shop.Core.Services.Wallets;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Wallets;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Orders;

/// <summary>
/// مدیریت سفارش‌ها: ثبت سفارش با اسنپ‌شات قیمت و آدرس، گردش وضعیت سفارش،
/// وضعیت پرداخت، کد رهگیری و بازمحاسبه مبالغ.
/// </summary>
public class OrderService(
    FratakShopDbContext context,
    IStoreSettingService settingService,
    IShippingService shippingService,
    IWalletService walletService,
    ICouponService couponService,
    IShopEvents events) : IOrderService
{
    /// <summary>مسیرهای مجاز تغییر وضعیت سفارش.</summary>
    private static readonly Dictionary<OrderStatus, OrderStatus[]> AllowedTransitions = new()
    {
        [OrderStatus.Pending] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Processing, OrderStatus.Cancelled],
        [OrderStatus.Processing] = [OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Shipped] = [OrderStatus.Delivered, OrderStatus.Returned],
        [OrderStatus.Delivered] = [OrderStatus.Returned],
        [OrderStatus.Cancelled] = [],
        [OrderStatus.Returned] = [],
    };

    public async Task<OperationResult<OrderDto>> CreateOrderAsync(CreateOrderCommand command)
    {
        var error = Validate(command);
        if (error is not null)
            return OperationResult<OrderDto>.Error(error);

        var settings = await settingService.GetAsync();

        if (command.UserId.HasValue)
        {
            var userExists = await context.Users.AnyAsync(x => x.Id == command.UserId.Value);
            if (!userExists)
                return OperationResult<OrderDto>.NotFound("کاربر مورد نظر یافت نشد.");
        }

        var paymentError = ValidatePaymentMethod(command, settings);
        if (paymentError is not null)
            return OperationResult<OrderDto>.Error(paymentError);

        // Merge duplicate lines of the same product.
        var requested = command.Items
            .GroupBy(item => item.ProductId)
            .Select(group => new OrderItemRequest { ProductId = group.Key, Quantity = group.Sum(item => item.Quantity) })
            .ToList();

        var productIds = requested.Select(item => item.ProductId).ToList();
        var products = await context.Products
            .Where(x => productIds.Contains(x.Id) && x.IsActive)
            .ToListAsync();

        if (products.Count != productIds.Count)
            return OperationResult<OrderDto>.Error("برخی از محصولات سفارش یافت نشد یا غیرفعال است.");

        var now = DateTimeOffset.UtcNow;

        var order = new Order
        {
            UserId = command.UserId,
            OrderNumber = await GenerateOrderNumberAsync(),
            Status = OrderStatus.Pending,
            PaymentMethod = command.PaymentMethod,
            PaymentStatus = PaymentStatus.Pending,
            ShippingMethod = command.ShippingMethod?.Trim(),
            RequestedDeliveryDate = command.RequestedDeliveryDate,
            DiscountAmount = command.DiscountAmount,
            ShippingAmount = command.ShippingAmount,
            CustomerName = command.CustomerName.Trim(),
            CustomerPhone = command.CustomerPhone.Trim(),
            ShippingProvince = command.ShippingProvince.Trim(),
            ShippingCity = command.ShippingCity.Trim(),
            ShippingAddress = command.ShippingAddress.Trim(),
            ShippingBuildingNumber = command.ShippingBuildingNumber?.Trim(),
            ShippingUnit = command.ShippingUnit?.Trim(),
            ShippingPostalCode = command.ShippingPostalCode.Trim(),
            ShippingPhone = command.ShippingPhone.Trim(),
            ShippingLatitude = command.ShippingLatitude.HasValue && command.ShippingLongitude.HasValue ? command.ShippingLatitude : null,
            ShippingLongitude = command.ShippingLatitude.HasValue && command.ShippingLongitude.HasValue ? command.ShippingLongitude : null,
            CustomerNote = command.CustomerNote.ToPlainText() is { Length: > 0 } note ? note : null,
        };

        foreach (var request in requested)
        {
            var product = products.First(x => x.Id == request.ProductId);

            if (product.StockQuantity < request.Quantity)
                return OperationResult<OrderDto>.Error($"موجودی محصول «{product.Name}» کافی نیست.");

            var finalUnitPrice = product.GetFinalPrice(now);

            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                SKU = product.SKU,
                UnitPrice = product.Price,
                DiscountAmount = (product.Price - finalUnitPrice) * request.Quantity,
                Quantity = request.Quantity,
                TotalAmount = finalUnitPrice * request.Quantity,
                UnitCostPrice = product.PurchasePrice,
            });
        }

        var itemsTotal = order.Items.Sum(item => item.TotalAmount);

        if (settings.MinimumOrderAmount > 0 && itemsTotal < settings.MinimumOrderAmount)
            return OperationResult<OrderDto>.Error($"حداقل مبلغ سفارش {settings.MinimumOrderAmount:N0} تومان است.");

        if (command.ShippingMethodId.HasValue)
        {
            var shipping = await shippingService.ValidateSelectionAsync(command.ShippingMethodId.Value,
                new ShippingDestination(command.ShippingProvinceId, command.ShippingCityId), itemsTotal,
                command.DeliveryDate, command.DeliveryTimeSlotId, now);

            if (!shipping.IsSuccess() || shipping.Data is null)
                return OperationResult<OrderDto>.Error(shipping.Message);

            var selection = shipping.Data;
            if (command.PaymentMethod == PaymentMethod.CashOnDelivery && !selection.SupportsCashOnDelivery)
                return OperationResult<OrderDto>.Error($"پرداخت در محل برای «{selection.ShippingMethodTitle}» امکان‌پذیر نیست.");

            order.ShippingMethodId = selection.ShippingMethodId;
            order.ShippingMethod = selection.ShippingMethodTitle;
            order.ShippingAmount = selection.Cost;
            order.DeliveryTimeSlotId = selection.DeliveryTimeSlotId;
            order.DeliveryTimeSlotTitle = selection.DeliveryTimeSlotTitle;
            order.RequestedDeliveryDate = selection.RequestedDeliveryDate;
        }

        CouponEvaluation? coupon = null;
        if (!string.IsNullOrWhiteSpace(command.CouponCode))
        {
            if (!settings.IsCouponEnabled)
                return OperationResult<OrderDto>.Error("استفاده از کد تخفیف در حال حاضر غیرفعال است.");

            var evaluation = await couponService.EvaluateAsync(new CouponEvaluationRequest
            {
                Code = command.CouponCode,
                UserId = command.UserId,
                Lines = requested.Select(item => new CouponLine(item.ProductId, item.Quantity)).ToList(),
                ShippingCost = order.ShippingAmount,
                RequireUser = true,
            });

            if (!evaluation.IsSuccess())
                return OperationResult<OrderDto>.Error(evaluation.Message).WithCode("coupon");

            coupon = evaluation.Data!;
            order.CouponId = coupon.CouponId;
            order.CouponCode = coupon.Code;
            order.CouponDiscountAmount = coupon.TotalDiscount;
            order.DiscountAmount += coupon.TotalDiscount;
        }

        var payable = CalculatePayable(order);
        if (payable < 0)
            return OperationResult<OrderDto>.Error("مبلغ قابل پرداخت سفارش نمی‌تواند منفی باشد.");

        if (command.PaymentMethod == PaymentMethod.CashOnDelivery && !settings.AllowsCashOnDelivery(payable))
            return OperationResult<OrderDto>.Error($"پرداخت در محل برای سفارش‌های بالای {settings.CashOnDeliveryMaxAmount:N0} تومان امکان‌پذیر نیست.");

        order.SubtotalAmount = order.Items.Sum(item => item.UnitPrice * item.Quantity);
        order.PayableAmount = payable;

        if (command.PaymentMethod == PaymentMethod.OnlineGateway)
            order.PaymentDeadline = now.AddMinutes(settings.UnpaidOrderTimeoutMinutes);

        await using var transaction = await context.Database.BeginTransactionAsync();

        // Reserve stock with conditional updates: two customers can never buy the last item twice.
        foreach (var item in order.Items)
        {
            var quantity = item.Quantity;
            var reserved = await context.Products
                .Where(p => p.Id == item.ProductId && p.StockQuantity >= quantity)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.StockQuantity, p => p.StockQuantity - quantity)
                    .SetProperty(p => p.SalesCount, p => p.SalesCount + quantity));

            if (reserved == 0)
            {
                await transaction.RollbackAsync();
                return OperationResult<OrderDto>.Error($"موجودی محصول «{item.ProductName}» کافی نیست.");
            }
        }

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        if (coupon is not null)
        {
            var usage = await couponService.RecordUsageAsync(coupon.CouponId, order.UserId, order.Id, coupon.TotalDiscount);
            if (!usage.IsSuccess())
            {
                await transaction.RollbackAsync();
                context.ChangeTracker.Clear();
                return OperationResult<OrderDto>.Error(usage.Message).WithCode("coupon");
            }
        }

        if (command.PaymentMethod == PaymentMethod.Wallet)
        {
            var debit = await walletService.DebitAsync(new WalletEntry
            {
                UserId = command.UserId!.Value,
                Amount = order.PayableAmount,
                Type = WalletTransactionType.OrderPayment,
                Description = $"پرداخت سفارش {order.OrderNumber}",
                OrderId = order.Id,
                DebitSource = WalletDebitSource.PromotionalFirst,
            });

            if (!debit.IsSuccess())
            {
                await transaction.RollbackAsync();
                context.ChangeTracker.Clear();
                return OperationResult<OrderDto>.Error(debit.Message);
            }

            // Remember how much promotional credit was used: a refund returns it as promotional credit again.
            order.WalletBonusAmount = Math.Min(order.PayableAmount, Math.Max(0, -debit.Data.BonusChange));
            order.PaymentStatus = PaymentStatus.Paid;
            order.Status = OrderStatus.Confirmed;
            order.PaidAt = now;
            order.TransactionId = $"WALLET-{order.OrderNumber}";
            await context.SaveChangesAsync();
        }

        await transaction.CommitAsync();

        // Stock was reserved with bulk updates: availability on cached pages must refresh.
        Fratak_Shop.Data.Utilities.DataChangeNotifier.Publish(typeof(Fratak_Shop.Data.Entities.Products.Product));

        await events.OrderPlacedAsync(order.Id);
        if (order.PaymentStatus == PaymentStatus.Paid)
            await events.OrderPaidAsync(order.Id);

        return OperationResult<OrderDto>.Success(order.Map(), $"سفارش با شماره {order.OrderNumber} با موفقیت ثبت شد.");
    }

    public async Task<OperationResult> EditOrderAsync(EditOrderCommand command)
    {
        var current = await context.Orders.FirstOrDefaultAsync(x => x.Id == command.Id);
        if (current is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        var error = Validate(command);
        if (error is not null)
            return OperationResult.Error(error);

        // Cancelling / returning goes through the cancellation workflow (stock + refund).
        var cancelled = false;
        if (command.Status != current.Status && command.Status is OrderStatus.Cancelled or OrderStatus.Returned)
        {
            var cancel = await CancelCoreAsync(command.Id, command.Status, OrderCancelledBy.Admin, null, null);
            if (!cancel.IsSuccess())
                return cancel;

            cancelled = true;
        }

        var order = await context.Orders
            .AsTracking()
            .Include(x => x.Items)
            .FirstAsync(x => x.Id == command.Id);

        var previousStatus = order.Status;
        var previousPayment = order.PaymentStatus;

        if (!cancelled)
        {
            var statusError = ValidateTransition(order.Status, command.Status);
            if (statusError is not null)
                return OperationResult.Error(statusError);

            order.Status = command.Status;

            // A refunded payment can't be turned back into "paid" from the edit form.
            if (order.PaymentStatus != PaymentStatus.Refunded)
            {
                order.PaymentStatus = command.PaymentStatus;
                order.PaidAt = command.PaymentStatus == PaymentStatus.Paid
                    ? command.PaidAt ?? order.PaidAt ?? DateTimeOffset.UtcNow
                    : command.PaidAt;
            }
        }

        order.PaymentMethod = command.PaymentMethod;
        order.TransactionId = command.TransactionId?.Trim();
        order.TrackingCode = command.TrackingCode?.Trim();
        order.ShippingMethod = command.ShippingMethod?.Trim();
        order.RequestedDeliveryDate = command.RequestedDeliveryDate;
        order.CustomerName = command.CustomerName.Trim();
        order.CustomerPhone = command.CustomerPhone.Trim();
        order.ShippingProvince = command.ShippingProvince.Trim();
        order.ShippingCity = command.ShippingCity.Trim();
        order.ShippingAddress = command.ShippingAddress.Trim();
        order.ShippingBuildingNumber = command.ShippingBuildingNumber?.Trim();
        order.ShippingUnit = command.ShippingUnit?.Trim();
        order.ShippingPostalCode = command.ShippingPostalCode.Trim();
        order.ShippingPhone = command.ShippingPhone.Trim();
        order.ShippingLatitude = command.ShippingLatitude.HasValue && command.ShippingLongitude.HasValue ? command.ShippingLatitude : null;
        order.ShippingLongitude = command.ShippingLatitude.HasValue && command.ShippingLongitude.HasValue ? command.ShippingLongitude : null;
        order.CustomerNote = command.CustomerNote.ToPlainText() is { Length: > 0 } note ? note : null;

        await context.SaveChangesAsync();

        if (!cancelled)
        {
            if (order.PaymentStatus == PaymentStatus.Paid && previousPayment != PaymentStatus.Paid)
                await events.OrderPaidAsync(order.Id);

            if (order.Status != previousStatus)
                await events.OrderStatusChangedAsync(order.Id, order.Status);
        }

        return OperationResult.Success("سفارش با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteOrderAsync(Guid id)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();

        var order = await context.Orders
            .AsTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (order is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        if (order.PaymentStatus == PaymentStatus.Paid && order.Status is not (OrderStatus.Cancelled or OrderStatus.Returned))
            return OperationResult.Error("سفارش پرداخت‌شده را ابتدا لغو کنید تا مبلغ آن به کیف پول مشتری برگردد.");

        if (order.Status is not (OrderStatus.Cancelled or OrderStatus.Returned))
            await RestoreStockAsync(order.Items);

        context.Orders.Remove(order);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        return OperationResult.Success("سفارش با موفقیت حذف شد.");
    }

    public async Task<OperationResult> ChangeOrderStatusAsync(Guid id, OrderStatus status)
    {
        if (status is OrderStatus.Cancelled or OrderStatus.Returned)
            return await CancelOrderByAdminAsync(id, status, null);

        var order = await context.Orders.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (order is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        var error = ValidateTransition(order.Status, status);
        if (error is not null)
            return OperationResult.Error(error);

        order.Status = status;
        if (status == OrderStatus.Delivered)
            order.DeliveredAt ??= DateTimeOffset.UtcNow; // starts the return / feedback windows
        await context.SaveChangesAsync();

        await events.OrderStatusChangedAsync(order.Id, status);

        return OperationResult.Success("وضعیت سفارش با موفقیت تغییر کرد.");
    }

    public async Task<OperationResult> CancelOrderByCustomerAsync(Guid orderId, Guid userId, string? reason)
    {
        var settings = await settingService.GetAsync();
        if (!settings.AllowCustomerCancellation)
            return OperationResult.Error("لغو سفارش از طریق سایت غیرفعال است؛ لطفاً با پشتیبانی تماس بگیرید.");

        return await CancelCoreAsync(orderId, OrderStatus.Cancelled, OrderCancelledBy.Customer, reason, order =>
        {
            if (order.UserId != userId)
                return "سفارش مورد نظر یافت نشد.";

            return OrderRules.IsCancellableByCustomer(order.Status)
                ? null
                : "این سفارش وارد مرحله آماده‌سازی شده و دیگر قابل لغو نیست؛ برای پیگیری با پشتیبانی تماس بگیرید.";
        });
    }

    public Task<OperationResult> CancelOrderByAdminAsync(Guid orderId, OrderStatus status, string? reason)
    {
        if (status is not (OrderStatus.Cancelled or OrderStatus.Returned))
            return Task.FromResult(OperationResult.Error("وضعیت انتخاب‌شده معتبر نیست."));

        return CancelCoreAsync(orderId, status, OrderCancelledBy.Admin, reason, null);
    }

    public async Task<int> ExpireUnpaidOrdersAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var expiredIds = await context.Orders
            .Where(o => o.Status == OrderStatus.Pending
                        && o.PaymentMethod == PaymentMethod.OnlineGateway
                        && o.PaymentStatus != PaymentStatus.Paid
                        && o.PaymentDeadline != null
                        && o.PaymentDeadline < now)
            .OrderBy(o => o.PaymentDeadline)
            .Select(o => o.Id)
            .Take(200)
            .ToListAsync(cancellationToken);

        var count = 0;
        foreach (var id in expiredIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await CancelCoreAsync(id, OrderStatus.Cancelled, OrderCancelledBy.System,
                "عدم پرداخت در مهلت مقرر", order => order.PaymentStatus == PaymentStatus.Paid ? "paid" : null);

            if (result.IsSuccess())
                count++;
        }

        return count;
    }

    public async Task<OrderPaymentOutcome> ApplyOnlinePaymentAsync(Guid orderId, decimal amount, string referenceId)
    {
        var now = DateTimeOffset.UtcNow;

        // Atomic: only a still-pending, unpaid order with the exact amount is marked as paid.
        var affected = await context.Orders
            .Where(o => o.Id == orderId
                        && o.Status == OrderStatus.Pending
                        && o.PaymentStatus != PaymentStatus.Paid
                        && o.PaymentStatus != PaymentStatus.Refunded
                        && o.PayableAmount == amount)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.PaymentStatus, PaymentStatus.Paid)
                .SetProperty(o => o.Status, OrderStatus.Confirmed)
                .SetProperty(o => o.PaymentMethod, PaymentMethod.OnlineGateway)
                .SetProperty(o => o.PaidAt, now)
                .SetProperty(o => o.TransactionId, referenceId)
                .SetProperty(o => o.UpdatedAt, now));

        if (affected == 1)
            return OrderPaymentOutcome.Paid;

        var state = await context.Orders
            .Where(o => o.Id == orderId)
            .Select(o => new { o.PaymentStatus, o.TransactionId })
            .FirstOrDefaultAsync();

        if (state is not null && state.PaymentStatus == PaymentStatus.Paid && state.TransactionId == referenceId)
            return OrderPaymentOutcome.AlreadyPaid;

        // Cancelled/expired meanwhile, paid twice, or amount mismatch: the money goes to the wallet.
        return OrderPaymentOutcome.NotPayable;
    }

    /// <summary>
    /// Cancels or returns an order: atomic status change (guards against concurrent
    /// payment / admin actions), stock release and refund of the paid amount to the wallet.
    /// </summary>
    private async Task<OperationResult> CancelCoreAsync(Guid orderId, OrderStatus targetStatus,
        OrderCancelledBy cancelledBy, string? reason, Func<Order, string?>? guard)
    {
        var ownsTransaction = context.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await context.Database.BeginTransactionAsync() : null;

        var order = await context.Orders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId);

        if (order is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        var guardError = guard?.Invoke(order);
        if (guardError is not null)
            return OperationResult.Error(guardError);

        var transitionError = ValidateTransition(order.Status, targetStatus);
        if (transitionError is not null)
            return OperationResult.Error(transitionError);

        var now = DateTimeOffset.UtcNow;
        var note = reason.ToPlainText(500) is { Length: > 0 } text ? text : null;
        var loadedStatus = order.Status;
        var loadedPayment = order.PaymentStatus;

        var affected = await context.Orders
            .Where(o => o.Id == orderId && o.Status == loadedStatus && o.PaymentStatus == loadedPayment)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, targetStatus)
                .SetProperty(o => o.CancelledAt, now)
                .SetProperty(o => o.CancelledBy, (OrderCancelledBy?)cancelledBy)
                .SetProperty(o => o.CancellationReason, note)
                .SetProperty(o => o.UpdatedAt, now));

        if (affected == 0)
        {
            if (transaction is not null)
                await transaction.RollbackAsync();

            return OperationResult.Error("وضعیت سفارش هم‌زمان تغییر کرده است؛ صفحه را تازه کنید و دوباره تلاش کنید.");
        }

        await RestoreStockAsync(order.Items);
        await couponService.ReleaseUsageAsync(order.Id);

        var refundable = order.PayableAmount - order.RefundedAmount;
        var refunded = false;

        // Promotional credit spent on the order comes back as promotional credit (never withdrawable).
        var refundableBonus = Math.Clamp(order.WalletBonusAmount - order.RefundedBonusAmount, 0, Math.Max(0, refundable));

        if (loadedPayment == PaymentStatus.Paid && refundable > 0 && order.UserId.HasValue)
        {
            var credit = await walletService.CreditAsync(new WalletEntry
            {
                UserId = order.UserId.Value,
                Amount = refundable,
                NonWithdrawableAmount = refundableBonus,
                Type = WalletTransactionType.OrderRefund,
                Description = targetStatus == OrderStatus.Returned
                    ? $"بازگشت وجه سفارش مرجوعی {order.OrderNumber}"
                    : $"بازگشت وجه سفارش لغوشده {order.OrderNumber}",
                OrderId = order.Id,
            });

            if (!credit.IsSuccess())
            {
                if (transaction is not null)
                    await transaction.RollbackAsync();

                return OperationResult.Error(credit.Message);
            }

            await context.Orders
                .Where(o => o.Id == orderId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.PaymentStatus, PaymentStatus.Refunded)
                    .SetProperty(o => o.RefundedAmount, o => o.RefundedAmount + refundable)
                    .SetProperty(o => o.RefundedBonusAmount, o => o.RefundedBonusAmount + refundableBonus)
                    .SetProperty(o => o.RefundedAt, now));

            refunded = true;
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync();
            await events.OrderStatusChangedAsync(order.Id, targetStatus);
        }

        var action = targetStatus == OrderStatus.Returned ? "مرجوع" : "لغو";
        return OperationResult.Success(refunded
            ? refundableBonus > 0
                ? $"سفارش {action} شد و مبلغ {refundable:N0} تومان به کیف پول برگشت داده شد ({refundableBonus:N0} تومان آن اعتبار هدیه و فقط برای خرید قابل استفاده است)."
                : $"سفارش {action} شد و مبلغ {refundable:N0} تومان به کیف پول برگشت داده شد."
            : $"سفارش {order.OrderNumber} {action} شد.");
    }

    public async Task<OperationResult> ChangePaymentStatusAsync(Guid id, PaymentStatus paymentStatus,
        string? transactionId = null)
    {
        if (paymentStatus == PaymentStatus.None)
            return OperationResult.Error("وضعیت پرداخت را انتخاب کنید.");

        var order = await context.Orders.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (order is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        var wasPaid = order.PaymentStatus == PaymentStatus.Paid;
        order.PaymentStatus = paymentStatus;

        if (paymentStatus == PaymentStatus.Paid)
            order.PaidAt ??= DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(transactionId))
            order.TransactionId = transactionId.Trim();

        await context.SaveChangesAsync();

        if (paymentStatus == PaymentStatus.Paid && !wasPaid)
            await events.OrderPaidAsync(order.Id);

        return OperationResult.Success("وضعیت پرداخت سفارش با موفقیت به‌روزرسانی شد.");
    }

    public async Task<OperationResult> SetTrackingCodeAsync(Guid id, string trackingCode)
    {
        if (string.IsNullOrWhiteSpace(trackingCode))
            return OperationResult.Error("کد رهگیری مرسوله را وارد کنید.");

        var order = await context.Orders.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (order is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        order.TrackingCode = trackingCode.Trim();

        // ثبت کد رهگیری یعنی سفارش تحویل پست/پیک شده است.
        var shipped = order.Status is OrderStatus.Confirmed or OrderStatus.Processing;
        if (shipped)
            order.Status = OrderStatus.Shipped;

        await context.SaveChangesAsync();

        if (shipped)
            await events.OrderStatusChangedAsync(order.Id, OrderStatus.Shipped);

        return OperationResult.Success("کد رهگیری مرسوله با موفقیت ثبت شد.");
    }

    public async Task<OperationResult> RecalculateTotalsAsync(Guid orderId)
    {
        var order = await context.Orders
            .AsTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId);

        if (order is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        var payable = CalculatePayable(order);
        if (payable < 0)
            return OperationResult.Error("مبلغ قابل پرداخت سفارش نمی‌تواند منفی باشد.");

        order.SubtotalAmount = order.Items.Sum(item => item.UnitPrice * item.Quantity);
        order.PayableAmount = payable;

        await context.SaveChangesAsync();

        return OperationResult.Success("مبالغ سفارش بازمحاسبه شد.");
    }

    public async Task<OrderFilterResult> GetOrdersByFilterAsync(OrderFilterParams filterParams)
    {
        var query = context.Orders
            .Include(x => x.Items)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filterParams.Search))
        {
            var search = filterParams.Search.Trim();
            query = query.Where(x => x.OrderNumber.Contains(search)
                                     || x.CustomerName.Contains(search)
                                     || x.CustomerPhone.Contains(search));
        }

        if (filterParams.Status.HasValue)
            query = query.Where(x => x.Status == filterParams.Status.Value);

        if (filterParams.PaymentStatus.HasValue)
            query = query.Where(x => x.PaymentStatus == filterParams.PaymentStatus.Value);

        if (filterParams.UserId.HasValue)
            query = query.Where(x => x.UserId == filterParams.UserId.Value);

        if (filterParams.FromDate.HasValue)
            query = query.Where(x => x.CreatedAt >= filterParams.FromDate.Value);

        if (filterParams.ToDate.HasValue)
        {
            var toDate = filterParams.ToDate.Value;
            var endOfDay = new DateTimeOffset(toDate.Date.AddDays(1).AddTicks(-1), toDate.Offset);
            query = query.Where(x => x.CreatedAt <= endOfDay);
        }

        query = query.OrderByDescending(x => x.CreatedAt);

        var skip = (filterParams.PageId - 1) * filterParams.Take;

        var orders = await query
            .Skip(skip)
            .Take(filterParams.Take)
            .ToListAsync();

        var result = new OrderFilterResult
        {
            Data = orders.MapFilterData(),
            FilterParams = filterParams,
        };

        result.GeneratePaging(query, filterParams.Take, filterParams.PageId);

        return result;
    }

    public async Task<OrderDto?> GetOrderByIdAsync(Guid id)
    {
        var order = await context.Orders
            .Include(x => x.Items)
            .ThenInclude(x => x.Product)
            .ThenInclude(x => x!.Images)
            .FirstOrDefaultAsync(x => x.Id == id);

        return order?.Map();
    }

    public async Task<OrderDto?> GetOrderByNumberAsync(string orderNumber)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
            return null;

        var order = await context.Orders
            .Include(x => x.Items)
            .ThenInclude(x => x.Product)
            .ThenInclude(x => x!.Images)
            .FirstOrDefaultAsync(x => x.OrderNumber == orderNumber.Trim());

        return order?.Map();
    }

    public async Task<OrderDto?> TrackOrderAsync(string orderNumber, string phoneNumber)
    {
        var number = orderNumber?.Trim().ToUpperInvariant();
        var phone = phoneNumber?.Trim();
        if (string.IsNullOrWhiteSpace(number) || number.Length > 30 || string.IsNullOrWhiteSpace(phone) || !phone.IsValidMobile())
            return null;

        var order = await context.Orders
            .Include(x => x.Items)
            .ThenInclude(x => x.Product)
            .ThenInclude(x => x!.Images)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.OrderNumber == number
                                      && (x.CustomerPhone == phone || x.ShippingPhone == phone || (x.User != null && x.User.PhoneNumber == phone)));

        return order?.Map();
    }

    public async Task<List<OrderFilterData>> GetOrdersByUserAsync(Guid userId)
    {
        var orders = await context.Orders
            .Where(x => x.UserId == userId)
            .Include(x => x.Items)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        return orders.MapFilterData();
    }

    public async Task<List<OrderFilterData>> GetOrdersAsync()
    {
        var orders = await context.Orders
            .Include(x => x.Items)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        return orders.MapFilterData();
    }

    /// <summary>اعتبارسنجی مسیر تغییر وضعیت سفارش.</summary>
    private static string? ValidateTransition(OrderStatus current, OrderStatus newStatus)
    {
        if (newStatus == OrderStatus.None)
            return "وضعیت سفارش را انتخاب کنید.";

        if (current == newStatus)
            return null;

        if (!AllowedTransitions.TryGetValue(current, out var allowed) || !allowed.Contains(newStatus))
            return "تغییر وضعیت سفارش از این مرحله امکان‌پذیر نیست.";

        return null;
    }

    /// <summary>Returns the reserved stock of the items (atomic SQL updates).</summary>
    private async Task RestoreStockAsync(IEnumerable<OrderItem> items)
    {
        var quantities = items
            .Where(item => item.ProductId.HasValue)
            .GroupBy(item => item.ProductId!.Value)
            .Select(group => (ProductId: group.Key, Quantity: group.Sum(item => item.Quantity)));

        foreach (var (productId, quantity) in quantities)
        {
            await context.Products
                .IgnoreQueryFilters()
                .Where(p => p.Id == productId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.StockQuantity, p => p.StockQuantity + quantity)
                    .SetProperty(p => p.SalesCount, p => p.SalesCount >= quantity ? p.SalesCount - quantity : 0));
        }

        // Bulk updates bypass SaveChanges: tell the caches (availability shown on pages) explicitly.
        Fratak_Shop.Data.Utilities.DataChangeNotifier.Publish(typeof(Fratak_Shop.Data.Entities.Products.Product));
    }

    private static string? ValidatePaymentMethod(CreateOrderCommand command, StoreSettingDto settings)
    {
        return command.PaymentMethod switch
        {
            PaymentMethod.OnlineGateway when !settings.HasZarinPal => "پرداخت اینترنتی در حال حاضر فعال نیست.",
            PaymentMethod.CashOnDelivery when !settings.IsCashOnDeliveryEnabled => "پرداخت در محل در حال حاضر فعال نیست.",
            PaymentMethod.Wallet when !settings.CanPayWithWallet => "پرداخت با کیف پول در حال حاضر فعال نیست.",
            PaymentMethod.Wallet when !command.UserId.HasValue => "برای پرداخت با کیف پول وارد حساب کاربری شوید.",
            PaymentMethod.BankTransfer => "روش پرداخت انتخاب‌شده پشتیبانی نمی‌شود.",
            _ => null,
        };
    }

    private static decimal CalculatePayable(Order order)
    {
        var subtotal = order.Items.Sum(item => item.UnitPrice * item.Quantity);
        var itemsDiscount = order.Items.Sum(item => item.DiscountAmount);

        return subtotal - itemsDiscount - order.DiscountAmount + order.ShippingAmount;
    }

    private async Task<string> GenerateOrderNumberAsync()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var orderNumber = $"FR-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(100000, 999999)}";

            var exists = await context.Orders
                .IgnoreQueryFilters()
                .AnyAsync(x => x.OrderNumber == orderNumber);

            if (!exists)
                return orderNumber;
        }

        return $"FR-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
    }

    private static string? Validate(CreateOrderCommand command)
    {
        if (command.Items.Count == 0)
            return "سفارش باید حداقل یک محصول داشته باشد.";

        if (command.Items.Any(item => item.ProductId == Guid.Empty))
            return "شناسه محصول انتخابی معتبر نیست.";

        if (command.Items.Any(item => item.Quantity <= 0))
            return "تعداد هر محصول باید بزرگ‌تر از صفر باشد.";

        if (command.PaymentMethod == PaymentMethod.None)
            return "روش پرداخت را انتخاب کنید.";

        if (command.DiscountAmount < 0)
            return "مبلغ تخفیف سفارش نمی‌تواند منفی باشد.";

        if (command.ShippingAmount < 0)
            return "هزینه ارسال نمی‌تواند منفی باشد.";

        if (command.UserId.HasValue && command.UserId.Value == Guid.Empty)
            return "شناسه کاربر معتبر نیست.";

        return ValidateDelivery(command.CustomerName, command.CustomerPhone, command.ShippingProvince,
            command.ShippingCity, command.ShippingAddress, command.ShippingPostalCode, command.ShippingPhone);
    }

    private static string? Validate(EditOrderCommand command)
    {
        if (command.PaymentMethod == PaymentMethod.None)
            return "روش پرداخت را انتخاب کنید.";

        return ValidateDelivery(command.CustomerName, command.CustomerPhone, command.ShippingProvince,
            command.ShippingCity, command.ShippingAddress, command.ShippingPostalCode, command.ShippingPhone);
    }

    private static string? ValidateDelivery(string? customerName, string? customerPhone, string? province,
        string? city, string? address, string? postalCode, string? shippingPhone)
    {
        if (string.IsNullOrWhiteSpace(customerName))
            return "نام گیرنده سفارش را وارد کنید.";

        if (string.IsNullOrWhiteSpace(province))
            return "استان مقصد را وارد کنید.";

        if (string.IsNullOrWhiteSpace(city))
            return "شهر مقصد را وارد کنید.";

        if (string.IsNullOrWhiteSpace(address))
            return "نشانی کامل پستی را وارد کنید.";

        var normalizedPostalCode = postalCode?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedPostalCode)
            || normalizedPostalCode.Length != 10
            || !normalizedPostalCode.All(char.IsDigit))
            return "کد پستی باید ۱۰ رقم باشد.";

        if (string.IsNullOrWhiteSpace(customerPhone) || !IsValidPhoneNumber(customerPhone))
            return "شماره تماس خریدار معتبر نیست.";

        if (string.IsNullOrWhiteSpace(shippingPhone) || !IsValidPhoneNumber(shippingPhone))
            return "شماره تماس گیرنده سفارش معتبر نیست.";

        return null;
    }

    /// <summary>شماره موبایل (۰۹xxxxxxxxx) یا تلفن ثابت (۰۲۱xxxxxxxx) را می‌پذیرد.</summary>
    private static bool IsValidPhoneNumber(string phoneNumber)
    {
        var phone = phoneNumber.Trim();

        return phone.IsValidMobile() || phone.IsValidPhone();
    }
}
