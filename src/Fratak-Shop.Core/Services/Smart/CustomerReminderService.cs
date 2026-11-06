using Fratak_Shop.Core.DTOs.Notifications;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>نتیجهٔ یک دور کار یادآوری‌ها.</summary>
public sealed record ReminderRunResult(int Scheduled, int Sent, int Skipped, bool QuietHours);

/// <summary>آمار یادآوری‌ها برای پنل.</summary>
public sealed record ReminderStats(int Pending, int SentLast30Days, int SkippedLast30Days);

/// <summary>
/// یادآوری‌های مشتری (C19، C20، C21): «نظرتان دربارهٔ کالا چیست؟» بعد از تحویل و «وقت تهیهٔ دوباره است؟» برای کالای مصرفی.
/// <para>قانون‌های ضدمزاحمت (درخواست مالک: کاربر اذیت نشود):</para>
/// <list type="bullet">
/// <item>هیچ پیامی در ساعت سکوت شبانه (پیش‌فرض ۲۲ تا ۹ به وقت ایران) فرستاده نمی‌شود؛ صبح فرستاده می‌شود.</item>
/// <item>هر مشتری حداکثر یک یادآوری در ۷۲ ساعت؛ بقیه یک روز عقب می‌افتند.</item>
/// <item>درخواست نظر برای کالایی که مشتری قبلاً نظر داده، یا سفارشی که مرجوع/لغو شده فرستاده نمی‌شود.</item>
/// <item>یادآوری تمدید اگر مشتری خودش دوباره همان کالا را خریده یا کالا ناموجود است فرستاده نمی‌شود.</item>
/// <item>یادآوری‌ای که بیش از ۲۰ روز از موعدش گذشته (مثلاً سرور خاموش بوده) دیگر فرستاده نمی‌شود.</item>
/// </list>
/// <para>متن پیام: اگر ادمین برای رویداد قالب ساخته باشد همان، وگرنه متن‌های پیش‌فرض تنظیمات هوشمند.</para>
/// <para>محل استفاده: SmartWorker (هر ۳۰ دقیقه)؛ تنظیم در «پنل ← محتوای هوشمند ← تنظیمات».</para>
/// </summary>
public interface ICustomerReminderService
{
    Task<ReminderRunResult> RunAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<ReminderStats> StatsAsync(CancellationToken cancellationToken = default);
}

