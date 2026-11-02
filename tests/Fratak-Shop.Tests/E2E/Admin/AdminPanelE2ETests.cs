using System.Net;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.E2E.Admin;

/// <summary>
/// پنل مدیریت: مهمان به ورود فرستاده می‌شود، مشتری اصلاً پنل را نمی‌بیند (404)، هر نقش فقط بخش‌های مجاز خودش را
/// می‌بیند (بقیه → صفحهٔ «دسترسی ندارید» + ثبت رویداد امنیتی)، فرم‌ها بدون توکن CSRF رد می‌شوند و خزندهٔ مدیر کل
/// همهٔ صفحه‌های پنل را بدون حتی یک خطای سرور باز می‌کند.
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Admin")]
public class AdminPanelE2ETests(ShopApp app)
{
    [SqlTheory]
    [InlineData("/admin")]
    [InlineData("/admin/orders")]
    [InlineData("/admin/settings")]
    [InlineData("/admin/staff")]
    public async Task Guests_are_sent_to_login(string url)
    {
        using var client = app.CreateClient();

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [SqlTheory]
    [InlineData("/admin")]
    [InlineData("/admin/orders")]
    [InlineData("/admin/customers")]
    [InlineData("/admin/finance")]
    public async Task Customers_do_not_even_see_that_the_panel_exists(string url)
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(customer.Phone, customer.Password);

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SqlTheory]
    [InlineData(ShopApp.WriterPhone, "/admin/orders")]
    [InlineData(ShopApp.WriterPhone, "/admin/finance")]
    [InlineData(ShopApp.WriterPhone, "/admin/staff")]
    [InlineData(ShopApp.SeoPhone, "/admin/finance")]
    [InlineData(ShopApp.SeoPhone, "/admin/system/firewall")]
    [InlineData(ShopApp.AccountantPhone, "/admin/staff")]
    [InlineData(ShopApp.AccountantPhone, "/admin/settings")]
    public async Task Staff_cannot_open_sections_outside_their_role(string phone, string url)
    {
        using var client = app.CreateClient();
        await client.LoginAsync(phone);

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/admin/denied", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.True(await app.WithDbAsync(db => db.SecurityLogs.AnyAsync(e => e.Type == SecurityEventType.AdminAccessDenied && e.Details!.Contains(url))));
    }

    [SqlTheory]
    [InlineData(ShopApp.WriterPhone, "/admin/blog")]
    [InlineData(ShopApp.SeoPhone, "/admin/seo")]
    [InlineData(ShopApp.AccountantPhone, "/admin/finance")]
    [InlineData(ShopApp.SalesManagerPhone, "/admin/orders")]
    [InlineData(ShopApp.OwnerPhone, "/admin/staff")]
    public async Task Staff_can_open_their_own_sections(string phone, string url)
    {
        using var client = app.CreateClient();
        await client.LoginAsync(phone);

        var page = await client.GetPageAsync(url);

        Assert.Contains("noindex", page.Response.Headers.TryGetValues("X-Robots-Tag", out var robots) ? string.Join(",", robots) : "", StringComparison.Ordinal);
        Assert.Contains("no-store", page.Response.Headers.CacheControl?.ToString() ?? "", StringComparison.Ordinal);
    }

    [SqlFact]
    public async Task Denied_staff_post_changes_nothing()
    {
        var before = await app.WithDbAsync(db => db.StoreSettings.Select(s => s.SiteName).FirstAsync());
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.WriterPhone);

