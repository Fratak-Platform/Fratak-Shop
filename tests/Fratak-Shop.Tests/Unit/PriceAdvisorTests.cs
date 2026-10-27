using Fratak_Shop.Core.Services.Pricing;
using Fratak_Shop.Core.Services.Suppliers;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های «پیشنهاد قیمت فروش» (دور ۱۰): فرمول هزینه و سود، راهبرد زیر قیمت رقبا با کف سود، حذف قیمت‌های پرت،
/// خواندن پاسخ‌های ترب و دیجی‌کالا (نمونه‌های ساختار واقعی) و سرویس جست‌وجو با HTTP جعلی (بدون اینترنت).
/// </summary>
[Trait("Category", "Unit")]
public class PriceAdvisorTests
{
    /// <summary>هزینه‌های پیش‌فرض تنظیمات: کارمزد درگاه ۱٪، سود هدف ۳۰٪، کف ۸٪، ارزان‌تر ۲٪، گرد کردن ۱۰۰۰.</summary>
    private static PricingCosts Costs(decimal packaging = 0, decimal shipping = 0, decimal gateway = 1, decimal tax = 0, int target = 30, int min = 8, decimal undercut = 2)
        => new(packaging, shipping, 0, gateway, 0, tax, target, min, undercut, 1000);

    private static CompetitorOffer Offer(string shop, decimal price) => new(shop, price, "ترب");

    // ── فرمول ──

    [Fact]
    public void Users_example_cost_100_competitors_300_280_270()
    {
        var s = PriceAdvisor.Suggest(100_000, 0, Costs(), [Offer("دیجی‌کالا", 300_000), Offer("فروشگاه X", 280_000), Offer("فروشگاه Y", 270_000)]);

        Assert.Equal("undercut", s.Strategy);
        Assert.Equal(264_000, s.Suggested);       // ۲۷۰ × ۰٫۹۸ = ۲۶۴٫۶ ← گرد به پایین
        Assert.True(s.Suggested < 270_000);        // ارزان‌تر از همهٔ رقبا
        Assert.Equal(161_360, s.Profit);           // ۲۶۴٬۰۰۰ × ۰٫۹۹ − ۱۰۰٬۰۰۰
        Assert.Equal(270_000, s.Lowest);
        Assert.Equal(280_000, s.Median);
        Assert.Equal(3, s.Offers.Count);
    }

    [Fact]
    public void Without_competitors_the_target_margin_is_used()
    {
        // هزینهٔ کل = ۱۰۰ + ۱۰ بسته‌بندی + ۵ هزینهٔ اضافه = ۱۱۵ هزار؛ ۱۱۵ × ۱٫۳ ÷ ۰٫۹۹ = ۱۵۱٬۰۱۰ ← ۱۵۲٬۰۰۰
        var s = PriceAdvisor.Suggest(100_000, 5_000, Costs(packaging: 10_000), []);
        Assert.Equal("target", s.Strategy);
        Assert.Equal(115_000, s.TotalCost);
        Assert.Equal(152_000, s.Suggested);
        Assert.Equal(s.TargetPrice, s.Suggested);
        Assert.True(s.Profit >= 115_000 * 0.30m);
    }

    [Fact]
    public void Percent_costs_are_taken_from_the_sale_price()
    {
        // کارمزد ۱٪ + مالیات ۹٪ = ۱۰٪ از فروش؛ سربه‌سر = ۱۰۰ ÷ ۰٫۹ = ۱۱۱٬۱۱۱.
        var s = PriceAdvisor.Suggest(100_000, 0, Costs(tax: 9), []);
        Assert.Equal(111_111, s.BreakEven);
        Assert.Equal(120_000, s.MinPrice); // ۱۰۰ × ۱٫۰۸ ÷ ۰٫۹ = ۱۲۰٬۰۰۰
    }

