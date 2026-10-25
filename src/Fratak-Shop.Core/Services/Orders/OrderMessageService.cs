using Fratak_Shop.Core.DTOs.Notifications;
using Fratak_Shop.Core.DTOs.Wallets;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Wallets;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Wallets;
using Microsoft.EntityFrameworkCore;
using StockLedger = Fratak_Shop.Data.Utilities.StockLedger;
using StockMovementReason = Fratak_Shop.Data.Entities.Products.StockMovementReason;

namespace Fratak_Shop.Core.Services.Orders;

/// <summary>یک پیام سفارش برای نمایش در پنل و صفحهٔ سفارش مشتری.</summary>
public sealed record OrderMessageDto(
    Guid Id,
    bool FromStaff,
    string AuthorName,
    string Body,
    OrderMessageKind Kind,
    Guid? OrderItemId,
    string? ItemName,
    OrderMessageDecision Decision,
    DateTimeOffset? DecidedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadByCustomerAt,
    DateTimeOffset? ReadByStaffAt)
{
    /// <summary>سؤالی که هنوز منتظر پاسخ مشتری است (دکمه‌های «حذف کالا» / «لغو سفارش» فقط برای این حالت).</summary>
    public bool IsOpenQuestion => Kind == OrderMessageKind.ItemUnavailable && Decision == OrderMessageDecision.Pending;
}

/// <summary>
/// پیام‌های سفارش بین کارکنان و مشتری، به‌همراه سؤال «کالا ناموجود است».
/// <para>
/// سناریوی اصلی: کارمند هنگام خرید از تأمین‌کننده می‌بیند «کابل شارژ» تمام شده. در صفحهٔ سفارش پنل،
/// «پرسیدن از مشتری» را می‌زند؛ مشتری پیامک/اعلان می‌گیرد و در صفحهٔ سفارش یکی را انتخاب می‌کند:
/// <list type="bullet">
/// <item>«حذف کالا و ادامه»: قلم حذف، مبالغ سفارش بازمحاسبه و اگر پرداخت شده بود مابه‌التفاوت به کیف پول برمی‌گردد.</item>
/// <item>«لغو کل سفارش»: سفارش لغو و کل مبلغ پرداختی به کیف پول برمی‌گردد (همان مسیر لغو استاندارد).</item>
/// </list>
/// همهٔ تغییرها در یادداشت داخلی سفارش ثبت و به کارکنان اعلان می‌شود. هیچ پرداختی دستی ثبت نمی‌شود.
/// </para>
/// محل استفاده: <c>Areas/Admin/Pages/Orders/Detail</c> و <c>Pages/Profile/OrderDetail</c>.
/// </summary>
public interface IOrderMessageService
{
    /// <summary>پیام‌های یک سفارش به ترتیب زمان (قدیمی به جدید).</summary>
    Task<List<OrderMessageDto>> ListAsync(Guid orderId);

    /// <summary>پیام متنی کارمند به مشتری (با اعلان و پیامک طبق قالب «پیام فروشگاه دربارهٔ سفارش»).</summary>
    Task<OperationResult> SendFromStaffAsync(Guid orderId, string? body, Guid? staffId, string staffName);

    /// <summary>
    /// سؤال «کالا ناموجود است؛ حذف شود یا سفارش لغو شود؟».
    /// </summary>
    /// <param name="zeroStock">true یعنی موجودی محصول در سایت همین حالا صفر شود تا مشتری دیگری آن را نخرد.</param>
    Task<OperationResult> AskItemUnavailableAsync(Guid orderId, Guid orderItemId, string? body, bool zeroStock, Guid? staffId, string staffName);

    /// <summary>پس گرفتن سؤال باز (مثلاً کالا دوباره پیدا شد).</summary>
    Task<OperationResult> WithdrawAsync(Guid orderId, Guid messageId, Guid? staffId, string staffName);

    /// <summary>پاسخ متنی مشتری.</summary>
    Task<OperationResult> ReplyFromCustomerAsync(Guid orderId, Guid userId, string? body);

