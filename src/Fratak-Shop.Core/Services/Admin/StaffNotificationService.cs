using System.Text.Json;
using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Admin;

/// <summary>
/// یک اعلان داخلی که باید به کارکنان برسد.
/// <para>
/// <see cref="AnyOfPermissions"/>: کارکنانی که «حداقل یکی» از این مجوزها را دارند دریافت می‌کنند (مالک همیشه دریافت می‌کند).
/// اگر خالی باشد فقط مالک‌ها دریافت می‌کنند.
/// <see cref="ExcludeUserId"/>: معمولاً خود کسی که کار را انجام داده (به خودش اعلان نرود).
/// </para>
/// مثال: <c>new StaffAlert(StaffNotificationKind.NewTicket, "تیکت جدید T-1024", "مشکل در پرداخت", "/admin/tickets/…", [AdminPermissions.TicketsManage])</c>
/// </summary>
public sealed record StaffAlert(
    StaffNotificationKind Kind,
    string Title,
    string? Body,
    string? Url,
    IReadOnlyList<string> AnyOfPermissions,
    Guid? ExcludeUserId = null);

/// <summary>یک ردیف اعلان برای نمایش در منوی زنگوله و صفحهٔ «اعلان‌های من».</summary>
public sealed record StaffNotificationDto(Guid Id, StaffNotificationKind Kind, string Title, string? Body, string? Url, bool IsRead, DateTimeOffset CreatedAt);

/// <summary>
/// مرکز اعلان کارکنان پنل.
/// <para>
/// کار این سرویس: پیدا کردن گیرنده‌ها بر اساس مجوز، ذخیرهٔ اعلان برای هر نفر، ارسال زنده (SignalR)
/// به پنل‌های باز و ارسال پوش مرورگر برای وقتی که پنل بسته است.
/// </para>
/// چه کسی صدا می‌زند؟
/// - <c>Fratak_Shop.Web.Infrastructure.Realtime.StaffAlertDispatcher</c> (سفارش/تیکت/دیدگاه/پیام جدید به‌طور خودکار)؛
/// - صفحه‌های پنل برای منشن (@نام) و ارجاع کار؛
/// - کارهای زمان‌بندی‌شده (گزارش تأمین‌کننده، هشدارها).
/// </summary>
public interface IStaffNotificationService
{
    /// <summary>ارسال به همهٔ کارکنان دارای مجوز؛ تعداد گیرنده‌ها را برمی‌گرداند. هرگز خطا پرتاب نمی‌کند.</summary>
    Task<int> NotifyAsync(StaffAlert alert);

    /// <summary>ارسال به یک کارمند مشخص (مثلاً منشن یا ارجاع تیکت). هرگز خطا پرتاب نمی‌کند.</summary>
    Task NotifyUserAsync(Guid userId, StaffNotificationKind kind, string title, string? body, string? url);

    /// <summary>صفحه‌بندی اعلان‌های یک کارمند (جدیدترین اول).</summary>
    Task<PagedList<StaffNotificationDto>> GetPageAsync(Guid userId, int page, int take, bool unreadOnly);

    /// <summary>چند اعلان آخر برای منوی زنگوله.</summary>
    Task<List<StaffNotificationDto>> GetLatestAsync(Guid userId, int take = 8);

    /// <summary>تعداد خوانده‌نشده‌ها (۳۰ ثانیه کش می‌شود).</summary>
    Task<int> GetUnreadCountAsync(Guid userId);

    Task MarkReadAsync(Guid userId, Guid notificationId);

    Task MarkAllReadAsync(Guid userId);

    /// <summary>نوع‌هایی که کارمند بی‌صدا کرده است (از تنظیمات شخصی).</summary>
    Task<HashSet<StaffNotificationKind>> GetMutedKindsAsync(Guid userId);

    Task SetMutedKindsAsync(Guid userId, IEnumerable<StaffNotificationKind> kinds);

    /// <summary>پاک‌سازی اعلان‌های قدیمی (کار نگه‌داری روزانه).</summary>
    Task<int> PurgeAsync(TimeSpan olderThan);

