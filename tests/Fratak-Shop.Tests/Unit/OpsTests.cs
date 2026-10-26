using System.Net;
using Fratak_Shop.Core.Services.Ops;
using Fratak_Shop.Data.Entities.Ops;
using Fratak_Shop.Web.Areas.Admin.Pages.SystemPages;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Ops;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های بخش فنی (مرحلهٔ هفتم – فاز ۹) بدون دیتابیس:
/// • فایروال: تجزیهٔ IP/بازهٔ CIDR (IPv4، IPv6، IPv4 داخل IPv6)، بازهٔ خیلی بزرگ، اولویت «مجاز» بر «مسدود»، انقضا و ربات‌ها.
/// • زمان‌بندی پشتیبان: نوبت فعلی/بعدی به وقت ایران، نام فایل و اعتبار مسیر پوشه.
/// • هشدارها: شکستن آستانه (از جمله «کمتر بد است» برای دیسک)، تصمیم ارسال/برطرف شدن با فاصلهٔ تکرار، اعتبار آستانه.
/// • کارهای پس‌زمینه: توقف، رد شدن نوبت، کارهای غیرقابل توقف و شمارش کارهای ناسالم.
/// • گزارش‌های سرور: حافظهٔ حلقوی، نام فایل روزانه (جلوگیری از خواندن فایل دیگر) و فیلتر پیام‌های فریم‌ورک.
/// • حالت تعمیرات و /healthz: مسیرهای مستثنا، IPهای مجاز و مقایسهٔ توکن.
/// </summary>
public class OpsTests
{
    private static IPAddress Ip(string value) => IPAddress.Parse(value);

    private static IpRange Range(string value)
    {
        Assert.True(IpRange.TryParse(value, out var range), value);
        return range;
    }

    // ═══ IpRange / IpList ═══════════════════════════════════════════════

    [Fact]
    public void Single_ipv4_matches_only_itself()
    {
        var range = Range(" 1.2.3.4 ");
        Assert.Equal("1.2.3.4", range.Text);
        Assert.True(range.IsSingle);
        Assert.True(range.Contains(Ip("1.2.3.4")));
        Assert.False(range.Contains(Ip("1.2.3.5")));
        Assert.False(range.Contains(null));
    }

    [Fact]
    public void Cidr_is_canonicalized_and_matches_its_block()
    {
        // بیت‌های میزبان صفر می‌شوند: «1.2.3.77/24» همان «1.2.3.0/24» است (ایندکس یکتا تکراری را می‌گیرد).
        var range = Range("1.2.3.77/24");
        Assert.Equal("1.2.3.0/24", range.Text);
        Assert.False(range.IsSingle);
        Assert.True(range.Contains(Ip("1.2.3.0")));
        Assert.True(range.Contains(Ip("1.2.3.255")));
        Assert.False(range.Contains(Ip("1.2.4.1")));
    }

    [Theory]
    [InlineData("10.20.31.255", true)]
    [InlineData("10.20.16.0", true)]
    [InlineData("10.20.32.0", false)]
    [InlineData("10.20.15.255", false)]
    public void Partial_byte_prefix_is_respected(string address, bool expected)
    {
        Assert.Equal(expected, Range("10.20.16.0/20").Contains(Ip(address)));
    }

    [Fact]
    public void Ipv4_mapped_ipv6_is_treated_as_ipv4()
    {
        // Kestrel گاهی IP را به شکل ::ffff:5.6.7.8 می‌دهد؛ باید با قانون IPv4 یکی حساب شود.
        Assert.Equal("5.6.7.8", Range("::ffff:5.6.7.8").Text);
        Assert.True(Range("5.6.7.0/24").Contains(Ip("::ffff:5.6.7.9")));
    }

    [Fact]
    public void Ipv6_ranges_work_and_never_match_ipv4()
    {
        var range = Range("2001:db8::/32");
        Assert.True(range.Contains(Ip("2001:db8:1::5")));
        Assert.False(range.Contains(Ip("2001:db9::1")));
        Assert.False(range.Contains(Ip("1.2.3.4")));
        Assert.False(Range("1.2.3.0/24").Contains(Ip("2001:db8::1")));
    }

