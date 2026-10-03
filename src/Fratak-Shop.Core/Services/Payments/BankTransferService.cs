using Fratak_Shop.Core.DTOs.Payments.Command;
using Fratak_Shop.Core.DTOs.Payments.Query;
using Fratak_Shop.Core.Services.Media;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Payments;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Payments;

public class BankTransferService(
    FratakShopDbContext context,
    ISiteSettingService settingService,
    INotificationService notificationService,
    IMediaService mediaService) : IBankTransferService
{
    // ------------------------------------------------------------------ cards

    public async Task<List<PaymentCardDto>> GetCardsAsync(bool onlyActive = false)
    {
        var query = context.PaymentCards.AsQueryable();
        if (onlyActive)
            query = query.Where(card => card.IsActive);

        var cards = await query
            .OrderByDescending(card => card.IsDefault)
            .ThenBy(card => card.DisplayOrder)
            .ToListAsync();

        return cards.Select(MapCard).ToList();
    }

    public async Task<PaymentCardDto?> GetCardAsync(Guid id)
    {
        var card = await context.PaymentCards.FirstOrDefaultAsync(item => item.Id == id);
        return card is null ? null : MapCard(card);
    }

    public async Task<OperationResult> SaveCardAsync(Guid? id, PaymentCardDto model)
    {
        if (string.IsNullOrWhiteSpace(model.BankName))
            return OperationResult.Error("نام بانک را وارد کنید.");

        var cardNumber = SlugHelper.ToLatinDigits(model.CardNumber).Replace("-", string.Empty).Replace(" ", string.Empty).Trim();
        if (!cardNumber.IsValidCardNumber())
            return OperationResult.Error("شماره کارت باید ۱۶ رقم و بدون حرف باشد.");

        if (string.IsNullOrWhiteSpace(model.AccountHolder))
            return OperationResult.Error("نام صاحب حساب را وارد کنید.");

        if (!string.IsNullOrWhiteSpace(model.ShebaNumber) && !model.ShebaNumber.Trim().IsValidShabaNumber())
            return OperationResult.Error("شماره شبا باید با IR شروع شود و ۲۴ رقم داشته باشد.");

        if (id.HasValue)
        {
            var card = await context.PaymentCards.AsTracking().FirstOrDefaultAsync(item => item.Id == id.Value);
            if (card is null)
                return OperationResult.NotFound("کارت مورد نظر یافت نشد.");

            var duplicate = await context.PaymentCards.AnyAsync(item => item.Id != id.Value && item.CardNumber == cardNumber);
            if (duplicate)
                return OperationResult.Error("این شماره کارت قبلاً ثبت شده است.");

            card.BankName = model.BankName.Trim();
            card.CardNumber = cardNumber;
            card.AccountHolder = model.AccountHolder.Trim();
            card.ShebaNumber = model.ShebaNumber?.Trim().ToUpperInvariant();
            card.BranchName = model.BranchName;
            card.Instructions = model.Instructions;
            card.DisplayOrder = model.DisplayOrder;
            card.IsActive = model.IsActive;

            await ApplyDefaultCardAsync(card, model.IsDefault);
            await context.SaveChangesAsync();

            return OperationResult.Success("کارت با موفقیت ویرایش شد.");
        }

        var exists = await context.PaymentCards.AnyAsync(item => item.CardNumber == cardNumber);
        if (exists)
            return OperationResult.Error("این شماره کارت قبلاً ثبت شده است.");

        var newCard = new PaymentCard
        {
            BankName = model.BankName.Trim(),
            CardNumber = cardNumber,
            AccountHolder = model.AccountHolder.Trim(),
            ShebaNumber = model.ShebaNumber?.Trim().ToUpperInvariant(),
            BranchName = model.BranchName,
            Instructions = model.Instructions,
            DisplayOrder = model.DisplayOrder,
            IsActive = model.IsActive,
            IsDefault = model.IsDefault,
        };

        if (!await context.PaymentCards.AnyAsync())
            newCard.IsDefault = true;

        context.PaymentCards.Add(newCard);
        await ApplyDefaultCardAsync(newCard, model.IsDefault);
        await context.SaveChangesAsync();

        return OperationResult.Success("کارت با موفقیت ثبت شد.");
    }

    public async Task<OperationResult> ToggleCardAsync(Guid id)
    {
        var card = await context.PaymentCards.AsTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (card is null)
            return OperationResult.NotFound("کارت مورد نظر یافت نشد.");

        card.IsActive = !card.IsActive;
        await context.SaveChangesAsync();

        return OperationResult.Success(card.IsActive ? "کارت فعال شد." : "کارت غیرفعال شد.");
    }

    public async Task<OperationResult> DeleteCardAsync(Guid id)
    {
        var card = await context.PaymentCards.AsTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (card is null)
            return OperationResult.NotFound("کارت مورد نظر یافت نشد.");

        var used = await context.BankTransferPayments.AnyAsync(payment => payment.PaymentCardId == id);
        if (used)
        {
            card.IsActive = false;
            await context.SaveChangesAsync();
            return OperationResult.Success("این کارت در واریزهای قبلی استفاده شده است؛ به‌جای حذف غیرفعال شد.");
        }

        context.PaymentCards.Remove(card);
        await context.SaveChangesAsync();
        return OperationResult.Success("کارت حذف شد.");
    }

    // ------------------------------------------------------------- storefront

    public async Task<CardToCardInstructionsDto> GetInstructionsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetStorefrontSettingsAsync();

        var cards = await context.PaymentCards
            .Where(card => card.IsActive)
            .OrderByDescending(card => card.IsDefault)
            .ThenBy(card => card.DisplayOrder)
            .ToListAsync(cancellationToken);

        var deadline = int.TryParse(SlugHelper.ToLatinDigits(settings.BankTransferDeadlineHours ?? "24"), out var hours) && hours > 0
            ? hours
            : 24;

        return new CardToCardInstructionsDto
        {
            Cards = cards.Select(MapCard).ToList(),
            Instructions = settings.PaymentInstructions,
            DeadlineHours = deadline,
        };
    }

    public async Task<BankTransferDetailDto?> EnsureForOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await context.Orders.FirstOrDefaultAsync(item => item.Id == orderId, cancellationToken);
        if (order is null)
            return null;

        var payment = await context.BankTransferPayments.AsTracking()
            .Include(item => item.Receipts)
            .Include(item => item.PaymentCard)
            .Include(item => item.Order)
            .FirstOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);

        if (payment is null)
        {
            var defaultCard = await context.PaymentCards
                .Where(card => card.IsActive)
                .OrderByDescending(card => card.IsDefault)
                .ThenBy(card => card.DisplayOrder)
                .FirstOrDefaultAsync(cancellationToken);

            payment = new BankTransferPayment
            {
                OrderId = order.Id,
                PaymentCardId = defaultCard?.Id,
                Amount = order.PayableAmount,
                PayerName = order.CustomerName,
                PayerCardNumber = "0000000000000000",
                Status = BankTransferStatus.AwaitingCustomerInfo,
            };

            context.BankTransferPayments.Add(payment);
            await context.SaveChangesAsync(cancellationToken);

            payment.Order = order;
            payment.PaymentCard = defaultCard;
        }

        return MapDetail(payment);
    }

    public async Task<BankTransferDetailDto?> GetForOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var payment = await context.BankTransferPayments
            .Include(item => item.Receipts)
            .Include(item => item.PaymentCard)
            .Include(item => item.Order)
            .FirstOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);

        return payment is null ? null : MapDetail(payment);
    }

    public async Task<OperationResult> SubmitAsync(SubmitBankTransferCommand command, CancellationToken cancellationToken = default)
    {
        var payment = await context.BankTransferPayments.AsTracking()
            .Include(item => item.Receipts)
            .Include(item => item.Order)
            .FirstOrDefaultAsync(item => item.OrderId == command.OrderId, cancellationToken);

        if (payment is null)
            return OperationResult.NotFound("واریز کارت به کارت این سفارش یافت نشد.");

        if (payment.Status == BankTransferStatus.Approved)
            return OperationResult.Error("این واریز قبلاً تأیید شده است.");

        if (string.IsNullOrWhiteSpace(command.PayerName))
            return OperationResult.Error("نام واریزکننده را وارد کنید.");

        var payerCard = SlugHelper.ToLatinDigits(command.PayerCardNumber).Replace("-", string.Empty).Replace(" ", string.Empty).Trim();
        if (!payerCard.IsValidCardNumber())
            return OperationResult.Error("شماره کارت واریزکننده باید ۱۶ رقم باشد.");

        if (command.Receipts.Count == 0)
            return OperationResult.Error("تصویر یا فایل فیش واریز را بارگذاری کنید.");

        payment.PayerName = command.PayerName.Trim();
        payment.PayerCardNumber = payerCard;
        payment.PayerBankName = command.PayerBankName;
        payment.TrackingNumber = command.TrackingNumber;
        payment.TransferDate = command.TransferDate;
        payment.CustomerNote = command.CustomerNote;
        payment.Status = BankTransferStatus.AwaitingReview;
        payment.SubmittedAt = DateTimeOffset.UtcNow;
        payment.RejectReason = null;

        if (command.PaymentCardId.HasValue)
            payment.PaymentCardId = command.PaymentCardId.Value;

        foreach (var receipt in command.Receipts)
        {
            payment.Receipts.Add(new BankTransferReceipt
            {
                BankTransferPaymentId = payment.Id,
                FileName = receipt.FileName,
                OriginalName = receipt.OriginalName,
                ContentType = receipt.ContentType,
                SizeBytes = receipt.SizeBytes,
            });
        }

        if (payment.Order is not null)
            payment.Order.PaymentStatus = PaymentStatus.Pending;

        await context.SaveChangesAsync(cancellationToken);

        await notificationService.NotifyStaffAsync("واریز کارت به کارت جدید",
            $"مشتری {payment.PayerName} برای سفارش {payment.Order?.OrderNumber} فیش واریز ثبت کرد و در انتظار بررسی است.",
            NotificationType.BankTransfer, $"/admin/payments/bank-transfers", cancellationToken);

        return OperationResult.Success("اطلاعات واریز ثبت شد و پس از تأیید کارشناس، سفارش شما پردازش می‌شود.");
    }

    // -------------------------------------------------------------- admin list

    public async Task<BankTransferFilterResult> GetFilterAsync(BankTransferFilterParams filterParams)
    {
        var query = context.BankTransferPayments
            .Include(item => item.Receipts)
            .Include(item => item.PaymentCard)
            .Include(item => item.Order)
            .AsQueryable();

        if (filterParams.Status.HasValue)
            query = query.Where(item => item.Status == filterParams.Status.Value);

        if (!string.IsNullOrWhiteSpace(filterParams.Search))
        {
            var term = filterParams.Search.Trim();
            query = query.Where(item => item.PayerName.Contains(term) ||
                                        (item.TrackingNumber != null && item.TrackingNumber.Contains(term)) ||
                                        (item.Order != null && item.Order.OrderNumber.Contains(term)));
        }

        var total = await query.CountAsync();
        var payments = await query
            .OrderBy(item => item.Status == BankTransferStatus.AwaitingReview ? 0 : 1)
            .ThenByDescending(item => item.SubmittedAt ?? item.CreatedAt)
            .Skip((filterParams.PageId - 1) * filterParams.Take)
            .Take(filterParams.Take)
            .ToListAsync();

        var result = new BankTransferFilterResult
        {
            Data = payments.Select(MapDetail).ToList(),
            FilterParams = filterParams,
        };

        result.GeneratePaging(total, filterParams.Take, filterParams.PageId);
        return result;
    }

    public async Task<BankTransferDetailDto?> GetAsync(Guid id)
    {
        var payment = await context.BankTransferPayments
            .Include(item => item.Receipts)
            .Include(item => item.PaymentCard)
            .Include(item => item.Order)
            .FirstOrDefaultAsync(item => item.Id == id);

        return payment is null ? null : MapDetail(payment);
    }

    public async Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default) =>
        await context.BankTransferPayments.CountAsync(item => item.Status == BankTransferStatus.AwaitingReview, cancellationToken);

    public async Task<OperationResult> ReviewAsync(ReviewBankTransferCommand command, CancellationToken cancellationToken = default)
    {
        var payment = await context.BankTransferPayments.AsTracking()
            .Include(item => item.Receipts)
            .Include(item => item.Order)
            .FirstOrDefaultAsync(item => item.Id == command.PaymentId, cancellationToken);

        if (payment is null)
            return OperationResult.NotFound("واریز مورد نظر یافت نشد.");

        if (payment.Status is BankTransferStatus.Approved or BankTransferStatus.Expired)
            return OperationResult.Error("این واریز قابل بررسی نیست.");

        var now = DateTimeOffset.UtcNow;
        payment.ReviewedAt = now;
        payment.ReviewedByUserId = command.ReviewerUserId;
        payment.AdminNote = command.AdminNote;

        if (command.Approve)
        {
            payment.Status = BankTransferStatus.Approved;

            var order = payment.Order;
            if (order is not null)
            {
                var previousStatus = order.Status;
                order.PaymentStatus = PaymentStatus.Paid;
                order.PaidAt = now;
                order.TransactionId = string.IsNullOrWhiteSpace(payment.TrackingNumber)
                    ? $"BT-{payment.Id.ToString()[..8]}"
                    : payment.TrackingNumber;
                if (order.Status is OrderStatus.None or OrderStatus.Pending)
                    order.Status = OrderStatus.Confirmed;

                context.OrderStatusHistories.Add(new OrderStatusHistory
                {
                    OrderId = order.Id,
                    Status = order.Status,
                    PreviousStatus = previousStatus,
                    Note = "واریز کارت به کارت تأیید شد و پرداخت ثبت گردید.",
                    ChangedByUserId = command.ReviewerUserId,
                    IsCustomerVisible = true,
                });
            }

            await context.SaveChangesAsync(cancellationToken);

            if (payment.Order?.UserId is { } userId)
            {
                await notificationService.NotifyUserAsync(userId, "تأیید واریز کارت به کارت",
                    $"واریز شما برای سفارش {payment.Order.OrderNumber} تأیید شد و سفارش در حال آماده‌سازی است.",
                    NotificationType.BankTransfer, $"/profile/orders/{payment.OrderId}", cancellationToken);
            }

            return OperationResult.Success("واریز تأیید شد و پرداخت سفارش ثبت گردید.");
        }

        if (string.IsNullOrWhiteSpace(command.RejectReason))
            return OperationResult.Error("دلیل رد واریز را وارد کنید.");

        payment.Status = BankTransferStatus.Rejected;
        payment.RejectReason = command.RejectReason.Trim();

        if (payment.Order is not null)
            payment.Order.PaymentStatus = PaymentStatus.None;

        await context.SaveChangesAsync(cancellationToken);

        if (payment.Order?.UserId is { } rejectedUserId)
        {
            await notificationService.NotifyUserAsync(rejectedUserId, "رد واریز کارت به کارت",
                $"فیش ارسال‌شده برای سفارش {payment.Order.OrderNumber} تأیید نشد. دلیل: {payment.RejectReason} — می‌توانید فیش صحیح را مجدداً ارسال کنید.",
                NotificationType.BankTransfer, $"/payment/bank-transfer?orderId={payment.OrderId}", cancellationToken);
        }

        return OperationResult.Success("واریز رد شد و نتیجه به مشتری اطلاع داده شد.");
    }

    public async Task<int> ExpireOverdueAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetStorefrontSettingsAsync();
        var deadlineHours = int.TryParse(SlugHelper.ToLatinDigits(settings.BankTransferDeadlineHours ?? "24"), out var hours) && hours > 0
            ? hours
            : 24;

        var deadline = DateTimeOffset.UtcNow.AddHours(-deadlineHours);

        var overdue = await context.BankTransferPayments.AsTracking()
            .Include(item => item.Order)
            .ThenInclude(order => order!.Items)
            .Where(item => item.Status == BankTransferStatus.AwaitingCustomerInfo && item.CreatedAt < deadline)
            .ToListAsync(cancellationToken);

        foreach (var payment in overdue)
        {
            payment.Status = BankTransferStatus.Expired;

            var order = payment.Order;
            if (order is null || order.Status == OrderStatus.Cancelled)
                continue;

            var previousStatus = order.Status;
            order.Status = OrderStatus.Cancelled;
            order.CancelledAt = DateTimeOffset.UtcNow;
            order.CancelReason = "عدم ثبت اطلاعات واریز کارت به کارت در مهلت مقرر";

            foreach (var item in order.Items.Where(item => item.ProductId.HasValue))
            {
                var product = await context.Products.AsTracking()
                    .FirstOrDefaultAsync(product => product.Id == item.ProductId!.Value, cancellationToken);

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

            if (order.UserId.HasValue)
            {
                await notificationService.NotifyUserAsync(order.UserId.Value, "لغو سفارش",
                    $"سفارش {order.OrderNumber} به دلیل عدم ثبت اطلاعات واریز در مهلت مقرر لغو شد.",
                    NotificationType.BankTransfer, $"/profile/orders/{order.Id}", cancellationToken);
            }
        }

        if (overdue.Count > 0)
            await context.SaveChangesAsync(cancellationToken);

        return overdue.Count;
    }

    // ---------------------------------------------------------------- internals

    /// <summary>فقط یک کارت می‌تواند پیشنهاد پیش‌فرض صفحه پرداخت باشد.</summary>
    private async Task ApplyDefaultCardAsync(PaymentCard card, bool makeDefault)
    {
        if (!makeDefault)
            return;

        var others = await context.PaymentCards.AsTracking()
            .Where(item => item.Id != card.Id && item.IsDefault)
            .ToListAsync();

        foreach (var item in others)
            item.IsDefault = false;

        card.IsDefault = true;
    }

    private static PaymentCardDto MapCard(PaymentCard card) => new()
    {
        Id = card.Id,
        CreatedAt = card.CreatedAt,
        UpdatedAt = card.UpdatedAt,
        BankName = card.BankName,
        CardNumber = card.CardNumber,
        AccountHolder = card.AccountHolder,
        ShebaNumber = card.ShebaNumber,
        BranchName = card.BranchName,
        Instructions = card.Instructions,
        IsDefault = card.IsDefault,
        DisplayOrder = card.DisplayOrder,
        IsActive = card.IsActive,
    };

    private BankTransferDetailDto MapDetail(BankTransferPayment payment) => new()
    {
        Id = payment.Id,
        CreatedAt = payment.CreatedAt,
        UpdatedAt = payment.UpdatedAt,
        OrderId = payment.OrderId,
        OrderNumber = payment.Order?.OrderNumber ?? string.Empty,
        CustomerName = payment.Order?.CustomerName ?? string.Empty,
        CustomerPhone = payment.Order?.CustomerPhone ?? string.Empty,
        OrderPayableAmount = payment.Order?.PayableAmount ?? payment.Amount,
        Amount = payment.Amount,
        Status = payment.Status,
        PayerName = payment.PayerName,
        PayerCardNumber = payment.PayerCardNumber,
        PayerBankName = payment.PayerBankName,
        TrackingNumber = payment.TrackingNumber,
        TransferDate = payment.TransferDate,
        SubmittedAt = payment.SubmittedAt,
        ReviewedAt = payment.ReviewedAt,
        RejectReason = payment.RejectReason,
        AdminNote = payment.AdminNote,
        CustomerNote = payment.CustomerNote,
        CardNumber = payment.PaymentCard?.CardNumber,
        BankName = payment.PaymentCard?.BankName,
        ReceiptFiles = payment.Receipts
            .Select(receipt => mediaService.GetUrl(MediaFolders.BankTransfers, receipt.FileName))
            .ToList(),
    };
}
