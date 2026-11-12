using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های دور ۲۱ برای «تحقیق» دربارهٔ کالا در سایت‌های دیگر (<see cref="ProductResearch"/>) و استفادهٔ آن در «توضیحات محصول».
/// <para>اصل‌ها: فقط دادهٔ مورد توافق (≥۲ منبع یا یک منبع معتبر)، هیچ جمله‌ای کپی نمی‌شود، ورودی خراب/حمله‌ای هرگز استثنا یا HTML نمی‌سازد.</para>
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Smart")]
public class ProductResearchTests
{
    private const string ShopA = """
        هدفون بی‌سیم هایلو مدل X با باتری قدرتمند و بلوتوث 5.3 عرضه می‌شود. این هدفون مناسب برای ورزش و پیاده‌روی است.
        قابلیت شارژ سریع و اتصال همزمان به دو دستگاه از ویژگی‌های آن است. ANC هم دارد.
        محتویات جعبه: هدفون، کابل شارژ USB-C، دفترچه راهنما.
        """;

    private const string ShopB = """
        طراحی سبک و باتری با دوام هایلو X آن را برای استفادهٔ روزانه مناسب کرده است. این مدل ایده‌آل برای ورزش است.
        پشتیبانی از شارژ سریع، حذف نویز فعال و multipoint (اتصال همزمان به دو گوشی) در آن دیده می‌شود.
        اقلام همراه
        کابل شارژ USB-C
        دفترچه راهنما
        سری گوشی اضافه
        """;

    private const string ShopC = """
        هایلو X یک هدفون بی‌سیم است با صدای خوب. مناسب برای مکالمه‌های کاری است. برای خرید با قیمت عالی و ارسال رایگان تماس بگیرید.
        بلوتوث نسخه 5.3 دارد و باتری آن تا 30 ساعت دوام می‌آورد.
        """;

    private static List<ResearchDoc> Docs() =>
    [
        new("shop-a.ir", ShopA, false),
        new("shop-b.ir", ShopB, false),
        new("shop-c.ir", ShopC, false),
    ];

    [Fact]
    public void Agreed_in_box_items_and_use_cases_need_two_sources()
    {
        var digest = ProductResearch.Analyze(Docs());

        Assert.Equal(3, digest.Sources);
        Assert.Contains("کابل شارژ USB-C", digest.InBox);
        Assert.Contains("دفترچه راهنما", digest.InBox);
        Assert.DoesNotContain("سری گوشی اضافه", digest.InBox); // فقط یک منبع غیرمعتبر گفته
        Assert.Contains("ورزش", digest.UseCases);
        Assert.DoesNotContain(digest.UseCases, u => u.Contains("مکالمه", StringComparison.Ordinal)); // فقط یک منبع
    }

    [Fact]
    public void One_trusted_source_is_enough()
    {
        var digest = ProductResearch.Analyze([new ResearchDoc("digikala.com", ShopC + "\nمحتویات جعبه: کیف حمل، کابل شارژ.", true)]);
        Assert.Contains("کیف حمل", digest.InBox);
        Assert.Contains(digest.UseCases, u => u.StartsWith("مکالمه", StringComparison.Ordinal));
    }

    [Fact]
    public void Highlights_use_our_own_labels_and_require_agreement()
    {
        var digest = ProductResearch.Analyze(Docs());
        var labels = digest.Highlights.Select(h => h.Label).ToList();
        Assert.Contains("شارژ سریع", labels);
        Assert.Contains("اتصال هم‌زمان به دو دستگاه", labels);
        Assert.Contains("حذف نویز فعال (ANC)", labels);
        Assert.True(digest.Highlights.Count <= 4);
        Assert.All(digest.Highlights, h => Assert.True(h.Sources >= 2));
    }

    [Fact]
    public void Negated_features_are_not_claimed()
    {
        var docs = new List<ResearchDoc>
        {
            new("a.ir", "این هدفون فاقد حذف نویز فعال است ولی باتری خوبی دارد و برای استفادهٔ روزمره مناسب است. توضیحات بیشتر در ادامه آمده است.", false),
            new("b.ir", "حذف نویز فعال ندارد؛ در عوض بلوتوث پایدار و وزن سبکی دارد که برای سفر مناسب است و کاربر از آن راضی خواهد بود.", false),
        };
        var digest = ProductResearch.Analyze(docs);
        Assert.DoesNotContain(digest.Highlights, h => h.Topic == SpecTopic.NoiseCancel);
    }

