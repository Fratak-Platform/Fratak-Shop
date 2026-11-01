using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Pricing;
using Fratak_Shop.Core.Services.Suppliers;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Web.Infrastructure.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های مرحلهٔ سیزدهم (آمادگی انتشار روی faratek.ir):
/// دروازهٔ مؤدب منابع بیرونی (فاصله، استراحت دوبرابرشونده، Retry-After)، داوری پاسخ منابع و تشخیص کپچا،
/// منبع باسلام، کش ماندگار قیمت رقبا (تازه/کهنه)، تصمیم قیمت‌گذاری بازار درون‌ریزی،
/// تغییر مسیر به دامنهٔ اصلی و نرمال‌سازی شمارهٔ مالک.
/// </summary>
[Trait("Category", "Unit")]
public class ProductionReadinessTests
{
    // ── دروازهٔ منابع بیرونی ──

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 40)]
    [InlineData(4, 80)]
    [InlineData(5, 120)]
    [InlineData(9, 120)]
    public void Cooldown_doubles_and_is_capped_at_two_hours(int strikes, int minutes)
        => Assert.Equal(TimeSpan.FromMinutes(minutes), ExternalSourceGate.Cooldown(strikes, null));

    [Fact]
    public void Cooldown_honours_larger_retry_after_but_caps_it()
    {
        Assert.Equal(TimeSpan.FromHours(3), ExternalSourceGate.Cooldown(1, TimeSpan.FromHours(3)));
        Assert.Equal(TimeSpan.FromMinutes(10), ExternalSourceGate.Cooldown(1, TimeSpan.FromSeconds(30))); // کمتر از استراحت پایه
        Assert.Equal(ExternalSourceGate.MaxRetryAfter, ExternalSourceGate.Cooldown(1, TimeSpan.FromDays(30)));
    }

    [Fact]
    public async Task Blocked_source_gets_no_requests_until_success_resets_it()
    {
        var gate = new ExternalSourceGate(0);
        using (var lease = await gate.TryEnterAsync("ترب", TimeSpan.FromSeconds(1), CancellationToken.None))
            Assert.NotNull(lease);

        gate.ReportBlocked("ترب", null, "کد 490");
        Assert.NotNull(gate.CoolingUntil("ترب"));
        Assert.Null(await gate.TryEnterAsync("ترب", TimeSpan.Zero, CancellationToken.None));

        // منبع دیگر تحت تأثیر نیست.
        using (var other = await gate.TryEnterAsync("دیجی‌کالا", TimeSpan.Zero, CancellationToken.None))
            Assert.NotNull(other);

        var status = Assert.Single(gate.Snapshot(), s => s.Source == "ترب");
        Assert.Equal(1, status.Strikes);
        Assert.Equal(1, status.Blocks);
        Assert.Equal("کد 490", status.LastProblem);

        gate.ReportSuccess("ترب");
        Assert.Null(gate.CoolingUntil("ترب"));
        using var again = await gate.TryEnterAsync("ترب", TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(again);
    }

    [Fact]
    public void Three_transient_failures_in_a_row_cause_a_short_rest()
    {
        var gate = new ExternalSourceGate(0);
        gate.ReportFailure("باسلام", "timeout");
        gate.ReportFailure("باسلام", "timeout");
        Assert.Null(gate.CoolingUntil("باسلام"));
        gate.ReportFailure("باسلام", "timeout");
        var until = gate.CoolingUntil("باسلام");
        Assert.NotNull(until);
        Assert.True(until <= DateTimeOffset.UtcNow + ExternalSourceGate.FailureCooldown + TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Gate_serialises_requests_and_keeps_the_minimum_interval()
    {
        var gate = new ExternalSourceGate();
        var started = DateTimeOffset.UtcNow;
        using (await gate.TryEnterAsync("x", TimeSpan.FromMilliseconds(150), CancellationToken.None))
        {
        }
        using (await gate.TryEnterAsync("x", TimeSpan.FromMilliseconds(150), CancellationToken.None))
        {
        }
        Assert.True(DateTimeOffset.UtcNow - started >= TimeSpan.FromMilliseconds(140));
    }

    // ── داوری پاسخ منابع ──

    [Theory]
    [InlineData(490, "<html>arcaptcha</html>", SourceVerdict.Blocked)]
    [InlineData(429, null, SourceVerdict.Blocked)]
    [InlineData(403, "forbidden", SourceVerdict.Blocked)]
    [InlineData(503, null, SourceVerdict.Blocked)]
    [InlineData(200, "{\"results\":[]}", SourceVerdict.Ok)]
    [InlineData(200, "  <!doctype html><title>Captcha</title>", SourceVerdict.Blocked)]
    [InlineData(200, "<html><body>maintenance</body></html>", SourceVerdict.Failed)]
    [InlineData(500, "{}", SourceVerdict.Failed)]
    [InlineData(0, null, SourceVerdict.Failed)]
    public void Responses_are_classified(int status, string? body, SourceVerdict expected)
        => Assert.Equal(expected, CompetitorPriceService.Classify(new SupplierHttpResponse(status, body)));

    [Theory]
    [InlineData("<div class=\"anomaly-modal\">", true)]
    [InlineData("<script src=\"/cdn-cgi/challenge-platform/x.js\">", true)]
    [InlineData("لطفاً تأیید کنید که ربات نیستید", true)]
    [InlineData("<html><body><li class=\"b_algo\">نتیجه</li></body></html>", false)]
    public void Challenge_pages_are_detected(string html, bool expected)
        => Assert.Equal(expected, CompetitorPriceService.LooksLikeChallenge(html));

    // ── باسلام ──

    private const string BasalamJson = """
        {"products":[
          {"id":12,"name":"هدفون بی سیم هایلو T15","price":2950000,"vendor":{"name":"غرفه صوتی"}},
          {"id":13,"name":"هدفون هایلو T15 اصل","price":3100000,"vendor":{"name":"دیجی‌صدا"}},
          {"id":"x","name":"بدون قیمت","vendor":{"name":"غرفه"}},
          {"id":14,"name":"قاب هدفون هایلو T15","price":450000,"vendor":{"name":"غرفه صوتی"}},
          "garbage"
        ]}
        """;

    [Fact]
    public void Basalam_results_are_parsed_in_toman_with_vendor_and_link()
    {
        var offers = CompetitorParsers.Basalam(BasalamJson);
        Assert.Equal(3, offers.Count);
        var first = offers[0];
        Assert.Equal("باسلام – غرفه صوتی", first.Shop);
        Assert.Equal(295_000, first.Price);
        Assert.Equal("باسلام", first.Source);
        Assert.Equal("https://basalam.com/p/12", first.Url);
        Assert.Empty(CompetitorParsers.Basalam("{\"products\":{}}"));
        Assert.Empty(CompetitorParsers.Basalam("[]"));
    }

    // ── سرویس قیمت رقبا با دروازه و کش ماندگار ──

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

    private sealed class MemoryStore : ICompetitorPriceStore
    {
        public Dictionary<string, StoredCompetitorPrices> Rows { get; } = [];

        public Task<StoredCompetitorPrices?> GetAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(Rows.TryGetValue(key, out var row) ? row : null);

        public Task SaveAsync(string key, IReadOnlyList<CompetitorOffer> offers, string? matchedTitle, CancellationToken cancellationToken)
        {
            Rows[key] = new StoredCompetitorPrices(offers, matchedTitle, DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }
    }

    private const string DigikalaJson = """
        {"data":{"products":[{"id":1,"title_fa":"هدفون بی سیم هایلو مدل T15","url":{"uri":"/product/dkp-1/"},"default_variant":{"price":{"selling_price":3050000}}}]}}
        """;

    private static CompetitorPriceService Service(FakeHttp http, ExternalSourceGate gate, ICompetitorPriceStore? store = null)
        => new(http, new MemoryCache(new MemoryCacheOptions()), gate, store);

    [Fact]
    public async Task Blocked_torob_is_not_asked_again_and_lookup_is_marked_limited()
    {
        var http = new FakeHttp(url => url.Contains("torob") ? new SupplierHttpResponse(490, "<html>captcha</html>")
            : url.Contains("digikala") ? new SupplierHttpResponse(200, DigikalaJson)
            : new SupplierHttpResponse(200, BasalamJson));
        var gate = new ExternalSourceGate(0);

        var first = await Service(http, gate).SearchAsync("هدفون هایلو T15", CancellationToken.None);
        Assert.True(first.Limited);
        Assert.Contains(first.Problems, p => p.StartsWith("ترب") && p.Contains("کپچا"));
        Assert.Contains(first.Offers, o => o.Source == "دیجی‌کالا" && o.Price == 305_000);
        Assert.Contains(first.Offers, o => o.Source == "باسلام");
        Assert.DoesNotContain(first.Offers, o => o.Price == 45_000); // قاب کنار رفت (شباهت کم)

        var torobCalls = http.Urls.Count(u => u.Contains("torob"));
        var second = await Service(http, gate).SearchAsync("هدفون هایلو T15 مشکی", CancellationToken.None);
        Assert.Equal(torobCalls, http.Urls.Count(u => u.Contains("torob"))); // در حال استراحت: درخواستی نرفت
        Assert.Contains(second.Problems, p => p.StartsWith("ترب") && p.Contains("دقیقه"));
    }

    [Fact]
    public async Task Fresh_stored_prices_skip_the_network_and_stale_ones_are_a_fallback()
    {
        var store = new MemoryStore();
        var offer = new CompetitorOffer("فروشگاه نمونه", 300_000, "ترب");
        store.Rows["هدفون هایلو t15"] = new StoredCompetitorPrices([offer], "هدفون هایلو T15", DateTimeOffset.UtcNow.AddHours(-1));

        var http = new FakeHttp(_ => new SupplierHttpResponse(503, null));
        var fresh = await Service(http, new ExternalSourceGate(0), store).SearchAsync("هدفون هایلو T15", CancellationToken.None);
        Assert.Empty(http.Urls);
        Assert.Equal(300_000, Assert.Single(fresh.Offers).Price);
        Assert.NotNull(fresh.CachedAt);

        // کهنه (۱۰ ساعت): جست‌وجوی تازه انجام می‌شود؛ چون همه محدودند، قیمت ذخیره‌شده با ذکر زمان برمی‌گردد.
        store.Rows["هدفون هایلو t15"] = new StoredCompetitorPrices([offer], "هدفون هایلو T15", DateTimeOffset.UtcNow.AddHours(-10));
        var stale = await Service(http, new ExternalSourceGate(0), store).SearchAsync("هدفون هایلو T15", CancellationToken.None);
        Assert.NotEmpty(http.Urls);
        Assert.Single(stale.Offers);
        Assert.True(stale.Limited);
        Assert.Contains(stale.Problems, p => p.Contains("ساعت پیش"));

        // خیلی کهنه (۴ روز): دیگر استفاده نمی‌شود.
        store.Rows["هدفون هایلو t15"] = new StoredCompetitorPrices([offer], null, DateTimeOffset.UtcNow.AddDays(-4));
        var old = await Service(http, new ExternalSourceGate(0), store).SearchAsync("هدفون هایلو T15", CancellationToken.None);
        Assert.Empty(old.Offers);
    }

    [Fact]
    public async Task Successful_lookups_are_saved_to_the_persistent_store()
    {
        var store = new MemoryStore();
        var http = new FakeHttp(url => url.Contains("digikala") ? new SupplierHttpResponse(200, DigikalaJson) : new SupplierHttpResponse(404, null));
        var result = await Service(http, new ExternalSourceGate(0), store).SearchAsync("هدفون هایلو T15", CancellationToken.None);
        Assert.NotEmpty(result.Offers);
        Assert.False(result.Limited); // ۴۰۴ محدودیت نیست
        var saved = Assert.Single(store.Rows);
        Assert.Equal("هدفون هایلو t15", saved.Key);
    }

    // ── تصمیم قیمت‌گذاری بازار ──

    private static PricingCosts Costs() => new(0, 0, 0, 1, 0, 0, 30, 8, 2, 1000);

    [Fact]
    public void Market_decision_applies_the_advisor_price_when_offers_exist()
    {
        var lookup = new CompetitorLookup([new("الف", 285_000, "ترب"), new("ب", 290_000, "ترب"), new("دیجی‌کالا", 310_000, "دیجی‌کالا")], [], "هدفون");
        var (status, suggestion, note) = MarketPricingService.Decide(200_000, Costs(), lookup, 1);
        Assert.Equal(MarketPriceCheckStatus.Applied, status);
        Assert.Equal(PriceAdvisor.Suggest(200_000, 0, Costs(), lookup.Offers).Suggested, suggestion.Suggested);
        Assert.True(suggestion.Suggested < 285_000);
        Assert.Contains("ترب", note);
    }

    [Fact]
    public void Market_decision_never_goes_below_the_minimum_margin()
    {
        var lookup = new CompetitorLookup([new("ارزان", 150_000, "ترب"), new("ارزان۲", 155_000, "ترب")], [], null);
        var (status, suggestion, _) = MarketPricingService.Decide(200_000, Costs(), lookup, 1);
        Assert.Equal(MarketPriceCheckStatus.Applied, status);
        Assert.Equal(suggestion.MinPrice, suggestion.Suggested);
        Assert.True(suggestion.Suggested > 200_000);
    }

    [Fact]
    public void Market_decision_retries_when_limited_and_keeps_the_price_otherwise()
    {
        var limited = new CompetitorLookup([], ["ترب: محدود"], null, Limited: true);
        Assert.Equal(MarketPriceCheckStatus.Pending, MarketPricingService.Decide(200_000, Costs(), limited, 1).Status);
        Assert.Equal(MarketPriceCheckStatus.Kept, MarketPricingService.Decide(200_000, Costs(), limited, MarketPricingService.MaxAttempts).Status);

        var notFound = new CompetitorLookup([], ["ترب: کالای مشابهی پیدا نشد"], null);
        var (status, _, note) = MarketPricingService.Decide(200_000, Costs(), notFound, 1);
        Assert.Equal(MarketPriceCheckStatus.Kept, status);
        Assert.Contains("پیدا نشد", note);
    }

    [Theory]
    [InlineData(1, 15)]
    [InlineData(2, 30)]
    [InlineData(3, 60)]
    [InlineData(5, 240)]
    [InlineData(8, 240)]
    public void Market_retry_delay_grows_and_is_capped(int attempts, int minutes)
        => Assert.Equal(TimeSpan.FromMinutes(minutes), MarketPricingService.RetryDelay(attempts));

    // ── دامنهٔ اصلی ──

    [Theory]
    [InlineData("www.faratek.ir", "/", true)]
    [InlineData("shop.hosting-temp.ir", "/product/x", true)]
    [InlineData("faratek.ir", "/", false)]
    [InlineData("FARATEK.IR", "/", false)]
    [InlineData("localhost", "/", false)]
    [InlineData("app.localhost", "/", false)]
    [InlineData("127.0.0.1", "/", false)]
    [InlineData("[::1]", "/", false)]
    [InlineData("185.10.20.30", "/", false)]
    [InlineData("www.faratek.ir", "/health", false)]
    [InlineData("www.faratek.ir", "/healthz", false)]
    [InlineData("www.faratek.ir", "/.well-known/acme-challenge/abc", false)]
    [InlineData("", "/", false)]
    public void Canonical_host_redirect_rules(string host, string path, bool expected)
        => Assert.Equal(expected, CanonicalHostMiddleware.ShouldRedirect("faratek.ir", host, new PathString(path)));

    [Theory]
    [InlineData("faratek.ir", "faratek.ir")]
    [InlineData(" https://www.Faratek.IR/ ", "www.faratek.ir")]
    [InlineData("http://faratek.ir:8080/x", "faratek.ir")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData("127.0.0.1", null)]
    [InlineData("not a host", null)]
    [InlineData("off", null)]
    [InlineData("OFF", null)]
    [InlineData("none", null)]
    [InlineData("localhost", null)]
    public void Canonical_host_setting_is_normalised(string? value, string? expected)
        => Assert.Equal(expected, CanonicalHostMiddleware.Normalize(value));

    // ── شمارهٔ مالک ──

    [Theory]
    [InlineData("09013181090", "09013181090")]
    [InlineData("۰۹۰۱ ۳۱۸ ۱۰۹۰", "09013181090")]
    [InlineData("+98 901-318-1090", "09013181090")]
    [InlineData("989013181090", "09013181090")]
    [InlineData("9013181090", null)]
    [InlineData("0901318109", null)]
    [InlineData("02112345678", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Owner_phone_is_normalised(string? value, string? expected)
        => Assert.Equal(expected, AdminAccessService.NormalizeOwnerPhone(value));

    [Theory]
    [InlineData("GET", "www.faratek.ir", "/product/x", "?a=1", 301, "https://faratek.ir/product/x?a=1")]
    [InlineData("POST", "www.faratek.ir", "/login", "", 308, "https://faratek.ir/login")]
    [InlineData("GET", "faratek.ir", "/", "", 200, null)]
    [InlineData("GET", "localhost", "/", "", 200, null)]
    public async Task Canonical_host_middleware_redirects_only_foreign_hosts(string method, string host, string path, string query, int status, string? location)
    {
        var called = false;
        var middleware = new CanonicalHostMiddleware(_ => { called = true; return Task.CompletedTask; },
            Microsoft.Extensions.Options.Options.Create(new Fratak_Shop.Web.Infrastructure.ShopOptions { CanonicalHost = "faratek.ir" }));
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Host = new HostString(host);
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);

        await middleware.InvokeAsync(context);

        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal(location is null, called);
        if (location is not null)
            Assert.Equal(location, context.Response.Headers.Location.ToString());
    }

    [Theory]
    [InlineData("off")]
    [InlineData("")]
    [InlineData("localhost")]
    public async Task Canonical_host_middleware_is_disabled_by_off_values(string setting)
    {
        var called = false;
        var middleware = new CanonicalHostMiddleware(_ => { called = true; return Task.CompletedTask; },
            Microsoft.Extensions.Options.Options.Create(new Fratak_Shop.Web.Infrastructure.ShopOptions { CanonicalHost = setting }));
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("fratak.test");
        context.Request.Path = "/";

        await middleware.InvokeAsync(context);

        Assert.True(called);
    }

    [Theory]
    [InlineData("12345678", true)]
    [InlineData("1234567", false)]
    [InlineData("        ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Default_admin_password_length_is_enforced(string? password, bool expected)
        => Assert.Equal(expected, DefaultAdminAccount.IsValidPassword(password));

    [Fact]
    public void Default_admin_password_never_appears_in_text_or_logs()
    {
        var account = new DefaultAdminAccount("09013181090", "محمدامین", "ناظری", "12345678");
        var options = new Fratak_Shop.Web.Infrastructure.DefaultAdminOptions { PhoneNumber = "09013181090", Password = "12345678" };

        Assert.DoesNotContain("12345678", account.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("12345678", options.ToString(), StringComparison.Ordinal);
        Assert.Equal("0901***1090", DefaultAdminAccount.Mask("09013181090"));
        Assert.False(DefaultAdminAccount.IsValidPassword(new string('x', DefaultAdminAccount.MaxPasswordLength + 1)));
    }

    [Fact]
    public void Default_admins_are_read_from_configuration_and_checked_in_constant_time()
    {
        var shop = new Fratak_Shop.Web.Infrastructure.ShopOptions
        {
            DefaultAdmins =
            [
                new() { PhoneNumber = "۰۹۰۱ ۳۱۸ ۱۰۹۰", FirstName = "محمدامین", Password = "12345678" },
                new() { PhoneNumber = "09121111111", Password = "short" },
                new() { PhoneNumber = " " },
            ],
        };
        Assert.Equal(2, shop.DefaultAdminAccounts().Count);

        var check = new Fratak_Shop.Web.Infrastructure.Auth.DefaultAdminPasswords(Microsoft.Extensions.Options.Options.Create(shop));
        Assert.True(check.IsDefault("09013181090", "12345678"));
        Assert.True(check.IsDefault("+989013181090", "12345678"));
        Assert.False(check.IsDefault("09013181090", "12345679"));
        Assert.False(check.IsDefault("09121111111", "short")); // رمز نامعتبر هرگز «پیش‌فرض» حساب نمی‌شود
        Assert.Single(check.Phones);

        var hash = PasswordHasher.Hash("12345678");
        Assert.True(check.StillUsesDefault("09013181090", hash));
        Assert.False(check.StillUsesDefault("09013181090", PasswordHasher.Hash("something-else")));
        Assert.False(check.StillUsesDefault("09013181090", null));
    }
}
