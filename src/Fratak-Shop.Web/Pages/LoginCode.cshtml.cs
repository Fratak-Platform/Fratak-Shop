using Fratak_Shop.Core.Services.Events;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Security;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// Second step of the passwordless sign-in: the SMS code. New numbers get a customer
/// account (optionally with the referral code of a friend).
/// </summary>
public class LoginCodeModel(
    IOtpService otpService,
    IUserService userService,
    IShopAuthService authService,
    IShopEvents events,
    ISiteContext site,
    Infrastructure.Auth.ITwoFactorTicket twoFactorTicket,
    Core.Services.Team.IStaffTotpService totp,
    Infrastructure.Admin.SecurityEventRecorder securityEvents) : ShopPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Phone { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public string[] Digits { get; set; } = [];

    [BindProperty]
    public string? ReferralCode { get; set; }

    public int CodeLength => otpService.CodeLength;

    public string? DevelopmentCode { get; private set; }

    public bool ShowReferral => site.Settings.IsReferralEnabled && site.Settings.IsOtpRegistrationEnabled;

    public IActionResult OnGet()
    {
        if (!site.Settings.IsOtpLoginEnabled)
            return RedirectToPage("/Login", new { mode = "password", returnUrl = ReturnUrl });

        if (IsSignedIn)
            return RedirectToLocal(ReturnUrl, "/Profile/Index");

        if (!IsValidPhone())
            return RedirectToPage("/Login", new { returnUrl = ReturnUrl });

        DevelopmentCode = TempData[LoginModel.DevelopmentCodeKey] as string;
        ReferralCode ??= ReferralCookie.Read(HttpContext);
        ViewData[PageMetaKeys.Title] = "کد ورود";
        PrepareCaptcha(CaptchaActions.OtpVerify);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!site.Settings.IsOtpLoginEnabled || !IsValidPhone())
            return RedirectToPage("/Login", new { returnUrl = ReturnUrl });

        var phone = Phone!.Trim();
        var code = string.Concat(Digits.Select(d => d?.Trim()));

        if (!VerifyCaptcha(CaptchaActions.OtpVerify))
        {
            ErrorMessage = "کد امنیتی (عدد داخل تصویر) درست وارد نشده است.";
            return RedirectToPage(new { phone, returnUrl = ReturnUrl });
        }

        if (code.Length != otpService.CodeLength || !otpService.Verify(phone, code, OtpPurpose.Login))
        {
            CaptchaFailed(CaptchaActions.OtpVerify);
            await securityEvents.RecordAsync(Data.Entities.Admin.SecurityEventType.OtpFailed, null, phone, "ورود با کد پیامکی");
            ErrorMessage = "کد وارد شده صحیح نیست یا منقضی شده است.";
            return RedirectToPage(new { phone, returnUrl = ReturnUrl });
        }

        CaptchaPassed(CaptchaActions.OtpVerify);

        var result = await userService.SignInWithPhoneAsync(phone, site.Settings.IsOtpRegistrationEnabled);
        if (!Succeeded(result) || result.Data is null)
        {
            if (result.Message == UserService.BlockedMessage)
                await securityEvents.RecordAsync(Data.Entities.Admin.SecurityEventType.BlockedUserLogin, null, phone, "ورود با کد پیامکی");
            ErrorMessage = result.Message;
            return RedirectToPage("/Login", new { returnUrl = ReturnUrl });
        }

        var user = result.Data.User;

        // فاز ۱۰: اگر کارمند برنامهٔ احراز هویت دارد، کد پیامکی به‌تنهایی کافی نیست (در برابر سرقت سیم‌کارت)؛
        // مرحلهٔ دوم در /login/2fa با کد برنامه انجام می‌شود.
        if (!result.Data.Created && await totp.IsActiveAsync(user.Id))
        {
            twoFactorTicket.Issue(HttpContext, user.Id, true);
            return RedirectToPage("/LoginTwoFactor", new { returnUrl = ReturnUrl });
        }

        await authService.SignInAsync(user, isPersistent: true);

        if (result.Data.Created)
        {
            var referral = string.IsNullOrWhiteSpace(ReferralCode) ? ReferralCookie.Read(HttpContext) : ReferralCode.Trim();
            await events.UserRegisteredAsync(user.Id, site.Settings.IsReferralEnabled ? referral : null);
            ReferralCookie.Clear(HttpContext);
            SuccessMessage = "حساب کاربری شما ساخته شد؛ خوش آمدید.";
            return RedirectToPage("/Welcome", new { returnUrl = ReturnUrl });
        }

        SuccessMessage = $"{user.FullName ?? "کاربر"} عزیز، خوش آمدید.";
        return RedirectToLocal(ReturnUrl, "/Index");
    }

    public async Task<IActionResult> OnPostResendAsync()
    {
        if (!IsValidPhone())
            return RedirectToPage("/Login", new { returnUrl = ReturnUrl });

        // Resending is an SMS request too: once the captcha is due, it has to be solved on the login form.
        if (IsCaptchaRequired(CaptchaActions.OtpSend))
        {
            ErrorMessage = "برای دریافت کد جدید، کد امنیتی را در صفحه ورود وارد کنید.";
            return RedirectToPage("/Login", new { returnUrl = ReturnUrl });
        }

        var send = await otpService.SendAsync(Phone!.Trim(), OtpPurpose.Login);
        CaptchaFailed(CaptchaActions.OtpSend);
        if (send.Sent)
            SuccessMessage = "کد ورود جدید ارسال شد.";
        else if (send.Blocked)
            ErrorMessage = "تعداد درخواست کد برای این شماره بیش از حد مجاز است؛ لطفاً بعداً تلاش کنید.";
        else
            ErrorMessage = $"برای ارسال مجدد {Math.Ceiling(send.RetryAfter.TotalSeconds)} ثانیه صبر کنید.";

        TempData[LoginModel.DevelopmentCodeKey] = send.DevelopmentCode;
        return RedirectToPage(new { phone = Phone!.Trim(), returnUrl = ReturnUrl });
    }

    private bool IsValidPhone() => !string.IsNullOrWhiteSpace(Phone) && Phone.Trim().IsValidMobile();
}