    /// <summary>
    /// تصمیم مشتری برای سؤال «کالا ناموجود»: <see cref="OrderMessageDecision.RemoveItem"/> یا <see cref="OrderMessageDecision.CancelOrder"/>.
    /// </summary>
    Task<OperationResult> DecideAsync(Guid orderId, Guid messageId, Guid userId, OrderMessageDecision decision);

    /// <summary>پیام‌های کارمند این سفارش برای مشتری «خوانده‌شده» علامت می‌خورند (هنگام باز کردن صفحهٔ سفارش).</summary>
    Task MarkReadByCustomerAsync(Guid orderId, Guid userId);

    /// <summary>پیام‌های مشتری این سفارش برای کارکنان «خوانده‌شده» علامت می‌خورند.</summary>
    Task MarkReadByStaffAsync(Guid orderId);

    /// <summary>تعداد پیام‌های خوانده‌نشدهٔ مشتریان (نشان منوی پنل).</summary>
    Task<int> CountUnreadForStaffAsync();
}

public class OrderMessageService(
    FratakShopDbContext context,
    IOrderService orders,
    IWalletService wallet,
    INotificationService notifications,
    IStaffNotificationService staff) : IOrderMessageService
{
    public const int MaxBodyLength = 2000;

    /// <summary>سفارش فقط پیش از ارسال قابل تغییر اقلام است.</summary>
    private static bool IsEditable(OrderStatus status) =>
        status is OrderStatus.Pending or OrderStatus.Confirmed or OrderStatus.Processing;

    public async Task<List<OrderMessageDto>> ListAsync(Guid orderId) =>
        await context.OrderMessages
            .Where(m => m.OrderId == orderId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new OrderMessageDto(m.Id, m.FromStaff, m.AuthorName ?? "", m.Body, m.Kind, m.OrderItemId, m.ItemName,
                m.Decision, m.DecidedAt, m.CreatedAt, m.ReadByCustomerAt, m.ReadByStaffAt))
            .ToListAsync();

    public async Task<OperationResult> SendFromStaffAsync(Guid orderId, string? body, Guid? staffId, string staffName)
    {
        var text = body.ToPlainText(MaxBodyLength);
        if (string.IsNullOrWhiteSpace(text))
            return OperationResult.Error("متن پیام را وارد کنید.");

        var order = await context.Orders.Where(o => o.Id == orderId)
            .Select(o => new { o.UserId, o.OrderNumber }).FirstOrDefaultAsync();
        if (order is null)
            return OperationResult.NotFound("سفارش یافت نشد.");
        if (order.UserId is null)
            return OperationResult.Error("این سفارش به حساب کاربری متصل نیست و پیام به مشتری نمی‌رسد.");

        context.OrderMessages.Add(new OrderMessage
        {
            OrderId = orderId, FromStaff = true, AuthorUserId = staffId, AuthorName = staffName, Body = text, Kind = OrderMessageKind.Message,
        });
        context.OrderNotes.Add(new OrderNote
        {
            OrderId = orderId, Kind = OrderNoteKind.System, AuthorUserId = staffId, AuthorName = staffName,
            Content = Cut($"پیام به مشتری ارسال شد: {text}", 2000),
        });
        await context.SaveChangesAsync();

        await NotifyCustomerAsync(order.UserId.Value, order.OrderNumber, text, null);
        return OperationResult.Success("پیام برای مشتری ارسال شد.");
    }

    public async Task<OperationResult> AskItemUnavailableAsync(Guid orderId, Guid orderItemId, string? body, bool zeroStock, Guid? staffId, string staffName)
    {
        var order = await context.Orders.Where(o => o.Id == orderId)
            .Select(o => new { o.UserId, o.OrderNumber, o.Status }).FirstOrDefaultAsync();
        if (order is null)
            return OperationResult.NotFound("سفارش یافت نشد.");
        if (order.UserId is null)
            return OperationResult.Error("این سفارش به حساب کاربری متصل نیست و مشتری نمی‌تواند پاسخ دهد.");
        if (!IsEditable(order.Status))
            return OperationResult.Error("فقط پیش از ارسال سفارش می‌توان دربارهٔ حذف کالا پرسید.");

        var item = await context.OrderItems.Where(i => i.Id == orderItemId && i.OrderId == orderId)
            .Select(i => new { i.Id, i.ProductId, i.ProductName, i.Quantity }).FirstOrDefaultAsync();
        if (item is null)
            return OperationResult.NotFound("این کالا در سفارش نیست.");

        if (await context.OrderMessages.AnyAsync(m => m.OrderId == orderId && m.OrderItemId == orderItemId
                                                      && m.Decision == OrderMessageDecision.Pending))
            return OperationResult.Error("برای این کالا یک سؤال بی‌پاسخ وجود دارد؛ منتظر پاسخ مشتری بمانید یا آن را پس بگیرید.");

        var extra = body.ToPlainText(1000);
        var text = $"متأسفانه کالای «{item.ProductName}» در حال حاضر موجود نیست. "
                   + "اگر موافق باشید این کالا را حذف می‌کنیم، مبلغش به کیف پول شما برمی‌گردد و بقیهٔ سفارش ارسال می‌شود؛ "
                   + "یا می‌توانید کل سفارش را لغو کنید."
                   + (string.IsNullOrWhiteSpace(extra) ? "" : $"\n{extra}");

        context.OrderMessages.Add(new OrderMessage
        {
            OrderId = orderId, FromStaff = true, AuthorUserId = staffId, AuthorName = staffName, Body = Cut(text, MaxBodyLength),
            Kind = OrderMessageKind.ItemUnavailable, OrderItemId = item.Id, ItemName = Cut(item.ProductName, 300),
            Decision = OrderMessageDecision.Pending,
        });
        context.OrderNotes.Add(new OrderNote
        {
            OrderId = orderId, Kind = OrderNoteKind.System, AuthorUserId = staffId, AuthorName = staffName,
            Content = Cut($"از مشتری دربارهٔ ناموجودی «{item.ProductName}» سؤال شد" + (zeroStock ? " و موجودی محصول در سایت صفر شد." : "."), 2000),
        });
        await context.SaveChangesAsync();

        if (zeroStock && item.ProductId is { } productId)
        {
            var stockBefore = await context.Products.Where(p => p.Id == productId).Select(p => p.StockQuantity).FirstOrDefaultAsync();
            await context.Products.Where(p => p.Id == productId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, 0));
            await StockLedger.RecordAsync(context, productId, -stockBefore, StockMovementReason.ItemUnavailable, orderId, order.OrderNumber,
                "ناموجودی اعلام‌شده در سفارش");
            // به‌روزرسانی گروهی از SaveChanges عبور نمی‌کند: کش صفحات (وضعیت موجودی) باید صریحاً خبردار شود.
            Fratak_Shop.Data.Utilities.DataChangeNotifier.Publish(typeof(Fratak_Shop.Data.Entities.Products.Product));
        }

        await NotifyCustomerAsync(order.UserId.Value, order.OrderNumber, text, item.ProductName);
        return OperationResult.Success("سؤال برای مشتری ارسال شد؛ پس از پاسخ، به شما اعلان می‌شود.");
    }

    public async Task<OperationResult> WithdrawAsync(Guid orderId, Guid messageId, Guid? staffId, string staffName)
    {
        var now = DateTimeOffset.UtcNow;
        var affected = await context.OrderMessages
            .Where(m => m.Id == messageId && m.OrderId == orderId && m.Decision == OrderMessageDecision.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Decision, OrderMessageDecision.Withdrawn)
                .SetProperty(m => m.DecidedAt, (DateTimeOffset?)now)
                .SetProperty(m => m.UpdatedAt, now));
        if (affected == 0)
            return OperationResult.Error("این سؤال باز نیست (شاید مشتری همین حالا پاسخ داده است).");

        context.OrderNotes.Add(new OrderNote
        {
            OrderId = orderId, Kind = OrderNoteKind.System, AuthorUserId = staffId, AuthorName = staffName, Content = "سؤال ناموجودی کالا پس گرفته شد.",
        });
        await context.SaveChangesAsync();
        return OperationResult.Success("سؤال پس گرفته شد.");
    }

    public async Task<OperationResult> ReplyFromCustomerAsync(Guid orderId, Guid userId, string? body)
    {
        var text = body.ToPlainText(MaxBodyLength);
        if (string.IsNullOrWhiteSpace(text))
            return OperationResult.Error("متن پیام را وارد کنید.");

        var order = await context.Orders.Where(o => o.Id == orderId && o.UserId == userId)
            .Select(o => new { o.OrderNumber, o.CustomerName }).FirstOrDefaultAsync();
        if (order is null)
            return OperationResult.NotFound("سفارش یافت نشد.");

        // جلوگیری از سیل پیام: حداکثر ۱۰ پیام مشتری در یک ساعت برای هر سفارش.
        var hourAgo = DateTimeOffset.UtcNow.AddHours(-1);
        if (await context.OrderMessages.CountAsync(m => m.OrderId == orderId && !m.FromStaff && m.CreatedAt > hourAgo) >= 10)
            return OperationResult.Error("تعداد پیام‌ها زیاد است؛ لطفاً کمی بعد دوباره تلاش کنید.");

        context.OrderMessages.Add(new OrderMessage
        {
            OrderId = orderId, FromStaff = false, AuthorUserId = userId, AuthorName = order.CustomerName, Body = text, Kind = OrderMessageKind.Message,
        });
        await context.SaveChangesAsync();

        await staff.NotifyAsync(new StaffAlert(StaffNotificationKind.OrderMessageAnswered,
            $"پیام مشتری – سفارش {order.OrderNumber}", Cut(text, 200), $"/admin/orders/{orderId}#messages",
            [AdminPermissions.OrdersManage]));
        return OperationResult.Success("پیام شما ارسال شد.");
    }

    public async Task<OperationResult> DecideAsync(Guid orderId, Guid messageId, Guid userId, OrderMessageDecision decision)
    {
        if (decision is not (OrderMessageDecision.RemoveItem or OrderMessageDecision.CancelOrder))
            return OperationResult.Error("گزینهٔ انتخاب‌شده معتبر نیست.");

        var order = await context.Orders.Where(o => o.Id == orderId && o.UserId == userId)
            .Select(o => new { o.OrderNumber, o.Status, o.CustomerName }).FirstOrDefaultAsync();
        if (order is null)
            return OperationResult.NotFound("سفارش یافت نشد.");

        var message = await context.OrderMessages
            .Where(m => m.Id == messageId && m.OrderId == orderId && m.Kind == OrderMessageKind.ItemUnavailable)
            .Select(m => new { m.Decision, m.OrderItemId, m.ItemName }).FirstOrDefaultAsync();
        if (message is null)
            return OperationResult.NotFound("پیام یافت نشد.");
        if (message.Decision != OrderMessageDecision.Pending)
            return OperationResult.Error("به این پیام قبلاً پاسخ داده شده است.");

        if (!IsEditable(order.Status))
        {
            await SetDecisionAsync(messageId, OrderMessageDecision.Pending, OrderMessageDecision.Withdrawn);
            return OperationResult.Error("وضعیت سفارش تغییر کرده و این سؤال دیگر معتبر نیست.");
        }

        // اگر فقط همین یک کالا در سفارش مانده، «حذف کالا» یعنی سفارش خالی؛ پس همان لغو کامل انجام می‌شود.
        var itemCount = await context.OrderItems.CountAsync(i => i.OrderId == orderId);
        var effective = decision == OrderMessageDecision.RemoveItem && itemCount <= 1 ? OrderMessageDecision.CancelOrder : decision;

        // ثبت اتمی تصمیم: اگر دو کلیک هم‌زمان برسد، فقط یکی اثر دارد.
        if (!await SetDecisionAsync(messageId, OrderMessageDecision.Pending, effective))
            return OperationResult.Error("به این پیام قبلاً پاسخ داده شده است.");

        OperationResult result;
        if (effective == OrderMessageDecision.CancelOrder)
        {
            result = await orders.CancelOrderByAdminAsync(orderId, OrderStatus.Cancelled,
                $"لغو به انتخاب مشتری به‌دلیل ناموجودی «{message.ItemName}»");
        }
        else
        {
            result = await RemoveItemAsync(orderId, message.OrderItemId, message.ItemName ?? "کالا");
        }

        if (!result.IsSuccess())
        {
            // تصمیم برگردانده می‌شود تا مشتری بتواند دوباره تلاش کند.
            await SetDecisionAsync(messageId, effective, OrderMessageDecision.Pending);
            return result;
        }

        var summary = effective == OrderMessageDecision.CancelOrder
            ? $"مشتری کل سفارش را لغو کرد (ناموجودی «{message.ItemName}»)."
            : $"مشتری حذف «{message.ItemName}» و ادامهٔ سفارش را پذیرفت.";
        context.OrderNotes.Add(new OrderNote { OrderId = orderId, Kind = OrderNoteKind.System, AuthorName = order.CustomerName, Content = Cut(summary + " " + result.Message, 2000) });
        await context.SaveChangesAsync();

        await staff.NotifyAsync(new StaffAlert(StaffNotificationKind.OrderMessageAnswered,
            $"پاسخ مشتری – سفارش {order.OrderNumber}", summary, $"/admin/orders/{orderId}#messages",
            [AdminPermissions.OrdersManage]));

        var customerText = effective == OrderMessageDecision.CancelOrder && decision == OrderMessageDecision.RemoveItem
            ? "چون این تنها کالای سفارش بود، سفارش لغو شد. " + result.Message
            : result.Message;
        return OperationResult.Success(customerText);
    }

    /// <summary>
    /// حذف یک قلم از سفارش و بازمحاسبهٔ مبالغ، به‌همراه بازگشت مابه‌التفاوت به کیف پول اگر سفارش پرداخت شده باشد.
    /// <para>
    /// قواعد:
    /// ۱) مبلغ قابل پرداخت جدید = جمع اقلام باقی‌مانده − تخفیف سفارش (حداکثر تا جمع اقلام + ارسال) + هزینهٔ ارسال.
    /// ۲) بازگشت وجه = مبلغ قبلی − مبلغ جدید. اعتبار هدیهٔ خرج‌شده اول برمی‌گردد (و غیرقابل برداشت می‌ماند).
    /// ۳) موجودی محصول برگشت داده نمی‌شود، چون کالا واقعاً موجود نبوده است (کارمند هنگام سؤال می‌تواند موجودی را صفر کند).
    /// </para>
    /// مثال: سفارش ۵۰۰ هزار تومانی پرداخت‌شده با یک کالای ۱۲۰ هزار تومانی ناموجود → مبلغ جدید ۳۸۰ هزار و ۱۲۰ هزار به کیف پول.
    /// </summary>
    private async Task<OperationResult> RemoveItemAsync(Guid orderId, Guid? orderItemId, string itemName)
    {
        if (orderItemId is null)
            return OperationResult.Error("کالای مربوط به این پیام مشخص نیست.");

        await using var transaction = await context.Database.BeginTransactionAsync();

        var order = await context.Orders.AsTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId);
        var item = order?.Items.FirstOrDefault(i => i.Id == orderItemId);
        if (order is null || item is null)
            return OperationResult.Error("این کالا قبلاً از سفارش حذف شده است.");
        if (!IsEditable(order.Status))
            return OperationResult.Error("وضعیت سفارش تغییر کرده و حذف کالا ممکن نیست.");

        var oldPayable = order.PayableAmount;
        order.Items.Remove(item);
        context.OrderItems.Remove(item);

        var remainingNet = order.Items.Sum(i => i.UnitPrice * i.Quantity - i.DiscountAmount);
        var orderDiscount = Math.Min(order.DiscountAmount, Math.Max(0, remainingNet + order.ShippingAmount));
        order.CouponDiscountAmount = Math.Min(order.CouponDiscountAmount, orderDiscount);
        order.DiscountAmount = orderDiscount;
        order.SubtotalAmount = order.Items.Sum(i => i.UnitPrice * i.Quantity);
        order.PayableAmount = Math.Max(0, remainingNet - orderDiscount + order.ShippingAmount);

        var refund = Math.Max(0, oldPayable - order.PayableAmount);
        var refunded = false;
        if (order.PaymentStatus == PaymentStatus.Paid && refund > 0 && order.UserId is { } userId)
        {
            // اعتبار هدیه (غیرقابل برداشت) اول برمی‌گردد؛ بنابراین سهم هدیهٔ باقی‌مانده هرگز از مبلغ جدید بیشتر نمی‌شود.
            var bonus = Math.Clamp(order.WalletBonusAmount - order.RefundedBonusAmount, 0, refund);
            var credit = await wallet.CreditAsync(new WalletEntry
            {
                UserId = userId,
                Amount = refund,
                NonWithdrawableAmount = bonus,
                Type = WalletTransactionType.OrderRefund,
                Description = $"بازگشت وجه «{Cut(itemName, 80)}» (حذف از سفارش {order.OrderNumber})",
                OrderId = order.Id,
            });
            if (!credit.IsSuccess())
            {
                await transaction.RollbackAsync();
                context.ChangeTracker.Clear();
                return OperationResult.Error(credit.Message);
            }

            order.WalletBonusAmount -= bonus;
            refunded = true;
        }

        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        return OperationResult.Success(refunded
            ? $"«{itemName}» از سفارش حذف شد و مبلغ {refund:N0} تومان به کیف پول شما برگشت."
            : $"«{itemName}» از سفارش حذف شد. مبلغ جدید سفارش: {order.PayableAmount:N0} تومان.");
    }

    public async Task MarkReadByCustomerAsync(Guid orderId, Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        await context.OrderMessages
            .Where(m => m.OrderId == orderId && m.FromStaff && m.ReadByCustomerAt == null && m.Order.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadByCustomerAt, (DateTimeOffset?)now));
    }

    public async Task MarkReadByStaffAsync(Guid orderId)
    {
        var now = DateTimeOffset.UtcNow;
        await context.OrderMessages
            .Where(m => m.OrderId == orderId && !m.FromStaff && m.ReadByStaffAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadByStaffAt, (DateTimeOffset?)now));
    }

    public Task<int> CountUnreadForStaffAsync() =>
        context.OrderMessages.CountAsync(m => !m.FromStaff && m.ReadByStaffAt == null);

    private async Task<bool> SetDecisionAsync(Guid messageId, OrderMessageDecision from, OrderMessageDecision to)
    {
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? decidedAt = to == OrderMessageDecision.Pending ? null : now;
        var affected = await context.OrderMessages
            .Where(m => m.Id == messageId && m.Decision == from)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Decision, to)
                .SetProperty(m => m.DecidedAt, decidedAt)
                .SetProperty(m => m.UpdatedAt, now));
        return affected == 1;
    }

    private Task NotifyCustomerAsync(Guid userId, string orderNumber, string text, string? itemName) =>
        notifications.NotifyAsync(new NotificationRequest
        {
            UserId = userId,
            Event = NotificationEvent.OrderMessage,
            Category = NotificationCategory.Order,
            LinkUrl = $"/profile/orders/{orderNumber.ToLowerInvariant()}#messages",
            Values =
            {
                ["OrderNumber"] = orderNumber,
                ["MessageText"] = Cut(text, 500),
                ["ItemName"] = itemName ?? "—",
            },
        });

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}
