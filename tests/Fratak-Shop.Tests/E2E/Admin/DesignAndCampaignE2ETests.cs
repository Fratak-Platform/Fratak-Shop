using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Fratak_Shop.Data.Entities.Design;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Fratak_Shop.Tests.E2E.Admin;

/// <summary>
/// دور ۲۱ از دید مرورگر (برنامهٔ واقعی + SQL Server):
/// <list type="bullet">
/// <item>صفحهٔ کمپین عمومی: فعال ← ۲۰۰ با ظاهر صفحه‌ساز؛ تمام‌شده ← پیام پایان + noindex (بدون فوریت جعلی)؛ غیرفعال/آینده/نامک فارسی ← ۴۰۴؛ هدر خاموش.</item>
/// <item>صفحه‌ساز: ساخت از قالب ← ویرایشگر؛ دادهٔ پروژه؛ ذخیرهٔ XHR با اسکریپت و فرم (پاک می‌شود) و CSS (محدود می‌شود)؛ تاریخ پایان قبل از شروع ← ۴۰۰.</item>
/// <item>استودیوی طراحی: طرح تازه ← ویرایشگر؛ دادهٔ بوم و قالب؛ ذخیرهٔ PNG ← WebP؛ انتشار با لینک خطرناک رد و با لینک داخلی ← بنر فعال.</item>
/// <item>دسترسی: حسابدار به صفحه‌ساز و استودیو راه ندارد؛ فرم بدون توکن CSRF رد می‌شود.</item>
/// </list>
/// همهٔ رکوردهای ساخته‌شده در پایان پاک می‌شوند.
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Design")]
public class DesignAndCampaignE2ETests(ShopApp app)
{
    private static string NewSlug() => "e2e-camp-" + Guid.NewGuid().ToString("N")[..8];

    private async Task<Guid> AddLandingAsync(string slug, bool active = true, DateTimeOffset? start = null, DateTimeOffset? end = null,
        bool header = true, string? css = null)
    {
        var page = new LandingPage
        {
            Slug = slug,
            Title = "کمپین آزمون " + slug,
            Html = "<section class=\"fs-lp-hero\"><div class=\"fs-lp-wrap\"><h1 class=\"fs-lp-title\">حراج آزمون</h1></div></section>",
            Css = css ?? ".fs-lp .fs-lp-title{color:#123456}",
            IsActive = active,
            StartAt = start,
            EndAt = end,
            ShowHeader = header,
            ShowFooter = true,
            TemplateKey = "campaign",
        };
        await app.WithDbAsync(async db =>
        {
            db.LandingPages.Add(page);
            await db.SaveChangesAsync();
        });
        return page.Id;
    }

    private Task DeleteLandingAsync(Guid id) => app.WithDbAsync(db => db.LandingPages.IgnoreQueryFilters().Where(p => p.Id == id).ExecuteDeleteAsync());

    // ───────────────────────────── صفحهٔ عمومی کمپین ─────────────────────────────

