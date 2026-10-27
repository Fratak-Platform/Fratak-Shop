using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// Phone-number verification with a one-time code (sent by SMS).
/// </summary>
public class VerifyModel(IOtpService otpService, IUserService userService, IWebHostEnvironment environment) : ShopPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public string[] Digits { get; set; } = [];

    public string PhoneNumber { get; private set; } = string.Empty;

    public int CodeLength => otpService.CodeLength;

    /// <summary>The code is shown on the page in development (no SMS provider configured).</summary>
    public string? DevelopmentCode { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userService.GetUserByIdAsync(CurrentUserId!.Value);
        if (user is null)
            return RedirectToPage("/Login");

        if (user.IsPhoneVerified)
            return RedirectToPage("/Welcome", new { returnUrl = ReturnUrl });

        PhoneNumber = user.PhoneNumber;
        var send = await otpService.SendAsync(PhoneNumber);
        DevelopmentCode = environment.IsDevelopment() ? send.DevelopmentCode : null;
        // همین درخواست صفحه را می‌سازد؛ ویژگی [TempData] تا پایان درخواست به TempData نمی‌رسد، پس مستقیم نوشته می‌شود.
        if (send.Failed)
            TempData["ErrorMessage"] = OtpSendResult.FailedMessage;

        ViewData[PageMetaKeys.Title] = "تایید شماره موبایل";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var phone = User.GetPhoneNumber();
        if (string.IsNullOrWhiteSpace(phone))
            return RedirectToPage("/Login");

        var code = string.Concat(Digits.Select(d => d?.Trim()));

        if (code.Length != otpService.CodeLength || !otpService.Verify(phone, code, OtpPurpose.VerifyPhone))
        {
            ErrorMessage = "کد وارد شده صحیح نیست یا منقضی شده است.";
            return RedirectToPage(new { returnUrl = ReturnUrl });
        }

        var result = await userService.ConfirmPhoneAsync(CurrentUserId!.Value);
        ShowResult(result);

        return RedirectToPage("/Welcome", new { returnUrl = ReturnUrl });
    }

    public async Task<IActionResult> OnPostResendAsync()
    {
        var phone = User.GetPhoneNumber();
        if (string.IsNullOrWhiteSpace(phone))
            return RedirectToPage("/Login");

        var send = await otpService.SendAsync(phone);
        if (send.Sent)
            SuccessMessage = "کد تایید جدید ارسال شد.";
        else if (send.Failed)
            ErrorMessage = OtpSendResult.FailedMessage;
        else if (send.Blocked)
            ErrorMessage = "تعداد درخواست کد برای این شماره بیش از حد مجاز است؛ لطفاً بعداً تلاش کنید.";
        else
            ErrorMessage = $"برای ارسال مجدد {Math.Ceiling(send.RetryAfter.TotalSeconds)} ثانیه صبر کنید.";

        return RedirectToPage(new { returnUrl = ReturnUrl });
    }
}
