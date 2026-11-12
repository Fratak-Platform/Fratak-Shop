using System.Threading.Channels;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Messages;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Support;
using Fratak_Shop.Data.Entities.Wallets;
using Fratak_Shop.Data.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Infrastructure.Realtime;

/// <summary>
/// تبدیل خودکار «ردیف تازه در دیتابیس» به اعلان کارکنان؛ بدون تغییر در سرویس‌های تیکت/دیدگاه/پیام/مرجوعی/برداشت.
/// <para>
/// روش کار:
/// ۱) بعد از هر ذخیرهٔ موفق، <see cref="DataChangeNotifier.EntitiesAdded"/> فهرست ردیف‌های تازه را می‌دهد؛
/// ۲) اینجا ردیف‌های مهم (تیکت، پاسخ مشتری به تیکت، پیام تماس، دیدگاه در انتظار، مرجوعی، درخواست برداشت)
///    به یک «هشدار» تبدیل و در صف گذاشته می‌شوند (کار سنگینی در مسیر درخواست کاربر انجام نمی‌شود)؛
/// ۳) سرویس پس‌زمینه کمی صبر می‌کند، مطمئن می‌شود ردیف واقعاً در دیتابیس هست (تراکنش برگشت نخورده)
///    و با <see cref="IStaffNotificationService"/> برای کارکنانِ دارای مجوز اعلان می‌سازد.
/// </para>
/// سفارش‌ها اینجا نیستند: اعلان سفارش در <c>ShopEvents</c> بعد از پرداخت ساخته می‌شود تا سفارشِ پرداخت‌نشده اعلام نشود.
/// برای افزودن نوع جدید کافی است یک case به <see cref="Map"/> اضافه کنید.
/// </summary>
public sealed class StaffAlertDispatcher(IServiceScopeFactory scopeFactory, ILogger<StaffAlertDispatcher> logger) : BackgroundService
{
    public const string JobName = "اعلان زندهٔ کارکنان";

    /// <summary>صف محدود: در شرایط سیل درخواست، قدیمی‌ترین‌ها حذف می‌شوند تا حافظه پر نشود.</summary>
    private readonly Channel<PendingAlert> _queue = Channel.CreateBounded<PendingAlert>(new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        DataChangeNotifier.EntitiesAdded += OnEntitiesAdded;
        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        DataChangeNotifier.EntitiesAdded -= OnEntitiesAdded;
        return base.StopAsync(cancellationToken);
    }

    /// <summary>در مسیر درخواست کاربر اجرا می‌شود؛ پس فقط نگاشت سبک و گذاشتن در صف.</summary>
    private void OnEntitiesAdded(IReadOnlyList<object> entities)
    {
        // تیکت‌هایی که در همین ذخیره ساخته شده‌اند: پیام اولشان جداگانه اعلام نمی‌شود.
        var newTickets = entities.OfType<SupportTicket>().Select(t => t.Id).ToHashSet();
        foreach (var entity in entities)
        {
            if (Map(entity, newTickets) is { } alert)
                _queue.Writer.TryWrite(alert);
        }
    }

