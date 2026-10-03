using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.DTOs.Categories.Query;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Services.Categories;
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

public class ProductsModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IProductService products,
    ICategoryService categories,
    IPageSeoService pageSeo,
    IPageAlertService pageAlerts)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    public ProductFilterResult Result { get; private set; } = null!;

    public List<CategoryDto> CategoryTree { get; private set; } = [];

    public ProductFilterParams Filter { get; private set; } = new();

    [BindProperty(SupportsGet = true, Name = "categoryId")]
    public Guid? CategoryId { get; set; }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "sortBy")]
    public ProductSortBy? SortBy { get; set; }

    [BindProperty(SupportsGet = true, Name = "onlyDiscounted")]
    public bool OnlyDiscounted { get; set; }

    [BindProperty(SupportsGet = true, Name = "onlyFeatured")]
    public bool OnlyFeatured { get; set; }

    [BindProperty(SupportsGet = true, Name = "onlyInStock")]
    public bool OnlyInStock { get; set; }

    [BindProperty(SupportsGet = true, Name = "minPrice")]
    public decimal? MinPrice { get; set; }

    [BindProperty(SupportsGet = true, Name = "maxPrice")]
    public decimal? MaxPrice { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageId { get; set; } = 1;

    public async Task<IActionResult> OnGetAsync()
    {
        Filter = new ProductFilterParams
        {
            Search = Search,
            CategoryId = CategoryId,
            SortBy = SortBy ?? ProductSortBy.Newest,
            OnlyDiscounted = OnlyDiscounted,
            OnlyFeatured = OnlyFeatured,
            OnlyInStock = OnlyInStock,
            MinPrice = MinPrice,
            MaxPrice = MaxPrice,
            PageId = PageId,
            Take = 12,
        };

        Result = await products.GetFilterAsync(Filter);
        CategoryTree = await categories.GetTreeAsync(onlyActive: true);

        var categoryName = CategoryId.HasValue
            ? CategoryTree.SelectMany(item => new[] { item }.Concat(item.SubCategories.Select(child => new CategoryDto
            {
                Id = child.Id,
                Name = child.Name,
                Slug = child.Slug,
            }))).FirstOrDefault(item => item.Id == CategoryId.Value)?.Name
            : null;

        var pageSeoModel = await pageSeo.GetAsync(SitePage.Products);
        ApplySeo(pageSeoModel?.Seo, new SeoFallback
        {
            SiteName = Site.SiteName,
            SiteUrl = Site.SiteUrl,
            DefaultDescription = Site.DefaultMetaDescription,
            DefaultKeywords = Site.DefaultMetaKeywords,
            DefaultShareImageName = Site.DefaultShareImageName,
            Path = CategoryId.HasValue ? $"/products?categoryId={CategoryId}" : "/products",
            ContentTitle = categoryName is null ? "فروشگاه" : $"محصولات {categoryName}",
            ShortDescription = categoryName is null
                ? "فهرست کامل محصولات فروشگاه با امکان فیلتر بر اساس دسته‌بندی، قیمت و موجودی."
                : $"خرید آنلاین {categoryName} با ضمانت اصالت کالا و ارسال سریع.",
            EntityType = SeoEntityType.Page,
            ForceNoIndex = !string.IsNullOrWhiteSpace(Search),
        });

        var alerts = await pageAlerts.GetVisibleAsync(SitePage.Products);
        if (alerts.Count > 0)
            ViewData["PageAlertsTop"] = alerts;

        return Page();
    }
}