    [Fact]
    public void Cheap_competitors_never_push_the_price_below_the_minimum_margin()
    {
        var s = PriceAdvisor.Suggest(100_000, 0, Costs(), [Offer("A", 105_000), Offer("B", 104_000), Offer("C", 103_000)]);
        Assert.Equal("floor", s.Strategy);
        Assert.Equal(s.MinPrice, s.Suggested);
        Assert.True(s.Profit > 0);
        Assert.True(s.ProfitPercent >= 8);
    }

    [Fact]
    public void Between_floor_and_target_the_undercut_price_is_kept()
    {
        var s = PriceAdvisor.Suggest(100_000, 0, Costs(), [Offer("A", 125_000), Offer("B", 126_000), Offer("C", 127_000)]);
        Assert.Equal("undercut", s.Strategy);
        Assert.Equal(122_000, s.Suggested); // ۱۲۵ × ۰٫۹۸ = ۱۲۲٫۵ ← ۱۲۲
        Assert.True(s.Suggested < s.TargetPrice && s.Suggested >= s.MinPrice);
    }

    [Fact]
    public void Undercut_zero_still_stays_below_the_cheapest_competitor()
    {
        var s = PriceAdvisor.Suggest(100_000, 0, Costs(undercut: 0), [Offer("A", 300_000), Offer("B", 310_000), Offer("C", 320_000)]);
        Assert.Equal(299_000, s.Suggested);
    }

    // ── پاک‌سازی رقبا ──

    [Fact]
    public void Outliers_and_duplicate_shops_are_removed()
    {
        var (offers, ignored) = PriceAdvisor.Clean([
            Offer("دیجی‌کالا", 300_000), Offer("ديجي كالا", 290_000), Offer("X", 280_000), Offer("Y", 270_000),
            Offer("قاب‌فروش", 45_000), Offer("گران", 2_000_000), Offer("", 100_000), Offer("صفر", 0)]);

        Assert.Equal(new[] { 270_000m, 280_000m, 290_000m }, offers.Select(o => o.Price).ToArray());
        Assert.Equal(new[] { 45_000m, 2_000_000m }, ignored.Select(o => o.Price).ToArray());
    }

    [Theory]
    [InlineData(new[] { 3.0, 1.0, 2.0 }, 2.0)]
    [InlineData(new[] { 1.0, 2.0, 3.0, 4.0 }, 2.5)]
    [InlineData(new double[0], 0.0)]
    public void Median(double[] values, double expected)
        => Assert.Equal((decimal)expected, PriceAdvisor.Median(values.Select(v => (decimal)v).ToList()));

    // ── خواندن پاسخ‌ها ──

    private const string TorobSearchJson = """
        {"results":[
          {"random_key":"aaa","name1":"قاب هدفون هایلو T15","price":45000,"shop_text":"در ۳ فروشگاه",
           "more_info_url":"https://api.torob.com/v4/base-product/details/?prk=aaa&search_id=1","web_client_absolute_url":"/p/aaa/x/"},
          {"random_key":"bbb","name1":"هدفون بی سیم هایلو مدل T15","price":270000,"shop_text":"در ۱۲ فروشگاه",
           "more_info_url":"https://api.torob.com/v4/base-product/details/?prk=bbb&search_id=1","web_client_absolute_url":"/p/bbb/haylou-t15/"},
          {"random_key":"ccc","name1":"هدفون هایلو GT1","price":300000,
           "more_info_url":"https://api.torob.com/v4/base-product/details/?prk=ccc&search_id=1"}
        ]}
        """;

    private const string TorobDetailsJson = """
        {"name1":"هدفون بی سیم هایلو مدل T15","products_info":{"result":[
          {"shop_name":"دیجی‌کالا","shop_id":123,"price":300000,"availability":true,"page_url":"https://api.torob.com/v4/redirect?x=1"},
          {"shop_name":"فروشگاه X","shop_id":"456","price":0,"price_text":"۲۸۰٬۰۰۰ تومان","availability":true},
          {"shop_name":"فروشگاه Y","price":270000,"availability":true},
          {"shop_name":"ناموجود","price":200000,"availability":false},
          {"shop_name":"قیمت مشکوک","price":10000,"availability":true,"is_price_unreliable":true},
          {"shop_name":"","price":250000}
        ]}}
        """;

