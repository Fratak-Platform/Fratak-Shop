using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

[IgnoreAntiforgeryToken]
public class Error404Model(IPageMetaService pageMeta) : ShopPageModel
{
    public async Task OnGetAsync()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        await pageMeta.ApplyAsync(ViewData, SitePage.NotFound, "صفحه پیدا نشد");
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
    }

    // The status-code page can be re-executed for any verb (e.g. a POST to a missing URL).
    public Task OnPostAsync() => OnGetAsync();
}
