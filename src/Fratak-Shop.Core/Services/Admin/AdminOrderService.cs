using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Payments;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Admin;

/// <summary>Order management of the admin panel: lists, details, internal notes, workflow and purchase invoices.</summary>
public interface IAdminOrderService
{
    Task<AdminOrderList> GetListAsync(AdminOrderFilter filter);

    /// <summary>All matching orders (for exports, max 50 000).</summary>
    Task<List<AdminOrderRow>> GetExportRowsAsync(AdminOrderFilter filter);

    Task<AdminOrderDetail?> GetDetailAsync(Guid id);

    Task<Guid?> FindIdByNumberAsync(string orderNumber);

    Task<OperationResult> AddNoteAsync(Guid orderId, string? content, Guid? authorId, string authorName, OrderNoteKind kind = OrderNoteKind.Note);

    /// <summary>Changes the status through the normal workflow (stock, refunds, notifications) and logs it on the timeline.</summary>
    Task<OperationResult> ChangeStatusAsync(Guid orderId, OrderStatus status, string? note, Guid? actorId, string actorName);

    Task<OperationResult> SetTrackingCodeAsync(Guid orderId, string? trackingCode, Guid? actorId, string actorName);

    Task<OperationResult> MarkCashPaidAsync(Guid orderId, string? reference, Guid? actorId, string actorName);

    Task<OperationResult<Guid>> AddPurchaseInvoiceAsync(AddPurchaseInvoiceCommand command);

    /// <summary>Deletes the invoice row; returns the stored file name so the caller can remove the file.</summary>
    Task<OperationResult<string>> DeletePurchaseInvoiceAsync(Guid invoiceId, Guid? actorId, string actorName);

    Task<PurchaseInvoiceDto?> GetPurchaseInvoiceAsync(Guid invoiceId);

    Task<PagedList<PurchaseInvoiceDto>> GetPurchaseInvoicesAsync(PurchaseInvoiceFilter filter);
}

