using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.DTOs.Users.Query;
using Fratak_Shop.Core.Services.Favorites;
using Fratak_Shop.Core.Services.Messages;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

public class IndexModel(
    IUserService userService,
    IOrderService orderService,
    IFavoriteService favoriteService,
    IMessageService messageService,
    IPageMetaService pageMeta) : ShopPageModel
{
    public UserDto CurrentUser { get; private set; } = null!;
    public List<OrderFilterData> RecentOrders { get; private set; } = [];
    public int TotalOrders { get; private set; }
    public int DeliveredOrders { get; private set; }
    public int FavoriteCount { get; private set; }
    public int MessageCount { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userService.GetUserByIdAsync(CurrentUserId!.Value);
        if (user is null)
            return RedirectToPage("/Logout");

        CurrentUser = user;

        var orders = await orderService.GetOrdersByUserAsync(user.Id);
        TotalOrders = orders.Count;
        DeliveredOrders = orders.Count(o => o.Status == OrderStatus.Delivered);
        RecentOrders = orders.Take(5).ToList();

        FavoriteCount = (await favoriteService.GetFavoritesByUserIdAsync(user.Id)).Count;
        MessageCount = (await messageService.GetMessagesByUserAsync(user.Id)).Count;

        await pageMeta.ApplyAsync(ViewData, SitePage.UserProfile, "پروفایل کاربری");
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        return Page();
    }
}