    [Theory]
    [InlineData("1.2.3.4/0", true)]
    [InlineData("10.0.0.0/7", true)]
    [InlineData("10.0.0.0/8", false)]
    [InlineData("2001::/15", true)]
    [InlineData("2001::/16", false)]
    [InlineData("1.2.3.4", false)]
    public void Too_wide_ranges_are_detected(string value, bool tooWide)
    {
        Assert.Equal(tooWide, Range(value).IsTooWide);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("300.1.1.1")]
    [InlineData("1.2.3.4/33")]
    [InlineData("1.2.3.4/-1")]
    [InlineData("1.2.3.4/x")]
    [InlineData("2001:db8::/129")]
    public void Invalid_values_are_rejected(string? value)
    {
        Assert.False(IpRange.TryParse(value, out _));
    }

    [Fact]
    public void Ip_list_accepts_mixed_separators_and_reports_invalid_parts()
    {
        var (valid, invalid) = IpList.Parse("1.2.3.4, 5.6.7.0/24\nbad،9.9.9.9; 2001:db8::1");
        Assert.Equal(4, valid.Count);
        Assert.Equal(new[] { "bad" }, invalid);
        Assert.True(IpList.Contains(valid, Ip("5.6.7.200")));
        Assert.False(IpList.Contains(valid, Ip("8.8.8.8")));
        Assert.Empty(IpList.Parse(null).Valid);
    }

    // ═══ FirewallSnapshot ═══════════════════════════════════════════════

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Allow_rule_wins_over_block_rule()
    {
        var blockId = Guid.NewGuid();
        var snapshot = new FirewallSnapshot(
            [
                new FirewallIpEntry(blockId, Range("5.6.7.0/24"), IpRuleAction.Block, null),
                new FirewallIpEntry(Guid.NewGuid(), Range("5.6.7.8"), IpRuleAction.Allow, null),
            ], [], Now);

        Assert.False(snapshot.Check(Ip("5.6.7.8"), null, Now).Blocked);
        var decision = snapshot.Check(Ip("5.6.7.9"), null, Now);
        Assert.True(decision.Blocked);
        Assert.Equal(blockId, decision.RuleId);
        Assert.Equal("ip", decision.Reason);
    }

    [Fact]
    public void Expired_rules_are_ignored()
    {
        var snapshot = new FirewallSnapshot(
            [
                new FirewallIpEntry(Guid.NewGuid(), Range("5.6.7.8"), IpRuleAction.Block, Now.AddMinutes(-1)),
                new FirewallIpEntry(Guid.NewGuid(), Range("9.9.9.9"), IpRuleAction.Block, Now.AddMinutes(1)),
            ], [], Now);

        Assert.False(snapshot.Check(Ip("5.6.7.8"), null, Now).Blocked);
        Assert.True(snapshot.Check(Ip("9.9.9.9"), null, Now).Blocked);
        // همان قانون بعد از رسیدن زمان انقضا دیگر عمل نمی‌کند (حتی پیش از بارگذاری دوباره).
        Assert.False(snapshot.Check(Ip("9.9.9.9"), null, Now.AddMinutes(2)).Blocked);
    }

    [Fact]
    public void Bot_patterns_match_user_agent_case_insensitively()
    {
        var botId = Guid.NewGuid();
        var snapshot = new FirewallSnapshot([], [new FirewallBotEntry(botId, "sqlmap")], Now);

        var decision = snapshot.Check(Ip("1.1.1.1"), "SQLMap/1.7 (https://sqlmap.org)", Now);
        Assert.True(decision.Blocked);
        Assert.Equal(botId, decision.RuleId);
        Assert.Equal("bot", decision.Reason);
        Assert.False(snapshot.Check(Ip("1.1.1.1"), "Mozilla/5.0 Chrome/130", Now).Blocked);
        Assert.False(snapshot.Check(Ip("1.1.1.1"), null, Now).Blocked);
    }

