using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

/// <summary>
/// «رمز عبور و امنیت» در پروفایل: تغییر رمز با رمز فعلی، ورود دو مرحله‌ای و
/// دور ۳۱ (#۶) «رمز فعلی را نمی‌دانم»: بازیابی رمز از داخل پروفایل بدون خروج از حساب.
/// <list type="number">
/// <item>کاربر روی «رمز فعلی را نمی‌دانم» می‌زند (?mode=forgot؛ GET هیچ تغییری نمی‌دهد).</item>
/// <item>«ارسال کد» یک کد پیامکی با هدف <see cref="OtpPurpose.ResetPassword"/> فقط به شمارهٔ <b>خود حساب</b> (از پایگاه داده، نه از فرم)
/// می‌فرستد؛ همان محدودیت‌های کد ورود (۶ کد در ساعت، ۶۰ ثانیه فاصله) برقرار است.</item>
/// <item>کد + رمز تازه + تکرار ← رمز ثبت می‌شود، مهر امنیتی عوض می‌شود، دستگاه‌های دیگر خارج می‌شوند و همین مرورگر وارد می‌ماند.</item>
/// </list>
/// </summary>
public class PasswordModel(
    IUserService userService,
    IShopAuthService authService,
    IOtpService otpService,
    Infrastructure.Admin.SecurityEventRecorder securityEvents) : ShopPageModel
{
    public static readonly string[] Rules =
    [
        "حداقل ۶ کاراکتر داشته باشد.",
        "ترکیبی از حروف و اعداد باشد.",
        "با رمز عبور فعلی یکسان نباشد.",
    ];

    [BindProperty]
    public PasswordInput Input { get; set; } = new();

    public Core.DTOs.Users.Query.UserDto? CurrentUser { get; private set; }

    /// <summary>دور ۳۱: حالت «رمز فعلی را نمی‌دانم» (فرم کد پیامکی + رمز تازه به‌جای فرم رمز فعلی).</summary>
    [BindProperty(SupportsGet = true, Name = "mode")]
    public string? Mode { get; set; }

    /// <summary>آیا فرم بازیابی نمایش داده شود؟ فقط برای حسابی که رمز دارد معنا دارد.</summary>
    public bool ForgotMode => string.Equals(Mode, "forgot", StringComparison.OrdinalIgnoreCase) && CurrentUser?.HasPassword != false;

    /// <summary>تعداد رقم کد پیامکی.</summary>
    public int CodeLength => otpService.CodeLength;

    /// <summary>شمارهٔ پوشیده برای نمایش. مثال: 09121234567 ← 0912***4567.</summary>
    public string MaskedPhone => Mask(CurrentUser?.PhoneNumber);

    public async Task OnGetAsync()
    {
        ViewData[PageMetaKeys.Title] = "رمز عبور و امنیت";
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        CurrentUser = await userService.GetUserByIdAsync(CurrentUserId!.Value);
    }

    /// <summary>
    /// دور ۳۱ (#۶): ارسال کد بازیابی به شمارهٔ خود حساب. شماره هرگز از فرم خوانده نمی‌شود (نمی‌شود برای شمارهٔ دیگری کد گرفت).
    /// </summary>
    public async Task<IActionResult> OnPostSendCodeAsync()
    {
        var user = await userService.GetUserByIdAsync(CurrentUserId!.Value);
        if (user is null || string.IsNullOrWhiteSpace(user.PhoneNumber) || !await userService.CanResetPasswordAsync(user.PhoneNumber))
        {
            ErrorMessage = "ارسال کد برای این حساب ممکن نیست؛ با پشتیبانی تماس بگیرید.";
            return RedirectToPage();
        }

        var send = await otpService.SendAsync(user.PhoneNumber, OtpPurpose.ResetPassword);
        if (send.Blocked)
            ErrorMessage = "تعداد درخواست کد برای این شماره بیش از حد مجاز است؛ لطفاً بعداً تلاش کنید.";
        else if (send.Failed)
            ErrorMessage = OtpSendResult.FailedMessage;
        else if (!send.Sent)
            ErrorMessage = $"برای ارسال مجدد {Math.Ceiling(send.RetryAfter.TotalSeconds)} ثانیه صبر کنید.";
        else
            SuccessMessage = $"کد بازیابی به شمارهٔ {Mask(user.PhoneNumber)} پیامک شد.";
        return RedirectToPage(new { mode = "forgot" });
    }

    /// <summary>
    /// دور ۳۱ (#۶): ثبت رمز تازه با کد پیامکی (بدون رمز فعلی). ترتیب امن: درستی فرم و قوت رمز ← کد (فقط هدف «بازیابی رمز»)
    /// ← ثبت رمز ← خروج دستگاه‌های دیگر (همین مرورگر وارد می‌ماند) ← رویداد امنیتی.
    /// </summary>
    public async Task<IActionResult> OnPostForgotAsync(string[]? digits, ForgotInput forgot)
    {
        ViewData[PageMetaKeys.Title] = "رمز عبور و امنیت";
        Mode = "forgot";
        CurrentUser = await userService.GetUserByIdAsync(CurrentUserId!.Value);
        if (CurrentUser is null || string.IsNullOrWhiteSpace(CurrentUser.PhoneNumber))
            return RedirectToPage("/Login");
        var phone = CurrentUser.PhoneNumber.Trim();

        // فقط خطاهای همین فرم مهم است (فرم «تغییر رمز» در این درخواست ارسال نشده است).
        ModelState.Clear();
        if (!TryValidateModel(forgot, nameof(forgot)))
            return Page();
        if (UserService.PasswordProblem(phone, forgot.NewPassword) is { } problem)
        {
            ModelState.AddModelError(string.Empty, problem);
            return Page();
        }

        var code = string.Concat((digits ?? []).Select(d => d?.Trim()));
        if (code.Length != otpService.CodeLength || !otpService.Verify(phone, code, OtpPurpose.ResetPassword))
        {
            await securityEvents.RecordAsync(Data.Entities.Admin.SecurityEventType.OtpFailed, CurrentUser.Id, phone, "بازیابی رمز از پروفایل");
            ModelState.AddModelError(string.Empty, "کد وارد شده صحیح نیست یا منقضی شده است.");
            return Page();
        }

        var result = await userService.ResetPasswordAsync(phone, forgot.NewPassword);
        if (!result.IsSuccess())
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return Page();
        }

        await authService.SignOutOtherDevicesAsync(CurrentUser.Id, keepCurrent: true);
        await securityEvents.RecordAsync(Data.Entities.Admin.SecurityEventType.PasswordReset, CurrentUser.Id, phone, "بازیابی رمز از پروفایل با کد پیامکی؛ دستگاه‌های دیگر خارج شدند");
        SuccessMessage = "رمز عبور تازه ثبت شد و سایر دستگاه‌ها از حساب شما خارج شدند.";
        return RedirectToPage("/Profile/Index");
    }

    /// <summary>پوشاندن وسط شماره. مثال: Mask("09121234567") ← «0912***4567»؛ null ← «».</summary>
    public static string Mask(string? phone)
    {
        var value = phone?.Trim() ?? string.Empty;
        return value.Length == 11 ? value[..4] + "***" + value[7..] : value;
    }

    /// <summary>Turns two-step sign-in (password + SMS code) on or off.</summary>
    public async Task<IActionResult> OnPostTwoFactorAsync(bool enable)
    {
        ShowResult(await userService.SetTwoFactorAsync(CurrentUserId!.Value, enable));
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ViewData[PageMetaKeys.Title] = "رمز عبور و امنیت";
        CurrentUser = await userService.GetUserByIdAsync(CurrentUserId!.Value);

        if (!ModelState.IsValid)
            return Page();

        var result = await userService.ChangePasswordAsync(new ChangePasswordCommand
        {
            UserId = CurrentUserId!.Value,
            CurrentPassword = Input.CurrentPassword,
            NewPassword = Input.NewPassword,
        });

        if (!Succeeded(result))
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return Page();
        }

        // The password change rotated the security stamp: every other device is signed
        // out (refresh tokens revoked) and this browser gets a fresh session.
        await authService.SignOutOtherDevicesAsync(CurrentUserId!.Value, keepCurrent: true);

        SuccessMessage = result.Message + " سایر دستگاه‌ها از حساب شما خارج شدند.";
        return RedirectToPage("/Profile/Index");
    }

    /// <summary>دور ۳۱: فرم «رمز فعلی را نمی‌دانم» (نام فیلدها: forgot.NewPassword و forgot.ConfirmPassword).</summary>
    public class ForgotInput
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

    public class PasswordInput
    {
        [DataType(DataType.Password)]
        public string? CurrentPassword { get; set; }

        [Required(ErrorMessage = "رمز عبور جدید را وارد کنید.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "رمز عبور جدید باید حداقل ۶ کاراکتر باشد.")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [Compare(nameof(NewPassword), ErrorMessage = "تکرار رمز عبور با رمز جدید یکسان نیست.")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
