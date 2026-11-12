using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Ops;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Auth;
using Fratak_Shop.Web.Infrastructure.Ops;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// آزمون‌های مرحلهٔ هفدهم (مشکلات سرور واقعی): مدیران اصلی با Key تکراری، کلید پایدار نشست‌ها، دروازهٔ راه‌اندازی،
/// پایش درخواست‌های ردشده، IPهای مورد اعتماد، متن ۴۰۳ دیوار آتش، بررسی‌های صفحهٔ سلامت و کوچک‌سازی خروجی انتشار.
/// <para>همه ایزوله‌اند (بدون پایگاه داده). وضعیت‌های سراسری (StartupGate.IsReady، FirewallState.Trusted) عمداً تغییر داده
/// نمی‌شوند تا آزمون‌های سرتاسری هم‌زمان تحت تأثیر قرار نگیرند؛ از نسخه‌های خالص همان منطق استفاده می‌شود.</para>
/// </summary>
public class Round17ProductionTests
{
    // ═══ مدیران اصلی: Key تکراری/نامعتبر دیگر مدیر دوم را حذف نمی‌کند ═══

    [Fact]
    public void Resolve_keeps_distinct_keys()
    {
        var resolved = DefaultAdminAccount.Resolve(
        [
            new DefaultAdminAccount("09913181090", Key: "owner"),
            new DefaultAdminAccount("09134426156", Key: "partner"),
        ]);

        Assert.Equal(new[] { "key:owner", "key:partner" }, resolved.Select(r => r.Key).ToArray());
        Assert.All(resolved, r => Assert.Null(r.Note));
    }

    [Fact]
    public void Resolve_duplicate_key_falls_back_to_phone_with_warning()
    {
        // علت واقعی «مدیر دوم دسترسی ندارد»: هر دو مدیر Key «owner» داشتند و دومی نادیده گرفته می‌شد.
        var resolved = DefaultAdminAccount.Resolve(
        [
            new DefaultAdminAccount("09913181090", Key: "owner"),
            new DefaultAdminAccount("09134426156", Key: "owner"),
        ]);

        Assert.Equal("key:owner", resolved[0].Key);
        Assert.Equal("phone:09134426156", resolved[1].Key);
        Assert.NotNull(resolved[1].Note);
        Assert.StartsWith("هشدار", resolved[1].Note);
        Assert.Contains("تکراری", resolved[1].Note);
        Assert.DoesNotContain("09134426156", resolved[1].Note); // شماره پوشیده است
        Assert.True(DefaultAdminStartupReport.IsProblem(resolved[1].Note!));
    }

    [Fact]
    public void Resolve_invalid_key_falls_back_to_phone()
    {
        var resolved = DefaultAdminAccount.Resolve([new DefaultAdminAccount("09134426156", Key: "مالک")]);
        Assert.Equal("phone:09134426156", resolved[0].Key);
        Assert.Contains("نامعتبر", resolved[0].Note);
    }

    [Fact]
    public void Resolve_skips_invalid_phone_and_duplicate_phone()
    {
        var resolved = DefaultAdminAccount.Resolve(
        [
            new DefaultAdminAccount("12345", Key: "a"),
            new DefaultAdminAccount("09134426156", Key: "b"),
            new DefaultAdminAccount("0913 442 6156", Key: "b"),
            null,
        ]);

        Assert.Equal(3, resolved.Count);
        Assert.Null(resolved[0].Key);
        Assert.Equal("key:b", resolved[1].Key);
        Assert.Null(resolved[2].Key);
        Assert.StartsWith("نادیده", resolved[2].Note);
    }

    [Fact]
    public void Resolve_without_keys_uses_phone_keys()
    {
        var resolved = DefaultAdminAccount.Resolve([new DefaultAdminAccount("+98 991 318 1090"), new DefaultAdminAccount("09134426156")]);
        Assert.Equal(new[] { "phone:09913181090", "phone:09134426156" }, resolved.Select(r => r.Key).ToArray());
    }