    /// <summary>
    /// دور ۲۹ (#۲۸): اعلان‌هایی که بیش از <paramref name="days"/> روز از خوانده شدنشان گذشته کامل پاک می‌شوند (۰ = هرگز).
    /// مثال: days = 7 ← اعلانی که ۸ روز پیش خوانده شده پاک می‌شود؛ خوانده‌نشده‌ها هرگز.
    /// </summary>
    Task<int> PurgeReadAsync(int days, DateTimeOffset now);

    /// <summary>دور ۲۹ (#۲۸): حذف یک اعلان پنل به دست گیرندهٔ خودش (اعلان دیگران حذف نمی‌شود). خروجی: حذف شد؟</summary>
    Task<bool> DeleteAsync(Guid userId, Guid notificationId);

    /// <summary>دور ۲۹ (#۲۸): حذف همهٔ اعلان‌های خوانده‌شدهٔ همین کارمند. خروجی: تعداد.</summary>
    Task<int> DeleteReadAsync(Guid userId);

    /// <summary>شناسهٔ کارکنان فعالی که حداقل یکی از مجوزها را دارند (به‌علاوهٔ مالک‌ها).</summary>
    Task<List<Guid>> GetRecipientsAsync(IReadOnlyCollection<string> anyOfPermissions);
}