    [SqlFact]
    public async Task Active_campaign_page_renders_the_builder_design()
    {
        var slug = NewSlug();
        var id = await AddLandingAsync(slug, end: DateTimeOffset.UtcNow.AddDays(3));
        try
        {
            using var client = app.CreateClient();
            var page = await client.GetPageAsync($"/campaign/{slug}");
            Assert.NotNull(page.One("main.fs-lp section.fs-lp-hero h1.fs-lp-title"));
            Assert.Contains("حراج آزمون", page.Text, StringComparison.Ordinal);
            Assert.Contains(".fs-lp .fs-lp-title{color:#123456}", page.Html, StringComparison.Ordinal);
            Assert.DoesNotContain("noindex", page.Html, StringComparison.OrdinalIgnoreCase);
            // شمارش معکوس با پایان واقعی صفحه.
            Assert.NotEqual("none", page.One("main.fs-lp")!.GetAttribute("data-fs-end"));
            Assert.Contains("/assets/js/landing.js", page.Html, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteLandingAsync(id);
        }
    }

    [SqlFact]
    public async Task Ended_campaign_says_so_and_is_noindex_without_old_offers()
    {
        var slug = NewSlug();
        var id = await AddLandingAsync(slug, start: DateTimeOffset.UtcNow.AddDays(-10), end: DateTimeOffset.UtcNow.AddMinutes(-5));
        try
        {
            using var client = app.CreateClient();
            var page = await client.GetPageAsync($"/campaign/{slug}");
            Assert.Contains("به پایان رسیده", page.Text, StringComparison.Ordinal);
            Assert.Contains("noindex", page.Html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("حراج آزمون", page.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("#123456", page.Html, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteLandingAsync(id);
        }
    }

    [SqlFact]
    public async Task Draft_future_and_unknown_campaigns_are_not_found()
    {
        var draft = NewSlug();
        var future = NewSlug();
        var ids = new[]
        {
            await AddLandingAsync(draft, active: false),
            await AddLandingAsync(future, start: DateTimeOffset.UtcNow.AddDays(2)),
        };
        try
        {
            using var client = app.CreateClient();
            foreach (var url in new[] { $"/campaign/{draft}", $"/campaign/{future}", "/campaign/no-such-campaign-e2e", "/campaign/%DA%A9%D9%85%D9%BE%DB%8C%D9%86" })
            {
                using var response = await client.GetAsync(url);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }
        }
        finally
        {
            foreach (var id in ids)
                await DeleteLandingAsync(id);
        }
    }

    [SqlFact]
    public async Task Campaign_without_header_hides_the_site_header()
    {
        var slug = NewSlug();
        var id = await AddLandingAsync(slug, header: false);
        try
        {
            using var client = app.CreateClient();
            var page = await client.GetPageAsync($"/campaign/{slug}");
            Assert.Contains("fs-no-header", page.One("body")!.GetAttribute("class") ?? "", StringComparison.Ordinal);
            Assert.Equal("none", page.One("main.fs-lp")!.GetAttribute("data-fs-end"));
        }
        finally
        {
            await DeleteLandingAsync(id);
        }
    }

    // ───────────────────────────── صفحه‌ساز (پنل) ─────────────────────────────

    [SqlFact]
    public async Task Owner_builds_saves_and_publishes_a_campaign_page_safely()
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);
        Guid? id = null;
        try
        {
            var list = await client.GetPageAsync("/admin/campaigns");
            Assert.NotNull(list.One(".a-page-head"));

            using (var create = await client.PostFormAsync("/admin/campaigns?handler=Create", ("template", "campaign"), ("title", "حراج آزمون پنل")))
            {
                Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
                var location = create.Headers.Location!.OriginalString;
                Assert.StartsWith("/admin/campaigns/edit/", location, StringComparison.Ordinal);
                id = Guid.Parse(location["/admin/campaigns/edit/".Length..]);
            }

            var created = await app.WithDbAsync(db => db.LandingPages.SingleAsync(p => p.Id == id));
            Assert.False(created.IsActive); // پیش‌نویس تا مدیر منتشر کند
            Assert.NotNull(created.EndAt);  // قالب حراج یک هفته پایان پیش‌فرض دارد
            Assert.Matches("^[a-z0-9-]+$", created.Slug);

            var editor = await client.GetPageAsync($"/admin/campaigns/edit/{id}");
            Assert.NotNull(editor.One("[data-page-builder]"));
            Assert.Contains("grapes.min.js", editor.Html, StringComparison.Ordinal);

            using (var project = await client.GetAsync($"/admin/campaigns/edit/{id}?handler=Project"))
            {
                Assert.Equal(HttpStatusCode.OK, project.StatusCode);
                using var json = JsonDocument.Parse(await project.Content.ReadAsStringAsync());
                Assert.Contains("[[deals:8]]", json.RootElement.GetProperty("html").GetString(), StringComparison.Ordinal);
            }
            using (var template = await client.GetAsync($"/admin/campaigns/edit/{id}?handler=Template&key=launch"))
            {
                using var json = JsonDocument.Parse(await template.Content.ReadAsStringAsync());
                Assert.Contains("[[newest:8]]", json.RootElement.GetProperty("html").GetString(), StringComparison.Ordinal);
            }

            var slug = NewSlug();
            using (var save = await client.PostFormAsync($"/admin/campaigns/edit/{id}?handler=Save",
                       ("title", "حراج آزمون پنل"), ("slug", slug), ("metaDescription", "تخفیف واقعی"),
                       ("project", "{\"pages\":[]}"),
                       ("html", "<body><section id=\"i3k9\" class=\"fs-lp-section\"><h2 onclick=\"alert(1)\">سلام</h2><script>alert(1)</script>" +
                                "<form action=\"https://evil.com\"><input name=\"card\"></form></section></body>"),
                       ("css", "#i3k9{padding:20px} @import url(https://evil.com/x.css); .x{background:url(javascript:alert(1))}"),
                       ("showHeader", "true"), ("showFooter", "false"), ("isActive", "true"), ("startAt", ""), ("endAt", "")))
            {
                var body = await save.Content.ReadAsStringAsync();
                Assert.True(save.StatusCode == HttpStatusCode.OK, body);
                using var json = JsonDocument.Parse(body);
                Assert.Equal(slug, json.RootElement.GetProperty("slug").GetString());
            }

            var saved = await app.WithDbAsync(db => db.LandingPages.SingleAsync(p => p.Id == id));
            Assert.True(saved.IsActive);
            Assert.False(saved.ShowFooter);
            Assert.Null(saved.EndAt);
            Assert.DoesNotContain("<script", saved.Html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("onclick", saved.Html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<form", saved.Html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("id=\"i3k9\"", saved.Html, StringComparison.Ordinal);
            Assert.Contains("class=\"fs-lp-section\"", saved.Html, StringComparison.Ordinal);
            Assert.Equal(".fs-lp #i3k9{padding:20px}", saved.Css);
            Assert.Equal("{\"pages\":[]}", saved.ProjectJson);

            using var visitor = app.CreateClient();
            var publicPage = await visitor.GetPageAsync($"/campaign/{slug}");
            Assert.NotNull(publicPage.One("main.fs-lp #i3k9.fs-lp-section"));
            Assert.Contains("fs-no-footer", publicPage.One("body")!.GetAttribute("class") ?? "", StringComparison.Ordinal);

            // پایان قبل از شروع ← خطا و هیچ تغییری ذخیره نمی‌شود.
            using (var bad = await client.PostFormAsync($"/admin/campaigns/edit/{id}?handler=Save",
                       ("title", "x"), ("slug", slug), ("html", "<p>x</p>"), ("isActive", "true"),
                       ("startAt", "1405/09/10 10:00"), ("endAt", "1405/09/01 10:00")))
                Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            using (var badDate = await client.PostFormAsync($"/admin/campaigns/edit/{id}?handler=Save",
                       ("title", "x"), ("html", "<p>x</p>"), ("endAt", "not-a-date")))
                Assert.Equal(HttpStatusCode.BadRequest, badDate.StatusCode);
            Assert.Equal("حراج آزمون پنل", (await app.WithDbAsync(db => db.LandingPages.SingleAsync(p => p.Id == id))).Title);
        }
        finally
        {
            if (id is { } created)
                await DeleteLandingAsync(created);
        }
    }

    [SqlFact]
    public async Task Builder_save_without_csrf_token_is_rejected()
    {
        var slug = NewSlug();
        var id = await AddLandingAsync(slug, active: false);
        try
        {
            using var client = app.CreateClient();
            await client.LoginAsync(ShopApp.OwnerPhone);
            using var response = await client.PostRawFormAsync($"/admin/campaigns/edit/{id}?handler=Save", ("title", "هک"), ("isActive", "true"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var row = await app.WithDbAsync(db => db.LandingPages.SingleAsync(p => p.Id == id));
            Assert.False(row.IsActive);
            Assert.NotEqual("هک", row.Title);
        }
        finally
        {
            await DeleteLandingAsync(id);
        }
    }

    [SqlTheory]
    [InlineData("/admin/campaigns")]
    [InlineData("/admin/design")]
    public async Task Accountant_cannot_open_marketing_design_tools(string url)
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.AccountantPhone);
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/admin/denied", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [SqlFact]
    public async Task Owner_can_open_the_growth_report()
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);
        var page = await client.GetPageAsync("/admin/smart/growth");
        Assert.Contains("رشد خودکار", page.Text, StringComparison.Ordinal);
        Assert.NotNull(page.One(".a-page-head"));
    }

    // ───────────────────────────── استودیوی طراحی ─────────────────────────────

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(14, 165, 233));
            using var paint = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(width / 4f, height / 4f, width / 2f, height / 2f, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [SqlFact]
    public async Task Owner_designs_saves_and_publishes_a_banner()
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);
        Guid? designId = null;
        Guid? bannerId = null;
        try
        {
            var index = await client.GetPageAsync("/admin/design");
            Assert.NotNull(index.One(".a-page-head"));

            using (var create = await client.PostFormAsync("/admin/design?handler=Create", ("preset", "wide"), ("layout", "sale"), ("palette", "sunset"), ("title", "بنر آزمون")))
            {
                Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
                var location = create.Headers.Location!.OriginalString;
                Assert.StartsWith("/admin/design/editor/", location, StringComparison.Ordinal);
                designId = Guid.Parse(location["/admin/design/editor/".Length..].Split('?')[0]);
            }

            var editor = await client.GetPageAsync($"/admin/design/editor/{designId}");
            Assert.NotNull(editor.One("[data-design-studio]"));
            Assert.Contains("fabric.min.js", editor.Html, StringComparison.Ordinal);

            string json;
            using (var doc = await client.GetAsync($"/admin/design/editor/{designId}?handler=Doc"))
            {
                using var parsed = JsonDocument.Parse(await doc.Content.ReadAsStringAsync());
                Assert.Equal(1600, parsed.RootElement.GetProperty("width").GetInt32());
                Assert.Equal(400, parsed.RootElement.GetProperty("height").GetInt32());
                json = parsed.RootElement.GetProperty("json").GetString()!;
                Assert.Contains("\"fsRole\":\"headline\"", json, StringComparison.Ordinal);
            }
            using (var template = await client.GetAsync($"/admin/design/editor/{designId}?handler=Template&layout=festive&palette=gold"))
            {
                using var parsed = JsonDocument.Parse(await template.Content.ReadAsStringAsync());
                Assert.Equal(1600, parsed.RootElement.GetProperty("fsWidth").GetInt32());
            }

            // ذخیره: JSON بوم + PNG ساخت مرورگر ← WebP در پوشهٔ بنرها.
            var token = await client.AntiforgeryTokenAsync();
            using (var form = new MultipartFormDataContent())
            {
                form.Add(new StringContent(token), "__RequestVerificationToken");
                form.Add(new StringContent("بنر آزمون"), "title");
                form.Add(new StringContent(json), "json");
                form.Add(new StringContent("حراج ویژه"), "headline");
                var png = new ByteArrayContent(Png(1600, 400));
                png.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                form.Add(png, "image", "design.png");
                using var save = await client.Http.PostAsync($"/admin/design/editor/{designId}?handler=Save", form);
                var body = await save.Content.ReadAsStringAsync();
                Assert.True(save.StatusCode == HttpStatusCode.OK, body);
                Assert.Contains("/uploads/banners/", body, StringComparison.Ordinal);
            }
            var saved = await app.WithDbAsync(db => db.DesignDocs.SingleAsync(d => d.Id == designId));
            Assert.EndsWith(".webp", saved.ImageName, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("حراج ویژه", saved.Headline);

            // لینک خطرناک ← رد؛ لینک داخلی ← بنر فعال در جایگاه «بنر عریض».
            using (var evil = await client.PostFormAsync($"/admin/design/editor/{designId}?handler=Publish",
                       ("position", nameof(BannerPosition.WideBanner)), ("link", "javascript:alert(1)"), ("button", "خرید")))
                Assert.Equal(HttpStatusCode.BadRequest, evil.StatusCode);
            using (var publish = await client.PostFormAsync($"/admin/design/editor/{designId}?handler=Publish",
                       ("position", nameof(BannerPosition.WideBanner)), ("link", "/products"), ("button", "خرید")))
            {
                var body = await publish.Content.ReadAsStringAsync();
                Assert.True(publish.StatusCode == HttpStatusCode.OK, body);
            }
            var published = await app.WithDbAsync(db => db.DesignDocs.SingleAsync(d => d.Id == designId));
            Assert.Equal(DesignStatus.Published, published.Status);
            bannerId = published.BannerId;
            var banner = await app.WithDbAsync(db => db.Banners.SingleAsync(b => b.Id == published.BannerId));
            Assert.Equal(BannerPosition.WideBanner, banner.Position);
            Assert.Equal("/products", banner.LinkUrl);
            Assert.Equal(saved.ImageName, banner.ImageName);
        }
        finally
        {
            if (bannerId is { } b)
                await app.WithDbAsync(db => db.Banners.Where(x => x.Id == b).ExecuteDeleteAsync());
            if (designId is { } d)
                await app.WithDbAsync(db => db.DesignDocs.Where(x => x.Id == d).ExecuteDeleteAsync());
        }
    }

    [SqlFact]
    public async Task Design_save_rejects_a_fake_image()
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);
        Guid? designId = null;
        try
        {
            using (var create = await client.PostFormAsync("/admin/design?handler=Create", ("preset", "blog-cover"), ("layout", "blank")))
                designId = Guid.Parse(create.Headers.Location!.OriginalString["/admin/design/editor/".Length..].Split('?')[0]);

            var token = await client.AntiforgeryTokenAsync();
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(token), "__RequestVerificationToken");
            form.Add(new StringContent("{}"), "json");
            var fake = new ByteArrayContent("<?php echo 1; ?>"u8.ToArray());
            fake.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(fake, "image", "shell.png");
            using var save = await client.Http.PostAsync($"/admin/design/editor/{designId}?handler=Save", form);
            Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
            Assert.Null((await app.WithDbAsync(db => db.DesignDocs.SingleAsync(x => x.Id == designId))).ImageName);
        }
        finally
        {
            if (designId is { } d)
                await app.WithDbAsync(db => db.DesignDocs.Where(x => x.Id == d).ExecuteDeleteAsync());
        }
    }
}
