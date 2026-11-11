using System.Text;
using Fratak_Shop.Core.Services.Insights;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Searches;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Services.Suppliers;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Web.Infrastructure.Commerce;
using Fratak_Shop.Web.Infrastructure.Middleware;
using Fratak_Shop.Web.Infrastructure.Smart;
using Fratak_Shop.Web.Services;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// دور ۲۸ – آزمون‌های خالص قابلیت‌های تازه:
/// بررسی سوژهٔ عکس (سفید/آدم/واترمارک/صحنه)، نشانه‌ها و امتیاز ذخیره‌شده، عکس دسته، «کارهای امروز»، جست‌وجوی جایگزین،
/// امنیت PDF، کمبودهای «آمادهٔ انتشار»، هشدار جهش قیمت، گزارش CSP و کمک‌کارهای آماده‌سازی خودکار.
/// ترتیب هر بخش: حالت درست ← مرزی ← منفی/حمله‌ای.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Round28")]
public class Round28FeaturesTests
{
    // ── ابزار ساخت عکس مصنوعی ───────────────────────────────────

    private const int Side = 64;

    private static byte[] Canvas((byte R, byte G, byte B) color)
    {
        var rgb = new byte[Side * Side * 3];
        for (var i = 0; i < rgb.Length; i += 3)
        {
            rgb[i] = color.R;
            rgb[i + 1] = color.G;
            rgb[i + 2] = color.B;
        }
        return rgb;
    }

