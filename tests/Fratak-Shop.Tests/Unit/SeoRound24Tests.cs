using Fratak_Shop.Core.Services.Contents;
using Fratak_Shop.Core.Services.Pricing;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Suppliers;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Contents;
using Fratak_Shop.Web.Infrastructure.Admin.Manage;
using Fratak_Shop.Web.Pages;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های واحد دور ۲۴ بستهٔ E (سئو) – بدون دیتابیس:
/// <list type="bullet">
/// <item>۴۰/۴۱ صفحه‌های فرود «دسته + برند» و «زیر X تومان» (ListingLandings).</item>
/// <item>۴۲/۴۳/۴۴ واژه‌نامه، راهنمای جامع، مناسبت‌ها (SeoHubService، SeoCalendar، GlossaryCatalog، صفحه‌ها).</item>
/// <item>۴۵ ایمالز (پارسر JSON/HTML، پیوند امن، سرویس با HTTP جعلی).</item>
/// <item>۴۶ کد نماد اعتماد (اینماد/ساماندهی، حمله‌های اسکریپت و نشانی ناامن).</item>
/// <item>۴۷ توضیح ویدیو در نقشهٔ سایت.</item>
/// <item>۴۸/۴۹/۵۰ فرصت‌های سئو (دلیل کم‌بازدیدی، نزدیک‌ترین مقصد جست‌وجو، مترادف امن، هم‌نوع‌خواری).</item>
/// </list>
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "SEO")]
public class SeoRound24Tests
{
    // ── ۴۰/۴۱: ListingLandings ─────────────────────────────────────────

    [Fact]
    public void Brand_path_title_and_description_are_built_from_real_data()
    {
        Assert.Equal("/category/headphone/brand/haylou", ListingLandings.BrandPath(" headphone ", "haylou"));
        Assert.Equal("/category/a%20b/brand/c", ListingLandings.BrandPath("a b", "c"));
        Assert.Equal("خرید هدفون هایلو", ListingLandings.BrandTitle(" هدفون ", "هایلو "));

        var description = ListingLandings.BrandDescription("هدفون", "هایلو", 7, "فراتک");
        Assert.StartsWith("خرید هدفون هایلو از فراتک:", description);
        Assert.Contains("۷ مدل", description);
        Assert.DoesNotContain(" از :", ListingLandings.BrandDescription("هدفون", "هایلو", 7, null));
        Assert.StartsWith("خرید هدفون هایلو:", ListingLandings.BrandDescription("هدفون", "هایلو", 7, "  "));
        Assert.Contains("۳ مدل هدفون برند هایلو", ListingLandings.BrandIntro("هدفون", "هایلو", 3));
    }

    [Theory]
    [InlineData(3, 1, false, true)]
    [InlineData(12, 0, false, true)]
    [InlineData(2, 1, false, false)]   // کم‌محتوا
    [InlineData(3, 2, false, false)]   // صفحهٔ دوم
    [InlineData(9, 1, true, false)]    // فیلتر دیگر
    public void Brand_landing_is_indexable_only_when_rich_and_unfiltered(int count, int page, bool filtered, bool expected)
        => Assert.Equal(expected, ListingLandings.BrandIndexable(count, page, filtered));

    [Fact]
    public void Price_band_matches_only_the_exact_linked_band()
    {
        var bands = new List<SeoLandingLink>
        {
            new("خرید گوشی زیر ۵ میلیون تومان", "/category/mobile?maxPrice=5000000", 8),
            new("خرید گوشی زیر ۱۵ میلیون تومان", "/category/mobile?maxPrice=15000000", 20),
        };
        Assert.Equal(bands[0], ListingLandings.MatchPriceBand(bands, 5_000_000m));
        Assert.Equal(bands[1], ListingLandings.MatchPriceBand(bands, 15_000_000m));
        Assert.Null(ListingLandings.MatchPriceBand(bands, 4_900_000m));
        Assert.Null(ListingLandings.MatchPriceBand(bands, 5_000_000.5m));
        Assert.Null(ListingLandings.MatchPriceBand(bands, 500_000m));       // پسوند «…=500000» نباید با «…=5000000» یکی شود
        Assert.Null(ListingLandings.MatchPriceBand([], 5_000_000m));

        var description = ListingLandings.PriceDescription("خرید گوشی زیر ۵ میلیون تومان", 12, "فراتک");
        Assert.StartsWith("خرید گوشی زیر ۵ میلیون تومان از فراتک:", description);
        Assert.Contains("۱۲ کالای موجود", description);
    }

