using Fratak_Shop.Core.DTOs.Banners.Query;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Media;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Marketing;

[Authorize(Policy = PolicyNames.Content)]
public class BannersModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    IBannerService banners,
    IMediaService media)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public List<BannerDto> Items { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public Guid? Edit { get; set; }

    [BindProperty]
    public BannerInput Input { get; set; } = new();

    [BindProperty]
    public IFormFile? Image { get; set; }

    public void OnGet()
    {
        ViewData["Title"] = "مدیریت بنرها";
        Items = banners.GetAllBannersAsync();

        if (Edit.HasValue)
        {
            var current = Items.FirstOrDefault(item => item.Id == Edit.Value);
            if (current is not null)
            {
                Input = new BannerInput
                {
                    Id = current.Id,
                    Title = current.Title,
                    Subtitle = current.Subtitle,
                    ImageName = current.ImageName,
                    ImageAlt = current.ImageAlt,
                    LinkUrl = current.LinkUrl,
                    ButtonText = current.ButtonText,
                    Position = current.Position,
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
            Notify("اطلاعات بنر کامل نیست.", "error");
            return RedirectToPage("/Marketing/Banners", new { area = "Admin" });
        }

        var imageName = Input.ImageName;
        if (Image is { Length: > 0 })
        {
            var saved = await media.SaveImageAsync(Image, MediaFolders.Banners);
            if (saved.IsSuccess())
                imageName = saved.Data;
            else
            {
                Notify(saved.Message, "error");
                return RedirectToPage("/Marketing/Banners", new { area = "Admin" });
            }
        }

        if (string.IsNullOrWhiteSpace(imageName) && Input.Id is null)
        {
            Notify("تصویر بنر الزامی است.", "error");
            return RedirectToPage("/Marketing/Banners", new { area = "Admin" });
        }

        var result = Input.Id.HasValue
            ? await banners.EditBannerAsync(new Core.DTOs.Banners.Command.EditBannerCommand
            {
                Id = Input.Id.Value,
                Title = Input.Title,
                Subtitle = Input.Subtitle,
                ImageName = imageName!,
                ImageAlt = Input.ImageAlt,
                LinkUrl = Input.LinkUrl,
                ButtonText = Input.ButtonText,
                Position = Input.Position,
                DisplayOrder = Input.DisplayOrder,
                StartAt = Input.StartAt,
                EndAt = Input.EndAt,
                IsActive = Input.IsActive,
            })
            : await banners.CreateBannerAsync(new Core.DTOs.Banners.Command.CreateBannerCommand
            {
                Title = Input.Title,
                Subtitle = Input.Subtitle,
                ImageName = imageName!,
                ImageAlt = Input.ImageAlt,
                LinkUrl = Input.LinkUrl,
                ButtonText = Input.ButtonText,
                Position = Input.Position,
                DisplayOrder = Input.DisplayOrder,
                StartAt = Input.StartAt,
                EndAt = Input.EndAt,
                IsActive = Input.IsActive,
            });

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Marketing/Banners", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await banners.DeleteBannerAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Marketing/Banners", new { area = "Admin" });
    }

    public class BannerInput
    {
        public Guid? Id { get; set; }
        public string? Title { get; set; }
        public string? Subtitle { get; set; }
        public string? ImageName { get; set; }
        public string? ImageAlt { get; set; }
        public string? LinkUrl { get; set; }
        public string? ButtonText { get; set; }
        public BannerPosition Position { get; set; } = BannerPosition.HomeHero;
        public int DisplayOrder { get; set; }
        public DateTimeOffset? StartAt { get; set; }
        public DateTimeOffset? EndAt { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