    private static void Fill(byte[] rgb, int x0, int y0, int x1, int y1, (byte R, byte G, byte B) color)
    {
        for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var i = (y * Side + x) * 3;
                rgb[i] = color.R;
                rgb[i + 1] = color.G;
                rgb[i + 2] = color.B;
            }
    }

    private static readonly (byte, byte, byte) White = (255, 255, 255);
    private static readonly (byte, byte, byte) Navy = (20, 40, 120);
    private static readonly (byte, byte, byte) SkinTone = (224, 172, 138);

    // ── بررسی سوژهٔ عکس ─────────────────────────────────────────

    [Fact]
    public void Product_on_white_background_is_accepted()
    {
        var rgb = Canvas(White);
        Fill(rgb, 17, 17, 46, 46, Navy);
        var verdict = ImageSubjectCheck.Check(rgb, Side, Side);
        Assert.True(verdict.WhiteBackground);
        Assert.True(verdict.PlainBackground);
        Assert.True(verdict.Centered);
        Assert.False(verdict.People);
        Assert.False(verdict.Watermark);
        Assert.True(verdict.Accept, string.Join("، ", verdict.Reasons));
        Assert.True(verdict.Confidence >= ImageSubjectCheck.MinConfidence);
        Assert.Equal(3, ImageSubjectCheck.Flags(verdict));
    }

    [Fact]
    public void Product_on_plain_grey_background_is_accepted_but_not_white()
    {
        var rgb = Canvas((200, 200, 200));
        Fill(rgb, 17, 17, 46, 46, Navy);
        var verdict = ImageSubjectCheck.Check(rgb, Side, Side);
        Assert.False(verdict.WhiteBackground);
        Assert.True(verdict.PlainBackground);
        Assert.True(verdict.Accept);
        Assert.Equal(2, ImageSubjectCheck.Flags(verdict));
    }

    [Fact]
    public void Skin_coloured_subject_is_rejected_as_people()
    {
        var rgb = Canvas(White);
        Fill(rgb, 17, 17, 46, 46, SkinTone);
        var verdict = ImageSubjectCheck.Check(rgb, Side, Side);
        Assert.True(verdict.People);
        Assert.False(verdict.Accept);
        Assert.Equal(4, ImageSubjectCheck.Flags(verdict) & 4);
    }

    [Fact]
    public void Corner_logo_is_rejected_as_watermark()
    {
        var rgb = Canvas(White);
        Fill(rgb, 17, 17, 46, 46, Navy);
        Fill(rgb, 2, 2, 4, 4, (200, 0, 0));
        var verdict = ImageSubjectCheck.Check(rgb, Side, Side);
        Assert.True(verdict.Watermark);
        Assert.False(verdict.Accept);
        Assert.Equal(8, ImageSubjectCheck.Flags(verdict) & 8);
    }

    [Fact]
    public void Busy_scene_is_lifestyle_and_rejected_in_strict_mode()
    {
        var random = new Random(28);
        var rgb = new byte[Side * Side * 3];
        random.NextBytes(rgb);
        var verdict = ImageSubjectCheck.Check(rgb, Side, Side, strict: true);
        Assert.True(verdict.Lifestyle);
        Assert.False(verdict.PlainBackground);
        Assert.False(verdict.Accept);
        Assert.Equal(16, ImageSubjectCheck.Flags(verdict) & 16);
    }

    [Fact]
    public void Tiny_subject_on_white_is_not_accepted()
    {
        var rgb = Canvas(White);
        Fill(rgb, 31, 31, 32, 32, Navy);
        Assert.False(ImageSubjectCheck.Check(rgb, Side, Side).Accept);
    }

    [Theory]
    [InlineData(4, 4)]
    [InlineData(0, 0)]
    public void Unreadable_or_tiny_pixels_are_rejected(int width, int height)
    {
        var verdict = ImageSubjectCheck.Check(new byte[Math.Max(1, width * height * 3)], width, height);
        Assert.False(verdict.Accept);
        Assert.Equal(0d, verdict.Confidence);
    }

    [Fact]
    public void Truncated_pixel_buffer_is_rejected_without_crash() =>
        Assert.False(ImageSubjectCheck.Check(new byte[10], Side, Side).Accept);

    [Fact]
    public void Title_match_raises_confidence()
    {
        var rgb = Canvas(White);
        Fill(rgb, 17, 17, 46, 46, Navy);
        Assert.True(ImageSubjectCheck.Check(rgb, Side, Side, match: 1).Confidence > ImageSubjectCheck.Check(rgb, Side, Side, match: 0).Confidence);
    }

    [Theory]
    [InlineData(0.824, 82)]
    [InlineData(1.0, 100)]
    [InlineData(0.0, 0)]
    public void Score_is_a_rounded_percentage(double confidence, int expected) =>
        Assert.Equal(expected, ImageSubjectCheck.Score(new SubjectVerdict(true, true, .2, true, 0, false, false, false, confidence, true, [])));

    [Fact]
    public void IsSkin_and_IsWhite_classify_reference_colours()
    {
        Assert.True(ImageSubjectCheck.IsSkin((224, 172, 138)));
        Assert.False(ImageSubjectCheck.IsSkin((20, 40, 120)));
        Assert.False(ImageSubjectCheck.IsSkin((128, 128, 128)));
        Assert.True(ImageSubjectCheck.IsWhite((250, 250, 248)));
        Assert.False(ImageSubjectCheck.IsWhite((250, 230, 200)));
    }

    // ── عکس دسته ────────────────────────────────────────────────

    [Fact]
    public void Category_photo_queries_ask_for_the_subject_on_white()
    {
        var subject = new CategoryPhotoSubject(Guid.NewGuid(), "هدفون", "headphones");
        Assert.Contains("headphones white background", CategorySubjectImages.EnglishQueries(subject));
        Assert.Contains(CategorySubjectImages.PersianQueries(subject), q => q.Contains("هدفون", StringComparison.Ordinal));
        Assert.Empty(CategorySubjectImages.EnglishQueries(new CategoryPhotoSubject(Guid.NewGuid(), "هدفون", "هدفون")));
    }

    [Theory]
    [InlineData("Black headphones isolated on white", "https://upload.wikimedia.org/a/b.jpg", true)]
    [InlineData("Woman wearing headphones", "https://upload.wikimedia.org/a/b.jpg", false)]
    [InlineData("headphones", "https://www.shutterstock.com/x.jpg", false)]
    [InlineData("headphones", "javascript:alert(1)", false)]
    [InlineData("headphones", null, false)]
    public void Category_photo_hits_are_filtered(string title, string? url, bool expected) =>
        Assert.Equal(expected, CategorySubjectImages.IsUsefulHit(title, url));

    [Theory]
    [InlineData(800, 800, true)]
    [InlineData(1600, 900, true)]
    [InlineData(1600, 400, false)]
    [InlineData(300, 300, false)]
    [InlineData(800, 0, false)]
    public void Category_photo_size_rules(int width, int height, bool expected) =>
        Assert.Equal(expected, CategorySubjectImages.IsSizeOk(width, height));

    [Fact]
    public void Category_photo_file_names_carry_the_marker()
    {
        var name = CategorySubjectImages.FileName("headphones", "1a2b3c4d");
        Assert.Contains(CategorySubjectImages.Marker, name, StringComparison.Ordinal);
        Assert.EndsWith(".webp", name, StringComparison.Ordinal);
        Assert.True(CategorySubjectImages.IsSubject(name));
        Assert.False(CategorySubjectImages.IsSubject("headphones-photo-1a2b.webp"));
        Assert.False(CategorySubjectImages.IsSubject("../x-photo-w-1.webp"));
        Assert.StartsWith("category-photo-w", CategorySubjectImages.FileName("هدفون", "1a2b3c4d"), StringComparison.Ordinal);
    }

    // ── کارهای امروز ────────────────────────────────────────────

    [Fact]
    public void Today_plan_orders_customer_money_work_first_and_skips_zero()
    {
        var tasks = TodayPlan.Build(new TodayInputs(OrdersToConfirm: 2, ReadyToPublish: 5, OutOfStock: 0, PendingWithdrawals: 1, PendingComments: 3));
        Assert.Equal(new[] { "confirm", "withdraw", "ready", "comments" }, tasks.Select(t => t.Key).ToArray());
        Assert.StartsWith("۲ سفارش", tasks[0].Title, StringComparison.Ordinal);
        Assert.All(tasks, t => Assert.StartsWith("/admin", t.Url, StringComparison.Ordinal));
    }

    [Fact]
    public void Today_plan_hides_sections_without_permission_and_caps_the_list()
    {
        Assert.Empty(TodayPlan.Build(new TodayInputs()));
        var many = TodayPlan.Build(new TodayInputs(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1));
        Assert.Equal(TodayPlan.Max, many.Count);
        Assert.True(many.Select(t => t.Priority).SequenceEqual(many.Select(t => t.Priority).Order()));
    }

    // ── جست‌وجوی بی‌نتیجه ← جایگزین ─────────────────────────────

    [Fact]
    public void Relaxed_queries_drop_details_and_never_repeat_the_query()
    {
        var relaxed = SearchAlternatives.RelaxedQueries("هدفون بی سیم سامسونگ مشکی");
        Assert.NotEmpty(relaxed);
        Assert.True(relaxed.Count <= SearchAlternatives.MaxRelaxed);
        Assert.DoesNotContain("هدفون بی سیم سامسونگ مشکی", relaxed);
        Assert.Contains("سامسونگ", relaxed);
        Assert.Empty(SearchAlternatives.RelaxedQueries("هدفون"));
        Assert.Empty(SearchAlternatives.RelaxedQueries(null));
    }

    [Fact]
    public void Stop_words_are_ignored()
    {
        var words = SearchAlternatives.Words("خرید هدفون با قیمت ارزان");
        Assert.Equal(new[] { "هدفون" }, words.ToArray());
    }

    [Fact]
    public void Matching_categories_by_word_prefix()
    {
        var categories = new[] { (Guid.NewGuid(), "کتری و چای ساز", "kettle"), (Guid.NewGuid(), "قهوه ساز", "coffee"), (Guid.NewGuid(), "کتری و چای ساز", "kettle") };
        var found = SearchAlternatives.MatchCategories("کتری استیل", categories);
        Assert.Single(found);
        Assert.Equal("kettle", found[0].Slug);
        Assert.Empty(SearchAlternatives.MatchCategories("<script>", categories));
    }

    // ── امنیت PDF ───────────────────────────────────────────────

    private static byte[] Pdf(string body) => Encoding.Latin1.GetBytes("%PDF-1.4\n" + body + "\n" + new string(' ', 80) + "\n%%EOF\n");

    [Fact]
    public void Plain_pdf_is_accepted() => Assert.Null(PdfSafety.Problem(Pdf("1 0 obj << /Type /Catalog >> endobj")));

    [Theory]
    [InlineData("<< /OpenAction << /S /JavaScript /JS (app.alert(1)) >> >>")]
    [InlineData("<< /S /Launch /F (cmd.exe) >>")]
    [InlineData("<< /Type /EmbeddedFile >>")]
    [InlineData("<< /XFA 5 0 R >>")]
    [InlineData("<< /S /SubmitForm >>")]
    public void Active_content_pdf_is_rejected(string body) => Assert.NotNull(PdfSafety.Problem(Pdf(body)));

    [Fact]
    public void Truncated_or_tiny_pdf_is_rejected()
    {
        Assert.NotNull(PdfSafety.Problem(Encoding.Latin1.GetBytes("%PDF-1.4")));
        Assert.NotNull(PdfSafety.Problem(Encoding.Latin1.GetBytes("%PDF-1.4\n" + new string('x', 200))));
    }

    // ── آمادهٔ انتشار ───────────────────────────────────────────

    [Fact]
    public void Ready_gaps_list_every_missing_part_and_empty_means_ready()
    {
        Assert.Empty(ProductCompleteness.ReadyGaps(false, 100_000, 1, 600, 20, "Anker Power Bank"));
        var gaps = ProductCompleteness.ReadyGaps(true, 0, 0, 599, 19, " ");
        Assert.Equal(6, gaps.Count);
        Assert.Equal("قیمت", gaps[1]);
        Assert.Equal("عکس", gaps[2]);
    }

    private static Product Draft(Action<Product>? change = null)
    {
        var product = new Product
        {
            Name = "پاوربانک آزمون",
            Slug = "p",
            SKU = "S",
            IsActive = false,
            PublishedAt = null,
            Price = 900_000,
            Description = new string('ب', 600),
            ShortDescription = new string('پ', 20),
            EnglishName = "Test Power Bank",
            Category = new Category { Name = "پاوربانک", Slug = "power-bank", IsSystem = false },
            Images = [new ProductImage { ImageName = "a.webp" }],
        };
        change?.Invoke(product);
        return product;
    }

    [Fact]
    public void ReadyToPublish_has_no_feature_count_requirement()
    {
        var ready = ProductCompleteness.ReadyToPublish.Compile();
        var product = Draft();
        Assert.Empty(product.Features);
        Assert.True(ready(product));
    }

    [Theory]
    [InlineData("system")]
    [InlineData("price")]
    [InlineData("image")]
    [InlineData("description")]
    [InlineData("short")]
    [InlineData("english")]
    [InlineData("published")]
    [InlineData("active")]
    public void ReadyToPublish_fails_when_any_requirement_is_missing(string missing)
    {
        var ready = ProductCompleteness.ReadyToPublish.Compile();
        var product = Draft(p =>
        {
            switch (missing)
            {
                case "system": p.Category.IsSystem = true; break;
                case "price": p.Price = 0; break;
                case "image": p.Images.Clear(); break;
                case "description": p.Description = new string('ب', 599); break;
                case "short": p.ShortDescription = "کوتاه"; break;
                case "english": p.EnglishName = ""; break;
                case "published": p.PublishedAt = DateTimeOffset.UtcNow; break;
                case "active": p.IsActive = true; break;
            }
        });
        Assert.False(ready(product));
    }

    // ── جهش ناگهانی قیمت تأمین‌کننده ─────────────────────────────

    [Theory]
    [InlineData(400_000, 700_000, true)]
    [InlineData(400_000, 600_000, false)]
    [InlineData(400_000, 600_001, true)]
    [InlineData(null, 9_000_000, false)]
    [InlineData(0, 9_000_000, false)]
    [InlineData(400_000, 100_000, false)]
    public void Price_jump_above_the_cap_needs_review(int? oldCost, int newCost, bool expected) =>
        Assert.Equal(expected, SupplierRepricePolicy.Default.ExceedsJump(oldCost, newCost));

    [Fact]
    public void Zero_cap_disables_the_jump_alert() =>
        Assert.False((SupplierRepricePolicy.Default with { MaxJumpPercent = 0 }).ExceedsJump(100, 10_000));

    // ── گزارش نقض CSP ───────────────────────────────────────────

    [Fact]
    public void Csp_reports_are_recorded_in_both_formats_and_cleaned()
    {
        var host = "https://r28-" + Guid.NewGuid().ToString("N")[..8] + ".example";
        var now = DateTimeOffset.UtcNow;
        var legacy = "{\"csp-report\":{\"effective-directive\":\"script-src-elem\",\"blocked-uri\":\"" + host + "/x.js?token=secret\",\"document-uri\":\"https://shop.fratak.ir/cart?id=1\"}}";
        Assert.Equal(1, CspReports.Record(legacy, now));
        Assert.Equal(1, CspReports.Record(legacy, now));
        var modern = "[{\"type\":\"csp-violation\",\"body\":{\"effectiveDirective\":\"img-src\",\"blockedURL\":\"" + host + "/a.png\",\"documentURL\":\"https://shop.fratak.ir/\"}}]";
        Assert.Equal(1, CspReports.Record(modern, now));

        var entries = CspReports.Snapshot(1000).Where(e => e.Blocked == host).ToList();
        var script = Assert.Single(entries, e => e.Directive == "script-src-elem");
        Assert.Equal(2, script.Count);
        Assert.Equal("/cart", script.Page);
        Assert.DoesNotContain(entries, e => e.Blocked.Contains("secret", StringComparison.Ordinal));
        Assert.Contains(entries, e => e.Directive == "img-src");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"other\":1}")]
    [InlineData("[1,2,3]")]
    public void Bad_csp_reports_are_ignored(string json) => Assert.Equal(0, CspReports.Record(json, DateTimeOffset.UtcNow));

    [Fact]
    public void Oversized_csp_report_is_ignored() =>
        Assert.Equal(0, CspReports.Record("{\"csp-report\":{\"blocked-uri\":\"" + new string('a', CspReports.MaxBody) + "\"}}", DateTimeOffset.UtcNow));

    [Theory]
    [InlineData("inline", "inline")]
    [InlineData("eval", "eval")]
    [InlineData("https://cdn.evil.com/x.js?token=1", "https://cdn.evil.com")]
    [InlineData(null, "(نامعلوم)")]
    [InlineData("<script>", "(نامعلوم)")]
    public void Csp_source_is_reduced_to_origin_or_keyword(string? value, string expected) =>
        Assert.Equal(expected, CspReports.CleanSource(value));

    // ── آماده‌سازی خودکار ───────────────────────────────────────

    [Fact]
    public void Draft_image_needs_trusted_or_same_model_and_high_confidence()
    {
        var trusted = new FoundImage("a.webp", 800, 800, "digikala", true, Confidence: 0.8, Quality: 60);
        var unknown = new FoundImage("b.webp", 800, 800, "bing", false, Confidence: 0.95);
        var weak = new FoundImage("c.webp", 800, 800, "digikala", true, Confidence: 0.5);
        var same = new FoundImage("d.webp", 800, 800, "bing", false, Model: ModelVerdict.Same, Confidence: 0.9);
        Assert.Equal("d.webp", SmartOrchestrator.PickDraftImage([trusted, unknown, weak, same])?.FileName);
        Assert.Equal("a.webp", SmartOrchestrator.PickDraftImage([trusted, unknown, weak])?.FileName);
        Assert.Null(SmartOrchestrator.PickDraftImage([unknown, weak]));
        Assert.Null(SmartOrchestrator.PickDraftImage([]));
    }

    [Fact]
    public void Short_description_comes_from_the_first_sentences_without_headings()
    {
        var text = SmartOrchestrator.ShortFromBody("<h2>معرفی</h2><p>این کتری برقی یک و هفت دهم لیتری است. بدنه استیل دارد و زود جوش می‌آید. جمله سوم بلند.</p>");
        Assert.NotNull(text);
        Assert.StartsWith("این کتری برقی", text, StringComparison.Ordinal);
        Assert.DoesNotContain("معرفی", text, StringComparison.Ordinal);
        Assert.True(text.Length <= 251);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<p>کوتاه</p>")]
    [InlineData("<h2>فقط یک تیتر طولانی بدون هیچ متنی</h2>")]
    public void Short_description_is_null_for_thin_bodies(string? html) => Assert.Null(SmartOrchestrator.ShortFromBody(html));

    [Fact]
    public void Short_description_of_one_huge_sentence_is_cut_at_a_word()
    {
        var text = SmartOrchestrator.ShortFromBody("<p>" + string.Join(' ', Enumerable.Repeat("واژه", 120)) + "</p>");
        Assert.NotNull(text);
        Assert.True(text.Length <= 251);
        Assert.EndsWith("…", text, StringComparison.Ordinal);
    }

    // ── جست‌وجو با عکس ──────────────────────────────────────────

    [Fact]
    public void Broken_signatures_are_not_usable()
    {
        Assert.False(ImageSearchService.IsUsable(0, null));
        Assert.False(ImageSearchService.IsUsable(0, ImageSignature.Pack(new[] { (0L, 0L) })));
        Assert.True(ImageSearchService.IsUsable(0, ImageSignature.Pack(new[] { (0L, 7L) })));
        Assert.True(ImageSearchService.IsUsable(12345, null));
    }

    [Fact]
    public void Approximate_ranking_never_repeats_exact_matches_and_is_capped()
    {
        var query = new ImageSignature.Signature(0x0F0F0F0F0F0F0F0FL, Enumerable.Repeat((byte)8, ImageSignature.Bins).ToArray(), 0x00FF00FF00FF00FFL);
        var exact = new ImageSignature.Stored(Guid.NewGuid(), new[] { (query.Hash, query.PHash) }, query.Colors);
        var candidates = Enumerable.Range(0, 10).Select(_ => exact with { ProductId = Guid.NewGuid() }).Prepend(exact).ToList();
        Assert.Contains(exact.ProductId, ImageSearchService.Rank(new[] { query }, candidates));
        // همهٔ این‌ها بالای حد اصلی‌اند ← در «تقریبی» تکرار نمی‌شوند.
        Assert.Empty(ImageSearchService.RankApproximate(new[] { query }, candidates));
        Assert.True(ImageSearchService.Rank(new[] { query }, candidates).Count <= ImageSearchService.MaxResults);
        Assert.Empty(ImageSearchService.RankApproximate(new[] { query }, Array.Empty<ImageSignature.Stored>()));
    }

    [Theory]
    [InlineData("Samsung Galaxy A55", true)]
    [InlineData("گوشی سامسونگ", false)]
    [InlineData("Samsung گلکسی", false)]
    [InlineData("12", false)]
    [InlineData(" ", false)]
    [InlineData(null, false)]
    public void Usable_english_name_rules(string? name, bool expected) => Assert.Equal(expected, ProductNaming.IsUsableEnglish(name));
}