    // ── ۴۷: توضیح ویدیو ─────────────────────────────────────────────────

    [Fact]
    public void Video_description_combines_title_and_plain_summary()
    {
        var both = SitemapService.VideoDescription("آنباکس", "هدفون T15", "<p>هدفون بی‌سیم <b>خوب</b></p>");
        Assert.StartsWith("آنباکس – هدفون", both);
        Assert.Contains("خوب", both);
        Assert.DoesNotContain("<", both);
        Assert.Equal("ویدیوی معرفی هدفون T15", SitemapService.VideoDescription(null, " هدفون T15 ", null));
        Assert.Equal("آنباکس – ویدیوی معرفی هدفون", SitemapService.VideoDescription("آنباکس", "هدفون", "  "));
        Assert.Equal("خلاصه", SitemapService.VideoDescription("  ", "x", "خلاصه"));

        var longText = SitemapService.VideoDescription("t", "p", new string('ب', 900));
        Assert.True(longText.Length <= 300);
        Assert.EndsWith("…", longText);
    }

    // ── ۴۲/۴۳/۴۴: هاب‌ها ─────────────────────────────────────────────────

    private static SeoHubService.ProductFact SpecOf(Guid product, string category, string key, string value)
        => new(product, "دسته " + category, category, SpecKnowledge.Read(new SeoFeature(key, value))!);

