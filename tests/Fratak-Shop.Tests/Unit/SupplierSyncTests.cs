using System.Net;
using System.Text.Json;
using Fratak_Shop.Core.Services.Suppliers;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Web.Infrastructure.Suppliers;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های بررسی خودکار تأمین‌کننده (مرحلهٔ هفتم – فاز ۵):
/// خواندن پاسخ API ووکامرس (نمونهٔ واقعی ساختار پخش عبدی)، داده‌های ساختاریافتهٔ صفحه، قوانین مقایسه،
/// زمان‌بندی نوبت‌ها و محافظ SSRF. هیچ تستی به اینترنت وصل نمی‌شود؛ پاسخ‌ها نمونه‌اند.
/// </summary>
public class SupplierSyncTests
{
    // ── نمونهٔ پاسخ Store API (ساختار واقعی ووکامرس؛ قیمت به تومان) ──

    private const string WooInStock = """
        [{"id":4521,"name":"شامپو کراتینه &amp; ترمیم‌کننده","slug":"شامپو-کراتینه","permalink":"https://pakhshabdi.com/product/x/",
          "prices":{"price":"415000","regular_price":"450000","sale_price":"415000","currency_code":"IRT","currency_minor_unit":0},
          "is_in_stock":true,"is_purchasable":true,"low_stock_remaining":null,
          "add_to_cart":{"maximum":276,"minimum":1},
          "stock_availability":{"text":"276 عدد در انبار","class":"in-stock"}}]
        """;

    private static SupplierSnapshot ParseOne(string json)
    {
        using var document = JsonDocument.Parse(json);
        return WooCommerceStoreAdapter.ParseProduct(document.RootElement);
    }

    [Fact]
    public void Woo_InStockProduct_ReadsPriceStockAndName()
    {
        var list = WooCommerceStoreAdapter.ParseList(WooInStock);
        Assert.NotNull(list);
        var snapshot = Assert.Single(list!).Value;
        Assert.Equal(SupplierItemStatus.InStock, snapshot.Status);
        Assert.Equal(415000m, snapshot.Price);
        Assert.Equal(276, snapshot.Stock);
        Assert.Equal("4521", snapshot.ExternalId);
        Assert.Equal("شامپو کراتینه & ترمیم‌کننده", snapshot.Name);
    }

    [Fact]
    public void Woo_PersianDigitsInStockText_AreNormalized()
    {
        var snapshot = ParseOne("""{"id":1,"prices":{"price":"1000","currency_code":"IRT","currency_minor_unit":0},"is_in_stock":true,"stock_availability":{"text":"۱۲ عدد در انبار"}}""");
        Assert.Equal(12, snapshot.Stock);
    }

    [Fact]
    public void Woo_RialCurrency_IsConvertedToToman()
    {
        var snapshot = ParseOne("""{"id":1,"prices":{"price":"4150000","currency_code":"IRR","currency_minor_unit":0},"is_in_stock":true}""");
        Assert.Equal(415000m, snapshot.Price);
        Assert.Null(snapshot.Stock); // عدد اعلام نشده
    }

    [Fact]
    public void Woo_MinorUnit_IsApplied()
    {
        var snapshot = ParseOne("""{"id":1,"prices":{"price":"41500000","currency_code":"IRT","currency_minor_unit":2},"is_in_stock":true}""");
        Assert.Equal(415000m, snapshot.Price);
    }

    [Fact]
    public void Woo_UnknownCurrency_GivesNoPrice()
    {
        var snapshot = ParseOne("""{"id":1,"prices":{"price":"1999","currency_code":"USD","currency_minor_unit":2},"is_in_stock":true}""");
        Assert.Null(snapshot.Price);
    }

    [Theory]
    [InlineData("""{"id":1,"is_in_stock":false,"stock_availability":{"text":"ناموجود","class":"out-of-stock"}}""")]
    [InlineData("""{"id":1,"is_in_stock":true,"is_purchasable":false}""")]
    [InlineData("""{"id":1,"is_in_stock":true,"stock_availability":{"text":"0 عدد در انبار"}}""")]
    public void Woo_OutOfStockVariants(string json)
    {
        var snapshot = ParseOne(json);
        Assert.Equal(SupplierItemStatus.OutOfStock, snapshot.Status);
        Assert.Equal(0, snapshot.Stock);
    }

