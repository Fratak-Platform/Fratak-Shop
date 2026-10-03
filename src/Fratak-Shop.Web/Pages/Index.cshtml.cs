using Fratak_Shop.Core.DTOs.Banners.Query;
using Fratak_Shop.Core.DTOs.Blogs.Posts.Query;
using Fratak_Shop.Core.DTOs.Categories.Query;
using Fratak_Shop.Core.DTOs.Faqs.Query;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.Services.Blogs;
using Fratak_Shop.Core.Services.Categories;
using Fratak_Shop.Core.Services.Faqs;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Pages;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

public class IndexModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IProductService products,
    ICategoryService categories,
    IBannerService banners,
    IPostService posts,
    IFaqItemService faqs,
    IPageSeoService pageSeo,
    IPageAlertService pageAlerts)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    public List<ProductCardDto> Featured { get; private set; } = [];
    public List<ProductCardDto> Discounted { get; private set; } = [];
    public List<ProductCardDto> Latest { get; private set; } = [];
    public List<ProductCardDto> BestSelling { get; private set; } = [];
    public List<CategoryMenuDto> Categories { get; private set; } = [];
    public List<BannerDto> HeroBanners { get; private set; } = [];
    public List<BlogPostFilterData> BlogPosts { get; private set; } = [];
    public List<FaqItemDto> Faqs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        Featured = await products.GetFeaturedAsync(10);
        Discounted = await products.GetDiscountedAsync(10);
        Latest = await products.GetLatestAsync(8);
        BestSelling = await products.GetBestSellingAsync(8);
        Categories = await categories.GetMenuAsync(8);
        Faqs = (await faqs.GetActiveFaqItemsAsync()).Take(6).ToList();

        var activeBanners = await banners.GetActiveBannersAsync();
        HeroBanners = activeBanners.Where(banner => banner.Position == BannerPosition.HomeHero).ToList();

        var postFilter = await posts.GetPostByFilterAsync(new BlogPostFilterParams { Take = 4 });
        BlogPosts = postFilter.Data;

        var seo = await pageSeo.GetAsync(SitePage.Home);
        ApplySeo(seo?.Seo, new SeoFallback
        {
            SiteName = Site.SiteName,
            SiteUrl = Site.SiteUrl,
            DefaultDescription = Site.DefaultMetaDescription,
            DefaultKeywords = Site.DefaultMetaKeywords,
            DefaultShareImageName = Site.DefaultShareImageName,
            Path = "/",
            ContentTitle = Site.SiteName,
            ShortDescription = Site.SiteDescription,
            EntityType = SeoEntityType.Page,
        });

        var alerts = await pageAlerts.GetVisibleAsync(SitePage.Home);
        if (alerts.Count > 0)
            ViewData["PageAlertsTop"] = alerts;

        return Page();
    }
}
