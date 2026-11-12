using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Ops;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Ops;

/// <summary>دور ۲۴ (پیشنهاد ۷۴): شدت یک مورد گزارش امنیتی. عدد بزرگ‌تر = مهم‌تر.</summary>
public enum SecurityCheckLevel
{
    Ok = 0,
    Info = 1,
    Warning = 2,
    Critical = 3,
}

/// <summary>
/// یک مورد گزارش امنیتی. مثال: («default-password»، «رمز پیش‌فرض کارکنان»، Warning، «۲ نفر هنوز رمز اولیه دارند: …»، «/admin/account»).
/// </summary>
public sealed record SecurityCheck(string Key, string Title, SecurityCheckLevel Level, string Detail, string? FixUrl);

/// <summary>آمار رویدادهای ۷ روز اخیر. TopIps = آی‌پی‌هایی با ۵ ورود ناموفق یا بیشتر.</summary>
public sealed record SecurityActivity(int FailedLogins, int LockedOut, int AdminDenied, int NewDevices, int RoleChanges, int BlockedIps,
    IReadOnlyList<(string Ip, int Count)> TopIps);

/// <summary>گزارش کامل: امتیاز ۰ تا ۱۰۰، رتبهٔ فارسی، موارد (مهم‌ترین اول) و آمار هفته.</summary>
public sealed record SecurityReport(DateTimeOffset GeneratedAt, int Score, string Grade, IReadOnlyList<SecurityCheck> Checks, SecurityActivity Activity);

