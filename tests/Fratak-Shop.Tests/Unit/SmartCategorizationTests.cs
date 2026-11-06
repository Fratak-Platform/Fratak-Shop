using Fratak_Shop.Core.Services.Smart;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های دور ۲۱ برای «دسته‌بندی هوشمند» و «نیت جست‌وجو» (بدون دیتابیس).
/// <list type="bullet">
/// <item>نوع کالا از ابتدای نام: «قاب گوشی» قاب است نه گوشی؛ واژه‌های پرکننده («خرید»، «پک») رد می‌شوند.</item>
/// <item>دسته‌بند آماری فقط با اطمینان کافی تصمیم می‌گیرد؛ نام بی‌معنی/خالی/حمله‌ای هرگز استثنا نمی‌دهد.</item>
/// <item>خوشه‌بندی: ≥۳ کالای مشابه ← دستهٔ تازه؛ برند، رنگ و عدد هرگز نام دسته نمی‌شوند.</item>
/// <item>جست‌وجو: عبارت کلی ← دسته؛ عبارت مدل‌دار ← خود کالا.</item>
/// </list>
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Smart")]
public class SmartCategorizationTests
{
    // ── نوع کالا ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("گوشی موبایل سامسونگ مدل Galaxy A55 ظرفیت 256 گیگابایت", "mobile-phone")]
    [InlineData("قاب گوشی سامسونگ A55 سیلیکونی", "phone-case")]
    [InlineData("خرید هدفون بی‌سیم سونی WH-1000XM5", "headphone")]
    [InlineData("هندزفری بی سیم شیائومی Redmi Buds 4", "wireless-earbuds")]
    [InlineData("هندزفری سیمی سامسونگ", "handsfree")]
    [InlineData("پاور بانک انکر ۲۰۰۰۰ میلی‌آمپر", "power-bank")]
    [InlineData("لپ‌تاپ ایسوس Vivobook 15", "laptop")]
    [InlineData("ساعت هوشمند شیائومی Watch S1", "smartwatch")]
    [InlineData("ساعت مچی مردانه کاسیو", "wristwatch")]
    [InlineData("پک ۳ عددی کابل شارژ USB-C", "cable")]   // «پک»، «۳» و «عددی» پرکننده‌اند
    [InlineData("2 عدد گلس محافظ صفحه", "screen-protector")]
    [InlineData("iPhone 15 Pro Max 256GB", "mobile-phone")]
    [InlineData("قاب‌های سیلیکونی آیفون", "phone-case")]
    public void Product_type_is_read_from_the_start_of_the_name(string title, string expected)
    {
        Assert.Equal(expected, ProductTypes.Detect(title)?.Key);
    }

