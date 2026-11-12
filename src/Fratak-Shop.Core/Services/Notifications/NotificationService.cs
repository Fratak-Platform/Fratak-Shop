using System.Text.RegularExpressions;
using Fratak_Shop.Core.DTOs.Notifications;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Notifications;

/// <summary>
/// In-site notifications: automatic personal messages from editable templates
/// (order, wallet, club, security events), broadcasts to customer groups and the
/// unread state shown in the header.
/// </summary>
public interface INotificationService
{
    /// <summary>Creates a personal notification from the event template. Never throws.</summary>
    Task NotifyAsync(NotificationRequest request);

    Task<NotificationPage> GetPageAsync(Guid userId, int pageId = 1, int pageSize = 15, bool unreadOnly = false);

    /// <summary>Unread personal + broadcast notifications (cached for a minute per customer).</summary>
    Task<int> GetUnreadCountAsync(Guid userId);

    Task<OperationResult> MarkReadAsync(Guid userId, Guid notificationId);

    Task<OperationResult> MarkAllReadAsync(Guid userId);

    /// <summary>
    /// دور ۲۹ (#۲۴): حذف یک اعلان به دست خود مشتری – فقط اگر با پرچم «قابل حذف» فرستاده شده باشد.
    /// اعلان شخصی ← حذف نرم؛ پیام همگانی ← فقط از فهرست همین مشتری برداشته می‌شود. بقیه ← خطا «فقط مدیر…».
    /// </summary>
    Task<OperationResult> DeleteAsync(Guid userId, Guid notificationId);