/// <summary>
/// دور ۲۴ (پیشنهاد ۷۴): «گزارش امنیتی» خودکار.
/// <para>
/// هر هفته (و هر بار باز کردن صفحهٔ <c>/admin/system/security-report</c>) این‌ها بررسی می‌شود: کارکنانی که هنوز رمز پیش‌فرض دارند،
/// ورود دومرحله‌ای کارکنان، روشن بودن کد امنیتی، تازگی پشتیبان پایگاه داده، حملهٔ حدس رمز (آی‌پی‌های پرتکرار) و مسدودسازی خودکار،
/// تلاش برای ورود غیرمجاز به پنل، دسترس‌پذیری هفته و توکن پایش بیرونی. خروجی یک امتیاز ساده و فهرست کارهای پیشنهادی با لینک رفع است.
/// </para>
/// <para>فقط گزارش می‌دهد؛ هیچ تنظیمی را خودش تغییر نمی‌دهد. هر هفته یک اعلان خلاصه برای مالک و مسئولان فنی/امنیت فرستاده می‌شود.</para>
/// </summary>
public interface ISecurityReportService
{
    /// <summary>ساخت گزارش (کش ۱۰ دقیقه؛ بررسی رمز پیش‌فرض سنگین است). <paramref name="refresh"/> = بدون کش.</summary>
    Task<SecurityReport> BuildAsync(DateTimeOffset now, bool refresh = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// اعلان هفتگی (ساعت ۹ تا ۲۰ ایران، حداکثر هفته‌ای یک‌بار). خروجی: تعداد گیرنده‌ها (۰ = هنوز وقتش نیست).
    /// محل صدا زدن: EngagementMaintenanceService (هر چند دقیقه؛ خودش ساعتی یک‌بار بیشتر کار نمی‌کند).
    /// </summary>
    Task<int> SendWeeklyAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>منطق خالص امتیاز و متن گزارش (بدون دیتابیس) برای تست‌پذیری.</summary>
public static class SecurityReportRules
{
    /// <summary>پیشوند عنوان اعلان هفتگی (برای جلوگیری از ارسال تکراری).</summary>
    public const string WeeklyTitlePrefix = "گزارش امنیتی هفتگی";

    /// <summary>
    /// امتیاز: ۱۰۰ منهای ۲۵ برای هر بحرانی، ۱۰ برای هر هشدار و ۲ برای هر نکته (حداقل ۰).
    /// مثال: [Warning، Info، Ok] ← ۸۸.
    /// </summary>
    public static int Score(IEnumerable<SecurityCheck> checks) =>
        Math.Clamp(100 - checks.Sum(c => c.Level switch
        {
            SecurityCheckLevel.Critical => 25,
            SecurityCheckLevel.Warning => 10,
            SecurityCheckLevel.Info => 2,
            _ => 0,
        }), 0, 100);

    /// <summary>رتبه. مثال: ۹۵ ← «عالی»؛ ۸۰ ← «خوب»؛ ۶۰ ← «نیاز به توجه»؛ ۳۰ ← «ضعیف».</summary>
    public static string Grade(int score) => score switch
    {
        >= 90 => "عالی",
        >= 75 => "خوب",
        >= 50 => "نیاز به توجه",
        _ => "ضعیف",
    };

    /// <summary>
    /// وضعیت پشتیبان. مثال: هیچ ← Warning؛ ۷۰ ساعت پیش ← Warning؛ ۱۰ ساعت پیش ← Ok.
    /// </summary>
    public static SecurityCheck Backup(DateTimeOffset? lastSuccess, DateTimeOffset now)
    {
        if (lastSuccess is not { } last)
            return new SecurityCheck("backup", "پشتیبان پایگاه داده", SecurityCheckLevel.Warning,
                "هنوز هیچ پشتیبان موفقی ثبت نشده است. پشتیبان خودکار روزانه را روشن کنید.", "/admin/system/backups");
        var hours = (int)(now - last).TotalHours;
        return hours > 48
            ? new SecurityCheck("backup", "پشتیبان پایگاه داده", SecurityCheckLevel.Warning,
                $"آخرین پشتیبان موفق {Digits(hours)} ساعت پیش بوده است (بیش از ۲ روز).", "/admin/system/backups")
            : new SecurityCheck("backup", "پشتیبان پایگاه داده", SecurityCheckLevel.Ok,
                $"آخرین پشتیبان موفق {Digits(Math.Max(0, hours))} ساعت پیش.", "/admin/system/backups");
    }

    /// <summary>
    /// حملهٔ حدس رمز. مثال: آی‌پی با ۴۰ ورود ناموفق و مسدودسازی خودکار خاموش ← Warning؛ روشن ← Info؛ بدون آی‌پی پرتکرار ← Ok.
    /// </summary>
    public static SecurityCheck BruteForce(IReadOnlyList<(string Ip, int Count)> topIps, bool autoBlock)
    {
        var heavy = topIps.Where(t => t.Count >= 20).ToList();
        if (heavy.Count == 0)
            return new SecurityCheck("brute-force", "حملهٔ حدس رمز", SecurityCheckLevel.Ok,
                "در ۷ روز اخیر آی‌پی‌ای با ۲۰ ورود ناموفق یا بیشتر دیده نشد.", "/admin/system/security");
        var list = string.Join("، ", heavy.Take(5).Select(t => $"{t.Ip} ({Digits(t.Count)})"));
        return autoBlock
            ? new SecurityCheck("brute-force", "حملهٔ حدس رمز", SecurityCheckLevel.Info,
                $"تلاش‌های پرتکرار از {list}؛ مسدودسازی خودکار روشن است و جلوی آن‌ها را می‌گیرد.", "/admin/system/firewall")
            : new SecurityCheck("brute-force", "حملهٔ حدس رمز", SecurityCheckLevel.Warning,
                $"تلاش‌های پرتکرار از {list}. مسدودسازی خودکار آی‌پی را روشن کنید یا این آی‌پی‌ها را در فایروال ببندید.", "/admin/system/firewall");
    }

    /// <summary>متن اعلان هفتگی. مثال: «امتیاز ۸۸ از ۱۰۰ (خوب) · ۱ هشدار: رمز پیش‌فرض کارکنان».</summary>
    public static string WeeklyBody(SecurityReport report)
    {
        var problems = report.Checks.Where(c => c.Level >= SecurityCheckLevel.Warning).ToList();
        var head = $"امتیاز {Digits(report.Score)} از ۱۰۰ ({report.Grade})";
        return problems.Count == 0
            ? $"{head} · مورد مهمی پیدا نشد. ورود ناموفق هفته: {Digits(report.Activity.FailedLogins)}."
            : $"{head} · {Digits(problems.Count)} مورد نیازمند توجه: {string.Join("، ", problems.Select(p => p.Title))}.";
    }

    /// <summary>عدد با رقم فارسی. مثال: 42 ← «۴۲».</summary>
    public static string Digits(int value) => new(value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        .Select(c => char.IsAsciiDigit(c) ? (char)('۰' + (c - '0')) : c).ToArray());
}

/// <inheritdoc cref="ISecurityReportService"/>
public sealed class SecurityReportService(
    FratakShopDbContext context,
    IStoreSettingService settings,
    IUptimeService uptime,
    IStaffNotificationService notifications,
    IMemoryCache cache) : ISecurityReportService
{
    private const string CacheKey = "ops:security-report";
    private static DateTimeOffset _lastWeeklyCheck = DateTimeOffset.MinValue;

    /// <inheritdoc />
    public async Task<SecurityReport> BuildAsync(DateTimeOffset now, bool refresh = false, CancellationToken cancellationToken = default)
    {
        if (!refresh && cache.TryGetValue(CacheKey, out SecurityReport? cached) && cached is not null)
            return cached;

        var store = await settings.GetAsync();
        var weekAgo = now.AddDays(-7);
        var checks = new List<SecurityCheck>();

        // ── کارکنان: رمز پیش‌فرض و ورود دومرحله‌ای ──
        var staffIds = await context.UserAdminRoles.Select(m => m.UserId).Distinct().ToListAsync(cancellationToken);
        var staff = await context.Users
            .Where(u => !u.IsBlocked && (u.Role == UserRole.SuperAdmin || u.Role == UserRole.Admin || staffIds.Contains(u.Id)))
            .OrderBy(u => u.Role).Take(60)
            .Select(u => new { u.FirstName, u.LastName, u.PhoneNumber, u.PasswordHash, u.TwoFactorEnabled })
            .ToListAsync(cancellationToken);
        // رمز اولیهٔ مدیران پیش‌فرض (۱۲۳۴۵۶۷۸)؛ هر کارمندی که هنوز همین رمز را دارد گزارش می‌شود.
        var defaultPassword = DefaultAdminAccount.DefaultInitialPassword;
        var weak = staff.Where(s => s.PasswordHash is { Length: > 0 } && SafeVerify(defaultPassword, s.PasswordHash)).ToList();
        checks.Add(weak.Count == 0
            ? new SecurityCheck("default-password", "رمز پیش‌فرض کارکنان", SecurityCheckLevel.Ok, "هیچ‌کدام از کارکنان رمز اولیه را ندارند.", "/admin/account")
            : new SecurityCheck("default-password", "رمز پیش‌فرض کارکنان", SecurityCheckLevel.Warning,
                $"{SecurityReportRules.Digits(weak.Count)} نفر هنوز رمز اولیه دارند: {string.Join("، ", weak.Take(6).Select(w => Name(w.FirstName, w.LastName, w.PhoneNumber)))}. هر نفر از «حساب من» رمز را عوض کند.",
                "/admin/account"));
        var without2Fa = staff.Count(s => !s.TwoFactorEnabled);
        checks.Add(without2Fa == 0
            ? new SecurityCheck("staff-2fa", "ورود دومرحله‌ای کارکنان", SecurityCheckLevel.Ok, "همهٔ کارکنان ورود دومرحله‌ای دارند.", "/admin/account")
            : new SecurityCheck("staff-2fa", "ورود دومرحله‌ای کارکنان", SecurityCheckLevel.Info,
                $"{SecurityReportRules.Digits(without2Fa)} نفر از {SecurityReportRules.Digits(staff.Count)} کارمند ورود دومرحله‌ای (برنامهٔ Authenticator) را روشن نکرده‌اند.", "/admin/account"));

        // ── کد امنیتی و توکن پایش ──
        checks.Add(store.IsCaptchaEnabled
            ? new SecurityCheck("captcha", "کد امنیتی فرم‌ها", SecurityCheckLevel.Ok, "کد امنیتی هوشمند (بعد از چند تلاش ناموفق) روشن است.", "/admin/settings")
            : new SecurityCheck("captcha", "کد امنیتی فرم‌ها", SecurityCheckLevel.Warning,
                "کد امنیتی خاموش است؛ فرم‌های ورود، کد تخفیف و کارت هدیه در برابر ربات بی‌دفاع‌اند.", "/admin/settings"));
        if (string.IsNullOrWhiteSpace(store.HealthCheckToken))
            checks.Add(new SecurityCheck("health-token", "پایش بیرونی", SecurityCheckLevel.Info,
                "برای دیدن جزئیات /healthz در سرویس پایش (مثل UptimeRobot) یک «توکن بررسی سلامت» در تنظیمات بگذارید.", "/admin/settings"));

        // ── پشتیبان ──
        var lastBackup = await context.BackupRecords
            .Where(b => b.Kind == BackupKind.Database && b.Status == BackupStatus.Succeeded)
            .OrderByDescending(b => b.CreatedAt).Select(b => (DateTimeOffset?)(b.FinishedAt ?? b.CreatedAt)).FirstOrDefaultAsync(cancellationToken);
        checks.Add(SecurityReportRules.Backup(lastBackup, now));

        // ── رویدادهای امنیتی هفته ──
        var logs = context.SecurityLogs.Where(l => l.CreatedAt >= weekAgo);
        var stats = await logs.GroupBy(l => l.Type).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        int Stat(SecurityEventType type) => stats.FirstOrDefault(s => s.Key == type)?.Count ?? 0;
        var topIps = (await logs.Where(l => (l.Type == SecurityEventType.LoginFailed || l.Type == SecurityEventType.OtpFailed) && l.IpAddress != null)
                .GroupBy(l => l.IpAddress!).Select(g => new { Ip = g.Key, Count = g.Count() })
                .Where(x => x.Count >= 5).OrderByDescending(x => x.Count).Take(10).ToListAsync(cancellationToken))
            .Select(x => (x.Ip, x.Count)).ToList();
        checks.Add(SecurityReportRules.BruteForce(topIps, store.IsIpAutoBlockEnabled));
        var denied = Stat(SecurityEventType.AdminAccessDenied);
        if (denied > 0)
            checks.Add(new SecurityCheck("admin-denied", "تلاش برای ورود غیرمجاز به پنل", denied >= 10 ? SecurityCheckLevel.Warning : SecurityCheckLevel.Info,
                $"{SecurityReportRules.Digits(denied)} بار کسی بدون مجوز صفحه‌ای از پنل را خواسته است.", "/admin/system/security?type=AdminAccessDenied"));

        // ── دسترس‌پذیری ──
        var availability = await uptime.SummaryAsync(now, cancellationToken);
        if (availability.Week < 99)
            checks.Add(new SecurityCheck("uptime", "دسترس‌پذیری هفته", SecurityCheckLevel.Warning,
                $"سایت در ۷ روز اخیر {availability.Week.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}٪ در دسترس بوده است (کمتر از ۹۹٪).", "/admin/system/uptime"));

        var activity = new SecurityActivity(
            Stat(SecurityEventType.LoginFailed) + Stat(SecurityEventType.OtpFailed),
            Stat(SecurityEventType.LockedOut),
            denied,
            await context.UserKnownDevices.CountAsync(d => d.CreatedAt >= weekAgo && staffIds.Contains(d.UserId), cancellationToken),
            Stat(SecurityEventType.RolesChanged),
            await context.IpRules.CountAsync(r => r.Action == IpRuleAction.Block && (r.ExpiresAt == null || r.ExpiresAt > now), cancellationToken),
            topIps);

        var ordered = checks.OrderByDescending(c => c.Level).ToList();
        var score = SecurityReportRules.Score(ordered);
        var report = new SecurityReport(now, score, SecurityReportRules.Grade(score), ordered, activity);
        cache.Set(CacheKey, report, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10), Size = 1 });
        return report;
    }

