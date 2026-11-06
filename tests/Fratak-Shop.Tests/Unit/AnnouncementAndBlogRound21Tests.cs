using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.Smart;
using Fratak_Shop.Web.Infrastructure;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های دور ۲۱ برای «نوار اطلاعیهٔ بالای سایت» (<see cref="AnnouncementLook"/>) و تغییرهای بلاگ هوشمند
/// (فهرست «۵ پیشنهاد خرید» بدون ادعای «بهترین»، تنوع برند، فهرست مطالب و عنوان دلخواه).
/// <para>اصل امنیتی نوار: هیچ مقدار دلخواهی از تنظیمات مستقیم وارد CSS نمی‌شود (رنگ فقط هگز، کلاس فقط از فهرست ثابت).</para>
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Smart")]
public class AnnouncementAndBlogRound21Tests
{
    // ── نوار اطلاعیه ─────────────────────────────────────────────────

    [Fact]
    public void Default_look_matches_the_old_bar()
    {
        var look = AnnouncementLook.Build("#0B1927", null, null, 0, 0, 0, 0, "ارسال رایگان", null, false);
        Assert.Equal("#0B1927", look.Background);
        Assert.Equal("#ffffff", look.TextColor);
        Assert.Equal("h-9", look.HeightClass);
        Assert.Contains("fs-ann-solid", look.CssClass);
        Assert.Contains("text-xs", look.CssClass);
        Assert.Equal(new[] { "ارسال رایگان" }, look.Messages);
        Assert.False(look.Closeable);
    }

    [Theory]
    [InlineData(1, "fs-ann-gradient", "linear-gradient")]
    [InlineData(2, "fs-ann-festive", "linear-gradient")]
    [InlineData(3, "fs-ann-urgent", "#b91c1c")]
    [InlineData(4, "fs-ann-gold", "#c9a227")]
    public void Every_style_has_its_class_and_background(int style, string css, string background)
    {
        var look = AnnouncementLook.Build("#123456", "#abcdef", null, style, 0, 0, 0, "x", null, false);
        Assert.Contains(css, look.CssClass);
        Assert.Contains(background, look.Background);
    }

    [Theory]
    [InlineData(1, "fs-ann-blink")]
    [InlineData(2, "fs-ann-pulse")]
    [InlineData(3, "fs-ann-shimmer")]
    [InlineData(4, "fs-ann-marquee")]
    public void Effects_add_only_known_classes(int effect, string css)
    {
        Assert.Contains(css, AnnouncementLook.Build(null, null, null, 0, effect, 0, 0, "x", null, false).CssClass);
    }