    private const string DigikalaJson = """
        {"status":200,"data":{"products":[
          {"id":11,"title_fa":"هدفون بلوتوثی هایلو مدل T15","url":{"uri":"/product/dkp-11/x/"},
           "default_variant":{"price":{"selling_price":3050000,"rrp_price":3500000}}},
          {"id":12,"title_fa":"هدفون هایلو T15 ناموجود","default_variant":[]},
          {"id":13,"title_fa":"کابل شارژ","default_variant":{"price":{"selling_price":500000}}}
        ]}}
        """;

    [Fact]
    public void Torob_search_cards_are_read()
    {
        var cards = CompetitorParsers.TorobSearch(TorobSearchJson);
        Assert.Equal(3, cards.Count);
        Assert.Equal("هدفون بی سیم هایلو مدل T15", cards[1].Title);
        Assert.Equal(270_000, cards[1].Price);
        Assert.Equal("https://torob.com/p/bbb/haylou-t15/", cards[1].WebUrl);
        Assert.True(CompetitorParsers.IsTorobDetailsUrl(cards[1].DetailsUrl!));
    }

    [Fact]
    public void Torob_offers_skip_unavailable_and_unreliable_prices()
    {
        var offers = CompetitorParsers.TorobOffers(TorobDetailsJson, "هدفون");
        Assert.Equal(new[] { "دیجی‌کالا", "فروشگاه X", "فروشگاه Y" }, offers.Select(o => o.Shop).ToArray());
        Assert.Equal(280_000, offers[1].Price); // از متن فارسی
        Assert.Equal("https://torob.com/shop/123/", offers[0].Url);
        Assert.Null(offers[2].Url);
    }

    [Fact]
    public void Digikala_prices_are_converted_from_rial()
    {
        var items = CompetitorParsers.Digikala(DigikalaJson);
        Assert.Equal(2, items.Count); // کالای بدون تنوع (ناموجود) کنار رفت
        Assert.Equal(305_000, items[0].Price);
        Assert.Equal("https://www.digikala.com/product/dkp-11/x/", items[0].Url);
    }

    [Theory]
    [InlineData("""{"results":"x"}""")]
    [InlineData("""{}""")]
    [InlineData("""[]""")]
    public void Unexpected_shapes_return_nothing(string json)
    {
        Assert.Empty(CompetitorParsers.TorobSearch(json));
        Assert.Empty(CompetitorParsers.TorobOffers(json, null));
        Assert.Empty(CompetitorParsers.Digikala(json));
    }

    [Theory]
    [InlineData("هدفون هایلو T15", "هدفون بی‌سیم هایلو مدل T15", 1.0)]
    [InlineData("هدفون هایلو T15", "هدفون هایلو GT1", 1.0 / 3)]
    [InlineData("هدفون هایلو T15", "قاب هدفون هایلو T15", 0.5)]         // لوازم جانبی
    [InlineData("قاب هدفون هایلو T15", "قاب هدفون هایلو T15", 1.0)]     // خودِ قاب را می‌فروشیم
    [InlineData("گوشی سامسونگ A55 256GB", "گوشی موبایل سامسونگ مدل Galaxy A55 ظرفیت 256 گیگابایت", 1.0)]
    [InlineData("گوشی سامسونگ A55 256", "گوشی سامسونگ A55 256GB", 1.0)]
    [InlineData("هدفون هایلو T15", "هدفون هایلو T150", 1.0 / 3)]
    [InlineData("", "هر چیزی", 0.0)]
    public void Title_score(string query, string title, double expected)
        => Assert.Equal(expected, CompetitorParsers.TitleScore(query, title), 3);

    [Theory]
    [InlineData("https://api.torob.com/v4/base-product/details/?prk=a", true)]
    [InlineData("http://api.torob.com/v4/base-product/details/?prk=a", false)]
    [InlineData("https://evil.example/v4/base-product/details/", false)]
    [InlineData("https://api.torob.com.evil.example/v4/base-product/details/", false)]
    [InlineData("https://api.torob.com/v4/user/", false)]
    public void Only_torob_detail_urls_are_followed(string url, bool expected)
        => Assert.Equal(expected, CompetitorParsers.IsTorobDetailsUrl(url));