    [Fact]
    public void Configured_default_admins_have_distinct_keys()
    {
        // appsettings.json واقعی: دو مالک با Keyهای جدا (owner / partner).
        var json = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Fratak-Shop.Web", "appsettings.json"));
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json))).Build();
        var options = new ShopOptions();
        configuration.GetSection("Shop").Bind(options);

        var resolved = DefaultAdminAccount.Resolve(options.DefaultAdminAccounts());
        Assert.True(resolved.Count >= 2);
        Assert.All(resolved, r => Assert.NotNull(r.Key));
        Assert.All(resolved, r => Assert.Null(r.Note));
        Assert.Equal(resolved.Count, resolved.Select(r => r.Key).Distinct().Count());
    }

    [Theory]
    [InlineData("ساخته شد 0991***1090 (مالک، همهٔ دسترسی‌ها) با رمز اولیه.", false)]
    [InlineData("بدون تغییر 0991***1090.", false)]
    [InlineData("به‌روز شد 0991***1090: شماره طبق تنظیمات اصلاح شد.", false)]
    [InlineData("هشدار 0913***6156: Key تکراری", true)]
    [InlineData("نادیده (شمارهٔ موبایل نامعتبر در Shop:DefaultAdmins).", true)]
    [InlineData("ساخته نشد 0913***6156: این شماره در اختیار مدیر اصلی دیگری است.", true)]
    [InlineData("تداخل 0913***6156: …", true)]
    [InlineData("بازگردانی نشد 0913***6156: …", true)]
    public void Startup_report_detects_problem_notes(string note, bool expected)
        => Assert.Equal(expected, DefaultAdminStartupReport.IsProblem(note));

    // ═══ کلید امضای توکن‌ها: پایدار بین ری‌استارت‌ها ═══

    [Fact]
    public void Signing_key_is_created_once_and_reloaded()
    {
        var folder = TempFolder();
        try
        {
            var protector = new EphemeralDataProtectionProvider().CreateProtector("test");
            var first = JwtSigningKeyProvider.LoadOrCreate(folder, protector);
            var second = JwtSigningKeyProvider.LoadOrCreate(folder, protector);

            Assert.Equal(64, first.Length);
            Assert.Equal(first, second);
            Assert.True(File.Exists(Path.Combine(folder, JwtSigningKeyProvider.FileName)));
            // فایل رمز شده است؛ کلید خام در آن دیده نمی‌شود.
            Assert.DoesNotContain(Convert.ToBase64String(first), File.ReadAllText(Path.Combine(folder, JwtSigningKeyProvider.FileName)));
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Unreadable_signing_key_is_replaced()
    {
        var folder = TempFolder();
        try
        {
            var first = JwtSigningKeyProvider.LoadOrCreate(folder, new EphemeralDataProtectionProvider().CreateProtector("test"));
            // کلیدهای DataProtection عوض شده (مثلاً پوشهٔ keys پاک شده): فایل قبلی باز نمی‌شود ← کلید تازه، بدون خطا.
            var otherProtector = new EphemeralDataProtectionProvider().CreateProtector("test");
            var replaced = JwtSigningKeyProvider.LoadOrCreate(folder, otherProtector);
            Assert.NotEqual(first, replaced);
            Assert.Equal(replaced, JwtSigningKeyProvider.LoadOrCreate(folder, otherProtector));

            File.WriteAllText(Path.Combine(folder, JwtSigningKeyProvider.FileName), "not base64 !!");
            Assert.Equal(64, JwtSigningKeyProvider.LoadOrCreate(folder, otherProtector).Length);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    // ═══ دروازهٔ راه‌اندازی: قطعی موقت پایگاه داده برنامه را از کار نمی‌اندازد ═══

    [Fact]
    public async Task Startup_gate_retries_until_initialization_succeeds()
    {
        var calls = 0;
        var before = StartupGate.Attempts;
        var ok = await StartupGate.TryRunAsync(() =>
        {
            calls++;
            return calls == 1 ? throw new InvalidOperationException("SQL Server unreachable") : Task.CompletedTask;
        }, NullLogger.Instance, TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(2, calls);
        Assert.True(StartupGate.Attempts >= before + 2);
    }

    [Fact]
    public async Task Startup_gate_gives_up_within_the_budget()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var ok = await StartupGate.TryRunAsync(() => throw new TimeoutException("down"), NullLogger.Instance, TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.False(ok);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Startup_page_is_self_contained()
    {
        var page = StartupGate.Page();
        Assert.Contains("noindex", page);
        Assert.Contains("http-equiv=\"refresh\"", page);
        Assert.DoesNotContain("<script", page, StringComparison.OrdinalIgnoreCase); // CSP: بدون اسکریپت درون‌خطی
    }

    // ═══ پایش درخواست‌های ردشده ═══

    [Fact]
    public void Request_monitor_records_rejections_with_reason()
    {
        var path = "/r17-" + Guid.NewGuid().ToString("N");
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Path = path;
        http.Request.Host = new HostString("shop.fratak.ir");
        http.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:5.6.7.8");
        http.Request.Headers.Cookie = AuthCookies.RefreshCookie + "=abc";
        var before = RequestMonitor.TotalsByKind()[RequestMonitor.Kind.LoginRedirect];

        RequestMonitor.Record(http, RequestMonitor.Kind.LoginRedirect, "SecurityTokenInvalidSignatureException");

        var recorded = Assert.Single(RequestMonitor.Recent(RequestMonitor.Kind.LoginRedirect, 500), e => e.Path == path);
        Assert.Equal("5.6.7.8", recorded.Ip);
        Assert.Equal(302, recorded.Status);
        Assert.True(recorded.HadSession);
        Assert.Equal("shop.fratak.ir", recorded.Host);
        Assert.Equal("SecurityTokenInvalidSignatureException", recorded.Detail);
        Assert.True(RequestMonitor.TotalsByKind()[RequestMonitor.Kind.LoginRedirect] > before);
        Assert.True(RequestMonitor.LastMinutes(5)[RequestMonitor.Kind.LoginRedirect] >= 1);
        Assert.True(RequestMonitor.LostSessionsLastHour() >= 1);
        Assert.Contains(RequestMonitor.TopIps(50), t => t.Ip == "5.6.7.8");
        Assert.DoesNotContain(RequestMonitor.Recent(RequestMonitor.Kind.Firewall, 500), e => e.Path == path);
    }

    [Fact]
    public void Request_monitor_never_throws_and_clips_long_values()
    {
        var http = new DefaultHttpContext();
        http.Request.Path = "/" + new string('a', 1000);
        RequestMonitor.Record(http, RequestMonitor.Kind.Forbidden, new string('x', 1000));
        var e = RequestMonitor.Recent(RequestMonitor.Kind.Forbidden, 500).First(x => x.Path.StartsWith("/aaaa", StringComparison.Ordinal));
        Assert.Equal(200, e.Path.Length);
        Assert.Equal(300, e.Detail!.Length);
        Assert.Equal("-", e.Ip);
        Assert.Equal(403, e.Status);
    }

    [Fact]
    public void Untrusted_forwarding_is_counted()
    {
        var before = RequestMonitor.ForwardedUntrusted;
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.77");
        http.Request.Headers["X-Forwarded-For"] = "1.2.3.4";
        RequestMonitor.InspectForwarding(http);

        var clean = new DefaultHttpContext();
        clean.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.78");
        RequestMonitor.InspectForwarding(clean);

        Assert.True(RequestMonitor.ForwardedUntrusted >= before + 1);
        Assert.Contains(RequestMonitor.UntrustedProxies(), p => p.Ip == "203.0.113.77");
        Assert.DoesNotContain(RequestMonitor.UntrustedProxies(), p => p.Ip == "203.0.113.78");
    }

    [Fact]
    public void Every_rejection_kind_has_a_persian_title()
    {
        foreach (var kind in Enum.GetValues<RequestMonitor.Kind>())
            Assert.DoesNotContain(kind.ToString(), RequestMonitor.Title(kind));
    }

    // ═══ دیوار آتش و محدودیت تعداد ═══

    [Theory]
    [InlineData("5.6.7.9", true)]
    [InlineData("::ffff:5.6.7.9", true)]
    [InlineData("5.6.8.1", false)]
    [InlineData("10.0.0.5", true)]
    [InlineData(null, false)]
    public void Trusted_ips_match_single_addresses_and_ranges(string? ip, bool expected)
    {
        Assert.True(IpRange.TryParse("5.6.7.0/24", out var office));
        Assert.True(IpRange.TryParse("10.0.0.5", out var server));
        Assert.Equal(expected, FirewallState.IsTrusted(ip is null ? null : IPAddress.Parse(ip), [office, server]));
    }

    [Fact]
    public void Firewall_block_page_shows_ip_and_rule_code()
    {
        var id = Guid.Parse("3f2a9c1b-0000-0000-0000-000000000000");
        Assert.Equal("FW-3F2A9C1B", FirewallMiddleware.FirewallReference(id));
        Assert.Equal("FW-?", FirewallMiddleware.FirewallReference(null));

        var text = FirewallMiddleware.BlockedText(IPAddress.Parse("::ffff:5.6.7.8"), "FW-3F2A9C1B");
        Assert.StartsWith("403", text);
        Assert.Contains("IP شما: 5.6.7.8", text);
        Assert.Contains("FW-3F2A9C1B", text);
        Assert.Contains("IP شما: -", FirewallMiddleware.BlockedText(null, "FW-?"));
    }

    [Theory]
    [InlineData("POST", "/login", "ورود")]
    [InlineData("POST", "/api/auth/refresh", "ورود")]
    [InlineData("GET", "/api/search", "جست‌وجو")]
    [InlineData("GET", "/api/cart", "API")]
    [InlineData("POST", "/cart/add", "ارسال فرم")]
    [InlineData("GET", "/login", "سهمیهٔ دقیقه‌ای")]
    public void Rate_limit_policy_name_explains_the_quota(string method, string path, string expected)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Path = path;
        Assert.Contains(expected, RateLimitingSetup.PolicyName(http));
    }

    // ═══ بررسی‌های صفحهٔ سلامت ═══

    [Theory]
    [InlineData("configured", HealthLevel.Healthy)]
    [InlineData("persisted", HealthLevel.Healthy)]
    [InlineData("ephemeral", HealthLevel.Critical)]
    public void Signing_key_check(string source, HealthLevel expected)
    {
        var check = ProductionDiagnostics.SigningKey(source, source == "ephemeral" ? "Access denied" : null);
        Assert.Equal(expected, check.Level);
        if (expected == HealthLevel.Critical)
        {
            Assert.Contains("Access denied", check.Summary);
            Assert.Contains("Jwt__SigningKey", check.Hint);
        }
    }

    [Fact]
    public void Canonical_check_flags_wrong_domain()
    {
        // همان اشتباه واقعی: CanonicalHost = faratek.ir ولی سایت با shop.fratak.ir باز می‌شود.
        var seen = new Dictionary<string, long> { ["shop.fratak.ir"] = 120, ["localhost"] = 5 };
        var check = ProductionDiagnostics.CanonicalHost("faratek.ir", seen, [], production: true);
        Assert.NotNull(check);
        Assert.Equal(HealthLevel.Critical, check.Level);
        Assert.Contains("shop.fratak.ir", check.Summary);
        Assert.Contains("shop.fratak.ir", check.Hint);
    }

    [Fact]
    public void Canonical_check_is_healthy_on_the_right_domain()
    {
        var seen = new Dictionary<string, long> { ["shop.fratak.ir"] = 120, ["www.shop.fratak.ir"] = 3, ["temp.plesk.page"] = 9 };
        var check = ProductionDiagnostics.CanonicalHost("shop.fratak.ir", seen, [], production: true);
        Assert.NotNull(check);
        Assert.Equal(HealthLevel.Healthy, check.Level);
        Assert.Contains("temp.plesk.page", check.Summary);
    }

    [Fact]
    public void Canonical_check_without_setting()
    {
        Assert.Null(ProductionDiagnostics.CanonicalHost("off", new Dictionary<string, long>(), [], production: false));
        var warning = ProductionDiagnostics.CanonicalHost("", new Dictionary<string, long> { ["shop.fratak.ir"] = 4 }, [], production: true);
        Assert.Equal(HealthLevel.Warning, warning!.Level);
        Assert.Contains("shop.fratak.ir", warning.Hint);
    }

    [Fact]
    public void Proxy_check_needs_repeated_evidence()
    {
        Assert.Null(ProductionDiagnostics.Proxy(3, [("203.0.113.1", 3)]));
        var check = ProductionDiagnostics.Proxy(250, [("203.0.113.1", 250)]);
        Assert.Equal(HealthLevel.Warning, check!.Level);
        Assert.Contains("203.0.113.1", check.Summary);
        Assert.Contains("KnownNetworks", check.Hint);
    }

    [Fact]
    public void Default_admin_check_lists_problems()
    {
        Assert.Equal(HealthLevel.Healthy, ProductionDiagnostics.DefaultAdmins(["بدون تغییر 0991***1090."], []).Level);
        var check = ProductionDiagnostics.DefaultAdmins(["هشدار 0913***6156: Key تکراری"], ["هشدار 0913***6156: Key تکراری"]);
        Assert.Equal(HealthLevel.Warning, check.Level);
        Assert.Contains("partner", check.Hint);
    }

    [Fact]
    public void Lifecycle_check_levels()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(HealthLevel.Healthy, ProductionDiagnostics.Lifecycle(2, []).Level);
        Assert.Equal(HealthLevel.Warning, ProductionDiagnostics.Lifecycle(12, []).Level);
        Assert.Contains("AlwaysRunning", ProductionDiagnostics.Lifecycle(12, []).Hint);

        var sudden = new LifecycleRun(now.AddHours(-3), null, 999_999, "1.0", null);
        Assert.Equal(HealthLevel.Warning, ProductionDiagnostics.Lifecycle(3, [sudden]).Level);

        var crashed = sudden with { Crash = "OutOfMemoryException: boom" };
        var check = ProductionDiagnostics.Lifecycle(3, [crashed]);
        Assert.Equal(HealthLevel.Critical, check.Level);
        Assert.Contains("OutOfMemoryException", check.Summary);
    }

    [Fact]
    public void Lifecycle_detects_unclean_runs()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(AppLifecycle.IsUnclean(new LifecycleRun(now.AddHours(-2), now.AddHours(-1), 999_999, "1.0", null)));
        Assert.True(AppLifecycle.IsUnclean(new LifecycleRun(now.AddHours(-2), now.AddHours(-1), 999_999, "1.0", "Crash")));
        // پروسه‌ای که دیگر وجود ندارد و خاموشی عادی ثبت نکرده ← ناگهانی.
        Assert.True(AppLifecycle.IsUnclean(new LifecycleRun(now.AddDays(-30), null, int.MaxValue - 7, "1.0", null)));
        // پروسهٔ همین آزمون (زنده) ← هنوز در حال اجرا، نه ناگهانی.
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        Assert.False(AppLifecycle.IsUnclean(new LifecycleRun(self.StartTime.ToUniversalTime(), null, Environment.ProcessId, "1.0", null)));
    }

    [Fact]
    public void Rejection_check_warns_on_lost_sessions_and_mass_429()
    {
        var quiet = Enum.GetValues<RequestMonitor.Kind>().ToDictionary(k => k, _ => 0);
        Assert.Equal(HealthLevel.Healthy, ProductionDiagnostics.Rejections(quiet, 0).Level);
        Assert.Equal(HealthLevel.Warning, ProductionDiagnostics.Rejections(quiet, 8).Level);

        var limited = new Dictionary<RequestMonitor.Kind, int>(quiet) { [RequestMonitor.Kind.RateLimited] = 90 };
        var check = ProductionDiagnostics.Rejections(limited, 0);
        Assert.Equal(HealthLevel.Warning, check.Level);
        Assert.Contains("TrustedIps", check.Hint);
    }

    [Fact]
    public void Native_libraries_load_on_this_machine()
    {
        // اگر خروجی برای سیستم‌عامل دیگری ساخته شده باشد، همین بررسی در صفحهٔ سلامت بحرانی می‌شود.
        Assert.Null(NativeLibraries.Skia);
        Assert.Null(NativeLibraries.Onnx);
        Assert.Single(ProductionDiagnostics.Natives(null, null));
        Assert.Equal(HealthLevel.Critical, ProductionDiagnostics.Natives("DllNotFoundException: libSkiaSharp", null).First().Level);
    }

    // ═══ خروجی انتشار کم‌حجم ═══

    [Theory]
    [InlineData("windows", "runtimes/win-x64/native/onnxruntime.dll", true)]
    [InlineData("windows", "runtimes\\win-x86\\native\\libSkiaSharp.dll", true)]
    [InlineData("windows", "runtimes/win/lib/net9.0/Microsoft.Data.SqlClient.dll", true)]
    [InlineData("windows", "runtimes/win-arm64/native/onnxruntime.dll", false)]
    [InlineData("windows", "runtimes/linux-x64/native/libonnxruntime.so", false)]
    [InlineData("windows", "runtimes/osx-arm64/native/libonnxruntime.dylib", false)]
    [InlineData("windows", "runtimes/unix/lib/net9.0/Microsoft.Data.SqlClient.dll", false)]
    [InlineData("windows", "runtimes/winx/native/x.dll", false)]
    [InlineData("windows", "Fratak-Shop.Web.dll", true)]
    [InlineData("linux", "runtimes/linux-x64/native/libSkiaSharp.so", true)]
    [InlineData("linux", "runtimes/linux-musl-x64/native/libSkiaSharp.so", true)]
    [InlineData("linux", "runtimes/unix/lib/net9.0/Microsoft.Data.SqlClient.dll", true)]
    [InlineData("linux", "runtimes/linux-arm64/native/libSkiaSharp.so", false)]
    [InlineData("linux", "runtimes/win-x64/native/onnxruntime.dll", false)]
    [InlineData("linux", "wwwroot/assets/css/output.css", true)]
    public void Publish_keeps_only_target_runtimes(string target, string relativePath, bool kept)
    {
        var project = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "Fratak-Shop.Web", "Fratak-Shop.Web.csproj"));
        var trim = project.Descendants("Target").Single(t => (string?)t.Attribute("Name") == "FratakTrimPublishRuntimes");
        var pattern = trim.Descendants("_FratakOtherRuntimes").Single(p => ((string?)p.Attribute("Condition"))!.Contains($"'{target}'", StringComparison.Ordinal)).Value;

        Assert.Equal(!kept, Regex.IsMatch(relativePath, pattern));
    }

    [Theory]
    [InlineData("wwwroot/uploads/products/a.webp", true)]
    [InlineData("wwwroot\\Uploads\\x.jpg", true)]
    [InlineData("App_Data/logs/lifecycle.json", true)]
    [InlineData("wwwroot/assets/image/logo.svg", false)]
    [InlineData("appsettings.json", false)]
    public void Publish_never_ships_local_data(string relativePath, bool dropped)
    {
        var project = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "Fratak-Shop.Web", "Fratak-Shop.Web.csproj"));
        var target = project.Descendants("Target").Single(t => (string?)t.Attribute("Name") == "FratakSkipLocalDataOnPublish");
        var condition = (string)target.Descendants("_FratakLocalDataFile").Single().Attribute("Condition")!;
        var pattern = Regex.Match(condition, @"RelativePath\)', '(?<p>[^']+)'").Groups["p"].Value;

        Assert.False(string.IsNullOrEmpty(pattern));
        Assert.Equal(dropped, Regex.IsMatch(relativePath, pattern));
        // تنظیمات توسعه (با رشتهٔ اتصال) هرگز منتشر نمی‌شود.
        Assert.Contains(project.Descendants("Content"), c => (string?)c.Attribute("Update") == "appsettings.Development.json"
                                                             && (string?)c.Attribute("CopyToPublishDirectory") == "Never");
    }

    // ═══ آزمون‌ها هرگز به پایگاه دادهٔ واقعی وصل نمی‌شوند ═══

    [Theory]
    [InlineData(@"Server=.;User Id=sa;Password=1", true)]
    [InlineData(@"Server=(local);Integrated Security=True", true)]
    [InlineData(@"Server=localhost,1433;User Id=sa;Password=1", true)]
    [InlineData(@"Server=tcp:127.0.0.1,1433;User Id=sa;Password=1", true)]
    [InlineData(@"Server=.\SQLEXPRESS;Integrated Security=True", true)]
    [InlineData(@"Server=(localdb)\MSSQLLocalDB;Integrated Security=True", true)]
    [InlineData(@"Server=185.191.78.226,2019;Database=shop;User Id=u;Password=p", false)]
    [InlineData(@"Server=db.example.com;User Id=u;Password=p", false)]
    [InlineData(@"not a connection string", false)]
    public void Test_sql_discovery_only_uses_local_servers(string connection, bool local)
        => Assert.Equal(local, TestSql.IsLocalServer(connection));

    private static string TempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "fratak-r17-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Fratak-Shop.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }
}
