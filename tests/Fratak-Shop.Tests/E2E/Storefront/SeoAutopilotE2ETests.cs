using System.Net;
using System.Text;
using System.Text.Json;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.E2E.Storefront;

/// <summary>
/// سئوی خودکار از بیرون (مرورگر، ربات ترب، گوگل):
/// • فید محصولات و API ترب: روشن/خاموش، توکن نامعتبر ← 401، کلید IndexNow جعلی ← 404.
/// • صفحهٔ محصول متاتگ‌های ترب و تصویر اشتراک WebP دارد؛ هیچ کامنت HTML و هیچ تصویر بی‌alt در خروجی نیست.
/// • صفحه‌های مدیریت «سئوی خودکار» برای مالک باز می‌شوند و «بررسی زنده» JSON درست برمی‌گرداند؛ مهمان راه ندارد.
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "SeoAutopilot")]
public class SeoAutopilotE2ETests(ShopApp app)
{
    private const string ProductUrl = "/product/xiaomi-redmi-watch-3-active";

    private async Task SetFeedsAsync(bool enabled)
    {
        await using var scope = app.Scope();
        var autopilot = scope.ServiceProvider.GetRequiredService<ISeoAutopilotService>();
        var options = await autopilot.GetOptionsAsync();
        options.ProductFeeds = enabled;
        await autopilot.SaveOptionsAsync(options);
        scope.ServiceProvider.GetRequiredService<ISeoLinkTargetProvider>().Invalidate();
    }

