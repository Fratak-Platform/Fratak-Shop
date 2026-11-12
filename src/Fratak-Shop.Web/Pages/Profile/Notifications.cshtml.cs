using Fratak_Shop.Core.DTOs.Notifications;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

/// <summary>Personal notifications and store announcements, with read state.</summary>
public class NotificationsModel(INotificationService notificationService) : ShopPageModel
{
    [FromQuery(Name = "page")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public bool Unread { get; set; }

    public NotificationPage Notifications { get; private set; } = new();

    public Models.PagerModel Pager => new()
    {
        CurrentPage = Notifications.PageId,
        PageCount = Notifications.PageCount,
        StartPage = Math.Max(1, Notifications.PageId - 2),
        EndPage = Math.Min(Notifications.PageCount, Notifications.PageId + 2),
        UrlFor = p => "/profile/notifications" + (p > 1 || Unread ? "?" : string.Empty)
                      + string.Join("&", new[] { p > 1 ? $"page={p}" : null, Unread ? "unread=true" : null }.Where(x => x is not null)),
    };

    public async Task OnGetAsync()
    {
        ViewData[PageMetaKeys.Title] = "اعلان ها";
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        Notifications = await notificationService.GetPageAsync(CurrentUserId!.Value, Math.Max(1, PageNumber), 15, Unread);
    }

    /// <summary>Marks the notification read and follows its link (local links only).</summary>
    public async Task<IActionResult> OnPostOpenAsync(Guid id, string? url)
    {
        await notificationService.MarkReadAsync(CurrentUserId!.Value, id);
        return !string.IsNullOrWhiteSpace(url) && Url.IsLocalUrl(url) ? LocalRedirect(url) : RedirectToPage(new { page = (int?)null, unread = Unread ? true : (bool?)null });
    }

    public async Task<IActionResult> OnPostReadAsync(Guid id)
    {
        await notificationService.MarkReadAsync(CurrentUserId!.Value, id);
        return RedirectToPage(new { page = (int?)null, unread = Unread ? true : (bool?)null });
    }

    /// <summary>دور ۲۹ (#۲۴): حذف اعلان به دست خود مشتری؛ سرویس فقط اعلان «قابل حذف» همین کاربر را حذف می‌کند.</summary>
    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        ShowResult(await notificationService.DeleteAsync(CurrentUserId!.Value, id));
        return RedirectToPage(new { page = PageNumber > 1 ? PageNumber : (int?)null, unread = Unread ? true : (bool?)null });
    }

    public async Task<IActionResult> OnPostReadAllAsync()
    {
        ShowResult(await notificationService.MarkAllReadAsync(CurrentUserId!.Value));
        return RedirectToPage();
    }
}