    [Fact]
    public void Allowed_ip_also_bypasses_bot_rules_and_empty_snapshot_passes_everything()
    {
        var snapshot = new FirewallSnapshot(
            [
                new FirewallIpEntry(Guid.NewGuid(), Range("7.7.7.7"), IpRuleAction.Allow, null),
            ], [new FirewallBotEntry(Guid.NewGuid(), "nikto")], Now);

        // ابزار اسکن امنیتی خود فروشگاه از IP مجاز (مثلاً دفتر) بسته نمی‌شود.
        Assert.False(snapshot.Check(Ip("7.7.7.7"), "Nikto/2.5", Now).Blocked);
        Assert.True(snapshot.Check(Ip("7.7.7.8"), "Nikto/2.5", Now).Blocked);

        // فقط قانون مجاز ← چیزی برای بستن نیست.
        var allowOnly = new FirewallSnapshot([new FirewallIpEntry(Guid.NewGuid(), Range("7.7.7.7"), IpRuleAction.Allow, null)], [], Now);
        Assert.True(allowOnly.IsEmpty);
        Assert.False(FirewallSnapshot.Empty.Check(Ip("1.2.3.4"), "sqlmap", Now).Blocked);
    }

    [Theory]
    [InlineData("googlebot", true)]
    [InlineData("Googlebot/2.1", true)]
    [InlineData("bot", true)]
    [InlineData("Mozilla/5.0", true)]
    [InlineData("mac", true)]
    [InlineData("telegrambot", true)]
    [InlineData("sqlmap", false)]
    [InlineData("semrushbot", false)]
    [InlineData("ahrefsbot", false)]
    public void Protected_bot_patterns_cannot_be_blocked(string pattern, bool isProtected)
    {
        Assert.Equal(isProtected, BotRules.IsProtected(pattern));
    }

    [Fact]
    public void Default_bot_patterns_are_safe_to_block()
    {
        Assert.All(FirewallService.DefaultBots, bot => Assert.False(BotRules.IsProtected(bot.Pattern), bot.Pattern));
    }

    [Fact]
    public void Private_addresses_are_detected()
    {
        Assert.True(FirewallService.IsPrivate(Ip("10.1.2.3")));
        Assert.True(FirewallService.IsPrivate(Ip("172.16.0.1")));
        Assert.True(FirewallService.IsPrivate(Ip("172.31.255.255")));
        Assert.True(FirewallService.IsPrivate(Ip("192.168.1.1")));
        Assert.True(FirewallService.IsPrivate(Ip("::ffff:192.168.1.1")));
        Assert.True(FirewallService.IsPrivate(Ip("fd00::1")));
        Assert.False(FirewallService.IsPrivate(Ip("172.32.0.1")));
        Assert.False(FirewallService.IsPrivate(Ip("5.6.7.8")));
        Assert.False(FirewallService.IsPrivate(Ip("2001:db8::1")));
    }

    [Fact]
    public void Persian_digits_with_thousands_separator()
    {
        Assert.Equal("۱٬۲۳۴٬۵۶۷", PersianNumbers.Digits(1234567));
        Assert.Equal("۰", PersianNumbers.Digits(0));
        Assert.Equal("IP ۵.۶", PersianNumbers.Digits("IP 5.6"));
    }

    // ═══ BackupSchedule ═════════════════════════════════════════════════
    // ایران UTC+03:30 (بدون ساعت تابستانی).

