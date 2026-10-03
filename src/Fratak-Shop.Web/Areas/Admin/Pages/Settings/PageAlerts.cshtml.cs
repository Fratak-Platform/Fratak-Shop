using Fratak_Shop.Core.DTOs.Pages.Query;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Pages;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Settings;

/// <summary>
/// هشدارهای قابل نمایش در صفحه‌های مختلف سایت (بالای محتوا، زیر تصویر محصول و ...)
/// که بدون تغییر کد و از پنل مدیریت کنترل می‌شوند.
/// </summary>
[Authorize(Policy = PolicyNames.Content)]
public class PageAlertsModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    IPageAlertService alerts)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public List<PageAlertDto> Items { get; private set; } = [];

    public static IReadOnlyList<SitePage> Pages { get; } =
        Enum.GetValues<SitePage>().Where(page => page != SitePage.None).ToList();

    public static IReadOnlyList<AlertPosition> Positions { get; } =
        Enum.GetValues<AlertPosition>().Where(position => position != AlertPosition.None).ToList();

    public static IReadOnlyList<AlertType> Types { get; } =
        Enum.GetValues<AlertType>().Where(type => type != AlertType.None).ToList();

    [BindProperty(SupportsGet = true)]
    public Guid? Edit { get; set; }

    [BindProperty]
    public AlertInput Input { get; set; } = new();

    public async Task OnGetAsync(SitePage? page = null)
    {
        ViewData["Title"] = "هشدارهای صفحه";
        Items = await alerts.GetAllAsync(page);

        if (Edit.HasValue)
        {
            var current = Items.FirstOrDefault(item => item.Id == Edit.Value);
            if (current is not null)
            {
                Input = new AlertInput
                {
                    Id = current.Id,
                    Page = current.Page,
                    Position = current.Position,
                    Type = current.Type,
                    Title = current.Title,
                    Content = current.Content,
                    DisplayOrder = current.DisplayOrder,
                    StartAt = current.StartAt,
                    EndAt = current.EndAt,
                    IsActive = current.IsActive,
                };
            }
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!ModelState.IsValid)
        {
            Notify("اطلاعات هشدار کامل نیست.", "error");
            return RedirectToPage("/Settings/PageAlerts", new { area = "Admin" });
        }

        var result = await alerts.SaveAsync(new PageAlertDto
        {
            Id = Input.Id ?? Guid.Empty,
            Page = Input.Page,
            Position = Input.Position,
            Type = Input.Type,
            Title = Input.Title,
            Content = Input.Content,
            DisplayOrder = Input.DisplayOrder,
            StartAt = Input.StartAt,
            EndAt = Input.EndAt,
            IsActive = Input.IsActive,
        });

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Settings/PageAlerts", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id)
    {
        var result = await alerts.ToggleActiveAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Settings/PageAlerts", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await alerts.DeleteAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Settings/PageAlerts", new { area = "Admin" });
    }

    public class AlertInput
    {
        public Guid? Id { get; set; }
        public SitePage Page { get; set; } = SitePage.Home;
        public AlertPosition Position { get; set; } = AlertPosition.AboveContent;
        public AlertType Type { get; set; } = AlertType.Info;
        public string? Title { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "متن هشدار را وارد کنید.")]
        public string Content { get; set; } = null!;

        public int DisplayOrder { get; set; }
        public DateTimeOffset? StartAt { get; set; }
        public DateTimeOffset? EndAt { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
