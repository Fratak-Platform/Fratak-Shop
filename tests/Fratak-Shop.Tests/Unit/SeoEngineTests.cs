using System.Net;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Web.Infrastructure.SeoEngine;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های موتور هوشمند سئو (مرحلهٔ هفتم – فاز ۸) بدون دیتابیس و بدون اینترنت:
/// ابزار متن فارسی، مدل‌های متنی (SimHash، Jaccard، مدل زبانی)، نمره‌دهی، برنامه‌ریز کلمهٔ کلیدی و نامک،
/// نویسندهٔ محتوا، خواندن پاسخ موتورهای جست‌وجو (نمونه‌ها ساختار واقعی دارند)، زمان‌بندی نوبت‌ها، اولویت و محافظ SSRF.
/// </summary>
public class SeoEngineTests
{
    // ── داده‌های نمونه ──────────────────────────────────────────────────

    private static readonly StoreFacts Store = new()
    {
        SiteName = "فراتک",
        TitleSuffixLength = 8, // « | فراتک»
        ReturnEnabled = true,
        ReturnDays = 7,
        DeliveryMinDays = 3,
        DeliveryMaxDays = 7,
        FreeShippingThreshold = 1_000_000,
        CashOnDelivery = false,
    };

    private static SeoSubject Product(
        string name = "گوشی موبایل سامسونگ مدل Galaxy A55 ظرفیت 256 گیگابایت",
        string? seoTitle = null,
        string? meta = null,
        string? keyword = null,
        string? body = null,
        string slug = "galaxy-a55",
        int views = 5,
        DateTimeOffset? createdAt = null,
        bool noIndex = false,
        Guid? id = null) => new()
        {
            Kind = SeoEntityKind.Product,
            EntityId = id ?? Guid.NewGuid(),
            Name = name,
            EnglishName = "Samsung Galaxy A55",
            Slug = slug,
            PathPrefix = "/product/",
            SeoTitle = seoTitle,
            MetaDescription = meta,
            FocusKeyword = keyword,
            NoIndex = noIndex,
            DefaultTitle = name,
            BodyHtml = body,
            Views = views,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow.AddDays(-60),
            Images = [new SeoImageInfo(Guid.NewGuid(), null, true)],
            Product = new ProductFacts
            {
                CategoryName = "گوشی موبایل",
                BrandName = "سامسونگ",
                WarrantyTitle = "۱۸ ماهه شرکتی",
                InStock = true,
                Features = [new SeoFeature("حافظه داخلی", "256 گیگابایت"), new SeoFeature("اندازه صفحه نمایش", "6.6 اینچ")],
            },
        };

    private static string LongBody(string keyword) =>
        $"<h2>معرفی {keyword}</h2>" +
        string.Concat(Enumerable.Range(1, 12).Select(i =>
            $"<p>{keyword} یکی از گزینه‌های محبوب بازار است و در این بخش شمارهٔ {i} دربارهٔ کیفیت ساخت، دوام باتری، صفحه نمایش روشن و دوربین آن توضیح می‌دهیم تا انتخاب آسان‌تری داشته باشید. همچنین <a href=\"/category/mobile\">گوشی‌های دیگر</a> را ببینید.</p>"));

    // ── ابزار متن فارسی ─────────────────────────────────────────────────

    [Fact]
    public void Normalize_UnifiesArabicLettersDigitsZwnjAndDiacritics()
    {
        Assert.Equal("کیف های چرمی 123", PersianText.Normalize("كيف\u200Cهای  چرمِی ۱۲۳"));
        Assert.Equal("", PersianText.Normalize("   "));
        Assert.Equal("galaxy a55", PersianText.Normalize("Galaxy A55"));
    }

    [Fact]
    public void Tokenize_KeepsDecimalsAndDropsPunctuation()
    {
        Assert.Equal(new List<string> { "صفحه", "6.6", "اینچ", "عالی" }, PersianText.Tokenize("صفحهٔ 6.6 اینچ، عالی!"));
        Assert.Equal(3, PersianText.WordCount("یک، دو. سه"));
        Assert.Empty(PersianText.Tokenize(null));
    }

