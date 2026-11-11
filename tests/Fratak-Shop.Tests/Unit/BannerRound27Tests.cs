using System.Text.Json.Nodes;
using Fratak_Shop.Core.Services.Design;
using Fratak_Shop.Data.Entities.Design;
using Fratak_Shop.Web.Infrastructure.Design;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// دور ۲۷ – بازطراحی بنرها: «عکس واقعی همیشه» (کالای دوربری‌شده، عکس فضاسازی دسته)، چیدمان‌های تازه (ویترین، عکس تمام‌قاب، کاشی شیشه‌ای)،
/// حرکت‌های تازهٔ نسخهٔ زنده (هاله، عمق پارالاکس، Ken Burns) و ابزار «حذف زمینه».
/// ترتیب: بهترین حالت ← حالت‌های مرزی/جایگزین ← حمله و ورودی خراب.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Round27")]
public sealed class BannerRound27Tests
{
    private static DesignImage Cut(string name, int w = 600, int h = 700) => new($"/uploads/cutouts/products-{name}.webp", w, h) { Cutout = true };

    private static DesignImage Plain(string name, int w = 800, int h = 800) => new($"/uploads/products/{name}.webp", w, h);

    private static readonly DesignImage Scene = new("/uploads/categories/headphone-photo-3fa1.webp", 1600, 900);

    private static JsonArray Objects(string json) => (JsonArray)JsonNode.Parse(json)!["objects"]!;

    private static IEnumerable<JsonObject> All(string json) => Objects(json).OfType<JsonObject>();

