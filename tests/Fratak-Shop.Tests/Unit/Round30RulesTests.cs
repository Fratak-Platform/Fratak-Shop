using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Web.Infrastructure.Smart;
using SkiaSharp;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// دور ۳۰ – آزمون‌های خالص (بدون پایگاه داده و شبکه):
/// <list type="bullet">
/// <item>#۹ سئوی همیشه در حال بهتر شدن: سیاست «بالا بردن نمره» (<see cref="AutoApprovePolicy.Raise"/>) و بازهٔ امن تنظیمات.</item>
/// <item>#۱۲ رفع خودکار هشدار توضیحات محصول: فهرست نسخه‌های جایگزین.</item>
/// <item>#۶ کاور هوشمند مقاله (<see cref="ArticleCover"/>).</item>
/// <item>#۵ حافظهٔ یادگرفتهٔ تصویر دسته (<see cref="CategoryImageMemory"/>).</item>
/// </list>
/// ترتیب هر گروه: حالت درست ← مرزی ← خراب/حمله.
/// </summary>
[Trait("Category", "Unit")]
public class Round30RulesTests
{
    // ───────────── #۹ سیاست بالا بردن نمره ─────────────

    private static readonly FieldState Filled = new(TitleEmpty: false, MetaEmpty: false, FocusEmpty: false, FaqEmpty: false, AllAltsEmpty: false, BodyThin: false);

    [Fact]
    public void Raise_TitleSafe_AllowsTitleMetaBodyFaqAlt_ButNeverSlugOrExistingKeyword()
    {
        var fields = AutoApprovePolicy.Raise(SeoFields.All, Filled, titleSafe: true);

        Assert.True(fields.HasFlag(SeoFields.Title));
        Assert.True(fields.HasFlag(SeoFields.MetaDescription));
        Assert.True(fields.HasFlag(SeoFields.Description));
        Assert.True(fields.HasFlag(SeoFields.Faq));
        Assert.True(fields.HasFlag(SeoFields.ImageAlt));
        Assert.False(fields.HasFlag(SeoFields.Slug));         // نامک هرگز (لینک‌های قبلی می‌شکند)
        Assert.False(fields.HasFlag(SeoFields.FocusKeyword)); // کلمهٔ کلیدی موجود عوض نمی‌شود
    }

    [Fact]
    public void Raise_NotTitleSafe_KeepsTitleAndMeta()
    {
        var fields = AutoApprovePolicy.Raise(SeoFields.All, Filled, titleSafe: false);

        Assert.False(fields.HasFlag(SeoFields.Title));
        Assert.False(fields.HasFlag(SeoFields.MetaDescription));
        Assert.True(fields.HasFlag(SeoFields.Description));
    }

    [Fact]
    public void Raise_EmptyKeyword_IsFilled_ButEmptyTitleIsFilledEvenWhenNotSafe()
    {
        var empty = Filled with { FocusEmpty = true, TitleEmpty = true };
        var fields = AutoApprovePolicy.Raise(SeoFields.All, empty, titleSafe: false);

        Assert.True(fields.HasFlag(SeoFields.FocusKeyword));
        Assert.True(fields.HasFlag(SeoFields.Title)); // عنوان خالی «پر کردن» است، نه بازنویسی
        Assert.False(fields.HasFlag(SeoFields.Slug));
    }

    [Fact]
    public void Raise_OnlyReturnsFieldsTheEngineProposed()
    {
        var fields = AutoApprovePolicy.Raise(SeoFields.Faq, Filled, titleSafe: true);
        Assert.Equal(SeoFields.Faq, fields);

        Assert.Equal(SeoFields.None, AutoApprovePolicy.Raise(SeoFields.None, Filled, titleSafe: true));
        Assert.Equal(SeoFields.None, AutoApprovePolicy.Raise(SeoFields.Slug, Filled, titleSafe: true));
    }

    [Fact]
    public void Options_RaiseDefaults_AreSafe()
    {
        var options = SeoAutopilotOptions.Parse(null);

        Assert.Equal(SeoAutoMode.Auto, options.RaiseLowScores);
        Assert.Equal(85, options.RaiseTarget);
        Assert.Equal(60, options.RaiseNightly);
        Assert.Equal(14, options.RaiseCooldownDays);
    }

    [Theory]
    [InlineData("{\"RaiseTarget\":100,\"RaiseNightly\":100000,\"RaiseCooldownDays\":1}", 95, 300, 7)]
    [InlineData("{\"RaiseTarget\":10,\"RaiseNightly\":0,\"RaiseCooldownDays\":9999}", 60, 5, 180)]
    [InlineData("{\"RaiseTarget\":-5,\"RaiseNightly\":-5,\"RaiseCooldownDays\":-5}", 60, 5, 7)]
    public void Options_RaiseValues_AreClamped(string json, int target, int nightly, int cooldown)
    {
        var options = SeoAutopilotOptions.Parse(json);

        Assert.Equal(target, options.RaiseTarget);
        Assert.Equal(nightly, options.RaiseNightly);
        Assert.Equal(cooldown, options.RaiseCooldownDays);
    }

