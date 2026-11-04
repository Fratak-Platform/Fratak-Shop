using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Core.Services.Seo.Feeds;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Data.Utilities;
using Fratak_Shop.Web.Infrastructure.Seo;
using FsCheck.Xunit;
using Microsoft.AspNetCore.Http;

namespace Fratak_Shop.Tests.Unit.Seo;

/// <summary>
/// تست‌های واحد دور «سئوی خودکار». هر بخش جدا دسته‌بندی شده (Trait «Area») تا بتوان فقط همان بخش را اجرا کرد:
/// نامک انگلیسی، لینک داخلی، مرتب‌سازی HTML، مدل CTR، فید ترب و XML، نقشهٔ سایت، سیگنال ربات/سرعت، زمان‌بندی شبانه و صف.
/// <para>ترتیب در هر بخش: حالت درست ← حالت مرزی ← ورودی خراب/حمله.</para>
/// </summary>
[Trait("Category", "Unit")]
public class SeoAutopilotTests
{
    // ── نامک انگلیسی ─────────────────────────────────────────────────────

    [Theory]
    [Trait("Area", "Slug")]
    [InlineData("samsung-a55", true)]
    [InlineData("galaxy-a55-2026", true)]
    [InlineData("گوشی", false)]
    [InlineData("A55", false)]
    [InlineData("-a55", false)]
    [InlineData("a55-", false)]
    [InlineData("a--b", false)]
    [InlineData("a b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsEnglish_accepts_only_clean_latin_slugs(string? slug, bool expected) =>
        Assert.Equal(expected, EnglishSlug.IsEnglish(slug));

    [Theory]
    [Trait("Area", "Slug")]
    [InlineData("گوشی موبایل سامسونگ مدل Galaxy A55")]
    [InlineData("هدفون بی‌سیم با میکروفون")]
    [InlineData("درباره ما")]
    [InlineData("کیف چرمی زنانه")]
    [InlineData("ساعت هوشمند شیائومی ۲۰۲۴")]
    [InlineData("قاب   گوشی    آیفون ۱۵ پرو مکس")]
    [InlineData("مُحَمَّد – تست؛ «علامت‌ها» و ي/ك عربی")]
    public void Persian_text_always_becomes_an_english_slug(string text)
    {
        var slug = EnglishSlug.From(text);

        Assert.True(slug.Length > 0, text);
        Assert.True(EnglishSlug.IsEnglish(slug), slug);
        Assert.True(slug.Length <= EnglishSlug.DefaultMaxLength);
    }

    [Fact]
    [Trait("Area", "Slug")]
    public void Latin_name_is_preferred_and_kept_readable()
    {
        Assert.Equal("samsung-galaxy-a55", EnglishSlug.Prefer("گوشی سامسونگ", "Samsung Galaxy A55"));
        Assert.Equal("galaxy-a55", EnglishSlug.From("Galaxy A55"));
    }

    [Fact]
    [Trait("Area", "Slug")]
    public void Unique_adds_a_numeric_suffix_and_respects_the_length_limit()
    {
        Assert.Equal("case", EnglishSlug.Unique("case", _ => false));
        Assert.Equal("case-2", EnglishSlug.Unique("case", s => s == "case"));
        Assert.Equal("case-3", EnglishSlug.Unique("case", s => s is "case" or "case-2"));
        var longSlug = EnglishSlug.Unique(new string('a', 50), s => s.Length == 50, maxLength: 50);
        Assert.True(longSlug.Length <= 50);
        Assert.EndsWith("-2", longSlug, StringComparison.Ordinal);
    }

    [Property(MaxTest = 400)]
    [Trait("Area", "Slug")]
    public bool Any_input_gives_an_empty_or_valid_bounded_slug(string? input)
    {
        var slug = EnglishSlug.From(input);
        return slug.Length == 0 || (EnglishSlug.IsEnglish(slug) && slug.Length <= EnglishSlug.DefaultMaxLength);
    }

    [Theory]
    [Trait("Area", "Slug")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("../../etc/passwd")]
    [InlineData("'; DROP TABLE Products; --")]
    [InlineData("%2e%2e%2f")]
    public void Attack_strings_never_survive_into_the_slug(string input)
    {
        var slug = EnglishSlug.From(input);
        Assert.DoesNotContain("<", slug, StringComparison.Ordinal);
        Assert.DoesNotContain("/", slug, StringComparison.Ordinal);
        Assert.DoesNotContain(".", slug, StringComparison.Ordinal);
        Assert.DoesNotContain("'", slug, StringComparison.Ordinal);
        Assert.True(slug.Length == 0 || EnglishSlug.IsEnglish(slug));
    }

    // ── لینک داخلی ───────────────────────────────────────────────────────

    private static readonly LinkTarget Headphones = new("/category/headphones", ["هدفون"], 10);

    [Fact]
    [Trait("Area", "InternalLinks")]
    public void Links_the_first_whole_word_mention()
    {
        var html = InternalLinker.Link("<p>این هدفون برای ورزش عالی است</p>", "/product/x", [Headphones], 6);

        Assert.Contains("<a href=\"/category/headphones\">هدفون</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Area", "InternalLinks")]
    public void Never_links_to_itself_inside_headings_or_existing_links()
    {
        Assert.Equal("<p>هدفون</p>", InternalLinker.Link("<p>هدفون</p>", "/category/headphones", [Headphones], 6));
        Assert.DoesNotContain("<a", InternalLinker.Link("<h2>هدفون</h2>", "/product/x", [Headphones], 6), StringComparison.Ordinal);
        var existing = "<p><a href=\"/category/headphones\">خرید</a> هدفون</p>";
        Assert.Equal(existing, InternalLinker.Link(existing, "/product/x", [Headphones], 6));
    }

    [Fact]
    [Trait("Area", "InternalLinks")]
    public void Partial_words_are_not_linked()
    {
        var bag = new LinkTarget("/category/bags", ["کیف"], 10);
        Assert.Equal(-1, InternalLinker.FindWhole("کیفیت عالی", "کیف"));
        Assert.Equal(6, InternalLinker.FindWhole("کیفیت کیف خوب", "کیف"));
        Assert.Equal("<p>کیفیت عالی</p>", InternalLinker.Link("<p>کیفیت عالی</p>", "/product/x", [bag], 6));
    }

    [Fact]
    [Trait("Area", "InternalLinks")]
    public void Respects_the_link_cap_and_links_each_target_once()
    {
        var targets = Enumerable.Range(1, 10).Select(i => new LinkTarget($"/category/c{i}", [$"واژه{i}"], i)).ToList();
        var text = "<p>" + string.Join("</p><p>", Enumerable.Range(1, 10).Select(i => $"متن واژه{i} و دوباره واژه{i}")) + "</p>";

        var html = InternalLinker.Link(text, "/product/x", targets, 3);

        var count = html.Split("<a ", StringSplitOptions.None).Length - 1;
        Assert.Equal(3, count);
    }

    [Fact]
    [Trait("Area", "InternalLinks")]
    public void Ignores_external_or_empty_targets_and_huge_input()
    {
        var external = new LinkTarget("https://evil.example/x", ["هدفون"], 99);
        Assert.Equal("<p>هدفون</p>", InternalLinker.Link("<p>هدفون</p>", "/p", [external], 6));
        Assert.Equal("", InternalLinker.Link(null, "/p", [Headphones], 6));
        var huge = "<p>" + new string('ا', HtmlSeoFixer.MaxInputLength + 1) + " هدفون</p>";
        Assert.Same(huge, InternalLinker.Link(huge, "/p", [Headphones], 6));
    }

    [Fact]
    [Trait("Area", "InternalLinks")]
    public void Link_phrases_need_enough_words_and_no_lonely_stop_words()
    {
        var phrases = SeoLinkTargetProvider.Phrases(["هدفون بی‌سیم", "هدفون", "  هدفون بی‌سیم  ", null, "ab"], minWords: 2);
        Assert.Single(phrases);
        Assert.Contains("هدفون", phrases[0], StringComparison.Ordinal);
    }

    // ── مرتب‌سازی HTML ───────────────────────────────────────────────────

    [Fact]
    [Trait("Area", "HtmlFix")]
    public void Html_fixer_removes_comments_and_demotes_a_second_h1()
    {
        var result = HtmlSeoFixer.Fix("<!-- note --><h1>معرفی</h1><p>متن</p>", new HtmlFixContext("هدفون سونی"));

        Assert.True(result.Changed);
        Assert.DoesNotContain("<!--", result.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<h1", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<h2", result.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Area", "HtmlFix")]
    public void Html_fixer_gives_every_image_an_alt_and_is_idempotent()
    {
        var first = HtmlSeoFixer.Fix("<p>متن</p><img src=\"/uploads/media/a.webp\">", new HtmlFixContext("هدفون سونی"));
        Assert.Matches("alt=\"[^\"]+\"", first.Html);

        var second = HtmlSeoFixer.Fix(first.Html, new HtmlFixContext("هدفون سونی"));
        Assert.False(second.Changed);
        Assert.Equal(first.Html, second.Html);
    }

    [Fact]
    [Trait("Area", "HtmlFix")]
    public void Html_fixer_removes_hidden_text_and_scripts()
    {
        var result = HtmlSeoFixer.Fix("<p>دیده</p><div style=\"display:none\">کلمه کلمه کلمه</div><script>alert(1)</script>", new HtmlFixContext("x"));

        Assert.DoesNotContain("کلمه کلمه", result.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("دیده", result.Html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Area", "HtmlFix")]
    public void Strip_comments_leaves_clean_html_untouched()
    {
        const string clean = "<p>بدون کامنت</p>";
        Assert.Same(clean, HtmlSeoFixer.StripComments(clean));
        Assert.Equal("<p>a</p>", HtmlSeoFixer.StripComments("<p>a</p><!-- secret -->"));
        Assert.Equal("", HtmlSeoFixer.StripComments(null));
    }

    [Fact]
    [Trait("Area", "Images")]
    public void Image_reference_replacement_only_touches_known_tokens()
    {
        const string hex = "0123456789abcdef0123456789abcdef";
        var map = new Dictionary<string, string> { [hex + ".jpg"] = hex + ".webp", ["/assets/image/blog/1.jpg"] = "/assets/image/blog/1.webp" };

        var output = SeoMaintenanceService.Replace($"<img src=\"/uploads/media/{hex}.jpg\"><img src=\"/assets/image/blog/1.jpg\"><img src=\"/x/1.png\">", map);

        Assert.Contains(hex + ".webp", output, StringComparison.Ordinal);
        Assert.Contains("/assets/image/blog/1.webp", output, StringComparison.Ordinal);
        Assert.Contains("/x/1.png", output, StringComparison.Ordinal); // نام کوتاه عمومی هرگز عوض نمی‌شود
        Assert.Equal("", SeoMaintenanceService.Replace("", map));
    }

    // ── مدل CTR و آزمون عنوان ────────────────────────────────────────────

    [Fact]
    [Trait("Area", "Ctr")]
    public void Expected_ctr_falls_with_position_and_stays_positive()
    {
        var previous = double.MaxValue;
        foreach (var position in new[] { 1.0, 2, 3, 5, 8, 10, 15, 25, 40 })
        {
            var expected = CtrModel.Expected(position);
            Assert.True(expected > 0 && expected <= previous, $"{position}: {expected}");
            previous = expected;
        }

        Assert.Equal(CtrModel.Expected(1), CtrModel.Expected(double.NaN));
    }

    [Fact]
    [Trait("Area", "Ctr")]
    public void Title_test_needs_enough_data_and_a_significant_change()
    {
        Assert.Equal(SeoTitleTestStatus.Inconclusive, CtrModel.Judge(new CtrSample(100, 1, 5), new CtrSample(100, 20, 5)));
        Assert.Equal(SeoTitleTestStatus.Won, CtrModel.Judge(new CtrSample(2000, 40, 5), new CtrSample(2000, 100, 5)));
        Assert.Equal(SeoTitleTestStatus.Lost, CtrModel.Judge(new CtrSample(2000, 100, 5), new CtrSample(2000, 40, 5)));
        Assert.Equal(SeoTitleTestStatus.Inconclusive, CtrModel.Judge(new CtrSample(2000, 100, 5), new CtrSample(2000, 102, 5)));
    }

    [Fact]
    [Trait("Area", "Ctr")]
    public void Near_page_one_and_sudden_drop_detection()
    {
        Assert.True(CtrModel.IsNearPageOne(new CtrSample(80, 1, 12)));
        Assert.False(CtrModel.IsNearPageOne(new CtrSample(80, 1, 3)));
        Assert.False(CtrModel.IsNearPageOne(new CtrSample(10, 0, 12)));

        var steady = Enumerable.Repeat(100, 20).ToList();
        Assert.False(CtrModel.IsSuddenDrop(steady));
        Assert.True(CtrModel.IsSuddenDrop(steady.Take(17).Concat(new[] { 20, 25, 30 }).ToList()));
        Assert.False(CtrModel.IsSuddenDrop([1, 2, 3]));
    }

    [Fact]
    [Trait("Area", "Landings")]
    public void Price_landings_need_enough_products_below_a_round_price()
    {
        Assert.Empty(SeoLandingService.Build([100_000m, 200_000m], "mobile", "گوشی"));

        var prices = new List<decimal> { 150_000, 250_000, 280_000, 400_000, 450_000, 900_000, 1_200_000, 1_800_000, 2_500_000, 4_000_000, 6_000_000, 9_000_000 };
        var links = SeoLandingService.Build(prices, "mobile", "گوشی");

        Assert.NotEmpty(links);
        Assert.All(links, l => Assert.StartsWith("/category/mobile?maxPrice=", l.Url, StringComparison.Ordinal));
        Assert.All(links, l => Assert.StartsWith("خرید گوشی زیر", l.Title, StringComparison.Ordinal));
    }

    // ── فید ترب و XML ───────────────────────────────────────────────────

    [Theory]
    [Trait("Area", "Torob")]
    [InlineData("{\"page\":1,\"sort\":\"date_added_desc\"}", TorobMode.Paged)]
    [InlineData("{\"page_urls\":[\"https://shop.fratak.ir/product/a\"]}", TorobMode.PageUrls)]
    [InlineData("{\"page_uniques\":[\"abc\"]}", TorobMode.PageUniques)]
    [InlineData("{\"sort\":\"product_id_desc\"}", TorobMode.Cursor)]
    public void Torob_request_modes_are_parsed(string body, TorobMode mode)
    {
        var (query, error) = TorobFeedService.Parse(body);
        Assert.Null(error);
        Assert.Equal(mode, query!.Mode);
    }

    [Theory]
    [Trait("Area", "Torob")]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"page_urls\":[]}")]
    [InlineData("{\"page_urls\":\"x\"}")]
    [InlineData("{\"page_urls\":[\"a\"],\"page_uniques\":[\"b\"]}")]
    [InlineData("{\"sort\":\"product_id_desc\",\"page\":2}")]
    [InlineData("{\"sort\":\"product_id_desc\",\"cursor\":\"!!!\"}")]
    public void Torob_bad_requests_get_an_error(string body)
    {
        var (query, error) = TorobFeedService.Parse(body);
        Assert.Null(query);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [Trait("Area", "Torob")]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(123456)]
    public void Torob_cursor_round_trips_and_rejects_tampering(int offset)
    {
        var cursor = TorobFeedService.EncodeCursor(offset);
        Assert.Equal(offset, TorobFeedService.DecodeCursor(cursor));
        Assert.DoesNotContain("=", cursor, StringComparison.Ordinal);
        Assert.Null(TorobFeedService.DecodeCursor(Convert.ToBase64String(Encoding.ASCII.GetBytes("o:-5"))));
        Assert.Null(TorobFeedService.DecodeCursor(new string('A', 100)));
    }

    private static Product SampleProduct(int stock = 5) => new()
    {
        Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Name = "گوشی سامسونگ گلکسی A55",
        EnglishName = "Samsung Galaxy A55",
        Slug = "samsung-galaxy-a55",
        SKU = "A55",
        Price = 1_000_000,
        DiscountPrice = 900_000,
        StockQuantity = stock,
        IsActive = true,
        WarrantyTitle = "گارانتی ۱۸ ماهه",
        ShortDescription = "<p>گوشی میان‌رده با دوربین خوب</p>",
        CreatedAt = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero),
        Images = [new ProductImage { ImageName = "b.webp", DisplayOrder = 2 }, new ProductImage { ImageName = "a.webp", IsPrimary = true, DisplayOrder = 1 }],
        Features = [new ProductFeature { Key = "رنگ", Value = "مشکی", DisplayOrder = 1 }],
    };

    [Fact]
    [Trait("Area", "Torob")]
    public void Torob_product_uses_toman_prices_absolute_urls_and_iran_time()
    {
        var item = TorobFeedService.Map(SampleProduct(), "https://shop.fratak.ir/");

        Assert.Equal("11111111222233334444555555555555", item.PageUnique);
        Assert.Equal("https://shop.fratak.ir/product/samsung-galaxy-a55", item.PageUrl);
        Assert.Equal(900_000, item.CurrentPrice);
        Assert.Equal(1_000_000, item.OldPrice);
        Assert.True(item.Availability);
        Assert.Equal("https://shop.fratak.ir/uploads/products/a.webp", item.ImageLinks[0]);
        Assert.Equal("مشکی", item.Spec["رنگ"]);
        Assert.EndsWith("+03:30", item.DateAdded, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Area", "Torob")]
    public void Unavailable_product_has_zero_price_for_torob()
    {
        var item = TorobFeedService.Map(SampleProduct(stock: 0), "https://shop.fratak.ir");
        Assert.False(item.Availability);
        Assert.Equal(0, item.CurrentPrice);
        Assert.Null(item.OldPrice);
    }

    [Fact]
    [Trait("Area", "Torob")]
    public void Torob_response_always_writes_next_cursor_even_when_null()
    {
        var json = JsonSerializer.Serialize(new TorobResponse { CurrentPage = 1, Total = 0, MaxPages = 0 });
        Assert.Contains("\"next_cursor\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"api_version\":\"torob_api_v3\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Area", "Torob")]
    [InlineData("https://shop.fratak.ir/product/a55?x=1", "a55")]
    [InlineData("/product/a55/", "a55")]
    [InlineData("/category/a55", null)]
    [InlineData("/product/a/b", null)]
    public void Slug_is_read_from_a_product_url(string url, string? slug) =>
        Assert.Equal(slug, TorobFeedService.SlugFromUrl(url));

    [Fact]
    [Trait("Area", "Feeds")]
    public void Product_feed_is_valid_rss_with_rial_prices()
    {
        var product = TorobFeedService.Map(SampleProduct(), "https://shop.fratak.ir");
        var xml = Encoding.UTF8.GetString(SeoFeedEndpoints.Feed([product], "فراتک & شاپ", "https://shop.fratak.ir"));

        var document = XDocument.Parse(xml);
        XNamespace g = "http://base.google.com/ns/1.0";
        var item = Assert.Single(document.Descendants("item"));
        Assert.Equal("10000000 IRR", item.Element(g + "price")!.Value);
        Assert.Equal("9000000 IRR", item.Element(g + "sale_price")!.Value);
        Assert.Equal("in_stock", item.Element(g + "availability")!.Value);
        Assert.Equal("فراتک & شاپ", document.Descendants("title").First().Value);
    }

    // ── نقشهٔ سایت ──────────────────────────────────────────────────────

    [Theory]
    [Trait("Area", "Sitemap")]
    [InlineData("/product/a", null, false)]
    [InlineData("/product/a", "", false)]
    [InlineData("/product/a", "https://shop.fratak.ir/product/a/", false)]
    [InlineData("/product/a", "/product/A?utm=1", false)]
    [InlineData("/product/a", "https://shop.fratak.ir/product/b", true)]
    public void Pages_with_a_foreign_canonical_are_left_out(string path, string? canonical, bool elsewhere)
    {
        Assert.Equal(elsewhere, SitemapService.PointsElsewhere(path, canonical));
        Assert.Equal(elsewhere, SitemapService.Entry(path, null, "weekly", 0.5m, canonical, null) is null);
    }

    [Fact]
    [Trait("Area", "Sitemap")]
    public void Sitemap_images_follow_the_upload_rules_and_are_capped()
    {
        var images = SitemapService.Images("products", "a b.webp", "/assets/image/x.webp", null, "", "a b.webp", "c.webp", "d.webp", "e.webp", "f.webp", "g.webp")!;

        Assert.Equal("/uploads/products/a%20b.webp", images[0]);
        Assert.Equal("/assets/image/x.webp", images[1]);
        Assert.Equal(images.Count, images.Distinct().Count());
        Assert.True(images.Count <= 5);
        Assert.Null(SitemapService.Images("products", null, " "));
    }

    // ── سیگنال‌های ربات و سرعت ─────────────────────────────────────────

    [Theory]
    [Trait("Area", "Signals")]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", "Googlebot")]
    [InlineData("Mozilla/5.0 (compatible; bingbot/2.0; +http://www.bing.com/bingbot.htm)", "Bingbot")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0", null)]
    [InlineData(null, null)]
    public void Search_bots_are_detected_by_user_agent(string? userAgent, string? bot) =>
        Assert.Equal(bot, SeoBots.Detect(userAgent)?.Name);

    [Fact]
    [Trait("Area", "Signals")]
    public void Fake_googlebot_is_caught_by_reverse_dns_host()
    {
        var google = SeoBots.Detect("Googlebot/2.1")!;
        Assert.True(SeoBots.HostMatches(google, "crawl-66-249-66-1.googlebot.com"));
        Assert.False(SeoBots.HostMatches(google, "googlebot.com.evil.example"));
        Assert.False(SeoBots.HostMatches(google, "scraper.example"));
    }

    [Theory]
    [Trait("Area", "Signals")]
    [InlineData("/", true)]
    [InlineData("/product/a", true)]
    [InlineData("/sitemap.xml", true)]
    [InlineData("/robots.txt", true)]
    [InlineData("/assets/js/main.js", false)]
    [InlineData("/uploads/products/a.webp", false)]
    [InlineData("/admin/seo", false)]
    [InlineData("/api/rum", false)]
    [InlineData("/media/og/uploads/a.webp", false)]
    public void Bot_log_counts_only_pages(string path, bool expected) =>
        Assert.Equal(expected, BotLogMiddleware.IsPage(new PathString(path)));

    [Fact]
    [Trait("Area", "Signals")]
    public void Vitals_use_google_thresholds_and_reject_garbage()
    {
        Assert.Equal(1, SeoVitals.Rate("LCP", 2000));
        Assert.Equal(0, SeoVitals.Rate("LCP", 3000));
        Assert.Equal(-1, SeoVitals.Rate("LCP", 5000));
        Assert.Equal(1, SeoVitals.Rate("CLS", 0.05));
        Assert.False(SeoVitals.IsValid("LCP", -1));
        Assert.False(SeoVitals.IsValid("LCP", double.NaN));
        Assert.False(SeoVitals.IsValid("LCP", 1e9));
        Assert.False(SeoVitals.IsValid("XSS", 1));
        Assert.Equal(0, SeoVitals.P75([]));
        Assert.Equal(4, SeoVitals.P75([1, 2, 3, 4, 5]));
    }

    [Fact]
    [Trait("Area", "Signals")]
    public void Rum_body_is_accepted_only_with_known_types_and_metrics()
    {
        var buffer = new SeoSignalBuffer();
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(3, RumEndpoint.Accept("{\"t\":\"product\",\"d\":\"mobile\",\"m\":{\"LCP\":2100,\"CLS\":0.02,\"INP\":120}}", buffer, now));
        Assert.Equal(1, RumEndpoint.Accept("{\"t\":\"home\",\"d\":\"desktop\",\"m\":{\"LCP\":900,\"EVIL\":1,\"FCP\":\"x\"}}", buffer, now));
        Assert.Equal(0, RumEndpoint.Accept("{\"t\":\"<script>\",\"d\":\"mobile\",\"m\":{\"LCP\":1}}", buffer, now));
        Assert.Equal(0, RumEndpoint.Accept("{\"t\":\"home\",\"d\":\"tv\",\"m\":{\"LCP\":1}}", buffer, now));
        Assert.Equal(0, RumEndpoint.Accept("[1,2,3]", buffer, now));
        Assert.Equal(0, RumEndpoint.Accept("{broken", buffer, now));
        Assert.Equal(0, RumEndpoint.Accept(new string(' ', 10) + "{\"t\":\"home\",\"d\":\"mobile\",\"m\":{\"LCP\":1}}" + new string(' ', RumEndpoint.MaxBody), buffer, now));

        Assert.Equal(4, buffer.Drain().Vitals.Count);
    }

    [Fact]
    [Trait("Area", "Signals")]
    public void Rum_is_ignored_when_switched_off()
    {
        var buffer = new SeoSignalBuffer { VitalsEnabled = false };
        Assert.Equal(0, RumEndpoint.Accept("{\"t\":\"home\",\"d\":\"mobile\",\"m\":{\"LCP\":1000}}", buffer, DateTimeOffset.UtcNow));
    }

    [Fact]
    [Trait("Area", "Signals")]
    public void P75_merge_is_a_sample_weighted_average()
    {
        Assert.Equal(2500, SeoSignalWriter.MergeP75(2000, 10, 3000, 10));
        Assert.Equal(3000, SeoSignalWriter.MergeP75(0, 0, 3000, 4));
        Assert.Equal(2000, SeoSignalWriter.MergeP75(2000, 4, 9999, 0));
    }

    // ── زمان‌بندی شبانه و صف ─────────────────────────────────────────────

    [Fact]
    [Trait("Area", "Worker")]
    public void Nightly_job_runs_soon_after_first_start_then_once_a_day_at_the_configured_hour()
    {
        var started = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
        Assert.False(SeoAutopilotWorker.IsNightlyDue(null, started.AddMinutes(1), started, 4));
        Assert.True(SeoAutopilotWorker.IsNightlyDue(null, started.AddMinutes(4), started, 4));

        // ۴ صبح تهران = ۰:۳۰ UTC
        var lastRun = new DateTimeOffset(2026, 11, 4, 0, 30, 0, TimeSpan.Zero);
        Assert.False(SeoAutopilotWorker.IsNightlyDue(lastRun, lastRun.AddHours(10), started, 4));
        Assert.False(SeoAutopilotWorker.IsNightlyDue(lastRun, lastRun.AddHours(22).AddMinutes(30), started, 4)); // ۲:۳۰ بامداد تهران
        Assert.True(SeoAutopilotWorker.IsNightlyDue(lastRun, lastRun.AddDays(1).AddMinutes(5), started, 4));
        Assert.True(SeoAutopilotWorker.IsNightlyDue(lastRun, lastRun.AddHours(31), started, 23)); // سرور خاموش بوده: جبران
    }

    [Fact]
    [Trait("Area", "Worker")]
    public void Queue_deduplicates_pages_and_manual_runs()
    {
        var queue = new SeoAutopilotQueue();
        var id = Guid.NewGuid();

        queue.Enqueue([new SeoChange(SeoEntityKind.Product, id, false), new SeoChange(SeoEntityKind.Product, id, true)]);
        queue.Enqueue([new SeoChange(SeoEntityKind.BlogPost, id, false)]);

        Assert.Equal(2, queue.Pending);
        Assert.True(queue.RequestNightly());
        Assert.False(queue.RequestNightly());
    }

    // ── تصویرها ─────────────────────────────────────────────────────────

    [Theory]
    [Trait("Area", "Images")]
    [InlineData("/uploads/products/a.webp", "/media/og/uploads/products/a.webp")]
    [InlineData("/uploads/products/a.webp?v=2", "/media/og/uploads/products/a.webp")]
    [InlineData("https://cdn.example/a.webp", "https://cdn.example/a.webp")]
    [InlineData("/assets/brand/og-default.png", "/assets/brand/og-default.png")]
    [InlineData("/uploads/../appsettings.json", "/uploads/../appsettings.json")]
    public void Share_image_url_wraps_only_local_images(string input, string expected) =>
        Assert.Equal(expected, OgImageRenderer.Url(input));

    [Fact]
    [Trait("Area", "Images")]
    public void Share_image_is_a_1200x630_webp()
    {
        using var bitmap = new SkiaSharp.SKBitmap(300, 500);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
            canvas.Clear(SkiaSharp.SKColors.OrangeRed);
        using var png = SkiaSharp.SKImage.FromBitmap(bitmap).Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);

        var output = OgImageRenderer.Render(png.ToArray());

        Assert.NotNull(output);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(output!, 0, 4));
        Assert.Equal("WEBP", Encoding.ASCII.GetString(output, 8, 4));
        using var decoded = SkiaSharp.SKBitmap.Decode(output);
        Assert.Equal(OgImageRenderer.Width, decoded.Width);
        Assert.Equal(OgImageRenderer.Height, decoded.Height);
    }

    [Fact]
    [Trait("Area", "Images")]
    public void Share_image_rejects_non_images()
    {
        Assert.Null(OgImageRenderer.Render(Encoding.UTF8.GetBytes("<svg onload=alert(1)>")));
        Assert.Null(OgImageRenderer.Render([]));
    }

    [Theory]
    [Trait("Area", "Images")]
    [InlineData("/uploads/media/a.webp", 180, "/media/icon/180/uploads/media/a.webp")]
    [InlineData("/uploads/media/a.webp", 100, "/media/icon/128/uploads/media/a.webp")]
    [InlineData("/uploads/media/a.webp", 9999, "/media/icon/512/uploads/media/a.webp")]
    [InlineData("/uploads/media/a.png", 180, "/uploads/media/a.png")]
    [InlineData("https://x.example/a.webp", 180, "https://x.example/a.webp")]
    public void Raster_icons_are_only_made_for_local_webp(string url, int size, string expected) =>
        Assert.Equal(expected, Fratak_Shop.Web.Infrastructure.Media.MediaEndpoints.RasterIcon(url, size));

    [Fact]
    [Trait("Area", "Images")]
    public void Legacy_image_urls_redirect_only_to_existing_webp_inside_allowed_folders()
    {
        var root = Path.Combine(Path.GetTempPath(), "fratak-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "uploads", "products"));
        try
        {
            File.WriteAllBytes(Path.Combine(root, "uploads", "products", "a.webp"), [1]);
            File.WriteAllBytes(Path.Combine(root, "uploads", "products", "b.jpg"), [1]);

            var target = Fratak_Shop.Web.Infrastructure.Media.LegacyImageRedirectMiddleware.Target;
            Assert.Equal("/uploads/products/a.webp", target("/uploads/products/a.jpg", root));
            Assert.Equal("/uploads/products/a.webp", target("/uploads/products/a.PNG", root));
            Assert.Null(target("/uploads/products/b.jpg", root));      // فایل اصلی هنوز هست
            Assert.Null(target("/uploads/products/c.jpg", root));      // WebP ندارد
            Assert.Null(target("/uploads/../uploads/products/a.jpg", root));
            Assert.Null(target("/secret/a.jpg", root));
            Assert.Null(target("/uploads/products/a.txt", root));
            Assert.Null(target(null, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Area", "Images")]
    public void Uploads_are_always_stored_as_webp()
    {
        using var bitmap = new SkiaSharp.SKBitmap(64, 48);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
            canvas.Clear(SkiaSharp.SKColors.SteelBlue);
        using var jpeg = SkiaSharp.SKImage.FromBitmap(bitmap).Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);

        var image = Fratak_Shop.Web.Infrastructure.Media.WebPEncoder.ToWebP(jpeg.ToArray());

        Assert.NotNull(image);
        Assert.Equal("WEBP", Encoding.ASCII.GetString(image!.Data, 8, 4));
        Assert.Null(Fratak_Shop.Web.Infrastructure.Media.WebPEncoder.ToWebP(Encoding.UTF8.GetBytes("GIF89a<script>")));
    }

    // ── متاتگ ترب صفحهٔ محصول ───────────────────────────────────────────

    [Fact]
    [Trait("Area", "Torob")]
    public void Product_page_meta_tags_match_the_torob_feed()
    {
        var dto = new Fratak_Shop.Core.DTOs.Products.Query.ProductDto
        {
            Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Name = "گوشی A55",
            Price = 1_000_000,
            FinalPrice = 900_000,
            HasActiveDiscount = true,
            StockQuantity = 2,
        };

        var meta = Fratak_Shop.Web.Pages.ProductModel.TorobMeta(dto).ToDictionary(p => p.Key, p => p.Value);

        Assert.Equal("11111111222233334444555555555555", meta["product_id"]);
        Assert.Equal("900000", meta["product_price"]);
        Assert.Equal("1000000", meta["product_old_price"]);
        Assert.Equal("instock", meta["availability"]);

        dto.StockQuantity = 0;
        var soldOut = Fratak_Shop.Web.Pages.ProductModel.TorobMeta(dto).ToDictionary(p => p.Key, p => p.Value);
        Assert.Equal("0", soldOut["product_price"]);
        Assert.Equal("outofstock", soldOut["availability"]);
        Assert.False(soldOut.ContainsKey("product_old_price"));
    }
    // ── صفحه‌های مدیریت ────────────────────────────────────────────────

    [Fact]
    [Trait("Area", "AdminPages")]
    public void Revision_diff_lists_only_changed_fields_as_plain_text()
    {
        var diff = Fratak_Shop.Web.Areas.Admin.Pages.Seo.HistoryModel.Diff(
            "{\"Title\":\"قدیم\",\"Slug\":\"a\",\"Body\":\"<p>متن</p>\"}",
            "{\"Title\":\"جدید\",\"Slug\":\"a\",\"Body\":\"<p>متن <b>تازه</b></p>\",\"Alt\":\"x\"}");

        Assert.Equal(3, diff.Count);
        Assert.Contains(diff, d => d.Field == "Title" && d.Before == "قدیم" && d.After == "جدید");
        Assert.Contains(diff, d => d.Field == "Alt" && d.Before is null);
        Assert.All(diff, d => Assert.DoesNotContain("<", d.After ?? "", StringComparison.Ordinal));
        Assert.Empty(Fratak_Shop.Web.Areas.Admin.Pages.Seo.HistoryModel.Diff("not json", "[1]"));
    }

    [Theory]
    [Trait("Area", "AdminPages")]
    [InlineData("product")]
    [InlineData("home")]
    [InlineData("unknown-type")]
    public void Page_type_titles_are_never_empty(string type) =>
        Assert.False(string.IsNullOrWhiteSpace(Fratak_Shop.Web.Areas.Admin.Pages.Seo.SignalsModel.PageTypeTitle(type)));
}