    /// <summary>
    /// دور ۲۹ (#۲۴): پاک‌سازی دوره‌ای – اعلان‌های مشتری قدیمی‌تر از <paramref name="days"/> روز (۰ = خاموش) کامل از پایگاه داده
    /// پاک می‌شوند (حذف‌نرم‌شده‌ها هم). پیام همگانیِ سنجاق‌شدهٔ هنوز معتبر می‌ماند. خروجی: تعداد اعلان پاک‌شده.
    /// </summary>
    Task<int> PurgeAsync(int days, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Admin: sends a message to a customer group (stored once).</summary>
    Task<OperationResult> CreateBroadcastAsync(CreateBroadcastCommand command);

    /// <summary>پیش‌نمایش قالب با دادهٔ واقعی کارمند (ویرایشگر قالب پیام در پنل).</summary>
    Task<NotificationPreview> PreviewAsync(NotificationEvent @event, Guid userId, string? title, string? body, string? sms);

    /// <summary>جایگزینی برچسب‌های متن آزاد (پاسخ آماده، اعلان تکی) با اطلاعات یک کاربر.</summary>
    Task<string> RenderForUserAsync(Guid? userId, string text, IReadOnlyDictionary<string, string?>? extra = null);
}

public partial class NotificationService(
    FratakShopDbContext context,
    IStoreSettingService settingService,
    IMemoryCache cache,
    ISmsSender smsSender,
    IPushSender pushSender,
    IRealtimeNotifier realtime,
    IEmailSender emailSender,
    ILogger<NotificationService> logger) : INotificationService
{
    private static readonly TimeSpan UnreadCacheLifetime = TimeSpan.FromSeconds(60);

    public async Task NotifyAsync(NotificationRequest request)
    {
        try
        {
            string title, body;
            string? linkText, smsText;
            bool inSite, push, email;
            if (request.Content is { } content)
            {
                (title, body, linkText, inSite, smsText) = (content.Title, content.Body, content.LinkText, content.InSite, content.SmsText);
                push = content.Push;
                email = content.Email;
            }
            else
            {
                var templates = await GetTemplatesAsync();
                if (!templates.TryGetValue(request.Event, out var template))
                    return;

                (title, body, linkText, inSite) = (template.Title, template.Body, template.LinkText, template.IsActive);
                smsText = template.SendSms ? template.SmsText : null;
                // Every channel is switchable per event (admin panel): in-site = IsActive, SMS, push.
                push = template.IsActive && template.SendPush;
                // مرحلهٔ دوازدهم: کانال ایمیل هم برای هر رویداد جدا روشن/خاموش می‌شود (پیش‌فرض خاموش).
                email = template.SendEmail;
            }

            var settings = await settingService.GetAsync();
            request.Values.TryAdd("SiteName", settings.SiteName);

            // پر کردن خودکار برچسب‌های استفاده‌شده (نام، موبایل، اطلاعات سفارش و ...).
            await EnrichAsync(request.UserId, request.Values, [title, body, smsText, "{Name}"]);

            if (inSite && !string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(body))
            {
                var notification = new Notification
                {
                    UserId = request.UserId,
                    Audience = NotificationAudience.User,
                    Category = request.Category,
                    Event = request.Event,
                    Title = Limit(Render(title, request.Values), 200),
                    Body = Limit(Render(body, request.Values), 2000),
                    LinkUrl = IsSafeLink(request.LinkUrl) ? request.LinkUrl : null,
                    LinkText = linkText is null ? null : Limit(linkText, 100),
                    UserDeletable = request.UserDeletable ?? NotificationRequest.DeletableByDefault(request.Category),
                };
                context.Notifications.Add(notification);

                await context.SaveChangesAsync();
                cache.Remove(UnreadKey(request.UserId));

                // ارسال زنده به پنجره‌های باز سایت (نمایش toast + صدا + به‌روزرسانی شمارندهٔ زنگوله).
                await realtime.SendToUserAsync(request.UserId, RealtimeMethods.Notification, new
                {
                    id = notification.Id,
                    title = notification.Title,
                    body = Limit(notification.Body, 300),
                    url = IsSafeLink(request.LinkUrl) ? request.LinkUrl : "/profile/notifications",
                    unread = await GetUnreadCountAsync(request.UserId),
                });
            }

            if (push && settings.CanUseWebPush && !string.IsNullOrWhiteSpace(title))
            {
                pushSender.Enqueue(new PushMessage(
                    request.UserId,
                    Limit(Render(title, request.Values), 120),
                    Limit(Render(body, request.Values), 240),
                    IsSafeLink(request.LinkUrl) ? request.LinkUrl : "/profile/notifications",
                    request.Event.ToString()));
            }

            var wantsSms = !string.IsNullOrWhiteSpace(smsText);
            var wantsEmail = email && !string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(body);
            if (wantsSms || wantsEmail)
            {
                // یک پرس‌وجو برای هر دو کانال. ایمیل فقط به نشانی «تأییدشده» می‌رود تا کسی با ثبت ایمیل دیگران از فروشگاه برایشان هرزنامه نفرستد.
                var contact = await context.Users.Where(u => u.Id == request.UserId)
                    .Select(u => new { u.PhoneNumber, u.Email, u.IsEmailVerified })
                    .FirstOrDefaultAsync();

                if (wantsSms && !string.IsNullOrWhiteSpace(contact?.PhoneNumber))
                    await smsSender.SendAsync(contact.PhoneNumber, Render(smsText!, request.Values));

                if (wantsEmail && contact is { IsEmailVerified: true } && EmailRules.IsValidAddress(contact.Email))
                {
                    emailSender.Enqueue(new EmailMessage(contact.Email!, Limit(Render(title, request.Values), 200), Limit(Render(body, request.Values), 4000))
                    {
                        ActionUrl = IsSafeLink(request.LinkUrl) ? request.LinkUrl : "/profile/notifications",
                        ActionText = string.IsNullOrWhiteSpace(linkText) ? null : Limit(linkText, 60),
                        Category = request.Event.ToString(),
                    });
                }
            }
        }
        catch (Exception exception)
        {
            // Notifications are best effort: they must never break an order or a payment.
            context.ChangeTracker.Clear();
            logger.LogError(exception, "Notification {Event} for user {UserId} could not be created.", request.Event, request.UserId);
        }
    }

    public async Task<NotificationPage> GetPageAsync(Guid userId, int pageId = 1, int pageSize = 15, bool unreadOnly = false)
    {
        pageSize = Math.Clamp(pageSize, 5, 50);
        var query = await VisibleAsync(userId);

        var projected = query.Select(n => new NotificationDto
        {
            Id = n.Id,
            Category = n.Category,
            Title = n.Title,
            Body = n.Body,
            LinkUrl = n.LinkUrl,
            LinkText = n.LinkText,
            IsPinned = n.IsPinned,
            IsBroadcast = n.UserId == null,
            IsRead = (n.UserId != null && n.ReadAt != null)
                     || (n.UserId == null && context.NotificationReads.Any(r => r.NotificationId == n.Id && r.UserId == userId)),
            UserDeletable = n.UserDeletable,
            CreatedAt = n.PublishAt ?? n.CreatedAt,
        });

        if (unreadOnly)
            projected = projected.Where(n => !n.IsRead);

        var total = await projected.CountAsync();
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        pageId = Math.Clamp(pageId, 1, pageCount);

        var items = await projected
            .OrderByDescending(n => n.IsPinned && !n.IsRead)
            .ThenByDescending(n => n.CreatedAt)
            .Skip((pageId - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new NotificationPage
        {
            Items = items,
            TotalCount = total,
            UnreadCount = await GetUnreadCountAsync(userId),
            PageId = pageId,
            PageCount = pageCount,
        };
    }

    public async Task<int> GetUnreadCountAsync(Guid userId)
    {
        return await cache.GetOrCreateAsync(UnreadKey(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = UnreadCacheLifetime;
            var query = await VisibleAsync(userId);
            return await query.CountAsync(n =>
                (n.UserId != null && n.ReadAt == null)
                || (n.UserId == null && !context.NotificationReads.Any(r => r.NotificationId == n.Id && r.UserId == userId)));
        });
    }

    public async Task<OperationResult> MarkReadAsync(Guid userId, Guid notificationId)
    {
        var query = await VisibleAsync(userId);
        var notification = await query.Where(n => n.Id == notificationId)
            .Select(n => new { n.Id, n.UserId })
            .FirstOrDefaultAsync();

        if (notification is null)
            return OperationResult.NotFound("اعلان مورد نظر یافت نشد.");

        var now = DateTimeOffset.UtcNow;
        if (notification.UserId.HasValue)
        {
            await context.Notifications
                .Where(n => n.Id == notificationId && n.UserId == userId && n.ReadAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now).SetProperty(n => n.UpdatedAt, now));
        }
        else
        {
            await AddBroadcastReadsAsync(userId, [notificationId], now);
        }

        cache.Remove(UnreadKey(userId));
        return OperationResult.Success("اعلان خوانده شد.");
    }

    public async Task<OperationResult> MarkAllReadAsync(Guid userId)
    {
        var now = DateTimeOffset.UtcNow;

        await context.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now).SetProperty(n => n.UpdatedAt, now));

        var query = await VisibleAsync(userId);
        var unreadBroadcasts = await query
            .Where(n => n.UserId == null && !context.NotificationReads.Any(r => r.NotificationId == n.Id && r.UserId == userId))
            .Select(n => n.Id)
            .ToListAsync();

        await AddBroadcastReadsAsync(userId, unreadBroadcasts, now);

        cache.Remove(UnreadKey(userId));
        return OperationResult.Success("همه اعلان‌ها خوانده شدند.");
    }