    [Fact]
    public void Daily_slot_starts_at_configured_iran_hour()
    {
        // ۰۰:۰۰ UTC = ۰۳:۳۰ تهران ← نوبت ساعت ۳ امروز.
        var (key, start) = BackupSchedule.CurrentSlot(new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero), 1, 3);
        Assert.Equal("2026-10-26-03", key);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 23, 30, 0, TimeSpan.Zero), start);
    }

    [Fact]
    public void Before_first_slot_of_day_belongs_to_yesterday()
    {
        // ۲۳:۰۰ UTC = ۰۲:۳۰ تهران (۲۶ اکتبر) ← هنوز نوبت ساعت ۳ نرسیده؛ نوبت فعلی دیروز ساعت ۳ است.
        var (key, _) = BackupSchedule.CurrentSlot(new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.Zero), 1, 3);
        Assert.Equal("2026-10-25-03", key);
    }

    [Theory]
    [InlineData(12, 30, "2026-10-26-15")] // ۱۶:۰۰ تهران
    [InlineData(5, 30, "2026-10-26-09")]  // ۰۹:۰۰ تهران
    [InlineData(17, 29, "2026-10-26-15")] // ۲۰:۵۹ تهران
    [InlineData(17, 30, "2026-10-26-21")] // ۲۱:۰۰ تهران
    public void Four_runs_per_day_are_six_hours_apart(int utcHour, int utcMinute, string expected)
    {
        var (key, _) = BackupSchedule.CurrentSlot(new DateTimeOffset(2026, 10, 26, utcHour, utcMinute, 0, TimeSpan.Zero), 4, 3);
        Assert.Equal(expected, key);
    }

    [Fact]
    public void Slots_wrapping_past_midnight_use_previous_day()
    {
        // دو بار در روز از ساعت ۲۰ ← ساعت‌های ۸ و ۲۰. ساعت ۰۷:۰۰ تهران ← نوبت ۲۰ دیروز.
        var (key, _) = BackupSchedule.CurrentSlot(new DateTimeOffset(2026, 10, 26, 3, 30, 0, TimeSpan.Zero), 2, 20);
        Assert.Equal("2026-10-25-20", key);
        // ۰۱:۰۰ تهران با ۴ نوبت از ساعت ۳ ← نوبت ۲۱ دیروز.
        var (key2, _) = BackupSchedule.CurrentSlot(new DateTimeOffset(2026, 10, 25, 21, 30, 0, TimeSpan.Zero), 4, 3);
        Assert.Equal("2026-10-25-21", key2);
    }

    [Fact]
    public void Next_run_is_the_following_slot()
    {
        // ۱۶:۰۰ تهران، ۴ نوبت از ساعت ۳ ← بعدی ۲۱:۰۰ تهران = ۱۷:۳۰ UTC.
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 17, 30, 0, TimeSpan.Zero),
            BackupSchedule.NextRun(new DateTimeOffset(2026, 10, 26, 12, 30, 0, TimeSpan.Zero), 4, 3));
        // یک بار در روز ← فردا همان ساعت.
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 23, 30, 0, TimeSpan.Zero),
            BackupSchedule.NextRun(new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero), 1, 3));
    }

    [Fact]
    public void Out_of_range_settings_are_clamped()
    {
        var now = new DateTimeOffset(2026, 10, 26, 12, 30, 0, TimeSpan.Zero);
        Assert.Equal(BackupSchedule.CurrentSlot(now, 4, 3), BackupSchedule.CurrentSlot(now, 10, 3));
        Assert.Equal(BackupSchedule.CurrentSlot(now, 1, 23), BackupSchedule.CurrentSlot(now, 1, 99));
    }

    [Fact]
    public void Backup_file_names_use_iran_time_and_kind_extension()
    {
        var now = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal("fratak-db-20261026-033000.bak", BackupSchedule.FileName(BackupKind.Database, now));
        Assert.Equal("fratak-uploads-20261026-033000.zip", BackupSchedule.FileName(BackupKind.Uploads, now));
    }

    [Theory]
    [InlineData("/var/opt/mssql/backup/", "/var/opt/mssql/backup/a.bak")]
    [InlineData("/var/opt/mssql/backup", "/var/opt/mssql/backup/a.bak")]
    [InlineData(@"D:\Backups", @"D:\Backups\a.bak")]
    [InlineData(@"D:\Backups\", @"D:\Backups\a.bak")]
    public void Combine_uses_the_folder_style_separator(string folder, string expected)
    {
        Assert.Equal(expected, BackupSchedule.Combine(folder, "a.bak"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("/var/opt/mssql/backup", true)]
    [InlineData(@"D:\Backups", true)]
    [InlineData(@"\\nas\share\sql", true)]
    [InlineData("relative/path", false)]
    [InlineData("C:", false)]
    [InlineData("/var/../etc", false)]
    [InlineData(@"D:\x';DROP DATABASE x;--", false)]
    [InlineData("/tmp/a|b", false)]
    public void Backup_folder_validation_blocks_injection_and_relative_paths(string? folder, bool valid)
    {
        Assert.Equal(valid, BackupSchedule.IsValidFolder(folder));
    }

    // ═══ AlertRules ═════════════════════════════════════════════════════

    [Theory]
    [InlineData(AlertMetric.DiskFreePercent, 5d, 10d, true)]
    [InlineData(AlertMetric.DiskFreePercent, 15d, 10d, false)]
    [InlineData(AlertMetric.ErrorsPerHour, 20d, 20d, true)]
    [InlineData(AlertMetric.ErrorsPerHour, 19d, 20d, false)]
    [InlineData(AlertMetric.DatabaseLatencyMs, 1500d, 1000d, true)]
    public void Breach_direction_depends_on_metric(AlertMetric metric, double value, double threshold, bool breached)
    {
        Assert.Equal(breached, AlertRules.IsBreached(metric, value, threshold));
    }

    [Fact]
    public void Decide_fires_once_then_waits_for_cooldown_and_resolves_when_normal()
    {
        // مشکل تازه ← ارسال.
        Assert.Equal((true, false), AlertRules.Decide(true, false, null, 60, Now));
        // هنوز ادامه دارد ولی در فاصلهٔ تکرار ← ارسال نشود.
        Assert.Equal((false, false), AlertRules.Decide(true, true, Now.AddMinutes(-10), 60, Now));
        // فاصلهٔ تکرار گذشت ← یادآوری.
        Assert.Equal((true, false), AlertRules.Decide(true, true, Now.AddMinutes(-61), 60, Now));
        // عادی شد ← «برطرف شد» فقط اگر قبلاً فعال بوده.
        Assert.Equal((false, true), AlertRules.Decide(false, true, Now.AddMinutes(-5), 60, Now));
        Assert.Equal((false, false), AlertRules.Decide(false, false, null, 60, Now));
    }

    [Theory]
    [InlineData(AlertMetric.ServerErrorPercent, 150d, false)]
    [InlineData(AlertMetric.ServerErrorPercent, 5d, true)]
    [InlineData(AlertMetric.DiskFreePercent, 0d, false)]
    [InlineData(AlertMetric.ErrorsPerHour, 5d, true)]
    [InlineData(AlertMetric.MemoryMb, -1d, false)]
    public void Threshold_validation(AlertMetric metric, double threshold, bool valid)
    {
        Assert.Equal(valid, AlertRules.IsValidThreshold(metric, threshold));
    }

    [Fact]
    public void Every_metric_has_info_and_a_valid_default_threshold()
    {
        foreach (var metric in Enum.GetValues<AlertMetric>())
        {
            var info = AlertRules.Info(metric);
            Assert.False(string.IsNullOrWhiteSpace(info.Title));
            Assert.False(string.IsNullOrWhiteSpace(info.Help));
            Assert.True(AlertRules.IsValidThreshold(metric, info.DefaultThreshold), metric.ToString());
        }
        Assert.Equal(Enum.GetValues<AlertMetric>().Length, AlertRules.Metrics.Select(m => m.Metric).Distinct().Count());
    }

    [Fact]
    public void Numbers_are_persian_with_separators()
    {
        Assert.Equal("۱٬۵۰۰", AlertRules.Number(1500));
        Assert.Equal("۲٫۵", AlertRules.Number(2.5));
        Assert.Equal("۳", AlertRules.Number(3));
        Assert.Contains("۱٬۲۰۰", AlertRules.Describe(AlertMetric.DatabaseLatencyMs, 1200, 1000));
    }

    [Theory]
    [InlineData("5", 5d)]
    [InlineData("۱٫۵", 1.5d)]
    [InlineData("1.5", 1.5d)]
    [InlineData("۱٬۰۰۰", 1000d)]
    [InlineData("1,000", 1000d)]
    [InlineData(" ۲۰ ", 20d)]
    public void Alert_threshold_form_accepts_persian_input(string text, double expected)
    {
        Assert.Equal(expected, AlertsModel.ParseThreshold(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Alert_threshold_form_rejects_garbage(string? text)
    {
        Assert.Null(AlertsModel.ParseThreshold(text));
    }

    // ═══ JobMonitor ═════════════════════════════════════════════════════

    [Fact]
    public async Task Paused_job_is_skipped_and_counted_and_critical_jobs_ignore_pause()
    {
        var pausable = "تست-توقف-" + Guid.NewGuid().ToString("N");
        var critical = "تست-حیاتی-" + Guid.NewGuid().ToString("N");
        JobMonitor.Register(pausable, TimeSpan.FromMinutes(5));
        JobMonitor.Register(critical, TimeSpan.FromMinutes(5), pausable: false);
        try
        {
            JobMonitor.SetPaused([pausable, critical]);
            Assert.True(JobMonitor.IsPaused(pausable));
            Assert.False(JobMonitor.IsPaused(critical));

            var ran = false;
            await JobMonitor.TrackAsync(pausable, () => { ran = true; return Task.CompletedTask; });
            Assert.False(ran);
            Assert.True(JobMonitor.ShouldSkip(pausable));
            var state = JobMonitor.Find(pausable)!;
            Assert.Equal(2, state.Skips);
            Assert.Equal(0, state.Runs);

            await JobMonitor.TrackAsync(critical, () => { ran = true; return Task.CompletedTask; });
            Assert.True(ran);
            Assert.False(JobMonitor.ShouldSkip(critical));
        }
        finally
        {
            JobMonitor.SetPaused([]);
        }
        Assert.False(JobMonitor.ShouldSkip(pausable));
    }

    [Fact]
    public async Task Failing_job_counts_as_unhealthy_unless_paused()
    {
        var name = "تست-ناموفق-" + Guid.NewGuid().ToString("N");
        JobMonitor.Register(name, TimeSpan.FromMinutes(5));
        var now = DateTimeOffset.UtcNow;
        var before = JobMonitor.CountUnhealthy(now);

        await Assert.ThrowsAsync<InvalidOperationException>(() => JobMonitor.TrackAsync(name, () => throw new InvalidOperationException("boom")));
        var state = JobMonitor.Find(name)!;
        Assert.True(state.IsFailing);
        Assert.Equal("InvalidOperationException: boom", state.LastError);
        Assert.Equal(before + 1, JobMonitor.CountUnhealthy(now));
        Assert.Equal(("ناموفق", "a-badge-red"), JobsModel.Status(state));

        try
        {
            JobMonitor.SetPaused([name]);
            Assert.Equal(before, JobMonitor.CountUnhealthy(now));
            Assert.Equal(("متوقف", "a-badge-yellow"), JobsModel.Status(state));
        }
        finally
        {
            JobMonitor.SetPaused([]);
        }

        // اجرای موفق بعدی خطا را برطرف می‌کند.
        await JobMonitor.TrackAsync(name, () => Task.CompletedTask);
        Assert.False(JobMonitor.Find(name)!.IsFailing);
        Assert.Equal(("سالم", "a-badge-green"), JobsModel.Status(state));
    }

    [Fact]
    public void Job_status_reports_never_run_and_late_jobs()
    {
        Assert.Equal(("هنوز اجرا نشده", "a-badge-gray"), JobsModel.Status(new JobState { Name = "x" }));
        var late = new JobState
        {
            Name = "y",
            ExpectedInterval = TimeSpan.FromMinutes(10),
            LastStartedAt = DateTimeOffset.UtcNow.AddHours(-2),
            LastSucceededAt = DateTimeOffset.UtcNow.AddHours(-2),
        };
        Assert.Equal(("دیر شده", "a-badge-yellow"), JobsModel.Status(late));
    }

    // ═══ گزارش‌های سرور ═════════════════════════════════════════════════

    [Fact]
    public void Ring_buffer_keeps_newest_items_first()
    {
        var buffer = new RingBuffer<int>(3);
        Assert.Empty(buffer.Snapshot());
        buffer.Add(1);
        buffer.Add(2);
        Assert.Equal(new[] { 2, 1 }, buffer.Snapshot());
        buffer.Add(3);
        buffer.Add(4);
        buffer.Add(5);
        Assert.Equal(3, buffer.Count);
        Assert.Equal(new[] { 5, 4, 3 }, buffer.Snapshot());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer<int>(0));
    }

    [Theory]
    [InlineData("fratak-2026-10-26.log", true)]
    [InlineData("fratak-2026-13-01.log", false)]
    [InlineData("../appsettings.json", false)]
    [InlineData("fratak-2026-10-26.log.bak", false)]
    [InlineData("fratak-../../x.log", false)]
    [InlineData("other-2026-10-26.log", false)]
    [InlineData(null, false)]
    public void Log_file_names_are_strictly_validated(string? name, bool valid)
    {
        Assert.Equal(valid, LogStore.IsValidFileName(name));
    }

    [Fact]
    public void Log_file_name_round_trips_through_validation()
    {
        var name = LogStore.FileNameFor(new DateTimeOffset(2026, 10, 26, 23, 59, 0, TimeSpan.Zero));
        Assert.Equal("fratak-2026-10-26.log", name);
        Assert.True(LogStore.IsValidFileName(name));
    }

    [Theory]
    [InlineData("Fratak_Shop.Core.Services.Orders.OrderService", LogLevel.Information, true)]
    [InlineData("Fratak_Shop.Core.Services.Orders.OrderService", LogLevel.Debug, false)]
    [InlineData("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Information, false)]
    [InlineData("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning, true)]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Error, true)]
    [InlineData("Microsoft.Hosting.Lifetime", LogLevel.Information, true)]
    [InlineData("System.Net.Http.HttpClient.Default", LogLevel.Information, false)]
    [InlineData("Fratak_Shop.Web.Program", LogLevel.None, false)]
    public void Memory_logger_keeps_app_messages_and_only_framework_warnings(string category, LogLevel level, bool accepted)
    {
        Assert.Equal(accepted, MemoryLogProvider.Accepts(category, level));
    }

    [Theory]
    [InlineData("Fratak_Shop.Core.Services.Orders.OrderService", "Orders.OrderService")]
    [InlineData("Program", "Program")]
    [InlineData("Microsoft.Hosting", "Microsoft.Hosting")]
    public void Log_category_is_shortened_to_last_two_parts(string category, string expected)
    {
        Assert.Equal(expected, LogsModel.ShortCategory(category));
    }

    [Fact]
    public void Every_log_level_has_a_persian_title_and_badge()
    {
        foreach (var level in new[] { LogLevel.Information, LogLevel.Warning, LogLevel.Error, LogLevel.Critical })
        {
            Assert.False(string.IsNullOrWhiteSpace(LogLevels.Title(level)));
            Assert.StartsWith("a-badge-", LogLevels.Css(level));
        }
    }

    // ═══ حالت تعمیرات و /healthz ════════════════════════════════════════

    [Theory]
    [InlineData("/admin", true)]
    [InlineData("/admin/orders", true)]
    [InlineData("/login", true)]
    [InlineData("/api/auth/refresh", true)]
    [InlineData("/healthz", true)]
    [InlineData("/hubs/live", true)]
    [InlineData("/robots.txt", true)]
    [InlineData("/sw.js", true)]
    [InlineData("/", false)]
    [InlineData("/product/x", false)]
    [InlineData("/administrator", false)]
    [InlineData("/checkout", false)]
    public void Maintenance_exempt_paths(string path, bool exempt)
    {
        Assert.Equal(exempt, MaintenanceRules.IsExemptPath(new PathString(path)));
    }

    [Fact]
    public void Maintenance_allowed_ips_support_ranges()
    {
        Assert.True(MaintenanceRules.IsAllowedIp("5.6.7.0/24, 1.1.1.1", Ip("5.6.7.9")));
        Assert.True(MaintenanceRules.IsAllowedIp("5.6.7.0/24, 1.1.1.1", Ip("::ffff:1.1.1.1")));
        Assert.False(MaintenanceRules.IsAllowedIp("5.6.7.0/24", Ip("8.8.8.8")));
        Assert.False(MaintenanceRules.IsAllowedIp(null, Ip("8.8.8.8")));
        Assert.False(MaintenanceRules.IsAllowedIp("5.6.7.0/24", null));
    }

    [Fact]
    public void Maintenance_page_encodes_text_and_shows_eta()
    {
        var html = MaintenanceRules.Page("<b>فراتک</b>", "پیام\nدوم", 90);
        Assert.Contains("&lt;b&gt;فراتک&lt;/b&gt;", html);
        Assert.DoesNotContain("<b>فراتک</b>", html);
        Assert.Contains("پیام<br>دوم", html);
        Assert.Contains("حدود ۲ ساعت", html);
        Assert.Contains("حدود ۳۰ دقیقه", MaintenanceRules.Page(null, null, 30));
        Assert.Equal(MaintenanceRules.Message(null), MaintenanceRules.Message("  "));
    }

    [Theory]
    [InlineData("abcdefghijklmnop", "abcdefghijklmnop", true)]
    [InlineData("abcdefghijklmnop", "abcdefghijklmnoq", false)]
    [InlineData("abcdefghijklmnop", "", false)]
    [InlineData("", "", false)]
    [InlineData(null, null, false)]
    public void Health_token_comparison(string? expected, string? given, bool matches)
    {
        Assert.Equal(matches, HealthEndpoint.TokenMatches(expected, given));
    }

    [Fact]
    public void Cache_hit_rate()
    {
        Assert.Equal(90d, IndexModel.HitRate(90, 10));
        Assert.Null(IndexModel.HitRate(0, 0));
    }
}
