using Fratak_Shop.Core.Services.Smart;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// دور ۲۶ – منطق خالص «هوشمندی‌های پخته»:
/// <list type="bullet">
/// <item>عکس فضاسازی دسته از اینترنت (<see cref="CategoryPhotos"/>): کلمه‌های جست‌وجو، رد لوگو/آیکن/استوک، سنجهٔ رنگ و زمینهٔ سفید، نام فایل.</item>
/// <item>رأی رقبا برای دستهٔ کالا (<see cref="CategoryVote"/>): اکثریت ≥ ۲ سایت، واژه‌های کلی، رأی تکراری یک سایت، ورودی مخرب.</item>
/// <item>خواندن پاسخ منابع (Openverse، دیجی‌کالا، ترب، باسلام) – بهترین تا بدترین حالت و JSON خراب.</item>
/// </list>
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Round26")]
public class Round26SmartTests
{
    private static readonly CategoryPhotoSubject Headphones = new(Guid.NewGuid(), "هدفون", "headphones", "لوازم جانبی موبایل", "mobile-accessories");

    // ───────────── عکس فضاسازی: کلمه‌های جست‌وجو ─────────────

    [Fact]
    public void English_queries_expand_the_slug_with_mood_words_and_the_parent()
    {
        var queries = CategoryPhotos.EnglishQueries(Headphones);
        Assert.Equal("headphones lifestyle", queries[0]);
        Assert.Contains("headphones interior", queries);
        Assert.Contains("headphones mobile accessories", queries); // نام یک‌کلمه‌ای با والد دقیق‌تر می‌شود
        Assert.Contains("headphones", queries);
        Assert.True(queries.Count <= 4);
        Assert.Equal(queries.Count, queries.Distinct().Count());
    }

    [Fact]
    public void Multi_word_slugs_skip_the_parent_and_strip_numbers()
    {
        var queries = CategoryPhotos.EnglishQueries(new CategoryPhotoSubject(Guid.NewGuid(), "لوازم خانگی", "home-appliances-2", "خانه", "home"));
        Assert.Equal("home appliances lifestyle", queries[0]);
        Assert.DoesNotContain(queries, q => q.Contains('2'));
        Assert.DoesNotContain(queries, q => q.EndsWith(" home", StringComparison.Ordinal));
    }

    [Fact]
    public void Persian_only_slug_falls_back_to_the_parent_or_nothing()
    {
        Assert.Equal("", CategoryPhotos.SlugWords("هدفون"));
        Assert.Equal("", CategoryPhotos.SlugWords(null));
        Assert.Equal(new[] { "kitchen lifestyle" }, CategoryPhotos.EnglishQueries(new CategoryPhotoSubject(Guid.NewGuid(), "قابلمه", "قابلمه", "آشپزخانه", "kitchen")));
        Assert.Empty(CategoryPhotos.EnglishQueries(new CategoryPhotoSubject(Guid.NewGuid(), "قابلمه", "", null, null)));
    }

    [Fact]
    public void Persian_queries_add_a_home_scene_and_the_parent()
    {
        var queries = CategoryPhotos.PersianQueries(new CategoryPhotoSubject(Guid.NewGuid(), "لوازم خانگی", "home-appliances", "خانه و آشپزخانه", "home-kitchen"));
        Assert.Equal("لوازم خانگی در خانه", queries[0]);
        Assert.Contains("لوازم خانگی خانه و آشپزخانه", queries);
        Assert.Contains("لوازم خانگی", queries);
        Assert.Empty(CategoryPhotos.PersianQueries(new CategoryPhotoSubject(Guid.NewGuid(), " ", "x", null, null)));
    }

    // ───────────── عکس فضاسازی: رد نتیجه‌های نامناسب پیش از دانلود ─────────────

    [Theory]
    [InlineData("Modern kitchen with appliances", "https://upload.wikimedia.org/wikipedia/commons/3/33/Modern_kitchen.jpg", null, true)]
    [InlineData("آشپزخانهٔ مدرن", "https://example.ir/images/kitchen.jpg", "https://example.ir/blog/kitchen", true)]
    [InlineData("Kitchen icon set vector", "https://example.com/a.jpg", null, false)]
    [InlineData(null, "https://example.com/files/kitchen_icon_set.jpg", null, false)] // زیرخط هم مرز واژه است
    [InlineData("Headphones", "https://example.com/headphones.png", null, false)] // PNG معمولاً دوربری‌شده است
    [InlineData("Kitchen", "https://www.shutterstock.com/image-photo/kitchen.jpg", null, false)]
    [InlineData("Kitchen", "https://cdn.example.com/k.jpg", "https://www.freepik.com/photo/kitchen", false)]
    [InlineData("لوگوی فروشگاه", "https://example.ir/a.jpg", null, false)]
    [InlineData("هدفون روی پس زمینه سفید", "https://example.ir/a.jpg", null, false)]
    [InlineData("Kitchen", "javascript:alert(1)", null, false)]
    [InlineData("Kitchen", "ftp://example.com/a.jpg", null, false)]
    [InlineData("Kitchen", "", null, false)]
    public void Useful_hits_exclude_logos_icons_stock_sites_and_unsafe_urls(string? title, string url, string? page, bool expected) =>
        Assert.Equal(expected, CategoryPhotos.IsUsefulHit(title, url, page));

