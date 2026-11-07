using System.Globalization;
using System.Text.Json.Nodes;
using Fratak_Shop.Core.Services.Design;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.Design;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Web.Infrastructure.Design;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های واحد دور ۲۱ برای «استودیوی طراحی» (بنر و کاور بدون هوش مصنوعی)، «صفحه‌ساز کمپین» و «رشد خودکار».
/// <list type="bullet">
/// <item>اندازه‌های پیش‌فرض هر جایگاه بنر و محدودیت اندازهٔ دلخواه (حافظهٔ هاست ضعیف).</item>
/// <item>قالب‌های آماده: JSON معتبر Fabric، هر شیء نقش دارد، متن داخل بوم می‌ماند، طرح خالی و شفاف.</item>
/// <item>ایده‌ساز: فقط از دادهٔ واقعی (کالای موجود با عکس)، بدون ادعای تخفیف جعلی، مناسبت‌های تقویمی.</item>
/// <item>امنیت صفحه‌ساز: CSS محدود به «.fs-lp» و بدون expression/javascript/@import؛ HTML بدون اسکریپت/فرم/رویداد؛ لینک بنر امن.</item>
/// <item>رندر سرور (SkiaSharp + HarfBuzz): PNG واقعی با متن فارسی ساخته می‌شود؛ مسیر تصویر فقط داخل wwwroot.</item>
/// <item>رشد خودکار: داوری بازدید و تعداد ایدهٔ بنر روز کم‌بازدید.</item>
/// </list>
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Design")]
public class DesignStudioRound21Tests
{
    private static readonly TimeSpan Tehran = TimeSpan.FromHours(3.5);

    /// <summary>تاریخ شمسی ← لحظه به وقت تهران. مثال: (1405, 9, 20) ← ظهر ۲۰ آذر ۱۴۰۵.</summary>
    private static DateTimeOffset Jalali(int year, int month, int day) =>
        new(new PersianCalendar().ToDateTime(year, month, day, 12, 0, 0, 0), Tehran);

    private static IdeaProduct Product(string name, decimal price, decimal final, int stock = 5, string? image = "/uploads/products/a.webp",
        int sales = 0, int views = 0, DateTimeOffset? created = null) =>
        new(Guid.NewGuid(), name, "p-" + Guid.NewGuid().ToString("N")[..8], image, 800, 800, price, final, stock, views, sales, "هدفون", "headphone",
            created ?? DateTimeOffset.UtcNow.AddDays(-90));

    // ───────────────────────────── اندازه‌ها ─────────────────────────────

    [Fact]
    public void Presets_cover_every_banner_position_with_the_right_size()
    {
        Assert.Equal((1920, 640), (DesignPresets.ForPosition(BannerPosition.HeroSlider).Width, DesignPresets.ForPosition(BannerPosition.HeroSlider).Height));
        Assert.Equal(DesignPresets.Wide, DesignPresets.ForPosition(BannerPosition.WideBanner).Key);
        Assert.Equal(DesignPresets.Promo, DesignPresets.ForPosition(BannerPosition.PromoCard).Key);
        Assert.Equal(DesignPresets.Hero, DesignPresets.ForPosition(BannerPosition.None).Key);
        Assert.True(DesignPresets.Find("PROMO")!.Transparent);
        Assert.Null(DesignPresets.Find("nope"));
        Assert.Null(DesignPresets.Find(null));
        Assert.Equal((1200, 630), (DesignPresets.Find(DesignPresets.BlogCover)!.Width, DesignPresets.Find(DesignPresets.BlogCover)!.Height));

        // اسلایدر اصلی نسخهٔ موبایل جدا دارد و آن نسخه هم تعریف شده است.
        var hero = DesignPresets.Find(DesignPresets.Hero)!;
        Assert.NotNull(DesignPresets.Find(hero.MobileKey));
        Assert.All(DesignPresets.All, p => Assert.True((long)p.Width * p.Height <= 8_000_000, p.Key));
        Assert.Equal(DesignPresets.All.Count, DesignPresets.All.Select(p => p.Key).Distinct().Count());
    }

    [Theory]
    [InlineData(5000, 100, 4000, 200)]
    [InlineData(100, 100, 200, 200)]
    [InlineData(1200, 800, 1200, 800)]
    [InlineData(4000, 4000, 4000, 2000)]
    [InlineData(-5, int.MaxValue, 200, 4000)]
    public void Custom_size_is_clamped_to_a_safe_area(int width, int height, int expectedWidth, int expectedHeight)
    {
        var (w, h) = DesignPresets.ClampSize(width, height);
        Assert.Equal(expectedWidth, w);
        Assert.Equal(expectedHeight, h);
        Assert.True((long)w * h <= 8_000_000);
    }