    public async Task<OperationResult> CreateBroadcastAsync(CreateBroadcastCommand command)
    {
        var title = command.Title.ToPlainText(200);
        var body = command.Body.ToPlainText(2000);

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
            return OperationResult.Error("عنوان و متن پیام را وارد کنید.");

        if (command.Audience == NotificationAudience.User)
            return OperationResult.Error("گروه مخاطبان را انتخاب کنید.");

        if (command.Audience == NotificationAudience.LoyaltyTier
            && (command.LoyaltyTierId is null || !await context.LoyaltyTiers.AnyAsync(t => t.Id == command.LoyaltyTierId)))
            return OperationResult.Error("سطح باشگاه مشتریان را انتخاب کنید.");

        if (!IsSafeLink(command.LinkUrl))
            return OperationResult.Error("لینک باید با / شروع شود یا آدرس کامل https باشد.");

        if (command.ExpiresAt.HasValue && command.PublishAt.HasValue && command.ExpiresAt <= command.PublishAt)
            return OperationResult.Error("تاریخ انقضا باید بعد از تاریخ انتشار باشد.");

        context.Notifications.Add(new Notification
        {
            Audience = command.Audience,
            LoyaltyTierId = command.Audience == NotificationAudience.LoyaltyTier ? command.LoyaltyTierId : null,
            Category = command.Category,
            Title = title,
            Body = body,
            LinkUrl = string.IsNullOrWhiteSpace(command.LinkUrl) ? null : command.LinkUrl.Trim(),
            LinkText = command.LinkText.ToPlainText(100) is { Length: > 0 } text ? text : null,
            IsPinned = command.IsPinned,
            PublishAt = command.PublishAt ?? DateTimeOffset.UtcNow,
            ExpiresAt = command.ExpiresAt,
            UserDeletable = command.UserDeletable,
        });

        await context.SaveChangesAsync();

        // اعلان همگانی که همین حالا منتشر شده: به همهٔ کاربران آنلاین خبر داده می‌شود.
        // برای گروه‌های خاص (سطح باشگاه و ...) فقط «شمارنده را تازه کن» فرستاده می‌شود تا هر مرورگر
        // خودش تعداد درست را بگیرد و پیام به کسی که مخاطب نیست نشان داده نشود.
        if (command.PublishAt is null || command.PublishAt <= DateTimeOffset.UtcNow)
        {
            if (command.Audience == NotificationAudience.AllCustomers)
                await realtime.SendToAllUsersAsync(RealtimeMethods.Notification, new { title, body = Limit(body, 300), url = "/profile/notifications", unread = (int?)null });
            else
                await realtime.SendToAllUsersAsync(RealtimeMethods.Refresh, new { scope = "notifications" });
        }

        return OperationResult.Success("پیام برای مخاطبان منتشر شد.");
    }

