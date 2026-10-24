using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Auth;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// Sign-in with a one-time SMS code (no password needed, new numbers get an account)
/// or with mobile number + password; accounts with two-step login continue on /login/2fa.
/// </summary>
public class LoginModel(
    IUserService userService,
    IShopAuthService authService,
    IOtpService otpService,
    ITwoFactorTicket twoFactorTicket,
    ISiteContext site,
    IPageMetaService pageMeta) : ShopPageModel
{
    public const string DevelopmentCodeKey = "OtpDevelopmentCode";

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty]
    public OtpInput Otp { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>"password" or "otp" (default).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Mode { get; set; }

    public bool PasswordEnabled => site.Settings.IsPasswordLoginEnabled || !site.Settings.IsOtpLoginEnabled;

    public bool OtpEnabled => site.Settings.IsOtpLoginEnabled;

    public bool IsPasswordMode => !OtpEnabled || (PasswordEnabled && string.Equals(Mode, "password", StringComparison.OrdinalIgnoreCase));

    public async Task<IActionResult> OnGetAsync()
    {
        if (IsSignedIn)
            return RedirectToLocal(ReturnUrl, "/Profile/Index");

        await pageMeta.ApplyAsync(ViewData, SitePage.Login, "ورود به حساب کاربری");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Mode = "password";
        await pageMeta.ApplyAsync(ViewData, SitePage.Login, "ورود به حساب کاربری");

        ModelState.Clear();
        if (!TryValidateModel(Input, nameof(Input)))
            return Page();

        var result = await userService.LoginAsync(new LoginCommand
        {
            PhoneNumber = Input.PhoneNumber.Trim(),
            Password = Input.Password,
        });

        if (!Succeeded(result) || result.Data is null)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return Page();
        }

        var user = result.Data;

        if (user.TwoFactorEnabled && site.Settings.IsTwoFactorAvailable)
        {
            // Password was right; the session is created only after the SMS code.
            twoFactorTicket.Issue(HttpContext, user.Id, Input.RememberMe);
            var send = await otpService.SendAsync(user.PhoneNumber, OtpPurpose.TwoFactor);
            TempData[DevelopmentCodeKey] = send.DevelopmentCode;
            return RedirectToPage("/LoginTwoFactor", new { returnUrl = ReturnUrl });
        }

        await authService.SignInAsync(user, Input.RememberMe);
        SuccessMessage = $"{user.FullName ?? "کاربر"} عزیز، خوش آمدید.";

        return RedirectToLocal(ReturnUrl, "/Index");
    }

    public async Task<IActionResult> OnPostSendCodeAsync()
    {
        Mode = "otp";
        await pageMeta.ApplyAsync(ViewData, SitePage.Login, "ورود به حساب کاربری");

        if (!OtpEnabled)
            return RedirectToPage(new { mode = "password", returnUrl = ReturnUrl });

        ModelState.Clear();
        if (!TryValidateModel(Otp, nameof(Otp)))
            return Page();

        var phone = Otp.PhoneNumber.Trim();
        var send = await otpService.SendAsync(phone, OtpPurpose.Login);

        if (send.Blocked)
        {
            ModelState.AddModelError(string.Empty, "تعداد درخواست کد برای این شماره بیش از حد مجاز است؛ لطفاً بعداً تلاش کنید.");
            return Page();
        }

        // A code sent less than a minute ago is still valid: continue with it.
        TempData[DevelopmentCodeKey] = send.DevelopmentCode;
        return RedirectToPage("/LoginCode", new { phone, returnUrl = ReturnUrl });
    }

    public class LoginInput
    {
        [Required(ErrorMessage = "شماره موبایل را وارد کنید.")]
        [RegularExpression(@"^09[0-9]{9}$", ErrorMessage = "شماره موبایل باید با ۰۹ شروع شود و ۱۱ رقم باشد.")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "رمز عبور را وارد کنید.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; } = true;
    }

    public class OtpInput
    {
        [Required(ErrorMessage = "شماره موبایل را وارد کنید.")]
        [RegularExpression(@"^09[0-9]{9}$", ErrorMessage = "شماره موبایل باید با ۰۹ شروع شود و ۱۱ رقم باشد.")]
        public string PhoneNumber { get; set; } = string.Empty;
    }
}
