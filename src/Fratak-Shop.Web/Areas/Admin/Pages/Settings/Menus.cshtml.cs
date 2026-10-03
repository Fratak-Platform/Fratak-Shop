using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Core.DTOs.Navigation.Command;
using Fratak_Shop.Core.DTOs.Navigation.Query;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Navigation;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Settings;

/// <summary>
/// مدیریت منوهای هدر، ستون‌های فوتر و منوی پنل کاربری. هر آیتم منو قابل
/// فعال/غیرفعال‌کردن است، پس هیچ لینکی در قالب ثابت نیست.
/// </summary>
[Authorize(Policy = PolicyNames.SuperAdminOnly)]
public class MenusModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public List<MenuItemDto> Items { get; private set; } = [];

    public static IReadOnlyList<MenuLocation> Locations { get; } =
    [
        MenuLocation.Header,
        MenuLocation.Footer,
        MenuLocation.FooterBottom,
        MenuLocation.ProfileSidebar,
        MenuLocation.QuickAccess,
    ];

    [BindProperty(SupportsGet = true, Name = "location")]
    public MenuLocation Location { get; set; } = MenuLocation.Header;

    [BindProperty(SupportsGet = true)]
    public Guid? Edit { get; set; }

    [BindProperty]
    public MenuInput Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "منوها و فوتر";
        Input.Location = Location;
        Items = await menuServiceAllAsync();

        if (Edit.HasValue)
        {
            var current = Items.FirstOrDefault(item => item.Id == Edit.Value);
            if (current is not null)
            {
                Input = new MenuInput
                {
                    Id = current.Id,
                    Location = current.Location,
                    ParentId = current.ParentId,
                    Title = current.Title,
                    Url = current.Url,
                    Icon = current.Icon,
                    BadgeText = current.BadgeText,
                    BadgeColor = current.BadgeColor,
                    OpenInNewTab = current.OpenInNewTab,
                    ShowOnMobile = current.ShowOnMobile,
                    IsHighlighted = current.IsHighlighted,
                    DisplayOrder = current.DisplayOrder,
                    IsActive = current.IsActive,
                };
            }
        }
    }

    private async Task<List<MenuItemDto>> menuServiceAllAsync() => await menus.GetAllAsync();

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!ModelState.IsValid)
        {
            Notify("اطلاعات آیتم منو کامل نیست.", "error");
            return RedirectToPage("/Settings/Menus", new { area = "Admin", location = Input.Location });
        }

        var result = await menus.SaveAsync(new SaveMenuItemCommand
        {
            Id = Input.Id,
            Location = Input.Location,
            ParentId = Input.ParentId,
            Title = Input.Title,
            Url = Input.Url,
            Icon = Input.Icon,
            BadgeText = Input.BadgeText,
            BadgeColor = Input.BadgeColor,
            OpenInNewTab = Input.OpenInNewTab,
            ShowOnMobile = Input.ShowOnMobile,
            IsHighlighted = Input.IsHighlighted,
            DisplayOrder = Input.DisplayOrder,
            IsActive = Input.IsActive,
        });

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Settings/Menus", new { area = "Admin", location = Input.Location });
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id, MenuLocation location)
    {
        var result = await menus.ToggleActiveAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Settings/Menus", new { area = "Admin", location });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, MenuLocation location)
    {
        var result = await menus.DeleteAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Settings/Menus", new { area = "Admin", location });
    }

    public class MenuInput
    {
        public Guid? Id { get; set; }
        public MenuLocation Location { get; set; } = MenuLocation.Header;
        public Guid? ParentId { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "عنوان منو را وارد کنید.")]
        public string Title { get; set; } = null!;

        public string? Url { get; set; }
        public string? Icon { get; set; }
        public string? BadgeText { get; set; }
        public string? BadgeColor { get; set; }
        public bool OpenInNewTab { get; set; }
        public bool ShowOnMobile { get; set; } = true;
        public bool IsHighlighted { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
