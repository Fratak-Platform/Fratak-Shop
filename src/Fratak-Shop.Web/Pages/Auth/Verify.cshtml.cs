using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Core.DTOs.Auth.Command;
using Fratak_Shop.Core.DTOs.Seo.Query;
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
public class VerifyModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IAuthService auth)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    public string PhoneNumber { get; private set; } = string.Empty;

    public string? DevelopmentCode { get; private set; }

    [BindProperty]
    public string Code { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(bool resend = false)
    {
        PhoneNumber = HttpContext.Session.GetString(SessionKeys.PendingPhone) ?? string.Empty;
        var purpose = ResolvePurpose();

        if (string.IsNullOrWhiteSpace(PhoneNumber))
        {
            Notify("ابتدا شماره موبایل خود را وارد کنید.", "error");
            return RedirectToPage("/Auth/Register");
        }

        if (resend)
        {
            var sent = await auth.SendVerificationCodeAsync(new SendVerificationCodeCommand
            {
                PhoneNumber = PhoneNumber,
                Purpose = purpose,
            });

            DevelopmentCode = sent.Data?.DevelopmentCode;
            Notify(sent.Message, sent.IsSuccess() ? "success" : "error");
            ApplySeo(null, BuildFallback());
            return Page();
        }

        ApplySeo(null, BuildFallback());
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        PhoneNumber = HttpContext.Session.GetString(SessionKeys.PendingPhone) ?? string.Empty;
        var purpose = ResolvePurpose();

        if (string.IsNullOrWhiteSpace(PhoneNumber))
        {
            Notify("ابتدا شماره موبایل خود را وارد کنید.", "error");
            return RedirectToPage("/Auth/Register");
        }

        if (string.IsNullOrWhiteSpace(Code))
        {
            ModelState.AddModelError(string.Empty, "کد تأیید را وارد کنید.");
            ApplySeo(null, BuildFallback());
            return Page();
        }

        var result = await auth.VerifyCodeAsync(new VerifyCodeCommand
        {
            PhoneNumber = PhoneNumber,
            Code = PriceHelper.ToEnglishDigits(Code).Trim(),
            Purpose = purpose,
        });

        if (!result.IsSuccess() || result.Data is null)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            ApplySeo(null, BuildFallback());
            return Page();
        }

        await AuthSignIn.SignInAsync(HttpContext, result.Data, rememberMe: true);
        await cart.MergeGuestCartAsync(CartSessionKey, result.Data.UserId);

        HttpContext.Session.Remove(SessionKeys.PendingPhone);
        HttpContext.Session.Remove(SessionKeys.PendingPurpose);

        Notify("حساب شما تأیید شد. خوش آمدید.");

        if (purpose == UserTokenPurpose.PasswordReset)
            return RedirectToPage("/Profile/ChangePassword");

        return Redirect(AuthSignIn.LandingPath(result.Data, null));
    }

    private UserTokenPurpose ResolvePurpose()
    {
        var value = HttpContext.Session.GetString(SessionKeys.PendingPurpose);
        return Enum.TryParse<UserTokenPurpose>(value, out var purpose) && purpose != UserTokenPurpose.None
            ? purpose
            : UserTokenPurpose.PhoneVerification;
    }

    private SeoFallback BuildFallback() => new()
    {
        SiteName = Site.SiteName,
        SiteUrl = Site.SiteUrl,
        Path = "/auth/verify",
        ContentTitle = "تأیید شماره موبایل",
        ShortDescription = "کد یک‌بارمصرف ارسال‌شده به شماره موبایل خود را وارد کنید.",
        EntityType = SeoEntityType.Page,
        ForceNoIndex = true,
    };
}
