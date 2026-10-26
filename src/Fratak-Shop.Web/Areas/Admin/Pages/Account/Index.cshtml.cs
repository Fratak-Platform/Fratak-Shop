using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.DTOs.Auth;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Auth;
using Fratak_Shop.Core.Services.Team;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Auth;
using Fratak_Shop.Web.Infrastructure.Team;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.TeamPages;

/// <summary>
/// صفحهٔ «امنیت حساب من» در پنل (مرحلهٔ هفتم – فاز ۱۰) – برای همهٔ کارکنان:
/// • راه‌اندازی/غیرفعال کردن برنامهٔ احراز هویت (Google Authenticator و …) با QR و کدهای بازیابی؛
/// • تاریخچهٔ ورودهای خودم (موفق، ناموفق، کد نادرست) با IP و مرورگر؛
/// • دستگاه‌های فعال و خروج از آن‌ها.
/// اگر «برنامهٔ احراز هویت اجباری» روشن باشد، کارمندی که فعالش نکرده با هر ورود به پنل اول به این صفحه می‌آید
/// (برای همین [AllowWithoutTotp] دارد).
/// </summary>
[AllowWithoutTotp]
public class AccountModel(
    IStaffTotpService totp,
    IMonitoringService monitoring,
    IRefreshTokenService refreshTokens,
    IShopAuthService authService,
    SecurityEventRecorder securityEvents) : AdminPageModel
{
    /// <summary>کلید TempData برای نمایش یک‌بارهٔ کدهای بازیابی بعد از فعال‌سازی.</summary>
    private const string RecoveryCodesKey = "TotpRecoveryCodes";

    public TotpStatus Status { get; private set; } = null!;

    /// <summary>کلید در حال راه‌اندازی (برای نمایش QR)؛ null اگر راه‌اندازی در جریان نیست.</summary>
    public TotpSetup? Setup { get; private set; }

    /// <summary>تصویر SVG کد QR آدرس otpauth (روی سرور ساخته می‌شود؛ کلید به هیچ سرویس بیرونی نمی‌رود).</summary>
    public string? QrSvg { get; private set; }

    /// <summary>کدهای بازیابی تازه که فقط همین یک بار نمایش داده می‌شوند.</summary>
    public List<string> NewRecoveryCodes { get; private set; } = [];

    public List<SecurityLog> LoginHistory { get; private set; } = [];

    public List<UserSessionDto> Sessions { get; private set; } = [];

    /// <summary>از فیلتر پنل آمده: برنامهٔ احراز هویت اجباری است و هنوز فعال نشده.</summary>
    [BindProperty(SupportsGet = true, Name = "setup")]
    public string? SetupReason { get; set; }

    public async Task OnGetAsync()
    {
        Status = await totp.GetStatusAsync(ActorId);
        Setup = await totp.GetPendingSetupAsync(ActorId);
        if (Setup is not null)
            QrSvg = QrCodes.Svg(Setup.Uri);
        if (TempData[RecoveryCodesKey] is string codes)
            NewRecoveryCodes = codes.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

        var history = await monitoring.GetSecurityAsync(new SecurityFilter { UserId = ActorId, Days = 90, Page = 1, Take = 30 });
        LoginHistory = history.Items;
        Sessions = await refreshTokens.GetActiveSessionsAsync(ActorId, User.GetSessionId());
    }

    public async Task<IActionResult> OnPostBeginAsync()
    {
        ShowResult(await totp.BeginSetupAsync(ActorId));
        return Redirect("/admin/account#totp");
    }

    public async Task<IActionResult> OnPostConfirmAsync(string? code)
    {
        var result = await totp.ConfirmSetupAsync(ActorId, code);
        if (Succeeded(result) && result.Data is { } codes)
            TempData[RecoveryCodesKey] = string.Join('\n', codes);
        else
            await securityEvents.RecordAsync(SecurityEventType.TotpFailed, ActorId, Access.PhoneNumber, "راه‌اندازی برنامهٔ احراز هویت");
        ShowResult(result);
        return Redirect("/admin/account#totp");
    }

    public async Task<IActionResult> OnPostDisableAsync(string? code)
    {
        // اگر برنامه اجباری باشد، بعد از غیرفعال کردن فیلتر پنل خودش کارمند را برای راه‌اندازی دوباره (مثلاً با گوشی جدید) به همین صفحه می‌آورد.
        var result = await totp.DisableAsync(ActorId, code);
        if (!Succeeded(result))
            await securityEvents.RecordAsync(SecurityEventType.TotpFailed, ActorId, Access.PhoneNumber, "غیرفعال کردن برنامهٔ احراز هویت");
        ShowResult(result);
        return Redirect("/admin/account#totp");
    }

    public async Task<IActionResult> OnPostRecoveryAsync(string? code)
    {
        var result = await totp.RegenerateRecoveryCodesAsync(ActorId, code);
        if (Succeeded(result) && result.Data is { } codes)
            TempData[RecoveryCodesKey] = string.Join('\n', codes);
        else
            await securityEvents.RecordAsync(SecurityEventType.TotpFailed, ActorId, Access.PhoneNumber, "ساخت کدهای بازیابی");
        ShowResult(result);
        return Redirect("/admin/account#totp");
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid familyId)
    {
        if (familyId == User.GetSessionId())
        {
            await authService.SignOutAsync();
            return Redirect("/login");
        }

        if (await refreshTokens.RevokeSessionAsync(ActorId, familyId, "revoked from admin account page"))
            SuccessMessage = "دستگاه انتخاب‌شده خارج شد (حداکثر تا چند دقیقهٔ دیگر).";
        else
            ErrorMessage = "نشست یافت نشد.";
        return Redirect("/admin/account#sessions");
    }

    public async Task<IActionResult> OnPostRevokeOthersAsync()
    {
        await authService.SignOutOtherDevicesAsync(ActorId, keepCurrent: true);
        SuccessMessage = "همهٔ دستگاه‌های دیگر فوراً خارج شدند.";
        return Redirect("/admin/account#sessions");
    }
}

/// <summary>
/// ساخت تصویر QR به شکل SVG با کتابخانهٔ متن‌باز QRCoder (MIT، بدون اینترنت).
/// محل استفاده: صفحهٔ «امنیت حساب من» برای اسکن کلید برنامهٔ احراز هویت.
/// </summary>
public static class QrCodes
{
    public static string Svg(string text)
    {
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.M);
        var svg = new QRCoder.SvgQRCode(data);
        return svg.GetGraphic(5);
    }
}
