using Fratak_Shop.Core.DTOs.Notifications.Query;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages;

/// <summary>ارسال اعلان همگانی و پیگیری اعلان‌های ارسال‌شده.</summary>
[Authorize(Policy = PolicyNames.AdminOnly)]
public class NotificationsModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public NotificationFilterResult Result { get; private set; } = null!;

    public static IReadOnlyList<NotificationType> Types { get; } =
        Enum.GetValues<NotificationType>().Where(type => type != NotificationType.None).ToList();

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "type")]
    public NotificationType? Type { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageId { get; set; } = 1;

    [BindProperty]
    public BroadcastInput Broadcast { get; set; } = new();

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "اعلان‌ها";
        Result = await notifications.GetFilterAsync(new NotificationFilterParams
        {
            Search = Search,
            Type = Type,
            PageId = PageId,
            Take = 15,
        });
    }

    /// <summary>ارسال اعلان همگانی به همه کاربران فروشگاه.</summary>
    public async Task<IActionResult> OnPostBroadcastAsync()
    {
        if (!ModelState.IsValid)
        {
            Notify("عنوان اعلان را وارد کنید.", "error");
            return RedirectToPage("/Notifications", new { area = "Admin" });
        }

        var result = await notifications.BroadcastAsync(Broadcast.Title, Broadcast.Message, Broadcast.Url,
            CurrentUserId, Broadcast.Type);

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Notifications", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await notifications.DeleteAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Notifications", new { area = "Admin" });
    }

    public class BroadcastInput
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "عنوان اعلان را وارد کنید.")]
        public string Title { get; set; } = null!;

        public string? Message { get; set; }
        public string? Url { get; set; }
        public NotificationType Type { get; set; } = NotificationType.Info;
    }
}
