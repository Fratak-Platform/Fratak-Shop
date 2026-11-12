using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Security;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// دور ۳۰ (#۷) – «فراموشی رمز عبور»، گام دوم: کد پیامکی + رمز تازه + تکرار رمز.
/// <para>ترتیب امن کار:</para>
/// <list type="number">
/// <item>کپچا (اگر لازم شده باشد) و درستی فرم؛ رمز تازه «قبل از» مصرف کد سنجیده می‌شود تا رمز ضعیف کد را هدر ندهد.</item>
/// <item>کد فقط با هدف <see cref="OtpPurpose.ResetPassword"/> پذیرفته می‌شود (کد ورود به درد این صفحه نمی‌خورد)؛
/// ۵ اشتباه ← کد باطل؛ عمر کد ۳ دقیقه؛ مقایسه با زمان ثابت.</item>
/// <item>رمز ذخیره می‌شود، مهر امنیتی عوض می‌شود، <b>همهٔ نشست‌ها و توکن‌های refresh همهٔ دستگاه‌ها باطل می‌شوند</b>،
/// رویداد امنیتی ثبت و پیام «رمز شما تغییر کرد» برای صاحب حساب فرستاده می‌شود.</item>
/// <item>کاربر خودکار وارد نمی‌شود؛ با رمز تازه وارد می‌شود (اگر کارمند برنامهٔ احراز هویت دارد، آن مرحله هم سر جایش است).</item>
/// </list>
/// </summary>
public class ResetPasswordModel(
    IUserService userService,
    IOtpService otpService,
    IShopAuthService authService,
    Infrastructure.Admin.SecurityEventRecorder securityEvents) : ShopPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Phone { get; set; }

    [BindProperty]
    public string[] Digits { get; set; } = [];

    [BindProperty]
    public ResetInput Input { get; set; } = new();

    public int CodeLength => otpService.CodeLength;

    public IActionResult OnGet()
    {
        if (IsSignedIn)
            return RedirectToPage("/Profile/Password");
        if (!IsValidPhone())
            return RedirectToPage("/ForgotPassword");
        ViewData[PageMetaKeys.Title] = "تعیین رمز عبور تازه";
        PrepareCaptcha(CaptchaActions.OtpVerify);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ViewData[PageMetaKeys.Title] = "تعیین رمز عبور تازه";
        if (IsSignedIn)
            return RedirectToPage("/Profile/Password");
        if (!IsValidPhone())
            return RedirectToPage("/ForgotPassword");

        var phone = Phone!.Trim();
        // کپچای نادرست ← VerifyCaptcha خودش خطای فرم و تصویر تازه را می‌سازد.
        if (!VerifyCaptcha(CaptchaActions.OtpVerify))
            return Page();

        // ۱) فرم و رمز تازه (پیش از مصرف کد پیامکی).
        if (!ModelState.IsValid)
        {
            PrepareCaptcha(CaptchaActions.OtpVerify);
            return Page();
        }
        if (UserService.PasswordProblem(phone, Input.NewPassword) is { } problem)
        {
            ModelState.AddModelError(string.Empty, problem);
            PrepareCaptcha(CaptchaActions.OtpVerify);
            return Page();
        }

        // ۲) کد پیامکی (فقط هدف «بازیابی رمز»).
        var code = string.Concat(Digits.Select(d => d?.Trim()));
        if (code.Length != otpService.CodeLength || !otpService.Verify(phone, code, OtpPurpose.ResetPassword))
        {
            CaptchaFailed(CaptchaActions.OtpVerify);
            await securityEvents.RecordAsync(Data.Entities.Admin.SecurityEventType.OtpFailed, null, phone, "بازیابی رمز عبور");
            ModelState.AddModelError(string.Empty, "کد وارد شده صحیح نیست یا منقضی شده است.");
            PrepareCaptcha(CaptchaActions.OtpVerify);
            return Page();
        }
        CaptchaPassed(CaptchaActions.OtpVerify);

        // ۳) ثبت رمز و بستن همهٔ نشست‌ها.
        var result = await userService.ResetPasswordAsync(phone, Input.NewPassword);
        if (!result.IsSuccess())
        {
            ModelState.AddModelError(string.Empty, result.Message);
            PrepareCaptcha(CaptchaActions.OtpVerify);
            return Page();
        }

        await authService.SignOutOtherDevicesAsync(result.Data, keepCurrent: false);
        await securityEvents.RecordAsync(Data.Entities.Admin.SecurityEventType.PasswordReset, result.Data, phone, "بازیابی رمز با کد پیامکی؛ همهٔ دستگاه‌ها خارج شدند");

        SuccessMessage = "رمز عبور تازه ثبت شد و همهٔ دستگاه‌ها از حساب خارج شدند؛ حالا با رمز جدید وارد شوید.";
        return RedirectToPage("/Login", new { mode = "password" });
    }

    /// <summary>ارسال دوبارهٔ کد (همان محدودیت‌های ارسال؛ جواب یکسان برای شمارهٔ عضو و غیرعضو).</summary>
    public async Task<IActionResult> OnPostResendAsync()
    {
        if (!IsValidPhone())
            return RedirectToPage("/ForgotPassword");
        if (IsCaptchaRequired(CaptchaActions.OtpSend))
        {
            ErrorMessage = "برای دریافت کد جدید، کد امنیتی را در همین فرم وارد کنید.";
            return RedirectToPage("/ForgotPassword", new { phone = Phone!.Trim() });
        }

        var phone = Phone!.Trim();
        CaptchaFailed(CaptchaActions.OtpSend);
        if (await userService.CanResetPasswordAsync(phone))
        {
            var send = await otpService.SendAsync(phone, OtpPurpose.ResetPassword);
            if (send.Blocked)
            {
                ErrorMessage = "تعداد درخواست کد برای این شماره بیش از حد مجاز است؛ لطفاً بعداً تلاش کنید.";
                return RedirectToPage(new { phone });
            }
            if (send.Failed)
            {
                ErrorMessage = OtpSendResult.FailedMessage;
                return RedirectToPage(new { phone });
            }
            if (!send.Sent)
            {
                ErrorMessage = $"برای ارسال مجدد {Math.Ceiling(send.RetryAfter.TotalSeconds)} ثانیه صبر کنید.";
                return RedirectToPage(new { phone });
            }
        }

        SuccessMessage = "اگر این شماره در فروشگاه حساب داشته باشد، کد تازه پیامک شد.";
        return RedirectToPage(new { phone });
    }

    private bool IsValidPhone() => !string.IsNullOrWhiteSpace(Phone) && Phone.Trim().IsValidMobile();

    /// <summary>فرم رمز تازه.</summary>
    public class ResetInput
    {
        [Required(ErrorMessage = "رمز عبور تازه را وارد کنید.")]
        [DataType(DataType.Password)]
        [StringLength(128, MinimumLength = 6, ErrorMessage = "رمز عبور باید بین ۶ تا ۱۲۸ کاراکتر باشد.")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "تکرار رمز عبور را وارد کنید.")]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "تکرار رمز عبور با رمز تازه یکسان نیست.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
