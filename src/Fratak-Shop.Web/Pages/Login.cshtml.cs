using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Auth;
using Fratak_Shop.Web.Infrastructure.Security;
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
    IPageMetaService pageMeta,
    Core.Services.Team.IStaffTotpService totp,
    Infrastructure.Admin.SecurityEventRecorder securityEvents,
    DefaultAdminPasswords defaultAdminPasswords) : ShopPageModel
{

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
        PrepareCaptcha(IsPasswordMode ? CaptchaActions.Login : CaptchaActions.OtpSend);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Mode = "password";
        await pageMeta.ApplyAsync(ViewData, SitePage.Login, "ورود به حساب کاربری");

        ModelState.Clear();
        if (!VerifyCaptcha(CaptchaActions.Login) || !TryValidateModel(Input, nameof(Input)))
            return Page();

        var result = await userService.LoginAsync(new LoginCommand
        {
            PhoneNumber = Input.PhoneNumber.Trim(),
            Password = Input.Password,
        });

        if (!Succeeded(result) || result.Data is null)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            CaptchaFailed(CaptchaActions.Login);
            var eventType = result.Message == UserService.BlockedMessage ? Data.Entities.Admin.SecurityEventType.BlockedUserLogin
                : result.Message?.Contains("تلاش‌های ناموفق") == true ? Data.Entities.Admin.SecurityEventType.LockedOut
                : Data.Entities.Admin.SecurityEventType.LoginFailed;
            await securityEvents.RecordAsync(eventType, null, Input.PhoneNumber.Trim(), "ورود با رمز عبور");
            return Page();
        }

        CaptchaPassed(CaptchaActions.Login);
        var user = result.Data;

        // فاز ۱۰: کارمندی که برنامهٔ احراز هویت را فعال کرده، به‌جای پیامک کد برنامه را وارد می‌کند (پیامکی فرستاده نمی‌شود).
        if (await totp.IsActiveAsync(user.Id))
        {
            twoFactorTicket.Issue(HttpContext, user.Id, Input.RememberMe);
            return RedirectToPage("/LoginTwoFactor", new { returnUrl = ReturnUrl });
        }

        if (user.TwoFactorEnabled && site.Settings.IsTwoFactorAvailable)
        {
            // Password was right; the session is created only after the SMS code.
            twoFactorTicket.Issue(HttpContext, user.Id, Input.RememberMe);
            var send = await otpService.SendAsync(user.PhoneNumber, OtpPurpose.TwoFactor);
            if (send.Failed)
                ErrorMessage = OtpSendResult.FailedMessage; // در صفحهٔ بعد دکمهٔ «ارسال مجدد» هست
            return RedirectToPage("/LoginTwoFactor", new { returnUrl = ReturnUrl });
        }

        await authService.SignInAsync(user, Input.RememberMe);

        // مرحلهٔ چهاردهم: مدیر پیش‌فرضی که هنوز با رمز اولیهٔ تنظیمات (مثلاً 12345678) وارد شده، اول باید رمزش را عوض کند.
        if (defaultAdminPasswords.IsDefault(user.PhoneNumber, Input.Password))
        {
            ErrorMessage = "شما با رمز اولیهٔ پیش‌فرض وارد شدید. برای امنیت حساب مدیریت، همین حالا رمز عبور تازه‌ای تعیین کنید.";
            return RedirectToPage("/Profile/Password");
        }

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
        if (!VerifyCaptcha(CaptchaActions.OtpSend) || !TryValidateModel(Otp, nameof(Otp)))
            return Page();

        var phone = Otp.PhoneNumber.Trim();
        var send = await otpService.SendAsync(phone, OtpPurpose.Login);

        // Every SMS request counts: after a few from one IP the captcha is shown (SMS-bombing guard).
        CaptchaFailed(CaptchaActions.OtpSend);

        if (send.Blocked)
        {
            ModelState.AddModelError(string.Empty, "تعداد درخواست کد برای این شماره بیش از حد مجاز است؛ لطفاً بعداً تلاش کنید.");
            return Page();
        }

        // پیامک به سرویس‌دهنده نرسید (مثلاً قطعی SMS.ir): در همین صفحه می‌ماند تا دوباره تلاش کند یا با رمز وارد شود.
        if (send.Failed)
        {
            ModelState.AddModelError(string.Empty, OtpSendResult.FailedMessage);
            return Page();
        }

        // A code sent less than a minute ago is still valid: continue with it.
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