    [Theory]
    [InlineData("کتابها", "کتاب")]
    [InlineData("گوشیهای", "گوشی")]
    [InlineData("بهترین", "بهترین")] // ریشه کوتاه‌تر از ۳ حرف نمی‌شود
    [InlineData("galaxy", "galaxy")] // واژهٔ لاتین دست نمی‌خورد
    public void Stem_RemovesOnlySafePersianSuffixes(string token, string expected) =>
        Assert.Equal(expected, PersianText.Stem(token));

    [Fact]
    public void Sentences_SplitOnPersianPunctuationButNotDecimals()
    {
        var sentences = PersianText.Sentences("صفحه 6.5 اینچ است. باتری قوی دارد؟ بله!");
        Assert.Equal(3, sentences.Count);
        Assert.StartsWith("صفحه 6.5", sentences[0]);
    }

    [Fact]
    public void Truncate_CutsOnWordBoundaryWithEllipsis()
    {
        var result = PersianText.Truncate("این یک جملهٔ نسبتاً طولانی برای آزمایش بریدن متن است", 20);
        Assert.True(result.Length <= 21);
        Assert.EndsWith("…", result);
        Assert.Equal("کوتاه", PersianText.Truncate("کوتاه", 20));
    }

    [Fact]
    public void AnalyzeHtml_FindsHeadingsImagesLinksAndIgnoresScripts()
    {
        var stats = PersianText.AnalyzeHtml(
            "<h2>عنوان</h2><p>متن <a href=\"/product/x\">داخلی</a> و <a href=\"https://other.com\">خارجی</a></p>" +
            "<img src=\"/a.jpg\" alt=\"تصویر\"><script>var x = 'نباید شمرده شود';</script><ul><li>یک</li><li>دو</li></ul>");
        Assert.Single(stats.Headings);
        Assert.Equal(2, stats.Headings[0].Level);
        Assert.Single(stats.Images);
        Assert.Equal(2, stats.Links.Count);
        Assert.True(stats.Links[0].Internal);
        Assert.False(stats.Links[1].Internal);
        Assert.DoesNotContain("نباید", stats.Text);
        Assert.Equal(3, stats.Paragraphs.Count);
    }

    [Fact]
    public void MoneyAndDigits_UsePersianDigits()
    {
        Assert.Equal("۱٬۲۵۰٬۰۰۰", PersianText.Money(1_250_000));
        Assert.Equal("۱۸", PersianText.Digits(18));
        Assert.Equal("الف، ب و ج", PersianText.JoinList(["الف", "ب", "ج"]));
    }

    // ── مدل‌های متنی ────────────────────────────────────────────────────

    [Fact]
    public void SimHash_NearDuplicatesAreCloserThanDifferentTexts()
    {
        var a = "این گوشی صفحه نمایش بزرگ و باتری قوی دارد و برای بازی و تماشای فیلم بسیار مناسب است و دوربین خوبی هم دارد";
        var b = a + " و قیمت مناسبی دارد";
        var c = "کفش ورزشی سبک با زیره لاستیکی برای دویدن روزانه و پیاده روی طولانی در پارک طراحی شده است";
        var near = TextModels.Hamming(TextModels.SimHash(a), TextModels.SimHash(b));
        var far = TextModels.Hamming(TextModels.SimHash(a), TextModels.SimHash(c));
        Assert.True(near < far, $"near={near}, far={far}");
        Assert.Equal(0, TextModels.Hamming(TextModels.SimHash(a), TextModels.SimHash(a)));
    }

    [Fact]
    public void Jaccard_MeasuresOverlap()
    {
        Assert.Equal(1d, TextModels.Jaccard(["a", "b"], ["b", "a"]));
        Assert.Equal(1d / 3, TextModels.Jaccard(["a", "b"], ["b", "c"]), 3);
        Assert.Equal(0d, TextModels.Jaccard(["a"], ["b"]));
    }

