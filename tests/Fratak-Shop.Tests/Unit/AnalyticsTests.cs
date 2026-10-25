using Fratak_Shop.Core.Services.Reports;
using Fratak_Shop.Data.Entities.Analytics;
using Fratak_Shop.Web.Infrastructure.Analytics;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست قواعد خالص آمار بازدید (مرحلهٔ هفتم – فاز ۷): تشخیص منبع ورودی، دستگاه، ربات، کوکی منبع،
/// سازندهٔ لینک کمپین، پله‌های قیف و ادغام بازدید با فروش هر منبع.
/// </summary>
public class AnalyticsTests
{
    private const string Own = "fratak.ir";

    [Theory]
    [InlineData("?utm_source=Instagram&utm_medium=Story&utm_campaign=Yalda 1405", null, "instagram", "story", "yalda-1405")]
    [InlineData("?utm_source=sms", "https://www.google.com/", "sms", "campaign", null)]
    [InlineData("?gclid=abc123", "https://www.google.com/", "google", "cpc", null)]
    [InlineData("", "https://www.google.com/search?q=x", "google", "organic", null)]
    [InlineData("", "https://www.google.co.uk/", "google", "organic", null)]
    [InlineData("", "https://www.bing.com/", "bing", "organic", null)]
    [InlineData("", "https://l.instagram.com/?u=x", "instagram", "social", null)]
    [InlineData("", "https://t.me/fratak", "telegram", "social", null)]
    [InlineData("", "android-app://org.telegram.messenger/", "telegram", "social", null)]
    [InlineData("", "https://t.co/abc", "x", "social", null)]
    [InlineData("", "https://torob.com/p/123/", "torob", "comparison", null)]
    [InlineData("", "https://mail.google.com/mail/u/0/", "gmail", "email", null)]
    [InlineData("", "https://www.someblog.ir/post/1", "someblog.ir", "referral", null)]
    [InlineData("", "https://fratak.ir/product/a", "direct", "direct", null)]
    [InlineData("", "https://www.fratak.ir/", "direct", "direct", null)]
    [InlineData("", "", "direct", "direct", null)]
    [InlineData("?utm_campaign=newsletter", "", "direct", "campaign", "newsletter")]
    [InlineData("?UTM_SOURCE=Bale", "", "bale", "campaign", null)]
    public void Classify_detects_source_medium_and_campaign(string query, string? referrer, string source, string medium, string? campaign)
    {
        var touch = TrafficRules.Classify(query, referrer, Own);

        Assert.Equal(source, touch.Source);
        Assert.Equal(medium, touch.Medium);
        Assert.Equal(campaign, touch.Campaign);
    }

    [Fact]
    public void Classify_ignores_garbage_and_cuts_long_values()
    {
        var touch = TrafficRules.Classify("?utm_source=" + new string('a', 500) + "&utm_medium=<script>", "not a url", Own);

        Assert.Equal(60, touch.Source.Length);
        Assert.Equal("script", touch.Medium);
        Assert.True(TrafficRules.Classify("?%%%=1&utm_source=%E0%A4", "javascript:alert(1)", Own).Source.Length > 0);
    }