    // ───────────── عکس فضاسازی: سنجه‌های ظاهری ─────────────

    private static byte[] Fill(int w, int h, Func<int, int, (byte R, byte G, byte B)> pixel)
    {
        var rgb = new byte[w * h * 3];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var (r, g, b) = pixel(x, y);
                var i = (y * w + x) * 3;
                rgb[i] = r;
                rgb[i + 1] = g;
                rgb[i + 2] = b;
            }
        }
        return rgb;
    }

    [Fact]
    public void Gray_photo_has_no_colorfulness_and_white_photo_is_all_white()
    {
        var gray = CategoryPhotos.Measure(Fill(20, 10, (x, _) => ((byte)(x * 10), (byte)(x * 10), (byte)(x * 10))), 20, 10);
        Assert.Equal(0, gray.Colorfulness);
        Assert.True(gray.Contrast > 25);

        var white = CategoryPhotos.Measure(Fill(10, 10, (_, _) => (255, 255, 255)), 10, 10);
        Assert.Equal(1, white.WhiteRatio);
        Assert.Equal(255, white.Brightness, 0);
    }

    [Fact]
    public void Colorful_scene_passes_and_product_on_white_fails()
    {
        // صحنهٔ رنگی: نیمه نارنجی گرم، نیمه آبی.
        var scene = Fill(32, 20, (x, _) => x < 16 ? ((byte)230, (byte)120, (byte)40) : ((byte)30, (byte)90, (byte)200));
        var colorful = CategoryPhotos.Measure(scene, 32, 20);
        Assert.True(colorful.Colorfulness > CategoryPhotos.MinColorfulness, colorful.ToString());
        Assert.True(CategoryPhotos.IsAcceptable(1600, 1000, colorful));

        // کالا وسط زمینهٔ سفید (۸۰٪ سفید) ← «سرد» و رد.
        var product = Fill(32, 20, (x, y) => x is >= 12 and < 20 && y is >= 6 and < 14 ? ((byte)200, (byte)30, (byte)30) : ((byte)255, (byte)255, (byte)255));
        var cold = CategoryPhotos.Measure(product, 32, 20);
        Assert.True(cold.WhiteRatio > CategoryPhotos.MaxWhiteRatio);
        Assert.False(CategoryPhotos.IsAcceptable(1600, 1000, cold));
    }

    [Theory]
    [InlineData(1600, 1000, true)]
    [InlineData(1920, 800, true)]
    [InlineData(1200, 1200, false)] // مربع
    [InlineData(800, 500, false)]   // کوچک
    [InlineData(3000, 1000, false)] // نوار باریک
    [InlineData(1000, 1600, false)] // عمودی
    [InlineData(1600, 0, false)]
    public void Only_large_landscape_photos_are_acceptable(int width, int height, bool expected) =>
        Assert.Equal(expected, CategoryPhotos.IsAcceptable(width, height, new PhotoMetrics(40, 0.05, 120, 50)));

    [Fact]
    public void Dark_flat_or_bright_photos_are_rejected()
    {
        Assert.False(CategoryPhotos.IsAcceptable(1600, 1000, new PhotoMetrics(40, 0.05, 20, 50)));  // خیلی تاریک
        Assert.False(CategoryPhotos.IsAcceptable(1600, 1000, new PhotoMetrics(40, 0.05, 240, 50))); // خیلی روشن
        Assert.False(CategoryPhotos.IsAcceptable(1600, 1000, new PhotoMetrics(40, 0.05, 120, 10))); // تخت و یکدست
        Assert.False(CategoryPhotos.IsAcceptable(1600, 1000, new PhotoMetrics(5, 0.05, 120, 50)));  // بی‌رنگ
    }

    [Fact]
    public void Measure_never_throws_on_bad_input()
    {
        Assert.Equal(new PhotoMetrics(0, 0, 0, 0), CategoryPhotos.Measure([], 10, 10));
        Assert.Equal(new PhotoMetrics(0, 0, 0, 0), CategoryPhotos.Measure(new byte[30], 0, 10));
        Assert.Equal(new PhotoMetrics(0, 0, 0, 0), CategoryPhotos.Measure(new byte[10], 10, 10)); // داده کمتر از ابعاد
    }

    [Fact]
    public void Score_prefers_colorful_sharp_and_well_proportioned_photos()
    {
        var vivid = CategoryPhotos.Score(1920, 1200, new PhotoMetrics(70, 0.02, 130, 60), 80);
        var dull = CategoryPhotos.Score(1920, 1200, new PhotoMetrics(20, 0.02, 130, 60), 80);
        var blurry = CategoryPhotos.Score(1920, 1200, new PhotoMetrics(70, 0.02, 130, 60), 20);
        Assert.True(vivid > dull);
        Assert.True(vivid > blurry);
        Assert.InRange(vivid, 0, 1);
        Assert.InRange(CategoryPhotos.Score(900, 0, new PhotoMetrics(0, 0, 0, 0), 0), 0, 1);
    }

    [Fact]
    public void File_names_follow_the_photo_convention()
    {
        Assert.Equal("headphones-photo-1a2b3c4d.webp", CategoryPhotos.FileName("headphones", "1a2b3c4d"));
        Assert.Equal("category-photo-1a2b3c4d.webp", CategoryPhotos.FileName("هدفون", "1a2b3c4d"));
        Assert.Equal("category-photo-1a2b3c4d.webp", CategoryPhotos.FileName(null, "1a2b3c4d"));
        var longName = CategoryPhotos.FileName("very-long-category-name-with-many-words-that-goes-on-and-on-forever", "1a2b3c4d");
        Assert.Contains("-photo-", longName, StringComparison.Ordinal);
        Assert.True(CategoryImages.IsPhoto(longName));
        Assert.True(CategoryImages.IsSafeName(CategoryPhotos.FileName("../../etc/passwd")));
        Assert.True(CategoryImages.IsPhoto(CategoryPhotos.FileName("kitchen")));
    }

    // ───────────── رأی رقبا ─────────────

    [Fact]
    public void Three_sites_with_slightly_different_names_agree()
    {
        var vote = CategoryVote.Decide([
            ("digikala", "هدفون، هدست و هندزفری"),
            ("torob", "هدفون، هدست و هندزفری"),
            ("basalam", "هدفون و هندزفری بی سیم و بلوتوثی"),
        ]);
        Assert.NotNull(vote);
        Assert.Equal(3, vote.Sites);
        Assert.Contains("هدفون", vote.Name, StringComparison.Ordinal);
        Assert.DoesNotContain("،", vote.Name, StringComparison.Ordinal); // ویرگول از نام دسته حذف می‌شود
        Assert.Equal(new[] { "digikala", "torob", "basalam" }, vote.Agreeing);
    }

    [Fact]
    public void Two_agreeing_sites_beat_a_lone_dissenter()
    {
        var vote = CategoryVote.Decide([
            ("digikala", "ماساژور"),
            ("torob", "کفش ورزشی"),
            ("basalam", "ماساژور"),
        ]);
        Assert.NotNull(vote);
        Assert.Equal("ماساژور", vote.Name);
        Assert.Equal(2, vote.Sites);
    }

    [Fact]
    public void No_majority_means_no_vote()
    {
        Assert.Null(CategoryVote.Decide([("digikala", "ماساژور"), ("torob", "کفش ورزشی")]));
        Assert.Null(CategoryVote.Decide([("digikala", "ماساژور")]));
        Assert.Null(CategoryVote.Decide([("digikala", "ماساژور"), ("torob", null), ("basalam", " ")]));
        Assert.Null(CategoryVote.Decide([]));
    }

    [Fact]
    public void One_site_voting_twice_counts_once()
    {
        Assert.Null(CategoryVote.Decide([("digikala", "ماساژور"), ("digikala", "ماساژور")]));
    }

    [Fact]
    public void Generic_words_alone_do_not_make_names_similar()
    {
        Assert.False(CategoryVote.Similar(CategoryVote.KeyTerms("لوازم خانگی"), CategoryVote.KeyTerms("لوازم جانبی موبایل")));
        Assert.Null(CategoryVote.Decide([("digikala", "لوازم خانگی"), ("torob", "لوازم جانبی موبایل")]));
        Assert.False(CategoryVote.Similar([], ["a"]));
    }

    [Fact]
    public void Junk_or_hostile_names_never_become_categories()
    {
        Assert.Null(CategoryVote.Decide([("digikala", "<script>alert(1)</script>"), ("torob", "<script>alert(1)</script>")]));
        Assert.Null(CategoryVote.Decide([("digikala", "حراج ویژه"), ("torob", "حراج ویژه")])); // نام تبلیغاتی
        Assert.Null(CategoryVote.Decide([("digikala", "سایر"), ("torob", "سایر")]));
    }

    // ───────────── خواندن پاسخ منابع ─────────────

    [Fact]
    public void Openverse_results_keep_only_safe_urls_with_sizes()
    {
        const string json = """
            {"result_count":3,"results":[
              {"url":"https://upload.wikimedia.org/a/kitchen.jpg","title":"Modern kitchen","foreign_landing_url":"https://commons.wikimedia.org/w/1","width":4032,"height":3024},
              {"url":"javascript:alert(1)","title":"Evil","width":2000,"height":1000},
              {"url":"https://example.com/nosize.jpg","title":"No size"}
            ]}
            """;
        var hits = SmartParsers.OpenverseImages(json);
        var hit = Assert.Single(hits);
        Assert.Equal("https://upload.wikimedia.org/a/kitchen.jpg", hit.Url);
        Assert.Equal(4032, hit.Width);
        Assert.Equal("openverse", hit.Source);
        Assert.Equal("https://commons.wikimedia.org/w/1", hit.PageUrl);
        Assert.Empty(SmartParsers.OpenverseImages("{not json"));
        Assert.Empty(SmartParsers.OpenverseImages(null));
        Assert.Empty(SmartParsers.OpenverseImages("""{"results":{}}"""));
    }

    [Fact]
    public void Digikala_and_torob_search_categories_are_read()
    {
        Assert.Equal("هدفون، هدست و هندزفری", SmartParsers.DigikalaSearchCategory(
            """{"data":{"products":[],"filters":{"categories":{"options":[{"id":1,"title_fa":"هدفون، هدست و هندزفری","code":"headphone"},{"id":2,"title_fa":"اسپیکر"}]}}}}"""));
        Assert.Null(SmartParsers.DigikalaSearchCategory("""{"data":{"products":[]}}"""));
        Assert.Null(SmartParsers.DigikalaSearchCategory("<html>captcha</html>"));

        Assert.Equal("هدفون، هدست و هندزفری", SmartParsers.TorobSearchCategory(
            """{"results":[],"categories":[{"title":"هدفون، هدست و هندزفری","cat_id":97,"cat_slug":"headphones"}]}"""));
        Assert.Null(SmartParsers.TorobSearchCategory("""{"categories":[]}"""));
        Assert.Null(SmartParsers.TorobSearchCategory(null));
    }

    [Fact]
    public void Basalam_category_comes_from_matching_products_only()
    {
        const string json = """
            {"products":[
              {"id":1,"name":"هدفون بی سیم هایلو مدل T15","categoryTitle":"هدفون و هندزفری بی سیم و بلوتوثی"},
              {"id":2,"name":"هدفون بی سیم هایلو مدل T15 مشکی","categoryTitle":"هدفون و هندزفری بی سیم و بلوتوثی"},
              {"id":3,"name":"قابلمه چدنی","categoryTitle":"ظروف آشپزخانه"}
            ]}
            """;
        Assert.Equal("هدفون و هندزفری بی سیم و بلوتوثی", SmartParsers.BasalamCategory(json, "هدفون بی سیم هایلو مدل T15"));
        Assert.Null(SmartParsers.BasalamCategory(json, "دوچرخهٔ کوهستان"));
        Assert.Null(SmartParsers.BasalamCategory("[]", "هدفون"));
    }

    [Fact]
    public void Torob_english_name_requires_a_close_persian_match()
    {
        var results = SmartParsers.TorobSearchResults("""
            {"results":[
              {"name1":"هدفون بی سیم هایلو مدل T15","name2":"Haylou T15 Wireless Earbuds","price":1200000},
              {"name1":"هدفون بی سیم شیائومی","name2":"Xiaomi Buds"},
              {"name1":"کابل شارژ","name2":""}
            ]}
            """);
        Assert.Equal(3, results.Count);
        Assert.Null(results[2].English);
        Assert.Equal("Haylou T15 Wireless Earbuds", SmartParsers.TorobEnglishName("هدفون بی سیم هایلو مدل T15", results));
        Assert.Null(SmartParsers.TorobEnglishName("ماساژور گردن برقی", results));
        Assert.Empty(SmartParsers.TorobSearchResults("oops"));
    }
}