    [Theory]
    [InlineData("۲۸۰٬۰۰۰ تومان", 280000)]
    [InlineData("95,800,000", 95800000)]
    [InlineData("از ۱۲۰٫۰۰۰ تومان", 120000)]
    [InlineData("ناموجود", null)]
    [InlineData(null, null)]
    public void Price_from_text(string? text, int? expected)
        => Assert.Equal((decimal?)expected, CompetitorParsers.FromText(text));

    // ── سرویس با HTTP جعلی ──

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

    private static SupplierHttpResponse Respond(string url) =>
        url.Contains("base-product/search") ? new(200, TorobSearchJson)
        : url.Contains("prk=bbb") ? new(200, TorobDetailsJson)
        : url.Contains("digikala") ? new(200, DigikalaJson)
        : new(404, null, "not found");

    [Fact]
    public async Task Service_picks_the_matching_product_and_lists_every_shop()
    {
        var http = new FakeHttp(Respond);
        var service = new CompetitorPriceService(http, new MemoryCache(new MemoryCacheOptions()));

        var result = await service.SearchAsync("هدفون هایلو T15", CancellationToken.None);

        Assert.Equal("هدفون بی سیم هایلو مدل T15", result.MatchedTitle);
        Assert.Contains(http.Urls, u => u.Contains("prk=bbb")); // نه «قاب» ارزان و نه مدل GT1
        Assert.DoesNotContain(http.Urls, u => u.Contains("prk=ccc"));
        Assert.Equal(4, result.Offers.Count); // ۳ فروشگاه ترب + دیجی‌کالا مستقیم
        Assert.Contains(result.Offers, o => o.Source == "دیجی‌کالا" && o.Price == 305_000);
        Assert.Empty(result.Problems);

        // با هم‌نام بودن «دیجی‌کالا»، پاک‌سازی فقط ارزان‌ترینش را نگه می‌دارد.
        var suggestion = PriceAdvisor.Suggest(100_000, 0, Costs(), result.Offers);
        Assert.Equal(3, suggestion.Offers.Count);
        Assert.Equal(264_000, suggestion.Suggested);
    }

    [Fact]
    public async Task Service_reports_torob_captcha_and_keeps_other_sources()
    {
        var http = new FakeHttp(url => url.Contains("torob") ? new SupplierHttpResponse(490, "<html>captcha</html>") : Respond(url));
        var service = new CompetitorPriceService(http, new MemoryCache(new MemoryCacheOptions()));

        var result = await service.SearchAsync("هدفون هایلو T15", CancellationToken.None);

        Assert.Single(result.Offers);
        Assert.Contains(result.Problems, p => p.StartsWith("ترب") && p.Contains("کپچا"));
    }

    [Fact]
    public async Task Service_survives_broken_json_and_short_queries()
    {
        var http = new FakeHttp(_ => new SupplierHttpResponse(200, "{not json"));
        var service = new CompetitorPriceService(http, new MemoryCache(new MemoryCacheOptions()));

        var result = await service.SearchAsync("هدفون هایلو T15", CancellationToken.None);
        Assert.Empty(result.Offers);
        Assert.Equal(2, result.Problems.Count);

        var tooShort = await service.SearchAsync(" x ", CancellationToken.None);
        Assert.Empty(tooShort.Offers);
        Assert.Single(tooShort.Problems);
    }

    [Fact]
    public async Task Successful_results_are_cached()
    {
        var http = new FakeHttp(Respond);
        var service = new CompetitorPriceService(http, new MemoryCache(new MemoryCacheOptions()));

        await service.SearchAsync("هدفون هایلو T15", CancellationToken.None);
        var calls = http.Urls.Count;
        await service.SearchAsync("هدفون  هايلو t15", CancellationToken.None); // همان عبارت پس از نرمال‌سازی
        Assert.Equal(calls, http.Urls.Count);
    }
}
