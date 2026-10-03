using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

[Authorize(Policy = PolicyNames.CustomerOnly)]
public class OrdersModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IOrderService orders)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    public List<OrderDto> Orders { get; private set; } = [];

    public OrderStatsDto Stats { get; private set; } = new();

    [BindProperty(SupportsGet = true)]
    public OrderStatus? Status { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (CurrentUserId is not { } userId)
            return Redirect("/login?returnUrl=/profile/orders");

        Orders = await orders.GetUserOrdersAsync(userId);

        if (Status.HasValue)
            Orders = Orders.Where(order => order.Status == Status.Value).ToList();

        Stats = new OrderStatsDto
        {
            TotalOrders = Orders.Count,
            PendingOrders = Orders.Count(order => order.Status == OrderStatus.Pending),
            PaidOrders = Orders.Count(order => order.PaymentStatus == PaymentStatus.Paid),
            TotalRevenue = Orders.Where(order => order.PaymentStatus == PaymentStatus.Paid).Sum(order => order.PayableAmount),
        };

        ApplySeo(null, new SeoFallback
        {
            SiteName = Site.SiteName,
            SiteUrl = Site.SiteUrl,
            Path = "/profile/orders",
            ContentTitle = "سفارش‌های من",
            EntityType = SeoEntityType.Page,
            ForceNoIndex = true,
        });

        return Page();
    }
}
