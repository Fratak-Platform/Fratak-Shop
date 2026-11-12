using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// دور ۲۴ (پیشنهاد ۴۳): «راهنمای جامع خرید» یک دسته (/guide/{category}) – صفحهٔ ستون (pillar) که همهٔ صفحه‌های مرتبط
/// (دسته، دسته + برند، بازه‌های قیمت، مقاله‌ها، واژه‌نامه) را به هم وصل می‌کند. همهٔ بخش‌ها از دادهٔ واقعی دسته ساخته می‌شوند.
/// <para>دستهٔ کم‌محتوا (کمتر از ۶ کالا یا کمتر از دو بخش واقعی) ← noindex. دستهٔ ناموجود/سیستمی ← ۴۰۴.</para>
/// </summary>
[Microsoft.AspNetCore.OutputCaching.OutputCache(PolicyName = Fratak_Shop.Web.Infrastructure.PageCache.PageCachePolicy.Name)]
public class GuideModel(ISeoHubService hubs, IProductService products, IPageMetaService pageMeta) : ShopPageModel
{
    [FromRoute(Name = "category")]
    public string? CategorySlug { get; set; }

    public CategoryGuideView View { get; private set; } = null!;
    public List<ProductFilterData> TopProducts { get; private set; } = [];
    public string Heading { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        var view = await hubs.GuideAsync(CategorySlug ?? "", HttpContext.RequestAborted);
        if (view is null)
            return NotFound();
        View = view;
        if (view.TopProducts.Count > 0)
        {
            var cards = await products.GetCardsByIdsAsync(view.TopProducts, onlyInStock: true);
            TopProducts = view.TopProducts.Select(id => cards.FirstOrDefault(c => c.Id == id)).OfType<ProductFilterData>().ToList();
        }

        Heading = $"راهنمای جامع خرید {view.Name}";
        pageMeta.Apply(ViewData, null, Heading, GuideDescription(view), Helpers.ImageHelper.Category(view.ImageName));
        ViewData[PageMetaKeys.SitePage] = SitePage.ContentPage;
        pageMeta.AddBreadcrumbs(ViewData, ("محصولات", "/products"), (view.Name, $"/category/{view.Slug}"), ("راهنمای خرید", null));
        if (!view.Indexable)
            ViewData[PageMetaKeys.Robots] = "noindex, follow";
        else if (view.Questions.Count > 0)
            pageMeta.AddJsonLd(ViewData, new Dictionary<string, object>
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "FAQPage",
                ["mainEntity"] = view.Questions.Select(q => new Dictionary<string, object>
                {
                    ["@type"] = "Question",
                    ["name"] = q.Question,
                    ["acceptedAnswer"] = new Dictionary<string, object> { ["@type"] = "Answer", ["text"] = q.Answer },
                }).ToList(),
            });
        return Page();
    }

    /// <summary>
    /// توضیح متا از دادهٔ واقعی. مثال: «راهنمای خرید هدفون: مقایسهٔ ۲۴ مدل از ۶ برند، قیمت از ۴۵۰٬۰۰۰ تا ۸٬۹۰۰٬۰۰۰ تومان، …».
    /// </summary>
    public static string GuideDescription(CategoryGuideView view)
    {
        var parts = new List<string> { $"راهنمای خرید {view.Name}: مقایسهٔ {Fratak_Shop.Core.Services.Seo.Engine.PersianText.Digits(view.Facts.Count)} مدل" };
        if (view.Brands.Count > 1)
            parts[0] += $" از {Fratak_Shop.Core.Services.Seo.Engine.PersianText.Digits(view.Brands.Count)} برند";
        if (view.Facts.MinPrice is { } min && view.Facts.MaxPrice is { } max && max > min)
            parts.Add($"قیمت از {Helpers.Format.Toman(min)} تا {Helpers.Format.Toman(max)}");
        if (view.Specs.Count > 0)
            parts.Add("معنی مشخصات مهم و بازهٔ آن‌ها");
        parts.Add("پرسش‌های متداول و پرفروش‌ها");
        return string.Join("، ", parts) + ".";
    }
}