public sealed class StaffNotificationService(
    FratakShopDbContext context,
    IRealtimeNotifier realtime,
    IPushSender pushSender,
    IStoreSettingService settings,
    IMemoryCache cache,
    ILogger<StaffNotificationService> logger) : IStaffNotificationService
{
    /// <summary>کلید تنظیم شخصی که نوع‌های بی‌صدا را نگه می‌دارد (JSON آرایهٔ عدد).</summary>
    public const string MutedPreferenceKey = "staff-notify:muted";

    private static readonly TimeSpan UnreadLifetime = TimeSpan.FromSeconds(30);

    public async Task<int> NotifyAsync(StaffAlert alert)
    {
        try
        {
            var recipients = await GetRecipientsAsync(alert.AnyOfPermissions);
            if (alert.ExcludeUserId is { } excluded)
                recipients.Remove(excluded);

            var count = 0;
            foreach (var userId in recipients)
            {
                if (await DeliverAsync(userId, alert.Kind, alert.Title, alert.Body, alert.Url))
                    count++;
            }

            return count;
        }
        catch (Exception exception)
        {
            // اعلان «بهترین تلاش» است و هرگز نباید کار اصلی (ثبت سفارش و ...) را خراب کند.
            context.ChangeTracker.Clear();
            logger.LogError(exception, "Staff notification {Kind} could not be delivered.", alert.Kind);
            return 0;
        }
    }

    public async Task NotifyUserAsync(Guid userId, StaffNotificationKind kind, string title, string? body, string? url)
    {
        try
        {
            await DeliverAsync(userId, kind, title, body, url);
        }
        catch (Exception exception)
        {
            context.ChangeTracker.Clear();
            logger.LogError(exception, "Staff notification {Kind} for {UserId} could not be delivered.", kind, userId);
        }
    }

    /// <summary>
    /// ذخیره + ارسال زنده + پوش برای یک نفر. اگر کارمند این نوع را بی‌صدا کرده باشد چیزی ارسال نمی‌شود.
    /// </summary>
    private async Task<bool> DeliverAsync(Guid userId, StaffNotificationKind kind, string title, string? body, string? url)
    {
        if ((await GetMutedKindsAsync(userId)).Contains(kind))
            return false;

        var notification = new StaffNotification
        {
            RecipientUserId = userId,
            Kind = kind,
            Title = Limit(title.Trim(), 200),
            Body = string.IsNullOrWhiteSpace(body) ? null : Limit(body.Trim(), 500),
            // فقط آدرس داخلی مجاز است تا لینک مخرب در اعلان قرار نگیرد.
            Url = IsLocal(url) ? Limit(url!, 300) : null,
        };
        context.StaffNotifications.Add(notification);
        await context.SaveChangesAsync();
        cache.Remove(UnreadKey(userId));

        var unread = await GetUnreadCountAsync(userId);
        await realtime.SendToUserAsync(userId, RealtimeMethods.StaffNotification, new
        {
            id = notification.Id,
            kind = kind.ToString(),
            title = notification.Title,
            body = notification.Body,
            url = notification.Url,
            unread,
        });

        // پوش مرورگر: وقتی پنل بسته است هم کارمند باخبر می‌شود (اگر پوش در تنظیمات فعال باشد).
        var current = await settings.GetAsync();
        if (current.CanUseWebPush)
            pushSender.Enqueue(new PushMessage(userId, notification.Title, notification.Body ?? "", notification.Url ?? "/admin", "staff-" + kind.ToString().ToLowerInvariant()));

        return true;
    }

    public async Task<PagedList<StaffNotificationDto>> GetPageAsync(Guid userId, int page, int take, bool unreadOnly)
    {
        take = Math.Clamp(take, 10, 100);
        var query = context.StaffNotifications.Where(n => n.RecipientUserId == userId);
        if (unreadOnly)
            query = query.Where(n => n.ReadAt == null);

        var total = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)take));
        page = Math.Clamp(page, 1, pages);

        var items = await query.OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * take).Take(take)
            .Select(n => new StaffNotificationDto(n.Id, n.Kind, n.Title, n.Body, n.Url, n.ReadAt != null, n.CreatedAt))
            .ToListAsync();

        return new PagedList<StaffNotificationDto>(items, total, page, take);
    }

    public Task<List<StaffNotificationDto>> GetLatestAsync(Guid userId, int take = 8) =>
        context.StaffNotifications.Where(n => n.RecipientUserId == userId)
            .OrderByDescending(n => n.CreatedAt).Take(Math.Clamp(take, 1, 30))
            .Select(n => new StaffNotificationDto(n.Id, n.Kind, n.Title, n.Body, n.Url, n.ReadAt != null, n.CreatedAt))
            .ToListAsync();

    public async Task<int> GetUnreadCountAsync(Guid userId) =>
        await cache.GetOrCreateAsync(UnreadKey(userId), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = UnreadLifetime;
            return context.StaffNotifications.CountAsync(n => n.RecipientUserId == userId && n.ReadAt == null);
        });

    public async Task MarkReadAsync(Guid userId, Guid notificationId)
    {
        var item = await context.StaffNotifications.AsTracking()
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.RecipientUserId == userId && n.ReadAt == null);
        if (item is null)
            return;

        item.ReadAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();
        cache.Remove(UnreadKey(userId));
    }

    public async Task MarkAllReadAsync(Guid userId)
    {
        // بارگذاری و ذخیره (به‌جای ExecuteUpdate) تا روی دیتابیس آزمایشی InMemory هم کار کند؛ حداکثر ۵۰۰ ردیف در هر بار.
        var now = DateTimeOffset.UtcNow;
        var unread = await context.StaffNotifications.AsTracking()
            .Where(n => n.RecipientUserId == userId && n.ReadAt == null)
            .OrderBy(n => n.CreatedAt).Take(500).ToListAsync();
        foreach (var item in unread)
            item.ReadAt = now;

        await context.SaveChangesAsync();
        cache.Remove(UnreadKey(userId));
    }

    public async Task<HashSet<StaffNotificationKind>> GetMutedKindsAsync(Guid userId)
    {
        return await cache.GetOrCreateAsync(MutedKey(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            var json = await context.AdminUserPreferences
                .Where(p => p.UserId == userId && p.Key == MutedPreferenceKey)
                .Select(p => p.Value).FirstOrDefaultAsync();
            if (string.IsNullOrEmpty(json))
                return new HashSet<StaffNotificationKind>();

            try
            {
                return (JsonSerializer.Deserialize<int[]>(json) ?? [])
                    .Where(v => Enum.IsDefined(typeof(StaffNotificationKind), v))
                    .Select(v => (StaffNotificationKind)v).ToHashSet();
            }
            catch (JsonException)
            {
                return new HashSet<StaffNotificationKind>();
            }
        }) ?? [];
    }

    public async Task SetMutedKindsAsync(Guid userId, IEnumerable<StaffNotificationKind> kinds)
    {
        var json = JsonSerializer.Serialize(kinds.Distinct().Select(k => (int)k).OrderBy(k => k).ToArray());
        var row = await context.AdminUserPreferences.AsTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Key == MutedPreferenceKey);
        if (row is null)
            context.AdminUserPreferences.Add(new AdminUserPreference { UserId = userId, Key = MutedPreferenceKey, Value = json });
        else
            row.Value = json;

        await context.SaveChangesAsync();
        cache.Remove(MutedKey(userId));
    }

    public async Task<int> PurgeAsync(TimeSpan olderThan)
    {
        var cutoff = DateTimeOffset.UtcNow - olderThan;
        // حذف واقعی (نه حذف نرم) و دسته‌ای ۲۰۰۰تایی برای جلوگیری از قفل طولانی جدول؛
        // اعلان کارکنان سابقهٔ مهمی نیست و نگه داشتنش فقط جدول را بزرگ می‌کند.
        var total = 0;
        while (true)
        {
            var ids = await context.StaffNotifications.IgnoreQueryFilters()
                .Where(n => n.CreatedAt < cutoff).OrderBy(n => n.CreatedAt).Select(n => n.Id).Take(2000).ToListAsync();
            if (ids.Count == 0)
                return total;

            total += await context.StaffNotifications.IgnoreQueryFilters().Where(n => ids.Contains(n.Id)).ExecuteDeleteAsync();
            if (ids.Count < 2000)
                return total;
        }
    }

    public async Task<int> PurgeReadAsync(int days, DateTimeOffset now)
    {
        if (days <= 0)
            return 0;
        var cutoff = now.AddDays(-days);
        return await context.StaffNotifications.IgnoreQueryFilters()
            .Where(n => n.ReadAt != null && n.ReadAt < cutoff)
            .ExecuteDeleteAsync();
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid notificationId)
    {
        // شرط گیرنده داخل خود دستور حذف است ← هیچ کارمندی نمی‌تواند اعلان دیگری را با حدس شناسه پاک کند.
        var removed = await context.StaffNotifications.IgnoreQueryFilters()
            .Where(n => n.Id == notificationId && n.RecipientUserId == userId)
            .ExecuteDeleteAsync();
        cache.Remove(UnreadKey(userId));
        return removed > 0;
    }

    public async Task<int> DeleteReadAsync(Guid userId)
    {
        var removed = await context.StaffNotifications.IgnoreQueryFilters()
            .Where(n => n.RecipientUserId == userId && n.ReadAt != null)
            .ExecuteDeleteAsync();
        cache.Remove(UnreadKey(userId));
        return removed;
    }

    public async Task<List<Guid>> GetRecipientsAsync(IReadOnlyCollection<string> anyOfPermissions)
    {
        // مالک فروشگاه همیشه دریافت می‌کند؛ بقیه بر اساس مجوز نقش‌هایشان.
        var owners = await context.Users
            .Where(u => u.Role == UserRole.SuperAdmin && !u.IsBlocked)
            .Select(u => u.Id).ToListAsync();

        if (anyOfPermissions.Count == 0)
            return owners;

        var permissions = anyOfPermissions.ToList();
        var staff = await context.UserAdminRoles
            .Where(m => !m.User.IsBlocked && m.Role.Permissions.Any(p => permissions.Contains(p.Permission)))
            .Select(m => m.UserId).Distinct().ToListAsync();

        return owners.Union(staff).ToList();
    }

    private static bool IsLocal(string? url) =>
        !string.IsNullOrWhiteSpace(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.Contains('\\');

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];

    private static string UnreadKey(Guid userId) => $"admin:staff-notify:unread:{userId:N}";

    private static string MutedKey(Guid userId) => $"admin:staff-notify:muted:{userId:N}";
}
