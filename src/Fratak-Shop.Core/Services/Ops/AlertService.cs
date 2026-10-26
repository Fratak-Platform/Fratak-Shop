using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Insights;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Ops;
using Fratak_Shop.Data.Entities.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Ops;

// ═══════════════════════════════════════════════════════════════════════════
//  قانون‌های هشدار (مرحلهٔ هفتم – فاز ۹)
//  هر ۵ دقیقه (OpsWorker) همهٔ قانون‌های فعال سنجیده می‌شوند. اگر مقدار از آستانه بگذرد و از آخرین هشدار همین قانون
//  به اندازهٔ «فاصلهٔ تکرار» گذشته باشد، به دارندگان مجوز «عملیات فنی» اعلان پنل + پوش می‌رود (و اگر خواسته شده،
//  ایمیل/بله/تلگرام از طریق کانال‌های «گزارش خودکار»). وقتی وضعیت عادی شد، یک اعلان «برطرف شد» هم می‌رود.
//  اندازه‌گیری شاخص‌هایی که به لایهٔ Web نیاز دارند (درخواست‌ها، دیسک، کارهای پس‌زمینه) با IOpsMetricsSource انجام می‌شود.
//  پنل: /admin/system/alerts
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// اندازه‌گیری یک شاخص. پیاده‌سازی: Web/Infrastructure/Ops/OpsMetricsSource.cs. null = قابل اندازه‌گیری نیست (قانون رد می‌شود).
/// </summary>
public interface IOpsMetricsSource
{
    Task<double?> MeasureAsync(AlertMetric metric, CancellationToken cancellationToken);
}

/// <summary>
/// تعریف ثابت هر شاخص: عنوان، واحد، جهت (بیشتر بد است یا کمتر)، آستانهٔ پیش‌فرض و پیشنهادی.
/// مثال: ErrorsPerHour → «خطاها در یک ساعت»، واحد «خطا»، بیشتر = بد، پیش‌فرض ۲۰.
/// </summary>
public sealed record AlertMetricInfo(AlertMetric Metric, string Title, string Unit, bool LowerIsWorse, double DefaultThreshold, bool DefaultEnabled, string Help);

/// <summary>منطق خالص هشدار (بدون دیتابیس) برای تست‌پذیری.</summary>
public static class AlertRules
{
    public static readonly IReadOnlyList<AlertMetricInfo> Metrics =
    [
        new(AlertMetric.ErrorsPerHour, "خطاهای یک ساعت اخیر", "خطا", false, 20, true, "تعداد خطاهای ثبت‌شده در «خطاها». افزایش ناگهانی یعنی بخشی از سایت خراب شده است."),
        new(AlertMetric.ServerErrorPercent, "درصد خطای ۵۰۰ (۵ دقیقه)", "٪", false, 5, true, "درصد درخواست‌هایی که با خطای سرور تمام شده‌اند (فقط وقتی بیش از ۲۰ درخواست باشد)."),
        new(AlertMetric.DiskFreePercent, "فضای آزاد دیسک", "٪", true, 10, true, "وقتی فضای آزاد کمتر از این درصد شود. پر شدن دیسک پایگاه داده و آپلود را متوقف می‌کند."),
        new(AlertMetric.DatabaseLatencyMs, "زمان پاسخ پایگاه داده", "میلی‌ثانیه", false, 1000, true, "زمان یک اتصال ساده به SQL Server. عدد بالا یعنی سرور پایگاه داده زیر فشار است."),
        new(AlertMetric.FailingJobs, "کارهای پس‌زمینهٔ ناموفق یا گیرکرده", "کار", false, 1, true, "کارهایی که آخرین اجرایشان خطا داده یا مدت زیادی اجرا نشده‌اند."),
        new(AlertMetric.HoursSinceBackup, "ساعت از آخرین پشتیبان موفق", "ساعت", false, 36, true, "اگر پشتیبان خودکار روشن است و این مدت پشتیبان موفقی گرفته نشده باشد."),
        new(AlertMetric.StuckPayments, "پرداخت‌های گیرکرده", "تراکنش", false, 1, true, "تراکنش‌هایی که بیش از ۳۰ دقیقه در مرحلهٔ «در حال تأیید» با درگاه گیر کرده‌اند (ممکن است پول کسر شده ولی سفارش ثبت نشده باشد)."),
        new(AlertMetric.MemoryMb, "مصرف حافظهٔ برنامه", "مگابایت", false, 1500, false, "حافظهٔ فرایند سایت. افزایش مداوم می‌تواند نشانهٔ نشت حافظه باشد."),
        new(AlertMetric.FailedLoginsPerHour, "ورودهای ناموفق در یک ساعت", "تلاش", false, 50, true, "نشانهٔ حملهٔ حدس رمز؛ «مسدودسازی خودکار IP» را هم در تنظیمات ببینید."),
        new(AlertMetric.AverageResponseMs, "میانگین زمان پاسخ صفحه‌ها (۵ دقیقه)", "میلی‌ثانیه", false, 1500, false, "کند شدن سایت برای بازدیدکنندگان."),
    ];

