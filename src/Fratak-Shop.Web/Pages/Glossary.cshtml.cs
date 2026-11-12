using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// دور ۲۴ (پیشنهاد ۴۲): واژه‌نامهٔ اصطلاحات فنی (/glossary). اصطلاح‌ها فقط وقتی نمایش داده می‌شوند که کالای واقعی با آن مشخصه
/// در فروشگاه باشد؛ هر اصطلاح نشانی ثابت دارد (<c>/glossary#ip-rating</c>) و به دستهٔ مرتبط لینک می‌شود.
/// دادهٔ ساخت‌یافته: DefinedTermSet + DefinedTerm. کمتر از ۵ اصطلاح ← noindex.
/// </summary>
[Microsoft.AspNetCore.OutputCaching.OutputCache(PolicyName = Fratak_Shop.Web.Infrastructure.PageCache.PageCachePolicy.Name)]
public class GlossaryModel(ISeoHubService hubs, IPageMetaService pageMeta) : ShopPageModel
{
    public GlossaryView View { get; private set; } = new([]);

    public async Task OnGetAsync()
    {
        View = await hubs.GlossaryAsync(HttpContext.RequestAborted);
        const string title = "واژه‌نامهٔ مشخصات فنی کالاهای دیجیتال";
        pageMeta.Apply(ViewData, null, title,
            "معنی ساده اصطلاحات فنی مثل استاندارد IP، حذف نویز فعال، نسخه بلوتوث، رفرش ریت و شارژ سریع؛ برای انتخاب آگاهانه پیش از خرید.");
        ViewData[PageMetaKeys.SitePage] = SitePage.ContentPage;
        pageMeta.AddBreadcrumbs(ViewData, ("واژه‌نامه", null));
        if (!View.Indexable)
        {
            ViewData[PageMetaKeys.Robots] = "noindex, follow";
            return;
        }
        var url = pageMeta.AbsoluteUrl("/glossary");
        pageMeta.AddJsonLd(ViewData, new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "DefinedTermSet",
            ["@id"] = url,
            ["name"] = title,
            ["hasDefinedTerm"] = View.Terms.Select(t => new Dictionary<string, object?>
            {
                ["@type"] = "DefinedTerm",
                ["@id"] = url + "#" + t.Slug,
                ["name"] = t.Name,
                ["description"] = t.Definition,
                ["inDefinedTermSet"] = url,
            }).ToList(),
        });
    }
}