    [SqlFact]
    public async Task Product_feed_is_valid_xml_and_disappears_when_switched_off()
    {
        using var client = app.CreateClient();

        using (var response = await client.GetAsync("/feeds/products.xml"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var xml = System.Xml.Linq.XDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.NotEmpty(xml.Descendants("item"));
            Assert.All(xml.Descendants("link").Skip(1), l => Assert.Contains("/product/", l.Value, StringComparison.Ordinal));
        }

        await SetFeedsAsync(false);
        try
        {
            using var off = await client.GetAsync("/feeds/products.xml");
            Assert.Equal(HttpStatusCode.NotFound, off.StatusCode);
            using var torob = await client.Http.PostAsync("/torob_api/v3/products", new StringContent("{\"page\":1,\"sort\":\"date_added_desc\"}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.NotFound, torob.StatusCode);
        }
        finally
        {
            await SetFeedsAsync(true);
        }
    }

    [SqlTheory]
    [InlineData(null, null)]
    [InlineData("not-a-jwt", "1")]
    [InlineData("eyJhbGciOiJub25lIn0.eyJhdWQiOiJmcmF0YWsudGVzdCJ9.", "1")]
    [InlineData("a.b.c", "2")]
    public async Task Torob_api_rejects_missing_forged_or_unsigned_tokens(string? token, string? version)
    {
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/torob_api/v3/products")
        {
            Content = new StringContent("{\"page\":1,\"sort\":\"date_added_desc\"}", Encoding.UTF8, "application/json"),
        };
        if (token is not null)
            request.Headers.TryAddWithoutValidation("X-Torob-Token", token);
        if (version is not null)
            request.Headers.TryAddWithoutValidation("X-Torob-Token-Version", version);

        using var response = await client.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(JsonDocument.Parse(body).RootElement.TryGetProperty("error", out _), body);
        Assert.DoesNotContain("products", body, StringComparison.Ordinal);
    }

    [SqlTheory]
    [InlineData("/indexnow/x.txt")]
    [InlineData("/indexnow/abcdefgh12345678.txt")]
    public async Task Fake_indexnow_keys_are_not_found(string url)
    {
        using var client = app.CreateClient();
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SqlFact]
    public async Task Product_page_has_torob_meta_a_webp_share_image_and_no_html_comments()
    {
        using var client = app.CreateClient();

        var page = await client.GetPageAsync(ProductUrl);

        Assert.NotNull(page.One("meta[name='product_id']"));
        Assert.Matches("^[0-9]+$", page.One("meta[name='product_price']")?.GetAttribute("content") ?? "");
        Assert.Contains(page.One("meta[name='availability']")?.GetAttribute("content"), new[] { "instock", "outofstock" });
        Assert.DoesNotContain("<!--", page.Html, StringComparison.Ordinal);
        Assert.All(page.All("img"), img => Assert.True(img.HasAttribute("alt"), img.OuterHtml));
        Assert.Null(page.One("meta[name='keywords']"));

        var image = page.One("meta[property='og:image']")?.GetAttribute("content");
        Assert.False(string.IsNullOrWhiteSpace(image));
        var path = Uri.TryCreate(image, UriKind.Absolute, out var absolute) ? absolute.PathAndQuery : image!;
        if (path.StartsWith("/media/og/", StringComparison.Ordinal))
        {
            // تصویر نمونهٔ محصول ممکن است روی دیسک تست نباشد (uploads در گیت نیست): بودن ← WebP، نبودن ← 404 تمیز.
            var webRoot = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().WebRootPath;
            var source = Path.Combine(webRoot, Uri.UnescapeDataString(path["/media/og/".Length..].Split('?')[0]).Replace('/', Path.DirectorySeparatorChar));
            using var og = await client.GetAsync(path);
            if (File.Exists(source))
            {
                Assert.Equal(HttpStatusCode.OK, og.StatusCode);
                Assert.Equal("image/webp", og.Content.Headers.ContentType?.MediaType);
            }
            else
            {
                Assert.Equal(HttpStatusCode.NotFound, og.StatusCode);
            }
        }
    }

    [SqlTheory]
    [InlineData("/media/og/uploads/../appsettings.json")]
    [InlineData("/media/og/uploads/products/no-such-file.webp")]
    [InlineData("/media/og/assets/js/main.js")]
    public async Task Share_image_endpoint_refuses_anything_but_existing_local_images(string url)
    {
        using var client = app.CreateClient();
        using var response = await client.GetAsync(url);
        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest, $"{url}: {(int)response.StatusCode}");
    }

    [SqlFact]
    public async Task Rum_beacon_is_accepted_silently_and_garbage_does_not_fail()
    {
        using var client = app.CreateClient();

        using var good = await client.Http.PostAsync("/api/rum", new StringContent("{\"t\":\"product\",\"d\":\"mobile\",\"m\":{\"LCP\":2100}}", Encoding.UTF8, "text/plain"));
        using var bad = await client.Http.PostAsync("/api/rum", new StringContent("{\"t\":", Encoding.UTF8, "text/plain"));
        using var huge = await client.Http.PostAsync("/api/rum", new StringContent(new string('x', 10_000), Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.NoContent, good.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, bad.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, huge.StatusCode);
    }

    [SqlTheory]
    [InlineData("/admin/seo/autopilot")]
    [InlineData("/admin/seo/tasks")]
    [InlineData("/admin/seo/tasks?status=resolved")]
    [InlineData("/admin/seo/history")]
    [InlineData("/admin/seo/signals")]
    [InlineData("/admin/seo/signals?days=7")]
    public async Task Autopilot_admin_pages_open_for_the_owner(string url)
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);
        var started = DateTimeOffset.UtcNow;

        var page = await client.GetPageAsync(url);

        Assert.NotNull(page.One("main, .a-card"));
        Assert.DoesNotContain("<!--", page.Html, StringComparison.Ordinal);
        Assert.Empty(app.Logs.ErrorsSince(started));
    }

    [SqlTheory]
    [InlineData("/admin/seo/autopilot")]
    [InlineData("/admin/seo/tasks")]
    [InlineData("/admin/seo/history")]
    public async Task Staff_without_seo_permission_are_denied(string url)
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.AccountantPhone);
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/admin/denied", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [SqlFact]
    public async Task Live_check_returns_a_score_checklist_and_publish_blockers_without_saving()
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.SeoPhone);

        using var response = await client.PostFormAsync("/admin/seo/smart?handler=Live",
            ("Kind", "Product"), ("Name", "هدفون بی‌سیم سونی"), ("FocusKeyword", "هدفون بی‌سیم"),
            ("SeoTitle", "هدفون"), ("Body", "<p>هدفون خوب</p><!-- note --><script>alert(1)</script>"));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var json = JsonDocument.Parse(body).RootElement;
        Assert.True(json.GetProperty("ok").GetBoolean(), body);
        Assert.InRange(json.GetProperty("score").GetInt32(), 0, 100);
        Assert.True(json.GetProperty("thin").GetBoolean());
        Assert.NotEmpty(json.GetProperty("checks").EnumerateArray());
        Assert.NotEmpty(json.GetProperty("blockers").EnumerateArray());
        Assert.DoesNotContain("<script", body, StringComparison.OrdinalIgnoreCase);
    }

    [SqlFact]
    public async Task Live_check_is_closed_to_guests_and_rejects_unknown_page_types()
    {
        using var guest = app.CreateClient();
        using var denied = await guest.PostRawFormAsync("/admin/seo/smart?handler=Live", ("Kind", "Product"), ("Name", "x"));
        Assert.NotEqual("application/json", denied.Content.Headers.ContentType?.MediaType);

        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.SeoPhone);
        using var invalid = await client.PostFormAsync("/admin/seo/smart?handler=Live", ("Kind", "None"), ("Name", "x"));
        var json = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync()).RootElement;
        Assert.False(json.GetProperty("ok").GetBoolean());
    }

    [SqlFact]
    public async Task Writer_cannot_live_check_product_pages()
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.WriterPhone);
        using var response = await client.PostFormAsync("/admin/seo/smart?handler=Live", ("Kind", "Product"), ("Name", "هدفون"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [SqlFact]
    public async Task Public_pages_never_ship_html_comments_or_images_without_alt()
    {
        using var client = app.CreateClient();
        foreach (var url in new[] { "/", "/products", "/blog", "/blog/headphone-life-tips", "/page/about-us", "/category/smart-watch" })
        {
            var page = await client.GetPageAsync(url);
            Assert.DoesNotContain("<!--", page.Html, StringComparison.Ordinal);
            Assert.All(page.All("img"), img => Assert.True(img.HasAttribute("alt"), url + ": " + img.OuterHtml));
        }
    }
}