    [Theory]
    [InlineData("لیوان سرامیکی طرح گل")]
    [InlineData("سامسونگ گوشی")]                       // برند اول، «گوشی» در جایگاه دوم هم پذیرفته است ← پایین بررسی می‌شود
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("<script>alert(1)</script>")]
    public void Unknown_or_hostile_names_never_throw(string? title)
    {
        var type = ProductTypes.Detect(title);
        if (title == "سامسونگ گوشی")
            Assert.Equal("mobile-phone", type?.Key);
        else
            Assert.Null(type);
    }

    [Fact]
    public void Every_product_type_has_a_valid_english_slug_and_persian_name()
    {
        Assert.Equal(ProductTypes.All.Count, ProductTypes.All.Select(t => t.Key).Distinct().Count());
        Assert.All(ProductTypes.All, t =>
        {
            Assert.Matches("^[a-z]+(-[a-z]+)*$", t.Key);
            Assert.True(Fratak_Shop.Data.Utilities.EnglishSlug.IsEnglish(t.Key));
            Assert.True(t.Name.Any(c => c is >= '\u0600' and <= '\u06FF'));
            Assert.NotEmpty(t.Keywords);
            Assert.NotEmpty(t.ParentHints);
        });
        Assert.Contains(ProductTypes.All, t => t is { Key: "mobile-phone", Important: true });
    }

    [Theory]
    [InlineData("گوشی موبایل", null, true)]
    [InlineData("گوشی", null, true)]
    [InlineData("هر چیزی", "mobile-phone", true)]
    [InlineData("لوازم جانبی گوشی", null, false)]
    [InlineData("قاب گوشی", null, false)]
    public void Category_names_are_matched_to_types_by_their_own_head(string name, string? slug, bool expected)
    {
        Assert.Equal(expected, ProductTypes.NameMatches(ProductTypes.Find("mobile-phone")!, name, slug));
    }

    // ── دسته‌بند آماری ───────────────────────────────────────────────

    private static readonly Guid PowerBanks = Guid.NewGuid();
    private static readonly Guid Kitchen = Guid.NewGuid();
    private static readonly Guid Toys = Guid.NewGuid();

    private static CategoryClassifier Trained() => CategoryClassifier.Train(
    [
        new CategorySample(PowerBanks, "پاوربانک", ["پاوربانک انکر 10000", "پاوربانک شیائومی 20000 فست", "پاوربانک باسئوس مگنتی", "پاوربانک سامسونگ"]),
        new CategorySample(Kitchen, "ظروف آشپزخانه", ["قابلمه چدنی", "ماهیتابه نچسب", "قابلمه استیل", "ماهیتابه گرانیتی سامسونگ"]),
        new CategorySample(Toys, "اسباب بازی", ["عروسک پولیشی خرس", "ماشین کنترلی", "پازل ۱۰۰۰ تکه", "عروسک باربی"]),
    ]);

    [Theory]
    [InlineData("پاوربانک ریمکس 30000", "power")]
    [InlineData("ماهیتابه سرامیکی سایز ۲۸", "kitchen")]
    [InlineData("عروسک پولیشی خرگوش", "toys")]
    public void Classifier_picks_the_right_category_with_confidence(string title, string expected)
    {
        var guess = Trained().Classify(title);
        Assert.True(CategoryClassifier.IsConfident(guess), $"{guess}");
        Assert.Equal(expected switch { "power" => PowerBanks, "kitchen" => Kitchen, _ => Toys }, guess!.CategoryId);
    }

    [Theory]
    [InlineData("سامسونگ")]                 // برند در چند دسته ← مردد
    [InlineData("کالای ناشناختهٔ عجیب")]
    [InlineData("A55 256")]
    [InlineData("")]
    [InlineData("' OR 1=1 --")]
    public void Classifier_is_never_confident_without_real_evidence(string title)
    {
        Assert.False(CategoryClassifier.IsConfident(Trained().Classify(title)));
    }

    [Fact]
    public void Empty_classifier_returns_null()
    {
        var empty = CategoryClassifier.Train([]);
        Assert.Equal(0, empty.Count);
        Assert.Null(empty.Classify("پاوربانک"));
        Assert.Equal(1, CategoryClassifier.Train([new CategorySample(Guid.NewGuid(), "پاوربانک", [])]).Count); // دستهٔ بدون کالا با نام خودش
    }

    [Fact]
    public void Terms_drop_numbers_models_stopwords_and_fillers()
    {
        var terms = CategoryClassifier.Terms("خرید هدفون‌های بی‌سیم سونی مدل WH-1000XM5 با 30 ساعت");
        Assert.DoesNotContain("خرید", terms);
        Assert.DoesNotContain("مدل", terms);
        Assert.DoesNotContain("با", terms);
        Assert.False(terms.Any(t => t.Any(char.IsDigit)));
        Assert.Contains("هدفون", terms);
        Assert.Contains("سونی", terms);
    }

    // ── خوشه‌بندی ────────────────────────────────────────────────────

    [Fact]
    public void Three_similar_products_form_a_new_category_but_brands_and_singletons_do_not()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var s1 = Guid.NewGuid(); var s2 = Guid.NewGuid(); var s3 = Guid.NewGuid();
        var lone = Guid.NewGuid();
        var p1 = Guid.NewGuid(); var p2 = Guid.NewGuid(); var p3 = Guid.NewGuid();
        var clusters = CategoryClassifier.Cluster(
        [
            (a, "لیوان سرامیکی طرح گل"), (b, "لیوان سرامیکی دسته‌دار"), (c, "لیوان سرامیکی ساده سفید"),
            (s1, "سامسونگ مدل X1"), (s2, "سامسونگ مدل X2"), (s3, "سامسونگ مدل X3"),
            (lone, "گلدان شیشه‌ای"),
            (p1, "پاوربانک انکر"), (p2, "پاوربانک شیائومی"), (p3, "پاور بانک باسئوس"),
        ], ["سامسونگ", "Anker"]);

        Assert.Equal(2, clusters.Count);
        var mugs = Assert.Single(clusters, x => x.Type is null);
        Assert.Equal("لیوان سرامیکی", mugs.Name);
        Assert.Equal(new[] { a, b, c }.OrderBy(x => x), mugs.ProductIds.OrderBy(x => x));
        var banks = Assert.Single(clusters, x => x.Type is not null);
        Assert.Equal("power-bank", banks.Type!.Key);
        Assert.Equal("پاوربانک", banks.Name);
        Assert.Equal(3, banks.ProductIds.Count);
        Assert.False(clusters.Any(x => x.ProductIds.Contains(lone) || x.ProductIds.Contains(s1)));
    }

    [Fact]
    public void Colours_and_adjectives_never_become_a_category_name()
    {
        var clusters = CategoryClassifier.Cluster(
        [
            (Guid.NewGuid(), "مشکی ۱"), (Guid.NewGuid(), "مشکی ۲"), (Guid.NewGuid(), "مشکی ۳"),
            (Guid.NewGuid(), "اورجینال الف"), (Guid.NewGuid(), "اورجینال ب"), (Guid.NewGuid(), "اورجینال ج"),
        ], []);
        Assert.Empty(clusters);
    }

    [Theory]
    [InlineData("سایر لوازم جانبی", null, true)]
    [InlineData("متفرقه", null, true)]
    [InlineData("دسته‌بندی نشده", null, true)]
    [InlineData("Other", null, true)]
    [InlineData("هر چیزی", "uncategorized", true)]
    [InlineData("لوازم جانبی", "accessories", false)]
    [InlineData("گوشی موبایل", "mobile-phone", false)]
    public void Catch_all_categories_are_recognised(string name, string? slug, bool expected)
    {
        Assert.Equal(expected, CategoryClassifier.IsCatchAll(name, slug));
    }

    [Theory]
    [InlineData("power bank", "Power Bank")]
    [InlineData("  لیوان   سرامیکی ", "لیوان سرامیکی")]
    [InlineData("", "دستهٔ تازه")]
    public void Display_names_are_clean(string input, string expected)
    {
        Assert.Equal(expected, SmartCategoryService.Display(input));
    }

    [Fact]
    public void Report_summary_is_short_persian()
    {
        Assert.Equal("کالای بی‌دسته‌ای نبود.", CategorizeReport.Empty.Summary);
        var report = new CategorizeReport(10, 7, [new CreatedCategory(Guid.NewGuid(), "پاوربانک", "power-bank", 3, null)], 3);
        Assert.Contains("۷ کالا دسته گرفت", report.Summary);
        Assert.Contains("پاوربانک", report.Summary);
        Assert.Contains("۳ کالا هنوز", new CategorizeReport(3, 0, [], 3).Summary);
    }

    // ── نیت جست‌وجو ─────────────────────────────────────────────────

    [Theory]
    [InlineData("گوشی", true)]
    [InlineData("هدفون بی سیم", true)]
    [InlineData("گوشی A55", false)]
    [InlineData("گوشی ۱۲۸ گیگ", false)]
    [InlineData("iphone 15", false)]
    [InlineData("یک دو سه چهار", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Broad_queries_have_no_model_numbers_and_few_words(string? query, bool expected)
    {
        Assert.Equal(expected, SearchIntent.IsBroad(query));
    }

    private static readonly (Guid Id, string Name, string Slug)[] ShopCategories =
    [
        (Guid.NewGuid(), "قاب گوشی", "phone-case"),
        (Guid.NewGuid(), "گوشی موبایل", "mobile-phone"),
        (Guid.NewGuid(), "هدفون بی سیم", "wireless-headphones"),
        (Guid.NewGuid(), "سایر گوشی‌ها", "other-phones"),
    ];

    [Theory]
    [InlineData("گوشی", "mobile-phone")]
    [InlineData("موبایل", "mobile-phone")]
    [InlineData("قاب گوشی", "phone-case")]
    [InlineData("هدفون", "wireless-headphones")]
    [InlineData("هدفون بی‌سیم", "wireless-headphones")]
    public void Broad_query_finds_its_category(string query, string slug)
    {
        Assert.Equal(slug, SearchIntent.MatchCategory(query, ShopCategories)?.Slug);
    }

    [Theory]
    [InlineData("گوشی A55")]
    [InlineData("لیوان")]
    [InlineData("سایر")]
    [InlineData("<b>گوشی</b> 2")]
    public void Specific_or_unknown_queries_find_no_category(string query)
    {
        Assert.Null(SearchIntent.MatchCategory(query, ShopCategories));
    }
}
