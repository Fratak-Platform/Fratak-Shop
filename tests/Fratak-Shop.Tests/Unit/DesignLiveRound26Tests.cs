using Fratak_Shop.Core.Services.Design;
using Fratak_Shop.Data.Entities.Design;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// دور ۲۶ – «بنر زنده» (DesignLive): خروجی این کلاس با Html.Raw در صفحه می‌نشیند، پس این آزمون‌ها بیشتر «حمله» هستند:
/// اسکریپت در متن، رنگ آلوده به CSS، عکس از بیرون سایت، شیء ناشناخته و JSON خراب. انتظار: یا HTML امن، یا null (← تصویر ثابت).
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Round26")]
public sealed class DesignLiveRound26Tests
{
    /// <summary>طرح ساده با یک شیء دلخواه (برای آزمون یک ویژگی در هر بار).</summary>
    private static string Design(string objectJson, int width = 1920, int height = 300, string background = "#ffffff") =>
        "{\"fsWidth\":" + width + ",\"fsHeight\":" + height + ",\"background\":\"" + background + "\",\"objects\":[" + objectJson + "]}";

    private const string Headline = "{\"type\":\"textbox\",\"left\":100,\"top\":50,\"width\":800,\"fontSize\":60,\"fill\":\"#111111\",\"text\":\"تخفیف خوراکی\",\"fsAnim\":\"rise\",\"fsAnimDelay\":0.4}";

    // ---------------- بهترین حالت ----------------

