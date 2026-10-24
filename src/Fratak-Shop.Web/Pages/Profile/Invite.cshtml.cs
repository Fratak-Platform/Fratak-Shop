using Fratak_Shop.Core.DTOs.Engagement;
using Fratak_Shop.Core.Services.Loyalty;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

/// <summary>Invite friends: personal referral link / code, rewards and the list of invited friends.</summary>
public class InviteModel(ILoyaltyService loyalty, ISiteContext site) : ShopPageModel
{
    public ReferralStatsDto Stats { get; private set; } = new();
    public string? InviteUrl { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!site.Settings.IsReferralEnabled)
            return NotFound();

        Stats = await loyalty.GetReferralStatsAsync(CurrentUserId!.Value);
        if (Stats.Code is not null)
            InviteUrl = $"{Request.Scheme}://{Request.Host}/?ref={Uri.EscapeDataString(Stats.Code)}";

        ViewData[PageMetaKeys.Title] = "دعوت از دوستان";
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        return Page();
    }
}