    [Fact]
    public void Woo_StockFallsBackToLowStockThenCartMaximum()
    {
        Assert.Equal(3, ParseOne("""{"id":1,"is_in_stock":true,"low_stock_remaining":3,"add_to_cart":{"maximum":3}}""").Stock);
        Assert.Equal(40, ParseOne("""{"id":1,"is_in_stock":true,"add_to_cart":{"maximum":40}}""").Stock);
        // ۹۹۹۹ یعنی «بدون مدیریت موجودی» – عدد واقعی نیست.
        Assert.Null(ParseOne("""{"id":1,"is_in_stock":true,"add_to_cart":{"maximum":9999}}""").Stock);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"code":"rest_no_route"}""")]
    public void Woo_BrokenResponse_ReturnsNull(string body) => Assert.Null(WooCommerceStoreAdapter.ParseList(body));

    [Theory]
    [InlineData("https://pakhshabdi.com/product/%D8%B4%D8%A7%D9%85%D9%BE%D9%88-%DA%A9%D8%B1%D8%A7%D8%AA%DB%8C%D9%86%D9%87/", "شامپو-کراتینه")]
    [InlineData("https://shop.example/product/abc-123", "abc-123")]
    [InlineData("https://shop.example/", null)]
    public void Woo_SlugFromUrl(string url, string? expected) => Assert.Equal(expected, WooCommerceStoreAdapter.Slug(url));

    /// <summary>درخواست جعلی: آدرس‌ها را ثبت و پاسخ آماده برمی‌گرداند.</summary>
    private sealed class FakeHttp(Func<string, SupplierHttpResponse> respond) : ISupplierHttp
    {
        public List<string> Urls { get; } = [];

        public Task<SupplierHttpResponse> GetAsync(string url, bool expectJson, CancellationToken cancellationToken)
        {
            Urls.Add(url);
            return Task.FromResult(respond(url));
        }
    }

    [Fact]
    public async Task Woo_Adapter_UsesSlugSearchAndIncludeWhenIdKnown()
    {
        var known = Guid.NewGuid();
        var latin = Guid.NewGuid();
        var persian = Guid.NewGuid();
        var gone = Guid.NewGuid();
        var http = new FakeHttp(url =>
        {
            if (url.Contains("include=4521"))
                return new(200, WooInStock);
            if (url.Contains("slug=abc-123"))
                return new(200, """[{"id":12,"prices":{"price":"5000","currency_code":"IRT","currency_minor_unit":0},"is_in_stock":true}]""");
            // ووکامرس واقعی برای نامک فارسی با ?slug= همیشه [] می‌دهد؛ جست‌وجو چند نتیجه دارد و فقط permalink یکی دقیقاً همان است.
            if (url.Contains("search=" + Uri.EscapeDataString("کرم مو")))
                return new(200, """
                    [{"id":76,"permalink":"https://pakhshabdi.com/product/%DA%A9%D8%B1%D9%85-%D9%85%D9%88-%D8%A8%D8%B2%D8%B1%DA%AF/","prices":{"price":"1"},"is_in_stock":true},
                     {"id":77,"permalink":"https://pakhshabdi.com/product/%DA%A9%D8%B1%D9%85-%D9%85%D9%88/","prices":{"price":"99000","currency_code":"IRT","currency_minor_unit":0},"is_in_stock":false}]
                    """);
            return new(200, "[]");
        });
        var adapter = new WooCommerceStoreAdapter();
        var result = await adapter.FetchAsync(
        [
            new SupplierProductRef(known, "https://pakhshabdi.com/product/x/", "4521"),
            new SupplierProductRef(latin, "https://pakhshabdi.com/product/abc-123/", null),
            new SupplierProductRef(persian, "https://pakhshabdi.com/product/کرم-مو/", null),
            new SupplierProductRef(gone, "https://pakhshabdi.com/product/old/", null),
        ], new SupplierFetchContext(http, TimeSpan.Zero, CancellationToken.None));

        Assert.Equal(SupplierItemStatus.InStock, result[known].Status);
        Assert.Equal("12", result[latin].ExternalId);
        Assert.Equal(SupplierItemStatus.OutOfStock, result[persian].Status);
        Assert.Equal("77", result[persian].ExternalId);
        Assert.Equal(99000m, result[persian].Price);
        Assert.Equal(SupplierItemStatus.NotFound, result[gone].Status);
        Assert.DoesNotContain(http.Urls, u => u.Contains("slug=" + Uri.EscapeDataString("کرم-مو")));
    }

    [Fact]
    public async Task Woo_Adapter_FallsBackToProductPageId()
    {
        var id = Guid.NewGuid();
        const string page = """
            <html><head><link rel='shortlink' href='https://pakhshabdi.com/?p=103686' /></head>
            <body class="product-template-default single single-product postid-103686 woocommerce">
            <a href="?add-to-cart=99539" data-product_id="99539">مرتبط</a></body></html>
            """;
        var http = new FakeHttp(url =>
        {
            if (url.Contains("include=103686"))
                return new(200, """[{"id":103686,"prices":{"price":"204000","currency_code":"IRT","currency_minor_unit":0},"is_in_stock":true,"stock_availability":{"text":"276 عدد در انبار"}}]""");
            if (url.Contains("/wp-json/"))
                return new(200, "[]");
            return new(200, page);
        });
        var result = await new WooCommerceStoreAdapter().FetchAsync([new SupplierProductRef(id, "https://pakhshabdi.com/product/کاغذ-سرخ-کن/", null)],
            new SupplierFetchContext(http, TimeSpan.Zero, CancellationToken.None));
        Assert.Equal(SupplierItemStatus.InStock, result[id].Status);
        Assert.Equal("103686", result[id].ExternalId);
        Assert.Equal(204000m, result[id].Price);
        Assert.Equal(276, result[id].Stock);
    }

    [Fact]
    public async Task Woo_Adapter_DeletedPage_IsNotFound()
    {
        var id = Guid.NewGuid();
        var http = new FakeHttp(url => url.Contains("/wp-json/") ? new(200, "[]") : new(404, "<html>404</html>"));
        var result = await new WooCommerceStoreAdapter().FetchAsync([new SupplierProductRef(id, "https://pakhshabdi.com/product/حذف-شده/", null)],
            new SupplierFetchContext(http, TimeSpan.Zero, CancellationToken.None));
        Assert.Equal(SupplierItemStatus.NotFound, result[id].Status);
    }

    [Theory]
    [InlineData("<body class=\"single postid-55 x\">", "55")]
    [InlineData("<link rel=\"shortlink\" href=\"https://s.ir/?p=66\">", "66")]
    [InlineData("<button type=\"submit\" name=\"add-to-cart\" value=\"77\" class=\"single_add_to_cart_button\">", "77")]
    [InlineData("<link rel=\"alternate\" href=\"https://s.ir/wp-json/wp/v2/product/88\">", "88")]
    [InlineData("<a data-product_id=\"99\" href=\"?add-to-cart=99\">", null)]
    public void Woo_ProductIdFromHtml(string html, string? expected) => Assert.Equal(expected, WooCommerceStoreAdapter.ProductIdFromHtml(html));

    [Fact]
    public void Woo_NormalizePath_EncodedEqualsDecoded()
    {
        Assert.Equal(
            WooCommerceStoreAdapter.NormalizePath("https://pakhshabdi.com/product/%DA%A9%D8%B1%D9%85/"),
            WooCommerceStoreAdapter.NormalizePath("https://pakhshabdi.com/product/کرم"));
        Assert.Equal("کرم مو گرد", WooCommerceStoreAdapter.SearchWords("کرم-مو--گرد"));
    }

    [Fact]
    public void Page_WooHtmlFallback_InStockWithSalePrice()
    {
        const string html = """
            <p class="price"><del><span class="woocommerce-Price-amount amount"><bdi>250,000&nbsp;<span class="woocommerce-Price-currencySymbol">تومان</span></bdi></span></del>
            <ins><span class="woocommerce-Price-amount amount"><bdi>204,000&nbsp;<span class="woocommerce-Price-currencySymbol">تومان</span></bdi></span></ins></p>
            <p class="stock in-stock">276 عدد در انبار</p>
            """;
        var snapshot = StructuredDataAdapter.ParseHtml(html);
        Assert.Equal(SupplierItemStatus.InStock, snapshot.Status);
        Assert.Equal(204000m, snapshot.Price);
        Assert.Equal(276, snapshot.Stock);
    }

    [Fact]
    public void Page_WooHtmlFallback_OutOfStock()
    {
        const string html = """
            <p class="price"><span class="woocommerce-Price-amount amount"><bdi>۱۸۵٬۰۰۰&nbsp;<span class="woocommerce-Price-currencySymbol">تومان</span></bdi></span></p>
            <p class="stock out-of-stock">ناموجود</p>
            """;
        var snapshot = StructuredDataAdapter.ParseHtml(html);
        Assert.Equal(SupplierItemStatus.OutOfStock, snapshot.Status);
        Assert.Equal(185000m, snapshot.Price);
    }

    [Fact]
    public async Task Woo_Adapter_HttpFailure_IsErrorNotOutOfStock()
    {
        var id = Guid.NewGuid();
        var http = new FakeHttp(_ => new(0, null, "timeout"));
        var result = await new WooCommerceStoreAdapter().FetchAsync([new SupplierProductRef(id, "https://a.example/product/x/", "5")],
            new SupplierFetchContext(http, TimeSpan.Zero, CancellationToken.None));
        Assert.Equal(SupplierItemStatus.Error, result[id].Status);
    }

    [Fact]
    public async Task Registry_AutoMode_DetectsWooOncePerHost()
    {
        var http = new FakeHttp(url => url.Contains("/wp-json/") ? new(200, "[{\"id\":1}]") : new(404, ""));
        var context = new SupplierFetchContext(http, TimeSpan.Zero, CancellationToken.None);
        var registry = new SupplierAdapterRegistry();
        var first = await registry.ResolveAsync(SupplierSyncMode.Auto, "https://pakhshabdi.com/product/a/", context);
        var second = await registry.ResolveAsync(SupplierSyncMode.Auto, "https://pakhshabdi.com/product/b/", context);
        Assert.IsType<WooCommerceStoreAdapter>(first);
        Assert.Same(first, second);
        Assert.Single(http.Urls);
        Assert.Null(await registry.ResolveAsync(SupplierSyncMode.Disabled, "https://x.example/", context));
    }

    [Fact]
    public async Task Registry_AutoMode_FallsBackToPageData()
    {
        var http = new FakeHttp(_ => new(404, "<html></html>"));
        var adapter = await new SupplierAdapterRegistry().ResolveAsync(SupplierSyncMode.Auto, "https://other.example/p/1",
            new SupplierFetchContext(http, TimeSpan.Zero, CancellationToken.None));
        Assert.IsType<StructuredDataAdapter>(adapter);
    }

    // ── داده‌های ساختاریافتهٔ صفحه ──

    [Fact]
    public void Page_JsonLdInGraph_WithRial()
    {
        const string html = """
            <html><head><script type="application/ld+json">{"@context":"https://schema.org","@graph":[{"@type":"WebPage"},
            {"@type":["Product"],"name":"کرم","offers":{"@type":"Offer","price":"2500000","priceCurrency":"IRR","availability":"https://schema.org/InStock"}}]}</script></head>
            <body><p class="stock in-stock">۸ عدد در انبار</p></body></html>
            """;
        var snapshot = StructuredDataAdapter.ParseHtml(html);
        Assert.Equal(SupplierItemStatus.InStock, snapshot.Status);
        Assert.Equal(250000m, snapshot.Price);
        Assert.Equal(8, snapshot.Stock);
    }

    [Fact]
    public void Page_OutOfStockAvailability()
    {
        const string html = """<script type="application/ld+json">[{"@type":"Product","offers":[{"price":120000,"priceCurrency":"IRT","availability":"http://schema.org/OutOfStock"}]}]</script>""";
        var snapshot = StructuredDataAdapter.ParseHtml(html);
        Assert.Equal(SupplierItemStatus.OutOfStock, snapshot.Status);
        Assert.Equal(120000m, snapshot.Price);
    }

    [Fact]
    public void Page_OpenGraphFallback()
    {
        const string html = """<meta property="product:price:amount" content="۳۵۰٬۰۰۰"><meta property="product:price:currency" content="IRT"><meta property="product:availability" content="instock">""";
        var snapshot = StructuredDataAdapter.ParseHtml(html);
        Assert.Equal(SupplierItemStatus.InStock, snapshot.Status);
        Assert.Equal(350000m, snapshot.Price);
    }

    [Fact]
    public void Page_WithoutData_IsError()
    {
        Assert.Equal(SupplierItemStatus.Error, StructuredDataAdapter.ParseHtml("<html><body>سلام</body></html>").Status);
        Assert.Equal(SupplierItemStatus.Error, StructuredDataAdapter.ParseHtml("""<script type="application/ld+json">{broken</script>""").Status);
    }

    [Theory]
    [InlineData("https://schema.org/InStock", true)]
    [InlineData("LimitedAvailability", true)]
    [InlineData("http://schema.org/SoldOut", false)]
    [InlineData("PreOrder", false)]
    [InlineData("BackOrder", false)]
    public void Page_AvailabilityValues(string value, bool inStock) => Assert.Equal(inStock, StructuredDataAdapter.IsInStock(value));

    [Fact]
    public async Task Page_Adapter_404IsNotFound()
    {
        var id = Guid.NewGuid();
        var result = await new StructuredDataAdapter().FetchAsync([new SupplierProductRef(id, "https://a.example/p", null)],
            new SupplierFetchContext(new FakeHttp(_ => new(404, "")), TimeSpan.Zero, CancellationToken.None));
        Assert.Equal(SupplierItemStatus.NotFound, result[id].Status);
    }

    // ── قوانین ──

    [Theory]
    [InlineData(null, 20, 10, 1)]   // فقط «موجود»: محتاطانه یک عدد
    [InlineData(0, 20, 10, 0)]
    [InlineData(1, 20, 10, 0)]      // آخرین عدد منبع را نمی‌فروشیم
    [InlineData(5, 20, 10, 4)]
    [InlineData(2, 20, 10, 1)]
    [InlineData(50, 20, 10, 10)]    // سقف
    [InlineData(50, 0, 100, 50)]    // بدون حاشیه: برابر منبع
    [InlineData(3, 1, 10, 2)]       // حاشیهٔ کوچک هم باید کمتر از منبع باشد
    public void SuggestStock_Cases(int? source, int margin, int max, int expected) =>
        Assert.Equal(expected, SupplierSyncRules.SuggestStock(source, margin, max));

    private static readonly SupplierSyncPolicy Auto = new(true, 20, 10);
    private static readonly SupplierSyncPolicy Manual = new(false, 20, 10);

    private static SupplierDecision? Find(List<SupplierDecision> list, SupplierChangeKind kind) => list.SingleOrDefault(d => d.Kind == kind);

    [Fact]
    public void Rules_SourceOut_AutoZeroesOurStock()
    {
        var decisions = SupplierSyncRules.Evaluate(new SupplierSnapshot(SupplierItemStatus.OutOfStock, 400000m, 0), new SupplierProductView(3, 400000m, false, 0), Auto);
        var outOfStock = Find(decisions, SupplierChangeKind.OutOfStock)!;
        Assert.Equal(SupplierDecisionAction.AutoApply, outOfStock.Action);
        Assert.Equal(3m, outOfStock.OldValue);
        Assert.Equal(0m, outOfStock.NewValue);
        Assert.Equal(SupplierDecisionAction.Supersede, Find(decisions, SupplierChangeKind.BackInStock)!.Action);
        Assert.Equal(SupplierDecisionAction.Supersede, Find(decisions, SupplierChangeKind.PriceChange)!.Action);
    }

    [Fact]
    public void Rules_SourceOut_WithoutAuto_OnlyProposes()
    {
        var decisions = SupplierSyncRules.Evaluate(new SupplierSnapshot(SupplierItemStatus.OutOfStock), new SupplierProductView(3, null, false, 0), Manual);
        Assert.Equal(SupplierDecisionAction.Propose, Find(decisions, SupplierChangeKind.OutOfStock)!.Action);
    }

    [Fact]
    public void Rules_SourceOut_AlreadyZero_NothingToDo()
    {
        var decisions = SupplierSyncRules.Evaluate(new SupplierSnapshot(SupplierItemStatus.OutOfStock), new SupplierProductView(0, null, true, 0), Auto);
        Assert.DoesNotContain(decisions, d => d.Action != SupplierDecisionAction.Supersede);
    }

    [Fact]
    public void Rules_BackInStock_ProposesSuggestedQuantity()
    {
        var decisions = SupplierSyncRules.Evaluate(new SupplierSnapshot(SupplierItemStatus.InStock, null, 5), new SupplierProductView(0, null, true, 0), Auto);
        var back = Find(decisions, SupplierChangeKind.BackInStock)!;
        Assert.Equal(SupplierDecisionAction.Propose, back.Action);
        Assert.Equal(4m, back.NewValue);
        Assert.Equal(SupplierDecisionAction.Supersede, Find(decisions, SupplierChangeKind.OutOfStock)!.Action);
    }

    [Fact]
    public void Rules_BackInStock_SingleSourceItem_IsNotProposed()
    {
        var decisions = SupplierSyncRules.Evaluate(new SupplierSnapshot(SupplierItemStatus.InStock, null, 1), new SupplierProductView(0, null, true, 0), Auto);
        Assert.Equal(SupplierDecisionAction.Supersede, Find(decisions, SupplierChangeKind.BackInStock)!.Action);
    }

    [Fact]
    public void Rules_OurStockAboveSource_IsReducedAutomatically()
    {
        var decisions = SupplierSyncRules.Evaluate(new SupplierSnapshot(SupplierItemStatus.InStock, null, 4), new SupplierProductView(6, null, false, 0), Auto);
        var decrease = Find(decisions, SupplierChangeKind.StockDecrease)!;
        Assert.Equal(SupplierDecisionAction.AutoApply, decrease.Action);
        Assert.Equal(3m, decrease.NewValue);
    }

    [Fact]
    public void Rules_StockIncrease_WhenSourceHasMore()
    {
        var decisions = SupplierSyncRules.Evaluate(new SupplierSnapshot(SupplierItemStatus.InStock, null, 276), new SupplierProductView(2, null, false, 0), Auto);
        var increase = Find(decisions, SupplierChangeKind.StockIncrease)!;
        Assert.Equal(SupplierDecisionAction.Propose, increase.Action);
        Assert.Equal(10m, increase.NewValue);
        Assert.Equal(SupplierDecisionAction.Supersede, Find(decisions, SupplierChangeKind.StockDecrease)!.Action);
    }

    [Fact]
    public void Rules_OurStockAboveCapButBelowSource_IsLeftAlone()
    {
        var decisions = SupplierSyncRules.Evaluate(new SupplierSnapshot(SupplierItemStatus.InStock, null, 100), new SupplierProductView(30, null, false, 0), Auto);
        Assert.DoesNotContain(decisions, d => d.Action != SupplierDecisionAction.Supersede);
    }

    [Fact]
    public void Rules_PriceChange_ComparesWithPurchasePrice()
    {
        var decisions = SupplierSyncRules.Evaluate(new SupplierSnapshot(SupplierItemStatus.InStock, 415000m, null), new SupplierProductView(2, 400000m, false, 0), Auto);
        var price = Find(decisions, SupplierChangeKind.PriceChange)!;
        Assert.Equal(SupplierDecisionAction.Propose, price.Action);
        Assert.Equal(400000m, price.OldValue);
        Assert.Equal(415000m, price.NewValue);
    }

    [Fact]
    public void Rules_PriceChange_MissingPurchasePrice_IsProposed()
    {
        var decisions = SupplierSyncRules.Evaluate(new SupplierSnapshot(SupplierItemStatus.InStock, 99000m, null), new SupplierProductView(1, null, false, 0), Auto);
        Assert.Equal(SupplierDecisionAction.Propose, Find(decisions, SupplierChangeKind.PriceChange)!.Action);
    }

    [Fact]
    public void Rules_NotFound_NeverTouchesStock()
    {
        var decisions = SupplierSyncRules.Evaluate(SupplierSnapshot.NotFound("x"), new SupplierProductView(5, 1000m, false, 0), Auto);
        Assert.Equal(SupplierDecisionAction.Propose, Find(decisions, SupplierChangeKind.NotFound)!.Action);
        Assert.DoesNotContain(decisions, d => d.Action == SupplierDecisionAction.AutoApply);
        Assert.Null(Find(decisions, SupplierChangeKind.OutOfStock));
    }

    [Fact]
    public void Rules_Error_ReportedOnlyAfterThreshold()
    {
        Assert.Empty(SupplierSyncRules.Evaluate(SupplierSnapshot.Failed("x"), new SupplierProductView(5, null, false, 0), Auto));
        Assert.Empty(SupplierSyncRules.Evaluate(SupplierSnapshot.Failed("x"), new SupplierProductView(5, null, false, 1), Auto));
        var third = SupplierSyncRules.Evaluate(SupplierSnapshot.Failed("x"), new SupplierProductView(5, null, false, 2), Auto);
        Assert.Equal(SupplierDecisionAction.Propose, Assert.Single(third).Action);
    }

    [Fact]
    public void Rules_EachKindAppearsAtMostOnce()
    {
        foreach (var snapshot in new[]
                 {
                     new SupplierSnapshot(SupplierItemStatus.InStock, 1000m, 5), new SupplierSnapshot(SupplierItemStatus.InStock, null, null),
                     new SupplierSnapshot(SupplierItemStatus.OutOfStock, 1000m, 0), SupplierSnapshot.NotFound(),
                 })
        foreach (var stock in new[] { 0, 1, 4, 5, 20 })
        {
            var decisions = SupplierSyncRules.Evaluate(snapshot, new SupplierProductView(stock, 900m, false, 0), Auto);
            Assert.Equal(decisions.Count, decisions.Select(d => d.Kind).Distinct().Count());
        }
    }

    // ── زمان‌بندی (تهران = UTC+3:30) ──

    [Fact]
    public void Schedule_FourRuns_LatestAndNextSlot()
    {
        var now = new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero); // ۱۲:۳۰ تهران
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 8, 30, 0, TimeSpan.Zero), SupplierSyncSchedule.LatestSlot(now, 4)); // ۱۲:۰۰
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 14, 30, 0, TimeSpan.Zero), SupplierSyncSchedule.NextSlot(now, 4)); // ۱۸:۰۰
    }

    [Fact]
    public void Schedule_FiveRuns_UsesEqualIntervals()
    {
        var now = new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero); // ۱۲:۳۰ تهران → نوبت ۰۹:۳۶
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 6, 6, 0, TimeSpan.Zero), SupplierSyncSchedule.LatestSlot(now, 5));
    }

    [Fact]
    public void Schedule_NextSlotAfterLastOfDay_IsMidnight()
    {
        var now = new DateTimeOffset(2026, 10, 25, 20, 0, 0, TimeSpan.Zero); // ۲۳:۳۰ تهران
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 20, 30, 0, TimeSpan.Zero), SupplierSyncSchedule.NextSlot(now, 4)); // نیمه‌شب
    }

    [Fact]
    public void Schedule_IsDue()
    {
        var now = new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
        Assert.True(SupplierSyncSchedule.IsDue(now, 4, null));
        Assert.True(SupplierSyncSchedule.IsDue(now, 4, new DateTimeOffset(2026, 10, 25, 8, 0, 0, TimeSpan.Zero)));
        Assert.False(SupplierSyncSchedule.IsDue(now, 4, new DateTimeOffset(2026, 10, 25, 8, 31, 0, TimeSpan.Zero)));
    }

    // ── محافظ SSRF ──

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.5", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("185.143.233.120", true)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2a01:4f8::1", true)]
    public void Ssrf_IsPublic(string ip, bool expected) => Assert.Equal(expected, SupplierHttp.IsPublic(IPAddress.Parse(ip)));

    [Fact]
    public void Parsing_ToToman()
    {
        Assert.Equal(415000m, SupplierParsing.ToToman(4150000m, "IRR"));
        Assert.Equal(415000m, SupplierParsing.ToToman(415000m, "irt"));
        Assert.Equal(415000m, SupplierParsing.ToToman(415000m, null));
        Assert.Null(SupplierParsing.ToToman(0m, "IRT"));
        Assert.Null(SupplierParsing.ToToman(10m, "EUR"));
        Assert.Equal(276, SupplierParsing.ParseStockText("<span>276 عدد در انبار</span>"));
        Assert.Null(SupplierParsing.ParseStockText("ناموجود"));
    }
}
