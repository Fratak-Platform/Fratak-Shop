using Fratak_Shop.Core.Services.Design;
using Fratak_Shop.Web.Infrastructure.Design;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// دور ۲۸ – «بنر هیچ‌وقت خالی نمی‌ماند»: فروشگاه بی‌کالا هم ایدهٔ واقعی (دسته/اعتماد/برند) می‌گیرد؛ ایدهٔ اعتماد فقط وقتی
/// واقعیتش در تنظیمات هست؛ لینک‌ها درست‌اند؛ متن‌ها پایدار ولی ماهانه متنوع‌اند؛ نسخهٔ «بی‌عکس» همیشه ساختنی است.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Round28")]
public class BannerRound28Tests
{
    // ۱۵ مهر ۱۴۰۵ – بیرون از پنجرهٔ مناسبت‌ها.
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);

    private static IdeaInput Empty(IdeaStore? store, params IdeaCategory[] categories) =>
        new([], categories, "فراتک") { Store = store };

    private static readonly IdeaStore FullStore = new(SecurePayment: true, SupportPhone: "02112345678", Club: true, ReturnDays: 7, Posts: 5);

    [Fact]
    public void Empty_shop_with_store_facts_still_gets_trust_and_brand_ideas()
    {
        var ideas = DesignIdeas.Generate(Empty(FullStore), Now, max: 10);
        var keys = ideas.Select(i => i.Key.Split(':')[0]).ToList();
        Assert.Contains("trust-return", keys);
        Assert.Contains("trust-pay", keys);
        Assert.Contains("trust-support", keys);
        Assert.Contains("trust-club", keys);
        Assert.Contains("blog", keys);
        Assert.Contains("brand", keys);
        Assert.All(ideas, i => Assert.False(string.IsNullOrWhiteSpace(i.Spec.Headline)));
        Assert.All(ideas, i => Assert.Equal("pattern", i.Spec.Layout));
    }

    [Fact]
    public void Without_store_facts_empty_shop_gets_nothing()
    {
        Assert.Empty(DesignIdeas.Generate(Empty(null), Now));
    }

    [Fact]
    public void Brand_idea_always_exists_even_with_no_trust_facts()
    {
        var ideas = DesignIdeas.Generate(Empty(new IdeaStore(false, null, false, 0, 0)), Now);
        var brand = Assert.Single(ideas);
        Assert.StartsWith("brand:", brand.Key, StringComparison.Ordinal);
        Assert.Equal("فراتک", brand.Spec.Headline);
        Assert.Equal("/products", brand.LinkUrl);
    }

    [Fact]
    public void Trust_ideas_appear_only_for_true_facts()
    {
        var ideas = DesignIdeas.Generate(Empty(new IdeaStore(SecurePayment: false, SupportPhone: " ", Club: false, ReturnDays: 7, Posts: 2)), Now);
        var keys = ideas.Select(i => i.Key.Split(':')[0]).ToList();
        Assert.Contains("trust-return", keys);
        Assert.DoesNotContain("trust-pay", keys);
        Assert.DoesNotContain("trust-support", keys);
        Assert.DoesNotContain("trust-club", keys);
        Assert.DoesNotContain("blog", keys);
    }

    [Theory]
    [InlineData("trust-return", "/faq")]
    [InlineData("trust-pay", "/products")]
    [InlineData("trust-support", "/contact-us")]
    [InlineData("trust-club", "/club")]
    [InlineData("blog", "/blog")]
    [InlineData("brand", "/products")]
    public void Trust_ideas_link_to_real_pages(string key, string url)
    {
        var idea = DesignIdeas.Generate(Empty(FullStore), Now, max: 10).Single(i => i.Key.StartsWith(key + ":", StringComparison.Ordinal));
        Assert.Equal(url, idea.LinkUrl);
        Assert.NotNull(idea.ExpiresAt);
    }

    [Fact]
    public void Support_banner_shows_the_real_phone_in_persian_digits()
    {
        var idea = DesignIdeas.Generate(Empty(FullStore), Now, max: 10).Single(i => i.Key.StartsWith("trust-support:", StringComparison.Ordinal));
        Assert.Contains("۰۲۱۱۲۳۴۵۶۷۸", idea.Spec.Subline, StringComparison.Ordinal);
    }

    [Fact]
    public void Slogan_is_used_for_the_brand_banner()
    {
        var store = FullStore with { Slogan = "همه چیز برای خانه" };
        var brand = DesignIdeas.Generate(Empty(store), Now, max: 10).Single(i => i.Key.StartsWith("brand:", StringComparison.Ordinal));
        Assert.Equal("همه چیز برای خانه", brand.Spec.Subline);
    }

    [Fact]
    public void Max_limits_the_number_of_ideas()
    {
        Assert.Equal(2, DesignIdeas.Generate(Empty(FullStore), Now, max: 2).Count);
        Assert.Empty(DesignIdeas.Generate(Empty(FullStore), Now, max: 0));
        Assert.Empty(DesignIdeas.Generate(Empty(FullStore), Now, max: -3));
    }

    [Fact]
    public void Category_with_stock_gets_an_intro_banner_in_a_small_shop()
    {
        var category = new IdeaCategory(Guid.NewGuid(), "پاوربانک", "power-bank", 1, null);
        var ideas = DesignIdeas.Generate(Empty(FullStore, category), Now, max: 10);
        var intro = Assert.Single(ideas, i => i.Key.StartsWith("cat:", StringComparison.Ordinal));
        Assert.Equal("/category/power-bank", intro.LinkUrl);
        Assert.Equal("pattern", intro.Spec.Layout);
        Assert.Equal("پاوربانک", intro.Spec.Headline);
    }

    [Fact]
    public void Category_without_stock_gets_no_banner()
    {
        var category = new IdeaCategory(Guid.NewGuid(), "خالی", "empty", 0, null);
        Assert.DoesNotContain(DesignIdeas.Generate(Empty(FullStore, category), Now, max: 10), i => i.Key.StartsWith("cat:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 1)]
    [InlineData(11, 1)]
    [InlineData(12, 3)]
    [InlineData(500, 3)]
    public void Category_minimum_stock_depends_on_shop_size(int products, int expected) =>
        Assert.Equal(expected, DesignIdeas.CategoryMinStock(products));

    [Fact]
    public void Variant_is_stable_and_covers_options()
    {
        Assert.Equal(DesignIdeas.Variant("pay1405-07", "الف", "ب"), DesignIdeas.Variant("pay1405-07", "الف", "ب"));
        var seen = Enumerable.Range(0, 40).Select(i => DesignIdeas.Variant("seed" + i, "a", "b", "c")).Distinct().Count();
        Assert.Equal(3, seen);
        Assert.Equal("", DesignIdeas.Variant("x"));
        Assert.Equal("تنها", DesignIdeas.Variant("", "تنها"));
    }

    [Fact]
    public void Same_month_gives_same_keys_next_month_gives_new_keys()
    {
        var a = DesignIdeas.Generate(Empty(FullStore), Now, max: 10).Select(i => i.Key).ToList();
        var b = DesignIdeas.Generate(Empty(FullStore), Now.AddDays(3), max: 10).Select(i => i.Key).ToList();
        var c = DesignIdeas.Generate(Empty(FullStore), Now.AddDays(40), max: 10).Select(i => i.Key).ToList();
        Assert.Equal(a, b);
        Assert.Empty(a.Intersect(c));
    }

    [Fact]
    public void Plain_version_drops_every_image_and_uses_the_pattern_layout()
    {
        var image = new DesignImage("/uploads/products/a.webp", 800, 800);
        var idea = new DesignIdea("x", "t", "r", new DesignSpec("collage", "ocean", "تیتر", Image: image, Gallery: [image], Photo: image), "/products", null);
        var plain = DesignStudio.Plain(idea);
        Assert.Equal("pattern", plain.Spec.Layout);
        Assert.Null(plain.Spec.Image);
        Assert.Null(plain.Spec.Gallery);
        Assert.Null(plain.Spec.Photo);
        Assert.Equal("تیتر", plain.Spec.Headline);
        Assert.Equal("/products", plain.LinkUrl);
        Assert.Equal("collage", idea.Spec.Layout);
    }
}
