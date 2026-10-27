using System.Net;
using Fratak_Shop.Core.Services.Ops;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Ops;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست بخش فنی روی SQL Server واقعی (مرحلهٔ هفتم – فاز ۹):
/// • پشتیبان‌گیری واقعی با BACKUP DATABASE + RESTORE VERIFYONLY، نوبت زمان‌بندی‌شده فقط یک بار، حذف نسخه.
/// • فایروال: قانون IP/بازه، جلوگیری از بستن IP خود کارمند و بازهٔ خیلی بزرگ، اولویت «مجاز»، ربات‌های پیش‌فرض (فقط یک بار)،
///   مسدودسازی خودکار از روی ورودهای ناموفق (و فعال شدن دوبارهٔ مسدودی منقضی‌شده) و ذخیرهٔ شمارندهٔ درخواست‌های ردشده.
/// • توقف کارهای پس‌زمینه در دیتابیس (ماندگار بعد از ری‌استارت).
/// • هشدارها: ارسال، عدم تکرار در فاصلهٔ تکرار، «برطرف شد» و اعتبارسنجی ذخیره.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class OpsIntegrationTests(SqlServerFixture db)
{
    private static FratakShopDbContext Ctx(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();

    // ═══ پشتیبان‌گیری ═══════════════════════════════════════════════════

    [SqlFact]
    public async Task Manual_backup_creates_a_verified_database_backup()
    {
        await using var scope = db.Scope();
        var backups = scope.ServiceProvider.GetRequiredService<IBackupService>();

        var result = await backups.RunNowAsync("کارمند تست", includeUploads: false);

        Assert.True(result.Ran);
        Assert.True(result.Ok, result.Message);
        var record = Assert.Single(result.Records);
        Assert.Equal(BackupKind.Database, record.Kind);
        Assert.Equal(BackupStatus.Succeeded, record.Status);
        Assert.True(record.IsVerified);
        Assert.True(record.IsManual);
        Assert.Equal("کارمند تست", record.StartedBy);
        Assert.NotNull(record.FilePath);
        Assert.EndsWith(".bak", record.FilePath);
        Assert.True(record.SizeBytes > 0);

        var overview = await backups.GetOverviewAsync();
        Assert.NotNull(overview.LastSuccess);
        Assert.NotNull(overview.NextRunAt);

        // حذف: ردیف «حذف‌شده» می‌شود (فایل روی سرور SQL است و از سایت پاک نمی‌شود ← پیام راهنما).
        var deleted = await backups.DeleteAsync(record.Id);
        Assert.True(deleted.Status == EnOperationResultStatus.Success);
        Assert.Equal(BackupStatus.Removed, (await backups.GetAsync(record.Id))!.Status);
        Assert.False((await backups.DeleteAsync(Guid.NewGuid())).Status == EnOperationResultStatus.Success);
    }

    [SqlFact]
    public async Task Scheduled_backup_runs_once_per_slot()
    {
        await using var scope = db.Scope();
        var backups = scope.ServiceProvider.GetRequiredService<IBackupService>();
        var now = DateTimeOffset.UtcNow;
        var settings = await scope.ServiceProvider.GetRequiredService<IStoreSettingService>().GetAsync();
        Assert.True(settings.IsBackupEnabled);
        var (slot, _) = BackupSchedule.CurrentSlot(now, settings.BackupRunsPerDay, settings.BackupStartHour);

        var first = await backups.RunDueAsync(now);
        if (first.Ran)
            Assert.True(first.Ok, first.Message);
        Assert.True(await backups.HasSlotAsync(slot));

        // نوبت انجام شده ← دوباره اجرا نمی‌شود (چند سرور هم با ایندکس یکتای Kind+SlotKey فقط یکی را اجرا می‌کنند).
        var second = await backups.RunDueAsync(now.AddMinutes(1));
        Assert.False(second.Ran);
        Assert.True(second.Ok);
        Assert.Equal(1, await Ctx(scope).BackupRecords.CountAsync(b => b.Kind == BackupKind.Database && b.SlotKey == slot));
    }

    // ═══ فایروال ═════════════════════════════════════════════════════════

    [SqlFact]
    public async Task Ip_rules_are_validated_applied_and_removed()
    {
        await using var scope = db.Scope();
        var firewall = scope.ServiceProvider.GetRequiredService<IFirewallService>();
        var staffIp = IPAddress.Parse("198.51.100.7");

        var added = await firewall.AddIpRuleAsync(new IpRuleInput("203.0.113.77/24", IpRuleAction.Block, "اسکن", 24), "تست", staffIp);
        Assert.True(added.Status == EnOperationResultStatus.Success, added.Message);
        Assert.True(FirewallState.Current.Check(IPAddress.Parse("203.0.113.9"), null, DateTimeOffset.UtcNow).Blocked);

        // همان بازه با نوشتار دیگر ← همان قانون به‌روز می‌شود (نه ردیف تکراری).
        Assert.True((await firewall.AddIpRuleAsync(new IpRuleInput("203.0.113.0/24", IpRuleAction.Block, null, null), "تست", staffIp)).Status == EnOperationResultStatus.Success);
        var rule = Assert.Single((await firewall.ListIpRulesAsync()).Where(r => r.Value == "203.0.113.0/24"));
        Assert.Null(rule.ExpiresAt);

        // «مجاز» بر «مسدود» مقدم است.
        Assert.True((await firewall.AddIpRuleAsync(new IpRuleInput("203.0.113.5", IpRuleAction.Allow, "دفتر", null), "تست", staffIp)).Status == EnOperationResultStatus.Success);
        Assert.False(FirewallState.Current.Check(IPAddress.Parse("203.0.113.5"), null, DateTimeOffset.UtcNow).Blocked);

        // محافظ‌ها: IP خود کارمند، بازهٔ خیلی بزرگ، loopback، مقدار نامعتبر و مدت نامعتبر.
        Assert.False((await firewall.AddIpRuleAsync(new IpRuleInput("198.51.100.0/24", IpRuleAction.Block, null, null), "تست", staffIp)).Status == EnOperationResultStatus.Success);
        Assert.False((await firewall.AddIpRuleAsync(new IpRuleInput("0.0.0.0/0", IpRuleAction.Block, null, null), "تست", staffIp)).Status == EnOperationResultStatus.Success);
        Assert.False((await firewall.AddIpRuleAsync(new IpRuleInput("127.0.0.1", IpRuleAction.Block, null, null), "تست", staffIp)).Status == EnOperationResultStatus.Success);
        Assert.False((await firewall.AddIpRuleAsync(new IpRuleInput("not-an-ip", IpRuleAction.Block, null, null), "تست", staffIp)).Status == EnOperationResultStatus.Success);
        Assert.False((await firewall.AddIpRuleAsync(new IpRuleInput("192.0.2.1", IpRuleAction.Block, null, 0), "تست", staffIp)).Status == EnOperationResultStatus.Success);

        // شمارندهٔ درخواست‌های ردشده با نگهداری دوره‌ای در دیتابیس ذخیره می‌شود.
        FirewallState.CountHit(rule.Id);
        FirewallState.CountHit(rule.Id);
        await firewall.MaintainAsync(DateTimeOffset.UtcNow);
        var stored = await Ctx(scope).IpRules.AsNoTracking().SingleAsync(r => r.Id == rule.Id);
        Assert.True(stored.Hits >= 2);
        Assert.NotNull(stored.LastHitAt);

        foreach (var r in (await firewall.ListIpRulesAsync()).Where(r => r.Value.StartsWith("203.0.113.", StringComparison.Ordinal)))
            Assert.True((await firewall.DeleteIpRuleAsync(r.Id)).Status == EnOperationResultStatus.Success);
        Assert.False(FirewallState.Current.Check(IPAddress.Parse("203.0.113.9"), null, DateTimeOffset.UtcNow).Blocked);
        Assert.False((await firewall.DeleteIpRuleAsync(Guid.NewGuid())).Status == EnOperationResultStatus.Success);
    }

    [SqlFact]
    public async Task Default_bot_rules_are_seeded_once_and_protected_patterns_are_refused()
    {
        await using var scope = db.Scope();
        var firewall = scope.ServiceProvider.GetRequiredService<IFirewallService>();

        await firewall.EnsureDefaultsAsync();
        var bots = await firewall.ListBotRulesAsync();
        Assert.Contains(bots, b => b.Pattern == "sqlmap" && b.IsActive);
        Assert.DoesNotContain(bots, b => b.Pattern.StartsWith("__", StringComparison.Ordinal));

        // پیش‌فرضی که کارمند حذف کرده دوباره اضافه نمی‌شود.
        var nikto = bots.Single(b => b.Pattern == "nikto");
        Assert.True((await firewall.DeleteBotRuleAsync(nikto.Id)).Status == EnOperationResultStatus.Success);
        await firewall.EnsureDefaultsAsync();
        Assert.DoesNotContain(await firewall.ListBotRulesAsync(), b => b.Pattern == "nikto");

        Assert.False((await firewall.AddBotRuleAsync("Googlebot", null)).Status == EnOperationResultStatus.Success);
        Assert.False((await firewall.AddBotRuleAsync("ab", null)).Status == EnOperationResultStatus.Success);
        Assert.False((await firewall.AddBotRuleAsync("sqlmap", null)).Status == EnOperationResultStatus.Success);
        Assert.True((await firewall.AddBotRuleAsync("  BadScraper ", "مصرف زیاد")).Status == EnOperationResultStatus.Success);
        var scraper = Assert.Single((await firewall.ListBotRulesAsync()).Where(b => b.Pattern == "badscraper"));
        Assert.True(FirewallState.Current.Check(IPAddress.Parse("192.0.2.10"), "Mozilla/5.0 (compatible; BadScraper/1.0)", DateTimeOffset.UtcNow).Blocked);

        Assert.True((await firewall.SetBotRuleActiveAsync(scraper.Id, false)).Status == EnOperationResultStatus.Success);
        Assert.False(FirewallState.Current.Check(IPAddress.Parse("192.0.2.10"), "BadScraper/1.0", DateTimeOffset.UtcNow).Blocked);
        Assert.True((await firewall.DeleteBotRuleAsync(scraper.Id)).Status == EnOperationResultStatus.Success);
    }

    [SqlFact]
    public async Task Repeated_failed_logins_auto_block_the_ip_and_expired_blocks_are_reactivated()
    {
        await using var scope = db.Scope();
        var context = Ctx(scope);
        var firewall = scope.ServiceProvider.GetRequiredService<IFirewallService>();
        const string attacker = "192.0.2.77";
        const string office = "10.20.30.40";
        for (var i = 0; i < 6; i++)
        {
            context.SecurityLogs.Add(new SecurityLog { Type = SecurityEventType.LoginFailed, IpAddress = attacker, Identifier = "0912000" + i });
            context.SecurityLogs.Add(new SecurityLog { Type = SecurityEventType.OtpFailed, IpAddress = office, Identifier = "0912000" + i });
        }
        await context.SaveChangesAsync();

        Assert.True(await firewall.AutoBlockAsync(5, 24, DateTimeOffset.UtcNow) >= 1);
        var rule = await context.IpRules.AsNoTracking().SingleAsync(r => r.Value == attacker);
        Assert.True(rule.IsAutomatic);
        Assert.Equal(IpRuleAction.Block, rule.Action);
        Assert.True(rule.ExpiresAt > DateTimeOffset.UtcNow.AddHours(23));
        // IP شبکهٔ داخلی (پراکسی/دفتر) هرگز خودکار بسته نمی‌شود.
        Assert.False(await context.IpRules.AnyAsync(r => r.Value == office));

        var suspicious = await firewall.SuspiciousAsync(5);
        Assert.Contains(suspicious, s => s.Ip == attacker && s.IsBlocked && s.FailedLogins >= 6);

        // مسدودی فعال تکرار نمی‌شود.
        await firewall.AutoBlockAsync(5, 24, DateTimeOffset.UtcNow);
        Assert.Equal(1, await context.IpRules.CountAsync(r => r.Value == attacker));

        // مسدودی منقضی‌شده (هنوز پاک نشده) با ادامهٔ حمله دوباره فعال می‌شود (ایندکس یکتای Value).
        await context.IpRules.Where(r => r.Value == attacker).ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-5)));
        Assert.True(await firewall.AutoBlockAsync(5, 2, DateTimeOffset.UtcNow) >= 1);
        var renewed = await context.IpRules.AsNoTracking().SingleAsync(r => r.Value == attacker);
        Assert.Equal(rule.Id, renewed.Id);
        Assert.True(renewed.ExpiresAt > DateTimeOffset.UtcNow.AddHours(1));

        // «مجاز» دستی جلوی مسدودسازی خودکار را می‌گیرد.
        await context.IpRules.Where(r => r.Value == attacker).ExecuteDeleteAsync();
        Assert.True((await firewall.AddIpRuleAsync(new IpRuleInput(attacker, IpRuleAction.Allow, "تست", null), "تست", null)).Status == EnOperationResultStatus.Success);
        await firewall.AutoBlockAsync(5, 24, DateTimeOffset.UtcNow);
        Assert.Equal(IpRuleAction.Allow, (await context.IpRules.AsNoTracking().SingleAsync(r => r.Value == attacker)).Action);

        await context.IpRules.Where(r => r.Value == attacker).ExecuteDeleteAsync();
        await context.SecurityLogs.Where(l => l.IpAddress == attacker || l.IpAddress == office).ExecuteDeleteAsync();
        await firewall.ReloadAsync();
    }

    // ═══ کارهای پس‌زمینه ═════════════════════════════════════════════════

    [SqlFact]
    public async Task Job_pause_is_persisted()
    {
        await using var scope = db.Scope();
        var control = scope.ServiceProvider.GetRequiredService<IBackgroundJobControl>();
        var name = "کار تست " + Guid.NewGuid().ToString("N")[..8];

        Assert.True((await control.SetPausedAsync(name, true, "مدیر فنی")).Status == EnOperationResultStatus.Success);
        Assert.Contains(name, await control.GetPausedAsync());
        var row = Assert.Single((await control.ListAsync()).Where(j => j.JobName == name));
        Assert.True(row.IsPaused);
        Assert.Equal("مدیر فنی", row.PausedBy);
        Assert.NotNull(row.PausedAt);

        Assert.True((await control.SetPausedAsync(name, false, "مدیر فنی")).Status == EnOperationResultStatus.Success);
        Assert.DoesNotContain(name, await control.GetPausedAsync());
        row = Assert.Single((await control.ListAsync()).Where(j => j.JobName == name));
        Assert.False(row.IsPaused);
        Assert.Null(row.PausedAt);
        Assert.Null(row.PausedBy);

        Assert.False((await control.SetPausedAsync("   ", true, "x")).Status == EnOperationResultStatus.Success);
    }

    // ═══ هشدارها ═════════════════════════════════════════════════════════

    /// <summary>شاخص‌های ساختگی: فقط خطاها و دیسک مقدار دارند؛ بقیه «نامعلوم» (بدون تصمیم).</summary>
    private sealed class FakeMetrics(double errors, double diskFree) : IOpsMetricsSource
    {
        public Task<double?> MeasureAsync(AlertMetric metric, CancellationToken cancellationToken) => Task.FromResult<double?>(metric switch
        {
            AlertMetric.ErrorsPerHour => errors,
            AlertMetric.DiskFreePercent => diskFree,
            _ => null,
        });
    }

    [SqlFact]
    public async Task Alert_fires_once_per_cooldown_and_resolves()
    {
        await using var scope = db.Scope();
        var context = Ctx(scope);
        var alerts = scope.ServiceProvider.GetRequiredService<IAlertService>();

        await alerts.EnsureDefaultsAsync();
        await alerts.EnsureDefaultsAsync();
        var rows = await alerts.ListAsync();
        Assert.Equal(Enum.GetValues<AlertMetric>().Length, rows.Count);
        var errorsRule = rows.Single(r => r.Rule.Metric == AlertMetric.ErrorsPerHour).Rule;

        // آستانهٔ نامعتبر رد می‌شود؛ مقدار معتبر ذخیره می‌شود.
        Assert.False((await alerts.SaveAsync([new AlertRuleInput(errorsRule.Id, true, -3, 60, false)])).Status == EnOperationResultStatus.Success);
        Assert.False((await alerts.SaveAsync([new AlertRuleInput(errorsRule.Id, true, 20, 1, false)])).Status == EnOperationResultStatus.Success);
        Assert.True((await alerts.SaveAsync([new AlertRuleInput(errorsRule.Id, true, 20, 60, false)])).Status == EnOperationResultStatus.Success);

        var now = DateTimeOffset.UtcNow;
        Assert.True(await alerts.EvaluateAsync(new FakeMetrics(100, 50), now) >= 1);
        var rule = await context.AlertRules.AsNoTracking().SingleAsync(r => r.Id == errorsRule.Id);
        Assert.True(rule.IsFiring);
        Assert.Equal(100d, rule.LastValue);
        Assert.Equal(1, await context.AlertEvents.CountAsync(e => e.RuleId == rule.Id && !e.IsResolved));

        // ده دقیقه بعد هنوز مشکل هست ولی در فاصلهٔ تکرار است ← پیام تکراری نه.
        await alerts.EvaluateAsync(new FakeMetrics(120, 50), now.AddMinutes(10));
        Assert.Equal(1, await context.AlertEvents.CountAsync(e => e.RuleId == rule.Id && !e.IsResolved));

        // عادی شد ← «برطرف شد».
        await alerts.EvaluateAsync(new FakeMetrics(2, 50), now.AddMinutes(20));
        rule = await context.AlertRules.AsNoTracking().SingleAsync(r => r.Id == errorsRule.Id);
        Assert.False(rule.IsFiring);
        Assert.Equal(1, await context.AlertEvents.CountAsync(e => e.RuleId == rule.Id && e.IsResolved));
        // دیسک ۵۰٪ آزاد (آستانه ۱۰٪ «کمتر بد است») هرگز هشدار نداد.
        var diskRule = rows.Single(r => r.Rule.Metric == AlertMetric.DiskFreePercent).Rule;
        Assert.False(await context.AlertEvents.AnyAsync(e => e.RuleId == diskRule.Id));

        var recent = await alerts.RecentEventsAsync(10);
        Assert.Contains(recent, e => e.RuleId == rule.Id && e.IsResolved && e.Message.StartsWith("برطرف شد", StringComparison.Ordinal));
    }

    [SqlFact]
    public async Task Alert_recipients_are_validated_and_saved()
    {
        await using var scope = db.Scope();
        var alerts = scope.ServiceProvider.GetRequiredService<IAlertService>();

        Assert.False((await alerts.SaveRecipientsAsync(new AlertRecipients("not-an-email", null, null))).Status == EnOperationResultStatus.Success);
        Assert.False((await alerts.SaveRecipientsAsync(new AlertRecipients(null, "bad chat id!", null))).Status == EnOperationResultStatus.Success);
        Assert.True((await alerts.SaveRecipientsAsync(new AlertRecipients("tech@example.com, ops@example.com", "123456789", " @fratak_ops "))).Status == EnOperationResultStatus.Success);

        var saved = await alerts.GetRecipientsAsync();
        Assert.Equal("tech@example.com, ops@example.com", saved.EmailTo);
        Assert.Equal("123456789", saved.BaleChatId);
        Assert.Equal("@fratak_ops", saved.TelegramChatId);

        Assert.True((await alerts.SaveRecipientsAsync(new AlertRecipients(null, null, null))).Status == EnOperationResultStatus.Success);
        Assert.Null((await alerts.GetRecipientsAsync()).EmailTo);
    }
}
