using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.DTOs.Notifications;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Loyalty;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Notifications;

public sealed class AdminBroadcastRow
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public NotificationAudience Audience { get; init; }
    public string? TierName { get; init; }
    public NotificationCategory Category { get; init; }
    public string? LinkUrl { get; init; }
    public bool IsPinned { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset? PublishAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public int Reads { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class BroadcastInput
{
    public NotificationAudience Audience { get; set; } = NotificationAudience.AllCustomers;
    public Guid? LoyaltyTierId { get; set; }
    public NotificationCategory Category { get; set; } = NotificationCategory.Promotion;
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string? LinkUrl { get; set; }
    public string? LinkText { get; set; }
    public bool IsPinned { get; set; }
    public string? PublishDate { get; set; }
    public string? ExpireDate { get; set; }
    public bool SendPush { get; set; }
}

public sealed class DirectInput
{
    public string? Phone { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string? LinkUrl { get; set; }
    public bool SendPush { get; set; } = true;
    public bool SendSms { get; set; }
    public bool SendEmail { get; set; }
}

[AdminPermission(AdminPermissions.NotificationsSend)]
public partial class IndexModel(IAdminDataService data, INotificationService notifications, IPushSender pushSender, IStoreSettingService settings, IMonitoringService monitoring, IWebPushService webPush) : AdminPageModel
{
    private const int MaxPushRecipients = 20000;

    /// <summary>برچسب باقی‌مانده پس از جایگزینی {SiteName}، مثلاً {FirstName}.</summary>
    [global::System.Text.RegularExpressions.GeneratedRegex(@"\{[A-Za-z]+\}")]
    private static partial global::System.Text.RegularExpressions.Regex PersonalPlaceholder();

    [BindProperty] public BroadcastInput Broadcast { get; set; } = new();
    [BindProperty] public DirectInput Direct { get; set; } = new();
    [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;
    [FromQuery(Name = "user")] public Guid? PrefillUserId { get; set; }

    public PagedList<AdminBroadcastRow> Items { get; private set; } = new([], 0, 1, 20);
    public List<LoyaltyTier> Tiers { get; private set; } = [];
    public int PushSubscribers { get; private set; }
    public int PushDevices { get; private set; }
    public int Customers { get; private set; }
    public bool PushEnabled { get; private set; }
    /// <summary>سلامت اشتراک‌های پوش همهٔ دستگاه‌ها (سالم / دارای خطا / آخرین تحویل موفق).</summary>
    public PushStats PushHealth { get; private set; } = new(0, 0, 0, null);
    /// <summary>نتیجهٔ آخرین تلاش ارسال پوش در این نمونهٔ برنامه (برای عیب‌یابی، مثلاً در دسترس نبودن FCM).</summary>
    public Fratak_Shop.Web.Infrastructure.Engagement.PushDeliveryResult? LastPush => Fratak_Shop.Web.Infrastructure.Engagement.WebPushDispatcher.LastResult;

    public async Task OnGetAsync()
    {
        if (PrefillUserId is { } userId)
            Direct.Phone = await data.Query<User>().Where(u => u.Id == userId).Select(u => u.PhoneNumber).FirstOrDefaultAsync();
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostBroadcastAsync()
    {
        var publish = PersianDateInput.Parse(Broadcast.PublishDate);
        var expire = PersianDateInput.Parse(Broadcast.ExpireDate);
        // پیام همگانی یک بار ذخیره و برای همه نمایش داده می‌شود؛ پس برچسب‌های شخصی ({FirstName} و ...) معنا ندارند.
        var broadcastTitle = await notifications.RenderForUserAsync(null, Broadcast.Title ?? "");
        var broadcastBody = await notifications.RenderForUserAsync(null, Broadcast.Body ?? "");
        if (PersonalPlaceholder().IsMatch(broadcastTitle) || PersonalPlaceholder().IsMatch(broadcastBody))
        {
            ErrorMessage = "در پیام همگانی فقط برچسب {SiteName} قابل استفاده است؛ برچسب‌های شخصی مثل {FirstName} را حذف کنید یا از «پیام اختصاصی» استفاده کنید.";
            await LoadAsync();
            return Page();
        }

        var command = new CreateBroadcastCommand
        {
            Audience = Broadcast.Audience,
            LoyaltyTierId = Broadcast.LoyaltyTierId,
            Category = Broadcast.Category,
            Title = broadcastTitle,
            Body = broadcastBody,
            LinkUrl = Broadcast.LinkUrl,
            LinkText = Broadcast.LinkText,
            IsPinned = Broadcast.IsPinned,
            PublishAt = publish is { } p ? IranClock.StartOfDay(p) : null,
            ExpiresAt = expire is { } e ? IranClock.StartOfDay(e.AddDays(1)) : null,
        };
        var result = await notifications.CreateBroadcastAsync(command);
        if (!Succeeded(result))
        {
            ErrorMessage = result.Message;
            await LoadAsync();
            return Page();
        }

        var pushed = 0;
        if (Broadcast.SendPush && (publish is null || IranClock.StartOfDay(publish.Value) <= DateTimeOffset.UtcNow))
            pushed = await PushToAudienceAsync(command);

        await monitoring.LogActionAsync("notification.broadcast", "Notification", null, $"{command.Title.ToPlainText(80)} → {AudienceTitle(command.Audience)}" + (pushed > 0 ? $" (پوش: {pushed})" : ""));
        SuccessMessage = result.Message + (pushed > 0 ? $" اعلان مرورگر برای {pushed} مشتری در صف ارسال قرار گرفت." : "");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDirectAsync()
    {
        var phone = SearchText.NormalizeDigits((Direct.Phone ?? "").Trim());
        var user = await data.Query<User>().Where(u => u.PhoneNumber == phone).Select(u => new { u.Id }).FirstOrDefaultAsync();
        var title = Direct.Title.ToPlainText(200);
        var body = Direct.Body.ToPlainText(2000);
        if (user is null)
            ErrorMessage = "مشتری با این شماره موبایل یافت نشد.";
        else if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
            ErrorMessage = "عنوان و متن پیام را وارد کنید.";
        else if (!string.IsNullOrWhiteSpace(Direct.LinkUrl) && !(Direct.LinkUrl.StartsWith('/') && !Direct.LinkUrl.StartsWith("//")) && !Direct.LinkUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            ErrorMessage = "لینک باید با / شروع شود یا آدرس کامل https باشد.";
        if (ErrorMessage is not null)
        {
            await LoadAsync();
            return Page();
        }

        await notifications.NotifyAsync(new NotificationRequest
        {
            UserId = user!.Id,
            Category = NotificationCategory.System,
            LinkUrl = string.IsNullOrWhiteSpace(Direct.LinkUrl) ? null : Direct.LinkUrl.Trim(),
            Content = new NotificationContent { Title = title, Body = body, InSite = true, Push = Direct.SendPush, SmsText = Direct.SendSms ? $"{title}\n{body}" : null, Email = Direct.SendEmail },
        });
        await monitoring.LogActionAsync("notification.direct", "User", user.Id, $"پیام مستقیم: {title}");
        SuccessMessage = "پیام برای مشتری ارسال شد.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id)
    {
        var notification = await data.FindAsync<Notification>(id);
        if (notification is null || notification.UserId is not null)
            return NotFound();
        notification.IsActive = !notification.IsActive;
        ShowResult(await data.UpdateAsync(notification));
        return BackToList();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var notification = await data.Query<Notification>().Where(n => n.Id == id).Select(n => new { n.UserId }).FirstOrDefaultAsync();
        if (notification is null || notification.UserId is not null)
            return NotFound();
        var result = await data.DeleteAsync<Notification>(id);
        if (Succeeded(result))
            await monitoring.LogActionAsync("notification.delete", "Notification", id, "حذف پیام همگانی");
        ShowResult(result);
        return BackToList();
    }

    private async Task<int> PushToAudienceAsync(CreateBroadcastCommand command)
    {
        var current = await settings.GetAsync();
        if (!current.CanUseWebPush)
            return 0;

        var users = data.Query<User>().Where(u => !u.IsBlocked && data.Query<WebPushSubscription>().Any(s => s.UserId == u.Id));
        switch (command.Audience)
        {
            case NotificationAudience.CustomersWithOrders: users = users.Where(u => u.Orders.Any()); break;
            case NotificationAudience.CustomersWithoutOrders: users = users.Where(u => !u.Orders.Any()); break;
            case NotificationAudience.LoyaltyTier:
                var tiers = await data.Query<LoyaltyTier>().Where(t => t.IsActive).OrderBy(t => t.MinLifetimePoints).ToListAsync();
                var index = tiers.FindIndex(t => t.Id == command.LoyaltyTierId);
                if (index < 0) return 0;
                var min = tiers[index].MinLifetimePoints;
                int? max = index + 1 < tiers.Count ? tiers[index + 1].MinLifetimePoints : null;
                users = users.Where(u => u.LoyaltyLifetimePoints >= min && (max == null || u.LoyaltyLifetimePoints < max));
                break;
        }

        var ids = await users.Select(u => u.Id).Take(MaxPushRecipients).ToListAsync();
        var title = command.Title.ToPlainText(120);
        var body = command.Body.ToPlainText(240);
        var url = string.IsNullOrWhiteSpace(command.LinkUrl) ? "/profile/notifications" : command.LinkUrl.Trim();
        foreach (var id in ids)
            pushSender.Enqueue(new PushMessage(id, title, body, url, "broadcast"));
        return ids.Count;
    }

    private async Task LoadAsync()
    {
        Tiers = await data.Query<LoyaltyTier>().Where(t => t.IsActive).OrderBy(t => t.MinLifetimePoints).ToListAsync();
        PushEnabled = (await settings.GetAsync()).CanUseWebPush;
        PushDevices = await data.Query<WebPushSubscription>().CountAsync();
        PushHealth = await webPush.GetStatsAsync(null);
        PushSubscribers = await data.Query<WebPushSubscription>().Where(s => s.UserId != null).Select(s => s.UserId).Distinct().CountAsync();
        Customers = await data.Query<User>().CountAsync();

        var query = data.Query<Notification>().Where(n => n.UserId == null);
        var total = await query.CountAsync();
        var pageNumber = ClampPage(PageNumber, total, 20);
        var rows = await query.OrderByDescending(n => n.CreatedAt).Skip((pageNumber - 1) * 20).Take(20)
            .Select(n => new AdminBroadcastRow
            {
                Id = n.Id, Title = n.Title, Body = n.Body, Audience = n.Audience, TierName = n.LoyaltyTier != null ? n.LoyaltyTier.Name : null,
                Category = n.Category, LinkUrl = n.LinkUrl, IsPinned = n.IsPinned, IsActive = n.IsActive, PublishAt = n.PublishAt, ExpiresAt = n.ExpiresAt,
                Reads = data.Query<NotificationRead>().Count(r => r.NotificationId == n.Id), CreatedAt = n.CreatedAt,
            })
            .ToListAsync();
        Items = new PagedList<AdminBroadcastRow>(rows, total, pageNumber, 20);
    }

    public static string AudienceTitle(NotificationAudience audience) => audience switch
    {
        NotificationAudience.AllCustomers => "همه مشتریان",
        NotificationAudience.CustomersWithOrders => "مشتریانی که خرید داشته‌اند",
        NotificationAudience.CustomersWithoutOrders => "کاربرانی که هنوز خرید نکرده‌اند",
        NotificationAudience.LoyaltyTier => "سطح باشگاه",
        _ => "یک کاربر",
    };

    public static string CategoryTitle(NotificationCategory category) => category switch
    {
        NotificationCategory.Order => "سفارش",
        NotificationCategory.Wallet => "کیف پول",
        NotificationCategory.Loyalty => "باشگاه",
        NotificationCategory.Promotion => "تخفیف و پیشنهاد",
        NotificationCategory.Security => "امنیت",
        NotificationCategory.Support => "پشتیبانی",
        _ => "اطلاعیه",
    };
}
