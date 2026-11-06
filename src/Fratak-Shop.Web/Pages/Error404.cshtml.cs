using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// Error page for 404 and other status codes (re-executed by the status-code middleware).
/// The original status code is preserved; opening the page directly returns 404.
/// </summary>
[IgnoreAntiforgeryToken]
public class Error404Model(IPageMetaService pageMeta) : ShopPageModel
{
    public new int StatusCode { get; private set; } = StatusCodes.Status404NotFound;

    public bool IsNotFound => StatusCode == StatusCodes.Status404NotFound;

    public string Heading => StatusCode switch
    {
        StatusCodes.Status404NotFound => "صفحه مورد نظر پیدا نشد !",
        StatusCodes.Status400BadRequest => "درخواست نامعتبر است؛ صفحه را دوباره بارگذاری کنید.",
        StatusCodes.Status403Forbidden => "دسترسی به این صفحه مجاز نیست.",
        >= 500 => "خطای غیرمنتظره ای رخ داد؛ لطفاً دوباره تلاش کنید.",
        _ => "خطایی رخ داد.",
    };

    public async Task OnGetAsync(int? code)
    {
        var reExecute = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();
        // خطای پیش‌بینی‌نشده (UseExceptionHandler("/404") در محیط واقعی): باید 500 بماند، نه 404؛ وگرنه موتورهای
        // جست‌وجو صفحه‌ای را که لحظه‌ای خطا داده «حذف‌شده» می‌دانند و مانیتورینگ هم خطا را نمی‌بیند.
        var exception = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        StatusCode = exception is not null
            ? StatusCodes.Status500InternalServerError
            : reExecute is not null && code is >= 400 and < 600 ? code.Value : StatusCodes.Status404NotFound;
        Response.StatusCode = StatusCode;

        await pageMeta.ApplyAsync(ViewData, SitePage.NotFound, IsNotFound ? "صفحه پیدا نشد" : "خطا");
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
    }

    // The status-code page can be re-executed for any verb (e.g. a POST without a valid token).
    public Task OnPostAsync(int? code) => OnGetAsync(code);
}
