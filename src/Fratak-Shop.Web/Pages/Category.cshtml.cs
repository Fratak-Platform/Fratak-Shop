using Fratak_Shop.Core.DTOs.Categories.Query;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.Mappers.Seo;
using Fratak_Shop.Core.Services.Categories;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

public class CategoryModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    ICategoryService categories,
    IProductService products)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    public CategoryDetailDto Category { get; private set; } = null!;

    public ProductFilterResult Result { get; private set; } = null!;

    [BindProperty(SupportsGet = true, Name = "sortBy")]
    public ProductSortBy? SortBy { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageId { get; set; } = 1;

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        var category = await categories.GetDetailBySlugAsync(slug);
        if (category is null)
            return NotFound();

        Category = category;

        Result = await products.GetFilterAsync(new ProductFilterParams
        {
            CategorySlug = slug,
            SortBy = SortBy ?? ProductSortBy.Newest,
            PageId = PageId,
            Take = 12,
        });

        ApplySeo(category.Seo?.ToEntity(), new SeoFallback
        {
            SiteName = Site.SiteName,
            SiteUrl = Site.SiteUrl,
            DefaultDescription = Site.DefaultMetaDescription,
            DefaultKeywords = Site.DefaultMetaKeywords,
            DefaultShareImageName = Site.DefaultShareImageName,
            Path = $"/category/{slug}",
            ContentTitle = category.Name,
            ShortDescription = category.Description ?? $"خرید آنلاین {category.Name} با ارسال سریع و ضمانت اصالت کالا.",
            ImageName = category.ImageName,
            ImageFolder = "categories",
            ImageAlt = category.Name,
            EntityType = SeoEntityType.Category,
        });

        return Page();
    }
}
