using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Data.Entities.Smart;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Smart;
using Fratak_Shop.Web.Pages.Profile;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// دور ۳۱ – قاعده‌های خالص (بدون پایگاه داده):
/// <list type="bullet">
/// <item>#۱ دستهٔ کالا از صفحهٔ تأمین‌کننده (خواندن مسیر، تمیز کردن، ادغام با شاهد قبلی) و دقیق‌تر کردن دستهٔ کلی از نام کالا.</item>
/// <item>#۸ پیدا کردن قاب دور عکس، سهم شبانهٔ کار تکراری دوربری و اجازهٔ دوربری عکس‌های واترمارک‌دار/صحنه‌دار.</item>
/// <item>#۹ نگهبان محتوای تکراری پیشنهادهای سئو.</item>
/// <item>#۱۰ حراج چندروزه (تاریخ و ساعت شروع/پایان).</item>
/// <item>#۱۲ مقالهٔ آموزشی بدون تبلیغ کالا.</item>
/// <item>#۶ پوشاندن شماره در «رمز فعلی را نمی‌دانم».</item>
/// </list>
/// ترتیب هر بخش: حالت درست ← حالت مرزی ← ورودی بد/حمله.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Round31")]
public class Round31RulesTests
{
    // ───────────── #۱ مسیر دسته در صفحهٔ تأمین‌کننده ─────────────

    private const string Kettle = "کتری برقی پارس خزر مدل KT-2000";

    [Fact]
    public void SupplierPath_ReadsJsonLdBreadcrumb_AndDropsHomeAndTheProductItself()
    {
        const string html = """
            <html><head><script type="application/ld+json">
            {"@context":"https://schema.org","@type":"BreadcrumbList","itemListElement":[
              {"@type":"ListItem","position":1,"name":"خانه"},
              {"@type":"ListItem","position":2,"name":"لوازم آشپزخانه"},
              {"@type":"ListItem","position":3,"name":"کتری و قوری"},
              {"@type":"ListItem","position":4,"name":"کتری برقی پارس خزر مدل KT-2000"}]}
            </script></head><body></body></html>
            """;

        var path = SmartParsers.SupplierCategoryPath(html, Kettle);

        Assert.Equal(new[] { "لوازم آشپزخانه", "کتری و قوری" }, path);
    }