    [Fact]
    public void Glossary_lists_only_topics_with_real_products_most_used_first()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        var facts = new List<SeoHubService.ProductFact>
        {
            SpecOf(a, "headphone", "شارژدهی", "20 ساعت"),
            SpecOf(b, "headphone", "شارژدهی", "30 ساعت"),
            SpecOf(c, "speaker", "باتری", "4000 میلی‌آمپر ساعت"),
            SpecOf(c, "speaker", "مقاومت در برابر آب", "IPX7"),
            SpecOf(c, "speaker", "کشور سازنده", "چین"), // Generic: در واژه‌نامه نیست
        };
        var terms = SeoHubService.BuildGlossary(facts);
        Assert.Equal(2, terms.Count);
        Assert.Equal("battery-life", terms[0].Slug);
        Assert.Equal(3, terms[0].ProductCount);
        Assert.Equal("headphone", terms[0].CategorySlug);
        Assert.Equal("ip-rating", terms[1].Slug);
        Assert.Equal("speaker", terms[1].CategorySlug);
        Assert.Empty(SeoHubService.BuildGlossary([]));
        Assert.False(new GlossaryView(terms).Indexable);
    }

    [Fact]
    public void Glossary_catalog_slugs_are_unique_english_and_definitions_real()
    {
        var slugs = GlossaryCatalog.Terms.Values.Select(t => t.Slug).ToList();
        Assert.Equal(slugs.Count, slugs.Distinct().Count());
        Assert.All(slugs, s => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", s));
        Assert.All(GlossaryCatalog.Terms.Values, t => Assert.True(t.Definition.Length > 60, t.Slug));
        Assert.DoesNotContain(SpecTopic.Generic, GlossaryCatalog.Terms.Keys);
    }

    [Fact]
    public void Spec_ranges_need_two_products_and_a_real_spread()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        var facts = new List<SeoHubService.ProductFact>
        {
            SpecOf(a, "h", "شارژدهی", "10 ساعت"),
            SpecOf(b, "h", "شارژدهی", "40 ساعت"),
            SpecOf(a, "h", "وزن", "50 گرم"),
            SpecOf(b, "h", "وزن", "50 گرم"),          // بدون اختلاف ← بازه نیست
            SpecOf(c, "h", "حافظه داخلی", "128 گیگابایت"), // فقط یک کالا ← بازه نیست
        };
        var ranges = SeoHubService.BuildSpecRanges(facts);
        var range = Assert.Single(ranges);
        Assert.Equal("شارژدهی", range.Name);
        Assert.Equal("10 ساعت", range.Lowest);
        Assert.Equal("40 ساعت", range.Highest);
        Assert.Equal(2, range.Products);
        Assert.Equal("battery-life", range.TermSlug);
    }

    private static CategoryGuideView Guide(int count, int brands, int bands, int specs, int articles) => new(
        Guid.NewGuid(), "هدفون", "headphone", null, null,
        new Fratak_Shop.Core.Services.Categories.CategoryFacts(count, count, 100_000m, 900_000m, 0, []),
        Enumerable.Range(0, brands).Select(i => ($"برند {i}", $"b{i}", 3)).ToList(),
        Enumerable.Range(0, bands).Select(i => new SeoLandingLink($"زیر {i}", $"/category/headphone?maxPrice={i + 1}000000", 6)).ToList(),
        Enumerable.Range(0, specs).Select(i => new SpecRange($"مشخصه {i}", "1", "2", 2, null)).ToList(),
        [], [],
        Enumerable.Range(0, articles).Select(i => ($"مقاله {i}", $"post-{i}")).ToList(),
        []);

    [Theory]
    [InlineData(6, 2, 1, 0, 0, true)]
    [InlineData(6, 1, 0, 1, 1, true)]
    [InlineData(5, 3, 2, 2, 2, false)]  // کمتر از ۶ کالا
    [InlineData(20, 1, 1, 0, 0, false)] // فقط یک بخش واقعی (یک برند حساب نمی‌شود)
    [InlineData(20, 0, 0, 0, 0, false)]
    public void Guide_is_indexable_only_with_enough_products_and_two_real_sections(int count, int brands, int bands, int specs, int articles, bool expected)
        => Assert.Equal(expected, Guide(count, brands, bands, specs, articles).Indexable);

    [Fact]
    public void Guide_description_uses_real_numbers()
    {
        var text = GuideModel.GuideDescription(Guide(24, 6, 1, 2, 0));
        Assert.StartsWith("راهنمای خرید هدفون: مقایسهٔ ۲۴ مدل از ۶ برند", text);
        Assert.Contains("قیمت از 100,000 تومان تا 900,000 تومان", text);
        Assert.Contains("معنی مشخصات مهم", text);
        Assert.DoesNotContain("برند", GuideModel.GuideDescription(Guide(24, 1, 0, 0, 0)));
    }

    [Fact]
    public void Occasion_calendar_finds_fixed_slugs_case_insensitively()
    {
        var today = new DateOnly(2026, 10, 22);
        var yalda = SeoCalendar.Find(today, " YALDA ");
        Assert.NotNull(yalda);
        Assert.Equal("شب یلدا", yalda!.Name);
        Assert.Equal(new DateOnly(2026, 12, 21), yalda.Date);
        Assert.Equal(60, yalda.DaysLeft);
        Assert.Equal("yalda", yalda.Slug);
        Assert.Null(SeoCalendar.Find(today, null));
        Assert.Null(SeoCalendar.Find(today, ""));
        Assert.Null(SeoCalendar.Find(today, "../admin"));
        Assert.Null(SeoCalendar.Find(today, "unknown-day"));

        var all = SeoCalendar.All(today);
        Assert.Equal(10, all.Count);
        Assert.Equal(all.Count, all.Select(o => o.Slug).Distinct().Count());
        Assert.All(all, o =>
        {
            Assert.Matches("^[a-z]+(-[a-z]+)*$", o.Slug);
            Assert.True(o.Date >= today && o.DaysLeft <= 400);
        });
        Assert.Equal(all.OrderBy(o => o.Date).Select(o => o.Slug), all.Select(o => o.Slug));
    }

    [Fact]
    public void Occasion_texts_are_factual()
    {
        Assert.Equal("۳۰ آذر", SeoHubService.ShamsiLabel(new DateOnly(2026, 12, 21)));
        Assert.Equal("۱ فروردین", SeoHubService.ShamsiLabel(new DateOnly(2026, 3, 21)));

        var occasion = new SeoOccasion("شب یلدا", new DateOnly(2026, 12, 21), 60, [], "yalda");
        var intro = SeoHubService.OccasionIntro(occasion, "۳۰ آذر", 12);
        Assert.StartsWith("تا شب یلدا (۳۰ آذر) ۶۰ روز مانده است.", intro);
        Assert.Contains("۱۲ کالای موجود", intro);
        Assert.StartsWith("امروز شب یلدا است.", SeoHubService.OccasionIntro(occasion with { DaysLeft = 0 }, "۳۰ آذر", 1));
        Assert.StartsWith("فردا شب یلدا است", SeoHubService.OccasionIntro(occasion with { DaysLeft = 1 }, "۳۰ آذر", 1));
        Assert.Contains("به‌زودی", SeoHubService.OccasionIntro(occasion, "۳۰ آذر", 0));

        Assert.Equal(2, SeoHubService.OccasionQuestions(occasion, "۳۰ آذر", 0).Count);
        var questions = SeoHubService.OccasionQuestions(occasion, "۳۰ آذر", 9);
        Assert.Equal(3, questions.Count);
        Assert.Contains("۶۰ روز دیگر", questions[0].Answer);
        Assert.Contains("امروز", SeoHubService.OccasionQuestions(occasion with { DaysLeft = 0 }, "۳۰ آذر", 0)[0].Answer);

        Assert.True(new OccasionView(occasion, "", "", Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList(), [], []).Indexable);
        Assert.False(new OccasionView(occasion, "", "", [Guid.NewGuid()], [], []).Indexable);

        Assert.Equal("پیشنهاد خرید و هدیهٔ شب یلدا", OccasionModel.OccasionTitle(occasion));
        Assert.Equal("تخفیف‌های بلک فرایدی", OccasionModel.OccasionTitle(occasion with { Name = "بلک فرایدی", Slug = "black-friday" }));
        Assert.Equal("خرید برای بازگشایی مدارس", OccasionModel.OccasionTitle(occasion with { Name = "بازگشایی مدارس", Slug = "back-to-school" }));
    }

    // ── ۴۵: ایمالز ──────────────────────────────────────────────────────

    [Fact]
    public void Emalls_json_is_parsed_with_persian_digits_and_bad_rows_skipped()
    {
        const string json = """
            {"lstsearchresualt":[
              {"title":"هدفون هایلو T15","price":"۲,۹۵۰,۰۰۰","shopname":"صوتی‌شاپ","id":"123"},
              {"title":"هدفون صفر","price":"0","shopname":"x"},
              {"title":"","price":"100000"},
              {"name":"اسپیکر","price":1850000,"shop":"9981","url":"/مشخصات_x~id~77"},
              "not-an-object"
            ]}
            """;
        var offers = CompetitorParsers.Emalls(json);
        Assert.Equal(2, offers.Count);
        Assert.Equal("ایمالز – صوتی‌شاپ", offers[0].Shop);
        Assert.Equal(2_950_000m, offers[0].Price);
        Assert.Equal(CompetitorPriceService.Emalls, offers[0].Source);
        Assert.Equal("https://emalls.ir/~id~123", offers[0].Link);
        Assert.Equal("هدفون هایلو T15", offers[0].Title);
        Assert.Equal("ایمالز", offers[1].Shop); // نام فروشگاه فقط عدد ← بدون نام
        Assert.Equal(1_850_000m, offers[1].Price);
        Assert.StartsWith("https://emalls.ir/", offers[1].Link);
        Assert.Contains("~id~77", offers[1].Link);
    }

    [Fact]
    public void Emalls_unknown_root_html_fallback_and_garbage()
    {
        Assert.Single(CompetitorParsers.Emalls("""{"items":[{"title":"کالا","price":"1,000,000"}]}"""));
        Assert.Single(CompetitorParsers.Emalls("""[{"title":"کالا","price":"1000000"}]"""));
        Assert.Empty(CompetitorParsers.Emalls("{oops"));
        Assert.Empty(CompetitorParsers.Emalls(""));
        Assert.Empty(CompetitorParsers.Emalls("""{"lstsearchresualt":"none"}"""));

        const string html = """
            <html><body>
              <a href="/مشخصات_هدفون~id~55"><img alt="هدفون T15" src="a.jpg"><span>۱,۲۰۰,۰۰۰ تومان</span></a>
              <a href="/about">درباره ۱,۰۰۰,۰۰۰</a>
              <a href="javascript:alert(1)~id~9"><img alt="بد" src="b.jpg"><span>۵۰۰,۰۰۰</span></a>
            </body></html>
            """;
        var offers = CompetitorParsers.Emalls(html);
        Assert.Equal(2, offers.Count);
        Assert.Equal(1_200_000m, offers[0].Price);
        Assert.Equal("هدفون T15", offers[0].Title);
        Assert.Contains("~id~55", offers[0].Link);
        Assert.Null(offers[1].Link); // پیوند javascript: هرگز ذخیره نمی‌شود
    }

    [Theory]
    [InlineData("/x~id~1", null, "https://emalls.ir/x~id~1")]
    [InlineData("https://www.emalls.ir/a", null, "https://www.emalls.ir/a")]
    [InlineData("https://evil.com/a", "77", "https://emalls.ir/~id~77")]
    [InlineData("https://emalls.ir.evil.com/a", null, null)]
    [InlineData("http://emalls.ir/a", null, null)]
    [InlineData("javascript:alert(1)", null, null)]
    [InlineData(null, "12a", null)]
    [InlineData(null, "1234567890123", null)]
    [InlineData(null, null, null)]
    public void Emalls_link_is_always_safe(string? url, string? id, string? expected)
        => Assert.Equal(expected, CompetitorParsers.EmallsLink(url, id));

    private sealed class FormHttp : ISupplierHttp
    {
        public List<(string Url, IReadOnlyDictionary<string, string> Form)> Posts { get; } = [];

        public Task<SupplierHttpResponse> GetAsync(string url, bool expectJson, CancellationToken cancellationToken)
            => Task.FromResult(new SupplierHttpResponse(404, null, "not found"));

        public Task<SupplierHttpResponse> PostFormAsync(string url, IReadOnlyDictionary<string, string> form, bool expectJson, CancellationToken cancellationToken)
        {
            lock (Posts)
                Posts.Add((url, form));
            return Task.FromResult(new SupplierHttpResponse(200,
                """{"lstsearchresualt":[{"title":"هدفون هایلو T15 مشکی","price":"2,950,000","shopname":"فروشگاه الف","id":"5"},{"title":"هدفون هایلو T15","price":"2,900,000","shopname":"فروشگاه الف","id":"6"},{"title":"ساعت هوشمند","price":"1,000,000","shopname":"ب","id":"7"}]}"""));
        }
    }

    [Fact]
    public async Task Service_reads_emalls_by_form_post_admin_only_offers()
    {
        var http = new FormHttp();
        var service = new CompetitorPriceService(http, new MemoryCache(new MemoryCacheOptions()), new ExternalSourceGate(0));
        var lookup = await service.SearchAsync("هدفون هایلو T15", CancellationToken.None);

        var post = Assert.Single(http.Posts);
        Assert.Equal(CompetitorPriceService.EmallsSearchUrl, post.Url);
        Assert.Contains("هایلو", post.Form["findstr"]);
        Assert.Equal("listitemv2", post.Form["entekhab"]);
        var emalls = lookup.Offers.Where(o => o.Source == CompetitorPriceService.Emalls).ToList();
        var offer = Assert.Single(emalls); // یک فروشنده فقط یک بار؛ کالای نامرتبط حذف
        Assert.Equal("ایمالز – فروشگاه الف", offer.Shop);
        Assert.DoesNotContain(lookup.Problems, p => p.StartsWith(CompetitorPriceService.Emalls, StringComparison.Ordinal));
    }

    private sealed class BlockedHttp : ISupplierHttp
    {
        public Task<SupplierHttpResponse> GetAsync(string url, bool expectJson, CancellationToken cancellationToken)
            => Task.FromResult(new SupplierHttpResponse(404, null, "not found"));

        public Task<SupplierHttpResponse> PostFormAsync(string url, IReadOnlyDictionary<string, string> form, bool expectJson, CancellationToken cancellationToken)
            => Task.FromResult(new SupplierHttpResponse(429, null, null));
    }

    [Fact]
    public async Task Emalls_limit_is_reported_but_plain_failure_is_silent()
    {
        var blocked = await new CompetitorPriceService(new BlockedHttp(), new MemoryCache(new MemoryCacheOptions()), new ExternalSourceGate(0))
            .SearchAsync("هدفون هایلو T15", CancellationToken.None);
        Assert.True(blocked.Limited);
        Assert.Contains(blocked.Problems, p => p.StartsWith(CompetitorPriceService.Emalls, StringComparison.Ordinal));

        // HTTP بدون پشتیبانی POST (پیاده‌سازی پیش‌فرض) ← بی‌صدا، بدون پیام اضافه.
        var silent = await new CompetitorPriceService(new NoPostHttp(), new MemoryCache(new MemoryCacheOptions()), new ExternalSourceGate(0))
            .SearchAsync("هدفون هایلو T15", CancellationToken.None);
        Assert.DoesNotContain(silent.Problems, p => p.StartsWith(CompetitorPriceService.Emalls, StringComparison.Ordinal));
    }

    private sealed class NoPostHttp : ISupplierHttp
    {
        public Task<SupplierHttpResponse> GetAsync(string url, bool expectJson, CancellationToken cancellationToken)
            => Task.FromResult(new SupplierHttpResponse(404, null, "not found"));
    }

    // ── ۴۶: کد نماد اعتماد ──────────────────────────────────────────────

    private const string Enamad =
        "<a referrerpolicy='origin' target='_blank' href='https://trustseal.enamad.ir/?id=123&Code=abc'><img referrerpolicy='origin' src='https://trustseal.enamad.ir/logo.aspx?id=123&Code=abc' alt='' style='cursor:pointer' code='abc'></a>";

    private const string Samandehi =
        "<img referrerpolicy='origin' id='nbqe' style='cursor:pointer' onclick='window.open(\"https://logo.samandehi.ir/Verify.aspx?id=1&p=x\", \"Popup\",\"toolbar=no\")' alt='logo-samandehi' src='https://logo.samandehi.ir/logo.aspx?id=1&p=y' />";

    [Fact]
    public void Trust_badge_embed_extracts_only_safe_image_and_link()
    {
        var enamad = TrustBadgeEmbed.Parse(Enamad);
        Assert.True(enamad.IsValid);
        Assert.Equal("https://trustseal.enamad.ir/logo.aspx?id=123&Code=abc", enamad.ImageUrl);
        Assert.Equal("https://trustseal.enamad.ir/?id=123&Code=abc", enamad.LinkUrl);

        var samandehi = TrustBadgeEmbed.Parse(Samandehi);
        Assert.True(samandehi.IsValid);
        Assert.Equal("https://logo.samandehi.ir/logo.aspx?id=1&p=y", samandehi.ImageUrl);
        Assert.Equal("https://logo.samandehi.ir/Verify.aspx?id=1&p=x", samandehi.LinkUrl);

        var empty = TrustBadgeEmbed.Parse("  ");
        Assert.False(empty.IsValid);
        Assert.Null(empty.Error);
    }

    [Theory]
    [InlineData("<script>alert(1)</script><img src=\"javascript:alert(1)\">")]
    [InlineData("<img src=\"http://trustseal.enamad.ir/logo.aspx\">")]
    [InlineData("<img src=\"https://1.2.3.4/a.png\">")]
    [InlineData("<img src=\"https://user:pass@x.ir/a.png\">")]
    [InlineData("<img src=\"https://localhost/a.png\">")]
    [InlineData("<img src=\"data:image/png;base64,AAAA\">")]
    [InlineData("<iframe src=\"https://evil.ir\"></iframe>")]
    [InlineData("just text")]
    public void Trust_badge_embed_rejects_unsafe_codes(string html)
    {
        var result = TrustBadgeEmbed.Parse(html);
        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Trust_badge_embed_drops_unsafe_link_and_limits_length()
    {
        var result = TrustBadgeEmbed.Parse("<a href=\"javascript:alert(1)\"><img src=\"https://x.ir/a.png\"></a>");
        Assert.True(result.IsValid);
        Assert.Null(result.LinkUrl);
        Assert.NotNull(TrustBadgeEmbed.Parse("<img src=\"https://x.ir/a.png\">" + new string(' ', TrustBadgeEmbed.MaxLength)).Error);
        Assert.Null(TrustBadgeEmbed.SafeUrl("https://" + new string('a', 500) + ".ir/"));
        Assert.Equal("https://logo.samandehi.ir/logo.aspx?id=1", TrustBadgeEmbed.SafeUrl(" https://logo.samandehi.ir/logo.aspx?id=1 "));
    }

    [Fact]
    public void Trust_badge_rules_fill_image_from_embed_or_require_upload()
    {
        var fromEmbed = new TrustBadge { Title = "اینماد", ImageUrl = "", EmbedCode = "  " + Enamad + "  " };
        Assert.Null(TrustBadgeRules.Apply(fromEmbed));
        Assert.Equal("https://trustseal.enamad.ir/logo.aspx?id=123&Code=abc", fromEmbed.ImageUrl);
        Assert.Equal("https://trustseal.enamad.ir/?id=123&Code=abc", fromEmbed.LinkUrl);
        Assert.Equal(Enamad, fromEmbed.EmbedCode);

        var bad = new TrustBadge { Title = "بد", ImageUrl = "/uploads/content/old.webp", EmbedCode = "<script>x</script>" };
        Assert.NotNull(TrustBadgeRules.Apply(bad));
        Assert.Equal("/uploads/content/old.webp", bad.ImageUrl);

        var uploaded = new TrustBadge { Title = "اتحادیه", ImageUrl = "/uploads/content/a.webp", EmbedCode = " " };
        Assert.Null(TrustBadgeRules.Apply(uploaded));
        Assert.Null(uploaded.EmbedCode);

        Assert.NotNull(TrustBadgeRules.Apply(new TrustBadge { Title = "خالی", ImageUrl = "" }));
    }

    // ── ۴۸/۴۹/۵۰: فرصت‌های سئو ──────────────────────────────────────────

    [Fact]
    public void Low_traffic_reasons_are_specific_and_never_empty()
    {
        var poor = SeoOpportunityService.ProductReasons(150, 1, null, " ", null, 0, 0);
        Assert.Equal(7, poor.Count);
        Assert.Contains(poor, r => r.Contains("۱۵۰ نویسه", StringComparison.Ordinal));
        Assert.Contains(poor, r => r.Contains("فقط ۱ تصویر", StringComparison.Ordinal));
        Assert.Contains(poor, r => r.StartsWith("ناموجود", StringComparison.Ordinal));

        var good = SeoOpportunityService.ProductReasons(900, 4, "عنوان", "توضیح", "کلمه", 3, 5);
        Assert.Contains("محتوا کامل است", Assert.Single(good));

        Assert.Equal(5, SeoOpportunityService.PostReasons(100, null, null, null, null).Count);
        Assert.Contains("محتوا کامل است", Assert.Single(SeoOpportunityService.PostReasons(5000, "a.webp", "t", "d", "k")));
    }

    private static readonly List<SynonymCandidate> Candidates =
    [
        new("هدفون بی سیم", "/category/wireless-headphone", 3),
        new("سامسونگ", "/brand/samsung", 2),
        new("گوشی سامسونگ گلکسی A15 مشکی", "/product/galaxy-a15", 1),
    ];

    [Theory]
    [InlineData("هدفن بیسیم", "/category/wireless-headphone")] // غلط تایپی + بدون فاصله
    [InlineData("هدفون بی‌سیم", "/category/wireless-headphone")]
    [InlineData("سامسونگ", "/brand/samsung")]                 // برند بر کالا مقدم است
    [InlineData("گلکسی A15", "/product/galaxy-a15")]
    [InlineData("یخچال ساید", null)]
    [InlineData("", null)]
    [InlineData("x", null)]
    public void Best_match_finds_the_closest_real_page(string query, string? expectedUrl)
        => Assert.Equal(expectedUrl, SeoOpportunityService.BestMatch(query, Candidates)?.Url);

    [Fact]
    public void Synonym_terms_and_validation_are_safe()
    {
        Assert.Equal("ایرپاد, هدفون بی سیم", SeoOpportunityService.SynonymTerms(" ایرپاد ", "هدفون بی سیم"));
        Assert.Equal("ایرپاد, گوشی سامسونگ گلکسی", SeoOpportunityService.SynonymTerms("ایرپاد", "گوشی سامسونگ گلکسی A15 مشکی"));
        Assert.Equal("سامسونگ", SeoOpportunityService.SynonymTerms("سامسونگ", "سامسونگ"));

        Assert.Equal("ایرپاد, هدفون بی سیم", SeoOpportunityService.CleanSynonym("ایرپاد, هدفون بی سیم"));
        Assert.Equal("ایرپاد, هدفون", SeoOpportunityService.CleanSynonym("ايرپاد،هدفون"));
        Assert.Null(SeoOpportunityService.CleanSynonym("<script>, x"));
        Assert.Null(SeoOpportunityService.CleanSynonym("a\"b, c"));
        Assert.Null(SeoOpportunityService.CleanSynonym("تنها"));
        Assert.Null(SeoOpportunityService.CleanSynonym("هدفون, هدفون"));
        Assert.Null(SeoOpportunityService.CleanSynonym(new string('ا', 61) + ", ب"));
        Assert.Null(SeoOpportunityService.CleanSynonym(string.Join(", ", Enumerable.Range(0, 80).Select(i => "w" + i))));
        Assert.Null(SeoOpportunityService.CleanSynonym(null));
    }

    [Fact]
    public void Cannibalization_groups_by_keyword_then_title_without_duplicates()
    {
        var pages = new List<SeoPageRef>
        {
            new("دسته", "هدفون", "/category/headphone", "/admin/manage/categories/edit/1", "هدفون", null),
            new("کالا", "هدفون سونی", "/product/sony", "/admin/products/edit/2", " هدفون ", null),
            new("کالا", "هدفون جی‌بی‌ال", "/product/jbl", "/admin/products/edit/3", "هدفون", null),
            new("مقاله", "راهنما", "/blog/guide", "/admin/blog/edit/4", "راهنمای هدفون", null),
            new("کالا", "گوشی ۱", "/product/p1", null, "گوشی ۱", "خرید گوشی"),
            new("کالا", "گوشی ۲", "/product/p2", null, "گوشی ۲", "خرید گوشی"),
            new("کالا", "x", "/product/x", null, "یکسان", "عنوان یکسان"),
            new("کالا", "y", "/product/y", null, "یکسان", "عنوان یکسان"),     // همان دو صفحه با عنوان هم گروه می‌شوند ← تکرار نشود
            new("کالا", "z", "/product/z", null, "تک", null),
            new("کالا", "z2", "/PRODUCT/Z", null, "تک", null),               // همان صفحه با حروف بزرگ ← گروه نیست
        };
        var groups = SeoOpportunityService.FindCannibalization(pages);
        Assert.Equal(3, groups.Count);

        var headphone = groups.Single(g => g.Keyword == "هدفون");
        Assert.Equal(3, headphone.Pages.Count);
        Assert.Equal("کلمهٔ کلیدی", headphone.Source);
        Assert.Contains("صفحهٔ دسته", headphone.Advice);

        var title = groups.Single(g => g.Source == "عنوان");
        Assert.Equal("خرید گوشی", title.Keyword);
        Assert.Contains("مدل", title.Advice);
        Assert.Single(groups, g => g.Keyword == "یکسان");
    }

    [Fact]
    public void Search_console_groups_and_advice()
    {
        var pages = new Dictionary<string, SeoPageRef>(StringComparer.OrdinalIgnoreCase)
        {
            ["/category/headphone"] = new("دسته", "هدفون", "/category/headphone", "/admin/c", null, null),
            ["/blog/guide"] = new("مقاله", "راهنما", "/blog/guide", "/admin/b", null, null),
        };
        var groups = SeoOpportunityService.ConsoleGroups(
        [
            ("هدفون", "/blog/guide", 10),
            ("هدفون", "/category/headphone", 50),
            ("هدفون", "/landing/old", 7),
            ("گوشی", "/product/b", 9),
        ], pages).ToList();
        var group = Assert.Single(groups);
        Assert.Equal("سرچ کنسول", group.Source);
        Assert.Equal(new[] { "/category/headphone", "/blog/guide", "/landing/old" }, group.Pages.Select(p => p.Url).ToArray());
        Assert.Equal("صفحه", group.Pages[2].Kind);
        Assert.Null(group.Pages[2].AdminUrl);
        Assert.Contains("مقاله", group.Advice);

        Assert.Contains("صفحهٔ اصلی", SeoOpportunityService.Advice([new("دسته", "a", "/a", null), new("صفحه", "b", "/b", null)]));
    }
}