    [Fact]
    public void Large_bold_text_gets_a_taller_bar()
    {
        var look = AnnouncementLook.Build(null, null, "#111111", 0, 0, 2, 2, "x", null, true);
        Assert.Equal("h-11", look.HeightClass);
        Assert.Contains("text-base", look.CssClass);
        Assert.Contains("font-black", look.CssClass);
        Assert.Equal("#111111", look.TextColor);
        Assert.True(look.Closeable);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#fff;background:url(https://evil.example/x.png)")]
    [InlineData("expression(alert(1))")]
    [InlineData("#12345")]
    [InlineData("</style><script>alert(1)</script>")]
    public void Hostile_colours_fall_back_to_safe_defaults(string colour)
    {
        var look = AnnouncementLook.Build(colour, colour, colour, 1, 0, 0, 0, "x", null, false);
        Assert.DoesNotContain("url(", look.Background, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<", look.Background, StringComparison.Ordinal);
        Assert.DoesNotContain(";", look.Background, StringComparison.Ordinal);
        Assert.Equal("#ffffff", look.TextColor);
        Assert.Null(AnnouncementLook.SafeColor(colour));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    [InlineData(int.MaxValue)]
    public void Out_of_range_options_never_produce_unknown_classes(int value)
    {
        var look = AnnouncementLook.Build("#000000", null, null, value, value, value, value, "x", null, false);
        Assert.Equal("fs-ann fs-ann-solid text-xs md:text-sm", look.CssClass);
        Assert.Equal("h-9", look.HeightClass);
    }

    [Fact]
    public void Messages_are_trimmed_deduplicated_and_capped()
    {
        var extra = string.Join("\n", new[] { "  تخفیف ۱۰٪  ", "", "ارسال رایگان", new string('ا', 300), "۳", "۴", "۵", "۶", "۷" });
        var messages = AnnouncementLook.ParseMessages("ارسال رایگان", extra);
        Assert.Equal(AnnouncementLook.MaxMessages, messages.Count);
        Assert.Equal("ارسال رایگان", messages[0]);
        Assert.Equal("تخفیف ۱۰٪", messages[1]);
        Assert.Equal(200, messages[2].Length);
        Assert.Empty(AnnouncementLook.ParseMessages(null, null));
        Assert.Empty(AnnouncementLook.ParseMessages("   ", "\n\n"));
    }

    [Theory]
    [InlineData("#0B1927", "#3d4b59")]
    [InlineData("#fff", "#ffffff")]
    [InlineData("#000000", "#323232")]
    public void Gradient_second_colour_defaults_to_a_lighter_shade(string input, string expected)
    {
        Assert.Equal(expected, AnnouncementLook.Shift(input));
    }

    [Fact]
    public void Option_lists_are_complete_for_the_settings_page()
    {
        Assert.Equal(5, AnnouncementLook.Styles.Length);
        Assert.Equal(5, AnnouncementLook.Effects.Length);
        Assert.Equal(3, AnnouncementLook.Sizes.Length);
        Assert.Equal(3, AnnouncementLook.Weights.Length);
        Assert.Equal(Enumerable.Range(0, 5), AnnouncementLook.Styles.Select(s => s.Value));
    }

    // ── بلاگ ─────────────────────────────────────────────────────────

    private static readonly DateTimeOffset Now = new(2026, 11, 6, 8, 0, 0, TimeSpan.Zero);

    private static BlogProduct Product(int i, string brand, decimal price, double rating = 4.5, int reviews = 20, int sales = 10) => new(
        Guid.NewGuid(), $"هدفون بی سیم {brand} مدل H{i}", $"h-{i}", brand, price, true, rating, reviews, sales,
        [new SeoFeature("عمر باتری", $"{20 + i} ساعت"), new SeoFeature("نسخه بلوتوث", "5.3"), new SeoFeature("حذف نویز", "دارد")],
        ["صدای شفاف", "باتری خوب"], ["جعبهٔ ساده"], ["ورزشکاران"]);

    [Fact]
    public void Pick_list_is_diverse_and_keeps_a_budget_option()
    {
        var products = new List<BlogProduct>
        {
            Product(1, "سونی", 9_000_000, 4.9, 80, 90), Product(2, "سونی", 8_500_000, 4.8, 70, 80), Product(3, "سونی", 8_000_000, 4.8, 60, 70),
            Product(4, "سونی", 7_000_000, 4.7, 50, 60), Product(5, "انکر", 3_000_000, 4.6, 40, 50), Product(6, "شیائومی", 2_000_000, 4.2, 30, 40),
            Product(7, "هایلو", 900_000, 4.0, 10, 5),
        };
        var picked = BlogWriter.Diverse(products, 5);
        Assert.Equal(5, picked.Count);
        Assert.True(picked.Count(p => p.BrandName == "سونی") <= 2);
        Assert.Contains(picked, p => p.BrandName == "هایلو"); // ارزان‌ترین خوش‌نام
        Assert.Equal(picked.Count, picked.Distinct().Count());
        Assert.Equal(2, BlogWriter.Diverse(products.Take(2).ToList(), 5).Count); // کمبود کالا ← هرچه هست
        Assert.Empty(BlogWriter.Diverse([], 5));
    }

    [Fact]
    public void Pick_article_says_buy_suggestions_not_best_and_has_a_toc()
    {
        var products = Enumerable.Range(1, 6).Select(i => Product(i, i % 2 == 0 ? "سونی" : "انکر", 1_000_000 * i)).ToList();
        var article = BlogWriter.Write(new BlogBrief(BlogArticleType.BestPicks, "۵ پیشنهاد خرید هدفون", "خرید هدفون", "هدفون", "headphones", products, Now));

        Assert.StartsWith("۵ پیشنهاد خرید هدفون", article.Title);
        Assert.DoesNotContain("بهترین", article.Title);
        Assert.DoesNotContain("بهترین انتخاب", article.Html);
        Assert.Contains("خرید هدفون", article.Title);
        Assert.Contains("class=\"fs-toc\"", article.Html);
        Assert.Contains("<h2 id=\"s1\">", article.Html);
        Assert.Contains("href=\"#s1\"", article.Html);
        // کادر خلاصه اول، بعد فهرست مطالب، بعد اولین تیتر.
        var tldr = article.Html.IndexOf(BlogWriter.TldrClass, StringComparison.Ordinal);
        var toc = article.Html.IndexOf("fs-toc", StringComparison.Ordinal);
        var firstH2 = article.Html.IndexOf("<h2", StringComparison.Ordinal);
        Assert.True(tldr >= 0 && tldr < toc && toc < firstH2);
        Assert.Equal(5, article.ProductSlugs.Count);
    }

    [Fact]
    public void Custom_title_from_the_admin_is_used_as_is()
    {
        var products = Enumerable.Range(1, 4).Select(i => Product(i, "انکر" + i, 1_000_000 * i)).ToList();
        var article = BlogWriter.Write(new BlogBrief(BlogArticleType.BuyingGuide, "هدیه", null, "هدفون", "headphones", products, Now,
            CustomTitle: "  هدیهٔ تولد زیر ۵ میلیون؛ چه هدفونی بخریم؟ "));
        Assert.Equal("هدیهٔ تولد زیر ۵ میلیون؛ چه هدفونی بخریم؟", article.Title);
    }

    [Theory]
    [InlineData("<p>بدون تیتر</p>")]
    [InlineData("<h2>یک</h2><p>x</p><h2>دو</h2>")]
    public void Short_articles_get_no_toc(string html)
    {
        Assert.Equal(html, BlogWriter.WithToc(html));
    }

    [Fact]
    public void Planner_proposes_buy_suggestion_titles_without_best()
    {
        var categories = new List<PlannerCategory>
        {
            new(Guid.NewGuid(), "هدفون", 12, [(Guid.NewGuid(), "هدفون سونی"), (Guid.NewGuid(), "هدفون انکر")]),
            new(Guid.NewGuid(), "پاوربانک", 3, []),
        };
        var ideas = BlogPlanner.Propose(categories, [], ["هدفون بی سیم"], 6);
        Assert.NotEmpty(ideas);
        Assert.DoesNotContain(ideas, i => i.Topic.Contains("بهترین", StringComparison.Ordinal));
        Assert.Contains(ideas, i => i.Topic == "۵ پیشنهاد خرید هدفون بی سیم" && i.Origin == "demand");
        Assert.Contains(ideas, i => i.Topic == "۳ پیشنهاد خرید پاوربانک");
        Assert.All(ideas.Where(i => i.Type == BlogArticleType.BestPicks), i => Assert.StartsWith("خرید ", i.Keyword));
    }
}
