using Fratak_Shop.Core.DTOs.Auth.Command;
using Fratak_Shop.Core.Services.Auth;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Auth;

[AllowAnonymous]
public class LoginModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IAuthService auth)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    public string? ReturnUrl { get; private set; }

    [BindProperty(SupportsGet = true)]
    public bool RequireLogin { get; set; }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        ApplySeo(null, new SeoFallback
        {
            SiteName = Site.SiteName,
            SiteUrl = Site.SiteUrl,
            Path = "/login",
            ContentTitle = "ورود به حساب کاربری",
            ShortDescription = "ورود و ثبت‌نام در فروشگاه با شماره موبایل و رمز عبور یا کد یک‌بارمصرف.",
            EntityType = Data.Entities.Seo.SeoEntityType.Page,
            ForceNoIndex = true,
        });
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
            return Page();

        var result = await auth.LoginAsync(new LoginCommand
        {
            PhoneNumber = Input.PhoneNumber,
            Password = Input.Password,
            RememberMe = Input.RememberMe,
        });

        if (!result.IsSuccess() || result.Data is null)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return Page();
        }

        await AuthSignIn.SignInAsync(HttpContext, result.Data, Input.RememberMe);
        await cart.MergeGuestCartAsync(CartSessionKey, result.Data.UserId);

        Notify($"خوش آمدید {result.Data.FullName ?? result.Data.PhoneNumber}");

        var target = AuthSignIn.LandingPath(result.Data, ReturnUrl);
        return Redirect(target);
    }

    public class LoginInput
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "شماره موبایل را وارد کنید.")]
        [System.ComponentModel.DataAnnotations.RegularExpression(@"^09\d{9}$", ErrorMessage = "شماره موبایل معتبر نیست.")]
        public string PhoneNumber { get; set; } = null!;

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "رمز عبور را وارد کنید.")]
        [System.ComponentModel.DataAnnotations.DataType(System.ComponentModel.DataAnnotations.DataType.Password)]
        public string Password { get; set; } = null!;

        public bool RememberMe { get; set; } = true;
    }
}
