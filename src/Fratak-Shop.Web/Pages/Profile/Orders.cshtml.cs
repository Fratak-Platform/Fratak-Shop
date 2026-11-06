using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

public class OrdersModel(IOrderService orderService, IPageMetaService pageMeta) : ShopPageModel
{
    public List<OrderFilterData> Orders { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public OrderStatus? Status { get; set; }

    public static readonly (OrderStatus? Value, string Title)[] Tabs =
    [
        (null, "همه"),
        (OrderStatus.Pending, "در انتظار تایید"),
        (OrderStatus.Processing, "در حال پردازش"),
        (OrderStatus.Shipped, "ارسال شده"),
        (OrderStatus.Delivered, "تحویل شده"),
        (OrderStatus.Cancelled, "لغو شده"),
    ];

    public async Task OnGetAsync()
    {
        var orders = await orderService.GetOrdersByUserAsync(CurrentUserId!.Value);

        Orders = Status switch
        {
            null => orders,
            OrderStatus.Processing => orders.Where(o => o.Status is OrderStatus.Confirmed or OrderStatus.Processing).ToList(),
            OrderStatus.Cancelled => orders.Where(o => o.Status is OrderStatus.Cancelled or OrderStatus.Returned).ToList(),
            _ => orders.Where(o => o.Status == Status).ToList(),
        };

        await pageMeta.ApplyAsync(ViewData, SitePage.UserOrders, "سفارش های من");
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
    }
}
