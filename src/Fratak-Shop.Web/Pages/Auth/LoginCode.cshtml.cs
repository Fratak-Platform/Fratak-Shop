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

/// <summary>ورود بدون رمز عبور: ارسال کد یک‌بارمصرف برای کاربر ثبت‌نام‌شده.</summary>
[AllowAnonymous]
public class LoginCodeModel(
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
            Path = "/auth/login-code",
            ContentTitle = "ورود با کد یک‌بارمصرف",
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

        var result = await auth.SendPasswordlessLoginCodeAsync(PhoneNumber);

        if (!result.IsSuccess())
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return Page();
        }

        HttpContext.Session.SetString(SessionKeys.PendingPhone, PhoneNumber);
        HttpContext.Session.SetString(SessionKeys.PendingPurpose, UserTokenPurpose.PasswordlessLogin.ToString());

        if (!string.IsNullOrWhiteSpace(result.Data?.DevelopmentCode))
            Notify($"حالت توسعه: کد ورود شما {result.Data.DevelopmentCode} است.", "info");
        else
            Notify("کد ورود ارسال شد.");

        return RedirectToPage("/Auth/Verify");
    }
}
