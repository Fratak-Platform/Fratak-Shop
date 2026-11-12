using System.Text.Json;
using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Services.Branding;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Ops;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های «استودیوی لوگو» (مرحلهٔ دوازدهم): خواندن/نوشتن JSON تنظیمات، اعتبارسنجی محدوده‌ها و تلاش‌های تزریق،
/// پیش‌فرض‌های هر محل (باید همان ظاهر قبلی سایت باشند)، ساخت HTML لوگو (Encode، پنهان شدن، نشانی مطلق ایمیل)،
/// آیکون‌های اپ (PWA) و صفحهٔ تعمیرات.
/// </summary>
[Trait("Category", "Unit")]
public class BrandStyleTests
{
    private static BrandView Resolve(BrandPlace place, StoreSettingDto? settings = null, BrandStyleConfig? config = null) =>
        BrandResolver.Resolve(place, settings ?? new StoreSettingDto(), config ?? new BrandStyleConfig(), BrandIdentity.Defaults);

    private static BrandStyleConfig With(BrandPlace place, BrandPlaceStyle style)
    {
        var config = new BrandStyleConfig();
        config.Places[place.ToString()] = style;
        return config;
    }

    // ── JSON ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{broken")]
    [InlineData("[]")]
    [InlineData("123")]
    [InlineData("{\"places\":5}")]
    [InlineData("{\"places\":{\"header\":{\"iconHeight\":\"abc\"}}}")]
    public void Parse_never_throws_and_falls_back_to_defaults(string? json)
    {
        var config = BrandStyleRules.Parse(json);

        Assert.NotNull(config);
        Assert.Empty(config.Places);
        Assert.Equal(44, Resolve(BrandPlace.Header, config: config).IconHeight);
    }

    [Fact]
    public void Parse_drops_unknown_places_and_ignores_key_case()
    {
        var config = BrandStyleRules.Parse("{\"places\":{\"HEADER\":{\"iconHeight\":60},\"Nowhere\":{\"iconHeight\":20}}}");

        Assert.Single(config.Places);
        Assert.Equal(60, config.For(BrandPlace.Header).IconHeight);
    }

    [Fact]
    public void Serialize_of_defaults_is_null_and_round_trip_keeps_values()
    {
        Assert.Null(BrandStyleRules.Serialize(new BrandStyleConfig()));
        Assert.Null(BrandStyleRules.Serialize(With(BrandPlace.Footer, new BrandPlaceStyle())));

        var config = With(BrandPlace.Footer, new BrandPlaceStyle { IconHeight = 52, PartGap = 3, Layout = BrandLayout.IconTop, LetterSpacing = -0.5 });
        config.Tagline = "خرید آسان";
        var json = BrandStyleRules.Serialize(config);
        Assert.NotNull(json);
        Assert.DoesNotContain("null", json);

        var back = BrandStyleRules.Parse(json);
        var footer = back.For(BrandPlace.Footer);
        Assert.Equal(52, footer.IconHeight);
        Assert.Equal(3, footer.PartGap);
        Assert.Equal(BrandLayout.IconTop, footer.Layout);
        Assert.Equal(-0.5, footer.LetterSpacing);
        Assert.Equal("خرید آسان", back.Tagline);
    }

    // ── اعتبارسنجی ─────────────────────────────────────────────────

    [Theory]
    [InlineData(11, null, null)]
    [InlineData(201, null, null)]
    [InlineData(null, 7, null)]
    [InlineData(null, 97, null)]
    [InlineData(null, null, 81)]
    [InlineData(null, null, -1)]
    public void Validate_rejects_out_of_range_sizes(int? iconHeight, int? fontSize, int? gap)
    {
        var config = With(BrandPlace.Auth, new BrandPlaceStyle { IconHeight = iconHeight, FontSize = fontSize, IconGap = gap });

        Assert.NotNull(BrandStyleRules.Validate(config));
    }