    [Theory]
    [InlineData("{\"RaiseLowScores\":99}")]
    [InlineData("{\"RaiseLowScores\":-1}")]
    [InlineData("not json at all")]
    public void Options_BrokenRaiseMode_FallsBackToAuto(string json)
    {
        Assert.Equal(SeoAutoMode.Auto, SeoAutopilotOptions.Parse(json).RaiseLowScores);
    }

    // ───────────── #۱۲ نسخه‌های جایگزین توضیحات محصول ─────────────

    [Fact]
    public void ReviewAttempts_StartWithOriginal_AndCoverEveryStyle()
    {
        var attempts = SeoEngineService.ReviewAttempts;

        Assert.Equal((0, WriterStyle.None), attempts[0]); // اولین نسخه = همان رفتار قبلی
        Assert.Equal(attempts.Length, attempts.Select(a => a.Variant).Distinct().Count());
        Assert.Contains(attempts, a => a.Style.HasFlag(WriterStyle.LessPromotional));
        Assert.Contains(attempts, a => a.Style.HasFlag(WriterStyle.Shorter));
        Assert.Contains(attempts, a => a.Style.HasFlag(WriterStyle.Longer));
        Assert.InRange(attempts.Length, 3, 8); // سقف کار (هاست ضعیف)
    }

    // ───────────── #۶ کاور هوشمند مقاله ─────────────

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private static SKBitmap ProductOnWhite(SKColor color, int size = 400)
    {
        var bitmap = new SKBitmap(size, size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawRoundRect(size * 0.3f, size * 0.15f, size * 0.4f, size * 0.7f, 30, 30, paint);
        return bitmap;
    }

    private static void AssertCover(byte[]? cover)
    {
        Assert.NotNull(cover);
        using var decoded = SKBitmap.Decode(cover);
        Assert.NotNull(decoded);
        Assert.Equal(ArticleCover.Width, decoded.Width);
        Assert.Equal(ArticleCover.Height, decoded.Height);
        // WebP: «RIFF....WEBP»
        Assert.Equal((byte)'R', cover![0]);
        Assert.Equal((byte)'W', cover[8]);
    }

    [Fact]
    public void Cover_WithProducts_IsWideWebP_AndShowsTheProduct()
    {
        using var red = ProductOnWhite(new SKColor(200, 30, 40));
        using var blue = ProductOnWhite(new SKColor(30, 60, 200));
        var cover = ArticleCover.Compose([Encode(red), Encode(blue)], null);

        AssertCover(cover);
        using var decoded = SKBitmap.Decode(cover);
        // کالای اصلی (قرمز) جلو و حدود ۶۲٪ عرض یا وسط چیده می‌شود: جایی از نیمهٔ پایین باید قرمز پررنگ باشد.
        var foundRed = false;
        for (var x = decoded.Width / 3; x < decoded.Width && !foundRed; x += 8)
        {
            for (var y = decoded.Height / 3; y < decoded.Height * 0.9 && !foundRed; y += 8)
            {
                var c = decoded.GetPixel(x, y);
                foundRed = c.Red > 150 && c.Green < 90 && c.Blue < 100;
            }
        }
        Assert.True(foundRed);
    }

    [Fact]
    public void Cover_UsesAtMostThreeProducts()
    {
        using var p = ProductOnWhite(new SKColor(20, 140, 60));
        var bytes = Encode(p);
        AssertCover(ArticleCover.Compose([bytes, bytes, bytes, bytes, bytes], null));
        Assert.Equal(3, ArticleCover.MaxProducts);
    }

    [Fact]
    public void Cover_WithOnlyBackdrop_FillsTheFrame()
    {
        using var photo = new SKBitmap(1600, 1000);
        using (var canvas = new SKCanvas(photo))
        {
            using var paint = new SKPaint();
            paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1600, 1000),
                [new SKColor(250, 140, 20), new SKColor(30, 120, 220)], null, SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, 1600, 1000, paint);
        }
        var cover = ArticleCover.Compose([], Encode(photo, SKEncodedImageFormat.Jpeg));