public class AdminOrderService(
    FratakShopDbContext context,
    IOrderService orders,
    IStoreSettingService settings,
    IMonitoringService monitoring) : IAdminOrderService
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> NextStatuses = new()
    {
        [OrderStatus.Pending] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Processing, OrderStatus.Cancelled],
        [OrderStatus.Processing] = [OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Shipped] = [OrderStatus.Delivered, OrderStatus.Returned],
        [OrderStatus.Delivered] = [OrderStatus.Returned],
    };

    public async Task<AdminOrderList> GetListAsync(AdminOrderFilter filter)
    {
        var withoutStatus = Filter(filter, ignoreStatus: true);
        var statusCounts = await withoutStatus.GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count);

        var awaitingInvoice = await context.Orders.CountAsync(o =>
            (o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Processing)
            && !context.PurchaseInvoices.Any(i => i.OrderId == o.Id));

        var query = Filter(filter, ignoreStatus: false);
        var page = Math.Max(1, filter.Page);
        var take = Math.Clamp(filter.Take, 10, 100);
        var total = await query.CountAsync();
        var sum = total == 0 ? 0 : await query.SumAsync(o => o.PayableAmount);
        var rows = await Project(Sort(query, filter.Sort)).Skip((page - 1) * take).Take(take).ToListAsync();

        return new AdminOrderList
        {
            Orders = new PagedList<AdminOrderRow>(rows, total, page, take),
            StatusCounts = statusCounts,
            AwaitingInvoiceCount = awaitingInvoice,
            FilteredTotal = sum,
        };
    }

    public async Task<List<AdminOrderRow>> GetExportRowsAsync(AdminOrderFilter filter) =>
        await Project(Sort(Filter(filter, ignoreStatus: false), filter.Sort)).Take(50_000).ToListAsync();

    private IQueryable<Order> Filter(AdminOrderFilter filter, bool ignoreStatus)
    {
        var query = context.Orders.AsQueryable();
        if (!ignoreStatus && filter.Status is { } status)
            query = query.Where(o => o.Status == status);
        if (filter.PaymentStatus is { } paymentStatus)
            query = query.Where(o => o.PaymentStatus == paymentStatus);
        if (filter.PaymentMethod is { } method)
            query = query.Where(o => o.PaymentMethod == method);
        if (filter.From is { } from)
            query = query.Where(o => o.CreatedAt >= from);
        if (filter.To is { } to)
            query = query.Where(o => o.CreatedAt < to);
        if (filter.UserId is { } userId)
            query = query.Where(o => o.UserId == userId);
        if (!string.IsNullOrWhiteSpace(filter.Province))
            query = query.Where(o => o.ShippingProvince == filter.Province);
        if (filter.HasPurchaseInvoice is { } hasInvoice)
            query = hasInvoice
                ? query.Where(o => context.PurchaseInvoices.Any(i => i.OrderId == o.Id))
                : query.Where(o => !context.PurchaseInvoices.Any(i => i.OrderId == o.Id));
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = SearchText.NormalizeDigits(filter.Search.Trim());
            query = query.Where(o => o.OrderNumber.Contains(term) || o.CustomerName.Contains(term) || o.CustomerPhone.Contains(term)
                                     || o.ShippingPhone.Contains(term) || (o.TrackingCode != null && o.TrackingCode.Contains(term))
                                     || (o.InvoiceNumber != null && o.InvoiceNumber.Contains(term))
                                     || o.Items.Any(i => i.ProductName.Contains(term) || i.SKU == term));
        }
        return query;
    }

    private static IQueryable<Order> Sort(IQueryable<Order> query, string? sort) => sort switch
    {
        "oldest" => query.OrderBy(o => o.CreatedAt),
        "amount" => query.OrderByDescending(o => o.PayableAmount).ThenByDescending(o => o.CreatedAt),
        "amount-asc" => query.OrderBy(o => o.PayableAmount).ThenByDescending(o => o.CreatedAt),
        _ => query.OrderByDescending(o => o.CreatedAt),
    };

    private IQueryable<AdminOrderRow> Project(IQueryable<Order> query) => query.Select(o => new AdminOrderRow
    {
        Id = o.Id,
        OrderNumber = o.OrderNumber,
        CreatedAt = o.CreatedAt,
        UserId = o.UserId,
        CustomerName = o.CustomerName,
        CustomerPhone = o.CustomerPhone,
        ShippingProvince = o.ShippingProvince,
        ShippingCity = o.ShippingCity,
        Status = o.Status,
        PaymentStatus = o.PaymentStatus,
        PaymentMethod = o.PaymentMethod,
        PayableAmount = o.PayableAmount,
        DiscountTotal = o.DiscountAmount + o.CouponDiscountAmount,
        ShippingAmount = o.ShippingAmount,
        ItemCount = o.Items.Sum(i => i.Quantity),
        ShippingMethod = o.ShippingMethod,
        TrackingCode = o.TrackingCode,
        PurchaseInvoiceCount = context.PurchaseInvoices.Count(i => i.OrderId == o.Id),
        PurchaseCost = context.PurchaseInvoices.Where(i => i.OrderId == o.Id).Sum(i => (decimal?)i.Amount) ?? 0,
    });

    public async Task<Guid?> FindIdByNumberAsync(string orderNumber)
    {
        var number = SearchText.NormalizeDigits(orderNumber.Trim());
        return await context.Orders.Where(o => o.OrderNumber == number).Select(o => (Guid?)o.Id).FirstOrDefaultAsync();
    }

    public async Task<AdminOrderDetail?> GetDetailAsync(Guid id)
    {
        var order = await context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
            return null;

        var items = await context.OrderItems.Where(i => i.OrderId == id)
            .OrderBy(i => i.CreatedAt)
            .Select(i => new AdminOrderItem
            {
                Id = i.Id,
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                ProductSlug = i.Product != null ? i.Product.Slug : null,
                ImageName = i.Product != null ? i.Product.Images.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.DisplayOrder).Select(x => x.ImageName).FirstOrDefault() : null,
                SKU = i.SKU,
                UnitPrice = i.UnitPrice,
                DiscountAmount = i.DiscountAmount,
                Quantity = i.Quantity,
                TotalAmount = i.TotalAmount,
                UnitCostPrice = i.UnitCostPrice,
                CurrentStock = i.Product != null ? i.Product.StockQuantity : null,
                SupplierName = i.Product != null && i.Product.Supplier != null ? i.Product.Supplier.Name : null,
                SupplierUrl = i.Product != null ? i.Product.SupplierUrl : null,
                SupplierSku = i.Product != null ? i.Product.SupplierSku : null,
                SupplierNote = i.Product != null ? i.Product.SupplierNote : null,
            })
            .ToListAsync();

        var detail = new AdminOrderDetail
        {
            Order = order,
            Items = items,
            Notes = await context.OrderNotes.Where(n => n.OrderId == id).OrderByDescending(n => n.CreatedAt)
                .Select(n => new OrderNoteDto { Id = n.Id, Kind = n.Kind, AuthorName = n.AuthorName, Content = n.Content, CreatedAt = n.CreatedAt })
                .ToListAsync(),
            PurchaseInvoices = await InvoiceQuery(context.PurchaseInvoices.Where(i => i.OrderId == id)).OrderBy(i => i.CreatedAt).ToListAsync(),
            Payments = await context.PaymentTransactions.Where(p => p.OrderId == id).OrderByDescending(p => p.CreatedAt)
                .Select(p => new AdminPaymentRow
                {
                    Id = p.Id, CreatedAt = p.CreatedAt, Gateway = p.Gateway, Purpose = p.Purpose, Amount = p.Amount, Status = p.Status,
                    RefId = p.RefId, CardPan = p.CardPan, Authority = p.Authority, ResultCode = p.ResultCode, Message = p.Message, VerifiedAt = p.VerifiedAt,
                })
                .ToListAsync(),
            NextStatuses = NextStatuses.TryGetValue(order.Status, out var next) ? next : [],
            RequiresPurchaseInvoice = (await settings.GetAsync()).RequirePurchaseInvoiceForProcessing,
        };

        var returns = await context.ReturnRequests.Where(r => r.OrderId == id)
            .Select(r => new { r.RequestNumber, r.Status, r.RequestedAmount }).ToListAsync();
        detail.Returns = returns.Select(r => (r.RequestNumber, r.Status, r.RequestedAmount)).ToList();

        var tickets = await context.SupportTickets.Where(t => t.OrderId == id)
            .Select(t => new { t.TicketNumber, t.Subject }).ToListAsync();
        detail.Tickets = tickets.Select(t => (t.TicketNumber, t.Subject)).ToList();

        if (order.UserId is { } userId)
        {
            var paid = context.Orders.Where(o => o.UserId == userId && o.PaymentStatus == PaymentStatus.Paid
                                                 && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Returned);
            detail.CustomerOrderCount = await context.Orders.CountAsync(o => o.UserId == userId);
            detail.CustomerTotalSpent = await paid.SumAsync(o => (decimal?)(o.PayableAmount - o.RefundedAmount)) ?? 0;
            detail.CustomerIsBlocked = await context.Users.Where(u => u.Id == userId).Select(u => u.IsBlocked).FirstOrDefaultAsync();
        }

        return detail;
    }

    public async Task<OperationResult> AddNoteAsync(Guid orderId, string? content, Guid? authorId, string authorName, OrderNoteKind kind = OrderNoteKind.Note)
    {
        var text = content?.Trim();
        if (string.IsNullOrEmpty(text))
            return OperationResult.Error("متن یادداشت را وارد کنید.");
        if (text.Length > 2000)
            return OperationResult.Error("یادداشت حداکثر ۲۰۰۰ نویسه است.");
        if (!await context.Orders.AnyAsync(o => o.Id == orderId))
            return OperationResult.NotFound("سفارش یافت نشد.");

        context.OrderNotes.Add(new OrderNote { OrderId = orderId, Content = text, AuthorUserId = authorId, AuthorName = authorName, Kind = kind });
        await context.SaveChangesAsync();
        return OperationResult.Success("یادداشت ثبت شد.");
    }

    public async Task<OperationResult> ChangeStatusAsync(Guid orderId, OrderStatus status, string? note, Guid? actorId, string actorName)
    {
        var order = await context.Orders.Where(o => o.Id == orderId).Select(o => new { o.Status, o.OrderNumber }).FirstOrDefaultAsync();
        if (order is null)
            return OperationResult.NotFound("سفارش یافت نشد.");

        var result = status is OrderStatus.Cancelled or OrderStatus.Returned
            ? await orders.CancelOrderByAdminAsync(orderId, status, note)
            : await orders.ChangeOrderStatusAsync(orderId, status);

        if (result.Status != EnOperationResultStatus.Success)
            return result;

        var text = $"وضعیت از «{OrderStatusTitle(order.Status)}» به «{OrderStatusTitle(status)}» تغییر کرد."
                   + (string.IsNullOrWhiteSpace(note) ? "" : $" توضیح: {note.Trim()}");
        context.OrderNotes.Add(new OrderNote { OrderId = orderId, Kind = OrderNoteKind.StatusChange, AuthorUserId = actorId, AuthorName = actorName, Content = Cut(text, 2000) });
        await context.SaveChangesAsync();
        await monitoring.LogActionAsync("order.status", "Order", orderId, $"{order.OrderNumber}: {OrderStatusTitle(order.Status)} → {OrderStatusTitle(status)}");
        return result;
    }

    public async Task<OperationResult> SetTrackingCodeAsync(Guid orderId, string? trackingCode, Guid? actorId, string actorName)
    {
        var before = await context.Orders.Where(o => o.Id == orderId).Select(o => new { o.Status, o.OrderNumber }).FirstOrDefaultAsync();
        if (before is null)
            return OperationResult.NotFound("سفارش یافت نشد.");

        var code = trackingCode?.Trim() ?? "";
        if (code.Length > 100)
            return OperationResult.Error("کد رهگیری حداکثر ۱۰۰ نویسه است.");

        var result = await orders.SetTrackingCodeAsync(orderId, code);
        if (result.Status != EnOperationResultStatus.Success)
            return result;

        var after = await context.Orders.Where(o => o.Id == orderId).Select(o => o.Status).FirstAsync();
        var text = $"کد رهگیری مرسوله ثبت شد: {code}" + (after != before.Status ? $" (وضعیت: {OrderStatusTitle(after)})" : "");
        context.OrderNotes.Add(new OrderNote { OrderId = orderId, Kind = OrderNoteKind.StatusChange, AuthorUserId = actorId, AuthorName = actorName, Content = text });
        await context.SaveChangesAsync();
        return result;
    }

    public async Task<OperationResult> MarkCashPaidAsync(Guid orderId, string? reference, Guid? actorId, string actorName)
    {
        var order = await context.Orders.Where(o => o.Id == orderId).Select(o => new { o.PaymentMethod, o.PaymentStatus, o.Status }).FirstOrDefaultAsync();
        if (order is null)
            return OperationResult.NotFound("سفارش یافت نشد.");
        if (order.PaymentMethod is not (PaymentMethod.CashOnDelivery or PaymentMethod.BankTransfer))
            return OperationResult.Error("فقط پرداخت در محل یا کارت‌به‌کارت را می‌توان دستی پرداخت‌شده ثبت کرد.");
        if (order.PaymentStatus == PaymentStatus.Paid)
            return OperationResult.Error("این سفارش قبلاً پرداخت شده است.");
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Returned)
            return OperationResult.Error("سفارش لغو شده است.");

        var refText = string.IsNullOrWhiteSpace(reference) ? null : Cut(reference.Trim(), 100);
        var result = await orders.ChangePaymentStatusAsync(orderId, PaymentStatus.Paid, refText);
        if (result.Status != EnOperationResultStatus.Success)
            return result;

        context.OrderNotes.Add(new OrderNote
        {
            OrderId = orderId, Kind = OrderNoteKind.StatusChange, AuthorUserId = actorId, AuthorName = actorName,
            Content = "پرداخت سفارش به‌صورت دستی تأیید شد" + (refText is null ? "." : $" (مرجع: {refText})."),
        });
        await context.SaveChangesAsync();
        await monitoring.LogActionAsync("order.paid", "Order", orderId, "ثبت دستی پرداخت" + (refText is null ? "" : $" – {refText}"));
        return OperationResult.Success("پرداخت سفارش ثبت شد.");
    }

    public async Task<OperationResult<Guid>> AddPurchaseInvoiceAsync(AddPurchaseInvoiceCommand command)
    {
        if (command.Amount < 0 || command.Amount > 100_000_000_000m)
            return OperationResult<Guid>.Error("مبلغ فاکتور خرید معتبر نیست.");
        if (command.InvoiceNumber?.Length > 100 || command.Notes?.Length > 1000 || command.SupplierName?.Length > 150)
            return OperationResult<Guid>.Error("طول یکی از فیلدها بیش از حد مجاز است.");

        var order = await context.Orders.Where(o => o.Id == command.OrderId).Select(o => new { o.Id, o.OrderNumber, o.Status }).FirstOrDefaultAsync();
        if (order is null)
            return OperationResult<Guid>.NotFound("سفارش یافت نشد.");
        if (order.Status is OrderStatus.Cancelled)
            return OperationResult<Guid>.Error("برای سفارش لغوشده نمی‌توان فاکتور خرید ثبت کرد.");

        string? supplierName = string.IsNullOrWhiteSpace(command.SupplierName) ? null : command.SupplierName.Trim();
        if (command.SupplierId is { } supplierId)
        {
            supplierName = await context.Suppliers.Where(s => s.Id == supplierId).Select(s => s.Name).FirstOrDefaultAsync();
            if (supplierName is null)
                return OperationResult<Guid>.NotFound("تأمین‌کننده یافت نشد.");
        }

        var invoice = new PurchaseInvoice
        {
            OrderId = command.OrderId,
            SupplierId = command.SupplierId,
            SupplierName = supplierName,
            InvoiceNumber = string.IsNullOrWhiteSpace(command.InvoiceNumber) ? null : command.InvoiceNumber.Trim(),
            Amount = Math.Round(command.Amount, 0),
            PurchasedAt = command.PurchasedAt ?? DateTimeOffset.UtcNow,
            FileName = command.FileName,
            ContentType = command.ContentType,
            SizeBytes = command.SizeBytes,
            Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim(),
            UploadedByUserId = command.UploadedByUserId,
            UploadedByName = command.UploadedByName,
        };
        context.PurchaseInvoices.Add(invoice);
        context.OrderNotes.Add(new OrderNote
        {
            OrderId = command.OrderId, Kind = OrderNoteKind.System, AuthorUserId = command.UploadedByUserId, AuthorName = command.UploadedByName,
            Content = Cut($"فاکتور خرید بارگذاری شد: {supplierName ?? "تأمین‌کننده نامشخص"}، مبلغ {invoice.Amount:#,0} تومان"
                          + (invoice.InvoiceNumber is null ? "" : $"، شماره {invoice.InvoiceNumber}"), 2000),
        });
        await context.SaveChangesAsync();
        return OperationResult<Guid>.Success(invoice.Id, "فاکتور خرید ثبت شد.");
    }

    public async Task<OperationResult<string>> DeletePurchaseInvoiceAsync(Guid invoiceId, Guid? actorId, string actorName)
    {
        var invoice = await context.PurchaseInvoices.AsTracking().FirstOrDefaultAsync(i => i.Id == invoiceId);
        if (invoice is null)
            return OperationResult<string>.NotFound("فاکتور یافت نشد.");

        var orderStatus = await context.Orders.Where(o => o.Id == invoice.OrderId).Select(o => o.Status).FirstAsync();
        var remaining = await context.PurchaseInvoices.CountAsync(i => i.OrderId == invoice.OrderId && i.Id != invoiceId);
        if (remaining == 0 && orderStatus is OrderStatus.Processing or OrderStatus.Shipped or OrderStatus.Delivered
                           && (await settings.GetAsync()).RequirePurchaseInvoiceForProcessing)
            return OperationResult<string>.Error("این سفارش وارد مرحله آماده‌سازی شده و باید حداقل یک فاکتور خرید داشته باشد؛ ابتدا فاکتور صحیح را بارگذاری کنید.");

        context.PurchaseInvoices.Remove(invoice);
        context.OrderNotes.Add(new OrderNote
        {
            OrderId = invoice.OrderId, Kind = OrderNoteKind.System, AuthorUserId = actorId, AuthorName = actorName,
            Content = $"فاکتور خرید حذف شد ({invoice.SupplierName ?? "-"}، {invoice.Amount:#,0} تومان).",
        });
        await context.SaveChangesAsync();
        return OperationResult<string>.Success(invoice.FileName, "فاکتور خرید حذف شد.");
    }

    public async Task<PurchaseInvoiceDto?> GetPurchaseInvoiceAsync(Guid invoiceId) =>
        await InvoiceQuery(context.PurchaseInvoices.Where(i => i.Id == invoiceId)).FirstOrDefaultAsync();

    public async Task<PagedList<PurchaseInvoiceDto>> GetPurchaseInvoicesAsync(PurchaseInvoiceFilter filter)
    {
        var query = context.PurchaseInvoices.AsQueryable();
        if (filter.SupplierId is { } supplierId)
            query = query.Where(i => i.SupplierId == supplierId);
        if (filter.From is { } from)
            query = query.Where(i => i.CreatedAt >= from);
        if (filter.To is { } to)
            query = query.Where(i => i.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(i => i.Order.OrderNumber.Contains(term) || (i.InvoiceNumber != null && i.InvoiceNumber.Contains(term))
                                     || (i.SupplierName != null && i.SupplierName.Contains(term)));
        }

        var page = Math.Max(1, filter.Page);
        var take = Math.Clamp(filter.Take, 10, 200);
        var total = await query.CountAsync();
        var items = await InvoiceQuery(query.OrderByDescending(i => i.CreatedAt)).Skip((page - 1) * take).Take(take).ToListAsync();
        return new PagedList<PurchaseInvoiceDto>(items, total, page, take);
    }

    private static IQueryable<PurchaseInvoiceDto> InvoiceQuery(IQueryable<PurchaseInvoice> query) => query.Select(i => new PurchaseInvoiceDto
    {
        Id = i.Id,
        OrderId = i.OrderId,
        OrderNumber = i.Order.OrderNumber,
        SupplierId = i.SupplierId,
        SupplierName = i.SupplierName,
        InvoiceNumber = i.InvoiceNumber,
        Amount = i.Amount,
        PurchasedAt = i.PurchasedAt,
        FileName = i.FileName,
        ContentType = i.ContentType,
        SizeBytes = i.SizeBytes,
        Notes = i.Notes,
        UploadedByName = i.UploadedByName,
        CreatedAt = i.CreatedAt,
    });

    public static string OrderStatusTitle(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "ثبت‌شده",
        OrderStatus.Confirmed => "تأیید‌شده",
        OrderStatus.Processing => "در حال آماده‌سازی",
        OrderStatus.Shipped => "ارسال‌شده",
        OrderStatus.Delivered => "تحویل‌شده",
        OrderStatus.Cancelled => "لغو‌شده",
        OrderStatus.Returned => "مرجوع‌شده",
        _ => "نامشخص",
    };

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}
