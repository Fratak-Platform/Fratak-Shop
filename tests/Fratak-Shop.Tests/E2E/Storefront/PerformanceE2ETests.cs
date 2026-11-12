using System.IO.Compression;
using System.Net;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Ops;

namespace Fratak_Shop.Tests.E2E.Storefront;

/// <summary>
/// دور نوزدهم – فاز A از بیرون (مثل مرورگر و ربات):
/// • بودجهٔ پرس‌وجو: صفحه‌های اصلی با تعداد محدودی پرس‌وجوی پایگاه داده ساخته می‌شوند (سرآیند آزمایشی X-Db-Queries).
/// • robots.txt برای ربات‌های پرمصرف Crawl-delay دارد و در عمل هم دومین درخواست پشت‌سرهم AhrefsBot پاسخ ۴۲۹ می‌گیرد؛
///   ولی گوگل و مرورگر هرگز محدود نمی‌شوند.
/// • فایل‌های CSS/JS با Brotli/Gzip فشرده و درست باز می‌شوند.
/// • صفحهٔ «منابع سرور» برای مالک باز است و نویسنده راه ندارد.
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Performance")]
public class PerformanceE2ETests(ShopApp app)
{
    private const string ProductUrl = "/product/xiaomi-redmi-watch-3-active";

    private static int Queries(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues(QueryMetricsMiddleware.Header, out var values), "X-Db-Queries header missing");
        return int.Parse(values!.First(), System.Globalization.CultureInfo.InvariantCulture);
    }

    [SqlTheory]
    [InlineData("/", 60)]
    [InlineData(ProductUrl, 45)]
    [InlineData("/search?q=%D8%B3%D8%A7%D8%B9%D8%AA", 45)]
    [InlineData("/blog", 40)]
    [InlineData("/faq", 30)]
    public async Task Main_pages_stay_within_their_database_query_budget(string url, int budget)
    {
        using var client = app.CreateClient();
        // پارامتر یکتا تا پاسخ از کش صفحه نیاید و پرس‌وجوهای واقعی شمرده شوند.
        var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        using var response = await client.GetAsync(url + separator + "qc=" + Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var count = Queries(response);
        Assert.True(count <= budget, $"{url} ran {count} queries (budget {budget})");
    }

    [SqlFact]
    public async Task Robots_txt_slows_heavy_crawlers_but_keeps_one_rule_set_for_everyone_else()
    {
        using var client = app.CreateClient();
        var body = await client.Http.GetStringAsync("/robots.txt");

        Assert.Contains("User-agent: AhrefsBot", body, StringComparison.Ordinal);
        Assert.Contains("Crawl-delay: 10", body, StringComparison.Ordinal);
        Assert.Contains("User-agent: *", body, StringComparison.Ordinal);
        Assert.DoesNotContain("User-agent: Googlebot", body, StringComparison.Ordinal);
        // گروه ربات‌های پرمصرف هم مسیرهای خصوصی را می‌بندد (رباتی که گروه خودش را پیدا کند گروه * را نمی‌خواند).
        var heavyGroup = body[..body.IndexOf("User-agent: *", StringComparison.Ordinal)];
        Assert.Contains("Disallow: /admin", heavyGroup, StringComparison.Ordinal);
    }

    [SqlFact]
    public async Task Rapid_heavy_crawler_requests_get_429_but_search_engines_and_people_do_not()
    {
        using var bot = app.CreateClient(userAgent: "Mozilla/5.0 (compatible; AhrefsBot/7.0; +http://ahrefs.com/robot/)");
        using var first = await bot.GetAsync("/faq");
        using var second = await bot.GetAsync("/faq");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.NotNull(second.Headers.RetryAfter);

        using var google = app.CreateClient(userAgent: "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)");
        for (var i = 0; i < 3; i++)
        {
            using var response = await google.GetAsync("/faq");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var person = app.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            using var response = await person.GetAsync("/faq");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [SqlTheory]
    [InlineData("br")]
    [InlineData("gzip")]
    public async Task Static_css_is_served_precompressed_and_decodes_to_the_original(string encoding)
    {
        PrecompressedStaticFilesMiddleware.ResetForTests();
        using var client = app.CreateClient();

        using var plain = await client.GetAsync("/assets/css/output.css");
        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
        var original = await plain.Content.ReadAsByteArrayAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/assets/css/output.css?v=test");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", encoding);
        using var compressed = await client.Http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, compressed.StatusCode);
        Assert.Contains(encoding, compressed.Content.Headers.ContentEncoding);
        Assert.Contains("immutable", compressed.Headers.CacheControl?.ToString() ?? "", StringComparison.Ordinal);
        Assert.True(compressed.Headers.TryGetValues("X-Content-Type-Options", out var nosniff)
                    && nosniff.Any(v => v.Contains("nosniff", StringComparison.OrdinalIgnoreCase)), "security headers missing on precompressed files");

        var bytes = await compressed.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length < original.Length);
        Stream decoder = encoding == "br"
            ? new BrotliStream(new MemoryStream(bytes), CompressionMode.Decompress)
            : new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
        await using (decoder)
        {
            using var output = new MemoryStream();
            await decoder.CopyToAsync(output);
            Assert.Equal(original, output.ToArray());
        }
    }

    [SqlFact]
    public async Task Owner_sees_the_server_resources_page_and_a_writer_is_denied()
    {
        using var owner = app.CreateClient();
        await owner.LoginAsync(ShopApp.OwnerPhone);
        var page = await owner.GetPageAsync("/admin/system/resources");
        Assert.Contains("منابع سرور", page.Text, StringComparison.Ordinal);
        Assert.Contains("ضعیف (هاست اشتراکی)", page.Text, StringComparison.Ordinal);
        Assert.Contains("no-store", page.Response.Headers.CacheControl?.ToString() ?? "", StringComparison.Ordinal);

        using var writer = app.CreateClient();
        await writer.LoginAsync(ShopApp.WriterPhone);
        using var denied = await writer.GetAsync("/admin/system/resources");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("/admin/denied", denied.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [SqlFact]
    public async Task Health_endpoint_is_never_queued_or_throttled()
    {
        using var bot = app.CreateClient(userAgent: "Mozilla/5.0 (compatible; SemrushBot/7~bl)");
        for (var i = 0; i < 3; i++)
        {
            using var response = await bot.GetAsync("/healthz");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }
}
