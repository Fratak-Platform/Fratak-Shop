using System.Net;
using System.Text;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.E2E.Storefront;

/// <summary>
/// تست سرتاسری دور ۲۸ – قابلیت‌های تازهٔ بخش اول:
/// <list type="bullet">
/// <item>#۱۱۹: نقطهٔ گزارش نقض CSP همیشه ۲۰۴ می‌دهد؛ بدنهٔ خیلی بزرگ ← ۴۱۳؛ JSON خراب سایت را نمی‌خواباند.</item>
/// <item>#۷۶: جست‌وجوی بی‌نتیجه ← کالای عبارت ساده‌تر نشان داده می‌شود.</item>
/// <item>#۱۳۱ و #۱۳۰: نوار پایین موبایل و لینک «پرش به محتوا» در صفحه‌ها هست؛ در صفحهٔ ورود نوار پایین نیست.</item>
/// <item>#۹۴ و #۶۰: داشبورد «کارهای امروز» دارد؛ صفحهٔ ویرایش پیش‌نویس کمبودهای «آمادهٔ انتشار» را می‌گوید.</item>
/// </list>
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Round28")]
public class Round28StorefrontE2ETests(ShopApp app)
{
    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/csp-report");

    [SqlFact]
    public async Task Csp_report_endpoint_accepts_reports_and_rejects_huge_bodies()
    {
        using var client = app.CreateClient();
        var report = "{\"csp-report\":{\"effective-directive\":\"img-src\",\"blocked-uri\":\"https://e2e-csp.example/a.png?token=1\",\"document-uri\":\"https://shop.fratak.ir/\"}}";
        using (var ok = await client.Http.PostAsync("/api/csp-report", Json(report)))
            Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        using (var broken = await client.Http.PostAsync("/api/csp-report", Json("{not json")))
            Assert.Equal(HttpStatusCode.NoContent, broken.StatusCode);
        using (var huge = await client.Http.PostAsync("/api/csp-report", Json(new string('a', 64 * 1024))))
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, huge.StatusCode);
        using (var get = await client.GetAsync("/api/csp-report"))
            Assert.NotEqual(HttpStatusCode.OK, get.StatusCode);

        // سایت همچنان سالم است.
        using var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
    }

    [SqlFact]
    public async Task Zero_result_search_shows_products_of_a_simpler_query()
    {
        var (id, slug) = await app.CreateProductAsync(stock: 5);
        var key = slug["e2e-".Length..];
        using var client = app.CreateClient();
        var page = await client.GetPageAsync("/search?q=" + Uri.EscapeDataString(key + " zqxvbnmwplk"));
        // یا خود جست‌وجو کالا را پیدا کرده یا بخش «نتایج نزدیک» آن را نشان می‌دهد؛ در هر حال مشتری دست خالی نمی‌ماند.
        Assert.Contains("/product/" + slug, page.Html, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(Guid.Empty, id);
    }

    [SqlFact]
    public async Task Bottom_nav_and_skip_link_are_present_but_hidden_on_login()
    {
        using var client = app.CreateClient();
        var home = await client.GetPageAsync("/");
        Assert.NotNull(home.One("nav.fs-bnav"));
        Assert.NotNull(home.One("a.fs-skip[href='#fs-main']"));
        Assert.NotNull(home.One("#fs-main"));
        Assert.Equal("page", home.One("nav.fs-bnav a[href='/']")?.GetAttribute("aria-current"));

        var login = await client.GetPageAsync("/login");
        Assert.Null(login.One("nav.fs-bnav"));
    }

    [SqlFact]
    public async Task Dashboard_has_today_tasks_and_draft_edit_lists_ready_gaps()
    {
        var (draftId, _) = await app.CreateProductAsync(stock: 2, active: false);
        await app.WithDbAsync(db => db.Products.Where(p => p.Id == draftId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PublishedAt, (DateTimeOffset?)null).SetProperty(p => p.EnglishName, (string?)null)));

        using var owner = app.CreateClient();
        await owner.LoginAsync(ShopApp.OwnerPhone);
        var dashboard = await owner.GetPageAsync("/admin");
        Assert.NotNull(dashboard.One("section.a-today"));

        var edit = await owner.GetPageAsync($"/admin/products/edit/{draftId}");
        var gaps = edit.One("[data-ready-gaps]");
        Assert.NotNull(gaps);
        Assert.Contains("عکس", gaps!.TextContent, StringComparison.Ordinal);
        Assert.Contains("نام انگلیسی", gaps.TextContent, StringComparison.Ordinal);
    }
}