public sealed class CustomerReminderService(
    FratakShopDbContext context,
    ISmartConfigService config,
    INotificationService notifications,
    ISeoHost host,
    ILogger<CustomerReminderService> logger) : ICustomerReminderService
{
    /// <summary>فاصلهٔ حداقل بین دو یادآوری به یک مشتری.</summary>
    public static readonly TimeSpan PerUserGap = TimeSpan.FromHours(72);

    /// <summary>یادآوری که بیش از این از موعدش گذشته فرستاده نمی‌شود.</summary>
    public static readonly TimeSpan Staleness = TimeSpan.FromDays(20);

    public async Task<ReminderRunResult> RunAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var options = await config.GetAsync(cancellationToken);
        var scheduled = await ScheduleAsync(now, options, cancellationToken);
        if (CustomerQuietHours.IsQuiet(now, options.QuietFromHour, options.QuietToHour))
            return new ReminderRunResult(scheduled, 0, 0, true);
        var (sent, skipped) = await SendDueAsync(now, options, cancellationToken);
        return new ReminderRunResult(scheduled, sent, skipped, false);
    }

    /// <summary>ساخت یادآوری برای سفارش‌های تحویل‌شدهٔ ۶۰ روز اخیر (تکراری ساخته نمی‌شود؛ ایندکس یکتا هم محافظ است).</summary>
    private async Task<int> ScheduleAsync(DateTimeOffset now, SmartOptions options, CancellationToken cancellationToken)
    {
        var rules = options.ReplenishReminders ? options.ReplenishRules : [];
        if (!options.ReviewRequests && rules.Count == 0)
            return 0;
        var maxDays = Math.Max(options.ReviewRequests ? options.ReviewRequestAfterDays : 0, rules.Count == 0 ? 0 : rules.Max(r => r.Days));
        var since = now.AddDays(-(maxDays + 30));
        var items = await context.OrderItems
            .Where(i => i.Order.Status == OrderStatus.Delivered && i.Order.UserId != null && i.Order.DeliveredAt >= since && i.ProductId != null)
            .Select(i => new { i.OrderId, UserId = i.Order.UserId!.Value, ProductId = i.ProductId!.Value, DeliveredAt = i.Order.DeliveredAt!.Value, i.Product!.CategoryId })
            .OrderByDescending(i => i.DeliveredAt)
            .Take(2000)
            .ToListAsync(cancellationToken);
        if (items.Count == 0)
            return 0;

        var orderIds = items.Select(i => i.OrderId).Distinct().ToList();
        var existing = await context.CustomerReminders.IgnoreQueryFilters()
            .Where(r => orderIds.Contains(r.OrderId))
            .Select(r => new { r.Kind, r.OrderId, r.ProductId })
            .ToListAsync(cancellationToken);
        var seen = existing.Select(e => (e.Kind, e.OrderId, e.ProductId)).ToHashSet();

        var added = 0;
        foreach (var item in items)
        {
            if (options.ReviewRequests && seen.Add((CustomerReminderKind.ReviewRequest, item.OrderId, item.ProductId)))
            {
                context.CustomerReminders.Add(new CustomerReminder
                {
                    Kind = CustomerReminderKind.ReviewRequest, UserId = item.UserId, OrderId = item.OrderId, ProductId = item.ProductId,
                    DueAt = item.DeliveredAt.AddDays(options.ReviewRequestAfterDays), Status = CustomerReminderStatus.Pending,
                });
                added++;
            }
            var rule = rules.FirstOrDefault(r => r.CategoryId == item.CategoryId);
            if (rule is not null && seen.Add((CustomerReminderKind.Replenish, item.OrderId, item.ProductId)))
            {
                context.CustomerReminders.Add(new CustomerReminder
                {
                    Kind = CustomerReminderKind.Replenish, UserId = item.UserId, OrderId = item.OrderId, ProductId = item.ProductId,
                    DueAt = item.DeliveredAt.AddDays(rule.Days), Status = CustomerReminderStatus.Pending, Note = rule.Days.ToString(),
                });
                added++;
            }
            if (added >= 500)
                break;
        }
        if (added == 0)
            return 0;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // دو نمونهٔ برنامه هم‌زمان: ایندکس یکتا از تکرار جلوگیری کرد؛ دور بعد دوباره امتحان می‌شود.
            logger.LogInformation(exception, "Customer reminder scheduling raced; will retry next round");
            added = 0;
        }
        finally
        {
            context.ChangeTracker.Clear();
        }
        return added;
    }

    private async Task<(int Sent, int Skipped)> SendDueAsync(DateTimeOffset now, SmartOptions options, CancellationToken cancellationToken)
    {
        // یادآوری‌های خیلی قدیمی کنار گذاشته می‌شوند.
        var stale = now - Staleness;
        var skipped = await context.CustomerReminders
            .Where(r => r.Status == CustomerReminderStatus.Pending && r.DueAt < stale)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, CustomerReminderStatus.Skipped)
                .SetProperty(r => r.Note, "موعد خیلی گذشته بود").SetProperty(r => r.UpdatedAt, now), cancellationToken);

        var due = await context.CustomerReminders
            .Where(r => r.Status == CustomerReminderStatus.Pending && r.DueAt <= now)
            .OrderBy(r => r.DueAt).Take(40)
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
            return (0, skipped);

        var userIds = due.Select(r => r.UserId).Distinct().ToList();
        var recentlyContacted = (await context.CustomerReminders
            .Where(r => userIds.Contains(r.UserId) && r.Status == CustomerReminderStatus.Sent && r.SentAt > now - PerUserGap)
            .Select(r => r.UserId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        var templates = await context.NotificationTemplates
            .Where(t => t.Event == NotificationEvent.ProductReviewRequest || t.Event == NotificationEvent.ReplenishReminder)
            .Select(t => t.Event).ToListAsync(cancellationToken);
        var baseUrl = (host.BaseUrl ?? "https://shop.fratak.ir").TrimEnd('/');

        var sent = 0;
        foreach (var reminder in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (recentlyContacted.Contains(reminder.UserId))
            {
                await Postpone(reminder.Id, now.AddDays(1), cancellationToken);
                continue;
            }

            var product = await context.Products.Where(p => p.Id == reminder.ProductId)
                .Select(p => new { p.Name, p.Slug, p.IsActive, p.StockQuantity }).FirstOrDefaultAsync(cancellationToken);
            var reason = await SkipReasonAsync(reminder, product is not null && product.IsActive, product?.StockQuantity ?? 0, cancellationToken);
            if (reason is not null)
            {
                await Finish(reminder.Id, CustomerReminderStatus.Skipped, reason, now, cancellationToken);
                skipped++;
                continue;
            }

            var isReview = reminder.Kind == CustomerReminderKind.ReviewRequest;
            var @event = isReview ? NotificationEvent.ProductReviewRequest : NotificationEvent.ReplenishReminder;
            var path = "/product/" + product!.Slug + (isReview ? "#comments" : "");
            var request = new NotificationRequest
            {
                UserId = reminder.UserId,
                Event = @event,
                Category = isReview ? NotificationCategory.System : NotificationCategory.Promotion,
                LinkUrl = path,
                Content = templates.Contains(@event) ? null : new NotificationContent
                {
                    Title = isReview ? options.ReviewRequestTitle : options.ReplenishTitle,
                    Body = isReview ? options.ReviewRequestBody : options.ReplenishBody,
                    LinkText = isReview ? "ثبت نظر" : "مشاهدهٔ کالا",
                    InSite = true,
                    Push = true,
                    SmsText = isReview
                        ? options.ReviewRequestSms && !string.IsNullOrWhiteSpace(options.ReviewRequestSmsText) ? options.ReviewRequestSmsText : null
                        : string.IsNullOrWhiteSpace(options.ReplenishSmsText) ? null : options.ReplenishSmsText,
                },
            };
            request.Values["ProductName"] = product.Name;
            request.Values["Link"] = baseUrl + path;
            request.Values["Days"] = reminder.Note ?? string.Empty;
            try
            {
                await notifications.NotifyAsync(request);
                await Finish(reminder.Id, CustomerReminderStatus.Sent, null, now, cancellationToken);
                recentlyContacted.Add(reminder.UserId);
                sent++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Customer reminder {Id} failed", reminder.Id);
                await Postpone(reminder.Id, now.AddHours(6), cancellationToken);
            }
        }
        return (sent, skipped);
    }

    /// <summary>دلیل نفرستادن (یا null = بفرست).</summary>
    private async Task<string?> SkipReasonAsync(CustomerReminder reminder, bool productActive, int stock, CancellationToken cancellationToken)
    {
        if (!productActive)
            return "کالا غیرفعال است";
        var orderStatus = await context.Orders.Where(o => o.Id == reminder.OrderId).Select(o => (OrderStatus?)o.Status).FirstOrDefaultAsync(cancellationToken);
        if (orderStatus != OrderStatus.Delivered)
            return "سفارش دیگر در وضعیت تحویل‌شده نیست (مرجوع/لغو)";
        if (reminder.Kind == CustomerReminderKind.ReviewRequest)
        {
            var reviewed = await context.Comments.AnyAsync(c => c.UserId == reminder.UserId && c.ProductId == reminder.ProductId && c.Type == CommentType.Review, cancellationToken);
            return reviewed ? "مشتری قبلاً نظر داده است" : null;
        }
        if (stock <= 0)
            return "کالا ناموجود است";
        var orderDate = await context.Orders.Where(o => o.Id == reminder.OrderId).Select(o => o.CreatedAt).FirstAsync(cancellationToken);
        var reordered = await context.OrderItems.AnyAsync(i => i.ProductId == reminder.ProductId && i.Order.UserId == reminder.UserId
                                                               && i.Order.CreatedAt > orderDate && i.Order.Status != OrderStatus.Cancelled, cancellationToken);
        return reordered ? "مشتری دوباره خریده است" : null;
    }

    private Task Postpone(Guid id, DateTimeOffset dueAt, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return context.CustomerReminders.Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.DueAt, dueAt).SetProperty(r => r.UpdatedAt, now), cancellationToken);
    }

    private Task Finish(Guid id, CustomerReminderStatus status, string? note, DateTimeOffset now, CancellationToken cancellationToken)
    {
        DateTimeOffset? sentAt = status == CustomerReminderStatus.Sent ? now : null;
        return context.CustomerReminders.Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.SentAt, sentAt)
                .SetProperty(r => r.Note, r => note ?? r.Note)
                .SetProperty(r => r.UpdatedAt, now), cancellationToken);
    }

    public async Task<ReminderStats> StatsAsync(CancellationToken cancellationToken = default)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-30);
        var pending = await context.CustomerReminders.CountAsync(r => r.Status == CustomerReminderStatus.Pending, cancellationToken);
        var sent = await context.CustomerReminders.CountAsync(r => r.Status == CustomerReminderStatus.Sent && r.SentAt >= since, cancellationToken);
        var skipped = await context.CustomerReminders.CountAsync(r => r.Status == CustomerReminderStatus.Skipped && r.UpdatedAt >= since, cancellationToken);
        return new ReminderStats(pending, sent, skipped);
    }
}
