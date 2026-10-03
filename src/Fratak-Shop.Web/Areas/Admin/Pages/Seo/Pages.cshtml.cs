using Fratak_Shop.Core.DTOs.Content.Query;
using Fratak_Shop.Core.DTOs.Pages.Query;
using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.Mappers.Seo;
using Fratak_Shop.Core.Services.Content;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Pages;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Seo;

[Authorize(Policy = PolicyNames.Seo)]
public class PagesModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    IPageSeoService pageSeo,
    IContentPageService contentPages)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public List<PageSeoDto> Items { get; private set; } = [];

    public List<ContentPageDto> Contents { get; private set; } = [];

    public PageSeoDto? Current { get; private set; }

    public ContentPageDto? CurrentContent { get; private set; }

    [BindProperty(SupportsGet = true)]
    public SitePage? Page { get; set; }

    [BindProperty]
    public SeoFormInput Seo { get; set; } = new();

    [BindProperty]
    public ContentFormInput Content { get; set; } = new();

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "سئوی صفحات سایت";
        Items = await pageSeo.GetAllAsync();
        Contents = await contentPages.GetAllAsync();

        if (Page.HasValue && Page.Value != SitePage.None)
        {
            Current = Items.FirstOrDefault(item => item.Page == Page.Value);
            CurrentContent = Contents.FirstOrDefault(item => item.Page == Page.Value);
            Seo = SeoFormInput.FromEntity(Current?.Seo);

            if (CurrentContent is not null)
            {
                Content = new ContentFormInput
                {
                    Id = CurrentContent.Id,
                    Title = CurrentContent.Title,
                    Summary = CurrentContent.Summary,
                    Body = CurrentContent.Content,
                    IsPublished = CurrentContent.IsPublished,
                };
            }
        }
    }

    public async Task<IActionResult> OnPostSeoAsync()
    {
        if (!Page.HasValue)
            return RedirectToPage("/Seo/Pages", new { area = "Admin" });

        if (!ModelState.IsValid)
        {
            Notify("برخی مقادیر نامعتبر است.", "error");
            return RedirectToPage("/Seo/Pages", new { area = "Admin", page = Page });
        }

        var result = await pageSeo.SaveAsync(Page.Value, Seo.ToEntity());
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Seo/Pages", new { area = "Admin", page = Page });
    }

    public async Task<IActionResult> OnPostContentAsync()
    {
        var command = new Core.DTOs.Content.Command.SaveContentPageCommand
        {
            Id = Content.Id,
            Page = Page ?? SitePage.None,
            Title = Content.Title,
            Summary = Content.Summary,
            Content = Content.Body,
            IsPublished = Content.IsPublished,
        };

        var result = await contentPages.SaveAsync(command);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Seo/Pages", new { area = "Admin", page = Page });
    }

    /// <summary>
    /// «سئوی هوشمند» صفحه: تحلیل محتوای صفحه و برگرداندن پیشنهاد فیلدها برای بازبینی.
    /// نتیجه فقط در فرم قرار می‌گیرد و تا ذخیره‌نشدن روی محتوا اعمال نمی‌شود.
    /// </summary>
    public async Task<IActionResult> OnPostSmartSeoAsync()
    {
        if (Page is not { } page || page == SitePage.None)
            return new JsonResult(new { success = false, message = "ابتدا صفحه را انتخاب کنید." });

        var content = await contentPages.GetAsync(page);
        if (content is null)
            return new JsonResult(new { success = false, message = "ابتدا برای این صفحه محتوا ثبت کنید." });

        var result = await seoAudits.GenerateAsync(SeoEntityType.Page, content.Id, CurrentUserId);
        if (!result.IsSuccess() || result.Data is null)
            return new JsonResult(new { success = false, message = result.Message });

        return new JsonResult(new
        {
            success = true,
            message = $"تحلیل صفحه انجام شد؛ امتیاز فعلی {result.Data.Score} از ۱۰۰.",
            recordId = result.Data.Id,
            score = result.Data.Score,
            proposal = result.Data.Proposal,
            findings = result.Data.Findings.Select(finding => new
            {
                title = finding.Title,
                description = finding.Description,
                severityClass = Ui.SeverityClass(finding.Severity),
                severityTitle = finding.Severity switch
                {
                    SeoFindingSeverity.Critical => "بحرانی",
                    SeoFindingSeverity.Warning => "هشدار",
                    SeoFindingSeverity.Success => "بررسی‌شده",
                    _ => "اطلاع",
                },
            }),
        });
    }

    public class SeoFormInput
    {
        public string? Title { get; set; }
        public string? MetaDescription { get; set; }
        public string? MetaKeywords { get; set; }
        public string? FocusKeyword { get; set; }
        public string? CanonicalUrl { get; set; }
        public string? BreadcrumbTitle { get; set; }
        public string? OgTitle { get; set; }
        public string? OgDescription { get; set; }
        public string? XTitle { get; set; }
        public string? XDescription { get; set; }
        public string? SchemaType { get; set; }
        public string? StructuredDataJsonLd { get; set; }
        public string? Notes { get; set; }
        public bool NoIndex { get; set; }
        public bool NoFollow { get; set; }
        public bool IncludeInSitemap { get; set; } = true;
        public decimal? SitemapPriority { get; set; }
        public string? SitemapChangeFrequency { get; set; }

        public Data.Utilities.SeoManagement.SeoMetaData ToEntity() => new()
        {
            Title = Title,
            MetaDescription = MetaDescription,
            MetaKeywords = MetaKeywords,
            FocusKeyword = FocusKeyword,
            CanonicalUrl = CanonicalUrl,
            BreadcrumbTitle = BreadcrumbTitle,
            OgTitle = OgTitle,
            OgDescription = OgDescription,
            XTitle = XTitle,
            XDescription = XDescription,
            SchemaType = SchemaType,
            StructuredDataJsonLd = StructuredDataJsonLd,
            Notes = Notes,
            NoIndex = NoIndex,
            NoFollow = NoFollow,
            IncludeInSitemap = IncludeInSitemap,
            SitemapPriority = SitemapPriority,
            SitemapChangeFrequency = SitemapChangeFrequency,
        };

        public static SeoFormInput FromEntity(Data.Utilities.SeoManagement.SeoMetaData? seo) => new()
        {
            Title = seo?.Title,
            MetaDescription = seo?.MetaDescription,
            MetaKeywords = seo?.MetaKeywords,
            FocusKeyword = seo?.FocusKeyword,
            CanonicalUrl = seo?.CanonicalUrl,
            BreadcrumbTitle = seo?.BreadcrumbTitle,
            OgTitle = seo?.OgTitle,
            OgDescription = seo?.OgDescription,
            XTitle = seo?.XTitle,
            XDescription = seo?.XDescription,
            SchemaType = seo?.SchemaType,
            StructuredDataJsonLd = seo?.StructuredDataJsonLd,
            Notes = seo?.Notes,
            NoIndex = seo?.NoIndex ?? false,
            NoFollow = seo?.NoFollow ?? false,
            IncludeInSitemap = seo?.IncludeInSitemap ?? true,
            SitemapPriority = seo?.SitemapPriority,
            SitemapChangeFrequency = seo?.SitemapChangeFrequency,
        };
    }

    public class ContentFormInput
    {
        public Guid? Id { get; set; }
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public string? Body { get; set; }
        public bool IsPublished { get; set; } = true;
    }
}
