using Fratak_Shop.Core.Services.Importing;
using Fratak_Shop.Core.Services.Suppliers;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های «درون‌ریزی از سایت» (دور ۱۰): خواندن ووکامرس (قیمت ریال/تومان، مشخصات، تصاویر)، شاپیفای، JSON-LD (آرایه، @graph،
/// AggregateOffer)، نقشهٔ سایت، تمیز کردن توضیح (حذف تصویر و لینک سایت مبدأ) و سرویس تشخیص نوع سایت با HTTP جعلی.
/// </summary>
[Trait("Category", "Unit")]
public class SiteImportTests
{
    private const string Base = "https://shop.example";

    private const string WooJson = """
        [
          {"id":1,"name":"هدفون بی&#8204;سیم هایلو T15","permalink":"https://shop.example/product/haylou-t15/","sku":"HT15",
           "short_description":"<p>صدای&nbsp;عالی</p>","description":"<p>توضیح <a href=\"https://shop.example/x\">کامل</a><img src=\"https://shop.example/a.jpg\"></p>",
           "prices":{"price":"2500000","currency_code":"IRR","currency_minor_unit":0},
           "images":[{"src":"https://shop.example/wp-content/uploads/1.jpg"},{"src":"/wp-content/uploads/2.jpg"},{"src":"https://shop.example/wp-content/uploads/1.jpg"}],
           "categories":[{"name":"هدفون"}],"brands":[{"name":"Haylou"}],
           "attributes":[{"name":"رنگ","terms":[{"name":"مشکی"},{"name":"سفید"}]},{"name":"","terms":[{"name":"x"}]}],
           "is_in_stock":true},
          {"id":2,"name":"کابل","permalink":"https://shop.example/product/cable/","prices":{"price":"15000000","currency_code":"IRT","currency_minor_unit":2},
           "is_in_stock":false},
          {"id":3,"name":"","permalink":"https://shop.example/product/empty/"}
        ]
        """;

    [Fact]
    public void Woo_products_are_read_with_prices_in_toman()
    {
        var items = SiteCatalogParsers.Woo(WooJson, Base)!;
        Assert.Equal(2, items.Count); // بدون نام کنار رفت

        var first = items[0];
        Assert.Equal("هدفون بی\u200Cسیم هایلو T15", first.Name);   // موجودیت HTML باز شد
        Assert.Equal("https://shop.example/product/haylou-t15/", first.SourceUrl);
        Assert.Equal((decimal?)250_000m, first.Price);                         // ریال ÷ ۱۰
        Assert.Equal("HT15", first.Sku);
        Assert.Equal("صدای عالی", first.ShortDescription);
        Assert.Equal("Haylou", first.Brand);
        Assert.Equal("هدفون", first.Category);
        Assert.True(first.InStock);
        Assert.Equal(new[] { "https://shop.example/wp-content/uploads/1.jpg", "https://shop.example/wp-content/uploads/2.jpg" }, first.Images.ToArray());
        var attribute = Assert.Single(first.Attributes);
        Assert.Equal("رنگ", attribute.Key);
        Assert.Equal("مشکی، سفید", attribute.Value);

        Assert.Equal((decimal?)150_000m, items[1].Price);                      // ۱۵٬۰۰۰٬۰۰۰ با دو رقم اعشار تومان
        Assert.False(items[1].InStock);
    }

    [Theory]
    [InlineData("""{"products":[]}""")]
    [InlineData("""{"code":"rest_no_route"}""")]
    public void Woo_rejects_non_array_responses(string json) => Assert.Null(SiteCatalogParsers.Woo(json, Base));

    [Fact]
    public void Shopify_products_are_read()
    {
        const string json = """
            {"products":[{"title":"Wireless Earbuds","handle":"earbuds-pro","body_html":"<p>Great sound</p>","vendor":"Acme","product_type":"Audio",
              "variants":[{"price":"129.00","sku":"EB-1","available":false},{"price":"139.00","available":false}],
              "options":[{"name":"Title","values":["Default Title"]},{"name":"Color","values":["Black","White"]}],
              "images":[{"src":"//cdn.shopify.com/s/files/1.jpg"}]}]}
            """;
        var item = Assert.Single(SiteCatalogParsers.Shopify(json, Base)!);
        Assert.Equal("https://shop.example/products/earbuds-pro", item.SourceUrl);
        Assert.Equal((decimal?)129m, item.Price);
        Assert.Equal("EB-1", item.Sku);
        Assert.False(item.InStock);
        Assert.Equal("Acme", item.Brand);
        Assert.Equal(new[] { "https://cdn.shopify.com/s/files/1.jpg" }, item.Images.ToArray());
        var option = Assert.Single(item.Attributes); // گزینهٔ پیش‌فرض Title کنار رفت
        Assert.Equal("Color", option.Key);
        Assert.Null(SiteCatalogParsers.Shopify("[]", Base));
    }

    [Fact]
    public void JsonLd_product_is_read_from_graph_with_aggregate_offer()
    {
        const string html = """
            <html><head>
            <script type="application/ld+json">{ broken json </script>
            <script type="application/ld+json">
            {"@context":"https://schema.org","@graph":[
              {"@type":"WebPage","name":"صفحه"},
              {"@type":["Product"],"name":"گوشی سامسونگ A55","sku":"A55","brand":{"@type":"Brand","name":"Samsung"},
               "description":"خط اول\nخط <دوم>","image":[{"url":"/img/a55.jpg"},"https://cdn.example/a55-2.jpg"],
               "offers":{"@type":"AggregateOffer","lowPrice":"185000000","priceCurrency":"IRR","availability":"https://schema.org/InStock"},
               "additionalProperty":[{"name":"حافظه","value":"256 گیگابایت"}],"color":"آبی"}
            ]}
            </script></head><body></body></html>
            """;
        var item = Assert.Single(SiteCatalogParsers.JsonLd(html, "https://shop.example/p/a55"));
        Assert.Equal("گوشی سامسونگ A55", item.Name);
        Assert.Equal("https://shop.example/p/a55", item.SourceUrl);
        Assert.Equal((decimal?)18_500_000m, item.Price);
        Assert.Equal("Samsung", item.Brand);
        Assert.True(item.InStock);
        Assert.Equal(new[] { "https://shop.example/img/a55.jpg", "https://cdn.example/a55-2.jpg" }, item.Images.ToArray());
        Assert.Contains(item.Attributes, a => a.Key == "حافظه" && a.Value == "256 گیگابایت");
        Assert.Contains(item.Attributes, a => a.Key == "رنگ" && a.Value == "آبی");
        Assert.Equal("<p>خط اول</p><p>خط &lt;دوم&gt;</p>", item.Description); // متن ساده کدگذاری شد
    }

    [Fact]
    public void JsonLd_out_of_stock_and_non_products_are_handled()
    {
        const string html = """
            <script type='application/ld+json'>[{"@type":"Organization","name":"x"},
              {"@type":"Product","name":"ناموجود","offers":[{"price":"990000","priceCurrency":"IRT","availability":"OutOfStock"}]}]</script>
            """;
        var item = Assert.Single(SiteCatalogParsers.JsonLd(html, "https://shop.example/p/1"));
        Assert.False(item.InStock);
        Assert.Equal((decimal?)990_000m, item.Price);
        Assert.Empty(SiteCatalogParsers.JsonLd("<html>no data</html>", "https://shop.example/"));
    }

    [Fact]
    public void Description_loses_images_and_links_of_the_source_site()
    {
        var clean = SiteCatalogParsers.CleanDescription("""<p>عالی <a href="https://source.example/buy">خرید</a><img src="https://source.example/1.jpg"></p><script>alert(1)</script><iframe src="https://x"></iframe>""");
        Assert.Equal("<p>عالی خرید</p>", clean);
        Assert.Null(SiteCatalogParsers.CleanDescription("<p><img src='x.jpg'></p>"));
        Assert.Null(SiteCatalogParsers.CleanDescription(null));
    }

    [Fact]
    public void Sitemaps_and_robots_are_read()
    {
        Assert.Equal(new[] { "https://shop.example/product-sitemap.xml" },
            SiteCatalogParsers.RobotsSitemaps("User-agent: *\nDisallow: /cart\nSitemap: https://shop.example/product-sitemap.xml\nSitemap: javascript:x"));
        const string index = """<?xml version="1.0"?><sitemapindex><sitemap><loc>https://shop.example/post-sitemap.xml</loc></sitemap><sitemap><loc> https://shop.example/product-sitemap.xml </loc></sitemap></sitemapindex>""";
        Assert.True(SiteCatalogParsers.IsSitemapIndex(index));
        Assert.Equal(new[] { "https://shop.example/post-sitemap.xml", "https://shop.example/product-sitemap.xml" }, SiteCatalogParsers.SitemapLocations(index).ToArray());
        Assert.Equal(new[] { "https://shop.example/a?x=1&y=2" }, SiteCatalogParsers.SitemapLocations("<urlset><url><loc>https://shop.example/a?x=1&amp;y=2</loc></url></urlset>").ToArray());
    }

    [Theory]
    [InlineData("https://shop.example/product/haylou-t15/", true)]
    [InlineData("https://shop.example/p/123", true)]
    [InlineData("https://shop.example/%D9%85%D8%AD%D8%B5%D9%88%D9%84/x/", true)]
    [InlineData("https://shop.example/blog/news", false)]
    public void Product_urls_are_recognised(string url, bool expected) => Assert.Equal(expected, SiteCatalogParsers.LooksLikeProductUrl(url));

    [Theory]
    [InlineData("۱٬۲۵۰٬۰۰۰", 1250000.0)]
    [InlineData("99.50", 99.5)]
    [InlineData("abc", null)]
    public void Numbers_are_parsed(string text, double? expected)
        => Assert.Equal(expected is null ? null : (decimal?)(decimal)expected.Value, SiteCatalogParsers.Number(text));

    [Theory]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("data:image/png;base64,xx", null)]
    [InlineData("//cdn.example/a.jpg", "https://cdn.example/a.jpg")]
    [InlineData("/a.jpg", "https://shop.example/a.jpg")]
    public void Only_http_image_urls_are_kept(string url, string? expected) => Assert.Equal(expected, SiteCatalogParsers.Absolute(url, Base));

    // ── سرویس ──

    private sealed class FakeHttp(Func<string, SupplierHttpResponse> respond) : ISupplierHttp
    {
        public List<string> Urls { get; } = [];

        public Task<SupplierHttpResponse> GetAsync(string url, bool expectJson, CancellationToken cancellationToken)
        {
            lock (Urls)
                Urls.Add(url);
            return Task.FromResult(respond(url));
        }
    }

    [Fact]
    public async Task WooCommerce_sites_are_detected_first()
    {
        var http = new FakeHttp(url => url.Contains("/wp-json/wc/store/v1/products") ? new(200, WooJson) : new(404, null));
        var catalog = await new SiteCatalogService(http).DiscoverAsync("shop.example", 100, CancellationToken.None);

        Assert.Equal("ووکامرس (وردپرس)", catalog.Platform);
        Assert.Equal(Base, catalog.BaseUrl);
        Assert.Equal(2, catalog.Products.Count);
        Assert.DoesNotContain(http.Urls, u => u.Contains("products.json")); // بعد از پیدا شدن ووکامرس، روش‌های دیگر امتحان نمی‌شوند
    }

    [Fact]
    public async Task Max_products_is_respected()
    {
        var http = new FakeHttp(url => url.Contains("/wp-json/") ? new(200, WooJson) : new(404, null));
        var catalog = await new SiteCatalogService(http).DiscoverAsync("https://shop.example", 1, CancellationToken.None);
        Assert.Single(catalog.Products);
        Assert.True(catalog.Truncated);
    }

    [Fact]
    public async Task Sitemap_and_jsonld_are_the_fallback()
    {
        const string page = """<script type="application/ld+json">{"@type":"Product","name":"محصول یک","offers":{"price":"100000","priceCurrency":"IRT"}}</script>""";
        var http = new FakeHttp(url => url switch
        {
            "https://shop.example/robots.txt" => new(200, "Sitemap: https://shop.example/sitemap_index.xml"),
            "https://shop.example/sitemap_index.xml" => new(200, "<sitemapindex><sitemap><loc>https://shop.example/product-sitemap.xml</loc></sitemap><sitemap><loc>https://evil.example/product-sitemap.xml</loc></sitemap></sitemapindex>"),
            "https://shop.example/product-sitemap.xml" => new(200, "<urlset><url><loc>https://shop.example/product/one/</loc></url><url><loc>https://shop.example/product/two/</loc></url></urlset>"),
            "https://shop.example/product/one/" => new(200, page),
            "https://shop.example/product/two/" => new(200, "<html>بدون داده</html>"),
            _ => new(200, "<html>صفحهٔ اصلی</html>"), // سایت‌هایی که برای هر نشانی HTML برمی‌گردانند
        });

        var catalog = await new SiteCatalogService(http).DiscoverAsync("https://shop.example/", 100, CancellationToken.None);

        Assert.Equal("نقشهٔ سایت + دادهٔ ساختاریافته", catalog.Platform);
        var item = Assert.Single(catalog.Products);
        Assert.Equal("محصول یک", item.Name);
        Assert.Equal("https://shop.example/product/one/", item.SourceUrl);
        Assert.Contains(catalog.Problems, p => p.Contains("1 صفحه"));
        Assert.DoesNotContain(http.Urls, u => u.Contains("evil.example")); // نقشهٔ دامنهٔ دیگر دنبال نمی‌شود
    }

    [Fact]
    public async Task A_single_product_page_url_is_read_directly()
    {
        const string page = """<script type="application/ld+json">{"@type":"Product","name":"تکی"}</script>""";
        var http = new FakeHttp(url => url == "https://shop.example/product/single/" ? new(200, page) : new(404, null));
        var catalog = await new SiteCatalogService(http).DiscoverAsync("https://shop.example/product/single/", 100, CancellationToken.None);
        Assert.Equal("تکی", Assert.Single(catalog.Products).Name);
        Assert.Single(http.Urls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://shop.example")]
    [InlineData("https://")]
    public async Task Invalid_urls_are_reported(string url)
    {
        var http = new FakeHttp(_ => new(200, "[]"));
        var catalog = await new SiteCatalogService(http).DiscoverAsync(url, 10, CancellationToken.None);
        Assert.Empty(catalog.Products);
        Assert.Single(catalog.Problems);
        Assert.Empty(http.Urls);
    }

    [Fact]
    public async Task Unknown_sites_get_a_clear_message()
    {
        var http = new FakeHttp(_ => new(0, null, "خطای اتصال"));
        var catalog = await new SiteCatalogService(http).DiscoverAsync("https://shop.example", 10, CancellationToken.None);
        Assert.Empty(catalog.Products);
        Assert.Contains("شناخته نشد", Assert.Single(catalog.Problems));
    }
}
