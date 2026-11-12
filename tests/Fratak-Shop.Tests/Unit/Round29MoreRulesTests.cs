using Fratak_Shop.Core.Services.Design;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Web.Areas.Admin.Pages.Content;
using Fratak_Shop.Web.Infrastructure.Commerce;
using Fratak_Shop.Web.Infrastructure.Marketing;
using Fratak_Shop.Web.Infrastructure.PageCache;
using Fratak_Shop.Web.Infrastructure.Smart;
using SkiaSharp;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// دور ۲۹ (بخش دوم آزمون‌های خالص، بدون پایگاه داده و شبکه):
/// <list type="bullet">
/// <item>#۱۸ ویدیوی پیشنهادی: امتیاز تطبیق عنوان، خواندن آپارات/یوتیوب/صفحهٔ تأمین‌کننده، انتخاب نهایی.</item>
/// <item>#۱۷ سربرگ دسته: کلید، نشان تخفیف واقعی، تنوع چیدمان.</item>
/// <item>#۲۷ منوی هدر، #۱۱ قاب ایزولهٔ کد کمپین، #۱۰ دستهٔ ناجور، #۵ رتبهٔ زمینه و بوم یکسان، #۱۶ پنجرهٔ چرخش بنر.</item>
/// </list>
/// ترتیب هر گروه: حالت درست ← مرزی ← خراب/حمله.
/// </summary>
[Trait("Category", "Unit")]
public class Round29MoreRulesTests
{
    // ───────────── #۱۸ امتیاز تطبیق عنوان ویدیو ─────────────

    private const string BudsName = "هندزفری بلوتوثی شیائومی مدل Redmi Buds 4";
    private const string BudsEnglish = "Xiaomi Redmi Buds 4";

    [Fact]
    public void VideoScore_ExactModel_IsFull()
    {
        Assert.Equal(100, VideoSuggestionRules.Score(BudsName, BudsEnglish, "معرفی و بررسی Xiaomi Redmi Buds 4"));
    }

    [Fact]
    public void VideoScore_OtherEdition_IsBelowThreshold()
    {
        // «Pro» نسخهٔ دیگری است ← کمتر از حد پیشنهاد.
        var score = VideoSuggestionRules.Score(BudsName, BudsEnglish, "انباکس Xiaomi Redmi Buds 4 Pro");
        Assert.True(score < VideoSuggestionRules.MinScore, $"score={score}");
        Assert.True(score > 0);
    }

    [Fact]
    public void VideoScore_OtherModelNumber_IsVeryLow()
    {
        var score = VideoSuggestionRules.Score(BudsName, BudsEnglish, "Redmi Buds 5 Pro review");
        Assert.True(score < 30, $"score={score}");
    }

