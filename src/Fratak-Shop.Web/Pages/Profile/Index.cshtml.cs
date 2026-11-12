using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.DTOs.Users.Query;
using Fratak_Shop.Core.Services.Favorites;
using Fratak_Shop.Core.Services.Support;
using Fratak_Shop.Core.Utilities;
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
    ITicketService ticketService,
    IPageMetaService pageMeta) : ShopPageModel
{
    public UserDto CurrentUser { get; private set; } = null!;
    public List<OrderFilterData> RecentOrders { get; private set; } = [];
    public int TotalOrders { get; private set; }
    public int DeliveredOrders { get; private set; }
    public int FavoriteCount { get; private set; }
    /// <summary>دور ۲۹: «پیغام‌ها» حذف شد؛ کارت دوم پیشخوان تعداد تیکت‌های پشتیبانی را نشان می‌دهد.</summary>
    public int TicketCount { get; private set; }

    /// <summary>دور ۲۹: سلام با زمان روز و خطاب مناسب نسل، مثلاً «صبح بخیر، محمد عزیز».</summary>
    public string Hello { get; private set; } = string.Empty;

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
        TicketCount = (await ticketService.GetUserTicketsAsync(user.Id)).Count;
        var now = IranClock.ToLocal(DateTimeOffset.UtcNow);
        Hello = UserGreeting.Hello(UserGreeting.For(user.FirstName, user.LastName, user.BirthDate, DateOnly.FromDateTime(now.DateTime)), now.Hour);

        await pageMeta.ApplyAsync(ViewData, SitePage.UserProfile, "پروفایل کاربری");
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        return Page();
    }
}
