using Fratak_Shop.Core.DTOs.Loyalty;
using Fratak_Shop.Core.Services.Loyalty;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// Customer club: how to earn points, levels and rewards for everyone; balance,
/// progress, personal coupons, referral code and history for signed-in customers.
/// </summary>
public class ClubModel(ILoyaltyService loyaltyService, ISiteContext site) : ShopPageModel
{
    public LoyaltyProgramDto Program { get; private set; } = new();

    public LoyaltySummaryDto? Summary { get; private set; }

    public string? ReferralLink { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!site.Settings.IsLoyaltyEnabled)
            return NotFound();

        ViewData[PageMetaKeys.Title] = "باشگاه مشتریان";
        ViewData[PageMetaKeys.Description] = $"با هر خرید، ثبت دیدگاه و معرفی دوستان در باشگاه مشتریان {site.Settings.SiteName} امتیاز بگیرید و آن را به اعتبار کیف پول یا کد تخفیف تبدیل کنید.";

        Program = await loyaltyService.GetProgramAsync();

        if (CurrentUserId is { } userId)
        {
            await loyaltyService.EnsureReferralCodeAsync(userId);
            Summary = await loyaltyService.GetSummaryAsync(userId, 30);
            if (!string.IsNullOrWhiteSpace(Summary.ReferralCode))
                ReferralLink = $"{Request.Scheme}://{Request.Host}/register?ref={Uri.EscapeDataString(Summary.ReferralCode)}";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostRedeemAsync(Guid rewardId)
    {
        if (!IsSignedIn)
            return RedirectToLogin("/club");

        ShowResult(await loyaltyService.RedeemAsync(CurrentUserId!.Value, rewardId));
        return RedirectToPage();
    }
}
