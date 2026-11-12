using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Security;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Auditing;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Team;

// ═══════════════════════════════════════════════════════════════════════════
//  برنامهٔ احراز هویت کارکنان (TOTP) – مرحلهٔ هفتم، فاز ۱۰
//  مسیر کاربر: پنل ← «حساب من» ← «برنامهٔ احراز هویت» ← اسکن QR با Google Authenticator ← وارد کردن کد اول
//  ← نمایش ۱۰ کد بازیابی (فقط یک بار). از آن به بعد ورود با رمز یا پیامک، یک مرحلهٔ «کد برنامه» هم دارد.
//  کجا بررسی می‌شود؟ Web/Pages/Login* (ورود سایت)، /api/auth/token (ورود API) و صفحهٔ «تأیید دوباره» پنل.
//  روشن/خاموش کل قابلیت: تنظیمات ← امنیت کارکنان (IsStaffTotpEnabled / IsStaffTotpRequired).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// وضعیت برنامهٔ احراز هویت یک کارمند برای صفحهٔ «حساب من».
/// FeatureEnabled = قابلیت در تنظیمات روشن است؛ Active = این کارمند فعالش کرده؛ Pending = کلید ساخته شده ولی هنوز کد اول تأیید نشده.
/// </summary>
public sealed record TotpStatus(bool FeatureEnabled, bool Required, bool Active, bool Pending, DateTimeOffset? EnabledAt, int RecoveryCodesLeft);

/// <summary>اطلاعات راه‌اندازی: کلید (برای تایپ دستی، چهار‌حرفی جدا) و آدرس otpauth برای QR.</summary>
public sealed record TotpSetup(string Secret, string Uri);

/// <summary>نتیجهٔ بررسی یک کد.</summary>
public enum TotpCheck
{
    /// <summary>کد درست بود (یا کد بازیابی معتبر مصرف شد).</summary>
    Ok,

    Wrong,

    /// <summary>به‌خاطر تلاش‌های نادرست پشت‌سرهم، چند دقیقه قفل است.</summary>
    Locked,

    /// <summary>برای این کاربر فعال نیست (یا قابلیت در تنظیمات خاموش است).</summary>
    NotActive,

    /// <summary>کد درست بود ولی کد بازیابی بود (یکی کم شد) – برای هشدار به کاربر.</summary>
    RecoveryUsed,
}

public interface IStaffTotpService
{
    Task<TotpStatus> GetStatusAsync(Guid userId);

    /// <summary>
    /// آیا ورود این کاربر کد برنامهٔ احراز هویت لازم دارد؟ (قابلیت روشن + کاربر فعال کرده). ۲ دقیقه کش می‌شود.
    /// محل استفاده: Login، LoginCode، LoginTwoFactor، /api/auth/token و فیلتر پنل.
    /// </summary>
    Task<bool> IsActiveAsync(Guid userId);

    /// <summary>شروع راه‌اندازی: کلید تازه ساخته و (رمزنگاری‌شده) ذخیره می‌شود؛ تا تأیید کد اول، فعال نیست.</summary>
    Task<OperationResult<TotpSetup>> BeginSetupAsync(Guid userId);

    /// <summary>کلید در حال راه‌اندازی (برای نمایش دوبارهٔ QR بعد از کد نادرست). اگر راه‌اندازی در جریان نباشد null.</summary>
    Task<TotpSetup?> GetPendingSetupAsync(Guid userId);

    /// <summary>تأیید کد اول و فعال‌سازی. کدهای بازیابی (متن خام، فقط همین یک بار) برمی‌گردند.</summary>
    Task<OperationResult<List<string>>> ConfirmSetupAsync(Guid userId, string? code);

    /// <summary>غیرفعال کردن توسط خود کارمند؛ کد فعلی یا یک کد بازیابی لازم است.</summary>
    Task<OperationResult> DisableAsync(Guid userId, string? code);

    /// <summary>ساخت کدهای بازیابی تازه (قبلی‌ها باطل می‌شوند)؛ کد فعلی لازم است.</summary>
    Task<OperationResult<List<string>>> RegenerateRecoveryCodesAsync(Guid userId, string? code);

    /// <summary>
    /// بازنشانی برای کارمندی که گوشی‌اش را گم کرده – فقط مالک فروشگاه و نه برای خودش.
    /// محل استفاده: صفحهٔ کارمند (/admin/staff/member/{id}).
    /// </summary>
    Task<OperationResult> ResetAsync(Guid targetUserId, AdminAccess actor);