    [Theory]
    [InlineData(" Yalda ۱۴۰۵ ", "yalda-1405")]
    [InlineData("حراج  پاییز", "حراج-پاییز")]
    [InlineData("a/b?c=d", "abcd")]
    [InlineData("   ", null)]
    [InlineData("---", null)]
    public void Clean_normalizes_values(string input, string? expected) => Assert.Equal(expected, TrafficRules.Clean(input, 100));

    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148", "mobile")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; SM-S918B) AppleWebKit/537.36 Chrome/120.0 Mobile Safari/537.36", "mobile")]
    [InlineData("Mozilla/5.0 (Linux; Android 13; SM-X700) AppleWebKit/537.36 Chrome/120.0 Safari/537.36", "tablet")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15", "tablet")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0 Safari/537.36", "desktop")]
    [InlineData(null, "desktop")]
    public void Device_is_detected_from_user_agent(string? userAgent, string expected) => Assert.Equal(expected, TrafficRules.Device(userAgent));

    [Theory]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", true)]
    [InlineData("TelegramBot (like TwitterBot)", true)]
    [InlineData("curl/8.4.0", true)]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 HeadlessChrome/120.0 Safari/537.36", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0 Safari/537.36", false)]
    public void Bots_are_not_counted(string? userAgent, bool expected) => Assert.Equal(expected, TrafficRules.IsBot(userAgent));

    [Fact]
    public void Source_cookie_round_trips_and_rejects_tampering()
    {
        var touch = new TrafficTouch("instagram", "story", "یلدا-1405");
        var decoded = TrafficRules.DecodeTouch(TrafficRules.EncodeTouch(touch));

        Assert.Equal(touch, decoded);
        Assert.Equal(new TrafficTouch("google", "organic", null), TrafficRules.DecodeTouch(TrafficRules.EncodeTouch(new TrafficTouch("google", "organic", null))));
        Assert.Null(TrafficRules.DecodeTouch("only-one-part"));
        Assert.Null(TrafficRules.DecodeTouch("||"));
        Assert.Null(TrafficRules.DecodeTouch(new string('a', 500)));
        Assert.Null(TrafficRules.DecodeTouch(null));
    }

    [Fact]
    public void Visitor_keys_must_be_32_lowercase_hex()
    {
        var key = TrafficRules.NewVisitorKey();

        Assert.True(TrafficRules.IsValidVisitorKey(key));
        Assert.NotEqual(key, TrafficRules.NewVisitorKey());
        Assert.False(TrafficRules.IsValidVisitorKey(key.ToUpperInvariant()));
        Assert.False(TrafficRules.IsValidVisitorKey(key[..31]));
        Assert.False(TrafficRules.IsValidVisitorKey("'; drop table--" + key[..17]));
        Assert.False(TrafficRules.IsValidVisitorKey(null));
    }

    [Fact]
    public void Campaign_url_keeps_other_parameters_and_replaces_old_utm()
    {
        var url = TrafficRules.BuildCampaignUrl("https://fratak.ir/product/x?color=red&utm_source=old", "Instagram", "Story", "Yalda 1405", null);

        Assert.Equal("https://fratak.ir/product/x?color=red&utm_source=instagram&utm_medium=story&utm_campaign=yalda-1405", url);
        Assert.Equal("https://fratak.ir/?utm_source=sms", TrafficRules.BuildCampaignUrl("https://fratak.ir/", "sms", null, null));
        Assert.Null(TrafficRules.BuildCampaignUrl("javascript:alert(1)", "x", null, null));
        Assert.Null(TrafficRules.BuildCampaignUrl("/relative", "x", null, null));
        Assert.Null(TrafficRules.BuildCampaignUrl("https://fratak.ir/", "  ", null, null));
    }

    [Fact]
    public void Persian_titles_fall_back_to_raw_value()
    {
        Assert.Equal("گوگل", TrafficRules.SourceTitle("google"));
        Assert.Equal("مستقیم", TrafficRules.SourceTitle(null));
        Assert.Equal("someblog.ir", TrafficRules.SourceTitle("someblog.ir"));
        Assert.Equal("جست‌وجوی طبیعی", TrafficRules.MediumTitle("organic"));
        Assert.Equal("ثبت توسط کارکنان", TrafficRules.SourceTitle(TrafficRules.StaffSource));
    }

    [Fact]
    public void Funnel_steps_compute_rates_from_previous_and_start()
    {
        var steps = AnalyticsReportService.BuildSteps(1000, 400, 100, 50, 20, 10);

        Assert.Equal(6, steps.Count);
        Assert.Null(steps[0].RateFromPrevious);
        Assert.Equal(100, steps[0].RateFromStart);
        Assert.Equal(40, steps[1].RateFromPrevious);
        Assert.Equal(25, steps[2].RateFromPrevious);
        Assert.Equal(50, steps[5].RateFromPrevious);
        Assert.Equal(1, steps[5].RateFromStart);

        var empty = AnalyticsReportService.BuildSteps(0, 0, 0, 0, 0, 0);
        Assert.All(empty, s => Assert.Equal(0, s.RateFromStart));
        // کسی که بدون دیدن صفحهٔ محصول از فهرست به سبد افزوده: درصد از ۱۰۰ بیشتر نمی‌شود.
        Assert.Equal(100, AnalyticsReportService.BuildSteps(10, 2, 5, 0, 0, 0)[2].RateFromPrevious);
    }

    [Fact]
    public void Traffic_merge_combines_visits_with_sales_and_sorts_by_revenue()
    {
        var rows = AnalyticsReportService.MergeTraffic(
            [("google", "organic", null, 500), ("instagram", "social", null, 200), ("direct", "direct", null, 300)],
            [("instagram", "social", null, 4, 8_000_000m), ("google", "organic", null, 2, 3_000_000m), ("staff", "staff", null, 1, 500_000m)]).ToList();

        Assert.Equal("instagram,google,staff,direct", string.Join(',', rows.Select(r => r.Source)));
        Assert.Equal(2, rows[0].Conversion);
        Assert.Equal(0.4, rows[1].Conversion);
        Assert.Null(rows[2].Conversion); // سفارش پنل بدون بازدید
        Assert.Equal(0, rows[3].Orders);
    }

    [Fact]
    public void Heatmap_rows_start_on_saturday()
    {
        Assert.Equal(0, HeatmapReport.Row(DayOfWeek.Saturday));
        Assert.Equal(1, HeatmapReport.Row(DayOfWeek.Sunday));
        Assert.Equal(6, HeatmapReport.Row(DayOfWeek.Friday));
    }

    [Theory]
    [InlineData("/product/abc?x=1#top", "/product/abc")]
    [InlineData("/category/%D9%85%D9%88%D8%A8%D8%A7%DB%8C%D9%84/", "/category/موبایل")]
    [InlineData("/", "/")]
    [InlineData("//evil.com", null)]
    [InlineData("https://evil.com/", null)]
    [InlineData("", null)]
    public void Beacon_path_is_cleaned(string input, string? expected) => Assert.Equal(expected, AnalyticsEndpoints.CleanPath(input));

    [Fact]
    public async Task Buffer_merges_hits_of_same_visitor_and_day()
    {
        var buffer = new AnalyticsBuffer();
        var key = TrafficRules.NewVisitorKey();
        var now = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        buffer.Visit(key, null, "mobile", new TrafficTouch("instagram", "social", null), "/product/a", FunnelStage.ProductView, now);
        buffer.Visit(key, null, "mobile", TrafficTouch.Direct, "/cart", FunnelStage.None, now.AddSeconds(30));
        buffer.Stage(key, Guid.NewGuid(), FunnelStage.AddToCart, now.AddSeconds(40));
        buffer.Count("pv", "p1", now);
        buffer.Count("pv", "p1", now);

        var batch = await buffer.DrainAsync();

        var hit = Assert.Single(batch.Visitors);
        Assert.Equal("instagram", hit.Touch.Source);       // منبع اولین بازدید می‌ماند
        Assert.Equal("/product/a", hit.LandingPath);
        Assert.Equal(2, hit.PageViews);
        Assert.Equal(1 | 2 | 4, hit.Stages);
        Assert.NotNull(hit.UserId);
        Assert.Equal(2, Assert.Single(batch.Counters).Count);
        Assert.True((await buffer.DrainAsync()).IsEmpty);  // بعد از خالی شدن، چیزی دوباره ذخیره نمی‌شود
    }
}
