using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Pages;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

public class SearchModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IProductService products,
    IPageSeoService pageSeo)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    public ProductFilterResult Result { get; private set; } = null!;

    public List<ProductCardDto> QuickResults { get; private set; } = [];

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageId { get; set; } = 1;

    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

    public async Task<IActionResult> OnGetAsync()
    {
        Result = await products.GetFilterAsync(new ProductFilterParams
        {
            Search = Query,
            PageId = PageId,
            Take = 12,
            SortBy = ProductSortBy.BestRated,
        });

        if (HasQuery)
            QuickResults = await products.SearchAsync(Query!, 5);

        var seo = await pageSeo.GetAsync(SitePage.Search);
        ApplySeo(seo?.Seo, new SeoFallback
        {
            SiteName = Site.SiteName,
            SiteUrl = Site.SiteUrl,
            DefaultDescription = Site.DefaultMetaDescription,
            DefaultKeywords = Site.DefaultMetaKeywords,
            Path = $"/search?q={Query}",
            ContentTitle = HasQuery ? $"نتایج جستجو برای {Query}" : "جستجو در محصولات",
            ShortDescription = "جستجو میان هزاران کالای موجود با فیلترهای دقیق قیمت، دسته‌بندی و موجودی.",
            EntityType = SeoEntityType.Page,
            ForceNoIndex = true,
        });

        return Page();
    }
}
