using Fratak_Shop.Core.DTOs.Loyalty;
using Fratak_Shop.Core.Services.Loyalty;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// Public introduction of the customer club (ways to earn, levels, rewards). Signed-in
/// customers use their club page inside the profile (/profile/club).
/// </summary>
public class ClubModel(ILoyaltyService loyaltyService, ISiteContext site, IPageMetaService pageMeta) : ShopPageModel
{
    public LoyaltyProgramDto Program { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (!site.Settings.IsLoyaltyEnabled)
            return NotFound();

        if (IsSignedIn)
            return RedirectToPage("/Profile/Club");

        await pageMeta.ApplyAsync(ViewData, Fratak_Shop.Data.Entities.Pages.SitePage.CustomerClub, site.Settings.LoyaltyName,
            site.Settings.LoyaltyProgramDescription
            ?? $"با هر خرید، ثبت دیدگاه و معرفی دوستان در {site.Settings.LoyaltyName} {site.Settings.SiteName} امتیاز بگیرید.");
        pageMeta.AddBreadcrumbs(ViewData, (site.Settings.LoyaltyName, null));

        Program = await loyaltyService.GetProgramAsync();
        return Page();
    }
}
