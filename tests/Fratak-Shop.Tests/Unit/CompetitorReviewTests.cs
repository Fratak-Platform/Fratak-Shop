using Fratak_Shop.Core.Services.Pricing;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Suppliers;
using Fratak_Shop.Core.Utilities;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// مرحلهٔ پانزدهم – قیمت رقبا از همهٔ سایت‌ها، داوری هر قیمت، حذف موقت توسط کارمند و اولویت ترب:
/// <list type="bullet">
/// <item>داوری (Review): کالای دیگر، مدل دیگر، لوازم جانبی، فروشگاه خودمان، تکراری، پرت؛ شناسهٔ ثابت هر پیشنهاد.</item>
/// <item>حذف/حساب کن فقط برای همان محاسبه است و فراخوانی بعدی دوباره داوری می‌کند.</item>
/// <item>مرجع قیمت = ارزان‌ترین فروشندهٔ ترب (حتی اگر سایت دیگری ارزان‌تر باشد) و جایگاه ما در ترب.</item>
/// <item>خواندن قیمت از صفحهٔ فروشگاه‌ها (JSON-LD، متا، ناموجود، واحد پول) و منبع «سایت‌های دیگر» با منبع وب جعلی.</item>
/// </list>
/// </summary>
[Trait("Category", "Unit")]
public class CompetitorReviewTests
{
    private const string Query = "هدفون هایلو T15";
    private const string Title = "هدفون بی سیم هایلو مدل T15";

    private static PricingCosts Costs() => new(0, 0, 0, 1, 0, 0, 30, 8, 2, 1000);

    private static CompetitorOffer Torob(string shop, decimal price, string? title = Title) => new(shop, price, CompetitorPriceService.Torob, null, title);

    private static CompetitorOffer Web(string shop, decimal price, string? title = Title)
        => new(shop, price, CompetitorPriceService.WebShops, $"https://{shop}.example.ir/p/t15", title);

    // ── داوری ──