    // ───────────────────────────── قالب‌ها ─────────────────────────────

    public static TheoryData<string, string> LayoutsAndPresets()
    {
        var data = new TheoryData<string, string>();
        foreach (var (layout, _) in DesignTemplates.Layouts)
            foreach (var preset in new[] { DesignPresets.Hero, DesignPresets.HeroMobile, DesignPresets.Wide, DesignPresets.Promo, DesignPresets.BlogCover, DesignPresets.Story })
                data.Add(layout, preset);
        return data;
    }

    [Theory]
    [MemberData(nameof(LayoutsAndPresets))]
    public void Every_layout_builds_valid_fabric_json_with_text_inside_the_canvas(string layout, string presetKey)
    {
        var preset = DesignPresets.Find(presetKey)!;
        var spec = new DesignSpec(layout, "sunset", "حراج بزرگ پاییزه روی هدفون‌های بی‌سیم", "تخفیف واقعی روی کالاهای منتخب", "۲۵٪ تخفیف", "خرید کنید",
            new DesignImage("/uploads/products/a.webp", 800, 600), "۱٬۲۰۰٬۰۰۰ تومان", "۱٬۶۰۰٬۰۰۰ تومان", "پیشنهاد ویژه");

        var root = JsonNode.Parse(DesignTemplates.Build(spec, preset.Width, preset.Height, preset.Transparent))!.AsObject();
        Assert.Equal(DesignTemplates.FabricVersion, (string?)root["version"]);
        Assert.Equal(preset.Width, (int)root["fsWidth"]!);
        Assert.Equal(preset.Height, (int)root["fsHeight"]!);
        var objects = root["objects"]!.AsArray().Select(o => o!.AsObject()).ToList();
        Assert.NotEmpty(objects);

        foreach (var o in objects)
        {
            // fabric 7 پیش‌فرض origin را «center» گذاشته؛ قالب باید صریحاً left/top بدهد وگرنه همه چیز جابه‌جا می‌شود.
            Assert.Equal("left", (string?)o["originX"]);
            Assert.Equal("top", (string?)o["originY"]);
            Assert.False(string.IsNullOrWhiteSpace((string?)o["fsRole"]));
        }

        if (preset.Transparent)
        {
            // کارت تبلیغاتی: فقط عکس شفاف کالا (+ نشان)؛ متن را خود کارت سایت می‌نویسد.
            Assert.Contains(objects, o => (string?)o["fsRole"] == "image");
            Assert.DoesNotContain(objects, o => (string?)o["fsRole"] == "headline");
        }
        else
        {
            var headline = Assert.Single(objects, o => (string?)o["fsRole"] == "headline");
            Assert.Equal("Textbox", (string?)headline["type"]);
            Assert.Equal(DesignTemplates.Font, (string?)headline["fontFamily"]);
            Assert.Contains("حراج بزرگ", (string?)headline["text"]);
        }

        foreach (var text in objects.Where(o => (string?)o["type"] == "Textbox"))
        {
            var left = (double)text["left"]!;
            var top = (double)text["top"]!;
            var width = (double)text["width"]!;
            Assert.True(left >= -0.5 && left + width <= preset.Width + 0.5, $"{layout}/{presetKey}: «{text["text"]}» افقی بیرون زده ({left} + {width}).");
            Assert.True(top >= -0.5 && top < preset.Height, $"{layout}/{presetKey}: «{text["text"]}» عمودی بیرون زده ({top}).");
            Assert.True((double)text["fontSize"]! >= 10);
        }

        if (preset.Transparent)
            Assert.Equal("", (string?)root["background"]);
        else
            Assert.Contains(objects, o => (string?)o["fsRole"] == "bg");
    }

    [Fact]
    public void Blank_design_is_white_or_fully_transparent()
    {
        var white = JsonNode.Parse(DesignTemplates.Blank(1200, 800))!;
        var bg = Assert.Single(white["objects"]!.AsArray());
        Assert.Equal("bg", (string?)bg!["fsRole"]);
        Assert.False((bool)bg["selectable"]!);
        Assert.Equal("#ffffff", (string?)white["background"]);

        var clear = JsonNode.Parse(DesignTemplates.Blank(800, 600, transparent: true))!;
        Assert.Empty(clear["objects"]!.AsArray());
        Assert.Equal("", (string?)clear["background"]);
    }

