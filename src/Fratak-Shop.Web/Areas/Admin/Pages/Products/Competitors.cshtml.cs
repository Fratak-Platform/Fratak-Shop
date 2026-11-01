using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Pricing;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Products;

/// <summary>
/// صفحهٔ «رصد قیمت رقبا» (مرحلهٔ پانزدهم): برای هر محصول، قیمت ما کنار ارزان‌ترین فروشندهٔ ترب و ارزان‌ترین رقیب معتبر
/// (دیجی‌کالا، باسلام، سایت‌های دیگر) تا تیم ببیند کجا گران‌تریم و تصمیم بگیرد.
/// <para>آدرس: /admin/products/competitors?state=above&amp;q=هدفون. فیلترها: همه، گران‌تر از رقیب، ارزان‌ترین، بررسی‌نشده، کهنه.</para>
/// <para>داده از آخرین بررسی هر محصول است (ذخیره بعد از «پیشنهاد قیمت فروش»، قیمت‌گذاری بازار درون‌ریزی یا «بررسی دوباره»).
/// دسترسی: دیدن = «دیدن محصولات»؛ «بررسی دوباره» = «مدیریت محصولات». قیمت هیچ محصولی اینجا عوض نمی‌شود.</para>
/// </summary>
[AdminPermission(AdminPermissions.ProductsView)]
public class CompetitorsModel(IProductCompetitorService competitors, IStoreSettingService storeSettings,
    IOptions<global::Fratak_Shop.Web.Infrastructure.ShopOptions> shopOptions) : AdminPageModel
{
    /// <summary>فیلترهای وضعیت با عنوان فارسی.</summary>
    public static readonly (string Key, string Title)[] States =
    [
        ("all", "همه"), ("above", "گران‌تر از رقیب"), ("cheapest", "ارزان‌ترین هستیم"), ("unchecked", "بررسی‌نشده"), ("stale", "بیش از ۷ روز پیش"),
    ];

    [BindProperty(SupportsGet = true, Name = "state")] public string State { get; set; } = "all";
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }

    public ProductCompetitorPage Result { get; private set; } = new([], 0, 1, 1, 0, 0, 0, 0);
    public bool LookupEnabled { get; private set; }
    public bool CanRefresh => Can(AdminPermissions.ProductsManage);

    public async Task OnGetAsync([FromQuery(Name = "page")] int pageNumber = 1)
    {
        if (!States.Any(s => s.Key == State))
            State = "all";
        Result = await competitors.ListAsync(State, Search, ClampPage(pageNumber), HttpContext.RequestAborted);
        LookupEnabled = (await storeSettings.GetAsync(HttpContext.RequestAborted)).IsCompetitorPriceLookupEnabled;
    }

    /// <summary>«بررسی دوباره» یک محصول؛ بعد از آن به همان فیلتر و صفحه برمی‌گردد.</summary>
    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostRefreshAsync(Guid id, string? returnUrl)
    {
        var settings = await storeSettings.GetAsync(HttpContext.RequestAborted);
        ShowResult(await competitors.RefreshAsync(id, OwnShopNames.For(settings, shopOptions.Value, Request), HttpContext.RequestAborted));
        return LocalRedirect(returnUrl is not null && returnUrl.StartsWith("/admin/products/competitors", StringComparison.Ordinal) && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            ? returnUrl
            : "/admin/products/competitors");
    }

    /// <summary>آدرس فیلتر وضعیت با حفظ جست‌وجو.</summary>
    public string StateUrl(string state)
        => "/admin/products/competitors?state=" + Uri.EscapeDataString(state) + (string.IsNullOrWhiteSpace(Search) ? "" : "&q=" + Uri.EscapeDataString(Search.Trim()));
}