    [Fact]
    public void LanguageModel_PrefersNaturalWordOrder()
    {
        var model = new TrigramLanguageModel();
        for (var i = 0; i < 40; i++)
        {
            model.Train("این گوشی با گارانتی معتبر عرضه می‌شود و ارسال آن سریع است.");
            model.Train("این کفش با کیفیت بالا عرضه می‌شود و ارسال آن رایگان است.");
        }
        var natural = model.Fluency("این گوشی با کیفیت بالا عرضه می‌شود.");
        var shuffled = model.Fluency("می‌شود بالا عرضه کیفیت گوشی با این.");
        Assert.True(natural > shuffled, $"natural={natural}, shuffled={shuffled}");
    }

    [Fact]
    public void TfIdf_RanksDistinctiveTermsAboveCommonOnes()
    {
        var tfidf = new TfIdf();
        tfidf.Add("گوشی سامسونگ باتری قوی");
        tfidf.Add("گوشی شیائومی دوربین خوب");
        tfidf.Add("گوشی اپل پردازنده سریع");
        var top = tfidf.Top("گوشی سامسونگ سامسونگ", 2);
        Assert.Equal("سامسونگ", top[0].Stem);
    }

    // ── نمره‌دهی ───────────────────────────────────────────────────────

    [Fact]
    public void Analyzer_EmptyPageScoresLowAndWellWrittenPageScoresHigh()
    {
        var weak = SeoAnalyzer.Analyze(Product(), null, Store);
        var keyword = "گوشی سامسونگ Galaxy A55";
        var strong = SeoAnalyzer.Analyze(Product(
            seoTitle: "گوشی سامسونگ Galaxy A55 با گارانتی ۱۸ ماهه",
            meta: "خرید گوشی سامسونگ Galaxy A55 با حافظهٔ ۲۵۶ گیگابایت و گارانتی ۱۸ ماهه شرکتی؛ مشخصات کامل، قیمت روز و ارسال سریع به سراسر کشور با امکان مرجوعی.",
            keyword: keyword,
            body: LongBody(keyword)), null, Store);

        Assert.InRange(weak.Score, 0, 100);
        Assert.InRange(strong.Score, 0, 100);
        Assert.True(strong.Score > weak.Score + 20, $"weak={weak.Score}, strong={strong.Score}");
        Assert.Contains(weak.Checks, c => c.Id == "keyword-set" && c.Level == SeoCheckLevel.Bad);
        Assert.Contains(strong.Checks, c => c.Id == "keyword-set" && c.Level == SeoCheckLevel.Good);
        Assert.Contains(strong.Checks, c => c.Id == "title-keyword" && c.Level == SeoCheckLevel.Good);
        Assert.True(weak.Problems > strong.Problems);
    }

    [Fact]
    public void Analyzer_FlagsNoIndexAndDuplicateKeywordAcrossSite()
    {
        var first = Product(keyword: "گوشی سامسونگ", noIndex: true);
        var second = Product(keyword: "گوشی سامسونگ");
        var site = SeoSiteIndex.Build([first, second]);

        var report = SeoAnalyzer.Analyze(first, site, Store);

        Assert.Contains(report.Checks, c => c.Id == "noindex" && c.Level == SeoCheckLevel.Info);
        Assert.Contains(report.Checks, c => c.Id == "keyword-unique" && c.Level == SeoCheckLevel.Bad);
        Assert.Equal(SeoFields.FocusKeyword, report.Checks.First(c => c.Id == "keyword-unique").Fix);
    }

    [Fact]
    public void KeywordMatcher_IgnoresSpacingAndArabicLetters()
    {
        Assert.True(KeywordMatcher.Contains("خرید كيف چرمی زنانه", "کیف چرمی"));
        Assert.Equal(2, KeywordMatcher.Count("کیف چرمی خوب. یک کیف چرمی دیگر", "کیف چرمی"));
        Assert.True(KeywordMatcher.FirstPosition("کیف چرمی زنانه مشکی", "کیف چرمی") <= 0.34);
        Assert.True(KeywordMatcher.FirstPosition("خرید", "کیف") < 0);
    }

    // ── کلمهٔ کلیدی و نامک ──────────────────────────────────────────────

