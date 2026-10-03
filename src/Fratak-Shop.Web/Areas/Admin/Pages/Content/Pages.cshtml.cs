using Fratak_Shop.Core.DTOs.Content.Query;
using Fratak_Shop.Core.DTOs.Pages.Query;
using Fratak_Shop.Core.Services.Content;
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

namespace Fratak_Shop.Web.Areas.Admin.Pages.Content;

[Authorize(Policy = PolicyNames.Content)]
public class PagesModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    IContentPageService contentPages,
    IPageSeoService pageSeo)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public List<ContentPageDto> Items { get; private set; } = [];

    public List<SitePage> Pages { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public SitePage? Edit { get; set; }

    [BindProperty]
    public PageInput Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "صفحات سایت";
        Items = await contentPages.GetAllAsync();
        Pages = SitePageExtensions.ContentPages.ToList();

        if (Edit is { } page)
        {
            var current = Items.FirstOrDefault(item => item.Page == page);
            Input = new PageInput
            {
                Id = current?.Id,
                Page = page,
                Title = current?.Title ?? page.GetTitle(),
                Summary = current?.Summary,
                Content = current?.Content,
                IsPublished = current?.IsPublished ?? true,
                DisplayOrder = current?.DisplayOrder ?? 0,
            };
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!ModelState.IsValid)
        {
            Notify("اطلاعات صفحه کامل نیست.", "error");
            return RedirectToPage("/Content/Pages", new { area = "Admin", edit = Input.Page });
        }

        var result = await contentPages.SaveAsync(new Core.DTOs.Content.Command.SaveContentPageCommand
        {
            Id = Input.Id,
            Page = Input.Page,
            Title = Input.Title,
            Summary = Input.Summary,
            Content = Input.Content,
            IsPublished = Input.IsPublished,
            DisplayOrder = Input.DisplayOrder,
        });

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Content/Pages", new { area = "Admin", edit = Input.Page });
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id)
    {
        var result = await contentPages.TogglePublishedAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Content/Pages", new { area = "Admin" });
    }

    public class PageInput
    {
        public Guid? Id { get; set; }

        public SitePage Page { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "عنوان صفحه را وارد کنید.")]
        public string Title { get; set; } = null!;

        public string? Summary { get; set; }
        public string? Content { get; set; }
        public bool IsPublished { get; set; } = true;
        public int DisplayOrder { get; set; }
    }
}