    [Fact]
    public void VideoScore_PersianOnlyName_UsesPersianWords()
    {
        Assert.Equal(100, VideoSuggestionRules.Score("پاوربانک ظرفیت 20000", null, "معرفی پاوربانک ظرفیت 20000"));
        // مدل (عدد) نیامده ← نصف.
        Assert.True(VideoSuggestionRules.Score("پاوربانک ظرفیت 20000", null, "معرفی پاوربانک ظرفیت بالا") < VideoSuggestionRules.MinScore);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void VideoScore_EmptyTitle_IsZero(string? title)
    {
        Assert.Equal(0, VideoSuggestionRules.Score(BudsName, BudsEnglish, title));
    }

    [Fact]
    public void VideoScore_EmptyProduct_IsZero()
    {
        Assert.Equal(0, VideoSuggestionRules.Score(null, null, "Redmi Buds 4"));
    }

    // ───────────── #۱۸ خواندن منابع ─────────────

    [Fact]
    public void FromAparat_ReadsValidItems_AndSkipsUnsafeIds()
    {
        const string json = """
            {"videobysearch":[
              {"id":"1","title":"معرفی Redmi Buds 4","uid":"abc123","small_poster":"https://static.cdn.asset.aparat.com/a.jpg"},
              {"id":"2","title":"بد","uid":"bad uid/../x"},
              {"id":"3","title":"بدون شناسه"},
              {"id":"4","title":"پوستر ناامن","uid":"zz99","small_poster":"http://insecure.example/a.jpg"}
            ],"ui":{}}
            """;
        var hits = VideoSuggestionRules.FromAparat(json);
        Assert.Equal(2, hits.Count);
        Assert.Equal("https://www.aparat.com/v/abc123", hits[0].PageUrl);
        Assert.Equal("aparat", hits[0].Source);
        Assert.Equal("https://static.cdn.asset.aparat.com/a.jpg", hits[0].ThumbnailUrl);
        Assert.Null(hits[1].ThumbnailUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"videobysearch\":\"x\"}")]
    [InlineData("{\"videobysearch\":[1,\"x\",null,[]]}")]
    [InlineData("\"text\"")]
    public void FromAparat_BrokenJson_IsEmpty(string? json)
    {
        Assert.Empty(VideoSuggestionRules.FromAparat(json));
    }

    [Fact]
    public void FromYouTube_ReadsIdAndDecodedTitle()
    {
        const string html = """
            var ytInitialData = {"contents":[{"videoRenderer":{"videoId":"dQw4w9WgXcQ","thumbnail":{"thumbnails":[]},"title":{"runs":[{"text":"Redmi Buds 4 review \u0026 test"}]}}}]};
            """;
        var hit = Assert.Single(VideoSuggestionRules.FromYouTube(html));
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", hit.PageUrl);
        Assert.Equal("Redmi Buds 4 review & test", hit.Title);
        Assert.Equal("youtube", hit.Source);
    }

    [Fact]
    public void FromSupplierPage_FindsEmbedsAndFiles_AndRejectsScripts()
    {
        const string html = """
            <iframe src="https://www.aparat.com/video/video/embed/videohash/Xy12z/vt/frame"></iframe>
            <iframe src="https://www.youtube.com/embed/dQw4w9WgXcQ"></iframe>
            <video controls><source src="/media/demo.mp4" type="video/mp4"></video>
            <video src="javascript:alert(1).mp4"></video>
            <iframe src="https://www.aparat.com/v/Xy12z"></iframe>
            """;
        var hits = VideoSuggestionRules.FromSupplierPage(html, "https://shop.example.com/p/1");
        Assert.Equal(3, hits.Count);
        Assert.Contains(hits, h => h.PageUrl == "https://www.aparat.com/v/Xy12z");
        Assert.Contains(hits, h => h.PageUrl == "https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        Assert.Contains(hits, h => h.PageUrl == "https://shop.example.com/media/demo.mp4");
        Assert.All(hits, h => Assert.Equal(VideoSuggestionRules.SupplierScore, h.Score));
        Assert.DoesNotContain(hits, h => h.PageUrl.Contains("javascript", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FromSupplierPage_Empty_IsEmpty()
    {
        Assert.Empty(VideoSuggestionRules.FromSupplierPage(null, null));
        Assert.Empty(VideoSuggestionRules.FromSupplierPage("<p>بدون ویدیو</p>", "https://shop.example.com"));
    }

    [Fact]
    public void Rank_KeepsSupplierAndExactMatches_DropsOtherEditionsAndDuplicates()
    {
        var hits = new List<VideoHit>
        {
            new("supplier", "https://www.aparat.com/v/sup1", null, null, VideoSuggestionRules.SupplierScore),
            new("aparat", "https://www.aparat.com/v/good1", "معرفی Xiaomi Redmi Buds 4", null),
            new("aparat", "https://www.aparat.com/v/GOOD1", "تکراری Xiaomi Redmi Buds 4", null),
            new("aparat", "https://www.aparat.com/v/pro1", "Xiaomi Redmi Buds 4 Pro", null),
            new("youtube", "https://www.youtube.com/watch?v=aaaaaaaaaaa", "آموزش آشپزی", null),
        };
        var ranked = VideoSuggestionRules.Rank(hits, BudsName, BudsEnglish);
        Assert.Equal(2, ranked.Count);
        Assert.Equal("https://www.aparat.com/v/good1", ranked[0].PageUrl);
        Assert.Equal(100, ranked[0].Score);
        Assert.Equal("supplier", ranked[1].Source);
    }

    [Fact]
    public void Query_PrefersEnglishName_AndIsCapped()
    {
        Assert.Equal(BudsEnglish, VideoSuggestionRules.Query("هندزفری", BudsEnglish));
        Assert.Equal("هندزفری", VideoSuggestionRules.Query(" هندزفری ", "  "));
        Assert.Equal(80, VideoSuggestionRules.Query(new string('a', 200), null).Length);
    }

    [Theory]
    [InlineData("https://www.aparat.com/v/i7228or", "i7228or")]
    [InlineData("https://evil.example/v/i7228or", null)]
    [InlineData("https://www.aparat.com/v/a/../b", null)]
    [InlineData(null, null)]
    public void AparatHash_OnlyFromWatchPage(string? url, string? expected)
    {
        Assert.Equal(expected, VideoSuggestionRules.AparatHash(url));
    }

    // ───────────── #۱۷ سربرگ دسته ─────────────

    private static IdeaProduct Item(string name, string? image, decimal price, decimal final) =>
        new(Guid.NewGuid(), name, "p-" + Guid.NewGuid().ToString("N")[..6], image, 800, 800, price, final, 5, 10, 2, "آشپزخانه", "kitchen",
            DateTimeOffset.UtcNow.AddDays(-3));

    [Fact]
    public void CategoryHeader_RealDiscount_ShowsBadgeAndLinksToCategory()
    {
        var category = new IdeaCategory(Guid.NewGuid(), "لوازم آشپزخانه", "kitchen", 40, null);
        var now = new DateTimeOffset(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);
        var idea = DesignIdeas.CategoryHeader(category,
            [Item("قابلمه", "/uploads/products/a.webp", 1_000_000, 800_000), Item("تابه", "/uploads/products/b.webp", 500_000, 500_000)], 0, now);

        Assert.StartsWith($"{DesignIdeas.CategoryHeaderPrefix}:{category.Id:N}:", idea.Key, StringComparison.Ordinal);
        Assert.NotNull(idea.Spec.Badge);
        Assert.Contains("۲۰٪", idea.Spec.Badge, StringComparison.Ordinal);
        Assert.StartsWith("/category/kitchen", idea.LinkUrl, StringComparison.Ordinal);
        Assert.Equal(now.AddDays(45), idea.ExpiresAt);
    }

    [Fact]
    public void CategoryHeader_SmallDiscount_HasNoBadge()
    {
        var category = new IdeaCategory(Guid.NewGuid(), "لوازم آشپزخانه", "kitchen", 3, null);
        var idea = DesignIdeas.CategoryHeader(category, [Item("قابلمه", "/uploads/products/a.webp", 1_000_000, 950_000)], 1, DateTimeOffset.UtcNow);
        Assert.Null(idea.Spec.Badge);
        Assert.NotEqual("sale", idea.Spec.Layout);
    }

    [Fact]
    public void CategoryHeader_NoImages_UsesPattern()
    {
        var category = new IdeaCategory(Guid.NewGuid(), "ابزار", "tools", 0, null);
        var idea = DesignIdeas.CategoryHeader(category, [], 2, DateTimeOffset.UtcNow);
        Assert.Equal("pattern", idea.Spec.Layout);
        Assert.Null(idea.Spec.Gallery);
        Assert.Null(idea.Spec.Badge);
    }

    [Fact]
    public void CategoryHeaderLayout_NeighboursDiffer_AndRespectImages()
    {
        Assert.NotEqual(DesignIdeas.CategoryHeaderLayout("seed", 0, 3, false), DesignIdeas.CategoryHeaderLayout("seed", 1, 3, false));
        Assert.Equal("pattern", DesignIdeas.CategoryHeaderLayout("seed", 5, 0, true));
        Assert.DoesNotContain(Enumerable.Range(0, 10).Select(i => DesignIdeas.CategoryHeaderLayout("x", i, 1, false)), l => l == "collage");
    }

    // ───────────── #۲۷ منوی هدر ─────────────

    [Fact]
    public void HeaderMenuClean_ValidLink_IsTrimmed()
    {
        var input = HeaderMenuRules.Clean("  پرفروش  ", "/search?sort=BestSelling", " جدید ", false);
        Assert.Null(input.Error);
        Assert.Equal("پرفروش", input.Title);
        Assert.Equal("جدید", input.Badge);
    }

    [Fact]
    public void HeaderMenuClean_MegaMenuMayHaveNoUrl()
    {
        var input = HeaderMenuRules.Clean("فروشگاه", null, null, true);
        Assert.Null(input.Error);
        Assert.Null(input.Url);
    }

    [Theory]
    [InlineData("x", "/a", null)]
    [InlineData("لینک", null, null)]
    [InlineData("لینک", "javascript:alert(1)", null)]
    [InlineData("لینک", "/a b", null)]
    [InlineData("لینک", "/a", "برچسب‌خیلی‌بلند")]
    public void HeaderMenuClean_Invalid_ReturnsError(string title, string? url, string? badge)
    {
        var input = HeaderMenuRules.Clean(title, url, badge, false);
        Assert.NotNull(input.Error);
        Assert.Null(input.Title);
    }

    [Fact]
    public void HeaderMenuMove_SwapsAndStopsAtEdges()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        Assert.Equal(new[] { b, a, c }, HeaderMenuRules.Move([a, b, c], b, -1));
        Assert.Equal(new[] { a, b, c }, HeaderMenuRules.Move([a, b, c], a, -1));
        Assert.Equal(new[] { a, b, c }, HeaderMenuRules.Move([a, b, c], c, 1));
        Assert.Equal(new[] { a, b, c }, HeaderMenuRules.Move([a, b, c], Guid.NewGuid(), 1));
    }

    [Fact]
    public void HeaderMenuPresets_AreInternalAndWithinLimit()
    {
        Assert.True(HeaderMenuRules.Presets.Count <= HeaderMenuRules.MaxItems);
        Assert.All(HeaderMenuRules.Presets, p => Assert.StartsWith("/", p.Url, StringComparison.Ordinal));
        Assert.Contains(HeaderMenuRules.Presets, p => p.Key == "discounted");
    }

    // ───────────── #۱۱ قاب ایزولهٔ کد کمپین ─────────────

    [Fact]
    public void CampaignFrameDocument_EscapesClosingTags_AndTargetsTop()
    {
        var doc = CampaignFrameEndpoint.Document("<x>", "<p>سلام</p>", "a{}</style><b>", "var s='</script><i>';");
        Assert.Contains("<base target=\"_top\">", doc, StringComparison.Ordinal);
        Assert.Contains("&lt;x&gt;", doc, StringComparison.Ordinal);
        Assert.DoesNotContain("</style><b>", doc, StringComparison.Ordinal);
        Assert.DoesNotContain("</script><i>", doc, StringComparison.Ordinal);
        Assert.Contains("<p>سلام</p>", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void CampaignFramePolicy_IsSandboxedWithoutSameOrigin()
    {
        var policy = CampaignFrameEndpoint.Policy("https://shop.fratak.ir");
        Assert.Contains("sandbox allow-scripts", policy, StringComparison.Ordinal);
        Assert.Contains("form-action 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'self'", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("allow-same-origin", policy, StringComparison.Ordinal);
    }

    // ───────────── #۱۰ دستهٔ ناجور ─────────────

    [Fact]
    public void CategoryAudit_KitchenwareInPowerStrip_IsMisplaced()
    {
        Assert.True(CategoryAudit.IsMisplaced("ظرف شیش پارچه پیرکس", "چندراهی برق", "power-strip", ["چندراهی برق 6 خانه", "سه راهی برق کابل دار"]));
    }

    [Fact]
    public void CategoryAudit_MatchingOrGenericCategory_IsNotMisplaced()
    {
        Assert.False(CategoryAudit.IsMisplaced("چندراهی برق ارت دار", "چندراهی برق", "power-strip", ["چندراهی برق 6 خانه", "سه راهی برق کابل دار"]));
        Assert.False(CategoryAudit.IsMisplaced("کابل شارژ تایپ سی", "لوازم جانبی موبایل", "mobile-accessories", ["قاب گوشی", "کابل شارژ آیفون"]));
        Assert.False(CategoryAudit.IsMisplaced("", "چندراهی برق", "power-strip", []));
    }

    [Fact]
    public void ProductTypes_KitchenwareAndPowerBank_AreNotConfused()
    {
        Assert.Equal("kitchenware", ProductTypes.Detect("ظرف شیش پارچه")?.Key);
        Assert.Equal("power-bank", ProductTypes.Detect("پاوربانک ظرفیت 20000")?.Key);
    }

    // ───────────── #۵ رتبهٔ زمینه و بوم یکسان ─────────────

    [Theory]
    [InlineData(720, 870, 989)]
    [InlineData(200, 100, 800)]
    [InlineData(2000, 500, 1400)]
    public void UniformSide_IsClamped(int width, int height, int expected)
    {
        Assert.Equal(expected, ProductImageCare.UniformSide(width, height));
    }

    [Fact]
    public void Tier_PrefersCutoutThenWhite_AndRejectsProblems()
    {
        Assert.Equal(4, ImageCleanup.Tier(64));
        Assert.Equal(3, ImageCleanup.Tier(1));
        Assert.Equal(2, ImageCleanup.Tier(2));
        Assert.Equal(1, ImageCleanup.Tier(null));
        Assert.Equal(1, ImageCleanup.Tier(0));
        Assert.Equal(0, ImageCleanup.Tier(64 | 8));
        Assert.Equal(0, ImageCleanup.Tier(1 | 4));
    }

    // ───────────── #۱۶ پنجرهٔ چرخش بنر ─────────────

    [Fact]
    public void RotationWindow_ChangesEveryRotateHours()
    {
        var at = (int hours) => DateTimeOffset.FromUnixTimeSeconds(hours * 3600L);
        Assert.Equal(0, DiscountBoundaryWatcher.RotationWindow(at(5), 6));
        Assert.Equal(1, DiscountBoundaryWatcher.RotationWindow(at(7), 6));
        // صفر/منفی ← هر ساعت.
        Assert.Equal(7, DiscountBoundaryWatcher.RotationWindow(at(7), 0));
    }
    // ───────────── #۱ جست‌وجوی دقیق‌تر با عکس ─────────────

    private static ImageSearchService.ImageCandidate Cand(double score, Guid category, string name) => new(Guid.NewGuid(), score, category, name);

    [Fact]
    public void ImageDecide_HintLiftsMatchingNames()
    {
        var cat = Guid.NewGuid();
        var teapot = Cand(0.50, cat, "قوری چینی گل دار");
        var mug = Cand(0.52, cat, "ماگ سرامیکی");
        // بدون متن: هر دو زیر حد اصلی ← «شاید»، ماگ اول.
        var (plain, approx) = ImageSearchService.Decide([teapot, mug], []);
        Assert.True(approx);
        Assert.Equal(mug.ProductId, plain[0]);
        // با متن «قوری»: قوری از حد اصلی می‌گذرد و اول می‌آید.
        var (hinted, approx2) = ImageSearchService.Decide([teapot, mug], ImageSearchService.HintTokens("خرید قوری"));
        Assert.False(approx2);
        Assert.Equal(teapot.ProductId, Assert.Single(hinted));
    }

    [Fact]
    public void ImageDecide_ConfidentBestDropsOtherCategories()
    {
        Guid kitchen = Guid.NewGuid(), toys = Guid.NewGuid();
        var best = Cand(0.80, kitchen, "قوری");
        var sameCategory = Cand(0.60, kitchen, "کتری");
        var otherCategory = Cand(0.62, toys, "عروسک");
        var closeOther = Cand(0.75, toys, "اسباب بازی");
        var (ids, approx) = ImageSearchService.Decide([best, sameCategory, otherCategory, closeOther], []);
        Assert.False(approx);
        Assert.Equal(best.ProductId, ids[0]);
        Assert.Contains(sameCategory.ProductId, ids);
        Assert.Contains(closeOther.ProductId, ids);
        Assert.DoesNotContain(otherCategory.ProductId, ids);
    }

    [Fact]
    public void ImageDecide_NothingRelevant_IsEmpty()
    {
        var (ids, approx) = ImageSearchService.Decide([Cand(0.40, Guid.NewGuid(), "x")], []);
        Assert.Empty(ids);
        Assert.False(approx);
        Assert.Empty(ImageSearchService.Decide([], ImageSearchService.HintTokens("قوری")).Ids);
    }

    [Theory]
    [InlineData("خرید قوری چینی", 2)]
    [InlineData("  ", 0)]
    [InlineData(null, 0)]
    [InlineData("قیمت و خرید", 0)]
    public void HintTokens_DropGenericWords(string? hint, int expected)
    {
        Assert.Equal(expected, ImageSearchService.HintTokens(hint).Count);
    }

    [Fact]
    public void HintMatches_IsNormalized()
    {
        Assert.True(ImageSearchService.HintMatches("قوري چيني", ImageSearchService.HintTokens("قوری")));
        Assert.False(ImageSearchService.HintMatches("ماگ", ImageSearchService.HintTokens("قوری")));
        Assert.False(ImageSearchService.HintMatches(null, ImageSearchService.HintTokens("قوری")));
    }

    [Fact]
    public void ImageScore_ColorOnlyMatch_IsCapped()
    {
        var colors = Enumerable.Repeat((byte)30, ImageSignature.Bins).ToArray();
        var query = new ImageSignature.Signature(0L, colors, 0x00FF00FF00FF00FFL);
        // همان رنگ و همان pHash، ولی dHash کاملاً برعکس ← بدون سقف ۰٫۷۴ می‌شد؛ حالا «فقط هم‌رنگ» حساب می‌شود.
        var stored = new ImageSignature.Stored(Guid.NewGuid(), new[] { (-1L, 0x00FF00FF00FF00FFL) }, colors);
        Assert.True(ImageSignature.Score(new[] { query }, stored) <= ImageSignature.ColorOnlyCap);
        var same = stored with { Shapes = new[] { (0L, 0x00FF00FF00FF00FFL) } };
        Assert.True(ImageSignature.Score(new[] { query }, same) > 0.95);
    }

    /// <summary>عکس آزمایشی: مربع رنگی وسط زمینهٔ یکدست (مثل کالا روی میز).</summary>
    private static SKBitmap Scene(SKColor background, SKColor subject, int size = 200)
    {
        var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                bitmap.SetPixel(x, y, x >= size * 0.35 && x < size * 0.65 && y >= size * 0.3 && y < size * 0.7 ? subject : background);
        return bitmap;
    }

    [Fact]
    public void SubjectBox_FindsTheObjectOnAPlainTable()
    {
        using var scene = Scene(new SKColor(150, 110, 70), new SKColor(20, 60, 200));
        var box = ImageSignature.SubjectBox(scene);
        Assert.NotNull(box);
        Assert.InRange(box.Value.Left, 50, 75);
        Assert.InRange(box.Value.Right, 125, 150);
        Assert.InRange(box.Value.Top, 40, 65);
        Assert.InRange(box.Value.Bottom, 135, 160);

        using var isolated = ImageSignature.Isolate(scene);
        Assert.NotNull(isolated);
        // گوشهٔ برش (زمینه) شفاف شده و وسط (کالا) مانده است.
        Assert.Equal(0, isolated.GetPixel(0, 0).Alpha);
        Assert.Equal(255, isolated.GetPixel(isolated.Width / 2, isolated.Height / 2).Alpha);
    }

    [Fact]
    public void SubjectBox_PlainImage_HasNoSubject()
    {
        using var plain = Scene(new SKColor(150, 110, 70), new SKColor(150, 110, 70));
        Assert.Null(ImageSignature.SubjectBox(plain));
        Assert.Null(ImageSignature.Isolate(plain));
        using var tiny = new SKBitmap(8, 8);
        Assert.Null(ImageSignature.SubjectBox(tiny));
    }

    [Fact]
    public void Variants_IncludeIsolatedSubject_ForClutteredPhotos()
    {
        using var scene = Scene(new SKColor(150, 110, 70), new SKColor(20, 60, 200));
        using var image = SKImage.FromBitmap(scene);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var variants = ImageSignature.ComputeVariants(data.ToArray());
        Assert.True(variants.Count >= 3, $"variants={variants.Count}");

        // همان کالا روی زمینهٔ سفید (عکس کاتالوگ) با نسخهٔ «سوژهٔ جداشده» بهتر جور می‌شود تا با کل قاب.
        using var catalog = Scene(SKColors.White, new SKColor(20, 60, 200));
        var stored = ImageSignature.Compute(catalog)!;
        var candidate = new ImageSignature.Stored(Guid.NewGuid(), new[] { (stored.Hash, stored.PHash) }, stored.Colors);
        Assert.True(ImageSignature.Score(variants, candidate) >= ImageSearchService.MinScore,
            $"score={ImageSignature.Score(variants, candidate)}");
    }
}