    [Fact]
    public void Review_explains_every_rejected_offer()
    {
        var offers = new List<CompetitorOffer>
        {
            Torob("فروشگاه A", 280_000),
            Torob("فروشگاه A", 290_000),                                    // تکراری
            Torob("فروشگاه B", 285_000),
            Web("shop-c", 275_000),
            Web("caseshop", 45_000, "قاب هدفون هایلو T15"),                 // لوازم جانبی
            Web("other", 300_000, "هدفون هایلو GT1"),                        // مدل دیگر
            Web("random", 299_000, "اسپیکر بلوتوثی انکر"),                   // کالای دیگر
            new("فراتک", 260_000, CompetitorPriceService.WebShops, "https://www.faratek.ir/product/t15", Title), // خودمان
        };

        var reviewed = PriceAdvisor.Review(offers, new OfferReviewOptions(Query, OwnShops: ["فراتک", "faratek.ir"]));

        Assert.Equal(offers.Count, reviewed.Count);
        Assert.Equal(new[] { 275_000m, 280_000m, 285_000m }, reviewed.Where(r => r.Accepted).Select(r => r.Offer.Price).ToArray());
        Assert.Equal(OfferVerdict.Duplicate, reviewed.Single(r => r.Offer.Price == 290_000).Code);
        Assert.Equal(OfferVerdict.Accessory, reviewed.Single(r => r.Offer.Price == 45_000).Code);
        Assert.Equal(OfferVerdict.Model, reviewed.Single(r => r.Offer.Price == 300_000).Code);
        Assert.Equal(OfferVerdict.Mismatch, reviewed.Single(r => r.Offer.Price == 299_000).Code);
        Assert.Equal(OfferVerdict.Own, reviewed.Single(r => r.Offer.Price == 260_000).Code);
        Assert.All(reviewed, r => Assert.False(string.IsNullOrWhiteSpace(r.Reason)));
        Assert.Equal(100, reviewed.Single(r => r.Offer.Price == 280_000).Match);
        Assert.Equal(reviewed.Count, reviewed.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public void Offer_ids_are_stable_between_searches()
    {
        var a = PriceAdvisor.Review([Torob("فروشگاه A", 280_000), Web("shop-c", 275_000)]);
        var b = PriceAdvisor.Review([Web("shop-c", 275_000), Torob("فروشگاه A", 280_000)]);

        Assert.Equal(a.Select(r => r.Id).OrderBy(x => x), b.Select(r => r.Id).OrderBy(x => x));
        Assert.Matches("^[0-9a-f]{12}$", a[0].Id);
        Assert.NotEqual(PriceAdvisor.OfferId(Torob("فروشگاه A", 280_000)), PriceAdvisor.OfferId(Torob("فروشگاه A", 281_000)));
    }

    [Fact]
    public void Excluding_an_offer_changes_only_this_calculation()
    {
        var offers = new List<CompetitorOffer> { Torob("فروشگاه A", 270_000), Torob("فروشگاه B", 280_000), Torob("فروشگاه C", 300_000) };
        var first = PriceAdvisor.Suggest(100_000, 0, Costs(), offers, new OfferReviewOptions(Query));
        Assert.Equal(270_000, first.Reference);
        var wrongUrl = first.Reviewed!.Single(r => r.Offer.Price == 270_000).Id;

        // کارمند «فروشگاه A» را حذف کرد (مثلاً لینکش کالای دیگری بود) ← مرجع ۲۸۰ هزار.
        var second = PriceAdvisor.Suggest(100_000, 0, Costs(), offers, new OfferReviewOptions(Query, Excluded: [wrongUrl]));
        Assert.Equal(280_000, second.Reference);
        Assert.Equal(274_000, second.Suggested);
        Assert.Equal(OfferVerdict.Excluded, second.Reviewed!.Single(r => r.Id == wrongUrl).Code);

        // دفعهٔ بعد (بدون حذف) همان قیمت دوباره حساب می‌شود؛ هیچ چیز ذخیره نشده است.
        var third = PriceAdvisor.Suggest(100_000, 0, Costs(), offers, new OfferReviewOptions(Query));
        Assert.Equal(first.Suggested, third.Suggested);
        Assert.True(third.Reviewed!.Single(r => r.Id == wrongUrl).Accepted);
    }

    [Fact]
    public void Including_a_rejected_offer_forces_it_into_the_calculation()
    {
        var offers = new List<CompetitorOffer> { Torob("فروشگاه A", 280_000), Web("shop-b", 290_000, "هدفون T15 هایلو نسخهٔ گلوبال اصل"), Web("model", 250_000, "هدفون هایلو GT1") };
        var normal = PriceAdvisor.Review(offers, new OfferReviewOptions(Query));
        var model = normal.Single(r => r.Offer.Price == 250_000);
        Assert.False(model.Accepted);

        var forced = PriceAdvisor.Review(offers, new OfferReviewOptions(Query, Included: [model.Id]));
        var again = forced.Single(r => r.Id == model.Id);
        Assert.True(again.Accepted);
        Assert.Equal(OfferVerdict.Forced, again.Code);
    }

    // ── اولویت ترب ──

    [Fact]
    public void Torob_cheapest_is_the_reference_even_when_another_site_is_cheaper()
    {
        var offers = new List<CompetitorOffer>
        {
            Torob("فروشگاه A", 280_000), Torob("فروشگاه B", 300_000), Torob("فروشگاه C", 295_000),
            Web("cheap-site", 262_000),
            new("دیجی‌کالا", 290_000, CompetitorPriceService.DigikalaSource, null, Title),
        };

        var s = PriceAdvisor.Suggest(100_000, 0, Costs(), offers, new OfferReviewOptions(Query));

        Assert.Equal(280_000, s.Reference);
        Assert.Equal(274_000, s.Suggested);   // ۲۸۰ × ۰٫۹۸
        Assert.Equal(262_000, s.Lowest);      // ارزان‌ترین همهٔ منابع برای اطلاع تیم
        Assert.NotNull(s.Torob);
        Assert.Equal(3, s.Torob!.Sellers);
        Assert.Equal(280_000, s.Torob.Lowest);
        Assert.Equal(1, s.Torob.Rank);        // از همهٔ فروشندگان ترب ارزان‌تر
        Assert.Equal(1, s.Torob.CheaperElsewhere);
        Assert.Contains("ترب", s.Reason);
    }

    [Fact]
    public void Without_torob_the_cheapest_valid_competitor_is_the_reference()
    {
        var s = PriceAdvisor.Suggest(100_000, 0, Costs(), [Web("shop-a", 300_000), Web("shop-b", 280_000)], new OfferReviewOptions(Query));
        Assert.Null(s.Torob);
        Assert.Equal(280_000, s.Reference);
        Assert.Equal(274_000, s.Suggested);
    }

    [Fact]
    public void Torob_listing_hidden_as_duplicate_still_counts_for_torob_rank()
    {
        // «دیجی‌کالا» در ترب ۲۷۵ و دیجی‌کالای مستقیم ۲۷۰: در ترب همان ۲۷۵ دیده می‌شود و باید از آن ارزان‌تر باشیم.
        var offers = new List<CompetitorOffer>
        {
            Torob("دیجی‌کالا", 275_000), Torob("فروشگاه X", 290_000),
            new("دیجی‌کالا", 270_000, CompetitorPriceService.DigikalaSource, null, Title),
        };
        var reviewed = PriceAdvisor.Review(offers, new OfferReviewOptions(Query));
        Assert.Equal(OfferVerdict.Duplicate, reviewed.Single(r => r.Offer.Price == 275_000).Code);

        var sellers = PriceAdvisor.TorobSellers(reviewed);
        Assert.Equal(new[] { 275_000m, 290_000m }, sellers.Select(o => o.Price).ToArray());
        var s = PriceAdvisor.Suggest(100_000, 0, Costs(), offers, new OfferReviewOptions(Query));
        Assert.Equal(275_000, s.Reference);
        Assert.True(s.Suggested < 275_000);
    }

    [Fact]
    public void Calling_without_review_options_keeps_the_old_behaviour()
    {
        var offers = new List<CompetitorOffer> { Torob("دیجی‌کالا", 300_000), Torob("X", 280_000), Torob("Y", 270_000) };
        var plain = PriceAdvisor.Suggest(100_000, 0, Costs(), offers);
        var decided = MarketPricingService.Decide(100_000, Costs(), new CompetitorLookup(offers, [], Title), 1);
        Assert.Equal(264_000, plain.Suggested);
        Assert.Equal(plain.Suggested, decided.Suggestion.Suggested);
    }

    // ── صفحهٔ فروشگاه‌ها ──

    [Fact]
    public void Shop_page_reads_json_ld_and_converts_rial()
    {
        const string html = """
            <html><head><meta property="og:site_name" content="فروشگاه نمونه">
            <script type="application/ld+json">{"@context":"https://schema.org","@graph":[{"@type":"BreadcrumbList"},
              {"@type":"Product","name":"هدفون بی‌سیم هایلو مدل T15","offers":{"@type":"Offer","price":"2890000","priceCurrency":"IRR",
               "availability":"https://schema.org/InStock"}}]}</script></head><body></body></html>
            """;
        var page = CompetitorParsers.ShopPage(html, "https://www.shop.example.ir/product/t15", Query);

        Assert.NotNull(page);
        Assert.Equal("فروشگاه نمونه", page!.Shop);
        Assert.Equal(289_000, page.Price);
        Assert.Equal("IRR", page.Currency);
        Assert.True(page.Score >= CompetitorParsers.MinScore);
    }

    [Fact]
    public void Shop_page_reads_meta_tags_and_aggregate_offers()
    {
        const string meta = """
            <html><head><title>هدفون هایلو T15 | فروشگاه</title>
            <meta property="product:price:amount" content="285,000"><meta property="product:price:currency" content="IRT"></head></html>
            """;
        var fromMeta = CompetitorParsers.ShopPage(meta, "https://shop2.example.ir/p/1", Query);
        Assert.Equal(285_000, fromMeta!.Price);
        Assert.Equal("IRT", fromMeta.Currency);
        Assert.Equal("shop2.example.ir", fromMeta.Shop);

        const string aggregate = """
            <script type="application/ld+json">[{"@type":["Product"],"name":"هدفون هایلو T15",
              "offers":{"@type":"AggregateOffer","lowPrice":279000,"priceCurrency":"TOMAN"}}]</script>
            """;
        Assert.Equal(279_000, CompetitorParsers.ShopPage(aggregate, "https://shop3.example.ir/p/1", Query)!.Price);
    }

    [Theory]
    [InlineData("""<script type="application/ld+json">{"@type":"Product","name":"هدفون هایلو T15","offers":{"price":"290000","priceCurrency":"IRT","availability":"https://schema.org/OutOfStock"}}</script>""")]
    [InlineData("""<script type="application/ld+json">{"@type":"Product","name":"هدفون هایلو T15","offers":{"price":"25","priceCurrency":"USD"}}</script>""")]
    [InlineData("""<html><body>قیمت: ۲۹۰٬۰۰۰ تومان</body></html>""")]
    [InlineData("""<script type="application/ld+json">{broken json</script>""")]
    [InlineData("")]
    public void Shop_page_without_a_reliable_price_returns_null(string html)
        => Assert.Null(CompetitorParsers.ShopPage(html, "https://shop.example.ir/p/1", Query));

    [Fact]
    public void Price_without_currency_is_resolved_against_other_sources()
    {
        var page = CompetitorParsers.ShopPage("""<meta itemprop="price" content="2900000">""", "https://shop.example.ir/p/1", Query);
        Assert.NotNull(page);
        Assert.Null(page!.Currency);

        var known = new List<CompetitorOffer> { Torob("A", 280_000), Torob("B", 300_000) };
        var resolved = CompetitorParsers.ResolveCurrency([page, page with { Price = 285_000, Url = "https://b.example.ir/p" }, page with { Price = 9_000, Url = "https://c.example.ir/p" }], known);
        Assert.Equal(new[] { 290_000m, 285_000m }, resolved.Select(o => o.Price).ToArray()); // ۹ هزار به هیچ تعبیری نزدیک نیست
        Assert.All(resolved, o => Assert.Equal(CompetitorPriceService.WebShops, o.Source));
        Assert.Empty(CompetitorParsers.ResolveCurrency([page], []));
    }

    [Theory]
    [InlineData("2,890,000", 2890000)]
    [InlineData("۲۸۹۰۰۰۰.00", 2890000)]
    [InlineData("2.890.000", 2890000)]
    [InlineData("289000.75", 289000)]
    [InlineData("رایگان", null)]
    [InlineData(null, null)]
    public void Structured_prices_are_parsed(string? text, int? expected)
        => Assert.Equal((decimal?)expected, CompetitorParsers.ParsePrice(text));

    [Theory]
    [InlineData("IRR", "IRR")]
    [InlineData("rial", "IRR")]
    [InlineData("ریال", "IRR")]
    [InlineData("IRT", "IRT")]
    [InlineData("Toman", "IRT")]
    [InlineData("تومان", "IRT")]
    [InlineData("USD", "foreign")]
    [InlineData("", null)]
    public void Currencies_are_normalized(string currency, string? expected)
        => Assert.Equal(expected, CompetitorParsers.NormalizeCurrency(currency));

    [Theory]
    [InlineData("https://shop.example.ir/product/haylou-t15", true)]
    [InlineData("http://www.shop.ir/p/1", true)]
    [InlineData("https://shop.example.ir/", false)]                 // صفحهٔ اول
    [InlineData("https://torob.com/p/abc/", false)]                 // منبع جداگانه
    [InlineData("https://www.digikala.com/product/dkp-1/", false)]
    [InlineData("https://basalam.com/p/1", false)]
    [InlineData("https://www.aparat.com/v/1", false)]
    [InlineData("https://fa.wikipedia.org/wiki/x", false)]
    [InlineData("https://shop.example.ir:8443/p/1", false)]         // درگاه غیرپیش‌فرض
    [InlineData("https://127.0.0.1/p/1", false)]                    // IP
    [InlineData("ftp://shop.example.ir/p/1", false)]
    [InlineData("not a url", false)]
    public void Only_shop_pages_are_opened(string url, bool expected)
        => Assert.Equal(expected, CompetitorParsers.IsShopCandidate(url));

    // ── منبع «سایت‌های دیگر» در سرویس ──

    private sealed class FakeWeb(IReadOnlyList<SerpItem> results, IReadOnlyDictionary<string, string> pages) : ISeoWebSource
    {
        public List<string> Fetched { get; } = [];

        public Task<WebResult<IReadOnlyList<string>>> SuggestAsync(string query, CancellationToken cancellationToken)
            => Task.FromResult(WebResult<IReadOnlyList<string>>.Fail("x"));

        public Task<WebResult<IReadOnlyList<SerpItem>>> SearchAsync(string query, CancellationToken cancellationToken)
            => Task.FromResult(WebResult<IReadOnlyList<SerpItem>>.Success(results, "fake"));

        public Task<WebResult<string>> FetchPageAsync(string url, CancellationToken cancellationToken)
        {
            lock (Fetched)
                Fetched.Add(url);
            return Task.FromResult(pages.TryGetValue(url, out var html) ? WebResult<string>.Success(html, "fake") : WebResult<string>.Fail("404"));
        }
    }

    private sealed class NoHttp : ISupplierHttp
    {
        public Task<SupplierHttpResponse> GetAsync(string url, bool expectJson, CancellationToken cancellationToken)
            => Task.FromResult(new SupplierHttpResponse(404, null, "not found"));
    }

    [Fact]
    public async Task Other_shops_found_on_the_web_are_added_as_a_source()
    {
        static string Page(string name, string price) =>
            $$"""<meta property="og:site_name" content="{{name}}"><script type="application/ld+json">{"@type":"Product","name":"هدفون هایلو T15","offers":{"price":"{{price}}","priceCurrency":"IRT"}}</script>""";
        var web = new FakeWeb(
        [
            new SerpItem("https://torob.com/p/abc/", "ترب", null),
            new SerpItem("https://shop-a.example.ir/p/t15", "A", null),
            new SerpItem("https://shop-a.example.ir/p/t15-black", "A again", null), // همان دامنه ← فقط یک بار
            new SerpItem("https://shop-b.example.ir/product/t15", "B", null),
            new SerpItem("https://blog.example.ir/", "home", null),
        ], new Dictionary<string, string>
        {
            ["https://shop-a.example.ir/p/t15"] = Page("فروشگاه A", "289000"),
            ["https://shop-b.example.ir/product/t15"] = Page("فروشگاه B", "295000"),
        });
        var service = new CompetitorPriceService(new NoHttp(), new MemoryCache(new MemoryCacheOptions()), new ExternalSourceGate(0), null, web);

        var result = await service.SearchAsync(Query, CancellationToken.None);

        Assert.Equal(new[] { "https://shop-a.example.ir/p/t15", "https://shop-b.example.ir/product/t15" }, web.Fetched.OrderBy(u => u).ToArray());
        var others = result.Offers.Where(o => o.Source == CompetitorPriceService.WebShops).OrderBy(o => o.Price).ToList();
        Assert.Equal(new[] { 289_000m, 295_000m }, others.Select(o => o.Price).ToArray());
        Assert.Equal("فروشگاه A", others[0].Shop);
        Assert.Equal("https://shop-a.example.ir/p/t15", others[0].Url);
    }

    [Fact]
    public async Task Offline_web_source_is_not_used()
    {
        var service = new CompetitorPriceService(new NoHttp(), new MemoryCache(new MemoryCacheOptions()), new ExternalSourceGate(0), null, new OfflineSeoWebSource());
        var result = await service.SearchAsync(Query, CancellationToken.None);
        Assert.DoesNotContain(result.Problems, p => p.StartsWith(CompetitorPriceService.WebShops, StringComparison.Ordinal));
    }

    [Fact]
    public void Search_results_of_two_engines_are_merged_without_duplicates()
    {
        var into = new List<SerpItem> { new("https://www.a.ir/p/1/", "A", null) };
        Fratak_Shop.Web.Infrastructure.SeoEngine.SeoWebSource.MergeResults(into,
            [new SerpItem("http://a.ir/p/1#x", "A again", null), new SerpItem("https://b.ir/p?id=2", "B", null), new SerpItem("https://b.ir/p?id=3", "C", null)]);
        Assert.Equal(new[] { "A", "B", "C" }, into.Select(i => i.Title).ToArray());
    }

    // ── نمایش ──

    [Theory]
    [InlineData("ok", "حساب شد")]
    [InlineData("accessory", "لوازم جانبی")]
    [InlineData("excluded", "حذف دستی")]
    public void Verdicts_have_persian_titles(string code, string title)
        => Assert.Equal(title, Fratak_Shop.Web.Infrastructure.Admin.CompetitorDisplay.VerdictTitle(code));

    [Theory]
    [InlineData("faratek.ir", "faratek.ir")]
    [InlineData("www.Faratek.ir", "faratek.ir")]
    [InlineData("localhost", null)]
    [InlineData("off", null)]
    [InlineData("127.0.0.1", null)]
    [InlineData(null, null)]
    public void Own_shop_domains_are_normalized(string? host, string? expected)
        => Assert.Equal(expected, Fratak_Shop.Web.Infrastructure.Admin.OwnShopNames.Domain(host));

    [Fact]
    public void Effective_price_uses_only_an_active_discount()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(270, ProductCompetitorService.Effective(300, 270, now.AddDays(-1), now.AddDays(1), now));
        Assert.Equal(300, ProductCompetitorService.Effective(300, 270, now.AddDays(1), null, now));
        Assert.Equal(300, ProductCompetitorService.Effective(300, 320, null, null, now));
        Assert.Equal(300, ProductCompetitorService.Effective(300, null, null, null, now));
    }
}
