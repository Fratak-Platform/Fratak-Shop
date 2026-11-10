using System.Security.Claims;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Searches;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Web.Infrastructure.Auth;
using Fratak_Shop.Web.Infrastructure.Commerce;
using Microsoft.AspNetCore.Http;
using SkiaSharp;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های واحد دور ۲۴ (بستهٔ B – تجربهٔ خرید). همه بدون پایگاه داده:
/// <list type="bullet">
/// <item>تضمین قیمت (<see cref="PriceMatchRules"/>): نشانی امن (بدون javascript:، IP، localhost و دامنهٔ خودمان)، سقف اختلاف ۳۰٪، مبلغ تخفیف.</item>
/// <item>سفارش دوباره (<see cref="ReorderResult"/>): پیام کوتاه با ارقام فارسی.</item>
/// <item>آمار واقعی خرید (<see cref="SocialProofService.Describe"/>): زیر آستانه هیچ چیز نشان داده نمی‌شود (بدون ترفند).</item>
/// <item>رتبه‌بندی یادگیرنده (<see cref="SearchLearning.Key"/>): کلید نرمال عبارت.</item>
/// <item>جست‌وجو با عکس (<see cref="ImageSignature"/>، <see cref="ImageSearchService.Rank"/>): هش شکل، رنگ، امتیاز و فایل خراب/حمله.</item>
/// <item>شناسهٔ بازدیدکننده (<see cref="CommerceEndpoints.VisitorKey"/>).</item>
/// </list>
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Commerce")]
public class CommerceRound24Tests
{
    // ── تضمین قیمت ──────────────────────────────────────────────

