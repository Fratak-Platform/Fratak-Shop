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

    /// <summary>Admin: sends a message to a customer group (stored once).</summary>
    Task<OperationResult> CreateBroadcastAsync(CreateBroadcastCommand command);
}

public partial class NotificationService(
    FratakShopDbContext context,
    IStoreSettingService settingService,
    IMemoryCache cache,
    ISmsSender smsSender,
    ILogger<NotificationService> logger) : INotificationService
{
    private static readonly TimeSpan UnreadCacheLifetime = TimeSpan.FromSeconds(60);

    public async Task NotifyAsync(NotificationRequest request)
    {
        try
        {
            var templates = await GetTemplatesAsync();
            if (!templates.TryGetValue(request.Event, out var template))
                return;

            var settings = await settingService.GetAsync();
            request.Values.TryAdd("SiteName", settings.SiteName);

            if (!request.Values.ContainsKey("Name"))
            {
                var name = await context.Users.Where(u => u.Id == request.UserId)
                    .Select(u => u.FirstName ?? "کاربر").FirstOrDefaultAsync();
                request.Values["Name"] = name ?? "کاربر";
            }

            if (template.IsActive)
            {
                context.Notifications.Add(new Notification
                {
                    UserId = request.UserId,
                    Audience = NotificationAudience.User,
                    Category = request.Category,
                    Event = request.Event,
                    Title = Limit(Render(template.Title, request.Values), 200),
                    Body = Limit(Render(template.Body, request.Values), 2000),
                    LinkUrl = IsSafeLink(request.LinkUrl) ? request.LinkUrl : null,
                    LinkText = template.LinkText,
                });

                await context.SaveChangesAsync();
                cache.Remove(UnreadKey(request.UserId));
            }

            if (template.SendSms && !string.IsNullOrWhiteSpace(template.SmsText))
            {
                var phone = await context.Users.Where(u => u.Id == request.UserId).Select(u => u.PhoneNumber).FirstOrDefaultAsync();
                if (!string.IsNullOrWhiteSpace(phone))
                    await smsSender.SendAsync(phone, Render(template.SmsText, request.Values));
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
        });

        await context.SaveChangesAsync();
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
                    || (n.Audience == NotificationAudience.LoyaltyTier && tierId != null && n.LoyaltyTierId == tierId)))));
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
