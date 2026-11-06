using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.Smart;
using Fratak_Shop.Web.Areas.Admin.Pages.SmartContent;
using Fratak_Shop.Web.Infrastructure.Smart;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های دور نوزدهم (محتوای هوشمند و فروش هوشمند) بدون دیتابیس و بدون اینترنت.
/// <para>هر بخش یک قاعدهٔ «بی‌خطر بودن» را قفل می‌کند تا تغییر بعدی آن را نشکند:</para>
/// <list type="bullet">
/// <item>ادغام مشخصات (B7–B9): فقط ردیف‌های «جدید» با توافق چند منبع خودکار اعمال می‌شوند؛ تعارض و ردیف‌های بی‌ارزش (گارانتی، لینک) نه.</item>
/// <item>تحلیل نظرها (B10–B13): نکته فقط با تکرار؛ جمله‌های ارسال/فروشنده حذف؛ «نیست» قطب جمله را برمی‌گرداند.</item>
/// <item>نویسندهٔ بلاگ (B14–B16): دروازهٔ کیفیت متن کوتاه/عنوان تکراری را منتشر نمی‌کند.</item>
/// <item>کدهای کوتاه محصول در بلاگ، کیبورد اشتباه (C15)، dHash تصویر، محافظ خروجی LLM، ساعت سکوت، محافظ سود.</item>
/// <item>قاعده‌های «راهنمای خریدار» (C5/C6/C2/C8) که جلوی ادعای نادرست/الگوی فریبنده را می‌گیرند.</item>
/// <item>کارگزار دستیار محلی (A31) و کدهای کمکی پنل.</item>
/// </list>
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Smart")]
public class SmartContentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    // ── ادغام مشخصات ────────────────────────────────────────────────

    [Fact]
    public void SpecMerge_same_value_in_different_digits_and_units_counts_as_agreement()
    {
        var merged = SpecMerge.Merge(
        [
            new SpecObservation("digikala", "وزن", "250 گرم"),
            new SpecObservation("supplier", "وزن", "۲۵۰گرم"),
        ]);

        var row = Assert.Single(merged);
        Assert.Equal(2, row.Agreement);
        Assert.False(row.Conflict);
    }

    [Fact]
    public void SpecMerge_disagreeing_sources_are_marked_as_conflict()
    {
        var merged = SpecMerge.Merge(
        [
            new SpecObservation("a", "جنس بدنه", "فلز"),
            new SpecObservation("b", "جنس بدنه", "پلاستیک"),
        ]);

        var row = Assert.Single(merged);
        Assert.True(row.Conflict);
        Assert.Equal(1, row.Agreement);
    }

    [Theory]
    [InlineData("گارانتی", "۱۸ ماهه")]
    [InlineData("وزن", "http://spam.example/buy")]
    [InlineData("وزن", "www.spam.example")]
    [InlineData("x", "کوتاه")]
    public void SpecMerge_junk_rows_are_never_useful(string key, string value)
    {
        Assert.False(SpecMerge.IsUseful(key, value));
    }

    [Fact]
    public void SpecMerge_junk_rows_are_dropped_from_the_merge()
    {
        var merged = SpecMerge.Merge(
        [
            new SpecObservation("a", "گارانتی", "۱۸ ماهه"),
            new SpecObservation("b", "گارانتی", "۱۸ ماهه"),
            new SpecObservation("a", "لینک خرید", "https://x.example"),
        ]);

        Assert.Empty(merged);
    }

    [Fact]
    public void SpecMerge_diff_only_auto_applies_new_agreed_rows()
    {
        var merged = SpecMerge.Merge(
        [
            new SpecObservation("a", "وزن", "250 گرم"),
            new SpecObservation("b", "وزن", "250 گرم"),
            new SpecObservation("a", "جنس بدنه", "فلز"),
            new SpecObservation("b", "جنس بدنه", "فلز"),
            new SpecObservation("a", "رنگ", "مشکی"),
            new SpecObservation("b", "رنگ", "مشکی"),
            new SpecObservation("a", "ابعاد", "10 × 20"),
        ]);

        var current = new List<(string Key, string Value)> { ("وزن", "۲۵۰ گرم"), ("جنس بدنه", "پلاستیک") };
        var diff = SpecMerge.Diff(merged, current, minSources: 2);

        Assert.Equal(SpecDiffKind.Same, diff.Single(d => d.Key == merged[0].Key).Diff);

        var different = diff.Single(d => d.Key == merged[1].Key);
        Assert.Equal(SpecDiffKind.Different, different.Diff);
        Assert.False(different.AutoApplicable); // مقدار فعلی ادمین هیچ‌وقت خودکار عوض نمی‌شود

        var agreedNew = diff.Single(d => d.Key == merged[2].Key);
        Assert.Equal(SpecDiffKind.New, agreedNew.Diff);
        Assert.True(agreedNew.AutoApplicable);

        var singleSource = diff.Single(d => d.Key == merged[3].Key);
        Assert.Equal(SpecDiffKind.New, singleSource.Diff);
        Assert.False(singleSource.AutoApplicable); // فقط یک منبع ← منتظر تأیید

        // ترتیب: جدیدها، بعد متفاوت‌ها، آخر یکسان‌ها.
        Assert.Equal(SpecDiffKind.Same, diff[^1].Diff);
        Assert.Equal(SpecDiffKind.New, diff[0].Diff);
    }

    [Fact]
    public void SpecMerge_align_uses_the_existing_filter_key_spelling()
    {
        var aligned = SpecMerge.AlignToFilterKey("وزن", ["وزن"]);
        Assert.Equal("وزن", aligned);
        Assert.Equal(SpecMerge.NormalizeKey("ابعاد"), SpecMerge.AlignToFilterKey("ابعاد", ["وزن"]));
    }

    [Fact]
    public void SpecMerge_survives_hostile_input()
    {
        var huge = new string('۹', 50_000);
        var merged = SpecMerge.Merge([new SpecObservation("a", "وزن", huge), new SpecObservation("b", "<script>", "alert(1)")]);
        Assert.All(merged, m => Assert.True(m.Value.Length <= 300));
    }

    // ── متن، کیبورد و هش تصویر ───────────────────────────────────────

    [Fact]
    public void KeyboardLayout_swaps_latin_typed_persian_and_back()
    {
        Assert.Equal("سلام", KeyboardLayout.Swap("sghl"));
        Assert.Equal("sghl", KeyboardLayout.Swap("سلام"));
        Assert.Equal("سلام", KeyboardLayout.Swap("SGHL"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("سلام hello")]
    [InlineData("123 456")]
    public void KeyboardLayout_does_not_touch_mixed_or_meaningless_input(string? text)
    {
        Assert.Null(KeyboardLayout.Swap(text));
    }

    [Fact]
    public void KeyboardLayout_rejects_very_long_input()
    {
        Assert.Null(KeyboardLayout.Swap(new string('s', 101)));
    }

    [Fact]
    public void ImageHash_detects_near_duplicates_only()
    {
        var gradient = new byte[72];
        for (var i = 0; i < 72; i++)
            gradient[i] = (byte)(255 - i % 9 * 20);
        var same = (byte[])gradient.Clone();
        same[0] = (byte)(same[0] - 1); // تغییر بسیار جزئی (فشرده‌سازی دوباره)
        var reversed = new byte[72];
        for (var i = 0; i < 72; i++)
            reversed[i] = (byte)(i % 9 * 20);

        var a = ImageHash.DHash(gradient);
        Assert.True(ImageHash.IsDuplicate(a, ImageHash.DHash(same)));
        Assert.False(ImageHash.IsDuplicate(a, ImageHash.DHash(reversed)));
        Assert.Equal(0, ImageHash.Distance(a, a));
        Assert.Throws<ArgumentException>(() => ImageHash.DHash(new byte[10]));
    }

    [Fact]
    public void SmartText_detects_copied_text_and_similarity()
    {
        const string source = "این هدفون بی سیم با باتری قوی و صدای شفاف برای ورزش و سفر مناسب است و وزن کمی دارد";
        Assert.True(SmartText.IsCopied(source + " و رنگ مشکی", [source]));
        Assert.False(SmartText.IsCopied("متنی کاملاً تازه دربارهٔ یک کالای دیگر که هیچ شباهتی ندارد و خودش نوشته شده", [source]));
        Assert.Equal(1, SmartText.Jaccard("باتری قوی", "باتری قوی"), 3);
        Assert.Equal(0, SmartText.Jaccard(null, "باتری"));
    }

    [Fact]
    public void SmartText_pick_is_deterministic_per_seed()
    {
        Assert.Equal(SmartText.Pick("seed-1", "a", "b", "c"), SmartText.Pick("seed-1", "a", "b", "c"));
        Assert.Equal(string.Empty, SmartText.Pick("seed"));
    }

    // ── تحلیل نظرها ─────────────────────────────────────────────────

    private static SourceReview Review(string body, int? rating = 5, string[]? pros = null, string[]? cons = null) =>
        new("site", null, body, rating, pros ?? [], cons ?? [], null);

    [Theory]
    [InlineData("باتری قوی است", 1)]
    [InlineData("باتری خوب نیست", -1)]
    [InlineData("صدا ضعیف است", -1)]
    [InlineData("این یک جمله است", 0)]
    public void ReviewSynthesis_polarity_understands_negation(string sentence, int expected)
    {
        Assert.Equal(expected, ReviewSynthesis.Polarity(sentence));
    }

    [Fact]
    public void ReviewSynthesis_aspects_do_not_misfire_on_prefixes()
    {
        Assert.Equal("صدا", ReviewSynthesis.FindAspect("صداش عالیه")?.Name);
        Assert.Equal("باتری", ReviewSynthesis.FindAspect("باتری قوی")?.Name);
        Assert.Null(ReviewSynthesis.FindAspect("گوشی رسید"));
    }

    [Fact]
    public void ReviewSynthesis_needs_repetition_and_ignores_shipping_talk()
    {
        var reviews = new List<SourceReview>
        {
            Review("باتری قوی است"),
            Review("باتری خیلی خوب است"),
            Review("باتری عالی است"),
            Review("ارسال خیلی بد بود و بسته‌بندی خراب"),
            Review("ارسال بد بود"),
            Review("ظاهر زیبا"),
        };

        var (pros, cons) = ReviewSynthesis.ExtractPoints(reviews);

        var battery = Assert.Single(pros, p => p.Text == ReviewSynthesis.Aspects.Single(a => a.Name == "باتری").Positive);
        Assert.Equal(3, battery.Mentions);
        Assert.All(pros, p => Assert.True(p.Mentions >= 2));
        Assert.Empty(cons); // گلایه از ارسال، ضعف کالا نیست
        Assert.True(ReviewSynthesis.IsOffTopic("ارسال خیلی دیر بود"));
    }

    [Fact]
    public void ReviewSynthesis_build_reports_rating_only_with_enough_votes()
    {
        var subject = new InsightSubject(Guid.NewGuid(), "هدفون آزمایشی", null, null, []);
        Assert.Null(ReviewSynthesis.Build(subject, [Review("خوب", 5), Review("بد", 1)]).RatingTenths);
        Assert.Equal(40, ReviewSynthesis.Build(subject, [Review("الف", 5), Review("ب", 4), Review("ج", 3)]).RatingTenths);
    }

    // ── نویسندهٔ بلاگ و کدهای کوتاه ─────────────────────────────────

    [Fact]
    public void BlogWriter_quality_gate_blocks_short_and_duplicate_articles()
    {
        var article = new BlogArticle("راهنمای خرید هدفون", "خلاصه", "<p>متن خیلی کوتاه.</p>", "هدفون", []);

        var report = BlogWriter.Check(article, ["راهنمای خرید هدفون"], []);

        Assert.False(report.Passed);
        Assert.Contains(report.Problems, p => p.Contains("کوتاه", StringComparison.Ordinal));
        Assert.Contains(report.Problems, p => p.Contains("همین عنوان", StringComparison.Ordinal));
        Assert.Contains(report.Problems, p => p.Contains("خلاصه در یک نگاه", StringComparison.Ordinal));
        Assert.InRange(report.Score, 0, 100);
    }

    [Fact]
    public void BlogWriter_encodes_product_names_and_puts_key_info_first()
    {
        var products = Enumerable.Range(1, 3).Select(i => new BlogProduct(
            Guid.NewGuid(), $"<script>alert({i})</script> هدفون {i}", $"headphone-{i}", "برند", 1_000_000m * i, true, 4.5, 10 * i, 5 * i,
            [new SeoFeature("وزن", $"{200 + i} گرم")], ["صدای خوب"], [], [])).ToList();

        var article = BlogWriter.Write(new BlogBrief(BlogArticleType.BuyingGuide, "هدفون بی سیم", "هدفون بی سیم", "هدفون", "headphones", products, Now));

        Assert.DoesNotContain("<script", article.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", article.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"class=\"{BlogWriter.TldrClass}\"", article.Html, StringComparison.Ordinal);
        // خلاصه بالای مقاله است (پیش از اولین تیتر h2).
        var h2 = article.Html.IndexOf("<h2", StringComparison.OrdinalIgnoreCase);
        Assert.True(h2 < 0 || article.Html.IndexOf(BlogWriter.TldrClass, StringComparison.Ordinal) < h2);
        Assert.Contains("[[product:", article.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void BlogShortcodes_extracts_unique_slugs_and_ignores_garbage()
    {
        var slugs = BlogShortcodes.ProductSlugs("<p>[[product:abc-1]]</p> قیمت: [[price:X2]] [[product:abc-1]] [[product:../etc]] [[product:]]");
        Assert.Equal(new[] { "abc-1", "x2" }, slugs);
        Assert.Empty(BlogShortcodes.ProductSlugs(null));
        Assert.Empty(BlogShortcodes.ProductSlugs("<p>بدون کد</p>"));
    }

    [Fact]
    public void BlogShortcodes_render_cards_prices_and_hide_missing_products()
    {
        var products = new List<ShortcodeProduct>
        {
            new("abc-1", "<b>هدفون</b>", "a.webp", 1_200_000m, 990_000m, true, 4.4, 12),
            new("gone", "کالای ناموجود", null, 500_000m, 500_000m, false, 0, 0),
        };
        var html = BlogShortcodes.Render("<p>[[product:abc-1]]</p><p>قیمت امروز: [[price:abc-1]] / [[PRICE:gone]]</p><p>[[product:missing]]</p>",
            products, name => "/img/" + name);

        Assert.Contains("fs-product-card", html, StringComparison.Ordinal);
        Assert.Contains("/product/abc-1", html, StringComparison.Ordinal);
        Assert.Contains("990,000 تومان", html, StringComparison.Ordinal);
        Assert.Contains("<del>1,200,000</del>", html, StringComparison.Ordinal);
        Assert.Contains("ناموجود", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>هدفون</b>", html, StringComparison.Ordinal); // نام کالا encode می‌شود
        Assert.DoesNotContain("[[", html, StringComparison.Ordinal);
        Assert.DoesNotContain("missing", html, StringComparison.Ordinal);
    }

    // ── محافظ خروجی مدل زبانی، ساعت سکوت، محافظ سود ─────────────────

    [Fact]
    public void LlmGuard_rejects_changed_numbers_markup_links_and_other_languages()
    {
        const string original = "این پاوربانک ۱۰۰۰۰ میلی‌آمپر ساعت ظرفیت دارد و با دو درگاه خروجی همزمان دو گوشی را شارژ می‌کند.";
        Assert.False(LlmGuard.Accept(original, original.Replace("۱۰۰۰۰", "۲۰۰۰۰")));
        Assert.False(LlmGuard.Accept(original, original + " <a href=\"x\">خرید</a>"));
        Assert.False(LlmGuard.Accept(original, original + " http://spam.example"));
        Assert.False(LlmGuard.Accept(original, "This power bank has 10000 mAh capacity and charges two phones at the same time quickly."));
        Assert.False(LlmGuard.Accept(original, "کوتاه ۱۰۰۰۰"));
        Assert.False(LlmGuard.Accept(original, ""));
    }

    [Fact]
    public void LlmGuard_numbers_are_compared_digit_system_independently()
    {
        Assert.True(LlmGuard.Numbers("قیمت ۱٬۲۰۰ و وزن 2.5").SetEquals(LlmGuard.Numbers("قیمت 1200 و وزن ۲٫۵")));
    }

    [Theory]
    [InlineData(19, 0, true)]  // ۲۲:۳۰ تهران
    [InlineData(5, 0, true)]   // ۸:۳۰ تهران
    [InlineData(6, 0, false)]  // ۹:۳۰ تهران
    [InlineData(12, 0, false)] // ۱۵:۳۰ تهران
    public void Quiet_hours_follow_iran_time(int utcHour, int minute, bool quiet)
    {
        Assert.Equal(quiet, CustomerQuietHours.IsQuiet(new DateTimeOffset(2026, 11, 5, utcHour, minute, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Quiet_hours_equal_bounds_mean_never_quiet()
    {
        Assert.False(CustomerQuietHours.IsQuiet(Now, 10, 10));
    }

    [Theory]
    [InlineData(100_000, null, 120_000, true)]
    [InlineData(150_000, 90_000, 120_000, true)]
    [InlineData(150_000, null, 120_000, false)]
    [InlineData(150_000, 130_000, 120_000, false)]
    [InlineData(150_000, null, null, false)]
    [InlineData(0, null, 120_000, false)]
    public void ProfitGuard_flags_only_real_losses(int price, int? discount, int? purchase, bool loss)
    {
        var message = ProfitGuard.Check((decimal)price, (decimal?)discount, (decimal?)purchase);
        Assert.Equal(loss, message is not null);
    }

    [Fact]
    public void SmartOptions_are_clamped_to_safe_ranges()
    {
        var options = new SmartOptions
        {
            BlogPostsPerWeek = 99, BlogQualityMin = 10, AutoSpecMinSources = 1, MinImageSide = 10, AutoImagesMax = 100,
            ReviewRequestTitle = "", QuietFromHour = 40,
        }.Normalize();

        Assert.Equal(7, options.BlogPostsPerWeek);
        Assert.Equal(50, options.BlogQualityMin);
        Assert.Equal(2, options.AutoSpecMinSources);
        Assert.Equal(300, options.MinImageSide);
        Assert.Equal(8, options.AutoImagesMax);
        Assert.Equal(23, options.QuietFromHour);
        Assert.False(string.IsNullOrWhiteSpace(options.ReviewRequestTitle));
    }

    // ── راهنمای خریدار (فقط ادعای درست) ──────────────────────────────

    [Fact]
    public void Lowest_in_30_days_needs_real_history_and_real_price_movement()
    {
        Assert.Equal((900m, true), BuyerGuideService.Evaluate(900, 900, 1000, Now.AddDays(-10), Now));
        Assert.Equal((850m, false), BuyerGuideService.Evaluate(900, 850, 1000, Now.AddDays(-10), Now));
        Assert.Equal((null, false), BuyerGuideService.Evaluate(900, 900, 1000, Now.AddDays(-3), Now)); // تاریخچهٔ کوتاه
        Assert.Equal((null, false), BuyerGuideService.Evaluate(900, 900, 900, Now.AddDays(-20), Now)); // قیمت هیچ‌وقت عوض نشده
        Assert.Equal((null, false), BuyerGuideService.Evaluate(900, null, null, null, Now));
    }

    [Fact]
    public void Torob_line_is_shown_only_when_true_and_fresh()
    {
        Assert.NotNull(BuyerGuideService.TorobRule(100, 120, 3, Now.AddHours(-1), Now));
        Assert.NotNull(BuyerGuideService.TorobRule(120, 120, 3, Now.AddHours(-1), Now));
        Assert.Null(BuyerGuideService.TorobRule(130, 120, 3, Now.AddHours(-1), Now)); // گران‌تر
        Assert.Null(BuyerGuideService.TorobRule(100, 120, 1, Now.AddHours(-1), Now)); // یک فروشنده مقایسه نیست
        Assert.Null(BuyerGuideService.TorobRule(100, 120, 3, Now.AddHours(-49), Now)); // کهنه
        Assert.Null(BuyerGuideService.TorobRule(100, null, 3, Now, Now));
    }

    [Fact]
    public void Delivery_medians_need_five_samples_and_ignore_bad_rows()
    {
        var paid = Now.AddDays(-30);
        var rows = new List<(string, DateTimeOffset, DateTimeOffset)>();
        foreach (var days in new[] { 1, 2, 2, 3, 10 })
            rows.Add(("تهران", paid, paid.AddDays(days)));
        rows.Add(("اصفهان", paid, paid.AddDays(2)));     // فقط یک نمونه
        rows.Add(("تهران", paid, paid.AddDays(-1)));     // تاریخ خراب
        rows.Add(("تهران", paid, paid.AddDays(90)));     // پرت
        rows.Add(("", paid, paid.AddDays(1)));

        var result = BuyerGuideService.Medians(rows);

        var tehran = Assert.Single(result);
        Assert.Equal("تهران", tehran.Province);
        Assert.Equal(2, tehran.MedianDays);
        Assert.Equal(5, tehran.Samples);
    }

    [Fact]
    public void Quiz_budgets_are_rounded_real_price_tiers()
    {
        Assert.Empty(BuyerGuideService.Budgets([100_000m, 200_000m]));
        Assert.Equal(new[] { 200_000m, 300_000m }, BuyerGuideService.Budgets([100_000m, 200_000m, 300_000m]));
        var big = BuyerGuideService.Budgets([1_234_000m, 2_000_000m, 3_100_000m, 4_000_000m]);
        Assert.All(big, b => Assert.Equal(0, b % 100_000m));
        Assert.Equal(4_000_000m, big[^1]);
    }

    // ── تصویر ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(800, 800, true)]
    [InlineData(1600, 800, true)]
    [InlineData(500, 900, false)]  // ضلع کوچک
    [InlineData(3000, 700, false)] // خیلی کشیده (بنر تبلیغاتی)
    [InlineData(700, 2000, false)]
    public void Image_candidates_must_be_large_and_not_banner_shaped(int width, int height, bool ok)
    {
        Assert.Equal(ok, SmartImageAnalyzer.IsAcceptable(new ImageInfo(width, height, 0), 600));
    }

    [Fact]
    public void Image_analyzer_rejects_non_images()
    {
        var junk = System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("<html>not an image</html>", 40)));
        Assert.Null(SmartImageAnalyzer.Analyze(junk));
        Assert.Null(SmartImageAnalyzer.Analyze([]));
    }

    [Fact]
    public void Image_analyzer_reads_size_and_a_stable_fingerprint()
    {
        static byte[] Png(int width, int height)
        {
            using var bitmap = new SkiaSharp.SKBitmap(width, height);
            using (var canvas = new SkiaSharp.SKCanvas(bitmap))
            {
                canvas.Clear(SkiaSharp.SKColors.White);
                using var paint = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Black };
                canvas.DrawRect(0, 0, width / 3f, height, paint);
            }
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        var big = SmartImageAnalyzer.Analyze(Png(900, 700));
        var small = SmartImageAnalyzer.Analyze(Png(450, 350));

        Assert.NotNull(big);
        Assert.Equal(900, big!.Width);
        Assert.Equal(700, big.Height);
        Assert.True(SmartImageAnalyzer.IsAcceptable(big, 600));
        Assert.False(SmartImageAnalyzer.IsAcceptable(small!, 600));
        Assert.True(ImageHash.IsDuplicate(big.Hash, small.Hash)); // همان تصویر در اندازهٔ کوچک‌تر = تکراری
    }

    // ── دستیار محلی ────────────────────────────────────────────────

    [Fact]
    public async Task HelperBroker_returns_null_when_helper_is_offline()
    {
        var broker = new HelperBroker();
        Assert.False(broker.IsOnline(DateTimeOffset.UtcNow));
        Assert.Null(await broker.RunAsync("fetch", "https://example.com", null, null, CancellationToken.None));
        Assert.Equal(0, broker.Pending);
    }

    [Fact]
    public async Task HelperBroker_round_trips_a_job_through_the_helper()
    {
        var broker = new HelperBroker();
        broker.MarkSeen();

        var run = broker.RunAsync("fetch", "https://example.com/p", "text/html", null, CancellationToken.None);
        var job = await broker.NextAsync(CancellationToken.None);
        Assert.NotNull(job);
        Assert.Equal("https://example.com/p", job!.Url);

        Assert.True(broker.Complete(new HelperResult(job.Id, 200, "<html>ok</html>", null)));
        var result = await run;
        Assert.Equal(200, result!.Status);
        Assert.Equal("<html>ok</html>", result.Body);
        Assert.Equal(0, broker.Pending);
        Assert.False(broker.Complete(new HelperResult(job.Id, 200, "late", null))); // پاسخ دیرهنگام/تکراری پذیرفته نمی‌شود
    }

    [Fact]
    public async Task HelperBroker_rejects_unknown_and_oversized_results()
    {
        var broker = new HelperBroker();
        Assert.False(broker.Complete(new HelperResult(Guid.NewGuid(), 200, "x", null)));

        broker.MarkSeen();
        var run = broker.RunAsync("fetch", "https://example.com/big", null, null, CancellationToken.None);
        var job = await broker.NextAsync(CancellationToken.None);
        Assert.True(broker.Complete(new HelperResult(job!.Id, 200, new string('x', HelperBroker.MaxBodyChars + 1), null)));
        var result = await run;
        Assert.Null(result!.Body);
        Assert.Equal(0, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    // ── کدهای کمکی پنل ─────────────────────────────────────────────

    /// <summary>کار پس‌زمینهٔ هوشمند (هر چند ثانیه نوشتن در صف) نباید کش صفحه‌های عمومی را خالی کند؛ ولی خلاصهٔ خرید تأییدشده باید.</summary>
    [Fact]
    public void Smart_background_tables_do_not_evict_the_public_page_cache()
    {
        Assert.False(Fratak_Shop.Web.Infrastructure.PageCache.PageCachePolicy.AffectsPages(
            [typeof(SmartTask), typeof(SmartResearchCache), typeof(BlogPlanItem), typeof(CustomerReminder), typeof(SmartContentConfig)]));
        Assert.True(Fratak_Shop.Web.Infrastructure.PageCache.PageCachePolicy.AffectsPages([typeof(ProductInsight)]));
    }

    [Fact]
    public void SmartUi_has_a_title_and_badge_for_every_enum_value()
    {
        Assert.All(Enum.GetValues<SmartTaskKind>().Where(k => k != SmartTaskKind.None), k => Assert.NotEqual("نامشخص", SmartUi.KindTitle(k)));
        Assert.All(Enum.GetValues<SmartTaskStatus>(), s => Assert.StartsWith("a-badge-", SmartUi.Status(s).Badge));
        Assert.All(Enum.GetValues<BlogPlanStatus>(), s => Assert.NotEqual("—", SmartUi.PlanStatus(s).Title));
        Assert.All(Enum.GetValues<BlogArticleType>(), t => Assert.NotEqual("—", SmartUi.ArticleTypeTitle(t)));
        var id = Guid.NewGuid();
        Assert.Equal($"/admin/smart/product/{id}", SmartUi.TargetUrl(SmartTarget.Product, id));
        Assert.Null(SmartUi.TargetUrl(SmartTarget.Product, null));
    }

    [Fact]
    public void SmartUi_task_result_round_trips_and_tolerates_bad_json()
    {
        var result = new SmartTaskResult
        {
            SpecsAdded = 2,
            Specs = [new SpecSuggestion("وزن", "250 گرم", null, SpecDiffKind.New, ["a", "b"], 2, false, true)],
            PendingImages = [new FoundImage("x.webp", 800, 800, "bing", false)],
            Steps = ["مشخصات"],
        };

        var back = SmartUi.Result(SmartUi.Serialize(result));

        Assert.NotNull(back);
        Assert.Equal(2, back!.SpecsAdded);
        var spec = Assert.Single(back.Specs);
        Assert.Equal("وزن", spec.Key);
        Assert.True(spec.AutoApplicable);
        Assert.Equal(new[] { "a", "b" }, spec.Sources);
        Assert.Equal("x.webp", Assert.Single(back.PendingImages).FileName);
        Assert.Null(SmartUi.Result("not json"));
        Assert.Null(SmartUi.Result(null));
        Assert.Null(SmartUi.Result("{\"specs\": 5}"));
    }
}