    public static AlertMetricInfo Info(AlertMetric metric) => Metrics.First(m => m.Metric == metric);

    /// <summary>
    /// از آستانه گذشته؟ مثال: (ErrorsPerHour، ۲۵، ۲۰) → true؛ (DiskFreePercent، ۸، ۱۰) → true چون کمتر بد است.
    /// </summary>
    public static bool IsBreached(AlertMetric metric, double value, double threshold) =>
        Info(metric).LowerIsWorse ? value < threshold : value >= threshold;

    /// <summary>
    /// تصمیم قانون: (Fire = هشدار بده، Resolve = اعلان برطرف شدن بده). هشدار تکراری تا پایان cooldown داده نمی‌شود.
    /// مثال: در حال هشدار، ۳۰ دقیقه از هشدار قبلی گذشته، cooldown=60 → Fire=false.
    /// </summary>
    public static (bool Fire, bool Resolve) Decide(bool breached, bool wasFiring, DateTimeOffset? lastTriggered, int cooldownMinutes, DateTimeOffset now)
    {
        if (!breached)
            return (false, wasFiring);
        var coolingDown = lastTriggered is { } last && now - last < TimeSpan.FromMinutes(cooldownMinutes);
        return (!coolingDown, false);
    }

    /// <summary>متن هشدار. مثال: «خطاهای یک ساعت اخیر: ۲۷ خطا (آستانه: ۲۰)».</summary>
    public static string Describe(AlertMetric metric, double value, double threshold)
    {
        var info = Info(metric);
        return $"{info.Title}: {Number(value)} {info.Unit} ({(info.LowerIsWorse ? "حداقل مجاز" : "آستانه")}: {Number(threshold)})";
    }

    public static string Number(double value) =>
        PersianNumbers.Digits((Math.Abs(value) >= 100 ? value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)).Replace(',', '٬').Replace('.', '٫'));

    /// <summary>آستانهٔ معتبر برای هر شاخص (درصدها ۰ تا ۱۰۰، بقیه مثبت).</summary>
    public static bool IsValidThreshold(AlertMetric metric, double threshold) => metric switch
    {
        AlertMetric.ServerErrorPercent or AlertMetric.DiskFreePercent => threshold is > 0 and <= 100,
        _ => threshold > 0 && threshold <= 10_000_000,
    };
}

/// <summary>یک ردیف صفحهٔ هشدارها.</summary>
public sealed record AlertRuleRow(AlertRule Rule, AlertMetricInfo Info);

/// <summary>ورودی ویرایش یک قانون.</summary>
public sealed record AlertRuleInput(Guid Id, bool Enabled, double Threshold, int CooldownMinutes, bool SendExternal);

/// <summary>گیرندگان بیرونی هشدار (ایمیل/بله/تلگرام) که روی تنظیمات ارسال «گزارش خودکار» ذخیره می‌شوند.</summary>
public sealed record AlertRecipients(string? EmailTo, string? BaleChatId, string? TelegramChatId);