    [Theory]
    [InlineData("https://www.digikala.com/product/dkp-1", "digikala.com")]
    [InlineData("http://Shop.Example.ir/p/1?x=2", "shop.example.ir")]
    public void PriceMatch_accepts_real_shop_urls(string url, string host)
    {
        var (ok, parsed) = PriceMatchRules.CheckUrl(url, "shop.fratak.ir");
        Assert.True(ok);
        Assert.Equal(host, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("ftp://shop.example.ir/file")]
    [InlineData("http://127.0.0.1/admin")]
    [InlineData("http://[::1]/")]
    [InlineData("http://localhost/x")]
    [InlineData("http://192.168.1.10/router")]
    [InlineData("/product/relative")]
    [InlineData("https://shop.fratak.ir/product/x")]
    [InlineData("https://www.shop.fratak.ir/product/x")]
    [InlineData("https://cdn.shop.fratak.ir/x")]
    public void PriceMatch_rejects_unsafe_or_own_urls(string? url)
    {
        var (ok, host) = PriceMatchRules.CheckUrl(url, "shop.fratak.ir");
        Assert.False(ok);
        Assert.Null(host);
    }

    [Fact]
    public void PriceMatch_rejects_very_long_urls()
    {
        var url = "https://example.ir/" + new string('a', 600);
        Assert.False(PriceMatchRules.CheckUrl(url).Ok);
    }

    [Theory]
    [InlineData("WWW.Digikala.com", "digikala.com")]
    [InlineData(" torob.com ", "torob.com")]
    [InlineData("www2.example.ir", "www2.example.ir")]
    public void PriceMatch_host_is_normalized(string input, string expected) =>
        Assert.Equal(expected, PriceMatchRules.Host(input));

    [Fact]
    public void PriceMatch_price_rules_best_to_worst()
    {
        Assert.Null(PriceMatchRules.CheckPrice(1_000_000m, 950_000m));            // اختلاف ۵٪ ← پذیرفته
        Assert.Null(PriceMatchRules.CheckPrice(1_000_000m, 700_000m));            // دقیقاً ۳۰٪ ← هنوز پذیرفته
        Assert.Contains("کمتر نیست", PriceMatchRules.CheckPrice(1_000_000m, 1_000_000m));
        Assert.Contains("کمتر نیست", PriceMatchRules.CheckPrice(1_000_000m, 1_200_000m));
        Assert.Contains("خیلی زیاد", PriceMatchRules.CheckPrice(1_000_000m, 699_000m)); // بیش از ۳۰٪ ← حمله/کالای دیگر
        Assert.NotNull(PriceMatchRules.CheckPrice(1_000_000m, 0m));
        Assert.NotNull(PriceMatchRules.CheckPrice(1_000_000m, -5m));
        Assert.NotNull(PriceMatchRules.CheckPrice(0m, 100m));
    }

    [Theory]
    [InlineData(1_200_000, 1_155_500, 44_000)]
    [InlineData(1_000_000, 950_000, 50_000)]
    [InlineData(1_000_000, 999_500, 0)]        // کمتر از هزار تومان ← صفر
    [InlineData(900_000, 950_000, 0)]          // قیمت ما تا زمان تأیید پایین آمده ← هیچ تخفیفی
    public void PriceMatch_discount_is_current_gap_rounded_down(int current, int competitor, int expected) =>
        Assert.Equal((decimal)expected, PriceMatchRules.Discount(current, competitor));

    [Fact]
    public void PriceMatch_limits_are_conservative()
    {
        Assert.Equal(3, IPriceMatchService.DailyLimit);
        Assert.Equal(30, IPriceMatchService.MaxGapPercent);
        Assert.Equal(72, IPriceMatchService.CouponHours);
    }

    // ── سفارش دوباره ────────────────────────────────────────────

    [Fact]
    public void Reorder_message_all_added()
    {
        var message = new ReorderResult(2, Array.Empty<string>()).Message;
        Assert.Equal("۲ کالا به سبد خرید اضافه شد.", message);
    }

    [Fact]
    public void Reorder_message_lists_skipped_items()
    {
        var message = new ReorderResult(1, new[] { "شارژر X" }).Message;
        Assert.StartsWith("۱ کالا به سبد خرید اضافه شد.", message);
        Assert.Contains("«شارژر X» الان موجود نیست.", message);
    }

    [Fact]
    public void Reorder_message_caps_long_skip_list()
    {
        var message = new ReorderResult(1, new[] { "الف", "ب", "پ", "ت", "ث" }).Message;
        Assert.Contains("و چند کالای دیگر", message);
        Assert.DoesNotContain("«ت»", message);
    }

    [Fact]
    public void Reorder_message_when_nothing_available()
    {
        var message = new ReorderResult(0, new[] { "الف" }).Message;
        Assert.Equal("هیچ‌کدام از کالاهای این سفارش الان موجود نیست.", message);
    }

    // ── آمار واقعی خرید ─────────────────────────────────────────

    [Fact]
    public void SocialProof_prefers_buyers_then_carts()
    {
        Assert.Equal("۱۲ نفر در ۷ روز گذشته این کالا را خریده‌اند", SocialProofService.Describe(12, 0));
        Assert.Equal("۱۲ نفر در ۷ روز گذشته این کالا را خریده‌اند", SocialProofService.Describe(12, 40));
        Assert.Equal("این کالا در سبد خرید ۶ نفر است", SocialProofService.Describe(0, 6));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 4)]
    [InlineData(-5, -5)]
    public void SocialProof_below_threshold_shows_nothing(int buyers, int carts) =>
        Assert.Null(SocialProofService.Describe(buyers, carts));

    [Fact]
    public void SocialProof_thresholds_are_exact()
    {
        Assert.NotNull(SocialProofService.Describe(ISocialProofService.MinBuyers, 0));
        Assert.Null(SocialProofService.Describe(ISocialProofService.MinBuyers - 1, ISocialProofService.MinCarts - 1));
        Assert.NotNull(SocialProofService.Describe(0, ISocialProofService.MinCarts));
    }

    // ── رتبه‌بندی یادگیرنده ─────────────────────────────────────