    [Fact]
    public void Simple_design_becomes_responsive_html_with_animation()
    {
        var html = DesignLive.Html(Design(Headline), BannerMotion.Soft);

        Assert.NotNull(html);
        Assert.Contains("fsb fsb-soft", html, StringComparison.Ordinal);
        Assert.Contains("aspect-ratio:1920/300", html, StringComparison.Ordinal);
        Assert.Contains("fsb-a-rise", html, StringComparison.Ordinal);
        Assert.Contains("--d:0.4s", html, StringComparison.Ordinal);
        Assert.Contains("تخفیف خوراکی", html, StringComparison.Ordinal);
        Assert.Contains("cqw", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Loop_motion_uses_loop_class()
    {
        var html = DesignLive.Html(Design(Headline), BannerMotion.Loop);
        Assert.NotNull(html);
        Assert.Contains("fsb-loop", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Local_upload_image_is_allowed()
    {
        var image = "{\"type\":\"image\",\"left\":0,\"top\":0,\"width\":400,\"height\":300,\"src\":\"/uploads/products/kettle-1.webp\"}";
        var html = DesignLive.Html(Design(image), BannerMotion.Soft);
        Assert.NotNull(html);
        Assert.Contains("src=\"/uploads/products/kettle-1.webp\"", html, StringComparison.Ordinal);
    }

    // ---------------- حالت‌های بی‌اثر ----------------

    [Fact]
    public void No_motion_or_empty_input_gives_null()
    {
        Assert.Null(DesignLive.Html(Design(Headline), BannerMotion.None));
        Assert.Null(DesignLive.Html(null, BannerMotion.Soft));
        Assert.Null(DesignLive.Html("", BannerMotion.Soft));
        Assert.Null(DesignLive.Html("{bad json", BannerMotion.Soft));
        Assert.Null(DesignLive.Html("[1,2]", BannerMotion.Soft));
        Assert.Null(DesignLive.Html("{\"fsWidth\":1920,\"fsHeight\":300,\"objects\":[]}", BannerMotion.Soft));
    }

    [Fact]
    public void Absurd_sizes_give_null()
    {
        Assert.Null(DesignLive.Html(Design(Headline, width: 10), BannerMotion.Soft));
        Assert.Null(DesignLive.Html(Design(Headline, height: 99999), BannerMotion.Soft));
    }

    [Fact]
    public void Too_many_objects_give_null()
    {
        var many = string.Join(",", Enumerable.Repeat(Headline, DesignLive.MaxObjects + 1));
        Assert.Null(DesignLive.Html(Design(many), BannerMotion.Soft));
    }

    [Fact]
    public void Unknown_object_type_gives_null_so_static_image_is_used()
    {
        var path = "{\"type\":\"path\",\"left\":0,\"top\":0,\"width\":10,\"height\":10,\"path\":\"M0 0\"}";
        Assert.Null(DesignLive.Html(Design(path), BannerMotion.Soft));
    }

    // ---------------- حمله ----------------

    [Fact]
    public void Script_in_text_is_encoded()
    {
        var evil = "{\"type\":\"textbox\",\"left\":0,\"top\":0,\"width\":500,\"fontSize\":40,\"text\":\"<script>alert(1)</script><img src=x onerror=alert(2)>\"}";
        var html = DesignLive.Html(Design(evil), BannerMotion.Soft);

        Assert.NotNull(html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img src=x", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("red;background:url(https://evil.example/x)")]
    [InlineData("#fff\\\" onmouseover=\\\"alert(1)")]
    [InlineData("expression(alert(1))")]
    [InlineData("rgb(1,2,3);position:fixed")]
    public void Css_injection_in_colour_is_dropped(string colour)
    {
        var evil = "{\"type\":\"rect\",\"left\":0,\"top\":0,\"width\":100,\"height\":100,\"fill\":\"" + colour + "\"}";
        var html = DesignLive.Html(Design(evil, background: colour), BannerMotion.Soft);

        Assert.NotNull(html);
        Assert.DoesNotContain("evil.example", html, StringComparison.Ordinal);
        Assert.DoesNotContain("onmouseover", html, StringComparison.Ordinal);
        Assert.DoesNotContain("expression", html, StringComparison.Ordinal);
        Assert.DoesNotContain("position:fixed", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://evil.example/a.webp")]
    [InlineData("//evil.example/a.webp")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/uploads/../appsettings.json")]
    [InlineData("/uploads/a.svg")]
    [InlineData("/uploads/a.webp\\\" onerror=\\\"alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    public void Foreign_or_unsafe_image_gives_null(string src)
    {
        var image = "{\"type\":\"image\",\"left\":0,\"top\":0,\"width\":400,\"height\":300,\"src\":\"" + src + "\"}";
        Assert.Null(DesignLive.Html(Design(image), BannerMotion.Soft));
    }

    [Fact]
    public void Unknown_animation_name_is_ignored()
    {
        var evil = "{\"type\":\"textbox\",\"left\":0,\"top\":0,\"width\":500,\"fontSize\":40,\"text\":\"سلام\",\"fsAnim\":\"rise\\\" onclick=\\\"x\"}";
        var html = DesignLive.Html(Design(evil), BannerMotion.Soft);

        Assert.NotNull(html);
        Assert.DoesNotContain("onclick", html, StringComparison.Ordinal);
        Assert.DoesNotContain("fsb-a-", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Hidden_objects_are_skipped()
    {
        var hidden = "{\"type\":\"textbox\",\"left\":0,\"top\":0,\"width\":500,\"fontSize\":40,\"text\":\"پنهان\",\"visible\":false}";
        var html = DesignLive.Html(Design(Headline + "," + hidden), BannerMotion.Soft);

        Assert.NotNull(html);
        Assert.DoesNotContain("پنهان", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("#abc", "#abc")]
    [InlineData("#AABBCC", "#aabbcc")]
    [InlineData("rgba(0, 0, 0, 0.25)", "rgba(0, 0, 0, 0.25)")]
    [InlineData("transparent", "transparent")]
    public void Safe_colours_pass(string input, string expected) =>
        Assert.Equal(expected, DesignLive.Color(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#12")]
    [InlineData("url(x)")]
    public void Unsafe_colours_are_null(string? input) =>
        Assert.Null(DesignLive.Color(input));
}