        AssertCover(cover);
        using var decoded = SKBitmap.Decode(cover);
        // گوشهٔ بالا-چپ از خود عکس (نارنجی)، نه زمینهٔ روشن پیش‌فرض.
        var corner = decoded.GetPixel(20, 20);
        Assert.True(corner.Red > 180 && corner.Blue < 120);
    }

    [Fact]
    public void Cover_WithNothing_StillMakesAGradientCover()
    {
        AssertCover(ArticleCover.Compose([], null));
    }

    [Fact]
    public void Cover_GarbageOrTinyImages_AreIgnored_NotThrown()
    {
        using var tiny = ProductOnWhite(SKColors.Red, 40); // کوچک‌تر از ۱۲۰ پیکسل ← کنار گذاشته می‌شود
        var cover = ArticleCover.Compose([[1, 2, 3, 4], new byte[5000], Encode(tiny)], [9, 9, 9]);

        AssertCover(cover);
    }

    [Fact]
    public void Accent_PicksTheProductColor_AndDefaultsForGray()
    {
        using var red = ProductOnWhite(new SKColor(210, 30, 30));
        var accent = ArticleCover.Accent(red);
        Assert.True(accent.Red > 150 && accent.Green < 80);

        using var gray = new SKBitmap(100, 100);
        using (var canvas = new SKCanvas(gray))
            canvas.Clear(new SKColor(128, 128, 128));
        Assert.Equal(new SKColor(70, 110, 200), ArticleCover.Accent(gray));
    }

    // ───────────── #۵ حافظهٔ یادگرفتهٔ تصویر دسته ─────────────

    private static readonly Guid CategoryA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid CategoryB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid CategoryC = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    private static string Vector(double value) =>
        CategoryImageLearning.Serialize(Enumerable.Repeat(value, CategoryImageLearning.FeatureCount).ToArray());

    [Fact]
    public void Features_SerializeAndParse_RoundTrip()
    {
        var features = Enumerable.Range(0, CategoryImageLearning.FeatureCount).Select(i => i / 10.0).ToArray();
        var parsed = CategoryImageLearning.Parse(CategoryImageLearning.Serialize(features));

        Assert.NotNull(parsed);
        Assert.Equal(features, parsed!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.1,0.2")]                                  // طول اشتباه
    [InlineData("a,b,c,d,e,f,g,h,i,j,k")]                    // متن خراب
    [InlineData("NaN,1,1,1,1,1,1,1,1,1,1")]                  // عدد نامعتبر
    public void Features_BrokenText_IsIgnored(string? text)
    {
        Assert.Null(CategoryImageLearning.Parse(text));
    }

    [Fact]
    public void Memory_LearnsFromDecisions_AndRejectsLookAlikes()
    {
        var samples = new List<CategoryImageSample>();
        for (var i = 0; i < 8; i++)
        {
            samples.Add(new CategoryImageSample(CategoryA, null, 0, "openverse", Vector(0.9), true));
            samples.Add(new CategoryImageSample(CategoryB, null, 0, "bing:junk.ir", Vector(0.1), false));
        }
        var memory = new CategoryImageMemory(CategoryA, samples);

        Assert.NotNull(memory.Model);
        Assert.True(memory.Rejects(Enumerable.Repeat(0.1, CategoryImageLearning.FeatureCount).ToArray()));
        Assert.False(memory.Rejects(Enumerable.Repeat(0.9, CategoryImageLearning.FeatureCount).ToArray()));
        // منبعی که ۸ بار از ۸ بار رد شده کنار می‌رود؛ منبع خوب نه.
        Assert.True(memory.SourceBlocked("bing:junk.ir"));
        Assert.False(memory.SourceBlocked("openverse"));
        // رتبه: عکس شبیه تأییدشده‌ها بالاتر از عکس شبیه ردشده‌ها با همان امتیاز پایه.
        var good = memory.Rank(0.7, Enumerable.Repeat(0.9, CategoryImageLearning.FeatureCount).ToArray(), "openverse");
        var bad = memory.Rank(0.7, Enumerable.Repeat(0.1, CategoryImageLearning.FeatureCount).ToArray(), "bing:junk.ir");
        Assert.True(good > bad);
    }

    [Fact]
    public void Memory_TooFewDecisions_HasNoModel_AndNeverRejectsByModel()
    {
        var samples = Enumerable.Range(0, CategoryImageLearning.MinPerClass - 1)
            .SelectMany(_ => new[]
            {
                new CategoryImageSample(CategoryA, null, 0, null, Vector(0.9), true),
                new CategoryImageSample(CategoryA, null, 0, null, Vector(0.1), false),
            }).ToList();
        var memory = new CategoryImageMemory(CategoryA, samples);

        Assert.Null(memory.Model);
        Assert.False(memory.Rejects(Enumerable.Repeat(0.1, CategoryImageLearning.FeatureCount).ToArray()));
        Assert.Equal(0.5, memory.SourceScore("never-seen"));
    }

    [Fact]
    public void Memory_BlocksRejectedUrl_OnlyForThatCategory_AndHashRejectedInTwoCategoriesEverywhere()
    {
        const string url = "https://example.com/images/bad.jpg";
        const long shared = 0x0F0F_0F0F_0F0F_0F0FL;
        var samples = new List<CategoryImageSample>
        {
            new(CategoryA, SmartImageRules.UrlKey(url), 0, null, null, false),
            new(CategoryA, null, shared, null, null, false),
            new(CategoryB, null, shared, null, null, false),
        };

        Assert.True(new CategoryImageMemory(CategoryA, samples).Blocks(url, 0));
        Assert.False(new CategoryImageMemory(CategoryC, samples).Blocks(url, 0));
        // ردشده در ۲ دستهٔ مختلف ← برای دستهٔ سوم هم «عکس بد» است.
        Assert.True(new CategoryImageMemory(CategoryC, samples).Blocks(null, unchecked((ulong)shared)));
        Assert.False(CategoryImageMemory.Empty.Blocks(url, unchecked((ulong)shared)));
    }
}