    [Fact]
    public void Topic_order_puts_the_most_discussed_topic_first()
    {
        var digest = ProductResearch.Analyze(Docs());
        Assert.Equal(SpecTopic.Battery, digest.TopicOrder[0]); // هر سه منبع دربارهٔ باتری نوشته‌اند
        Assert.Contains(SpecTopic.Bluetooth, digest.TopicOrder);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("کوتاه")]
    public void Empty_or_tiny_input_gives_an_empty_digest(string? text)
    {
        var digest = ProductResearch.Analyze(text is null ? null : [new ResearchDoc("x.ir", text, true)]);
        Assert.Equal(0, digest.Sources);
        Assert.Empty(digest.InBox);
        Assert.Empty(digest.UseCases);
        Assert.Empty(digest.Highlights);
    }

    [Fact]
    public void Junk_and_ads_never_become_items()
    {
        var spam = "محتویات جعبه: تخفیف ویژه، ارسال رایگان، تماس با واتساپ 0912، www.spam.ir. مناسب برای همه افراد و بهترین خرید است. "
                   + new string('ا', 100);
        var digest = ProductResearch.Analyze([new ResearchDoc("spam.ir", spam, true), new ResearchDoc("spam2.ir", spam, true)]);
        Assert.Empty(digest.InBox);
        Assert.Empty(digest.UseCases);
    }

    [Fact]
    public void Same_source_counts_once()
    {
        var digest = ProductResearch.Analyze([new ResearchDoc("shop-a.ir", ShopA, false), new ResearchDoc("SHOP-A.ir", ShopA + " ", false)]);
        Assert.Equal(1, digest.Sources);
        Assert.Empty(digest.InBox); // یک منبع غیرمعتبر (حتی دو بار) کافی نیست
    }

    [Fact]
    public void Page_text_reads_json_ld_meta_and_woocommerce_panel_but_not_scripts_or_reviews()
    {
        const string html = """
            <html><head>
            <meta property="og:description" content="هدفون بی‌سیم با شارژدهی بالا و طراحی سبک">
            <script type="application/ld+json">{"@type":"Product","name":"X","description":"<p>توضیح ساختاریافتهٔ محصول برای موتور جست‌وجو</p>"}</script>
            </head><body>
            <div class="woocommerce-Tabs-panel woocommerce-Tabs-panel--description" id="tab-description">
              <h2>توضیحات</h2><p>محتویات جعبه</p><ul><li>کابل شارژ</li><li>دفترچه راهنما</li></ul>
              <script>alert('x')</script>
            </div>
            <div class="woocommerce-Tabs-panel woocommerce-Tabs-panel--reviews" id="tab-reviews"><p>نظر مشتری: افتضاح بود</p></div>
            </body></html>
            """;
        var text = ProductResearch.PageText(html);
        Assert.Contains("طراحی سبک", text);
        Assert.Contains("توضیح ساختاریافتهٔ محصول", text);
        Assert.Contains("کابل شارژ", text);
        Assert.DoesNotContain("alert", text);
        Assert.DoesNotContain("افتضاح", text);
        Assert.DoesNotContain("<", text);
        Assert.Contains("کابل شارژ", ProductResearch.InBoxItems(text));
    }

    [Theory]
    [InlineData("ANC هم دارد", "anc", true)]
    [InlineData("high performance chip", "anc", false)]
    [InlineData("balanced sound", "anc", false)]
    [InlineData("استاندارد IPX5", "ipx", true)]
    [InlineData("باتری 5000mAh", "mah", true)]
    [InlineData("شارژ سریع دارد", "شارژ سریع", true)]
    public void Mentions_matches_short_latin_tokens_only_as_whole_words(string text, string pattern, bool expected) =>
        Assert.Equal(expected, ProductResearch.Mentions(text, pattern));