/// <summary>سرویس هشدارها. محل استفاده: OpsWorker و /admin/system/alerts.</summary>
public interface IAlertService
{
    Task EnsureDefaultsAsync(CancellationToken cancellationToken = default);
    Task<List<AlertRuleRow>> ListAsync(CancellationToken cancellationToken = default);
    Task<List<AlertEvent>> RecentEventsAsync(int take, CancellationToken cancellationToken = default);
    Task<OperationResult> SaveAsync(IReadOnlyList<AlertRuleInput> inputs, CancellationToken cancellationToken = default);
    Task<AlertRecipients> GetRecipientsAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> SaveRecipientsAsync(AlertRecipients recipients, CancellationToken cancellationToken = default);

    /// <summary>سنجش همهٔ قانون‌های فعال. خروجی: تعداد هشدار/برطرف‌شدن ارسال‌شده.</summary>
    Task<int> EvaluateAsync(IOpsMetricsSource metrics, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>ارسال آزمایشی به همهٔ کانال‌ها (پنل + بیرونی) برای بررسی تنظیمات.</summary>
    Task<OperationResult> SendTestAsync(CancellationToken cancellationToken = default);
}

public sealed class AlertService(
    FratakShopDbContext context,
    IStaffNotificationService notifications,
    IScheduledReportService reports,
    ILogger<AlertService> logger) : IAlertService
{
    public async Task EnsureDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var existing = (await context.AlertRules.IgnoreQueryFilters().Select(r => r.Metric).ToListAsync(cancellationToken)).ToHashSet();
        var added = false;
        foreach (var info in AlertRules.Metrics.Where(m => !existing.Contains(m.Metric)))
        {
            context.AlertRules.Add(new AlertRule { Metric = info.Metric, Threshold = info.DefaultThreshold, IsEnabled = info.DefaultEnabled, CooldownMinutes = 60 });
            added = true;
        }
        if (!added)
            return;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
        }
    }

    public async Task<List<AlertRuleRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rules = await context.AlertRules.AsNoTracking().ToListAsync(cancellationToken);
        return rules.Where(r => AlertRules.Metrics.Any(m => m.Metric == r.Metric))
            .Select(r => new AlertRuleRow(r, AlertRules.Info(r.Metric)))
            .OrderBy(r => (int)r.Rule.Metric).ToList();
    }

    public Task<List<AlertEvent>> RecentEventsAsync(int take, CancellationToken cancellationToken = default) =>
        context.AlertEvents.AsNoTracking().OrderByDescending(e => e.CreatedAt).Take(Math.Clamp(take, 1, 200)).ToListAsync(cancellationToken);

    public async Task<OperationResult> SaveAsync(IReadOnlyList<AlertRuleInput> inputs, CancellationToken cancellationToken = default)
    {
        var ids = inputs.Select(i => i.Id).ToList();
        var rules = await context.AlertRules.AsTracking().Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken);
        foreach (var input in inputs)
        {
            var rule = rules.FirstOrDefault(r => r.Id == input.Id);
            if (rule is null)
                continue;
            var info = AlertRules.Info(rule.Metric);
            if (!AlertRules.IsValidThreshold(rule.Metric, input.Threshold))
                return OperationResult.Error($"آستانهٔ «{info.Title}» معتبر نیست.");
            if (input.CooldownMinutes is < 5 or > 10080)
                return OperationResult.Error($"فاصلهٔ تکرار «{info.Title}» باید بین ۵ دقیقه تا ۷ روز باشد.");
            rule.IsEnabled = input.Enabled;
            rule.Threshold = input.Threshold;
            rule.CooldownMinutes = input.CooldownMinutes;
            rule.SendExternal = input.SendExternal;
            if (!input.Enabled)
                rule.IsFiring = false;
        }
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success("قانون‌های هشدار ذخیره شد.");
    }

    public async Task<AlertRecipients> GetRecipientsAsync(CancellationToken cancellationToken = default)
    {
        var config = await context.ReportDeliveryConfigs.AsNoTracking().OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        return new AlertRecipients(config?.AlertEmailTo, config?.AlertBaleChatId, config?.AlertTelegramChatId);
    }

    public async Task<OperationResult> SaveRecipientsAsync(AlertRecipients recipients, CancellationToken cancellationToken = default)
    {
        var emails = ScheduledReportRules.ParseEmails(recipients.EmailTo);
        if (emails.Invalid.Count > 0)
            return OperationResult.Error("ایمیل نامعتبر: " + string.Join("، ", emails.Invalid.Take(3)));
        if (emails.Valid.Count > 10)
            return OperationResult.Error("حداکثر ۱۰ ایمیل.");
        var bale = string.IsNullOrWhiteSpace(recipients.BaleChatId) ? null : recipients.BaleChatId.Trim();
        var telegram = string.IsNullOrWhiteSpace(recipients.TelegramChatId) ? null : recipients.TelegramChatId.Trim();
        if ((bale is not null && !ScheduledReportRules.IsValidChatId(bale)) || (telegram is not null && !ScheduledReportRules.IsValidChatId(telegram)))
            return OperationResult.Error("شناسهٔ گفت‌وگو معتبر نیست (عدد یا @نام).");

        var config = await context.ReportDeliveryConfigs.AsTracking().OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (config is null)
        {
            config = new global::Fratak_Shop.Data.Entities.Insights.ReportDeliveryConfig();
            context.ReportDeliveryConfigs.Add(config);
        }
        config.AlertEmailTo = emails.Valid.Count == 0 ? null : string.Join(", ", emails.Valid);
        config.AlertBaleChatId = bale;
        config.AlertTelegramChatId = telegram;
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success("گیرندگان هشدار ذخیره شد. توکن ربات‌ها و SMTP همان تنظیمات «گزارش خودکار» است.");
    }

    public async Task<int> EvaluateAsync(IOpsMetricsSource metrics, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var rules = await context.AlertRules.AsTracking().Where(r => r.IsEnabled).ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var rule in rules)
        {
            if (!AlertRules.Metrics.Any(m => m.Metric == rule.Metric))
                continue;
            double? value;
            try
            {
                value = await metrics.MeasureAsync(rule.Metric, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Measuring alert metric {Metric} failed", rule.Metric);
                continue;
            }
            rule.LastCheckedAt = now;
            if (value is null)
                continue;
            rule.LastValue = Math.Round(value.Value, 2);

            var breached = AlertRules.IsBreached(rule.Metric, value.Value, rule.Threshold);
            var (fire, resolve) = AlertRules.Decide(breached, rule.IsFiring, rule.LastTriggeredAt, rule.CooldownMinutes, now);
            var text = AlertRules.Describe(rule.Metric, value.Value, rule.Threshold);
            if (fire)
            {
                rule.IsFiring = true;
                rule.LastTriggeredAt = now;
                context.AlertEvents.Add(new AlertEvent { RuleId = rule.Id, Metric = rule.Metric, Value = value.Value, Threshold = rule.Threshold, Message = text });
                await SendAsync("⚠️ هشدار فنی: " + AlertRules.Info(rule.Metric).Title, text + "\n" + AlertRules.Info(rule.Metric).Help, rule.SendExternal, cancellationToken);
                sent++;
            }
            else if (resolve)
            {
                rule.IsFiring = false;
                context.AlertEvents.Add(new AlertEvent { RuleId = rule.Id, Metric = rule.Metric, Value = value.Value, Threshold = rule.Threshold, IsResolved = true, Message = "برطرف شد – " + text });
                await SendAsync("✅ برطرف شد: " + AlertRules.Info(rule.Metric).Title, text, rule.SendExternal, cancellationToken);
                sent++;
            }
            else if (breached)
            {
                rule.IsFiring = true;
            }
        }
        await context.SaveChangesAsync(cancellationToken);
        // تاریخچهٔ بیش از ۹۰ روز لازم نیست.
        await context.AlertEvents.Where(e => e.CreatedAt < now.AddDays(-90)).ExecuteDeleteAsync(cancellationToken);
        return sent;
    }

    public async Task<OperationResult> SendTestAsync(CancellationToken cancellationToken = default)
    {
        var external = await SendAsync("🔔 آزمایش هشدار فنی", "این یک پیام آزمایشی از صفحهٔ «هشدارها» است. اگر آن را می‌بینید، کانال درست تنظیم شده است.", true, cancellationToken);
        return OperationResult.Success("اعلان آزمایشی در پنل ارسال شد." + (external is null ? "" : " " + external));
    }

    /// <summary>اعلان پنل + پوش برای کارکنان فنی، و در صورت درخواست ارسال بیرونی. خروجی: خلاصهٔ ارسال بیرونی (یا null).</summary>
    private async Task<string?> SendAsync(string title, string body, bool external, CancellationToken cancellationToken)
    {
        await notifications.NotifyAsync(new StaffAlert(StaffNotificationKind.Alert, title, body, "/admin/system/alerts",
            [AdminPermissions.SystemManage, AdminPermissions.MonitoringView]));
        if (!external)
            return null;
        try
        {
            var recipients = await GetRecipientsAsync(cancellationToken);
            return await reports.SendAlertAsync(title, body, recipients.EmailTo, recipients.BaleChatId, recipients.TelegramChatId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Sending external alert failed");
            return "ارسال بیرونی ناموفق بود.";
        }
    }
}

