using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;

namespace Fratak_Shop.Web.Pages;

public class AboutUsModel(IPageMetaService pageMeta) : ShopPageModel
{
    public Task OnGetAsync() =>
        pageMeta.ApplyAsync(ViewData, SitePage.AboutUs, "درباره ما", "آشنایی با فروشگاه، ارزش ها و خدمات ما.");
}
