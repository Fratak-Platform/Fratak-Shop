using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.Smart;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// دور ۲۶ – بلاگ: انواع تازهٔ مقاله (معرفی دسته، نگهداری، کمپین، خبر)، پیام درست تقویم (باگ «تقویم پره»)
/// و «تحقیق پیش از نوشتن» (جست‌وجوهای مرتبط واقعی).
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Round26")]
public class Round26BlogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 8, 0, 0, TimeSpan.Zero); // ۳ آبان ۱۴۰۵

    private static BlogProduct Product(string name, string slug, string brand, decimal price, params (string Key, string Value)[] features) =>
        new(Guid.NewGuid(), name, slug, brand, price, true, 4.3, 5, 10,
            features.Select(f => new SeoFeature(f.Key, f.Value)).ToList(), ["صدای شفاف"], [], ["ورزشکاران"]);

    private static List<BlogProduct> Headphones() =>
    [
        Product("هدفون بی سیم سونی مدل A1", "sony-a1", "سونی", 3_000_000, ("نوع اتصال", "بی‌سیم"), ("عمر باتری", "30 ساعت")),
        Product("هدفون بی سیم انکر مدل B2", "anker-b2", "انکر", 1_800_000, ("نوع اتصال", "بی‌سیم"), ("عمر باتری", "20 ساعت")),
        Product("هدفون سیمی شیائومی مدل C3", "xiaomi-c3", "شیائومی", 600_000, ("نوع اتصال", "باسیم")),
    ];

    // ───────────── پیام تقویم ─────────────

    [Fact]
    public void Plan_message_says_full_only_when_nothing_is_needed()
    {
        Assert.Contains("پر است", new BlogPlanResult(0, 0, null).Message, StringComparison.Ordinal);
        Assert.Contains("پر است", new BlogPlanResult(0, -2, null).Message, StringComparison.Ordinal);
        // باگ قبلی: همهٔ ایده‌ها حذف شده (Needed > 0) ولی چیزی ساخته نشد ← نباید «پر است» بگوید؛ دلیل واقعی نشان داده شود.
        var empty = new BlogPlanResult(0, 8, "هیچ دستهٔ فعالی کالای موجود ندارد.");
        Assert.DoesNotContain("پر است", empty.Message, StringComparison.Ordinal);
        Assert.Equal("هیچ دستهٔ فعالی کالای موجود ندارد.", empty.Message);
        Assert.Contains("۳", new BlogPlanResult(3, 8, "کم بود").Message, StringComparison.Ordinal);
        Assert.Contains("۵", new BlogPlanResult(3, 8, "کم بود").Message, StringComparison.Ordinal);
        Assert.DoesNotContain("جای خالی", new BlogPlanResult(8, 8, null).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Persian_month_names_the_current_solar_month()
    {
        Assert.Equal("آبان ۱۴۰۵", BlogPlanner.PersianMonth(Now));
        Assert.Equal("فروردین ۱۴۰۶", BlogPlanner.PersianMonth(new DateTimeOffset(2027, 3, 22, 8, 0, 0, TimeSpan.Zero)));
    }

    // ───────────── برنامه‌ریز ─────────────

    [Fact]
    public void Planner_makes_mostly_category_articles_plus_news_and_campaigns()
    {
        var fresh = new List<(Guid Id, string Name)> { (Guid.NewGuid(), "هدفون تازه ۱"), (Guid.NewGuid(), "هدفون تازه ۲") };
        var headphones = new PlannerCategory(Guid.NewGuid(), "هدفون", 8, [(Guid.NewGuid(), "هدفون سونی")], fresh);
        var single = new PlannerCategory(Guid.NewGuid(), "کتری برقی", 1, []);
        var campaigns = new List<PlannerCampaign> { new("جشنواره پاییزه فراتک", null, "/campaign/autumn") };

        var ideas = BlogPlanner.Propose(new[] { headphones, single }, Array.Empty<string>(), Array.Empty<string>(), 20, null, campaigns, Now);

        Assert.Contains(ideas, i => i.Type == BlogArticleType.Campaign && i.Topic == "جشنواره پاییزه فراتک" && i.Origin == "campaign");
        var news = Assert.Single(ideas, i => i.Type == BlogArticleType.News);
        Assert.Equal("تازه‌های هدفون در آبان ۱۴۰۵", news.Topic);
        Assert.Equal(fresh.Select(f => f.Id), news.ProductIds);
        Assert.Contains(news.Keyword, news.Topic, StringComparison.Ordinal); // کلیدواژه در عنوان
        Assert.Contains(ideas, i => i.Type == BlogArticleType.CategoryGuide && i.Topic == "انواع هدفون و کاربرد هر کدام" && i.Keyword == "انواع هدفون");
        Assert.Contains(ideas, i => i.Type == BlogArticleType.HowTo && i.Keyword == "نگهداری هدفون");
        // دستهٔ تک‌کالا هم مقالهٔ دسته‌ای می‌گیرد، ولی نه راهنمای خرید (نیاز به ≥ ۲ کالا) و نه معرفی کالای بی‌پرفروش.
        Assert.Contains(ideas, i => i.CategoryId == single.Id && i.Type == BlogArticleType.CategoryGuide);
        Assert.DoesNotContain(ideas, i => i.CategoryId == single.Id && i.Type is BlogArticleType.BuyingGuide or BlogArticleType.Review);
        // مقالهٔ دسته‌ای بیشترین سهم را دارد.
        var categoryTypes = ideas.Count(i => i.Type is BlogArticleType.CategoryGuide or BlogArticleType.BuyingGuide or BlogArticleType.HowTo or BlogArticleType.Comparison);
        Assert.True(categoryTypes > ideas.Count(i => i.Type == BlogArticleType.Review));
        Assert.Equal(ideas.Count, ideas.Select(i => PersianText.Normalize(i.Topic)).Distinct().Count()); // بدون تکرار
    }

    [Fact]
    public void Planner_without_now_makes_no_news_and_respects_taken_topics()
    {
        var headphones = new PlannerCategory(Guid.NewGuid(), "هدفون", 8, [], [(Guid.NewGuid(), "هدفون تازه")]);
        var ideas = BlogPlanner.Propose(new[] { headphones }, new[] { "انواع هدفون و کاربرد هر کدام" }, Array.Empty<string>(), 10);
        Assert.DoesNotContain(ideas, i => i.Type == BlogArticleType.News);
        Assert.DoesNotContain(ideas, i => i.Type == BlogArticleType.CategoryGuide);
        Assert.Empty(BlogPlanner.Propose(Array.Empty<PlannerCategory>(), Array.Empty<string>(), Array.Empty<string>(), 5, null, null, Now));
        Assert.Empty(BlogPlanner.Propose(new[] { headphones }, Array.Empty<string>(), Array.Empty<string>(), 0, null, null, Now));
    }

    [Fact]
    public void Second_pass_without_skipped_topics_brings_deleted_ideas_back()
    {
        // همان چیزی که سرویس انجام می‌دهد: دور اول با موضوع‌های حذف‌شده «گرفته»، دور دوم بدون آن‌ها.
        var single = new PlannerCategory(Guid.NewGuid(), "کتری برقی", 1, []);
        var skipped = BlogPlanner.Propose(new[] { single }, Array.Empty<string>(), Array.Empty<string>(), 10).Select(i => i.Topic).ToList();
        Assert.NotEmpty(skipped);
        Assert.Empty(BlogPlanner.Propose(new[] { single }, skipped, Array.Empty<string>(), 10));
        Assert.Equal(skipped.Count, BlogPlanner.Propose(new[] { single }, Array.Empty<string>(), Array.Empty<string>(), 10).Count);
    }

    // ───────────── نویسنده ─────────────

    [Fact]
    public void Category_guide_is_data_driven_and_links_real_related_searches()
    {
        var related = new[] { "هدفون بی سیم ارزان", "هدفون گیمینگ", "x", "<script>alert(1)</script>" };
        var article = BlogWriter.Write(new BlogBrief(BlogArticleType.CategoryGuide, "انواع هدفون و کاربرد هر کدام", "انواع هدفون", "هدفون", "headphones",
            Headphones(), Now, Related: related));

        Assert.Contains("انواع هدفون", article.Title, StringComparison.Ordinal);
        Assert.Contains("نوع اتصال", article.Html, StringComparison.Ordinal);          // گروه‌بندی بر اساس مشخصهٔ متمایزکننده
        Assert.Contains("۳ مدل", article.Html, StringComparison.Ordinal);               // تعداد واقعی
        Assert.Contains("۶۰۰٬۰۰۰", article.Html, StringComparison.Ordinal);             // کمترین قیمت واقعی
        Assert.Contains("/search?q=" + Uri.EscapeDataString("هدفون گیمینگ"), article.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", article.Html, StringComparison.Ordinal);       // ورودی تحقیق رمزگذاری می‌شود
        Assert.Contains("[[product:", article.Html, StringComparison.Ordinal);
        Assert.True(System.Text.RegularExpressions.Regex.Matches(article.Html, "<h2").Count >= 3);
        Assert.DoesNotContain("بهترین", article.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Writers_without_research_add_no_related_section()
    {
        var article = BlogWriter.Write(new BlogBrief(BlogArticleType.CategoryGuide, "انواع هدفون و کاربرد هر کدام", "انواع هدفون", "هدفون", "headphones", Headphones(), Now));
        Assert.DoesNotContain("/search?q=", article.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Care_guide_never_invents_specific_claims_and_refers_to_the_manual()
    {
        var article = BlogWriter.Write(new BlogBrief(BlogArticleType.HowTo, "نکات نگهداری و استفادهٔ درست از هدفون", "نگهداری هدفون", "هدفون", "headphones", Headphones(), Now));
        Assert.Contains("نگهداری هدفون", article.Title, StringComparison.Ordinal);
        Assert.Contains("راهنما", article.Html, StringComparison.Ordinal);
        Assert.True(System.Text.RegularExpressions.Regex.Matches(article.Html, "<h2").Count >= 3);
        Assert.Contains("[[product:", article.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Campaign_links_only_internal_campaign_pages()
    {
        var good = BlogWriter.Write(new BlogBrief(BlogArticleType.Campaign, "جشنواره پاییزه فراتک", "جشنواره پاییزه فراتک", "هدفون", "headphones",
            Headphones(), Now, LinkUrl: "/campaign/autumn"));
        Assert.Equal("جشنواره پاییزه فراتک", good.Title);
        Assert.Contains("href=\"/campaign/autumn\"", good.Html, StringComparison.Ordinal);
        Assert.Contains("علاقه‌مندی", good.Html, StringComparison.Ordinal); // ویژگی واقعی هشدار ارزانی

        foreach (var bad in new[] { "//evil.example/x", "https://evil.example", "javascript:alert(1)" })
        {
            var article = BlogWriter.Write(new BlogBrief(BlogArticleType.Campaign, "جشنواره پاییزه فراتک", null, "هدفون", "headphones", Headphones(), Now, LinkUrl: bad));
            Assert.DoesNotContain("evil", article.Html, StringComparison.Ordinal);
            Assert.DoesNotContain("javascript:", article.Html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Campaign_title_with_spin_characters_does_not_break_the_text()
    {
        var article = BlogWriter.Write(new BlogBrief(BlogArticleType.Campaign, "حراج [ویژه] | پاییز", null, "هدفون", "headphones", Headphones(), Now));
        Assert.Contains("ویژه", article.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("[ویژه]", article.Html.Replace(article.Title, "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void News_introduces_each_new_product_with_a_card()
    {
        var products = Headphones();
        var article = BlogWriter.Write(new BlogBrief(BlogArticleType.News, "تازه‌های هدفون در آبان ۱۴۰۵", "تازه‌های هدفون", "هدفون", "headphones", products, Now));
        Assert.Equal("تازه‌های هدفون در آبان ۱۴۰۵", article.Title);
        Assert.All(products, p => Assert.Contains($"[[product:{p.Slug}]]", article.Html, StringComparison.Ordinal));
        Assert.Contains("۳ مدل تازه", article.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void New_types_with_a_single_product_still_produce_an_article()
    {
        var one = Headphones().Take(1).ToList();
        foreach (var type in new[] { BlogArticleType.CategoryGuide, BlogArticleType.HowTo, BlogArticleType.Campaign, BlogArticleType.News })
        {
            var article = BlogWriter.Write(new BlogBrief(type, "موضوع آزمایشی هدفون", "هدفون", "هدفون", "headphones", one, Now));
            Assert.False(string.IsNullOrWhiteSpace(article.Html));
            Assert.Contains("[[product:sony-a1]]", article.Html, StringComparison.Ordinal);
        }
    }
}