    [Fact]
    public void Text_helpers_use_persian_digits_and_cut_on_word_boundaries()
    {
        Assert.Equal("۲۵٪", DesignTemplates.FaDigits("25%"));
        Assert.Equal("۱۸٬۹۰۰٬۰۰۰ تومان", DesignTemplates.Toman(18_900_000m));
        Assert.Equal("۱٬۰۰۰ تومان", DesignTemplates.Toman(999.6m));

        var name = "گوشی موبایل سامسونگ مدل Galaxy A55 5G ظرفیت ۲۵۶ گیگابایت رم ۸ گیگابایت";
        var shortName = DesignTemplates.Shorten(name, 40);
        Assert.True(shortName.Length <= 40);
        Assert.StartsWith(shortName, name);
        Assert.False(shortName.EndsWith(" مدل", StringComparison.Ordinal) || shortName.EndsWith(" ظرفیت", StringComparison.Ordinal));
        Assert.Equal("هدفون", DesignTemplates.Shorten("  هدفون  ", 40));

        // متن کوتاه همان قلم بزرگ را می‌گیرد؛ متن بلند کوچک می‌شود ولی از حداقل پایین‌تر نمی‌رود.
        Assert.Equal(80, DesignTemplates.Fit("حراج یلدا", 900, 80, 30, 2).FontSize);
        var (fs, lines) = DesignTemplates.Fit(name + " " + name, 500, 80, 30, 2);
        Assert.InRange(fs, 30, 79);
        Assert.InRange(lines, 1, 4);
    }

    [Fact]
    public void Sample_specs_fall_back_safely_and_support_with_expressions()
    {
        var unknown = DesignStudio.SampleSpec("hack<script>", "nope", DesignKind.Banner);
        Assert.Equal("center", unknown.Layout);
        Assert.Equal("ocean", unknown.Palette);
        Assert.Equal("sunset", DesignStudio.SampleSpec("sale", null, DesignKind.Banner).Palette);
        Assert.Equal("night", DesignStudio.SampleSpec("sale", "night", DesignKind.Banner).Palette);
        Assert.Equal("عنوان مقاله", DesignStudio.SampleSpec("center", null, DesignKind.BlogCover).Headline);
        Assert.Equal("تیتر من", (DesignStudio.SampleSpec("split", null, DesignKind.Banner) with { Headline = "تیتر من" }).Headline);

        Assert.Equal("/uploads/banners/a.webp", DesignStudio.Url(DesignKind.Banner, "a.webp"));
        Assert.Equal("/uploads/blogs/a.webp", DesignStudio.Url(DesignKind.BlogCover, "a.webp"));
        Assert.Equal(UploadArea.Content, DesignStudio.AreaFor(DesignKind.Social));
        Assert.Null(DesignStudio.Url(DesignKind.Free, " "));
    }

    // ───────────────────────────── ایده‌ساز ─────────────────────────────

    [Fact]
    public void Ideas_need_real_data_empty_shop_gets_nothing()
    {
        Assert.Empty(DesignIdeas.Generate(new IdeaInput([], [], "فراتک"), Jalali(1405, 8, 1)));

        // ناموجود، بدون عکس یا بی‌قیمت ← هیچ ایده‌ای.
        var junk = new[]
        {
            Product("ناموجود", 100, 50, stock: 0, sales: 10),
            Product("بی‌عکس", 100, 50, image: null, sales: 10),
            Product("بی‌قیمت", 0, 0, sales: 10),
        };
        Assert.Empty(DesignIdeas.Generate(new IdeaInput(junk, [], "فراتک"), Jalali(1405, 8, 1)));
    }