    [Fact]
    public void Validate_accepts_the_edges_of_every_range()
    {
        var config = With(BrandPlace.Auth, new BrandPlaceStyle
        {
            IconHeight = BrandStyleRules.MinIcon, IconHeightMobile = BrandStyleRules.MaxIcon,
            FontSize = BrandStyleRules.MinFont, FontSizeMobile = BrandStyleRules.MaxFont,
            IconGap = 0, PartGap = BrandStyleRules.MaxGap, TaglineGap = 0, TaglineSize = BrandStyleRules.MaxTagline,
            TextOffsetY = -BrandStyleRules.MaxOffset, LetterSpacing = BrandStyleRules.MaxLetterSpacing, FontWeight = 900,
        });

        Assert.Null(BrandStyleRules.Validate(config));
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#fff;background:url(x)")]
    [InlineData("#000000}body{display:none")]
    [InlineData("expression(alert(1))")]
    public void Validate_rejects_colour_injection(string colour)
    {
        Assert.NotNull(BrandStyleRules.Validate(With(BrandPlace.Header, new BrandPlaceStyle { Color1 = colour })));
        Assert.NotNull(BrandStyleRules.Validate(new BrandStyleConfig { TaglineColor = colour }));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("//evil.example/a.png")]
    [InlineData("/a.png\" onerror=\"alert(1)")]
    [InlineData("/a b.png")]
    [InlineData("data:image/svg+xml,<svg onload=alert(1)>")]
    [InlineData("/x.png') ; background:url('y")]
    public void Validate_rejects_dangerous_icon_urls(string url)
    {
        Assert.NotNull(BrandStyleRules.Validate(With(BrandPlace.Auth, new BrandPlaceStyle { IconUrl = url })));
        Assert.NotNull(BrandStyleRules.Validate(new BrandStyleConfig { PwaMaskableIconUrl = url }));
    }

    [Theory]
    [InlineData(BrandPlace.Invoice)]
    [InlineData(BrandPlace.Email)]
    public void Raster_places_reject_svg_icons(BrandPlace place)
    {
        Assert.NotNull(BrandStyleRules.Validate(With(place, new BrandPlaceStyle { IconUrl = "/uploads/media/logo.svg" })));
        Assert.Null(BrandStyleRules.Validate(With(place, new BrandPlaceStyle { IconUrl = "/uploads/media/logo.png" })));
        Assert.Null(BrandStyleRules.Validate(With(BrandPlace.Footer, new BrandPlaceStyle { IconUrl = "/uploads/media/logo.svg" })));
    }

    [Fact]
    public void Raster_places_skip_an_svg_site_logo()
    {
        var svgLogo = new StoreSettingDto { LogoUrl = "/uploads/media/logo.svg" };
        var invoice = Resolve(BrandPlace.Invoice, svgLogo);
        Assert.Equal(BrandIdentity.MarkPng, invoice.IconUrl);
        Assert.True(invoice.IsDefaultIcon);
        Assert.Equal("/uploads/media/logo.svg", Resolve(BrandPlace.Header, svgLogo).IconUrl);
        Assert.Equal("/uploads/media/logo.png", Resolve(BrandPlace.Email, new StoreSettingDto { LogoUrl = "/uploads/media/logo.png" }).IconUrl);
    }

    [Theory]
    [InlineData("<script>")]
    [InlineData("a{b}")]
    [InlineData("خط\nدوم")]
    public void Validate_rejects_markup_in_texts(string text)
    {
        Assert.NotNull(BrandStyleRules.Validate(With(BrandPlace.Footer, new BrandPlaceStyle { Text1 = text })));
    }

    [Fact]
    public void Header_and_auth_cannot_be_hidden_and_something_must_remain_visible()
    {
        Assert.NotNull(BrandStyleRules.Validate(With(BrandPlace.Header, new BrandPlaceStyle { Visible = false })));
        Assert.NotNull(BrandStyleRules.Validate(With(BrandPlace.Auth, new BrandPlaceStyle { Visible = false })));
        Assert.Null(BrandStyleRules.Validate(With(BrandPlace.Footer, new BrandPlaceStyle { Visible = false })));
        Assert.NotNull(BrandStyleRules.Validate(With(BrandPlace.Footer, new BrandPlaceStyle { ShowIcon = false, ShowText = false })));

        var unknown = new BrandStyleConfig();
        unknown.Places["Nowhere"] = new BrandPlaceStyle();
        Assert.NotNull(BrandStyleRules.Validate(unknown));
    }

    // ── پیش‌فرض‌ها = ظاهر قبلی ─────────────────────────────────────

    [Fact]
    public void Defaults_match_the_previous_look()
    {
        var header = Resolve(BrandPlace.Header);
        Assert.Equal(44, header.IconHeight);
        Assert.False(header.ShowText);
        Assert.True(header.ShowIcon);
        Assert.Equal(BrandIdentity.Mark, header.IconUrl);
        Assert.Equal(BrandIdentity.MarkOnDark, header.IconDarkUrl);

        var auth = Resolve(BrandPlace.Auth);
        Assert.True(auth.ShowText);
        Assert.Equal("فراتک", auth.Text1);
        Assert.Equal("شاپ", auth.Text2);
        Assert.Equal(BrandIdentity.Navy, auth.Color1);
        Assert.Equal(BrandIdentity.Blue, auth.Color2);

        var admin = Resolve(BrandPlace.Admin);
        Assert.Equal(30, admin.IconHeight);
        Assert.True(admin.OnDark);
        Assert.Equal("#FFFFFF", admin.Color1);
        Assert.Equal(BrandIdentity.MarkOnDark, admin.IconUrl);

        Assert.Equal(BrandIdentity.MarkPng, Resolve(BrandPlace.Invoice).IconUrl);
        Assert.Equal(BrandIdentity.MarkPng, Resolve(BrandPlace.Email).IconUrl);
        Assert.Equal(BrandLayout.IconTop, Resolve(BrandPlace.Maintenance).Layout);
        Assert.False(Resolve(BrandPlace.Footer).ShowTagline);
    }

    [Fact]
    public void Automatic_sizes_follow_the_icon_and_explicit_values_win()
    {
        var auto = Resolve(BrandPlace.Header, config: With(BrandPlace.Header, new BrandPlaceStyle { IconHeight = 60, IconHeightMobile = 40 }));
        Assert.Equal(39, auto.FontSize);          // round(60 × 0.65)
        Assert.Equal(26, auto.FontSizeMobile);    // round(39 × 40 / 60)
        Assert.Equal(12, auto.IconGap);           // max(6, 60 / 5)
        Assert.Equal(8, auto.IconGapMobile);      // max(6, 40 / 5)
        Assert.Equal(11, auto.PartGap);           // round(39 × 0.28)

        var set = Resolve(BrandPlace.Header, config: With(BrandPlace.Header, new BrandPlaceStyle { IconHeight = 60, FontSize = 20, IconGap = 2, PartGap = 0 }));
        Assert.Equal(20, set.FontSize);
        Assert.Equal(2, set.IconGap);
        Assert.Equal(0, set.PartGap);
    }

    [Fact]
    public void Non_responsive_places_ignore_mobile_sizes()
    {
        var admin = Resolve(BrandPlace.Admin, config: With(BrandPlace.Admin, new BrandPlaceStyle { IconHeight = 36, IconHeightMobile = 80, FontSizeMobile = 50 }));

        Assert.Equal(36, admin.IconHeightMobile);
        Assert.Equal(admin.FontSize, admin.FontSizeMobile);
    }

    [Fact]
    public void Legacy_text_flags_are_the_default_but_place_setting_wins()
    {
        var settings = new StoreSettingDto { BrandTextInHeader = true, BrandTextInAuth = false };
        Assert.True(Resolve(BrandPlace.Header, settings).ShowText);
        Assert.True(Resolve(BrandPlace.MobileMenu, settings).ShowText);
        Assert.False(Resolve(BrandPlace.Auth, settings).ShowText);
        Assert.False(Resolve(BrandPlace.Header, settings, With(BrandPlace.Header, new BrandPlaceStyle { ShowText = false })).ShowText);
    }

    [Fact]
    public void Place_icon_and_texts_override_the_global_identity()
    {
        var settings = new StoreSettingDto { LogoUrl = "/uploads/media/logo.png", BrandTextPrimary = "فروشگاه", BrandTextAccent = "-" };
        var config = With(BrandPlace.Footer, new BrandPlaceStyle { IconUrl = "/uploads/media/footer.svg", Text2 = "من" });

        Assert.Equal("/uploads/media/logo.png", Resolve(BrandPlace.Header, settings).IconUrl);
        Assert.Null(Resolve(BrandPlace.Header, settings).Text2);
        var footer = Resolve(BrandPlace.Footer, settings, config);
        Assert.Equal("/uploads/media/footer.svg", footer.IconUrl);
        Assert.Equal("فروشگاه", footer.Text1);
        Assert.Equal("من", footer.Text2);
        Assert.False(footer.IsDefaultIcon);
    }

    // ── HTML لوگو ──────────────────────────────────────────────────

    [Fact]
    public void Lockup_html_encodes_texts_and_carries_css_variables()
    {
        var view = Resolve(BrandPlace.Auth) with { Text1 = "<b>x</b>", Tagline = "\"q\"", ShowTagline = true };
        var html = BrandLockupHtml.Render(view);

        Assert.DoesNotContain("<b>", html);
        Assert.Contains("&lt;b&gt;x&lt;/b&gt;", html);
        Assert.Contains("--bl-ih:44px", html);
        Assert.Contains("--bl-c2:" + BrandIdentity.Blue, html);
        Assert.StartsWith("<span class=\"bl bl-auth", html);
    }

    [Fact]
    public void Hidden_lockup_renders_nothing_except_in_the_studio_preview()
    {
        var hidden = Resolve(BrandPlace.Footer, config: With(BrandPlace.Footer, new BrandPlaceStyle { Visible = false }));

        Assert.False(hidden.Visible);
        Assert.Equal("", BrandLockupHtml.Render(hidden));
        var preview = BrandLockupHtml.Render(hidden, preview: true);
        Assert.Contains("data-bl-preview=\"Footer\"", preview);
        Assert.Contains(" hidden", preview);
    }

    [Fact]
    public void Email_lockup_uses_absolute_urls_and_inline_styles()
    {
        var html = BrandLockupHtml.RenderEmail(Resolve(BrandPlace.Email), "https://shop.example");

        Assert.Contains("src=\"https://shop.example" + BrandIdentity.MarkPng + "\"", html);
        Assert.DoesNotContain("var(--", html);
        Assert.DoesNotContain("class=", html);
        Assert.Equal("https://cdn.example/a.png", BrandLockupHtml.Absolute("https://cdn.example/a.png", "https://shop.example"));
    }

    // ── آیکون‌های اپ ───────────────────────────────────────────────

    [Fact]
    public void Pwa_defaults_include_brand_maskable_icons()
    {
        var pwa = PwaIconSet.From(new StoreSettingDto(), new BrandStyleConfig());
        var icons = JsonSerializer.Serialize(PwaEndpoints.Icons(pwa, url => url + "?v=1"));

        Assert.True(pwa.IsDefault);
        Assert.Contains(BrandIdentity.AppIcon512 + "?v=1", icons);
        Assert.Contains("\"purpose\":\"maskable\"", icons);
    }

    [Fact]
    public void Custom_pwa_icon_is_not_declared_maskable_unless_given()
    {
        var pwa = PwaIconSet.From(new StoreSettingDto { PwaIconUrl = "/uploads/media/app.png" }, new BrandStyleConfig());
        var icons = JsonSerializer.Serialize(PwaEndpoints.Icons(pwa, url => url));

        Assert.False(pwa.IsDefault);
        Assert.Equal("/uploads/media/app.png", pwa.Icon192);
        Assert.Equal("/uploads/media/app.png", pwa.AppleTouch);
        Assert.DoesNotContain("maskable", icons);

        var withMask = PwaIconSet.From(new StoreSettingDto { PwaIconUrl = "/uploads/media/app.png" },
            new BrandStyleConfig { PwaMaskableIconUrl = "/uploads/media/mask.png", PwaBackgroundColor = "#101010" });
        Assert.Equal("/uploads/media/mask.png", withMask.Maskable512);
        Assert.Equal("#101010", withMask.Background);
    }

    [Theory]
    [InlineData("/a.svg", "image/svg+xml")]
    [InlineData("/a.SVG?v=2", "image/svg+xml")]
    [InlineData("/a.webp", "image/webp")]
    [InlineData("/a.jpeg#x", "image/jpeg")]
    [InlineData("/a.png", "image/png")]
    [InlineData("/a", "image/png")]
    public void Mime_type_comes_from_the_extension(string url, string mime) =>
        Assert.Equal(mime, PwaEndpoints.MimeOf(url));

    // ── صفحهٔ تعمیرات ──────────────────────────────────────────────

    [Fact]
    public void Maintenance_page_shows_the_lockup_only_when_given()
    {
        var lockup = BrandLockupHtml.Render(Resolve(BrandPlace.Maintenance));

        Assert.Contains("bl-maintenance", MaintenanceRules.Page("فروشگاه", null, 30, lockup));
        Assert.DoesNotContain("class=\"brand\"", MaintenanceRules.Page("فروشگاه", null, 30));
        Assert.Contains("brand-lockup.css", MaintenanceRules.Page("<x>", null, 30));
        Assert.DoesNotContain("<x>", MaintenanceRules.Page("<x>", null, 30));
    }
}