    /// <summary>
    /// بررسی کد ۶ رقمی (یا کد بازیابی اگر allowRecovery). کد پذیرفته‌شده دوباره قبول نمی‌شود و بعد از ۵ تلاش نادرست
    /// در ۱۰ دقیقه، بررسی ۱۰ دقیقه قفل می‌شود (حدس زدن ۶ رقم ناممکن شود).
    /// </summary>
    Task<TotpCheck> VerifyAsync(Guid userId, string? code, bool allowRecovery = true);
}

public sealed class StaffTotpService(
    FratakShopDbContext context,
    ISecretProtector protector,
    IStoreSettingService settings,
    IMemoryCache cache) : IStaffTotpService
{
    /// <summary>حداکثر تلاش نادرست پیش از قفل موقت.</summary>
    public const int MaxFailures = 5;

    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ActiveCacheLifetime = TimeSpan.FromMinutes(2);

    public async Task<TotpStatus> GetStatusAsync(Guid userId)
    {
        var setting = await settings.GetAsync();
        var user = await context.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.TotpSecretProtected, u.TotpEnabledAt, u.TotpRecoveryCodes })
            .FirstOrDefaultAsync();
        var active = user?.TotpSecretProtected is not null && user.TotpEnabledAt is not null;
        return new TotpStatus(setting.IsStaffTotpEnabled, setting.IsStaffTotpEnabled && setting.IsStaffTotpRequired, active,
            user?.TotpSecretProtected is not null && user.TotpEnabledAt is null, user?.TotpEnabledAt, CountCodes(user?.TotpRecoveryCodes));
    }

    public async Task<bool> IsActiveAsync(Guid userId)
    {
        if (!(await settings.GetAsync()).IsStaffTotpEnabled)
            return false;

        return await cache.GetOrCreateAsync(ActiveKey(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ActiveCacheLifetime;
            return await context.Users.AsNoTracking()
                .AnyAsync(u => u.Id == userId && u.TotpSecretProtected != null && u.TotpEnabledAt != null);
        });
    }

    public async Task<OperationResult<TotpSetup>> BeginSetupAsync(Guid userId)
    {
        var setting = await settings.GetAsync();
        if (!setting.IsStaffTotpEnabled)
            return OperationResult<TotpSetup>.Error("برنامهٔ احراز هویت در تنظیمات فروشگاه خاموش است.");

        var user = await context.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            return OperationResult<TotpSetup>.NotFound("کاربر یافت نشد.");
        if (user.TotpEnabledAt is not null)
            return OperationResult<TotpSetup>.Error("برنامهٔ احراز هویت از قبل فعال است؛ برای تعویض گوشی اول غیرفعالش کنید.");

        var secret = Totp.NewSecret();
        user.TotpSecretProtected = protector.Protect(Totp.ToBase32(secret));
        user.TotpLastStep = null;
        user.TotpRecoveryCodes = null;
        await context.SaveChangesAsync();
        return OperationResult<TotpSetup>.Success(BuildSetup(Totp.ToBase32(secret), user.PhoneNumber, setting.SiteName), "کلید ساخته شد.");
    }

    public async Task<TotpSetup?> GetPendingSetupAsync(Guid userId)
    {
        var user = await context.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.TotpSecretProtected, u.TotpEnabledAt, u.PhoneNumber }).FirstOrDefaultAsync();
        if (user?.TotpSecretProtected is null || user.TotpEnabledAt is not null)
            return null;
        var base32 = protector.Unprotect(user.TotpSecretProtected);
        return base32 is null ? null : BuildSetup(base32, user.PhoneNumber, (await settings.GetAsync()).SiteName);
    }

    public async Task<OperationResult<List<string>>> ConfirmSetupAsync(Guid userId, string? code)
    {
        if (IsLocked(userId))
            return OperationResult<List<string>>.Error(LockedMessage);

        var user = await context.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.TotpSecretProtected is null || user.TotpEnabledAt is not null)
            return OperationResult<List<string>>.Error("راه‌اندازی در جریان نیست؛ دوباره «شروع راه‌اندازی» را بزنید.");

        var secret = Totp.FromBase32(protector.Unprotect(user.TotpSecretProtected));
        if (secret is null)
            return OperationResult<List<string>>.Error("کلید قابل خواندن نیست؛ دوباره راه‌اندازی کنید.");

        var step = Totp.Verify(secret, code, DateTimeOffset.UtcNow);
        if (step is null)
        {
            RegisterFailure(userId);
            return OperationResult<List<string>>.Error("کد درست نیست. ساعت گوشی را روی «خودکار» بگذارید و کد تازه را وارد کنید.");
        }

        var codes = Totp.NewRecoveryCodes();
        user.TotpEnabledAt = DateTimeOffset.UtcNow;
        user.TotpLastStep = step;
        user.TotpRecoveryCodes = string.Join(';', codes.Select(c => Totp.HashRecoveryCode(Totp.NormalizeRecoveryCode(c)!)));
        context.SecurityLogs.Add(new SecurityLog
        {
            Type = SecurityEventType.TotpEnabled,
            UserId = userId,
            Details = "برنامهٔ احراز هویت فعال شد.",
            IpAddress = AuditContext.Current?.IpAddress,
        });
        await context.SaveChangesAsync();
        ClearFailures(userId);
        cache.Remove(ActiveKey(userId));
        return OperationResult<List<string>>.Success(codes, "برنامهٔ احراز هویت فعال شد. کدهای بازیابی را جای امنی نگه دارید.");
    }

    public async Task<OperationResult> DisableAsync(Guid userId, string? code)
    {
        var check = await VerifyAsync(userId, code);
        if (check is TotpCheck.Locked)
            return OperationResult.Error(LockedMessage);
        if (check is not (TotpCheck.Ok or TotpCheck.RecoveryUsed))
            return OperationResult.Error("برای غیرفعال کردن، کد فعلی برنامه یا یک کد بازیابی را درست وارد کنید.");

        await ClearAsync(userId, "برنامهٔ احراز هویت توسط خود کارمند غیرفعال شد.");
        return OperationResult.Success("برنامهٔ احراز هویت غیرفعال شد.");
    }

    public async Task<OperationResult<List<string>>> RegenerateRecoveryCodesAsync(Guid userId, string? code)
    {
        var check = await VerifyAsync(userId, code, allowRecovery: false);
        if (check is TotpCheck.Locked)
            return OperationResult<List<string>>.Error(LockedMessage);
        if (check is not TotpCheck.Ok)
            return OperationResult<List<string>>.Error("کد فعلی برنامه را درست وارد کنید.");

        var codes = Totp.NewRecoveryCodes();
        await context.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.TotpRecoveryCodes,
            string.Join(';', codes.Select(c => Totp.HashRecoveryCode(Totp.NormalizeRecoveryCode(c)!)))));
        return OperationResult<List<string>>.Success(codes, "کدهای بازیابی تازه ساخته شد؛ کدهای قبلی دیگر کار نمی‌کنند.");
    }

    public async Task<OperationResult> ResetAsync(Guid targetUserId, AdminAccess actor)
    {
        if (!actor.IsOwner)
            return OperationResult.Error("فقط مالک فروشگاه می‌تواند برنامهٔ احراز هویت کارمند را بازنشانی کند.");
        if (targetUserId == actor.UserId)
            return OperationResult.Error("برای حساب خودتان از صفحهٔ «حساب من» استفاده کنید.");
        if (!await context.Users.AnyAsync(u => u.Id == targetUserId && u.TotpSecretProtected != null))
            return OperationResult.Error("این کارمند برنامهٔ احراز هویت ندارد.");

        await ClearAsync(targetUserId, $"برنامهٔ احراز هویت توسط {actor.DisplayName} بازنشانی شد.");
        return OperationResult.Success("برنامهٔ احراز هویت کارمند بازنشانی شد؛ دفعهٔ بعد باید دوباره راه‌اندازی کند.");
    }

    public async Task<TotpCheck> VerifyAsync(Guid userId, string? code, bool allowRecovery = true)
    {
        if (!(await settings.GetAsync()).IsStaffTotpEnabled)
            return TotpCheck.NotActive;
        if (IsLocked(userId))
            return TotpCheck.Locked;

        var user = await context.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.TotpSecretProtected, u.TotpEnabledAt, u.TotpLastStep, u.TotpRecoveryCodes })
            .FirstOrDefaultAsync();
        if (user?.TotpSecretProtected is null || user.TotpEnabledAt is null)
            return TotpCheck.NotActive;

        var secret = Totp.FromBase32(protector.Unprotect(user.TotpSecretProtected));
        if (secret is not null && Totp.Verify(secret, code, DateTimeOffset.UtcNow, user.TotpLastStep) is { } step)
        {
            // ذخیرهٔ شرطی: اگر دو درخواست هم‌زمان با یک کد بیایند فقط یکی برنده می‌شود (کد تکراری رد می‌شود).
            var updated = await context.Users
                .Where(u => u.Id == userId && (u.TotpLastStep == null || u.TotpLastStep < step))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.TotpLastStep, step));
            if (updated == 1)
            {
                ClearFailures(userId);
                return TotpCheck.Ok;
            }
        }
        else if (allowRecovery && Totp.NormalizeRecoveryCode(code) is { } recovery && user.TotpRecoveryCodes is { Length: > 0 } stored)
        {
            var hash = Totp.HashRecoveryCode(recovery);
            var hashes = stored.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (hashes.Remove(hash))
            {
                // باز هم شرطی: فقط اگر فهرست در این فاصله عوض نشده باشد (هر کد فقط یک بار).
                var remaining = hashes.Count == 0 ? null : string.Join(';', hashes);
                var updated = await context.Users.Where(u => u.Id == userId && u.TotpRecoveryCodes == stored)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.TotpRecoveryCodes, remaining));
                if (updated == 1)
                {
                    context.SecurityLogs.Add(new SecurityLog
                    {
                        Type = SecurityEventType.TotpRecoveryUsed,
                        UserId = userId,
                        Details = $"یک کد بازیابی مصرف شد؛ {hashes.Count} کد باقی مانده.",
                        IpAddress = AuditContext.Current?.IpAddress,
                    });
                    await context.SaveChangesAsync();
                    ClearFailures(userId);
                    return TotpCheck.RecoveryUsed;
                }
            }
        }

        RegisterFailure(userId);
        return IsLocked(userId) ? TotpCheck.Locked : TotpCheck.Wrong;
    }

    /// <summary>پیام قفل موقت (در چند جای Web هم استفاده می‌شود).</summary>
    public const string LockedMessage = "به‌خاطر چند کد نادرست پشت‌سرهم، ۱۰ دقیقه صبر کنید و دوباره تلاش کنید.";

    /// <summary>کلید را برای خوانایی چهار‌حرفی جدا می‌کند. مثال: «GEZD GNBV GY3T …».</summary>
    public static string GroupSecret(string base32) =>
        string.Join(' ', Enumerable.Range(0, (base32.Length + 3) / 4).Select(i => base32.Substring(i * 4, Math.Min(4, base32.Length - i * 4))));

    private static TotpSetup BuildSetup(string base32, string account, string? siteName)
    {
        var issuer = string.IsNullOrWhiteSpace(siteName) ? "Fratak" : siteName.Trim();
        // «:» در نام صادرکننده آدرس را خراب می‌کند.
        issuer = issuer.Replace(":", " ");
        return new TotpSetup(GroupSecret(base32), Totp.BuildUri(issuer, account, base32));
    }

    private async Task ClearAsync(Guid userId, string details)
    {
        await context.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.TotpSecretProtected, (string?)null)
            .SetProperty(u => u.TotpEnabledAt, (DateTimeOffset?)null)
            .SetProperty(u => u.TotpLastStep, (long?)null)
            .SetProperty(u => u.TotpRecoveryCodes, (string?)null));
        context.SecurityLogs.Add(new SecurityLog
        {
            Type = SecurityEventType.TotpDisabled,
            UserId = userId,
            Details = details,
            IpAddress = AuditContext.Current?.IpAddress,
        });
        await context.SaveChangesAsync();
        cache.Remove(ActiveKey(userId));
    }

    private static int CountCodes(string? stored) =>
        string.IsNullOrEmpty(stored) ? 0 : stored.Split(';', StringSplitOptions.RemoveEmptyEntries).Length;

    private static string ActiveKey(Guid userId) => $"totp:active:{userId:N}";

    private static string FailureKey(Guid userId) => $"totp:fail:{userId:N}";

    private bool IsLocked(Guid userId) => cache.TryGetValue(FailureKey(userId), out FailureCounter? counter) && counter!.Count >= MaxFailures;

    private void RegisterFailure(Guid userId)
    {
        // شمارنده با «انقضای مطلق» از اولین خطا: بعد از ۱۰ دقیقه خودبه‌خود صفر می‌شود.
        var counter = cache.GetOrCreate(FailureKey(userId), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = FailureWindow;
            return new FailureCounter();
        })!;
        Interlocked.Increment(ref counter.Count);
    }

    private void ClearFailures(Guid userId) => cache.Remove(FailureKey(userId));

    private sealed class FailureCounter
    {
        public int Count;
    }
}
