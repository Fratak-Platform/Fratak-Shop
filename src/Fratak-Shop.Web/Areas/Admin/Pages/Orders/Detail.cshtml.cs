using Fratak_Shop.Core.DTOs.Orders.Command;
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
public class DetailModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    IOrderService orders)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public OrderDto Order { get; private set; } = null!;

    [BindProperty]
    public OrderStatus NewStatus { get; set; }

    [BindProperty]
    public string? StatusNote { get; set; }

    [BindProperty]
    public bool NotifyCustomer { get; set; } = true;

    [BindProperty]
    public string? TrackingCode { get; set; }

    [BindProperty]
    public string? TrackingUrl { get; set; }

    [BindProperty]
    public string? AdminNote { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        ViewData["Title"] = "جزئیات سفارش";

        var order = await orders.GetDetailAsync(id);
        if (order is null)
            return NotFound();

        Order = order;
        NewStatus = order.Status;
        TrackingCode = order.TrackingCode;
        TrackingUrl = order.TrackingUrl;
        AdminNote = order.AdminNote;
        return Page();
    }

    public async Task<IActionResult> OnPostStatusAsync(Guid id)
    {
        var result = await orders.ChangeStatusAsync(new ChangeOrderStatusCommand
        {
            OrderId = id,
            Status = NewStatus,
            Note = StatusNote,
            IsCustomerVisible = NotifyCustomer,
            ChangedByUserId = CurrentUserId,
        });

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Orders/Detail", new { area = "Admin", id });
    }

    public async Task<IActionResult> OnPostTrackingAsync(Guid id)
    {
        var result = await orders.SetTrackingAsync(new SetTrackingCommand
        {
            OrderId = id,
            TrackingCode = TrackingCode,
            TrackingUrl = TrackingUrl,
            Note = StatusNote,
            ChangedByUserId = CurrentUserId,
        });

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Orders/Detail", new { area = "Admin", id });
    }

    public async Task<IActionResult> OnPostNoteAsync(Guid id)
    {
        var result = await orders.SetAdminNoteAsync(id, AdminNote);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Orders/Detail", new { area = "Admin", id });
    }
}