    [Fact]
    public void In_box_items_keep_hyphenated_names_and_strip_filler_words()
    {
        var items = ProductResearch.InBoxItems("محتویات جعبه شامل: هدفون، کابل شارژ USB-C و یک عدد کیف حمل.").ToList();
        Assert.Contains("کابل شارژ USB-C", items);
        Assert.Contains("کیف حمل", items);
        Assert.DoesNotContain(items, i => i.StartsWith("شامل", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"data\":5}")]
    [InlineData("{\"data\":{\"product\":[]}}")]
    public void Digikala_text_is_safe_on_broken_json(string? json) =>
        Assert.Equal("", ProductResearch.DigikalaText(json));

    [Fact]
    public void Digikala_text_collects_long_strings_from_expert_review_branches()
    {
        const string json = """
            {"data":{"product":{"title_fa":"x","expert_reviews":{"description":"<p>این هدفون با طراحی ارگونومیک و باتری قوی برای استفادهٔ طولانی ساخته شده است.</p>",
              "review_sections":[{"title":"طراحی","sections":[{"text":"بدنهٔ سبک و مقاوم این مدل در دست به خوبی جا می‌گیرد و حمل آن آسان است.","image":"https://x/y.jpg"}]}]},
              "review":{"description":"محتویات جعبه: هدفون، کابل شارژ، دفترچه راهنما و چند سری اضافه."}}}}
            """;
        var text = ProductResearch.DigikalaText(json);
        Assert.Contains("طراحی ارگونومیک", text);
        Assert.Contains("بدنهٔ سبک", text);
        Assert.Contains("کابل شارژ", text);
        Assert.DoesNotContain("https://", text);
        Assert.DoesNotContain("<p>", text);
    }

    // ── استفاده در «توضیحات محصول» ───────────────────────────────────

    private static readonly StoreFacts Store = new() { SiteName = "فراتک شاپ", TitleSuffixLength = 12, ReturnEnabled = true, ReturnDays = 7 };

    private static SeoSubject Subject() => new()
    {
        Kind = SeoEntityKind.Product,
        EntityId = Guid.Parse("00000000-0000-4000-8000-000000021021"),
        Name = "هدفون بی‌سیم هایلو مدل X200",
        Slug = "haylou-x200",
        PathPrefix = "/product/",
        DefaultTitle = "هدفون بی‌سیم هایلو مدل X200",
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Product = new ProductFacts
        {
            CategoryName = "هدفون بی‌سیم",
            CategorySlug = "wireless-headphones",
            BrandName = "هایلو",
            BrandSlug = "haylou",
            WarrantyTitle = "۱۸ ماهه",
            InStock = true,
            Features = [new("نسخه بلوتوث", "5.3"), new("شارژدهی", "30 ساعت"), new("مقاومت در برابر آب", "IPX5")],
        },
    };

    [Fact]
    public void Description_uses_agreed_research_without_copying_any_source_sentence()
    {
        var digest = ProductResearch.Analyze(Docs());
        var notes = ProductResearch.ToNotes(digest, null);
        var subject = Subject();
        var ctx = new WriterContext { Subject = subject, Store = Store, Keyword = KeywordPlanner.ShortName(subject.Name), Notes = notes };
        var html = ContentWriter.ProductReview(ctx);

        Assert.NotNull(html);
        Assert.Contains("ویژگی‌های کلیدی", html);
        Assert.Contains("کابل شارژ USB-C", html);   // اقلام همراه مورد توافق
        Assert.Contains("ورزش", html);              // کاربرد مورد توافق
        Assert.Contains("شارژ سریع", html);          // ویژگی مورد توافق با برچسب خود ما
        Assert.DoesNotContain("سری گوشی اضافه", html);
        // هیچ جملهٔ منبع کپی نشده است.
        foreach (var sentence in (ShopA + ShopB).Split(['.', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 25))
            Assert.DoesNotContain(sentence, html);
    }

    [Fact]
    public void Research_highlight_is_skipped_when_our_own_spec_covers_the_topic()
    {
        var notes = new ReviewNotes { Highlights = [new ResearchHighlight("مقاومت در برابر آب و تعریق", SpecTopic.Water, 3)] };
        var subject = Subject();
        var ctx = new WriterContext { Subject = subject, Store = Store, Keyword = KeywordPlanner.ShortName(subject.Name), Notes = notes };
        var html = ContentWriter.ProductReview(ctx)!;
        Assert.DoesNotContain("مقاومت در برابر آب و تعریق</strong> (", html); // مشخصات خود ما (IPX5) دقیق‌تر است
    }

    [Fact]
    public void Attack_text_in_research_is_encoded()
    {
        var notes = new ReviewNotes
        {
            InBox = ["<script>alert(1)</script>", "کابل <img src=x onerror=alert(1)>"],
            UseCases = ["<a href=\"https://evil\">ورزش</a>"],
            Highlights = [new ResearchHighlight("<b onmouseover=x>ویژگی</b>", SpecTopic.Generic, 2)],
        };
        var subject = Subject();
        var ctx = new WriterContext { Subject = subject, Store = Store, Keyword = KeywordPlanner.ShortName(subject.Name), Notes = notes };
        var html = ContentWriter.ProductReview(ctx)!;
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onmouseover", html, StringComparison.OrdinalIgnoreCase);
    }
}