    /// <summary>Personal notifications of the user plus the broadcasts addressed to them.</summary>
    private async Task<IQueryable<Notification>> VisibleAsync(Guid userId)
    {
        var now = DateTimeOffset.UtcNow;

        var user = await context.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.LoyaltyLifetimePoints, HasOrders = u.Orders.Any() })
            .FirstOrDefaultAsync();

        var hasOrders = user?.HasOrders ?? false;
        var lifetime = user?.LoyaltyLifetimePoints ?? 0;

        var tierId = await context.LoyaltyTiers
            .Where(t => t.IsActive && t.MinLifetimePoints <= lifetime)
            .OrderByDescending(t => t.MinLifetimePoints)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync();

        return context.Notifications.Where(n => n.IsActive && (
            n.UserId == userId
            || (n.UserId == null
                && (n.PublishAt == null || n.PublishAt <= now)
                && (n.ExpiresAt == null || n.ExpiresAt > now)
                && (n.Audience == NotificationAudience.AllCustomers
                    || (n.Audience == NotificationAudience.CustomersWithOrders && hasOrders)
                    || (n.Audience == NotificationAudience.CustomersWithoutOrders && !hasOrders)
                    || (n.Audience == NotificationAudience.LoyaltyTier && tierId != null && n.LoyaltyTierId == tierId))
                // دور ۲۹: پیام همگانی که همین مشتری از فهرستش برداشته دیگر دیده نمی‌شود.
                && !context.NotificationReads.Any(r => r.NotificationId == n.Id && r.UserId == userId && r.HiddenAt != null))));
    }

    public async Task<OperationResult> DeleteAsync(Guid userId, Guid notificationId)
    {
        var query = await VisibleAsync(userId);
        var notification = await query.Where(n => n.Id == notificationId)
            .Select(n => new { n.Id, n.UserId, n.UserDeletable })
            .FirstOrDefaultAsync();
        if (notification is null)
            return OperationResult.NotFound("اعلان مورد نظر یافت نشد.");
        if (!notification.UserDeletable)
            return OperationResult.Error("این اعلان مهم است و فقط مدیر فروشگاه می‌تواند حذفش کند.");

        var now = DateTimeOffset.UtcNow;
        if (notification.UserId.HasValue)
        {
            // حذف نرم (برای گزارش و بازگردانی مدیر می‌ماند؛ پاک‌سازی دوره‌ای بعداً کامل پاکش می‌کند).
            await context.Notifications
                .Where(n => n.Id == notificationId && n.UserId == userId && n.UserDeletable)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsDeleted, true).SetProperty(n => n.DeletedAt, now).SetProperty(n => n.UpdatedAt, now));
        }
        else
        {
            // پیام همگانی: فقط برای همین مشتری پنهان می‌شود (ردیف «خوانده‌شده» با زمان پنهان شدن).
            var updated = await context.NotificationReads
                .Where(r => r.NotificationId == notificationId && r.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.HiddenAt, now).SetProperty(r => r.UpdatedAt, now));
            if (updated == 0)
            {
                context.NotificationReads.Add(new NotificationRead { NotificationId = notificationId, UserId = userId, ReadAt = now, HiddenAt = now });
                try
                {
                    await context.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // هم‌زمان «خوانده شد» ثبت شد (کلید یکتا) ← همان ردیف را پنهان می‌کنیم.
                    context.ChangeTracker.Clear();
                    await context.NotificationReads
                        .Where(r => r.NotificationId == notificationId && r.UserId == userId)
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.HiddenAt, now).SetProperty(r => r.UpdatedAt, now));
                }
            }
        }

        cache.Remove(UnreadKey(userId));
        return OperationResult.Success("اعلان حذف شد.");
    }

    public async Task<int> PurgeAsync(int days, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var customer = 0;
        if (days > 0)
        {
            var before = now.AddDays(-days);
            // اول ردیف‌های «خوانده‌شده»ی پیام‌های همگانی قدیمی (کلید خارجی)، بعد خود اعلان‌ها – شامل حذف‌نرم‌شده‌ها.
            // ردیف‌های پیام سنجاق‌شدهٔ هنوز معتبر می‌مانند (وگرنه دوباره «خوانده‌نشده» یا پنهان‌شده دوباره دیده می‌شد).
            await context.NotificationReads.IgnoreQueryFilters()
                .Where(r => r.Notification.CreatedAt < before
                            && !(r.Notification.UserId == null && r.Notification.IsPinned && r.Notification.IsActive
                                 && (r.Notification.ExpiresAt == null || r.Notification.ExpiresAt > now)))
                .ExecuteDeleteAsync(cancellationToken);
            customer = await context.Notifications.IgnoreQueryFilters()
                .Where(n => n.CreatedAt < before && !(n.UserId == null && n.IsPinned && n.IsActive && (n.ExpiresAt == null || n.ExpiresAt > now)))
                .ExecuteDeleteAsync(cancellationToken);
        }
        return customer;
    }

    private async Task AddBroadcastReadsAsync(Guid userId, List<Guid> notificationIds, DateTimeOffset now)
    {
        if (notificationIds.Count == 0)
            return;

        var existing = await context.NotificationReads
            .Where(r => r.UserId == userId && notificationIds.Contains(r.NotificationId))
            .Select(r => r.NotificationId)
            .ToListAsync();

        foreach (var id in notificationIds.Except(existing))
            context.NotificationReads.Add(new NotificationRead { NotificationId = id, UserId = userId, ReadAt = now });

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Marked read in parallel (unique index) – nothing to do.
            context.ChangeTracker.Clear();
        }
    }

    private async Task<Dictionary<NotificationEvent, NotificationTemplate>> GetTemplatesAsync()
    {
        return await cache.GetOrCreateAsync(CacheKeys.NotificationTemplates, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheKeys.DefaultLifetime;
            var templates = await context.NotificationTemplates.ToListAsync();
            return templates.GroupBy(t => t.Event).ToDictionary(g => g.Key, g => g.First());
        }) ?? [];
    }

    private static string Render(string template, IReadOnlyDictionary<string, string?> values) =>
        PlaceholderRegex().Replace(template, match =>
            values.TryGetValue(match.Groups[1].Value, out var value) ? value ?? string.Empty : string.Empty).Trim();

    private static bool IsSafeLink(string? url) =>
        string.IsNullOrWhiteSpace(url)
        || (url.StartsWith('/') && !url.StartsWith("//") && !url.Contains('\\'))
        || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];

    private static string UnreadKey(Guid userId) => $"core:notifications:unread:{userId:N}";

    [GeneratedRegex(@"\{([A-Za-z]+)\}")]
    private static partial Regex PlaceholderRegex();
}
