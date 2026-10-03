using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Orders;

[Authorize(Policy = PolicyNames.Orders)]
public class IndexModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    IOrderService orders)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public OrderFilterResult Result { get; private set; } = null!;

    public OrderStatsDto Stats { get; private set; } = new();

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "status")]
    public OrderStatus? Status { get; set; }

    [BindProperty(SupportsGet = true, Name = "paymentStatus")]
    public PaymentStatus? PaymentStatusFilter { get; set; }

    [BindProperty(SupportsGet = true, Name = "pendingReview")]
    public bool PendingReview { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageId { get; set; } = 1;

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "مدیریت سفارش‌ها";

        Result = await orders.GetFilterAsync(new OrderFilterParams
        {
            Search = Search,
            Status = Status,
            PaymentStatus = PaymentStatusFilter,
            PendingReviewOnly = PendingReview ? true : null,
            PageId = PageId,
            Take = 20,
        });

        Stats = await orders.GetStatsAsync();
    }
}
