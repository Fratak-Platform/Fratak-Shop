using Fratak_Shop.Core.Services.Events;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Infrastructure;
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
    ISiteContext site) : ShopPageModel
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
        ViewData[PageMetaKeys.Title] = "کد ورود";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!site.Settings.IsOtpLoginEnabled || !IsValidPhone())
            return RedirectToPage("/Login", new { returnUrl = ReturnUrl });

        var phone = Phone!.Trim();
        var code = string.Concat(Digits.Select(d => d?.Trim()));

        if (code.Length != otpService.CodeLength || !otpService.Verify(phone, code, OtpPurpose.Login))
        {
            ErrorMessage = "کد وارد شده صحیح نیست یا منقضی شده است.";
            return RedirectToPage(new { phone, returnUrl = ReturnUrl });
        }

        var result = await userService.SignInWithPhoneAsync(phone, site.Settings.IsOtpRegistrationEnabled);
        if (!Succeeded(result) || result.Data is null)
        {
            ErrorMessage = result.Message;
            return RedirectToPage("/Login", new { returnUrl = ReturnUrl });
        }

        var user = result.Data.User;
        await authService.SignInAsync(user, isPersistent: true);

        if (result.Data.Created)
        {
            await events.UserRegisteredAsync(user.Id, site.Settings.IsReferralEnabled ? ReferralCode?.Trim() : null);
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

        var send = await otpService.SendAsync(Phone!.Trim(), OtpPurpose.Login);
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
