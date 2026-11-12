using System.Net;
using Fratak_Shop.Core.Services.Ops;
using Fratak_Shop.Data.Entities.Ops;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.E2E.Security;

/// <summary>
/// لایهٔ HTTP زیر حمله: سرآیندهای امنیتی، اجبار HTTPS، سازگاری واقعی صفحه‌ها با CSP (هیچ اسکریپت/رویداد درون‌خطی)،
/// فایل‌های حساس، تزریق SQL و XSS در ورودی‌ها، پیمایش مسیر، ریدایرکت باز، فایروال IP و ربات، حالت تعمیرات و
/// محدودیت نرخ فرم‌ها. انتظار در همه: پاسخ کنترل‌شده (۴xx یا صفحهٔ عادی)، بدون ۵۰۰ و بدون نشت اطلاعات.
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Category", "Security")]
[Trait("Area", "Http")]
public class HttpSecurityE2ETests(ShopApp app)
{
    [SqlTheory]
    [InlineData("/")]
    [InlineData("/product/xiaomi-redmi-watch-3-active")]
    [InlineData("/login")]
    [InlineData("/no-such-page")]
    [InlineData("/api/search/suggest?q=a")]
    public async Task Every_response_carries_the_security_headers(string url)
    {
        using var client = app.CreateClient();

        using var response = await client.GetAsync(url);
        var headers = response.Headers;

        Assert.Equal("nosniff", string.Join("", headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("SAMEORIGIN", string.Join("", headers.GetValues("X-Frame-Options")));
        Assert.Equal("strict-origin-when-cross-origin", string.Join("", headers.GetValues("Referrer-Policy")));
        Assert.Equal("same-origin", string.Join("", headers.GetValues("Cross-Origin-Opener-Policy")));
        Assert.True(headers.Contains("Permissions-Policy"));
        var csp = string.Join("", headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", csp, StringComparison.Ordinal);
        // دور ۳۰: فقط 'wasm-unsafe-eval' (کامپایل WebAssembly) مجاز است؛ eval جاوااسکریپت و inline نه.
        Assert.DoesNotContain("'unsafe-eval'", csp, StringComparison.Ordinal);
        Assert.Contains("script-src 'self' 'wasm-unsafe-eval';", csp, StringComparison.Ordinal);
        Assert.False(headers.Contains("Server"));
        Assert.False(headers.Contains("X-Powered-By"));
        Assert.True(headers.Contains("Strict-Transport-Security"), "HSTS is missing in production.");
    }

    [SqlFact]
    public async Task Plain_http_is_redirected_to_https()
    {
        using var client = app.CreateClient();

        using var response = await client.Http.GetAsync("http://fratak.test/products");

        Assert.True((int)response.StatusCode is 301 or 307 or 308, $"status {(int)response.StatusCode}");
        Assert.StartsWith("https://", response.Headers.Location?.ToString(), StringComparison.Ordinal);
    }

    [SqlFact]
    public async Task Antiforgery_cookie_is_http_only_secure_and_same_site()
    {
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/api/antiforgery");

        var cookie = Assert.Single(ShopClient.SetCookies(response), c => c.StartsWith("fratak_af=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// CSP اسکریپت درون‌خطی و onclick را در مرورگر می‌بندد؛ پس اگر صفحه‌ای چنین چیزی داشته باشد، آن قابلیت در مرورگر واقعی
    /// خراب است. این آزمون صفحه‌های اصلی فروشگاه و پنل را بررسی می‌کند (JSON-LD داده است و مجاز).
    /// </summary>
    [SqlTheory]
    [InlineData(null, "/")]
    [InlineData(null, "/product/xiaomi-redmi-watch-3-active")]
    [InlineData(null, "/search?q=a")]
    [InlineData(null, "/blog/headphone-life-tips")]
    [InlineData(null, "/login")]
    [InlineData(null, "/cart")]
    [InlineData(ShopApp.DemoCustomerPhone, "/profile")]
    [InlineData(ShopApp.DemoCustomerPhone, "/profile/addresses")]
    [InlineData(ShopApp.DemoCustomerPhone, "/profile/wallet")]
    [InlineData(ShopApp.OwnerPhone, "/admin")]
    [InlineData(ShopApp.OwnerPhone, "/admin/orders")]
    [InlineData(ShopApp.OwnerPhone, "/admin/products")]
    [InlineData(ShopApp.OwnerPhone, "/admin/settings")]
    [InlineData(ShopApp.OwnerPhone, "/admin/seo")]
    public async Task Pages_contain_no_inline_script_or_event_handler_blocked_by_the_csp(string? phone, string url)
    {
        using var client = app.CreateClient();
        if (phone is not null)
            await client.LoginAsync(phone);

        var page = await client.GetPageAsync(url);

        var inlineScripts = page.All("script:not([src])")
            .Where(s => s.GetAttribute("type") is not ("application/ld+json" or "application/json" or "text/template"))
            .Select(s => s.OuterHtml[..Math.Min(120, s.OuterHtml.Length)]).ToList();
        Assert.True(inlineScripts.Count == 0, "Inline scripts: " + string.Join(" | ", inlineScripts));

        var handlers = page.All("*").SelectMany(e => e.Attributes.Where(a => a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            .Select(a => $"<{e.LocalName} {a.Name}>")).Distinct().ToList();
        Assert.True(handlers.Count == 0, "Inline event handlers: " + string.Join(", ", handlers));

        var javascriptUrls = page.All("a[href^='javascript:' i], form[action^='javascript:' i]").Count;
        Assert.Equal(0, javascriptUrls);
    }

    [SqlTheory]
    [InlineData("/appsettings.json")]
    [InlineData("/appsettings.Development.json")]
    [InlineData("/web.config")]
    [InlineData("/.git/config")]
    [InlineData("/.env")]
    [InlineData("/Fratak-Shop.Web.dll")]
    [InlineData("/assets/../appsettings.json")]
    [InlineData("/assets/%2e%2e/appsettings.json")]
    [InlineData("/uploads/..%2f..%2fappsettings.json")]
    [InlineData("/media/..%2f..%2fappsettings.json")]
    [InlineData("/Program.cs")]
    public async Task Server_files_and_secrets_are_never_served(string url)
    {
        using var client = app.CreateClient();

        using var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True((int)response.StatusCode < 500, $"{(int)response.StatusCode} {url}");
        Assert.DoesNotContain("ConnectionDefault", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SigningKey", body, StringComparison.Ordinal);
        Assert.DoesNotContain("[core]", body, StringComparison.Ordinal);
    }

    [SqlTheory]
    [InlineData("/search?q=' OR 1=1--")]
    [InlineData("/search?q=%27%3B%20DROP%20TABLE%20Users%3B--")]
    [InlineData("/search?sort=Name;DROP TABLE Products")]
    [InlineData("/search?minPrice=1 OR 1=1&maxPrice=abc")]
    [InlineData("/search?page=-1")]
    [InlineData("/search?page=99999999999")]
    [InlineData("/product/' OR '1'='1")]
    [InlineData("/category/1;WAITFOR DELAY '0:0:5'--")]
    [InlineData("/blog?page=1 UNION SELECT PasswordHash FROM Users")]
    [InlineData("/api/search/suggest?q=%' UNION SELECT PasswordHash, 1 FROM Users--")]
    public async Task Sql_injection_probes_are_harmless(string url)
    {
        using var client = app.CreateClient();
        var started = DateTimeOffset.UtcNow;
        var users = await app.WithDbAsync(db => db.Users.CountAsync());

        using var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True((int)response.StatusCode < 500, $"{(int)response.StatusCode} {url}");
        Assert.DoesNotContain("$argon2", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlException", body, StringComparison.Ordinal);
        Assert.Empty(app.Logs.ErrorsSince(started));
        Assert.True(await app.WithDbAsync(db => db.Users.CountAsync()) >= users);
    }

    [SqlTheory]
    [InlineData("/search?q=<script>alert(1)</script>")]
    [InlineData("/search?q=\"><img src=x onerror=alert(1)>")]
    [InlineData("/login?returnUrl=\"><script>alert(1)</script>")]
    [InlineData("/no-such-page?x=<script>alert(1)</script>")]
    public async Task Reflected_xss_payloads_are_encoded(string url)
    {
        using var client = app.CreateClient();

        using var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("<script>alert(1)</script>", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img src=x onerror=alert(1)>", body, StringComparison.OrdinalIgnoreCase);
    }

    [SqlTheory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example")]
    public async Task Form_return_urls_cannot_redirect_off_site(string returnUrl)
    {
        var (productId, _) = await app.CreateProductAsync();
        using var client = app.CreateClient();

        using var response = await client.PostFormAsync("/cart?handler=Add", ("productId", productId.ToString()), ("quantity", "1"), ("returnUrl", returnUrl));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.DoesNotContain("evil", location, StringComparison.OrdinalIgnoreCase);
    }

    [SqlFact]
    public async Task Forms_without_a_valid_csrf_token_are_rejected()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(customer.Phone, customer.Password);

        using var noToken = await client.PostRawFormAsync("/profile/info", ("Input.FirstName", "CSRF"));
        using var forged = await client.PostRawFormAsync("/profile/password",
            ("Input.CurrentPassword", customer.Password), ("Input.NewPassword", "Hijack123"), ("Input.ConfirmPassword", "Hijack123"),
            ("__RequestVerificationToken", "CfDJ8forgedforgedforged"));

        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        using var stillWorks = app.CreateClient();
        await stillWorks.LoginAsync(customer.Phone, customer.Password);
    }

    [SqlFact]
    public async Task Malformed_requests_get_client_errors_not_server_errors()
    {
        using var client = app.CreateClient();
        var started = DateTimeOffset.UtcNow;

        var probes = new List<HttpResponseMessage>
        {
            await client.GetAsync("/api/locations/provinces/not-a-guid/cities"),
            await client.GetAsync("/product/" + new string('a', 3000)),
            await client.GetAsync("/search?q=" + new string('ب', 5000)),
            await client.Http.SendAsync(new HttpRequestMessage(new HttpMethod("TRACE"), "/")),
            await client.Http.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/cart")),
            await client.PostJsonAsync("/api/auth/token", "not an object"),
            await client.Http.PostAsync("/api/auth/token", new StringContent("{broken json", System.Text.Encoding.UTF8, "application/json")),
            await client.PostRawFormAsync("/cart?handler=Add", ("productId", "'; DROP TABLE Carts;--")),
        };

        foreach (var response in probes)
        {
            Assert.True((int)response.StatusCode < 500, $"{(int)response.StatusCode} {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}");
            response.Dispose();
        }
        Assert.Empty(app.Logs.ErrorsSince(started));
    }

    [SqlFact]
    public async Task Blocked_ip_is_refused_everywhere_and_others_are_not_affected()
    {
        const string attackerIp = "10.250.0.66";
        Guid ruleId;
        await using (var scope = app.Scope())
        {
            var firewall = scope.ServiceProvider.GetRequiredService<IFirewallService>();
            var result = await firewall.AddIpRuleAsync(new IpRuleInput(attackerIp, IpRuleAction.Block, "آزمون حمله", 1), "آزمون", null);
            Assert.True(Fratak_Shop.Core.Utilities.OperationResultExtensions.IsSuccess(result), result.Message);
            await firewall.ReloadAsync();
            ruleId = await scope.ServiceProvider.GetRequiredService<Fratak_Shop.Data.Contexts.FratakShopDbContext>()
                .IpRules.Where(r => r.Value == attackerIp).Select(r => r.Id).FirstAsync();
        }

        try
        {
            using var attacker = app.CreateClient(ip: attackerIp);
            foreach (var url in new[] { "/", "/login", "/api/search/suggest?q=a", "/assets/css/output.css", "/admin" })
            {
                using var response = await attacker.GetAsync(url);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }

            using var visitor = app.CreateClient();
            await visitor.GetPageAsync("/");
        }
        finally
        {
            await using var scope = app.Scope();
            var firewall = scope.ServiceProvider.GetRequiredService<IFirewallService>();
            await firewall.DeleteIpRuleAsync(ruleId);
            await firewall.ReloadAsync();
        }

        using var released = app.CreateClient(ip: attackerIp);
        await released.GetPageAsync("/");
    }

    [SqlTheory]
    [InlineData("sqlmap/1.7.2#stable (https://sqlmap.org)")]
    [InlineData("Mozilla/5.00 (Nikto/2.1.6)")]
    [InlineData("Nuclei - Open-source project (github.com/projectdiscovery/nuclei)")]
    public async Task Known_attack_tools_are_blocked_by_user_agent(string userAgent)
    {
        await using (var scope = app.Scope())
            await scope.ServiceProvider.GetRequiredService<IFirewallService>().ReloadAsync();
        using var tool = app.CreateClient(userAgent: userAgent);

        using var response = await tool.GetAsync("/");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [SqlFact]
    public async Task Contact_form_flood_is_rate_limited()
    {
        using var client = app.CreateClient();
        var token = await client.AntiforgeryTokenAsync();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 25; i++)
        {
            using var response = await client.PostRawFormAsync("/contact-us",
                ("__RequestVerificationToken", token), ("Input.FullName", "ربات"), ("Input.PhoneNumber", "09121111111"),
                ("Input.Subject", "اسپم " + i), ("Input.Content", "پیام تکراری اسپم"));
            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, statuses);
    }

    [SqlFact]
    public async Task Sms_bombing_from_one_ip_triggers_the_captcha()
    {
        using var client = app.CreateClient();
        var phones = Enumerable.Range(0, 5).Select(_ => ShopApp.NewPhone()).ToList();

        foreach (var phone in phones)
        {
            using (await client.PostFormAsync("/login?handler=SendCode", ("Otp.PhoneNumber", phone)))
            {
            }
        }

        int sent;
        lock (app.Sms.Sent)
            sent = app.Sms.Sent.Count(s => phones.Contains(s.Phone));
        Assert.True(sent <= 3, $"{sent} SMS were sent from a single IP without a captcha.");
    }

    [SqlFact]
    public async Task Maintenance_mode_shows_503_to_visitors_but_keeps_login_and_admin_open()
    {
        var restore = await app.ChangeSettingsAsync(s => { s.IsMaintenanceMode = true; s.MaintenanceRetryMinutes = 30; });
        try
        {
            using var visitor = app.CreateClient();
            using var home = await visitor.GetAsync("/");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, home.StatusCode);
            Assert.NotNull(home.Headers.RetryAfter);
            using var health = await visitor.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            await visitor.GetPageAsync("/login");

            using var staff = app.CreateClient();
            await staff.LoginAsync(ShopApp.OwnerPhone);
            await staff.GetPageAsync("/admin");
            await staff.GetPageAsync("/");
        }
        finally
        {
            await restore();
        }

        using var after = app.CreateClient();
        await after.GetPageAsync("/");
    }

    [SqlFact]
    public async Task Error_pages_do_not_leak_stack_traces()
    {
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/product/%ff%fe%fd");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True((int)response.StatusCode < 500 || !body.Contains("   at ", StringComparison.Ordinal));
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Fratak_Shop.", body, StringComparison.Ordinal);
    }
}
