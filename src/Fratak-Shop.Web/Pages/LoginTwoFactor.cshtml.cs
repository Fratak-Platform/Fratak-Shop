using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Auth;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>Second step of a password sign-in for accounts with two-step login: the SMS code.</summary>
public class LoginTwoFactorModel(
    ITwoFactorTicket ticket,
    IOtpService otpService,
    IUserService userService,
    IShopAuthService authService) : ShopPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public string[] Digits { get; set; } = [];

    public int CodeLength => otpService.CodeLength;

    public string MaskedPhone { get; private set; } = string.Empty;

    public string? DevelopmentCode { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await PendingUserAsync();
        if (user is null)
            return Expired();

        MaskedPhone = Mask(user.PhoneNumber);
        DevelopmentCode = TempData[LoginModel.DevelopmentCodeKey] as string;
        ViewData[PageMetaKeys.Title] = "ورود دو مرحله‌ای";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var pending = ticket.Read(HttpContext);
        var user = await PendingUserAsync();
        if (pending is null || user is null)
            return Expired();

        var code = string.Concat(Digits.Select(d => d?.Trim()));
        if (code.Length != otpService.CodeLength || !otpService.Verify(user.PhoneNumber, code, OtpPurpose.TwoFactor))
        {
            ErrorMessage = "کد وارد شده صحیح نیست یا منقضی شده است.";
            return RedirectToPage(new { returnUrl = ReturnUrl });
        }

        ticket.Clear(HttpContext);
        await userService.RecordLoginAsync(user.Id);
        await authService.SignInAsync(user, pending.Value.RememberMe);

        SuccessMessage = $"{user.FullName ?? "کاربر"} عزیز، خوش آمدید.";
        return RedirectToLocal(ReturnUrl, "/Index");
    }

    public async Task<IActionResult> OnPostResendAsync()
    {
        var user = await PendingUserAsync();
        if (user is null)
            return Expired();

        var send = await otpService.SendAsync(user.PhoneNumber, OtpPurpose.TwoFactor);
        if (send.Sent)
            SuccessMessage = "کد جدید ارسال شد.";
        else if (send.Blocked)
            ErrorMessage = "تعداد درخواست کد بیش از حد مجاز است؛ لطفاً بعداً تلاش کنید.";
        else
            ErrorMessage = $"برای ارسال مجدد {Math.Ceiling(send.RetryAfter.TotalSeconds)} ثانیه صبر کنید.";

        TempData[LoginModel.DevelopmentCodeKey] = send.DevelopmentCode;
        return RedirectToPage(new { returnUrl = ReturnUrl });
    }

    private async Task<Core.DTOs.Users.Query.UserDto?> PendingUserAsync()
    {
        var pending = ticket.Read(HttpContext);
        if (pending is null)
            return null;

        var user = await userService.GetUserByIdAsync(pending.Value.UserId);
        return user is { TwoFactorEnabled: true } ? user : null;
    }

    private IActionResult Expired()
    {
        ticket.Clear(HttpContext);
        ErrorMessage = "زمان ورود به پایان رسید؛ لطفاً دوباره وارد شوید.";
        return RedirectToPage("/Login", new { mode = "password", returnUrl = ReturnUrl });
    }

    private static string Mask(string phone) =>
        phone.Length == 11 ? $"{phone[..4]}***{phone[^4..]}" : phone;
}