    /// <inheritdoc />
    public async Task<int> SendWeeklyAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (now - _lastWeeklyCheck < TimeSpan.FromHours(1))
            return 0;
        _lastWeeklyCheck = now;
        var hour = IranClock.ToLocal(now).Hour;
        if (hour is < 9 or >= 20)
            return 0;
        var since = now.AddDays(-7);
        if (await context.StaffNotifications.AnyAsync(n => n.Title.StartsWith(SecurityReportRules.WeeklyTitlePrefix) && n.CreatedAt >= since, cancellationToken))
            return 0;
        var report = await BuildAsync(now, refresh: true, cancellationToken);
        var day = PersianDate.Format(now)[..10];
        return await notifications.NotifyAsync(new StaffAlert(StaffNotificationKind.Alert, $"{SecurityReportRules.WeeklyTitlePrefix} ({day})",
            SecurityReportRules.WeeklyBody(report), "/admin/system/security-report", [AdminPermissions.AuditView, AdminPermissions.MonitoringView]));
    }

    private static bool SafeVerify(string password, string hash)
    {
        try
        {
            return PasswordHasher.Verify(password, hash);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return false;
        }
    }

    private static string Name(string? first, string? last, string phone)
    {
        var name = $"{first} {last}".Trim();
        return name.Length > 0 ? name : phone.Length >= 4 ? "…" + phone[^4..] : phone;
    }
}
