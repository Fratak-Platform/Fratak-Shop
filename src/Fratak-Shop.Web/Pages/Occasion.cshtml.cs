using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// دور ۲۴ (پیشنهاد ۴۴): صفحهٔ فرود مناسبت (/occasion/{slug}، مثلاً /occasion/yalda). نامک بدون سال است تا اعتبار صفحه هر سال
/// بیشتر شود؛ کالاها همیشه کالاهای واقعیِ موجود (بلک فرایدی: فقط تخفیف‌دار) و شمارش معکوس از تقویم شمسی/قمری.
/// کمتر از ۶ کالای موجود ← noindex. نامک ناشناخته ← ۴۰۴.
/// </summary>
[Microsoft.AspNetCore.OutputCaching.OutputCache(PolicyName = Fratak_Shop.Web.Infrastructure.PageCache.PageCachePolicy.Name)]
public class OccasionModel(ISeoHubService hubs, IProductService products, IPageMetaService pageMeta) : ShopPageModel
{
    [FromRoute(Name = "slug")]
    public string? Slug { get; set; }

    public OccasionView View { get; private set; } = null!;
    public List<ProductFilterData> Products { get; private set; } = [];

    /// <summary>مناسبت‌های دیگر ۶۰ روز آینده (لینک داخلی).</summary>
    public List<SeoOccasion> Others { get; private set; } = [];

    public string Heading { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        var view = await hubs.OccasionAsync(Slug ?? "", HttpContext.RequestAborted);
        if (view is null)
            return NotFound();
        View = view;
        if (view.Products.Count > 0)
        {
            var cards = await products.GetCardsByIdsAsync(view.Products, onlyInStock: true);
            Products = view.Products.Select(id => cards.FirstOrDefault(c => c.Id == id)).OfType<ProductFilterData>().ToList();
        }
        Others = SeoCalendar.Upcoming(IranClock.Today(DateTimeOffset.UtcNow), Array.Empty<string>(), 60)
            .Where(o => o.Slug != view.Occasion.Slug).ToList();

        Heading = OccasionTitle(view.Occasion);
        pageMeta.Apply(ViewData, null, Heading, view.Intro);
        ViewData[PageMetaKeys.SitePage] = SitePage.ContentPage;
        pageMeta.AddBreadcrumbs(ViewData, (view.Occasion.Name, null));
        if (!view.Indexable)
            ViewData[PageMetaKeys.Robots] = "noindex, follow";
        return Page();
    }

    /// <summary>عنوان صفحه. مثال: «شب یلدا» ← «پیشنهاد خرید و هدیهٔ شب یلدا»؛ «بلک فرایدی» ← «تخفیف‌های بلک فرایدی».</summary>
    public static string OccasionTitle(SeoOccasion occasion) =>
        occasion.Slug == "black-friday" ? $"تخفیف‌های {occasion.Name}"
        : occasion.Slug == "back-to-school" ? $"خرید برای {occasion.Name}"
        : $"پیشنهاد خرید و هدیهٔ {occasion.Name}";
}
