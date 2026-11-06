using System.Net;
using System.Xml.Linq;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.E2E.Storefront;

/// <summary>
/// فروشگاه از دید بازدیدکنندهٔ مهمان: همهٔ صفحه‌های عمومی بدون خطا باز می‌شوند، صفحهٔ ناموجود 404 واقعی است،
/// سئو (sitemap/robots/canonical) و PWA درست سرو می‌شوند و یک خزندهٔ کوچک هیچ لینک خرابی (5xx) پیدا نمی‌کند.
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Storefront")]
public class StorefrontPagesE2ETests(ShopApp app)
{
    [SqlTheory]
    [InlineData("/")]
    [InlineData("/products")]
    [InlineData("/search")]
    [InlineData("/search?discounted=true")]
    [InlineData("/search?sort=BestSelling")]
    [InlineData("/search?minPrice=1000000&maxPrice=5000000&available=true")]
    [InlineData("/category/smart-watch")]
    [InlineData("/category/laptop")]
    [InlineData("/brand/apple")]
    [InlineData("/product/xiaomi-redmi-watch-3-active")]
    [InlineData("/product/sony-playstation-5")]
    [InlineData("/blog")]
    [InlineData("/blog/headphone-life-tips")]
    [InlineData("/blog/category/buying-guide")]
    [InlineData("/faq")]
    [InlineData("/contact-us")]
    [InlineData("/page/about-us")]
    [InlineData("/page/rules")]
    [InlineData("/page/privacy")]
    [InlineData("/compare")]
    [InlineData("/coming-soon")]
    [InlineData("/club")]
    [InlineData("/track-order")]
    [InlineData("/login")]
    [InlineData("/register")]
    [InlineData("/cart")]
    public async Task Public_page_renders_without_errors(string url)
    {
        using var client = app.CreateClient();
        var started = DateTimeOffset.UtcNow;

        var page = await client.GetPageAsync(url);

        Assert.Contains("<html", page.Html, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(page.One("title"));
        Assert.Empty(app.Logs.ErrorsSince(started));
    }

    [SqlTheory]
    [InlineData("/no-such-page")]
    [InlineData("/product/this-product-does-not-exist")]
    [InlineData("/blog/no-such-post")]
    [InlineData("/page/no-such-page")]
    public async Task Missing_content_returns_a_real_404_page(string url)
    {
        using var client = app.CreateClient();

        var page = await client.GetPageAsync(url, HttpStatusCode.NotFound);

        Assert.Contains("noindex", page.Html, StringComparison.Ordinal);
    }

    [SqlFact]
    public async Task Inactive_product_is_not_reachable()
    {
        var (_, slug) = await app.CreateProductAsync(active: false);
        using var client = app.CreateClient();

        await client.GetPageAsync("/product/" + slug, HttpStatusCode.NotFound);
    }

    [SqlFact]
    public async Task New_product_appears_on_its_page_with_structured_data_and_canonical_link()
    {
        var (_, slug) = await app.CreateProductAsync(price: 1_234_000);
        using var client = app.CreateClient();

        var page = await client.GetPageAsync("/product/" + slug);

        Assert.Contains("application/ld+json", page.Html, StringComparison.Ordinal);
        var canonical = page.One("link[rel='canonical']")?.GetAttribute("href");
        Assert.NotNull(canonical);
        Assert.EndsWith("/product/" + slug, canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("content=\"noindex", page.Html, StringComparison.Ordinal);
    }

    [SqlTheory]
    [InlineData("/about-us", "/page/about-us")]
    [InlineData("/rules", "/page/rules")]
    [InlineData("/contact", "/contact-us")]
    [InlineData("/Products", "/products")]
    [InlineData("/products/", "/products")]
    public async Task Duplicate_urls_permanently_redirect_to_the_canonical_one(string url, string target)
    {
        using var client = app.CreateClient();

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(target, response.Headers.Location?.OriginalString);
    }

    [SqlFact]
    public async Task Sitemap_is_valid_xml_listing_products_pages_and_blog_posts()
    {
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/sitemap.xml");
        var xml = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = XDocument.Parse(xml);
        var locations = document.Descendants().Where(e => e.Name.LocalName == "loc").Select(e => e.Value).ToList();
        Assert.Contains(locations, l => l.Contains("/product/", StringComparison.Ordinal));
        Assert.Contains(locations, l => l.Contains("/page/about-us", StringComparison.Ordinal));
        Assert.Contains(locations, l => l.Contains("/blog/", StringComparison.Ordinal));
        Assert.DoesNotContain(locations, l => l.Contains("/admin", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(locations, l => l.Contains("/profile", StringComparison.OrdinalIgnoreCase));
    }

    [SqlFact]
    public async Task Robots_txt_points_to_the_sitemap_and_hides_private_areas()
    {
        using var client = app.CreateClient();

        var body = await client.Http.GetStringAsync("/robots.txt");

        Assert.Contains("Sitemap:", body, StringComparison.Ordinal);
        Assert.Contains("/admin", body, StringComparison.Ordinal);
    }

    [SqlTheory]
    [InlineData("/login")]
    [InlineData("/cart")]
    [InlineData("/checkout/failed/x")]
    public async Task Private_or_utility_pages_are_noindex(string url)
    {
        using var client = app.CreateClient();
        using var response = await client.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect or HttpStatusCode.NotFound);
        if (response.StatusCode == HttpStatusCode.OK)
            Assert.Contains("noindex", html, StringComparison.Ordinal);
    }

    [SqlFact]
    public async Task Health_endpoint_reports_the_database_as_up_without_caching()
    {
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"ok\"", body, StringComparison.Ordinal);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [SqlTheory]
    [InlineData("haylou")]
    [InlineData("اپل")]
    [InlineData("")]
    [InlineData("ه")]
    public async Task Search_suggestions_return_json_for_any_query(string query)
    {
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/api/search/suggest?q=" + Uri.EscapeDataString(query));
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, document.RootElement.GetProperty("products").ValueKind);
    }

    [SqlFact]
    public async Task Search_by_text_finds_a_matching_product()
    {
        using var client = app.CreateClient();

        var page = await client.GetPageAsync("/search?q=" + Uri.EscapeDataString("اپل"));

        Assert.Contains("MacBook", page.Html, StringComparison.Ordinal);
    }

    [SqlFact]
    public async Task Province_cities_api_returns_the_cities_of_a_province()
    {
        var provinceId = await app.WithDbAsync(db => db.Locations
            .Where(l => l.Type == Fratak_Shop.Data.Entities.Locations.LocationType.Province)
            .Select(l => l.Id).FirstAsync());
        using var client = app.CreateClient();

        using var response = await client.GetAsync($"/api/locations/provinces/{provinceId}/cities");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetArrayLength() > 0);
    }

    /// <summary>
    /// خزندهٔ کوچک: همهٔ لینک‌های داخلی صفحهٔ اول و صفحه‌های لیست (تا ۱۵۰ آدرس) – هیچ‌کدام نباید خطای 5xx یا استثنا بدهند.
    /// </summary>
    [SqlFact]
    public async Task Crawling_the_storefront_finds_no_broken_page()
    {
        using var client = app.CreateClient();
        var started = DateTimeOffset.UtcNow;
        var queue = new Queue<string>(["/", "/products", "/blog", "/faq"]);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var failures = new List<string>();

        while (queue.Count > 0 && seen.Count < 150)
        {
            var url = queue.Dequeue();
            if (!seen.Add(url))
                continue;

            using var response = await client.GetAsync(url);
            var status = (int)response.StatusCode;
            if (status >= 500)
                failures.Add($"{status} {url}");
            if (status != 200 || response.Content.Headers.ContentType?.MediaType != "text/html")
                continue;

            var page = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync());
            foreach (var href in page.QuerySelectorAll("a[href]").Select(a => a.GetAttribute("href") ?? ""))
            {
                if (!href.StartsWith('/') || href.StartsWith("//", StringComparison.Ordinal))
                    continue;
                var path = href.Split('#')[0];
                if (path.Length == 0 || path.StartsWith("/admin", StringComparison.Ordinal) || path.Contains("handler=", StringComparison.Ordinal)
                    || path.StartsWith("/logout", StringComparison.Ordinal))
                    continue;
                if (!seen.Contains(path))
                    queue.Enqueue(path);
            }
        }

        Assert.True(seen.Count >= 30, $"The crawler only reached {seen.Count} pages.");
        Assert.Empty(failures);
        Assert.Empty(app.Logs.ErrorsSince(started));
    }
}