    [Fact]
    public void SearchKey_normalizes_arabic_letters_and_spaces()
    {
        Assert.Equal(SearchLearning.Key("هدفون بی سیم"), SearchLearning.Key("  هدفون  بي سيم "));
        Assert.Equal(SearchLearning.Key("کیف"), SearchLearning.Key("كيف"));
        Assert.Equal("iphone 15", SearchLearning.Key("IPhone ۱۵"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    public void SearchKey_too_short_is_null(string? query) => Assert.Null(SearchLearning.Key(query));

    [Fact]
    public void SearchKey_is_capped()
    {
        var key = SearchLearning.Key(new string('ب', 500));
        Assert.NotNull(key);
        Assert.True(key!.Length <= 100);
    }

    // ── جست‌وجو با عکس ──────────────────────────────────────────

    private static SKBitmap Solid(SKColor color, int size = 64)
    {
        var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(color);
        return bitmap;
    }

    /// <summary>تصویر شیب‌دار افقی (برای هش شکل). leftToRight: روشن ← تیره از چپ به راست.</summary>
    private static SKBitmap Gradient(bool leftToRight, SKColor tint)
    {
        var bitmap = new SKBitmap(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (var x = 0; x < 64; x++)
        {
            var t = leftToRight ? x / 63f : 1 - x / 63f;
            var c = new SKColor((byte)(tint.Red * (1 - t)), (byte)(tint.Green * (1 - t)), (byte)(tint.Blue * (1 - t)));
            for (var y = 0; y < 64; y++)
                bitmap.SetPixel(x, y, c);
        }
        return bitmap;
    }

    [Fact]
    public void ImageBin_handles_white_transparent_colour_and_gray()
    {
        Assert.Equal(-1, ImageSignature.Bin(SKColors.White));
        Assert.Equal(-1, ImageSignature.Bin(new SKColor(255, 0, 0, 10)));
        Assert.Equal(1, ImageSignature.Bin(new SKColor(255, 0, 0)));
        Assert.Equal(24, ImageSignature.Bin(SKColors.Black));
        Assert.InRange(ImageSignature.Bin(new SKColor(128, 128, 128)), 24, 31);
        Assert.InRange(ImageSignature.Bin(new SKColor(0, 0, 255)), 0, 23);
        Assert.NotEqual(ImageSignature.Bin(new SKColor(255, 0, 0)), ImageSignature.Bin(new SKColor(0, 0, 255)));
    }

    [Fact]
    public void ImageNormalize_scales_and_clamps()
    {
        var counts = new int[ImageSignature.Bins];
        counts[0] = 1;
        counts[1] = 9;
        var bytes = ImageSignature.Normalize(counts, 10);
        Assert.Equal(ImageSignature.Bins, bytes.Length);
        Assert.Equal(102, bytes[0]);
        Assert.Equal(255, bytes[1]); // ۹۱۸ ← سقف ۲۵۵
        Assert.All(ImageSignature.Normalize(counts, 0), b => Assert.Equal(0, b));
    }

    [Fact]
    public void ImageHamming_counts_bits()
    {
        Assert.Equal(0, ImageSignature.Hamming(0, 0));
        Assert.Equal(64, ImageSignature.Hamming(0, -1));
        Assert.Equal(1, ImageSignature.Hamming(1, 3));
        Assert.Equal(ImageSignature.Hamming(12345, 999), ImageSignature.Hamming(999, 12345));
    }

    [Fact]
    public void ImageColorSimilarity_edges()
    {
        var a = new byte[ImageSignature.Bins];
        var b = new byte[ImageSignature.Bins];
        a[0] = 200;
        b[5] = 200;
        Assert.Equal(1.0, ImageSignature.ColorSimilarity(a, a));
        Assert.Equal(0.0, ImageSignature.ColorSimilarity(a, b));
        Assert.Equal(0.0, ImageSignature.ColorSimilarity(new byte[ImageSignature.Bins], new byte[ImageSignature.Bins]));
        Assert.Equal(0.0, ImageSignature.ColorSimilarity(a, new byte[3]));
    }

    [Fact]
    public void ImageSignature_same_picture_scores_one_and_different_colour_scores_low()
    {
        using var red = Gradient(true, new SKColor(220, 30, 30));
        // تصویر دیگر: رنگ و جهت هر دو فرق دارد. (همان شکل با رنگ دیگر عمداً «شبیه» حساب می‌شود؛ مثلاً همان کالا در رنگ دیگر.)
        using var blue = Gradient(false, new SKColor(30, 30, 220));
        var r = ImageSignature.Compute(red);
        var b = ImageSignature.Compute(blue);
        Assert.NotNull(r);
        Assert.NotNull(b);
        Assert.Equal(1.0, ImageSignature.Score(r!, r!.Hash, r.Colors), 6);
        Assert.True(ImageSignature.ColorSimilarity(r.Colors, b!.Colors) < 0.5);
        Assert.True(ImageSignature.Score(r, b.Hash, b.Colors) < ImageSearchService.MinScore);
    }

    [Fact]
    public void ImageSignature_shape_hash_sees_direction()
    {
        using var forward = Gradient(true, new SKColor(40, 160, 90));
        using var backward = Gradient(false, new SKColor(40, 160, 90));
        var f = ImageSignature.Compute(forward)!;
        var w = ImageSignature.Compute(backward)!;
        Assert.True(ImageSignature.Hamming(f.Hash, w.Hash) > 32);
    }

    [Fact]
    public void ImageSignature_from_encoded_png_matches_bitmap()
    {
        using var bitmap = Gradient(true, new SKColor(200, 120, 20));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var fromBytes = ImageSignature.Compute(encoded.ToArray());
        var direct = ImageSignature.Compute(bitmap)!;
        Assert.NotNull(fromBytes);
        Assert.True(ImageSignature.Score(direct, fromBytes!.Hash, fromBytes.Colors) > 0.9);
    }

    [Fact]
    public void ImageSignature_rejects_garbage_and_empty()
    {
        Assert.Null(ImageSignature.Compute(Array.Empty<byte>()));
        Assert.Null(ImageSignature.Compute(System.Text.Encoding.UTF8.GetBytes("<script>alert(1)</script> not an image")));
        var fakePng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 }; // سرآیند PNG بدون بدنه
        Assert.Null(ImageSignature.Compute(fakePng));
    }

    [Fact]
    public void ImageSignature_white_only_picture_has_empty_colours_but_no_crash()
    {
        using var white = Solid(SKColors.White);
        var signature = ImageSignature.Compute(white);
        Assert.NotNull(signature);
        Assert.All(signature!.Colors, c => Assert.Equal(0, c));
    }

    [Fact]
    public void ImageRank_keeps_only_close_matches_best_first_and_caps()
    {
        using var red = Gradient(true, new SKColor(220, 30, 30));
        var query = ImageSignature.Compute(red)!;
        var same = Guid.NewGuid();
        var near = Guid.NewGuid();
        var far = Guid.NewGuid();
        var nearHash = query.Hash ^ 0b1111; // ۴ بیت تفاوت
        var farColors = new byte[ImageSignature.Bins];
        farColors[20] = 255;

        var ranked = ImageSearchService.Rank(query, new[]
        {
            (far, ~query.Hash, farColors),
            (near, nearHash, query.Colors),
            (same, query.Hash, query.Colors),
        });
        Assert.Equal(new[] { same, near }, ranked.ToArray());

        var many = Enumerable.Range(0, 20).Select(_ => (Guid.NewGuid(), query.Hash, query.Colors)).ToList();
        Assert.Equal(ImageSearchService.MaxResults, ImageSearchService.Rank(query, many).Count);
        Assert.Empty(ImageSearchService.Rank(query, Array.Empty<(Guid, long, byte[])>()));
    }

    // ── شناسهٔ بازدیدکننده ──────────────────────────────────────

    [Fact]
    public void VisitorKey_prefers_cookie_then_user_then_ip()
    {
        var visitor = Guid.NewGuid().ToString("N");
        var withCookie = new DefaultHttpContext();
        withCookie.Request.Headers.Cookie = "fr_vid=" + visitor;
        Assert.Equal("v-" + visitor, CommerceEndpoints.VisitorKey(withCookie));

        var userId = Guid.NewGuid();
        var withUser = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ShopClaimTypes.UserId, userId.ToString()) }, "test")),
        };
        withUser.Request.Headers.Cookie = "fr_vid=forged<script>";
        Assert.Equal("u-" + userId.ToString("N"), CommerceEndpoints.VisitorKey(withUser));

        var anonymous = new DefaultHttpContext();
        anonymous.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.1.2.3");
        Assert.Equal("ip-10.1.2.3", CommerceEndpoints.VisitorKey(anonymous));
    }

    [Fact]
    public void VisitorKey_is_never_longer_than_64()
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7334");
        Assert.True(CommerceEndpoints.VisitorKey(http).Length <= 64);
    }
}