    [Fact]
    public void Best_real_discount_becomes_a_deal_idea_with_honest_numbers()
    {
        var now = Jalali(1405, 8, 1); // بدون مناسبت
        var deal = Product("هدفون بی‌سیم", 1_000_000, 750_000, stock: 4, views: 30);
        var small = Product("کابل", 100_000, 95_000, stock: 50, views: 900); // ۵٪ ← تخفیف «ویژه» حساب نمی‌شود
        var ideas = DesignIdeas.Generate(new IdeaInput([deal, small], [], "فراتک"), now);

        var idea = Assert.Single(ideas, i => i.Key.StartsWith("deal:", StringComparison.Ordinal));
        Assert.Equal(deal.Id, idea.ProductId);
        Assert.Equal("/product/" + deal.Slug, idea.LinkUrl);
        Assert.Equal("۲۵٪ تخفیف", idea.Spec.Badge);
        Assert.Equal("۷۵۰٬۰۰۰ تومان", idea.Spec.Price);
        Assert.Equal("۱٬۰۰۰٬۰۰۰ تومان", idea.Spec.OldPrice);
        Assert.Equal(now.AddDays(7), idea.ExpiresAt);
        Assert.DoesNotContain(ideas, i => i.ProductId == small.Id && i.Key.StartsWith("deal:", StringComparison.Ordinal));
        Assert.Equal(ideas.Count, ideas.Select(i => i.Key).Distinct().Count());
    }

    [Fact]
    public void Ideas_cover_best_seller_new_arrival_and_category_and_respect_max()
    {
        var now = Jalali(1405, 8, 1);
        var seller = Product("ساعت هوشمند", 2_000_000, 2_000_000, sales: 40);
        var fresh = Product("اسپیکر تازه", 900_000, 900_000, created: now.AddDays(-3));
        var category = new IdeaCategory(Guid.NewGuid(), "هدفون", "headphone", 12, seller);
        var ideas = DesignIdeas.Generate(new IdeaInput([seller, fresh], [category], "فراتک"), now);

        Assert.Contains(ideas, i => i.Key.StartsWith("best:", StringComparison.Ordinal) && i.ProductId == seller.Id && i.Spec.OldPrice is null);
        Assert.Contains(ideas, i => i.Key.StartsWith("new:", StringComparison.Ordinal) && i.ProductId == fresh.Id);
        Assert.Contains(ideas, i => i.Key.StartsWith("cat:", StringComparison.Ordinal) && i.LinkUrl == "/category/headphone");
        Assert.Single(DesignIdeas.Generate(new IdeaInput([seller, fresh], [category], "فراتک"), now, max: 1));
        Assert.Empty(DesignIdeas.Generate(new IdeaInput([seller, fresh], [category], "فراتک"), now, max: -3));
    }

    [Fact]
    public void Occasions_follow_the_iranian_calendar_and_never_claim_fake_discounts()
    {
        Assert.Equal("yalda", DesignIdeas.Occasion(Jalali(1405, 9, 20))!.Key);
        Assert.Equal("nowruz", DesignIdeas.Occasion(Jalali(1405, 12, 10))!.Key);
        Assert.Equal("nowruz", DesignIdeas.Occasion(Jalali(1405, 1, 5))!.Key);
        Assert.Equal("school", DesignIdeas.Occasion(Jalali(1405, 6, 20))!.Key);
        Assert.Null(DesignIdeas.Occasion(Jalali(1405, 8, 1)));
        Assert.Null(DesignIdeas.Occasion(Jalali(1405, 3, 10)));

        // یلدا بدون تخفیف ≥۱۰٪ ← ایدهٔ مناسبت بدون نشان تخفیف.
        var now = Jalali(1405, 9, 20);
        var noDeal = DesignIdeas.Generate(new IdeaInput([Product("لپ‌تاپ", 30_000_000, 30_000_000, sales: 3)], [], "فراتک"), now);
        var occasion = noDeal[0];
        Assert.StartsWith("occasion:yalda:", occasion.Key, StringComparison.Ordinal);
        Assert.Null(occasion.Spec.Badge);
        Assert.Equal("festive", occasion.Spec.Layout);
        Assert.Equal(DesignIdeas.Occasion(now)!.EndsAt, occasion.ExpiresAt);

        var withDeal = DesignIdeas.Generate(new IdeaInput([Product("هدفون", 1_000_000, 600_000, stock: 3)], [], "فراتک"), now);
        Assert.Equal("تا ۴۰٪ تخفیف", withDeal[0].Spec.Badge);
    }

    [Fact]
    public void Idea_keys_are_stable_per_week_and_month()
    {
        Assert.Equal(DesignIdeas.MonthKey(Jalali(1405, 8, 1)), DesignIdeas.MonthKey(Jalali(1405, 8, 29)));
        Assert.NotEqual(DesignIdeas.MonthKey(Jalali(1405, 8, 1)), DesignIdeas.MonthKey(Jalali(1405, 9, 1)));
        Assert.Equal("1405-08", DesignIdeas.MonthKey(Jalali(1405, 8, 15)));
        Assert.Equal(DesignIdeas.WeekKey(Jalali(1405, 1, 1)), DesignIdeas.WeekKey(Jalali(1405, 1, 7)));
        Assert.NotEqual(DesignIdeas.WeekKey(Jalali(1405, 1, 1)), DesignIdeas.WeekKey(Jalali(1405, 1, 8)));
    }

