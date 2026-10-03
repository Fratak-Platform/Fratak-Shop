using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.DTOs.Notifications.Query;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

[Authorize(Policy = PolicyNames.CustomerOnly)]
public class NotificationsModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    public List<NotificationDto> Items { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (CurrentUserId is not { } userId)
            return Redirect("/login?returnUrl=/profile/notifications");

        Items = await notifications.GetForUserAsync(userId, 50);

        ApplySeo(null, new SeoFallback
        {
            SiteName = Site.SiteName,
            SiteUrl = Site.SiteUrl,
            Path = "/profile/notifications",
            ContentTitle = "اعلان‌های من",
            EntityType = SeoEntityType.Page,
            ForceNoIndex = true,
        });

        return Page();
    }

    public async Task<IActionResult> OnPostReadAsync(Guid id)
    {
        await notifications.MarkAsReadAsync(id, CurrentUserId);
        return RedirectToPage("/Profile/Notifications");
    }

    public async Task<IActionResult> OnPostReadAllAsync()
    {
        if (CurrentUserId is { } userId)
            await notifications.MarkAllAsReadAsync(userId);

        Notify("همه اعلان‌ها خوانده‌شده علامت خوردند.");
        return RedirectToPage("/Profile/Notifications");
    }
}
