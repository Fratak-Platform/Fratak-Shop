using Fratak_Shop.Core.DTOs.Loyalty;
using Fratak_Shop.Core.Services.Loyalty;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

/// <summary>
/// The customer's club inside the profile: points, level progress, rewards to redeem,
/// personal coupons, referral code, history, ways to earn and the levels.
/// </summary>
public class ClubModel(ILoyaltyService loyaltyService, ISiteContext site) : ShopPageModel
{
    public LoyaltyProgramDto Program { get; private set; } = new();

    public LoyaltySummaryDto Summary { get; private set; } = null!;

    public string? ReferralLink { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!site.Settings.IsLoyaltyEnabled)
            return NotFound();

        var userId = CurrentUserId!.Value;
        Program = await loyaltyService.GetProgramAsync();
        await loyaltyService.EnsureReferralCodeAsync(userId);
        Summary = await loyaltyService.GetSummaryAsync(userId, 30);

        if (!string.IsNullOrWhiteSpace(Summary.ReferralCode))
            ReferralLink = $"{Request.Scheme}://{Request.Host}/register?ref={Uri.EscapeDataString(Summary.ReferralCode)}";

        ViewData[PageMetaKeys.Title] = site.Settings.LoyaltyName;
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        return Page();
    }

    public async Task<IActionResult> OnPostRedeemAsync(Guid rewardId)
    {
        if (!site.Settings.IsLoyaltyEnabled)
            return NotFound();

        ShowResult(await loyaltyService.RedeemAsync(CurrentUserId!.Value, rewardId));
        return RedirectToPage();
    }
}