    // ───────────────────────────── امنیت لینک و صفحه‌ساز ─────────────────────────────

    [Theory]
    [InlineData("/product/a", "/product/a")]
    [InlineData("  /products?Discounted=true  ", "/products?Discounted=true")]
    [InlineData("https://shop.fratak.ir/x", "https://shop.fratak.ir/x")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("//evil.com/x", null)]
    [InlineData("/\\evil.com", null)]
    [InlineData("http://insecure.ir", null)]
    [InlineData("data:text/html,<script>", null)]
    [InlineData("", null)]
    public void Banner_links_are_internal_or_https_only(string input, string? expected) =>
        Assert.Equal(expected, DesignService.SafeLink(input));

    [Fact]
    public void Banner_link_longer_than_500_is_rejected() =>
        Assert.Null(DesignService.SafeLink("/" + new string('a', 600)));

    [Fact]
    public void Landing_css_is_scoped_to_the_page()
    {
        var css = LandingCss.Sanitize("* { box-sizing: border-box; } body {margin: 0;} #i3k .t{color:red} html body .x{padding:4px} :root{--a:1}");
        Assert.Contains(".fs-lp *{box-sizing: border-box;}", css);
        Assert.Contains(".fs-lp{margin: 0;}", css);
        Assert.Contains(".fs-lp #i3k .t{color:red}", css);
        Assert.Contains(".fs-lp .x{padding:4px}", css);
        Assert.DoesNotContain("body", css);
        Assert.Equal(".fs-lp, .fs-lp .a:hover", LandingCss.ScopeSelectors("body, .a:hover"));
        Assert.Equal(".fs-lp .x", LandingCss.ScopeSelectors("html body .x"));
        Assert.Equal(".fs-lp a[href^=\"/p,q\"]", LandingCss.ScopeSelectors("a[href^=\"/p,q\"]"));
        Assert.Equal("", LandingCss.Sanitize(null));
        Assert.Equal("", LandingCss.Sanitize("   "));
    }

    [Fact]
    public void Landing_css_keeps_responsive_media_queries_and_animations()
    {
        var css = LandingCss.Sanitize("@media (max-width: 480px){#a{font-size:20px} body{padding:0}} @keyframes pulse{0%{opacity:0}100%{opacity:1}} .b{animation:pulse 1s}");
        Assert.Contains("@media (max-width: 480px){.fs-lp #a{font-size:20px}", css);
        Assert.Contains(".fs-lp{padding:0}", css);
        Assert.Contains("@keyframes pulse{", css);
        Assert.Contains(".fs-lp .b{animation:pulse 1s}", css);
    }