    /// <summary>نگاشت یک ردیف تازه به هشدار کارکنان (یا null اگر مهم نیست).</summary>
    internal static PendingAlert? Map(object entity, IReadOnlySet<Guid> newTickets) => entity switch
    {
        SupportTicket ticket => new PendingAlert(typeof(SupportTicket), ticket.Id, new StaffAlert(
            StaffNotificationKind.NewTicket,
            $"تیکت جدید {ticket.TicketNumber}",
            ticket.Subject,
            $"/admin/tickets/{ticket.Id}",
            [AdminPermissions.TicketsManage])),

        SupportTicketMessage { IsStaff: false } message when !newTickets.Contains(message.TicketId) => new PendingAlert(typeof(SupportTicketMessage), message.Id, new StaffAlert(
            StaffNotificationKind.TicketReply,
            $"پاسخ مشتری در تیکت ({message.AuthorName})",
            Excerpt(message.Content),
            $"/admin/tickets/{message.TicketId}",
            [AdminPermissions.TicketsManage])),

        Message contact => new PendingAlert(typeof(Message), contact.Id, new StaffAlert(
            StaffNotificationKind.NewMessage,
            $"پیام جدید از {(string.IsNullOrWhiteSpace(contact.FullName) ? "بازدیدکننده" : contact.FullName)}",
            contact.Subject ?? Excerpt(contact.Content),
            "/admin/messages",
            [AdminPermissions.MessagesManage])),

        Comment { Status: CommentStatus.Pending } comment => new PendingAlert(typeof(Comment), comment.Id, new StaffAlert(
            StaffNotificationKind.NewComment,
            comment.Type == CommentType.Question ? $"پرسش جدید از {comment.AuthorName}"
                : global::Fratak_Shop.Core.Services.Learning.ReviewSentiment.Analyze(comment.Content, comment.Rating, comment.IsRecommended).IsNegative
                    ? $"دیدگاه منفی از {comment.AuthorName} (نیاز به پیگیری)"
                    : $"دیدگاه جدید از {comment.AuthorName}",
            Excerpt(comment.Content),
            "/admin/comments",
            [AdminPermissions.CommentsModerate])),

        ReturnRequest request => new PendingAlert(typeof(ReturnRequest), request.Id, new StaffAlert(
            StaffNotificationKind.NewReturn,
            $"درخواست مرجوعی {request.RequestNumber}",
            request.ReasonTitle,
            $"/admin/returns/{request.Id}",
            [AdminPermissions.ReturnsManage])),

        WithdrawalRequest withdrawal => new PendingAlert(typeof(WithdrawalRequest), withdrawal.Id, new StaffAlert(
            StaffNotificationKind.NewWithdrawal,
            "درخواست برداشت از کیف پول",
            $"{withdrawal.AccountHolder} – {withdrawal.Amount:N0} تومان",
            "/admin/finance/withdrawals",
            [AdminPermissions.WithdrawalsManage])),

        _ => null,
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // صف‌محور است: فقط وقتی کاری هست اجرا می‌شود، پس هرگز «متوقف‌شده» گزارش نشود.
        Admin.JobMonitor.Register(JobName, TimeSpan.FromDays(3650), pausable: false);
        try
        {
            await foreach (var alert in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await Admin.JobMonitor.TrackAsync(JobName, () => DeliverAsync(alert, stoppingToken));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception, "Staff alert {Kind} failed.", alert.Alert.Kind);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // خاموش شدن برنامه
        }
    }

    private async Task DeliverAsync(PendingAlert pending, CancellationToken cancellationToken)
    {
        // کمی صبر تا اگر ذخیره داخل یک تراکنش بزرگ‌تر بوده، تراکنش کامل (یا برگشت) شود.
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        if (!await ExistsAsync(context, pending, cancellationToken))
            return;

        // شمارنده‌های منوی پنل (تیکت/پیام/دیدگاه باز) فوراً تازه شوند، نه پس از ۳۰ ثانیه کش.
        scope.ServiceProvider.GetRequiredService<Fratak_Shop.Web.Infrastructure.Admin.AdminBadgeService>().Invalidate();

        var staff = scope.ServiceProvider.GetRequiredService<IStaffNotificationService>();
        await staff.NotifyAsync(pending.Alert);
    }

    private static Task<bool> ExistsAsync(FratakShopDbContext context, PendingAlert pending, CancellationToken cancellationToken) =>
        pending.EntityType.Name switch
        {
            nameof(SupportTicket) => context.SupportTickets.AnyAsync(e => e.Id == pending.EntityId, cancellationToken),
            nameof(SupportTicketMessage) => context.SupportTicketMessages.AnyAsync(e => e.Id == pending.EntityId, cancellationToken),
            nameof(Message) => context.Messages.AnyAsync(e => e.Id == pending.EntityId, cancellationToken),
            nameof(Comment) => context.Comments.AnyAsync(e => e.Id == pending.EntityId && e.Status == CommentStatus.Pending, cancellationToken),
            nameof(ReturnRequest) => context.ReturnRequests.AnyAsync(e => e.Id == pending.EntityId, cancellationToken),
            nameof(WithdrawalRequest) => context.WithdrawalRequests.AnyAsync(e => e.Id == pending.EntityId, cancellationToken),
            _ => Task.FromResult(false),
        };

    private static string Excerpt(string? text)
    {
        var value = (text ?? string.Empty).ReplaceLineEndings(" ").Trim();
        return value.Length <= 140 ? value : value[..139] + "…";
    }

    /// <summary>یک هشدار در صف: نوع و شناسهٔ ردیف (برای بررسی وجود) + متن اعلان.</summary>
    internal sealed record PendingAlert(Type EntityType, Guid EntityId, StaffAlert Alert);
}
