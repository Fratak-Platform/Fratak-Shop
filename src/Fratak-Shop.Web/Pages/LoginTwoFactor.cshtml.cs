using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Auth;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// Second step of a sign-in for accounts with two-step login: the SMS code, or – for staff who set up an
/// authenticator app (مرحلهٔ هفتم – فاز ۱۰) – the 6-digit TOTP code / a one-time recovery code instead (no SMS is sent).
/// </summary>
public class LoginTwoFactorModel(
    ITwoFactorTicket ticket,
    IOtpService otpService,
    IUserService userService,
    IShopAuthService authService,
    Core.Services.Team.IStaffTotpService totp,
    Infrastructure.Admin.SecurityEventRecorder securityEvents) : ShopPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public string[] Digits { get; set; } = [];

    /// <summary>کد بازیابی (وقتی گوشیِ برنامهٔ احراز هویت در دسترس نیست). مثال: «K7QM-2XPA».</summary>
    [BindProperty]
    public string? RecoveryCode { get; set; }

    /// <summary>true = کد برنامهٔ احراز هویت پرسیده می‌شود (نه پیامک).</summary>
    public bool IsTotp { get; private set; }

    public int CodeLength => IsTotp ? 6 : otpService.CodeLength;

    public string MaskedPhone { get; private set; } = string.Empty;

    public string? DevelopmentCode { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await PendingUserAsync();
        if (user is null)
            return Expired();

        MaskedPhone = Mask(user.PhoneNumber);
        DevelopmentCode = IsTotp ? null : TempData[LoginModel.DevelopmentCodeKey] as string;
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
        if (IsTotp)
            return await VerifyTotpAsync(user, string.IsNullOrWhiteSpace(RecoveryCode) ? code : RecoveryCode, pending.Value.RememberMe);

        if (code.Length != otpService.CodeLength || !otpService.Verify(user.PhoneNumber, code, OtpPurpose.TwoFactor))
        {
            await securityEvents.RecordAsync(Data.Entities.Admin.SecurityEventType.OtpFailed, user.Id, user.PhoneNumber, "ورود دومرحله‌ای");
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
        if (IsTotp)
            return RedirectToPage(new { returnUrl = ReturnUrl });

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
        if (user is null)
            return null;
        // برنامهٔ احراز هویت (اگر کارمند فعال کرده باشد) بر پیامک مقدم است.
        IsTotp = await totp.IsActiveAsync(user.Id);
        return IsTotp || user.TwoFactorEnabled ? user : null;
    }

    /// <summary>بررسی کد برنامهٔ احراز هویت یا کد بازیابی؛ بعد از ۵ کد نادرست، ۱۰ دقیقه قفل می‌شود.</summary>
    private async Task<IActionResult> VerifyTotpAsync(Core.DTOs.Users.Query.UserDto user, string? code, bool rememberMe)
    {
        var check = await totp.VerifyAsync(user.Id, code, allowRecovery: true);
        if (check is not (Core.Services.Team.TotpCheck.Ok or Core.Services.Team.TotpCheck.RecoveryUsed))
        {
            await securityEvents.RecordAsync(Data.Entities.Admin.SecurityEventType.TotpFailed, user.Id, user.PhoneNumber, "ورود با برنامهٔ احراز هویت");
            ErrorMessage = check == Core.Services.Team.TotpCheck.Locked
                ? Core.Services.Team.StaffTotpService.LockedMessage
                : "کد برنامهٔ احراز هویت (یا کد بازیابی) صحیح نیست.";
            return RedirectToPage(new { returnUrl = ReturnUrl });
        }

        ticket.Clear(HttpContext);
        await userService.RecordLoginAsync(user.Id);
        await authService.SignInAsync(user, rememberMe);
        if (check == Core.Services.Team.TotpCheck.RecoveryUsed)
        {
            await securityEvents.RecordAsync(Data.Entities.Admin.SecurityEventType.TotpRecoveryUsed, user.Id, user.PhoneNumber, "ورود با کد بازیابی");
            SuccessMessage = "با کد بازیابی وارد شدید؛ آن کد دیگر کار نمی‌کند. اگر گوشی‌تان گم شده، از «امنیت حساب من» برنامه را دوباره راه‌اندازی کنید.";
            return Redirect("/admin/account#totp");
        }

        SuccessMessage = $"{user.FullName ?? "کاربر"} عزیز، خوش آمدید.";
        return RedirectToLocal(ReturnUrl, "/Index");
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