    private static string? Str(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static double Num(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;

    private static bool Flag(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static DesignSpec Spec(string layout, DesignImage? image = null, IReadOnlyList<DesignImage>? gallery = null, DesignImage? photo = null, string? badge = "۲۰٪ تخفیف") =>
        new(layout, "ocean", "حراج بزرگ هدفون", "تخفیف واقعی روی کالاهای موجود", badge, "خرید کنید", image, "۱۲۰٬۰۰۰ تومان", null, "حراج", gallery, photo);

    // ───────────── قالب‌ها: بهترین حالت ─────────────

    [Fact]
    public void Showcase_floats_every_cutout_with_contact_shadow_and_motion()
    {
        var gallery = new[] { Cut("a"), Cut("b"), Cut("c") };
        var json = DesignTemplates.Build(Spec("showcase", gallery[0], gallery), 1920, 420);
        var images = All(json).Where(o => Str(o, "type") == "Image").ToList();

        Assert.Equal(3, images.Count);
        Assert.All(images, i => Assert.True(Flag(i, "fsCutout")));
        Assert.All(images, i => Assert.Equal("hero", Str(i, "fsAnim")));
        Assert.All(images, i => Assert.InRange(Num(i, "fsDepth"), 2, 3));
        // سایهٔ تماس بیضی (هالهٔ شعاعی با scaleY کوچک) زیر هر کالا
        var shadows = All(json).Where(o => Str(o, "fsRole") == "shadow").ToList();
        Assert.Equal(3, shadows.Count);
        Assert.All(shadows, s => Assert.Equal("radial", Str((JsonObject)s["fill"]!, "type")));
        Assert.All(shadows, s => Assert.True(Num(s, "scaleY") is > 0 and < 0.5));
        // تیتر با «پرده‌برداری» و یک سکوی بیضی
        Assert.Contains(All(json), o => Str(o, "fsRole") == "headline" && Str(o, "fsAnim") == "reveal");
    }

    [Fact]
    public void Photo_layout_uses_category_photo_full_bleed_with_palette_scrim()
    {
        var json = DesignTemplates.Build(Spec("photo", Cut("a"), new[] { Cut("a") }, Scene), 1920, 420);
        var list = All(json).ToList();
        var photo = Assert.Single(list, o => Flag(o, "fsPhoto"));

        Assert.Equal(Scene.Url, Str(photo, "src"));
        Assert.Equal("kenburns", Str(photo, "fsAnim"));
        // عکس کل قاب را می‌پوشاند (cover)
        Assert.True(Num(photo, "left") <= 0 && Num(photo, "top") <= 0);
        Assert.True(Num(photo, "left") + Num(photo, "width") * Num(photo, "scaleX") >= 1920);
        Assert.True(Num(photo, "top") + Num(photo, "height") * Num(photo, "scaleY") >= 420);
        Assert.Contains(list, o => Str(o, "fsRole") == "scrim");
        // کالای دوربری‌شده روی عکس شناور است
        Assert.Contains(list, o => Flag(o, "fsCutout"));
        // تیتر با رنگ متن پالت (پرده به رنگ پالت است)
        var headline = Assert.Single(list, o => Str(o, "fsRole") == "headline");
        Assert.Equal(DesignPalettes.Find("ocean").Ink, Str(headline, "fill"));
    }

    [Fact]
    public void Collage_tiles_are_rotated_glass_cards_with_their_images()
    {
        var gallery = new[] { Plain("a"), Plain("b"), Plain("c") };
        var json = DesignTemplates.Build(Spec("collage", gallery[0], gallery), 1920, 420);
        var cards = All(json).Where(o => Str(o, "fsRole") == "card").ToList();

        Assert.Equal(3, cards.Count);
        Assert.Contains(cards, c => Num(c, "angle") < 0);
        Assert.Contains(cards, c => Num(c, "angle") > 0);
        Assert.Equal(3, All(json).Count(o => Str(o, "type") == "Image"));
    }

    [Fact]
    public void Plain_product_photo_sits_in_white_card_not_raw_on_colour()
    {
        var json = DesignTemplates.Build(Spec("split", Plain("a")), 1920, 420);
        var list = All(json).ToList();
        var image = Assert.Single(list, o => Str(o, "type") == "Image");
        var card = Assert.Single(list, o => Str(o, "fsRole") == "card");

        Assert.False(Flag(image, "fsCutout"));
        Assert.Equal("#ffffff", Str(card, "fill"));
        Assert.True(list.IndexOf(card) < list.IndexOf(image));
    }

    [Fact]
    public void Center_layout_puts_cutouts_on_both_edges()
    {
        var json = DesignTemplates.Build(Spec("center", Cut("a"), new[] { Cut("a"), Cut("b") }, badge: null), 1920, 420);
        var images = All(json).Where(o => Str(o, "type") == "Image").ToList();

        Assert.Equal(2, images.Count);
        Assert.Contains(images, i => Num(i, "left") < 1920 * 0.2);
        Assert.Contains(images, i => Num(i, "left") > 1920 * 0.8);
    }

    [Fact]
    public void Festive_with_several_cutouts_becomes_podium_showcase()
    {
        var gallery = new[] { Cut("a"), Cut("b") };
        var json = DesignTemplates.Build(Spec("festive", gallery[0], gallery), 1920, 420);
        Assert.Equal(2, All(json).Count(o => Flag(o, "fsCutout")));
    }

    // ───────────── قالب‌ها: جایگزینی بی‌نقص وقتی عکس نیست ─────────────

    [Fact]
    public void Photo_layout_without_photo_falls_back_to_showcase_or_pattern()
    {
        var withCutout = DesignTemplates.Build(Spec("photo", Cut("a")), 1920, 420);
        Assert.DoesNotContain(All(withCutout), o => Flag(o, "fsPhoto"));
        Assert.Contains(All(withCutout), o => Flag(o, "fsCutout"));

        var empty = DesignTemplates.Build(Spec("photo"), 1920, 420);
        Assert.DoesNotContain(All(empty), o => Str(o, "type") == "Image");
        Assert.Single(All(empty), o => Str(o, "fsRole") == "headline");
    }

    [Fact]
    public void Legacy_photo_in_image_field_still_becomes_background()
    {
        // طرح‌های دور ۲۶ عکس فضاسازی را در Image می‌گذاشتند.
        var json = DesignTemplates.Build(Spec("photo", Scene), 1920, 420);
        var photo = Assert.Single(All(json), o => Flag(o, "fsPhoto"));
        Assert.Equal(Scene.Url, Str(photo, "src"));
    }

    [Fact]
    public void Showcase_of_plain_photos_becomes_collage_and_collage_of_one_becomes_showcase()
    {
        var plain = new[] { Plain("a"), Plain("b") };
        var showcase = DesignTemplates.Build(Spec("showcase", plain[0], plain), 1920, 420);
        Assert.Equal(2, All(showcase).Count(o => Str(o, "fsRole") == "card" && Num(o, "angle") != 0));

        var single = DesignTemplates.Build(Spec("collage", Cut("a"), new[] { Cut("a") }), 1920, 420);
        Assert.DoesNotContain(All(single), o => Str(o, "fsRole") == "card");
        Assert.Single(All(single), o => Flag(o, "fsCutout"));
    }

    [Fact]
    public void Sale_without_badge_never_shows_an_empty_burst()
    {
        var json = DesignTemplates.Build(Spec("sale", Cut("a"), badge: null), 1920, 420);
        Assert.DoesNotContain(All(json), o => Str(o, "fsRole") is "badge-bg" or "badge");
    }

    [Fact]
    public void Invalid_image_sizes_are_ignored()
    {
        var broken = new DesignImage("/uploads/products/x.webp", 0, 0) { Cutout = true };
        var json = DesignTemplates.Build(Spec("showcase", broken, new[] { broken }), 1920, 420);
        Assert.DoesNotContain(All(json), o => Str(o, "type") == "Image");
    }

    public static TheoryData<string, int, int> LayoutSizes()
    {
        var data = new TheoryData<string, int, int>();
        foreach (var (key, _) in DesignTemplates.Layouts)
        {
            data.Add(key, 1920, 420);
            data.Add(key, 1920, 300);
            data.Add(key, 960, 320);
            data.Add(key, 1080, 1080);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(LayoutSizes))]
    public void Every_layout_with_real_pictures_keeps_text_inside_and_readable(string layout, int w, int h)
    {
        var gallery = new[] { Cut("a"), Plain("b"), Cut("c") };
        var json = DesignTemplates.Build(Spec(layout, gallery[0], gallery, Scene), w, h);
        var texts = All(json).Where(o => Str(o, "type") == "Textbox").ToList();

        Assert.Single(texts, t => Str(t, "fsRole") == "headline");
        foreach (var t in texts)
        {
            Assert.True(Num(t, "left") >= -0.5, Str(t, "fsRole"));
            Assert.True(Num(t, "top") >= -0.5, Str(t, "fsRole"));
            Assert.True(Num(t, "left") + Num(t, "width") <= w + 0.5, Str(t, "fsRole"));
            Assert.True(Num(t, "top") + Num(t, "fontSize") <= h + 0.5, Str(t, "fsRole"));
            Assert.True(Num(t, "fontSize") >= 10, Str(t, "fsRole"));
        }
        // هر طرح زنده هم قابل تبدیل است (نسخهٔ متحرک سایت)
        Assert.NotNull(DesignLive.Html(json, BannerMotion.Soft));
    }

    [Fact]
    public void Rgba_is_culture_safe_and_clamped()
    {
        Assert.Equal("rgba(255,0,0,0.5)", DesignTemplates.Rgba("#ff0000", 0.5));
        Assert.Equal("rgba(0,0,0,1)", DesignTemplates.Rgba("not-a-colour", 7));
        Assert.Equal("rgba(17,34,51,0)", DesignTemplates.Rgba("#123", -1));
    }

    // ───────────── نسخهٔ زنده ─────────────

    private static string Design(string objectJson) =>
        "{\"fsWidth\":1920,\"fsHeight\":420,\"background\":\"#ffffff\",\"objects\":[" + objectJson + "]}";

    [Fact]
    public void Concentric_radial_glow_becomes_css_radial_gradient()
    {
        var glow = "{\"type\":\"circle\",\"left\":100,\"top\":50,\"radius\":100,\"scaleY\":0.2,\"fill\":{\"type\":\"radial\",\"gradientUnits\":\"pixels\"," +
                   "\"coords\":{\"x1\":100,\"y1\":100,\"r1\":0,\"x2\":100,\"y2\":100,\"r2\":100},\"colorStops\":[{\"offset\":0,\"color\":\"rgba(2,6,23,0.4)\"},{\"offset\":1,\"color\":\"rgba(2,6,23,0)\"}]}}";
        var html = DesignLive.Html(Design(glow), BannerMotion.Soft);

        Assert.NotNull(html);
        Assert.Contains("radial-gradient(closest-side,rgba(2,6,23,0.4) 0%,rgba(2,6,23,0) 100%)", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Off_centre_radial_gradient_is_dropped_not_broken()
    {
        var glow = "{\"type\":\"circle\",\"left\":100,\"top\":50,\"radius\":100,\"fill\":{\"type\":\"radial\",\"gradientUnits\":\"pixels\"," +
                   "\"coords\":{\"x1\":10,\"y1\":10,\"r1\":0,\"x2\":10,\"y2\":10,\"r2\":100},\"colorStops\":[{\"offset\":0,\"color\":\"#ffffff\"},{\"offset\":1,\"color\":\"#000000\"}]}}";
        var html = DesignLive.Html(Design(glow), BannerMotion.Soft);
        Assert.NotNull(html);
        Assert.DoesNotContain("radial-gradient", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Vertical_gradient_on_squashed_ellipse_spans_whole_height()
    {
        // سکوی بیضی: مختصات فابریک بدون مقیاس (۰ تا ۲۰۰) ولی شیء با scaleY=0.16 فقط ۳۲ پیکسل ارتفاع دارد.
        var stage = "{\"type\":\"circle\",\"left\":100,\"top\":50,\"radius\":100,\"scaleY\":0.16,\"fill\":{\"type\":\"linear\",\"gradientUnits\":\"pixels\"," +
                    "\"coords\":{\"x1\":0,\"y1\":0,\"x2\":0,\"y2\":200},\"colorStops\":[{\"offset\":0,\"color\":\"#ffffff\"},{\"offset\":1,\"color\":\"#000000\"}]}}";
        var html = DesignLive.Html(Design(stage), BannerMotion.Soft);
        Assert.NotNull(html);
        Assert.Contains("linear-gradient(180deg,#ffffff 0%,#000000 100%)", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1", " fsb-z1")]
    [InlineData("3", " fsb-z3")]
    [InlineData("4", null)]
    [InlineData("1.5", null)]
    [InlineData("-2", null)]
    [InlineData("\"3\"", null)]
    public void Parallax_depth_accepts_only_one_to_three(string depth, string? expected)
    {
        var rect = "{\"type\":\"rect\",\"left\":0,\"top\":0,\"width\":100,\"height\":100,\"fill\":\"#ff0000\",\"fsDepth\":" + depth + "}";
        var html = DesignLive.Html(Design(rect), BannerMotion.Soft);
        Assert.NotNull(html);
        if (expected is null)
            Assert.DoesNotContain("fsb-z", html, StringComparison.Ordinal);
        else
            Assert.Contains(expected, html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("hero")]
    [InlineData("kenburns")]
    [InlineData("reveal")]
    [InlineData("twinkle")]
    [InlineData("breathe")]
    [InlineData("sweep")]
    [InlineData("shadow")]
    public void New_motions_map_to_css_classes(string anim)
    {
        var rect = "{\"type\":\"rect\",\"left\":0,\"top\":0,\"width\":100,\"height\":100,\"fill\":\"#ff0000\",\"fsAnim\":\"" + anim + "\"}";
        var html = DesignLive.Html(Design(rect), BannerMotion.Soft);
        Assert.Contains("fsb-a-" + anim, html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("explode")]
    [InlineData("hero\\\" onmouseover=\\\"x")]
    [InlineData("kenburns;background:url(x)")]
    public void Unknown_or_injected_motion_is_ignored(string anim)
    {
        var rect = "{\"type\":\"rect\",\"left\":0,\"top\":0,\"width\":100,\"height\":100,\"fill\":\"#ff0000\",\"fsAnim\":\"" + anim + "\"}";
        var html = DesignLive.Html(Design(rect), BannerMotion.Soft);
        Assert.NotNull(html);
        Assert.DoesNotContain("fsb-a-", html, StringComparison.Ordinal);
        Assert.DoesNotContain("onmouseover", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Full_showcase_template_becomes_live_html_with_images_depth_and_motion()
    {
        var gallery = new[] { Cut("a"), Cut("b") };
        var html = DesignLive.Html(DesignTemplates.Build(Spec("showcase", gallery[0], gallery), 1920, 420), BannerMotion.Soft);

        Assert.NotNull(html);
        Assert.Contains("<img src=\"/uploads/cutouts/products-a.webp\"", html, StringComparison.Ordinal);
        Assert.Contains("fsb-a-hero", html, StringComparison.Ordinal);
        Assert.Contains("fsb-z3", html, StringComparison.Ordinal);
        Assert.Contains("radial-gradient(closest-side", html, StringComparison.Ordinal);
        Assert.Contains("drop-shadow(", html, StringComparison.Ordinal);
    }

    // ───────────── ایده ← طرح: چیدمان بر اساس عکس‌ها ─────────────

    private static IdeaProduct Product(string name, string image) =>
        new(Guid.NewGuid(), name, "p-" + Guid.NewGuid().ToString("N")[..6], image, 800, 800, 100_000, 80_000, 5, 10, 3, "هدفون", "headphone", DateTimeOffset.UtcNow);

    private static BriefPlan CampaignPlan()
    {
        var products = new List<IdeaProduct> { Product("کالای اول", "/uploads/products/a.webp"), Product("کالای دوم", "/uploads/products/b.webp") };
        var category = new BriefCategory(Guid.NewGuid(), "هدفون", "headphone", 12);
        return DesignBrief.Plan("جشنواره پاییزه", DesignBrief.Parse("جشنواره پاییزه"), new BriefData(products, category, null, "فراتک"), DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Brief_with_cutouts_leads_with_showcase_and_every_variant_has_pictures()
    {
        var gallery = new[] { Cut("a"), Cut("b") };
        var ideas = DesignBrief.Variants(CampaignPlan(), null, gallery[0], "s1", gallery);

        Assert.Equal(3, ideas.Count);
        Assert.Equal("showcase", ideas[0].Spec.Layout);
        Assert.Equal(3, ideas.Select(i => i.Key).Distinct().Count());
        Assert.All(ideas.Where(i => i.Spec.Layout != "pattern"), i => Assert.NotNull(i.Spec.Image));
    }

    [Fact]
    public void Brief_with_plain_photos_leads_with_collage()
    {
        var gallery = new[] { Plain("a"), Plain("b") };
        var ideas = DesignBrief.Variants(CampaignPlan(), null, gallery[0], "s2", gallery);
        Assert.Equal("collage", ideas[0].Spec.Layout);
    }

    [Fact]
    public void Brief_with_category_photo_leads_with_photo_and_keeps_the_product()
    {
        var gallery = new[] { Cut("a"), Cut("b") };
        var ideas = DesignBrief.Variants(CampaignPlan(), null, gallery[0], "s3", gallery, Scene);

        Assert.Equal("photo", ideas[0].Spec.Layout);
        Assert.Equal(Scene, ideas[0].Spec.Photo);
        Assert.Equal(gallery[0], ideas[0].Spec.Image);
        Assert.All(ideas.Skip(1), i => Assert.Null(i.Spec.Photo));
    }

    // ───────────── دوربری (ImageColors.Cutout) ─────────────

    private static SKBitmap WhitePhoto(int size = 200)
    {
        var bitmap = new SKBitmap(size, size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = new SKColor(20, 40, 160), IsAntialias = true };
        canvas.DrawCircle(size / 2f, size / 2f, size * 0.3f, paint);
        // سفیدیِ داخل کالا (مثل صفحهٔ گوشی) نباید شفاف شود
        using var white = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(size * 0.45f, size * 0.45f, size * 0.1f, size * 0.1f, white);
        return bitmap;
    }

    [Fact]
    public void Cutout_trims_margins_keeps_inner_white_and_softens_edges()
    {
        using var photo = WhitePhoto();
        var result = ImageColors.Cutout(photo, 900);

        Assert.NotNull(result);
        Assert.InRange(result.Width, 120, 140);   // قطر ۱۲۰ + ۲٪ حاشیه از هر طرف
        Assert.InRange(result.Height, 120, 140);
        using var decoded = SKBitmap.Decode(result.Png);
        Assert.Equal(0, decoded.GetPixel(0, 0).Alpha);                                  // گوشه = زمینه
        Assert.Equal(255, decoded.GetPixel(decoded.Width / 2, decoded.Height / 2).Alpha); // سفیدیِ داخل کالا می‌ماند
        Assert.Equal(255, decoded.GetPixel(decoded.Width / 2, decoded.Height / 4).Alpha);  // بدنهٔ کالا
        Assert.True(ImageColors.HasTransparentBorder(decoded));
        Assert.InRange(result.Coverage, 0.2, 0.4);
    }

    [Fact]
    public void Cutout_refuses_images_without_a_white_background()
    {
        using var gray = new SKBitmap(100, 100);
        using (var canvas = new SKCanvas(gray))
            canvas.Clear(new SKColor(120, 120, 120));
        Assert.Null(ImageColors.Cutout(gray));

        using var white = new SKBitmap(100, 100);
        using (var canvas = new SKCanvas(white))
            canvas.Clear(SKColors.White);
        Assert.Null(ImageColors.Cutout(white)); // چیزی برای نگه داشتن نیست
    }

    [Theory]
    [InlineData(255, 255, 255, 255, true)]
    [InlineData(240, 242, 238, 255, true)]
    [InlineData(255, 228, 225, 255, false)] // صورتی روشن = رنگ کالا
    [InlineData(200, 200, 200, 255, false)]
    [InlineData(0, 0, 0, 0, true)]          // از قبل شفاف
    [InlineData(255, 255, 255, 120, false)]
    public void Background_pixel_rule(byte r, byte g, byte b, byte a, bool expected) =>
        Assert.Equal(expected, ImageColors.IsBackground(new SKColor(r, g, b, a)));

    // ───────────── عکس‌های بنر (BannerImagery) ─────────────

    [Theory]
    [InlineData("/uploads/products/9f3c.webp", "products", "9f3c.webp")]
    [InlineData("/uploads/cutouts/products-9f3c.webp?v=2", "cutouts", "products-9f3c.webp")]
    [InlineData("/uploads/categories/headphone-photo-1a.webp", "categories", "headphone-photo-1a.webp")]
    [InlineData("/uploads/../appsettings.json", null, null)]
    [InlineData("/uploads/products/../../x.webp", null, null)]
    [InlineData("/uploads/tickets/a.webp", null, null)]          // پوشهٔ خصوصی
    [InlineData("/uploads/purchase-invoices/a.webp", null, null)]
    [InlineData("https://evil.example/a.webp", null, null)]
    [InlineData("//evil.example/uploads/products/a.webp", null, null)]
    [InlineData("/uploads/products/sub/a.webp", null, null)]
    [InlineData("/uploads/products/.hidden.webp", null, null)]
    [InlineData("\\uploads\\products\\a.webp", null, null)]
    [InlineData("", null, null)]
    public void Parse_accepts_only_simple_local_public_files(string url, string? area, string? file)
    {
        var parsed = BannerImagery.Parse(url);
        Assert.Equal(area, parsed?.Area.Name);
        Assert.Equal(file, parsed?.FileName);
    }

    [Theory]
    [InlineData("9f3c.webp", "products-9f3c")]
    [InlineData("kitchen-photo-3fa1.webp", "products-kitchen-photo-3fa1")]
    [InlineData("A1.webp", null)]
    [InlineData("a_b.webp", null)]
    [InlineData("هدفون.webp", null)]
    public void Cutout_name_is_deterministic_and_safe(string file, string? expected) =>
        Assert.Equal(expected, BannerImagery.CutoutStem(UploadArea.Products, file));

    private sealed class TempEnvironment : IWebHostEnvironment, IDisposable
    {
        public TempEnvironment()
        {
            ContentRootPath = Path.Combine(Path.GetTempPath(), "fratak-r27-" + Guid.NewGuid().ToString("N"));
            WebRootPath = Path.Combine(ContentRootPath, "wwwroot");
            Directory.CreateDirectory(WebRootPath);
        }

        public string WebRootPath { get; set; }
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Fratak-Shop.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Testing";

        public void Dispose()
        {
            try
            {
                Directory.Delete(ContentRootPath, recursive: true);
            }
            catch (IOException)
            {
                // پوشهٔ موقت؛ پاک نشدن مهم نیست
            }
        }
    }

    private static void WritePng(TempEnvironment env, string area, string name, SKBitmap bitmap)
    {
        var folder = Path.Combine(env.WebRootPath, "uploads", area);
        Directory.CreateDirectory(folder);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(folder, name), data.ToArray());
    }

    [Fact]
    public async Task Product_picture_is_cut_once_reused_and_pruned_with_its_original()
    {
        using var env = new TempEnvironment();
        var store = new UploadStore(env, NullLogger<UploadStore>.Instance);
        var imagery = new BannerImagery(store, NullLogger.Instance);
        using (var photo = WhitePhoto(400))
            WritePng(env, "products", "abc123.png", photo);

        var first = await imagery.ProductAsync("/uploads/products/abc123.png", CancellationToken.None);
        Assert.NotNull(first);
        Assert.True(first.Cutout);
        Assert.StartsWith("/uploads/cutouts/products-abc123.", first.Url, StringComparison.Ordinal);
        Assert.InRange(first.Width, 200, 300);

        var second = await imagery.ProductAsync("/uploads/products/abc123.png", CancellationToken.None);
        Assert.Equal(first.Url, second!.Url);
        Assert.Single(store.List(UploadArea.Cutouts));

        // پاک‌سازی: تا عکس اصلی هست برش می‌ماند؛ بعد از حذف آن پاک می‌شود.
        Assert.Equal(0, imagery.PruneCutouts());
        File.Delete(Path.Combine(env.WebRootPath, "uploads", "products", "abc123.png"));
        Assert.Equal(1, imagery.PruneCutouts());
        Assert.Empty(store.List(UploadArea.Cutouts));
    }

    [Fact]
    public async Task Coloured_background_photo_is_kept_as_is_and_remove_tool_explains_why()
    {
        using var env = new TempEnvironment();
        var store = new UploadStore(env, NullLogger<UploadStore>.Instance);
        var imagery = new BannerImagery(store, NullLogger.Instance);
        using (var scene = new SKBitmap(300, 200))
        {
            using (var canvas = new SKCanvas(scene))
                canvas.Clear(new SKColor(30, 120, 60));
            WritePng(env, "products", "def456.png", scene);
        }

        var picture = await imagery.ProductAsync("/uploads/products/def456.png", CancellationToken.None);
        Assert.NotNull(picture);
        Assert.False(picture.Cutout);
        Assert.Equal("/uploads/products/def456.png", picture.Url);
        Assert.Empty(store.List(UploadArea.Cutouts));

        var (image, error) = await imagery.RemoveBackgroundAsync("/uploads/products/def456.png", CancellationToken.None);
        Assert.Null(image);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("/uploads/../../etc/passwd")]
    [InlineData("https://evil.example/a.png")]
    [InlineData("/uploads/products/missing.png")]
    [InlineData(null)]
    public async Task Remove_tool_rejects_foreign_or_missing_files(string? url)
    {
        using var env = new TempEnvironment();
        var imagery = new BannerImagery(new UploadStore(env, NullLogger<UploadStore>.Instance), NullLogger.Instance);
        var (image, error) = await imagery.RemoveBackgroundAsync(url, CancellationToken.None);
        Assert.Null(image);
        Assert.NotNull(error);
        Assert.Null(await imagery.ProductAsync(url, CancellationToken.None));
    }

    [Fact]
    public void Category_photo_prefers_real_web_photo_and_skips_portrait_or_small()
    {
        using var env = new TempEnvironment();
        var imagery = new BannerImagery(new UploadStore(env, NullLogger<UploadStore>.Instance), NullLogger.Instance);
        using (var wide = new SKBitmap(1200, 600))
        {
            using (var canvas = new SKCanvas(wide))
                canvas.Clear(new SKColor(90, 60, 30));
            WritePng(env, "categories", "kitchen-photo-1a.png", wide);
        }
        using (var tall = new SKBitmap(600, 1200))
        {
            using (var canvas = new SKCanvas(tall))
                canvas.Clear(new SKColor(90, 60, 30));
            WritePng(env, "categories", "kitchen-photo-2b.png", tall);
        }

        Assert.Equal("/uploads/categories/kitchen-photo-1a.png", imagery.CategoryPhoto("kitchen-photo-2b.png", new[] { "kitchen-photo-2b.png", "kitchen-photo-1a.png" })?.Url);
        Assert.Null(imagery.CategoryPhoto("kitchen-photo-2b.png", null));
        Assert.Null(imagery.CategoryPhoto("../x.png", new[] { "../../y.png" }));
    }

    [Fact]
    public async Task Named_save_rejects_unsafe_names()
    {
        using var env = new TempEnvironment();
        var store = new UploadStore(env, NullLogger<UploadStore>.Instance);
        using var photo = WhitePhoto(100);
        using var image = SKImage.FromBitmap(photo);
        var png = image.Encode(SKEncodedImageFormat.Png, 100).ToArray();

        Assert.Null(await store.SaveImageAsAsync(UploadArea.Cutouts, "../evil", png));
        Assert.Null(await store.SaveImageAsAsync(UploadArea.Cutouts, "Upper", png));
        Assert.Null(await store.SaveImageAsAsync(UploadArea.Cutouts, "-x", png));
        Assert.Null(await store.SaveImageAsAsync(UploadArea.Cutouts, "ok", Array.Empty<byte>()));
        Assert.Null(await store.SaveImageAsAsync(UploadArea.Cutouts, "ok", new byte[] { 1, 2, 3 }));
        var saved = await store.SaveImageAsAsync(UploadArea.Cutouts, "products-ok", png);
        Assert.NotNull(saved);
        Assert.StartsWith("products-ok.", saved.FileName, StringComparison.Ordinal);
    }
}
