using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Team;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Team;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.TeamPages;

/// <summary>
/// صفحهٔ «تأیید دوبارهٔ هویت» (مرحلهٔ هفتم – فاز ۱۰). وقتی کارمند کاری حساس انجام می‌دهد (مثلاً تغییر کیف پول مشتری،
/// تسویهٔ برداشت یا تغییر نقش‌ها) و «تأیید دوباره» در تنظیمات روشن است، فیلتر پنل او را اینجا می‌فرستد.
/// یکی از سه راه: رمز عبور، کد برنامهٔ احراز هویت یا کد پیامکی. بعد از تأیید، کوکی کوتاه‌عمر <see cref="ReauthTicket"/>
/// صادر می‌شود و کارمند به صفحهٔ قبلی برمی‌گردد تا کار را دوباره انجام دهد.
/// هدف: اگر کسی پشت سیستم باز یک کارمند نشست (یا نشست دزدیده شد)، نتواند بی‌درنگ کار مالی/دسترسی انجام دهد.
/// </summary>
[AllowWithoutTotp]
public class ReauthModel(
    IUserService userService,
    IStaffTotpService totp,
    IOtpService otpService,
    IReauthTicket ticket,
    IStoreSettingService settingsService,
    SecurityEventRecorder securityEvents) : AdminPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool HasPassword { get; private set; }

    public bool HasTotp { get; private set; }

    public int Minutes { get; private set; }

    /// <summary>کد پیامکی در حالت توسعه (بدون سرویس پیامک) برای آزمایش.</summary>

    /// <summary>آیا کد پیامکی فرستاده شده و فرم واردکردنش باید نمایش داده شود؟</summary>
    public bool SmsSent { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostPasswordAsync(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return await FailAsync("رمز عبور را وارد کنید.", null);
        // LoginAsync همان بررسی ورود را انجام می‌دهد (قفل شدن بعد از چند تلاش ناموفق هم در آن هست).
        var result = await userService.LoginAsync(new LoginCommand { PhoneNumber = Access.PhoneNumber, Password = password });
        return Succeeded(result) && result.Data?.Id == ActorId
            ? Done("رمز عبور")
            : await FailAsync(result.Message ?? "رمز عبور نادرست است.", "رمز عبور");
    }

    public async Task<IActionResult> OnPostTotpAsync(string? code)
    {
        var check = await totp.VerifyAsync(ActorId, code, allowRecovery: true);
        switch (check)
        {
            case TotpCheck.Ok:
            {
                return Done("برنامهٔ احراز هویت");
            }
            case TotpCheck.RecoveryUsed:
            {
                await securityEvents.RecordAsync(SecurityEventType.TotpRecoveryUsed, ActorId, Access.PhoneNumber, "تأیید دوباره با کد بازیابی");
                return Done("کد بازیابی");
            }
            case TotpCheck.Locked:
            {
                return await FailAsync(StaffTotpService.LockedMessage, "برنامهٔ احراز هویت (قفل)");
            }
            default:
            {
                await securityEvents.RecordAsync(SecurityEventType.TotpFailed, ActorId, Access.PhoneNumber, "تأیید دوباره");
                return await FailAsync("کد برنامهٔ احراز هویت نادرست است.", "برنامهٔ احراز هویت");
            }
        }
    }

    public async Task<IActionResult> OnPostSendSmsAsync()
    {
        var send = await otpService.SendAsync(Access.PhoneNumber, OtpPurpose.Reauth);
        await LoadAsync();
        SmsSent = !send.Failed;
        if (send.Failed)
            ErrorMessage = OtpSendResult.FailedMessage;
        else if (send.Blocked)
            ErrorMessage = "تعداد پیامک‌های این ساعت به حد مجاز رسیده است؛ از رمز عبور یا برنامهٔ احراز هویت استفاده کنید.";
        else if (!send.Sent)
            ErrorMessage = $"کد قبلی هنوز معتبر است؛ {Math.Ceiling(send.RetryAfter.TotalSeconds)} ثانیهٔ دیگر می‌توانید دوباره درخواست کنید.";
        return Page();
    }

    public async Task<IActionResult> OnPostSmsAsync(string? code)
    {
        var digits = global::Fratak_Shop.Core.Utilities.SearchText.NormalizeDigits(code).Trim();
        if (digits.Length > 0 && otpService.Verify(Access.PhoneNumber, digits, OtpPurpose.Reauth))
            return Done("کد پیامکی");
        var page = await FailAsync("کد پیامکی نادرست یا منقضی است.", "کد پیامکی");
        SmsSent = true;
        return page;
    }

    // ── کمکی‌ها ──

    private async Task LoadAsync()
    {
        var user = await userService.GetUserByIdAsync(ActorId);
        HasPassword = user?.HasPassword == true;
        HasTotp = await totp.IsActiveAsync(ActorId);
        Minutes = (await settingsService.GetAsync()).ReauthMinutes;
    }

    /// <summary>تأیید موفق: صدور بلیت و برگشت به صفحهٔ قبل (فقط آدرس‌های داخل پنل).</summary>
    private IActionResult Done(string method)
    {
        ticket.Issue(HttpContext, ActorId);
        SuccessMessage = $"هویت شما با {method} تأیید شد؛ حالا کار موردنظر را دوباره انجام دهید.";
        return Redirect(SafeReturnUrl(ReturnUrl));
    }

    private async Task<IActionResult> FailAsync(string message, string? method)
    {
        if (method is not null)
            await securityEvents.RecordAsync(SecurityEventType.ReauthFailed, ActorId, Access.PhoneNumber, method);
        await LoadAsync();
        ErrorMessage = message;
        return Page();
    }

    /// <summary>
    /// فقط آدرس نسبی داخل پنل پذیرفته می‌شود تا کسی نتواند با لینک ساختگی کارمند را بعد از تأیید به سایت دیگری بفرستد.
    /// مثال: «/admin/customers/…» ← همان؛ «https://evil.com» یا «//evil.com» ← «/admin».
    /// </summary>
    public static string SafeReturnUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) && url.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("//", StringComparison.Ordinal) && !url.Contains('\\') && !url.StartsWith("/admin/reauth", StringComparison.OrdinalIgnoreCase)
            ? url
            : "/admin";
}