/// <summary>
/// شاخص‌هایی که فقط به دیتابیس نیاز دارند؛ OpsMetricsSource لایهٔ Web برای این‌ها همین کلاس را صدا می‌زند.
/// </summary>
public static class OpsDbMetrics
{
    public static async Task<double?> MeasureAsync(FratakShopDbContext context, AlertMetric metric, bool backupEnabled, DateTimeOffset now, CancellationToken cancellationToken)
    {
        switch (metric)
        {
            case AlertMetric.ErrorsPerHour:
            {
                var since = now.AddHours(-1);
                return await context.ErrorLogs.CountAsync(e => e.CreatedAt >= since, cancellationToken);
            }
            case AlertMetric.FailedLoginsPerHour:
            {
                var since = now.AddHours(-1);
                return await context.SecurityLogs.CountAsync(l => (l.Type == SecurityEventType.LoginFailed || l.Type == SecurityEventType.OtpFailed) && l.CreatedAt >= since, cancellationToken);
            }
            case AlertMetric.StuckPayments:
            {
                // «در انتظار» (Pending) معمولاً یعنی مشتری درگاه را رها کرده و طبیعی است؛ گیرکرده یعنی «در حال تأیید» ماندهٔ بیش از ۳۰ دقیقه.
                var older = now.AddMinutes(-30);
                var newer = now.AddDays(-3);
                return await context.PaymentTransactions.CountAsync(p =>
                    p.Status == PaymentTransactionStatus.Verifying && p.UpdatedAt < older && p.UpdatedAt > newer, cancellationToken);
            }
            case AlertMetric.HoursSinceBackup:
            {
                if (!backupEnabled)
                    return null;
                var last = await context.BackupRecords.AsNoTracking()
                    .Where(b => b.Kind == BackupKind.Database && b.Status == BackupStatus.Succeeded)
                    .MaxAsync(b => (DateTimeOffset?)b.CreatedAt, cancellationToken);
                if (last is null)
                {
                    // هنوز هیچ پشتیبانی نیست: از زمان اولین تلاش (یا اگر تلاشی نبوده، هشدار نده تا اولین نوبت برسد).
                    var first = await context.BackupRecords.AsNoTracking().MinAsync(b => (DateTimeOffset?)b.CreatedAt, cancellationToken);
                    return first is null ? null : (now - first.Value).TotalHours;
                }
                return (now - last.Value).TotalHours;
            }
            default:
                return null;
        }
    }
}
