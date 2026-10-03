using Fratak_Shop.Core.DTOs.Blogs.Categories.Query;
using Fratak_Shop.Core.DTOs.Blogs.Posts.Query;
using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.Services.Blogs;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Pages;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

public class BlogModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IPostService posts,
    IBlogCategoryService blogCategories,
    IPageSeoService pageSeo,
    IPageAlertService pageAlerts)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    public BlogPostFilterResult Result { get; private set; } = null!;

    public List<BlogCategoryDto> Categories { get; private set; } = [];

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "category")]
    public string? CategorySlug { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageId { get; set; } = 1;

    public async Task<IActionResult> OnGetAsync()
    {
        Result = await posts.GetPostByFilterAsync(new BlogPostFilterParams
        {
            Search = Search,
            CategorySlug = CategorySlug,
            PageId = PageId,
            Take = 9,
        });

        Categories = blogCategories.GetAllCategoriesAsync();

        var seo = await pageSeo.GetAsync(SitePage.Blog);
        ApplySeo(seo?.Seo, new SeoFallback
        {
            SiteName = Site.SiteName,
            SiteUrl = Site.SiteUrl,
            DefaultDescription = Site.DefaultMetaDescription,
            DefaultKeywords = Site.DefaultMetaKeywords,
            Path = "/blog",
            ContentTitle = "مجله آموزشی",
            ShortDescription = "مقالات تخصصی خرید، راهنمای انتخاب کالا و تازه‌های دنیای فناوری.",
            EntityType = SeoEntityType.Page,
            ForceNoIndex = !string.IsNullOrWhiteSpace(Search),
        });

        var alerts = await pageAlerts.GetVisibleAsync(SitePage.Blog);
        if (alerts.Count > 0)
            ViewData["PageAlertsTop"] = alerts;

        return Page();
    }
}