        using var response = await client.PostFormAsync("/admin/settings", ("Input.SiteName", "HACKED"), ("SiteName", "HACKED"));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, await app.WithDbAsync(db => db.StoreSettings.Select(s => s.SiteName).FirstAsync()));
    }

    /// <summary>
    /// مرحلهٔ چهاردهم و هفدهم: هر دو مدیر پیش‌فرض appsettings.json (Shop:DefaultAdmins: 09913181090 با Key «owner» و
    /// 09134426156 با Key «partner»؛ رمز اولیهٔ 12345678) در اولین اجرا با رمز ساخته شده‌اند، با همان رمز وارد می‌شوند،
    /// به صفحهٔ تغییر رمز فرستاده می‌شوند و پنل را می‌بینند (علت «مدیر دوم دسترسی ندارد» دیگر تکرار نمی‌شود).
    /// </summary>
    [SqlTheory]
    [InlineData("09913181090", "محمدامین")]
    [InlineData("09134426156", "محمد حسن")]
    public async Task Default_admin_from_config_signs_in_with_the_initial_password_and_is_asked_to_change_it(string phone, string firstName)
    {
        var account = await app.WithDbAsync(db => db.Users.Where(u => u.PhoneNumber == phone)
            .Select(u => new { u.Role, u.PasswordHash, u.FirstName, u.IsPhoneVerified, u.SystemAdminKey }).FirstAsync());
        Assert.Equal(global::Fratak_Shop.Data.Entities.Users.UserRole.SuperAdmin, account.Role);
        Assert.Equal(firstName, account.FirstName);
        Assert.True(account.IsPhoneVerified);
        Assert.StartsWith("key:", account.SystemAdminKey);
        Assert.False(string.IsNullOrEmpty(account.PasswordHash));
        Assert.DoesNotContain("12345678", account.PasswordHash, StringComparison.Ordinal);

        using var client = app.CreateClient();
        using var login = await client.PostFormAsync("/login",
            ("Input.PhoneNumber", phone), ("Input.Password", "12345678"), ("Input.RememberMe", "false"));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/profile/password", login.Headers.Location?.OriginalString.ToLowerInvariant());

        using var admin = await client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        using var access = await client.GetAsync("/admin/system/access");
        Assert.Equal(HttpStatusCode.OK, access.StatusCode);
    }

    [SqlFact]
    public async Task Admin_forms_without_a_csrf_token_are_rejected()
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);

        using var noToken = await client.PostRawFormAsync("/admin/settings", ("Input.SiteName", "CSRF"));
        using var fakeToken = await client.PostRawFormAsync("/admin/settings", ("Input.SiteName", "CSRF"), ("__RequestVerificationToken", "forged"));

        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, fakeToken.StatusCode);
        Assert.NotEqual("CSRF", await app.WithDbAsync(db => db.StoreSettings.Select(s => s.SiteName).FirstAsync()));
    }

    [SqlFact]
    public async Task Csrf_token_of_one_user_does_not_work_for_another()
    {
        using var attacker = app.CreateClient();
        await attacker.LoginAsync(ShopApp.WriterPhone);
        var attackerToken = await attacker.AntiforgeryTokenAsync();
        using var owner = app.CreateClient();
        await owner.LoginAsync(ShopApp.OwnerPhone);

        using var response = await owner.PostRawFormAsync("/admin/settings", ("Input.SiteName", "CSRF2"), ("__RequestVerificationToken", attackerToken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// مدیر کل همهٔ لینک‌های پنل را باز می‌کند (فقط GET؛ handler و خروج کنار گذاشته می‌شوند): هیچ 5xx و هیچ استثنایی.
    /// </summary>
    [SqlFact]
    public async Task Owner_can_open_every_admin_page_without_a_server_error()
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);
        var started = DateTimeOffset.UtcNow;
        var queue = new Queue<string>(["/admin"]);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var failures = new List<string>();

        while (queue.Count > 0 && seen.Count < 400)
        {
            var url = queue.Dequeue();
            if (!seen.Add(url))
                continue;

            using var response = await client.GetAsync(url);
            if ((int)response.StatusCode >= 500)
                failures.Add($"{(int)response.StatusCode} {url}");
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentType?.MediaType != "text/html")
                continue;

            var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync());
            foreach (var href in document.QuerySelectorAll("a[href]").Select(a => a.GetAttribute("href") ?? ""))
            {
                var path = href.Split('#')[0];
                if (!path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) || path.Contains("handler=", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("logout", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!seen.Contains(path))
                    queue.Enqueue(path);
            }
        }

        Assert.True(seen.Count >= 40, $"The admin crawler only reached {seen.Count} pages.");
        Assert.Empty(failures);
        Assert.Empty(app.Logs.ErrorsSince(started));
    }

    [SqlTheory]
    [InlineData(ShopApp.SalesManagerPhone)]
    [InlineData(ShopApp.SeoPhone)]
    [InlineData(ShopApp.WriterPhone)]
    [InlineData(ShopApp.AccountantPhone)]
    public async Task Every_role_dashboard_and_visible_links_work(string phone)
    {
        using var client = app.CreateClient();
        await client.LoginAsync(phone);
        var started = DateTimeOffset.UtcNow;

        var dashboard = await client.GetPageAsync("/admin");
        var links = dashboard.All("a[href^='/admin']").Select(a => a.GetAttribute("href")!.Split('#')[0])
            .Where(h => !h.Contains("handler=", StringComparison.Ordinal) && !h.Contains("logout", StringComparison.Ordinal))
            .Distinct().Take(80).ToList();

        var denied = new List<string>();
        foreach (var link in links)
        {
            using var response = await client.GetAsync(link);
            Assert.True((int)response.StatusCode < 500, $"{(int)response.StatusCode} {link}");
            if (response.Headers.Location?.OriginalString.StartsWith("/admin/denied", StringComparison.OrdinalIgnoreCase) == true)
                denied.Add(link);
        }

        // منو فقط چیزهایی را نشان می‌دهد که نقش اجازه‌اش را دارد.
        Assert.Empty(denied);
        Assert.Empty(app.Logs.ErrorsSince(started));
    }

    [SqlFact]
    public async Task Blocking_a_customer_from_the_panel_ends_their_session_immediately()
    {
        var customer = await app.CreateCustomerAsync();
        using var shopper = app.CreateClient();
        await shopper.LoginAsync(customer.Phone, customer.Password);
        await shopper.GetPageAsync("/profile");

        var ownerId = await app.WithDbAsync(db => db.Users.Where(u => u.PhoneNumber == ShopApp.OwnerPhone).Select(u => u.Id).FirstAsync());
        await using (var scope = app.Scope())
        {
            var owner = await scope.ServiceProvider.GetRequiredService<IAdminAccessService>().GetAccessAsync(ownerId);
            var result = await scope.ServiceProvider.GetRequiredService<IAdminCustomerService>().SetBlockedAsync(customer.Id, true, "آزمون مسدودسازی", owner);
            Assert.True(result.IsSuccess(), result.Message);
        }

        await shopper.GetPageAsync("/profile", HttpStatusCode.Redirect);
    }
}
