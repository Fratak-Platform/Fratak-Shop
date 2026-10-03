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
public class RegisterModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IAuthService auth)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    [BindProperty]
    public RegisterInput Input { get; set; } = new();

    public string? DevelopmentCode { get; private set; }

    public void OnGet()
    {
        ApplySeo(null, new SeoFallback
        {
            SiteName = Site.SiteName,
            SiteUrl = Site.SiteUrl,
            Path = "/auth/register",
            ContentTitle = "ساخت حساب کاربری",
            ShortDescription = "ثبت‌نام سریع با شماره موبایل و دریافت کد تأیید؛ پیگیری سفارش‌ها و لیست علاقه‌مندی‌ها.",
            EntityType = SeoEntityType.Page,
            ForceNoIndex = true,
        });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var result = await auth.RegisterAsync(new RegisterUserCommand
        {
            PhoneNumber = Input.PhoneNumber,
            Password = string.IsNullOrWhiteSpace(Input.Password) ? null : Input.Password,
            FirstName = Input.FirstName,
            LastName = Input.LastName,
            Email = Input.Email,
            AcceptTerms = Input.AcceptTerms,
        });

        if (!result.IsSuccess() || result.Data is null)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return Page();
        }

        HttpContext.Session.SetString(SessionKeys.PendingPhone, Input.PhoneNumber);
        HttpContext.Session.SetString(SessionKeys.PendingPurpose, UserTokenPurpose.PhoneVerification.ToString());

        DevelopmentCode = result.Data.DevelopmentCode;

        if (result.Data.IsStaff)
        {
            await AuthSignIn.SignInAsync(HttpContext, result.Data, rememberMe: true);
            Notify("حساب شما ساخته شد. خوش آمدید.");
            return Redirect("/admin");
        }

        Notify("ثبت‌نام انجام شد. کد تأیید را وارد کنید.");
        return RedirectToPage("/Auth/Verify");
    }

    public class RegisterInput
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "شماره موبایل را وارد کنید.")]
        [System.ComponentModel.DataAnnotations.RegularExpression(@"^09\d{9}$", ErrorMessage = "شماره موبایل معتبر نیست.")]
        public string PhoneNumber { get; set; } = null!;

        public string? Password { get; set; }

        public string? FirstName { get; set; }

        public string? LastName { get; set; }

        [System.ComponentModel.DataAnnotations.EmailAddress(ErrorMessage = "ایمیل معتبر نیست.")]
        public string? Email { get; set; }

        [System.ComponentModel.DataAnnotations.Range(typeof(bool), "true", "true", ErrorMessage = "پذیرش قوانین فروشگاه الزامی است.")]
        public bool AcceptTerms { get; set; }
    }
}