    [Fact]
    public void Landing_css_attacks_are_removed()
    {
        var css = LandingCss.Sanitize(
            "@import url(https://evil.com/x.css); @charset \"utf-8\"; @font-face{font-family:x;src:url(https://evil.com/f.woff)}" +
            ".a{background:url(javascript:alert(1))} .b{width:expression(alert(1))} .c{background:url(http://evil.com/x.png)}" +
            ".d{background:url('/uploads/a.webp')} .e{-moz-binding:url(x.xml#xss)} .f{behavior:url(x.htc)} .g{color:\\72 ed}" +
            ".h{background:url(//evil.com/a.png)} .i{background:url(data:image/png;base64,iVBORw0KGgo=)} </style><script>alert(1)</script>");
        Assert.DoesNotContain("@import", css);
        Assert.DoesNotContain("@font-face", css);
        Assert.DoesNotContain("@charset", css);
        Assert.DoesNotContain("javascript", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expression", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("binding", css);
        Assert.DoesNotContain("behavior", css);
        Assert.DoesNotContain("evil.com", css);
        Assert.DoesNotContain("<", css);
        Assert.DoesNotContain("\\", css);
        Assert.Contains(".fs-lp .c{background:none}", css);
        Assert.Contains(".fs-lp .d{background:url(\"/uploads/a.webp\")}", css);
        Assert.Contains("url(\"data:image/png;base64,", css);
    }

    [Fact]
    public void Landing_css_cannot_escape_with_unbalanced_braces_or_huge_input()
    {
        var css = LandingCss.Sanitize(".a{color:red}} } body{x:1} .b{color:blue");
        Assert.Contains(".fs-lp .a{color:red}", css);
        Assert.DoesNotContain("}}", css);
        foreach (var rule in css.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Assert.StartsWith(".fs-lp", rule);

        var huge = LandingCss.Sanitize(string.Concat(Enumerable.Repeat(".a{color:red}", 40_000)));
        Assert.True(huge.Length < 400_000);
    }

    [Fact]
    public void Landing_html_drops_scripts_forms_events_and_body_wrapper_but_keeps_builder_markup()
    {
        var html = LandingPageService.CleanHtml(
            "<body id=\"wrapper\"><h1 onclick=\"alert(1)\">حراج</h1><script>alert(1)</script><form action=\"/x\"><input name=\"p\"><button>ورود</button></form>" +
            "<section id=\"i3k\" class=\"fs-lp-section\"><div class=\"fs-lp-products\" data-fs-code=\"deals:8\">[[deals:8]]</div></section>" +
            "<details class=\"fs-lp-faq\"><summary>ارسال؟</summary><p>رایگان</p></details><a href=\"javascript:alert(1)\">x</a>" +
            "<iframe src=\"https://evil.com\"></iframe><img src=\"/uploads/content/a.webp\" onerror=\"alert(1)\" alt=\"عکس\"></body>");
        Assert.DoesNotContain("<body", html);
        Assert.DoesNotContain("onclick", html);
        Assert.DoesNotContain("onerror", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<form", html);
        Assert.DoesNotContain("<input", html);
        Assert.DoesNotContain("<button", html);
        Assert.DoesNotContain("<iframe", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.Contains("<h1>حراج</h1>", html);
        Assert.Contains("id=\"i3k\"", html);
        Assert.Contains("class=\"fs-lp-section\"", html);
        Assert.Contains("data-fs-code=\"deals:8\"", html);
        Assert.Contains("[[deals:8]]", html);
        Assert.Contains("<details class=\"fs-lp-faq\"><summary>ارسال؟</summary>", html);
        Assert.Contains("src=\"/uploads/content/a.webp\"", html);
        Assert.Equal("", LandingPageService.CleanHtml(null));
    }

    [Theory]
    [InlineData("i3k9", true)]
    [InlineData("ixp2-2", true)]
    [InlineData("fs-faq", true)]
    [InlineData("FratakModal", false)]   // متغیر سراسری اسکریپت سایت (DOM clobbering)
    [InlineData("csrf-token", false)]
    [InlineData("header", false)]
    [InlineData("i", false)]
    public void Landing_html_keeps_only_builder_style_ids(string id, bool kept)
    {
        var html = LandingPageService.CleanHtml($"<div id=\"{id}\" class=\"fs-lp-section\">x</div>");
        Assert.Equal(kept, html.Contains($"id=\"{id}\"", StringComparison.Ordinal));
        Assert.Contains("class=\"fs-lp-section\"", html);
    }

    [Fact]
    public void Every_landing_template_survives_the_sanitizer_and_has_honest_countdowns()
    {
        var end = new DateTimeOffset(2026, 11, 30, 23, 59, 0, Tehran);
        foreach (var (key, title, description) in LandingTemplates.List)
        {
            Assert.False(string.IsNullOrWhiteSpace(title));
            Assert.False(string.IsNullOrWhiteSpace(description));
            var built = LandingTemplates.Build(key, "فراتک شاپ", end);
            Assert.Equal(key, built.Key);
            var clean = LandingPageService.CleanHtml(built.Html);
            Assert.Contains("fs-lp-", clean);
            // پاک‌ساز چیزی از قالب خودمان نمی‌اندازد (کدهای کوتاه کالا هم سالم می‌مانند).
            foreach (var code in new[] { "[[deals:8]]", "[[newest:8]]", "[[bestsellers:8]]" })
                Assert.Equal(built.Html.Contains(code, StringComparison.Ordinal), clean.Contains(code, StringComparison.Ordinal));
            foreach (var rule in LandingCss.Sanitize(built.Css).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                Assert.True(rule.StartsWith(".fs-lp", StringComparison.Ordinal) || rule.StartsWith('@'), rule);

            // بدون تاریخ پایان ← شمارش معکوس ساخته نمی‌شود (فوریت جعلی ممنوع).
            Assert.DoesNotContain("data-fs-countdown", LandingTemplates.Build(key, "فراتک شاپ", null).Html);
        }

        var campaign = LandingTemplates.Build("campaign", "فراتک شاپ", end);
        Assert.Contains("data-fs-countdown=\"2026-11-30T23:59:00+03:30\"", campaign.Html);
        Assert.Contains("[[deals:8]]", campaign.Html);
        Assert.Contains("فراتک شاپ", campaign.Html);
        Assert.Equal("blank", LandingTemplates.Build("unknown", "x", null).Key);
    }

    [Fact]
    public void Landing_template_encodes_the_site_name()
    {
        var built = LandingTemplates.Build("campaign", "<img src=x onerror=alert(1)>", null);
        Assert.DoesNotContain("<img src=x", built.Html);
        Assert.Contains("&lt;img", built.Html);
        Assert.Contains("فروشگاه", LandingTemplates.Build("campaign", "  ", null).Html);
    }

    // ───────────────────────────── رندر سرور ─────────────────────────────

    private sealed class Env(string webRoot) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Fratak-Shop.Tests";
        public string WebRootPath { get; set; } = webRoot;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = webRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public void Server_renderer_draws_a_real_png_with_persian_text()
    {
        // قلم‌های Vazirmatn کنار برنامه کپی می‌شوند (Assets/Fonts)؛ اگر نباشند تأیید ایده‌ها روی سرور تصویر نمی‌سازد.
        Assert.True(DesignRenderer.IsAvailable, "Vazirmatn fonts or HarfBuzz native library not found next to the app.");
        var renderer = new DesignRenderer(new Env(Path.GetTempPath()), NullLogger<DesignRenderer>.Instance);
        var spec = new DesignSpec("sale", "night", "حراج بزرگ ۲۵٪ Galaxy S24", "تخفیف واقعی", "۲۵٪ تخفیف", "خرید کنید");
        var png = renderer.RenderPng(DesignTemplates.Build(spec, 800, 400), 800, 400);

        Assert.NotNull(png);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png![..4]);
        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(800, bitmap.Width);
        Assert.Equal(400, bitmap.Height);
        // پس‌زمینه کشیده شده (شفاف نیست) و متن/نشان رنگ‌های دیگری روی آن گذاشته‌اند.
        Assert.Equal(255, bitmap.GetPixel(5, 5).Alpha);
        var colors = new HashSet<SKColor>();
        for (var x = 0; x < 800; x += 7)
            for (var y = 0; y < 400; y += 7)
                colors.Add(bitmap.GetPixel(x, y));
        Assert.True(colors.Count > 30, $"only {colors.Count} colours – text was probably not drawn");
    }

    [Fact]
    public void Server_renderer_rejects_bad_input_without_throwing()
    {
        var renderer = new DesignRenderer(new Env(Path.GetTempPath()), NullLogger<DesignRenderer>.Instance);
        Assert.Null(renderer.RenderPng("", 800, 400));
        Assert.Null(renderer.RenderPng("{not json", 800, 400));
        Assert.Null(renderer.RenderPng(DesignTemplates.Blank(100, 100), 4000, 4000)); // بیش از ۸ مگاپیکسل
        Assert.Null(renderer.RenderPng(DesignTemplates.Blank(100, 100), 8, 8));
        var deep = string.Concat(Enumerable.Repeat("{\"objects\":[", 60)) + string.Concat(Enumerable.Repeat("]}", 60));
        Assert.Null(renderer.RenderPng(deep, 200, 200));
    }

    [Theory]
    [InlineData("#fff", 255, 255, 255, 255)]
    [InlineData("#FF000080", 255, 0, 0, 128)]
    [InlineData("rgb(0, 128, 255)", 0, 128, 255, 255)]
    [InlineData("rgba(255,0,0,0.5)", 255, 0, 0, 128)]
    [InlineData("white", 255, 255, 255, 255)]
    public void Colors_are_parsed_like_css(string input, byte r, byte g, byte b, byte a)
    {
        var color = DesignRenderer.ParseColor(input);
        Assert.NotNull(color);
        Assert.Equal(r, color!.Value.Red);
        Assert.Equal(g, color.Value.Green);
        Assert.Equal(b, color.Value.Blue);
        Assert.InRange(color.Value.Alpha, a - 1, a + 1);
    }

    [Theory]
    [InlineData("transparent")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("url(x)")]
    [InlineData("#zzz")]
    public void Invalid_colors_are_ignored(string? input) => Assert.Null(DesignRenderer.ParseColor(input));

    [Fact]
    public void Renderer_reads_only_local_site_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "fs-wwwroot");
        var expected = Path.Combine(root, "uploads", "products", "a.webp");
        Assert.Equal(expected, DesignRenderer.LocalImagePath("/uploads/products/a.webp?v=2", root));
        Assert.Equal(expected, DesignRenderer.LocalImagePath("https://shop.fratak.ir/uploads/products/a.webp", root));
        Assert.NotNull(DesignRenderer.LocalImagePath("/assets/image/logo.png", root));
        Assert.Null(DesignRenderer.LocalImagePath("/uploads/../appsettings.json", root));
        Assert.Null(DesignRenderer.LocalImagePath("/uploads/%2e%2e/appsettings.json", root));
        Assert.Null(DesignRenderer.LocalImagePath("/etc/passwd", root));
        Assert.Null(DesignRenderer.LocalImagePath("//evil.com/uploads/a.webp", root));
        Assert.Null(DesignRenderer.LocalImagePath("file:///etc/passwd", root));
        Assert.Null(DesignRenderer.LocalImagePath("data:image/png;base64,AAAA", root));
        Assert.Null(DesignRenderer.LocalImagePath("/uploads/a\\..\\..\\x", root));
        Assert.Null(DesignRenderer.LocalImagePath(null, root));
    }

    [Fact]
    public void Mixed_persian_and_latin_text_is_split_into_direction_runs()
    {
        var text = "تخفیف ۲۵٪ Galaxy S24";
        var runs = DesignRenderer.BidiSegments(text, baseRtl: true);
        Assert.Equal(text, string.Concat(runs.Select(r => r.Text)));
        Assert.Contains(runs, r => r.Text.Contains("Galaxy S24", StringComparison.Ordinal) && !r.Rtl);
        Assert.Contains(runs, r => r.Text.Contains("تخفیف", StringComparison.Ordinal) && r.Rtl);
        Assert.Contains(runs, r => r.Text.Contains("۲۵٪", StringComparison.Ordinal) && !r.Rtl);
        Assert.Single(DesignRenderer.BidiSegments("حراج", true));
    }

    // ───────────────────────────── رشد خودکار ─────────────────────────────

    [Theory]
    [InlineData(120, new[] { 200, 210, 190, 205 }, true)]   // افت ۴۰٪
    [InlineData(180, new[] { 200, 210, 190, 205 }, false)]  // افت کم ← عادی
    [InlineData(10, new int[0], true)]                      // سایت تازه
    [InlineData(25, new[] { 5, 8 }, false)]                 // دادهٔ کم، ولی ≥۲۰
    [InlineData(15, new[] { 12, 10, 14 }, true)]            // کم‌بازدید همیشگی
    [InlineData(-4, new[] { 100, 100, 100 }, true)]         // ورودی منفی ← صفر
    public void Traffic_verdict_compares_yesterday_with_the_median(int yesterday, int[] previous, bool low)
    {
        var verdict = GrowthAutopilot.Judge(yesterday, previous);
        Assert.Equal(low, verdict.Low);
        Assert.True(verdict.Visitors >= 0);
        Assert.False(string.IsNullOrWhiteSpace(verdict.Reason));
    }

    [Fact]
    public void Low_traffic_days_get_more_banner_ideas_but_never_more_than_six()
    {
        var normal = new TrafficVerdict(false, 200, 200, "");
        var low = new TrafficVerdict(true, 50, 200, "");
        Assert.Equal(3, new GrowthReport(true, normal, 0, 0, 0, false, "").BannerIdeas(3));
        Assert.Equal(6, new GrowthReport(true, low, 0, 0, 0, false, "").BannerIdeas(3));
        Assert.Equal(6, new GrowthReport(true, low, 0, 0, 0, false, "").BannerIdeas(5));
        Assert.Equal(0, new GrowthReport(false, low, 0, 0, 0, false, "").BannerIdeas(3));
        Assert.Equal(0, new GrowthReport(true, low, 0, 0, 0, false, "").BannerIdeas(0));
    }

    [Fact]
    public void Banner_ideas_setting_is_clamped()
    {
        Assert.Equal(6, new SmartOptions { BannerIdeasPerDay = 50 }.Normalize().BannerIdeasPerDay);
        Assert.Equal(0, new SmartOptions { BannerIdeasPerDay = -1 }.Normalize().BannerIdeasPerDay);
        Assert.True(new SmartOptions().GrowthAutopilot);
    }
}
