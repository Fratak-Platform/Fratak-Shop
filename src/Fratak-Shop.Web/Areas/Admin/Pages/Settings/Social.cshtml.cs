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

/// <summary>مدیریت شبکه‌های اجتماعی هدر و فوتر.</summary>
[Authorize(Policy = PolicyNames.SuperAdminOnly)]
public class SocialModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public List<SocialLinkDto> Items { get; private set; } = [];

    public static IReadOnlyList<SocialPlatform> Platforms { get; } =
        Enum.GetValues<SocialPlatform>().Where(platform => platform != SocialPlatform.None).ToList();

    [BindProperty(SupportsGet = true)]
    public Guid? Edit { get; set; }

    [BindProperty]
    public SocialInput Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "شبکه‌های اجتماعی";
        Items = await menus.GetAllSocialLinksAsync();

        if (Edit.HasValue)
        {
            var current = Items.FirstOrDefault(item => item.Id == Edit.Value);
            if (current is not null)
            {
                Input = new SocialInput
                {
                    Id = current.Id,
                    Platform = current.Platform,
                    Url = current.Url,
                    Title = current.Title,
                    ShowInHeader = current.ShowInHeader,
                    ShowInFooter = current.ShowInFooter,
                    DisplayOrder = current.DisplayOrder,
                    IsActive = current.IsActive,
                };
            }
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!ModelState.IsValid)
        {
            Notify("اطلاعات شبکه اجتماعی کامل نیست.", "error");
            return RedirectToPage("/Settings/Social", new { area = "Admin" });
        }

        var result = await menus.SaveSocialLinkAsync(new SaveSocialLinkCommand
        {
            Id = Input.Id,
            Platform = Input.Platform,
            Url = Input.Url,
            Title = Input.Title,
            ShowInHeader = Input.ShowInHeader,
            ShowInFooter = Input.ShowInFooter,
            DisplayOrder = Input.DisplayOrder,
            IsActive = Input.IsActive,
        });

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Settings/Social", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id)
    {
        var result = await menus.ToggleSocialLinkAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Settings/Social", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await menus.DeleteSocialLinkAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Settings/Social", new { area = "Admin" });
    }

    public class SocialInput
    {
        public Guid? Id { get; set; }
        public SocialPlatform Platform { get; set; } = SocialPlatform.Instagram;
        public string? Url { get; set; }
        public string? Title { get; set; }
        public bool ShowInHeader { get; set; }
        public bool ShowInFooter { get; set; } = true;
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
