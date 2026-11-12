using Fratak_Shop.Core.DTOs.Auth;
using Fratak_Shop.Core.Services.Auth;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Auth;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

/// <summary>Active sign-ins (refresh-token families) of the customer, with remote sign-out.</summary>
public class SessionsModel(IRefreshTokenService refreshTokens, IShopAuthService authService) : ShopPageModel
{
    public List<UserSessionDto> Sessions { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Sessions = await refreshTokens.GetActiveSessionsAsync(CurrentUserId!.Value, User.GetSessionId());
        ViewData[PageMetaKeys.Title] = "دستگاه های فعال";
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid familyId)
    {
        if (familyId == User.GetSessionId())
        {
            await authService.SignOutAsync();
            SuccessMessage = "از حساب کاربری خود خارج شدید.";
            return RedirectToPage("/Index");
        }

        var revoked = await refreshTokens.RevokeSessionAsync(CurrentUserId!.Value, familyId, "revoked by user");
        if (revoked)
            SuccessMessage = "دستگاه انتخاب‌شده از حساب شما خارج شد (حداکثر تا چند دقیقه دیگر).";
        else
            ErrorMessage = "نشست مورد نظر یافت نشد.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeOthersAsync()
    {
        await authService.SignOutOtherDevicesAsync(CurrentUserId!.Value, keepCurrent: true);
        SuccessMessage = "همه دستگاه‌های دیگر فوراً از حساب شما خارج شدند.";
        return RedirectToPage();
    }
}
