using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.DTOs.Users.Query;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

[Authorize(Policy = PolicyNames.CustomerOnly)]
public class IndexModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IUserService users,
    IOrderService orders)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    public UserProfileDto Profile { get; private set; } = null!;

    public List<OrderDto> RecentOrders { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (CurrentUserId is not { } userId)
            return Redirect("/login?returnUrl=/profile");

        Profile = await users.GetProfileAsync(userId) ?? new UserProfileDto();
        RecentOrders = (await orders.GetUserOrdersAsync(userId)).Take(5).ToList();

        ApplySeo(null, new SeoFallback
        {
            SiteName = Site.SiteName,
            SiteUrl = Site.SiteUrl,
            Path = "/profile",
            ContentTitle = "پنل کاربری",
            EntityType = SeoEntityType.Page,
            ForceNoIndex = true,
        });

        return Page();
    }
}