    [Fact]
    public void SupplierPath_FallsBackToTheHtmlBreadcrumb()
    {
        const string html = """
            <nav class="woocommerce-breadcrumb"><a href="/">خانه</a> / <a href="/c/home">لوازم خانگی</a> / <a href="/c/kitchen">آشپزخانه</a> / ماگ سرامیکی طرح گل</nav>
            """;

        var path = SmartParsers.SupplierCategoryPath(html, "ماگ سرامیکی طرح گل");

        Assert.Equal(new[] { "لوازم خانگی", "آشپزخانه" }, path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html><body><p>بدون مسیر</p></body></html>")]
    [InlineData("<script type=\"application/ld+json\">{ broken json </script>")]
    public void SupplierPath_BrokenOrEmptyPages_GiveNothing_NeverThrow(string? html)
    {
        Assert.Empty(SmartParsers.SupplierCategoryPath(html, Kettle));
    }

    [Fact]
    public void CleanSupplierPath_RemovesNoise_ModelCodes_AndDuplicates()
    {
        var clean = SmartParsers.CleanSupplierPath(
            ["خانه", "فروشگاه", "لوازم آشپزخانه", "لوازم آشپزخانه", "کتری KT-2000", null, "  ", Kettle], Kettle);

        Assert.Equal(new[] { "لوازم آشپزخانه" }, clean);
    }

    [Fact]
    public void CleanSupplierPath_EncodedHtmlInsideASegment_IsPlainText()
    {
        var clean = SmartParsers.CleanSupplierPath(["<b>ابزار</b> &amp; یراق", "<script>alert(1)</script>"], "دریل");
        Assert.DoesNotContain(clean, c => c.Contains('<'));
    }

    [Fact]
    public void MergeSource_SupplierWinsUnlessItIsShorterThanAFullPath()
    {
        Assert.Equal("لوازم آشپزخانه > کتری", SupplierCategoryService.MergeSource(null, ["لوازم آشپزخانه", "کتری"]));
        Assert.Equal("لوازم آشپزخانه > کتری", SupplierCategoryService.MergeSource("آشپزخانه", ["لوازم آشپزخانه", "کتری"]));
        Assert.Null(SupplierCategoryService.MergeSource("لوازم خانگی > لوازم آشپزخانه > کتری برقی", ["لوازم خانگی"]));
        Assert.Null(SupplierCategoryService.MergeSource("لوازم خانگی > کتری برقی", []));
    }

    [Theory]
    [InlineData("https://shop.example.ir/product/kettle", true)]
    [InlineData("http://shop.example.ir/p/1", true)]
    [InlineData(" https://shop.example.ir/p/1 ", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("ftp://shop.example.ir/x", false)]
    [InlineData("/product/kettle", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsWebUrl_OnlyAbsoluteHttp(string? url, bool expected)
    {
        Assert.Equal(expected, SupplierCategoryService.IsWebUrl(url));
    }

    // ───────────── #۱ دقیق‌تر کردن دستهٔ کلی ─────────────

    [Fact]
    public void IsCoarse_DetectsGeneralSupplierCategories()
    {
        Assert.True(CategoryRefiner.IsCoarse(["لوازم آشپزخانه"]));
        Assert.True(CategoryRefiner.IsCoarse(["خانه و آشپزخانه", "لوازم آشپزخانه"]));
        Assert.True(CategoryRefiner.IsCoarse(["لوازم خانگی", "کتری و قوری"]));
        Assert.False(CategoryRefiner.IsCoarse(["لوازم خانگی", "کتری برقی"]));
        Assert.False(CategoryRefiner.IsCoarse([]));
    }

    [Fact]
    public void FinerName_TakesTheProductTypeFromTheName()
    {
        Assert.Equal("کتری برقی", CategoryRefiner.FinerName(Kettle, "لوازم آشپزخانه", ["پارس خزر"]));
        Assert.Equal("ماگ سرامیکی", CategoryRefiner.FinerName("ماگ سرامیکی طرح گل", "ظروف"));
    }

    [Fact]
    public void FinerName_RefusesLatinNames_TheCoarseNameItself_AndJunk()
    {
        Assert.Null(CategoryRefiner.FinerName("Xiaomi Mi Band 8", "ساعت هوشمند"));
        Assert.Null(CategoryRefiner.FinerName("لوازم آشپزخانه", "لوازم آشپزخانه"));
        Assert.Null(CategoryRefiner.FinerName("", "ظروف"));
        Assert.Null(CategoryRefiner.FinerName(null, null));
        Assert.Null(CategoryRefiner.FinerName("<script>", "ظروف"));
    }

    // ───────────── #۸ قاب دور عکس و کار تکراری ─────────────

    private static byte[] Solid(int w, int h, (byte R, byte G, byte B) color)
    {
        var rgb = new byte[w * h * 3];
        for (var i = 0; i < w * h; i++)
        {
            rgb[i * 3] = color.R;
            rgb[i * 3 + 1] = color.G;
            rgb[i * 3 + 2] = color.B;
        }
        return rgb;
    }

    private static void Paint(byte[] rgb, int w, int x0, int y0, int x1, int y1, (byte R, byte G, byte B) color)
    {
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var i = (y * w + x) * 3;
                rgb[i] = color.R;
                rgb[i + 1] = color.G;
                rgb[i + 2] = color.B;
            }
        }
    }

    private static readonly (byte, byte, byte) White = (255, 255, 255);
    private static readonly (byte, byte, byte) Navy = (20, 30, 90);

    [Fact]
    public void ImageFrame_FindsTheInnerBoxOfAColouredFrame()
    {
        var rgb = Solid(100, 100, Navy);
        Paint(rgb, 100, 6, 6, 94, 94, White);       // قاب ۶ پیکسلی
        Paint(rgb, 100, 35, 35, 65, 65, (200, 40, 40)); // کالا وسط

        var box = ImageFrame.Inner(rgb, 100, 100);

        Assert.NotNull(box);
        Assert.InRange(box!.Value.Left, 6, 8);
        Assert.InRange(box.Value.Top, 6, 8);
        Assert.InRange(box.Value.Right, 92, 94);
        Assert.InRange(box.Value.Bottom, 92, 94);
    }

    [Fact]
    public void ImageFrame_PlainWhite_AndProductOnWhite_HaveNoFrame()
    {
        Assert.Null(ImageFrame.Inner(Solid(100, 100, White), 100, 100));
        var product = Solid(100, 100, White);
        Paint(product, 100, 30, 20, 70, 80, (40, 40, 40));
        Assert.Null(ImageFrame.Inner(product, 100, 100));
    }

    [Fact]
    public void ImageFrame_ASingleBottomBand_IsNotAFrame()
    {
        var rgb = Solid(100, 100, White);
        Paint(rgb, 100, 0, 90, 100, 100, (120, 120, 120)); // فقط نوار پایین (سایه یا متن شرکت)
        Assert.Null(ImageFrame.Inner(rgb, 100, 100));
    }

    [Fact]
    public void ImageFrame_BadInput_IsNull()
    {
        Assert.Null(ImageFrame.Inner([], 100, 100));
        Assert.Null(ImageFrame.Inner(new byte[10], 100, 100));
        Assert.Null(ImageFrame.Inner(Solid(10, 10, Navy), 10, 10));
    }

    [Fact]
    public void ImageCareBatch_IsTripleAtNight_InIranTime()
    {
        // ۰۳:۰۰ تهران = ۲۳:۳۰ UTC روز قبل؛ ۱۵:۰۰ تهران = ۱۱:۳۰ UTC.
        var night = new DateTimeOffset(2026, 10, 21, 23, 30, 0, TimeSpan.Zero);
        var day = new DateTimeOffset(2026, 10, 22, 11, 30, 0, TimeSpan.Zero);
        Assert.Equal(3, IranClock.ToLocal(night).Hour);
        Assert.Equal(15, IranClock.ToLocal(day).Hour);

        Assert.Equal((ProductImageCare.CleanPerRun * 3, ProductImageCare.CutoutPerRun * 3), ProductImageCare.Batch(night));
        Assert.Equal((ProductImageCare.CleanPerRun, ProductImageCare.CutoutPerRun), ProductImageCare.Batch(day));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(8, true)]    // واترمارک: حالا دوربری امتحان می‌شود
    [InlineData(16, true)]   // صحنه (مثلاً آشپزخانه): حالا دوربری امتحان می‌شود
    [InlineData(24, true)]
    [InlineData(4, false)]   // آدم در عکس: هرگز
    [InlineData(12, false)]
    public void CutoutAllowed_SkipsOnlyPeople(int flags, bool expected)
    {
        Assert.Equal(expected, ProductImageCare.CutoutAllowed((byte)flags));
    }

    // ───────────── #۹ نگهبان محتوای تکراری ─────────────

    private static string LongText(string topic) => "<p>" + string.Join(" ", Enumerable.Range(1, 20).Select(i =>
        $"بند {i} دربارهٔ {topic} توضیح می‌دهد که کیفیت ساخت، دوام، مصرف انرژی و راحتی استفاده چطور سنجیده می‌شود.")) + "</p>";

    [Fact]
    public void DuplicateGuard_DropsTitleMetaBodyAndFaqThatAnotherPageAlreadyHas()
    {
        var self = Guid.NewGuid();
        var other = Guid.NewGuid();
        var site = new SeoSiteIndex();
        const string title = "خرید کتری برقی پارس خزر";
        const string meta = "کتری برقی پارس خزر با ارسال سریع و ضمانت اصالت؛ مشخصات کامل، نظر خریداران و قیمت روز را همین‌جا ببینید و مقایسه کنید.";
        site.Titles[PersianText.Normalize(title)] = [other];
        site.Descriptions[PersianText.Normalize(meta)] = [other];
        var body = LongText("کتری برقی");
        site.BodyFingerprints[other] = TextModels.SimHash(PersianText.HtmlToText(body));
        site.FaqQuestions[PersianText.Normalize("گارانتی کتری چقدر است؟")] = [other];
        site.FaqQuestions[PersianText.Normalize("کتری چند وات است؟")] = [other];

        var proposal = new SeoProposal
        {
            Title = title,
            MetaDescription = meta,
            DescriptionHtml = body,
            FocusKeyword = "کتری برقی",
            Faq = [new("گارانتی کتری چقدر است؟", "۱۸ ماه"), new("کتری چند وات است؟", "۲۲۰۰ وات"), new("رنگ‌ها؟", "سفید")],
        };

        var removed = SeoDuplicateGuard.Prune(proposal, self, site);

        Assert.Equal(SeoFields.Title | SeoFields.MetaDescription | SeoFields.Description | SeoFields.Faq, removed);
        Assert.Null(proposal.Title);
        Assert.Null(proposal.MetaDescription);
        Assert.Null(proposal.DescriptionHtml);
        Assert.Empty(proposal.Faq);
        Assert.Equal("کتری برقی", proposal.FocusKeyword); // کلمهٔ کلیدی موضوع این نگهبان نیست
    }

    [Fact]
    public void DuplicateGuard_KeepsUniqueText_AndThePagesOwnCurrentValues()
    {
        var self = Guid.NewGuid();
        var site = new SeoSiteIndex();
        const string title = "خرید کتری برقی پارس خزر";
        site.Titles[PersianText.Normalize(title)] = [self]; // عنوان فعلی خود همین صفحه
        var body = LongText("کتری");
        site.BodyFingerprints[self] = TextModels.SimHash(PersianText.HtmlToText(body));
        site.FaqQuestions[PersianText.Normalize("سؤال یکتا؟")] = [Guid.NewGuid()];

        var proposal = new SeoProposal
        {
            Title = title,
            MetaDescription = "توضیحات کاملاً یکتا",
            DescriptionHtml = body,
            Faq = [new("سؤال یکتا؟", "پاسخ"), new("سؤال دوم؟", "پاسخ"), new("سؤال سوم؟", "پاسخ")], // فقط ۱ از ۳ تکراری
        };

        Assert.Equal(SeoFields.None, SeoDuplicateGuard.Prune(proposal, self, site));
        Assert.Equal(title, proposal.Title);
        Assert.Equal(3, proposal.Faq.Count);
        Assert.Equal(SeoFields.None, SeoDuplicateGuard.Prune(new SeoProposal { Title = title }, self, null));
    }

    [Fact]
    public void DuplicateGuard_ShortBodies_AreNotFingerprinted()
    {
        var site = new SeoSiteIndex();
        site.BodyFingerprints[Guid.NewGuid()] = TextModels.SimHash("کوتاه");
        Assert.False(SeoDuplicateGuard.IsNearDuplicateBody("<p>کوتاه</p>", Guid.NewGuid(), site));
    }

    [Fact]
    public void SiteIndex_FaqQuestionsCountOnlyOtherPages()
    {
        var site = new SeoSiteIndex();
        var id = Guid.NewGuid();
        site.FaqQuestions[PersianText.Normalize("سؤال قدیمی؟")] = [id];
        Assert.Equal(1, SeoSiteIndex.Others(site.FaqQuestions, "سؤال قدیمی؟", Guid.NewGuid()));
        Assert.Equal(0, SeoSiteIndex.Others(site.FaqQuestions, "سؤال قدیمی؟", id));
    }

    // ───────────── #۱۱ صفحه‌های دارای محتوای تکراری جلوی صف ─────────────

    private static SeoSubject Page(string title, string meta, string? body = null) => new()
    {
        Kind = SeoEntityKind.Product,
        EntityId = Guid.NewGuid(),
        Name = title,
        Slug = "p-" + Guid.NewGuid().ToString("N")[..6],
        PathPrefix = "/product/",
        SeoTitle = title,
        MetaDescription = meta,
        BodyHtml = body,
    };

    [Fact]
    public void Conflicts_FindsTitleMetaAndBodyThatAnotherPageShares()
    {
        var body = LongText("کتری برقی");
        var a = Page("خرید کتری برقی", "توضیحات مشترک کتری برقی با ارسال سریع", body);
        var b = Page("خرید کتری برقی", "توضیحات مشترک کتری برقی با ارسال سریع", body);
        var other = "<p>" + string.Join(" ", Enumerable.Range(1, 15).Select(i =>
            $"ماگ شمارهٔ {i} از سرامیک لعاب‌دار ساخته شده، در ماشین ظرف‌شویی سالم می‌ماند و برای چای صبحگاهی یا هدیهٔ تولد انتخاب خوبی است.")) + "</p>";
        var unique = Page("خرید ماگ سرامیکی", "ماگ سرامیکی طرح گل برای هدیه", other);
        var site = SeoSiteIndex.Build([a, b, unique]);

        Assert.Equal(SeoFields.Title | SeoFields.MetaDescription | SeoFields.Description, SeoDuplicateGuard.Conflicts(a, site));
        Assert.Equal(SeoFields.None, SeoDuplicateGuard.Conflicts(unique, site));
    }

    [Fact]
    public void Conflicts_ASinglePageNeverConflictsWithItself()
    {
        var only = Page("خرید کتری برقی", "توضیحات کتری", LongText("کتری"));
        Assert.Equal(SeoFields.None, SeoDuplicateGuard.Conflicts(only, SeoSiteIndex.Build([only])));
    }

    [Fact]
    public void Priority_PutsDuplicatePagesFirst_AtTheSameScore()
    {
        var page = Page("خرید کتری برقی", "توضیحات");
        var plain = SeoEngineService.Priority(page, 60);
        var duplicate = SeoEngineService.Priority(page, 60, SeoFields.MetaDescription);
        Assert.True(duplicate > plain);
        Assert.Equal(plain, SeoEngineService.Priority(page, 60, SeoFields.None));
        Assert.Equal(0, SeoEngineService.Priority(page, 100, SeoFields.Title)); // صفحهٔ کامل همچنان در صف نیست
    }

    [Fact]
    public void RunReport_MentionsDuplicatePages_OnlyWhenThereAreAny()
    {
        Assert.Contains("۳ صفحه محتوای تکراری", new SeoRunStats { Scored = 10, Queued = 3, Duplicates = 3 }.Report(), StringComparison.Ordinal);
        Assert.DoesNotContain("تکراری", new SeoRunStats { Scored = 10, Queued = 0 }.Report(), StringComparison.Ordinal);
    }

    // ───────────── #۱۰ حراج چندروزه ─────────────

    [Fact]
    public void MultiDayDiscount_StartAndEndKeepTheirOwnDatesAndTimes()
    {
        var start = PersianDateInput.ParseDateTime("1405/08/01 18:30");
        var end = PersianDateInput.ParseDateTime("1405/08/04 18:30", new TimeOnly(23, 59));

        Assert.NotNull(start);
        Assert.NotNull(end);
        Assert.Equal(TimeSpan.FromDays(3), end!.Value - start!.Value);
        Assert.Equal("1405/08/04 18:30", PersianDateInput.FormatDateTime(end));
    }

    [Fact]
    public void MultiDayDiscount_EndWithoutTime_RunsToTheEndOfThatDay()
    {
        var start = PersianDateInput.ParseDateTime("1405/08/01 00:00");
        var end = PersianDateInput.ParseDateTime("1405/08/03", new TimeOnly(23, 59));

        Assert.Equal(new TimeSpan(2, 23, 59, 0), end!.Value - start!.Value);
    }

    [Fact]
    public void MultiDayDiscount_SpanningMonthsAndYears_IsExact()
    {
        var start = PersianDateInput.ParseDateTime("1405/12/28 12:00");
        var end = PersianDateInput.ParseDateTime("1406/01/03 12:00");
        Assert.NotNull(start);
        Assert.NotNull(end);
        Assert.True(end > start);
        Assert.InRange((end!.Value - start!.Value).TotalDays, 4, 6.1); // اسفند ۲۹ یا ۳۰ روزه
    }

    // ───────────── #۶ پوشاندن شماره ─────────────

    [Theory]
    [InlineData("09121234567", "0912***4567")]
    [InlineData(" 09121234567 ", "0912***4567")]
    [InlineData("0912", "0912")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Mask_HidesTheMiddleOfThePhone(string? phone, string expected)
    {
        Assert.Equal(expected, PasswordModel.Mask(phone));
    }

    // ───────────── #۱۲ مقالهٔ آموزشی بدون تبلیغ ─────────────

    private static readonly DateTimeOffset BlogNow = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    private static List<BlogProduct> Headphones() =>
    [
        new(Guid.NewGuid(), "هدفون بی سیم سونی مدل A1", "sony-a1", "سونی", 3_000_000, true, 4.6, 12, 30,
            [new SeoFeature("عمر باتری", "30 ساعت"), new SeoFeature("مقاومت در برابر آب", "IPX4")], ["صدای شفاف"], [], ["ورزشکاران"]),
        new(Guid.NewGuid(), "هدفون بی سیم انکر مدل B2", "anker-b2", "انکر", 1_800_000, true, 4.2, 8, 20,
            [new SeoFeature("عمر باتری", "20 ساعت")], ["قیمت مناسب"], [], []),
    ];

    private static BlogArticle CareArticle(int variant = 0) => BlogWriter.Write(new BlogBrief(BlogArticleType.HowTo,
        "نکات نگهداری و استفادهٔ درست از هدفون", "نگهداری هدفون", "هدفون", "headphones", Headphones(), BlogNow, Variant: variant));

    [Fact]
    public void CareGuide_HasNoProductCards_PricesOrSalesPitch()
    {
        for (var variant = 0; variant < 4; variant++)
        {
            var article = CareArticle(variant);
            Assert.True(article.Educational);
            Assert.Empty(article.ProductSlugs);
            Assert.DoesNotContain("[[product:", article.Html, StringComparison.Ordinal);
            Assert.DoesNotContain("[[price:", article.Html, StringComparison.Ordinal);
            Assert.DoesNotContain("تومان", article.Html, StringComparison.Ordinal);
            Assert.Null(BlogWriter.SalesPitch(PersianText.HtmlToText(article.Html)));
        }
    }

    [Fact]
    public void CareGuide_OnlyMentionsTheCategoryGently_WithALink()
    {
        var article = CareArticle();
        Assert.Contains("href=\"/category/headphones\"", article.Html, StringComparison.Ordinal);
        Assert.Contains("توضیحات", article.Html, StringComparison.Ordinal);
        // فقط یک لینک به فروشگاه (همان دسته).
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(article.Html, "href=\"/category/"));
    }

    [Fact]
    public void CareGuide_IsLongAndCoversTheRealTraitsOfTheCategory()
    {
        var article = CareArticle();
        var words = PersianText.WordCount(PersianText.HtmlToText(article.Html));
        Assert.True(words >= 700, $"{words} words");
        Assert.True(System.Text.RegularExpressions.Regex.Matches(article.Html, "<h2").Count >= 8);
        Assert.Contains("باتری", article.Html, StringComparison.Ordinal);       // مشخصهٔ «عمر باتری»
        Assert.Contains("مقاومت در برابر آب", article.Html, StringComparison.Ordinal);
        Assert.Contains("<ol>", article.Html, StringComparison.Ordinal);         // تمیزکاری قدم‌به‌قدم
        Assert.Contains("راهنما", article.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void CareGuide_UnsafeCategorySlug_GetsNoLink()
    {
        var article = BlogWriter.Write(new BlogBrief(BlogArticleType.HowTo, "نگهداری هدفون", "نگهداری هدفون", "هدفون",
            "javascript:alert(1)", Headphones(), BlogNow));
        Assert.DoesNotContain("javascript:", article.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"/category/", article.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void QualityGate_DoesNotDemandCardsFromEducationalArticles_ButBlocksAdsInThem()
    {
        var article = CareArticle();
        var report = BlogWriter.Check(article, [], []);
        Assert.DoesNotContain(report.Problems, p => p.Contains("کارت محصول", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Problems, p => p.Contains("تبلیغی", StringComparison.Ordinal));

        var pushy = article with { Html = article.Html + "<p>برای نتیجهٔ بهتر یک تایمر بخرید.</p>" };
        Assert.Contains(BlogWriter.Check(pushy, [], []).Problems, p => p.Contains("تبلیغی", StringComparison.Ordinal));

        var withCard = article with { Html = article.Html + "<p>[[product:sony-a1]]</p>" };
        Assert.Contains(BlogWriter.Check(withCard, [], []).Problems, p => p.Contains("کارت محصول", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("برای نتیجهٔ بهتر یک تایمر بخرید.", "بخرید")]
    [InlineData("همین حالا سفارش دهید", "سفارش دهید")]
    [InlineData("دستمال میکروفایبر را مرطوب کنید.", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void SalesPitch_FindsPurchaseCalls(string? text, string? expected)
    {
        Assert.Equal(expected, BlogWriter.SalesPitch(text));
    }

    [Fact]
    public void CommercialArticles_StillHaveProductCards()
    {
        var article = BlogWriter.Write(new BlogBrief(BlogArticleType.BuyingGuide, "راهنمای خرید هدفون", "خرید هدفون", "هدفون", "headphones",
            Headphones(), BlogNow));
        Assert.False(article.Educational);
        Assert.Contains("[[product:", article.Html, StringComparison.Ordinal);
    }
}
