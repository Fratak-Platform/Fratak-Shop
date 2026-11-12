using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Security;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// دور ۳۰ (#۷) – «فراموشی رمز عبور»، گام اول: شماره موبایل + کد امنیتی (تصویر عدد).
/// <list type="bullet">
/// <item>جواب صفحه برای «شمارهٔ عضو» و «شمارهٔ غیرعضو» دقیقاً یکی است (همان پیام و همان مقصد) تا کسی نتواند با این فرم
/// بفهمد کدام شماره در فروشگاه حساب دارد. پیامک فقط وقتی فرستاده می‌شود که حساب فعال و مسدودنشده وجود داشته باشد.</item>
/// <item>محدودیت‌ها همان محدودیت‌های کد ورود است: ۶ کد در ساعت برای هر شماره، ۶۰ ثانیه فاصلهٔ ارسال، کپچا بعد از چند درخواست
/// از یک IP و سهمیهٔ «ورود/ثبت‌نام» (۱۰ درخواست در دقیقه).</item>
/// <item>گام دوم (کد + رمز تازه) در <see cref="ResetPasswordModel"/> است.</item>
/// </list>
/// </summary>
public class ForgotPasswordModel(IUserService userService, IOtpService otpService) : ShopPageModel
{
    [BindProperty]
    public ForgotInput Input { get; set; } = new();

    /// <summary>کاربر واردشده به این صفحه نیاز ندارد (رمز را از پروفایل عوض می‌کند).</summary>
    public IActionResult OnGet(string? phone)
    {
        if (IsSignedIn)
            return RedirectToPage("/Profile/Password");
        if (!string.IsNullOrWhiteSpace(phone) && phone.Trim().IsValidMobile())
            Input.PhoneNumber = phone.Trim();
        ViewData[PageMetaKeys.Title] = "بازیابی رمز عبور";
        PrepareCaptcha(CaptchaActions.OtpSend);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ViewData[PageMetaKeys.Title] = "بازیابی رمز عبور";
        if (IsSignedIn)
            return RedirectToPage("/Profile/Password");

        ModelState.Clear();
        if (!VerifyCaptcha(CaptchaActions.OtpSend) || !TryValidateModel(Input, nameof(Input)))
            return Page();

        var phone = Input.PhoneNumber.Trim();
        // هر درخواست پیامک شمرده می‌شود: بعد از چند درخواست از یک IP، کپچا لازم می‌شود (جلوگیری از بمباران پیامکی).
        CaptchaFailed(CaptchaActions.OtpSend);

        if (await userService.CanResetPasswordAsync(phone))
        {
            var send = await otpService.SendAsync(phone, OtpPurpose.ResetPassword);
            if (send.Blocked)
            {
                ModelState.AddModelError(string.Empty, "تعداد درخواست کد برای این شماره بیش از حد مجاز است؛ لطفاً بعداً تلاش کنید.");
                return Page();
            }
            if (send.Failed)
            {
                ModelState.AddModelError(string.Empty, OtpSendResult.FailedMessage);
                return Page();
            }
        }
        else
        {
            // زمان پاسخ هم نباید عضو نبودن را لو بدهد (ارسال پیامک واقعی چند صد میلی‌ثانیه طول می‌کشد).
            await Task.Delay(Random.Shared.Next(250, 700));
        }

        SuccessMessage = "اگر این شماره در فروشگاه حساب داشته باشد، کد بازیابی برایش پیامک شد.";
        return RedirectToPage("/ResetPassword", new { phone });
    }

    /// <summary>فرم گام اول.</summary>
    public class ForgotInput
    {
        [Required(ErrorMessage = "شماره موبایل را وارد کنید.")]
        [RegularExpression(@"^09[0-9]{9}$", ErrorMessage = "شماره موبایل باید با ۰۹ شروع شود و ۱۱ رقم باشد.")]
        public string PhoneNumber { get; set; } = string.Empty;
    }
}
