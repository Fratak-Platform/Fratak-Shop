using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.DTOs.Auth.Command;
using Fratak_Shop.Core.Services.Auth;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Auth;

[AllowAnonymous]
public class ForgotPasswordModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IAuthService auth)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    [BindProperty]
    public string PhoneNumber { get; set; } = string.Empty;

    public void OnGet()
    {
        ApplySeo(null, new SeoFallback
        {
            SiteName = Site.SiteName,
            SiteUrl = Site.SiteUrl,
            Path = "/auth/forgot-password",
            ContentTitle = "بازیابی رمز عبور",
            EntityType = SeoEntityType.Page,
            ForceNoIndex = true,
        });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!Core.Utilities.UserInputValidator.IsValidMobile(PhoneNumber))
        {
            ModelState.AddModelError(string.Empty, "شماره موبایل معتبر نیست.");
            return Page();
        }

        var result = await auth.SendVerificationCodeAsync(new SendVerificationCodeCommand
        {
            PhoneNumber = PhoneNumber,
            Purpose = UserTokenPurpose.PasswordReset,
        });

        if (!result.IsSuccess())
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return Page();
        }

        HttpContext.Session.SetString(SessionKeys.PendingPhone, PhoneNumber);
        HttpContext.Session.SetString(SessionKeys.PendingPurpose, UserTokenPurpose.PasswordReset.ToString());

        Notify(string.IsNullOrWhiteSpace(result.Data?.DevelopmentCode)
            ? "کد بازیابی ارسال شد."
            : $"حالت توسعه: کد بازیابی شما {result.Data.DevelopmentCode} است.", "info");

        return RedirectToPage("/Auth/Verify");
    }
}