    [Fact]
    public void ShortName_DropsSpecificationTail()
    {
        Assert.Equal("گوشی موبایل سامسونگ مدل Galaxy A55", KeywordPlanner.ShortName("گوشی موبایل سامسونگ مدل Galaxy A55 ظرفیت 256 گیگابایت"));
        Assert.Equal("کیف چرمی", KeywordPlanner.ShortName("کیف چرمی"));
    }

    [Fact]
    public void ResearchQuery_KeepsTheUsersOwnKeyword()
    {
        Assert.Equal("گلکسی a55", KeywordPlanner.ResearchQuery(Product(keyword: "گلکسی a55")));
    }

    [Fact]
    public void SlugAdvisor_LeavesPopularOldPagesAndCategoriesAlone()
    {
        var popular = Product(slug: "1234567890", views: 500, createdAt: DateTimeOffset.UtcNow.AddYears(-1));
        Assert.Null(SlugAdvisor.Advise(popular, "گوشی سامسونگ", null, null, DateTimeOffset.UtcNow));

        var category = new SeoSubject { Kind = SeoEntityKind.Category, EntityId = Guid.NewGuid(), Name = "گوشی", Slug = "123456789", PathPrefix = "/category/" };
        Assert.Null(SlugAdvisor.Advise(category, "گوشی", null, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SlugAdvisor_ReplacesMeaninglessSlugOnYoungPageFollowingCompetitors()
    {
        var subject = Product(slug: "4f9c2a7e1b3d5f60", views: 0, createdAt: DateTimeOffset.UtcNow.AddDays(-2));
        var latinCompetitors = new CompetitorInsight { LatinSlugRatio = 0.9, SampleTitles = ["a", "b", "c"] };

        var advice = SlugAdvisor.Advise(subject, "گوشی سامسونگ Galaxy A55", latinCompetitors, null, DateTimeOffset.UtcNow);

        Assert.NotNull(advice);
        Assert.Equal("samsung-galaxy-a55", advice!.Slug);
        Assert.Contains("۳۰۱", advice.Reason);
    }

    [Fact]
    public void SlugAdvisor_MakesSlugUniqueWithinTheSite()
    {
        var subject = Product(slug: "4f9c2a7e1b3d5f60", views: 0, createdAt: DateTimeOffset.UtcNow.AddDays(-2));
        var site = new SeoSiteIndex();
        site.Paths.Add("/product/samsung-galaxy-a55");
        var latin = new CompetitorInsight { LatinSlugRatio = 0.9, SampleTitles = ["a", "b", "c"] };

        var advice = SlugAdvisor.Advise(subject, "گوشی سامسونگ", latin, site, DateTimeOffset.UtcNow);

        Assert.Equal("samsung-galaxy-a55-2", advice?.Slug);
    }

    [Theory]
    [InlineData(0.9d, "گوشی", true)]
    [InlineData(0.1d, "galaxy a55", false)]
    public void PreferLatin_FollowsCompetitorsWhenSignalIsClear(double ratio, string keyword, bool expected) =>
        Assert.Equal(expected, SlugAdvisor.PreferLatin(new CompetitorInsight { LatinSlugRatio = ratio, SampleTitles = ["a", "b", "c"] }, keyword));

    [Fact]
    public void PreferLatin_WithoutResearchFollowsKeywordScript()
    {
        Assert.True(SlugAdvisor.PreferLatin(null, "galaxy a55"));
        Assert.False(SlugAdvisor.PreferLatin(null, "گوشی سامسونگ"));
    }

    // ── نویسندهٔ محتوا ───────────────────────────────────────────────────

    private static WriterContext Writer(SeoSubject subject, string keyword, WriterStyle style = WriterStyle.None, int variant = 0, IReadOnlySet<string>? avoid = null) => new()
    {
        Subject = subject,
        Store = Store,
        Keyword = keyword,
        Style = style,
        Variant = variant,
        Avoid = avoid ?? new HashSet<string>(),
    };

    [Fact]
    public void Writer_TitleFitsGoogleAndStartsWithKeyword()
    {
        var subject = Product();
        var title = ContentWriter.BestTitle(Writer(subject, "گوشی سامسونگ Galaxy A55"));

        Assert.NotNull(title);
        Assert.InRange(title!.Length + Store.TitleSuffixLength, 20, 70);
        Assert.True(KeywordMatcher.Contains(title, "Galaxy A55"), title);
    }

    [Fact]
    public void Writer_IsDeterministicForTheSameVariant()
    {
        var subject = Product(id: Guid.Parse("8f0a7c55-0000-4000-8000-000000000001"));
        Assert.Equal(ContentWriter.BestTitle(Writer(subject, "گوشی سامسونگ")), ContentWriter.BestTitle(Writer(subject, "گوشی سامسونگ")));
        Assert.Equal(ContentWriter.BestMeta(Writer(subject, "گوشی سامسونگ")), ContentWriter.BestMeta(Writer(subject, "گوشی سامسونگ")));
    }

    [Fact]
    public void Writer_LessPromotionalStyleDropsSalesWords()
    {
        var options = ContentWriter.TitleOptions(Writer(Product(), "گوشی سامسونگ", WriterStyle.LessPromotional));
        Assert.NotEmpty(options);
        Assert.DoesNotContain(options, o => o.Contains("خرید"));
    }

    [Fact]
    public void Writer_NeverRepeatsARejectedText()
    {
        var subject = Product();
        var first = ContentWriter.BestTitle(Writer(subject, "گوشی سامسونگ"))!;
        var second = ContentWriter.BestTitle(Writer(subject, "گوشی سامسونگ", avoid: new HashSet<string> { PersianText.Normalize(first) }));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Writer_MetaDescriptionStaysWithinSnippetLength()
    {
        var meta = ContentWriter.BestMeta(Writer(Product(), "گوشی سامسونگ Galaxy A55"));
        Assert.NotNull(meta);
        Assert.InRange(meta!.Length, 50, SeoLimits.MetaMax + 10);
    }

    [Fact]
    public void Writer_FaqUsesOnlyRealFacts()
    {
        var faq = ContentWriter.Faq(Writer(Product(), "گوشی سامسونگ"));
        Assert.InRange(faq.Count, 2, 6);
        Assert.Contains(faq, f => f.Answer.Contains("۱۸ ماهه شرکتی"));       // گارانتی از دادهٔ محصول
        Assert.Contains(faq, f => f.Answer.Contains("256 گیگابایت"));        // ویژگی واقعی
        Assert.Contains(faq, f => f.Answer.Contains("۷ روز"));              // مرجوعی از تنظیمات فروشگاه
        Assert.DoesNotContain(faq, f => f.Question.Contains("پرداخت در محل")); // در تنظیمات خاموش است
    }

    [Fact]
    public void Writer_FaqIsEmptyWhenThereAreNotEnoughFacts()
    {
        var bare = new SeoSubject { Kind = SeoEntityKind.Product, EntityId = Guid.NewGuid(), Name = "محصول آزمایشی", Slug = "x", Product = new ProductFacts() };
        Assert.Empty(ContentWriter.Faq(new WriterContext { Subject = bare, Store = new StoreFacts { SiteName = "x" } }));
    }

    [Fact]
    public void Writer_BlogFaqComesFromQuestionHeadings()
    {
        var items = ContentWriter.QuestionHeadings(
            "<h2>گارانتی چیست؟</h2><p>گارانتی تعهد فروشنده برای تعمیر یا تعویض کالا در مدت مشخص است.</p>" +
            "<h2>بدون سؤال</h2><p>این بخش پرسش نیست و نباید در پرسش‌وپاسخ بیاید.</p>" +
            "<h3>چگونه گارانتی را فعال کنیم</h3><p>کافی است کد فعال‌سازی روی کارت را در سایت شرکت گارانتی ثبت کنید.</p>");
        Assert.Equal(2, items.Count);
        Assert.Equal("گارانتی چیست؟", items[0].Question);
        Assert.StartsWith("گارانتی تعهد", items[0].Answer);
        Assert.Equal("چگونه گارانتی را فعال کنیم؟", items[1].Question); // علامت سؤال اضافه می‌شود

        // فقط یک پرسش → پرسش‌وپاسخ ساخته نمی‌شود (حداقل ۲ مورد).
        Assert.Empty(ContentWriter.QuestionHeadings("<h2>گارانتی چیست؟</h2><p>گارانتی تعهد فروشنده برای تعمیر یا تعویض کالا است.</p>"));
    }

    [Theory]
    [InlineData("خیلی طولانی است", WriterStyle.Shorter)]
    [InlineData("کوتاه‌تر بنویس", WriterStyle.Shorter)]
    [InlineData("خیلی کوتاه است", WriterStyle.Longer)]
    [InlineData("زیادی تبلیغاتی است", WriterStyle.LessPromotional)]
    [InlineData("طولانی و تبلیغاتی", WriterStyle.Shorter | WriterStyle.LessPromotional)]
    [InlineData("", WriterStyle.None)]
    public void StyleFromReason_UnderstandsRejectReasons(string reason, WriterStyle expected) =>
        Assert.Equal(expected, ContentWriter.StyleFromReason(reason));

    [Fact]
    public void WarrantyPhrase_IgnoresNoWarranty()
    {
        Assert.Equal("گارانتی ۱۸ ماهه", ContentWriter.WarrantyPhrase(new ProductFacts { WarrantyMonths = 18 }));
        Assert.Equal("گارانتی اصالت و سلامت فیزیکی", ContentWriter.WarrantyPhrase(new ProductFacts { WarrantyTitle = "گارانتی اصالت و سلامت فیزیکی" }));
        Assert.Null(ContentWriter.WarrantyPhrase(new ProductFacts { WarrantyTitle = "بدون گارانتی" }));
        Assert.Null(ContentWriter.WarrantyPhrase(null));
    }

    // ── پاسخ موتورهای جست‌وجو ───────────────────────────────────────────

    [Fact]
    public void ParseSuggestions_ReadsOpenSearchFormat()
    {
        var items = SeoParsers.ParseSuggestions("""["گوشی سامسونگ",["گوشی سامسونگ a55","گوشی سامسونگ a35","گوشی سامسونگ a55"]]""");
        Assert.Equal(new List<string> { "گوشی سامسونگ a55", "گوشی سامسونگ a35" }, items);
        Assert.Empty(SeoParsers.ParseSuggestions("<html>captcha</html>"));
    }

    [Fact]
    public void ParseBing_ReadsOrganicResults()
    {
        const string html = """
            <ol id="b_results">
              <li class="b_algo"><h2><a href="https://www.digikala.com/product/dkp-1/">گوشی سامسونگ A55 - دیجی‌کالا</a></h2><div class="b_caption"><p>خرید اینترنتی گوشی</p></div></li>
              <li class="b_ad"><h2><a href="https://ads.example.com">تبلیغ</a></h2></li>
              <li class="b_algo"><h2><a href="/relative">نسبی</a></h2></li>
            </ol>
            """;
        var item = Assert.Single(SeoParsers.ParseBing(html));
        Assert.Equal("https://www.digikala.com/product/dkp-1/", item.Url);
        Assert.Equal("خرید اینترنتی گوشی", item.Snippet);
    }

    [Fact]
    public void ParseDuckDuckGo_UnwrapsRedirectsAndSkipsAds()
    {
        const string html = """
            <div class="result results_links result--ad"><a class="result__a" href="https://duckduckgo.com/y.js?ad=1">تبلیغ</a></div>
            <div class="result results_links"><a class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Ftorob.com%2Fp%2Fa55%2F&amp;rut=abc">ترب</a><a class="result__snippet">مقایسه قیمت</a></div>
            """;
        var item = Assert.Single(SeoParsers.ParseDuckDuckGo(html));
        Assert.Equal("https://torob.com/p/a55/", item.Url);
        Assert.Equal("مقایسه قیمت", item.Snippet);
    }

    // ── مدل‌های داده ────────────────────────────────────────────────────

    [Fact]
    public void Faq_ParseDropsEmptyItemsAndUsesShortJsonKeys()
    {
        var json = SeoFaq.Serialize([new SeoFaqItem("سؤال؟", "پاسخ.")]);
        Assert.Contains("\"q\":\"سؤال؟\"", json);
        Assert.Single(SeoFaq.Parse("""[{"q":"سؤال؟","a":"پاسخ."},{"q":"","a":"x"},{"q":"y"}]"""));
        Assert.Empty(SeoFaq.Parse("not json"));
        Assert.Empty(SeoFaq.Parse(null));
    }

    [Fact]
    public void Proposal_FieldsReflectWhatHasAValue()
    {
        var proposal = new SeoProposal { Title = "t", Faq = [new SeoFaqItem("q", "a")] };
        Assert.Equal(SeoFields.Title | SeoFields.Faq, proposal.Fields);
        var roundTrip = SeoProposal.FromJson(proposal.ToJson());
        Assert.Equal("t", roundTrip.Title);
        Assert.Equal(SeoFields.None, SeoProposal.FromJson("{broken").Fields);
    }

    // ── زمان‌بندی و اولویت ───────────────────────────────────────────────

    [Fact]
    public void Schedule_SlotsStartAtTwoAmIranTime()
    {
        var day = new DateOnly(2026, 10, 26);

        var morning = SeoEngineSchedule.CurrentSlot(IranClock.At(day, new TimeOnly(3, 0)), 3);
        Assert.Equal("2026-10-26#3-0", morning.Key);
        Assert.Equal(IranClock.At(day, new TimeOnly(2, 0)), morning.Start);

        // ساعت ۱ بامداد هنوز نوبت سوم «دیروز» (۱۸:۰۰) است.
        var night = SeoEngineSchedule.CurrentSlot(IranClock.At(day, new TimeOnly(1, 0)), 3);
        Assert.Equal("2026-10-25#3-2", night.Key);
        Assert.Equal(IranClock.At(day.AddDays(-1), new TimeOnly(18, 0)), night.Start);

        Assert.Equal(IranClock.At(day, new TimeOnly(10, 0)), SeoEngineSchedule.NextRun(IranClock.At(day, new TimeOnly(3, 0)), 3));
    }

    [Fact]
    public void Schedule_KeyIsStableInsideASlot()
    {
        var day = new DateOnly(2026, 10, 26);
        Assert.Equal(
            SeoEngineSchedule.CurrentSlot(IranClock.At(day, new TimeOnly(2, 1)), 2).Key,
            SeoEngineSchedule.CurrentSlot(IranClock.At(day, new TimeOnly(13, 59)), 2).Key);
        Assert.NotEqual(
            SeoEngineSchedule.CurrentSlot(IranClock.At(day, new TimeOnly(13, 59)), 2).Key,
            SeoEngineSchedule.CurrentSlot(IranClock.At(day, new TimeOnly(14, 1)), 2).Key);
    }

    [Fact]
    public void Priority_FavoursWeakAndPopularPages()
    {
        var quiet = Product(views: 0);
        var popular = Product(views: 5000);
        Assert.True(SeoEngineService.Priority(quiet, 30) > SeoEngineService.Priority(quiet, 80));
        Assert.True(SeoEngineService.Priority(popular, 60) > SeoEngineService.Priority(quiet, 60));
        Assert.Equal(0d, SeoEngineService.Priority(quiet, 100));
    }

    [Fact]
    public void EditUrl_PointsToTheRightAdminPage()
    {
        var id = Guid.NewGuid();
        Assert.Equal($"/admin/products/edit/{id}#seo", SeoEngineService.EditUrl(SeoEntityKind.Product, id));
        Assert.Equal($"/admin/blog/edit/{id}#seo", SeoEngineService.EditUrl(SeoEntityKind.BlogPost, id));
    }

    // ── زیرساخت وب و نمایش ──────────────────────────────────────────────

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("185.143.233.1", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.5", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("::1", false)]
    [InlineData("fd00::1", false)]
    public void WebSource_BlocksPrivateAddresses(string address, bool expected) =>
        Assert.Equal(expected, SeoWebSource.IsPublic(IPAddress.Parse(address)));

    [Fact]
    public void Ui_LabelsAndBadges()
    {
        Assert.Equal("a-badge-green", SeoEngineUi.ScoreBadge(85));
        Assert.Equal("a-badge-yellow", SeoEngineUi.ScoreBadge(50));
        Assert.Equal("a-badge-red", SeoEngineUi.ScoreBadge(49));
        Assert.Equal("عنوان سئو، توضیحات متا", SeoEngineUi.FieldTitle(SeoFields.Title | SeoFields.MetaDescription));
        Assert.Equal("کلمهٔ کلیدی", SeoEngineUi.FieldTitle(SeoFields.FocusKeyword));
        Assert.Equal($"/admin/seo/analyze?kind=Product&id={Guid.Empty}", SeoEngineUi.AnalyzeUrl(SeoEntityKind.Product, Guid.Empty));
    }

    // ── اصلاحیه: گزارش اجرا و کیفیت تحلیل رقبا ──────────────────────────

    [Fact]
    public void RunStats_report_counts_competitor_quality_honestly()
    {
        var stats = new SeoRunStats { Scored = 420, AverageScore = 77.6, Perfect = 120, Queued = 3 };
        stats.Observe(new CompetitorInsight { PagesAnalyzed = 4, Hosts = ["digikala.com", "torob.com"] });
        stats.Observe(new CompetitorInsight { PagesAnalyzed = 3, Hosts = ["digikala.com"] });
        stats.Observe(new CompetitorInsight { Suggestions = ["شامپو کراتینه"] });
        stats.Observe(new CompetitorInsight());
        stats.Observe(null); // تحقیق انجام نشده حساب نمی‌شود
        stats.Gains.AddRange([10, 16]);
        stats.ResearchErrors.Add("نتایج جست‌وجو");

        Assert.Equal(4, stats.Researched);
        Assert.Equal(2, stats.WithCompetitors);
        Assert.Equal(1, stats.SuggestionsOnly);
        Assert.Equal(1, stats.InternalOnly);
        Assert.Equal(13, stats.AverageGain);
        Assert.Equal("digikala.com", stats.TopHosts[0]);

        var report = stats.Report();
        Assert.Contains("۴۲۰ صفحه نمره‌دهی شد", report);
        Assert.Contains("میانگین ۷۸", report);
        Assert.Contains("۲ از ۴ عبارت با صفحهٔ رقیب", report);
        Assert.Contains("میانگین ۳٫۵ صفحه", report);
        Assert.Contains("فقط دادهٔ داخلی", report);
        Assert.Contains("در دسترس نبود: نتایج جست‌وجو", report);
        Assert.True(report.Length <= 1000);
    }

    [Fact]
    public void RunStats_report_explains_empty_queue()
    {
        var report = new SeoRunStats { Scored = 10, AverageScore = 100, Perfect = 10, Queued = 0 }.Report();
        Assert.Contains("صفحه‌ای برای بازنویسی در صف نبود", report);
        Assert.DoesNotContain("تحلیل رقبا", report);
        Assert.Equal("۶٫۴", SeoRunStats.Decimal1(6.43));
        Assert.Equal("۵", SeoRunStats.Decimal1(5.0));
    }

    [Fact]
    public void Competitor_relevance_filters_unrelated_pages()
    {
        var related = new CompetitorPage { Url = "https://shop.example/product/x", Title = "خرید شامپو کراتینه + قیمت" };
        var byUrl = new CompetitorPage { Url = "https://shop.example/product/%D8%B4%D8%A7%D9%85%D9%BE%D9%88-%DA%A9%D8%B1%D8%A7%D8%AA%DB%8C%D9%86%D9%87", Title = "فروشگاه" };
        var unrelated = new CompetitorPage { Url = "https://shop.example/", Title = "صفحهٔ اصلی فروشگاه بزرگ" };

        Assert.True(SeoParsers.Relevance("شامپو کراتینه", related) >= 0.99);
        Assert.True(SeoParsers.Relevance("شامپو کراتینه", byUrl) >= 0.99);  // کلمه‌ها در آدرس صفحه
        Assert.Equal(0d, SeoParsers.Relevance("شامپو کراتینه", unrelated));
        Assert.Equal(1d, SeoParsers.Relevance("", unrelated));                // عبارت خالی فیلتر نمی‌کند
    }
}
