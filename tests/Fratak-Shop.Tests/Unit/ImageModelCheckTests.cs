using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Web.Infrastructure.Smart;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های واحد «تأیید خودکار مدل منبع عکس» (دور ۲۳): همان مدل، مدل دیگرِ همان خانواده، نسخهٔ Pro/Max، واحدها (128GB، 5G)،
/// کد خانوادهٔ دیگر (USB3) که نباید عکس درست را حذف کند، متن فارسی و ورودی خالی/خیلی بلند.
/// </summary>
[Trait("Category", "Unit")]
public class ImageModelCheckTests
{
    [Theory]
    [InlineData("گوشی سامسونگ Galaxy A55", null, "Samsung Galaxy A55 5G 256GB", ModelVerdict.Same)]
    [InlineData("گوشی سامسونگ Galaxy A55", null, "گوشی موبایل سامسونگ مدل Galaxy A55 دو سیم کارت", ModelVerdict.Same)]
    [InlineData("هدفون سونی WH-1000XM5", null, "Sony WH1000XM5 Wireless Headphones", ModelVerdict.Same)]
    [InlineData("هدفون سونی WH-1000XM5", null, "Sony WH 1000XM5 Black", ModelVerdict.Same)]
    [InlineData("گوشی اپل", "Apple iPhone 15", "Apple iPhone 15 128GB Blue", ModelVerdict.Same)]
    [InlineData("گوشی سامسونگ Galaxy A55", null, "Samsung Galaxy A35 5G", ModelVerdict.Different)]
    [InlineData("هدفون سونی WH-1000XM5", null, "Sony WH-1000XM4", ModelVerdict.Different)]
    [InlineData("گوشی اپل", "Apple iPhone 15", "Apple iPhone 14 Pro", ModelVerdict.Different)]
    [InlineData("گوشی شیائومی Redmi Note 13", null, "Xiaomi Redmi Note 13 Pro 5G", ModelVerdict.Different)] // نسخهٔ Pro
    [InlineData("گوشی شیائومی Redmi Note 13 پرو", null, "Xiaomi Redmi Note 13 Pro", ModelVerdict.Same)]       // «پرو» = Pro
    [InlineData("گوشی سامسونگ Galaxy S24 Ultra", null, "Samsung Galaxy S24", ModelVerdict.Unknown)]          // منبع نسخه را نگفته
    [InlineData("پاوربانک انکر A1263", null, "Anker PowerCore 10000mAh USB3", ModelVerdict.Unknown)]         // خانوادهٔ دیگر ← حذف نشود
    [InlineData("پاوربانک انکر A1263", null, "پاوربانک انکر ظرفیت ۱۰۰۰۰", ModelVerdict.Unknown)]
    [InlineData("کابل شارژ تایپ سی", null, "Samsung Galaxy A55", ModelVerdict.Unknown)]                       // کالای ما کد مدل ندارد
    [InlineData("گوشی سامسونگ Galaxy A55", null, "", ModelVerdict.Unknown)]
    [InlineData("گوشی سامسونگ Galaxy A55", null, null, ModelVerdict.Unknown)]
    public void Source_model_is_compared_with_our_product(string name, string? english, string? source, ModelVerdict expected)
    {
        Assert.Equal(expected, ImageModelCheck.Check(name, english, source));
    }

    [Fact]
    public void Units_years_and_ordinals_are_not_model_codes()
    {
        var codes = ImageModelCheck.Codes("PowerCore 24000mAh 140W 5G 4K 1080p 2nd 128GB 2024");
        Assert.Empty(codes);
        Assert.Contains("anker737", ImageModelCheck.Codes("Anker 737 Power Bank")); // «واژه + عدد» = نام مدل
        Assert.Equal(new[] { "wh1000xm5" }, ImageModelCheck.Codes("Sony WH-1000XM5 (2024)").ToArray());
        Assert.Contains("iphone15", ImageModelCheck.Codes("Apple iPhone ۱۵")); // رقم فارسی
    }

    [Fact]
    public void Model_rows_of_the_same_page_are_detected_but_compatible_lists_are_not()
    {
        Assert.True(ImageModelCheck.IsModelKey("مدل"));
        Assert.True(ImageModelCheck.IsModelKey("شماره مدل"));
        Assert.True(ImageModelCheck.IsModelKey("Model"));
        Assert.False(ImageModelCheck.IsModelKey("مدل‌های سازگار"));
        Assert.False(ImageModelCheck.IsModelKey("Compatible models"));
        Assert.False(ImageModelCheck.IsModelKey("رنگ"));
        Assert.False(ImageModelCheck.IsModelKey(null));
    }

    [Fact]
    public void Hostile_or_huge_input_is_safe()
    {
        var huge = string.Concat(Enumerable.Repeat("A55 ", 50_000));
        Assert.Equal(ModelVerdict.Same, ImageModelCheck.Check("Galaxy A55", null, huge));
        Assert.Equal(ModelVerdict.Unknown, ImageModelCheck.Check("Galaxy A55", null, "<script>alert(1)</script>"));
    }

    [Fact]
    public void Same_model_ranks_slightly_higher_and_old_results_stay_unknown()
    {
        var same = new ImageCandidate("https://b.ir/1.jpg", "bing:b.ir", false, 0.5, "Galaxy A55", null, ModelVerdict.Same);
        var unknown = new ImageCandidate("https://b.ir/2.jpg", "bing:b.ir", false, 0.5, "Galaxy");
        Assert.Equal(0.52, same.Score(null));
        Assert.Equal(0.47, unknown.Score(null));
        Assert.Equal(ModelVerdict.Unknown, new FoundImage("x.webp", 800, 800, "bing", false).Model);
        Assert.Equal("مدل تأیید شد", ImageModelCheck.Label(ModelVerdict.Same));
    }
}

/// <summary>تست‌های واحد نام فایل انگلیسی عکس کالا (پیشنهاد ۲۷، دور ۲۳).</summary>
[Trait("Category", "Unit")]
public class SeoImageNameTests
{
    [Theory]
    [InlineData("samsung-galaxy-a55", ".webp", "samsung-galaxy-a55-1a2b3c4d.webp")]
    [InlineData("Samsung--Galaxy A55", ".WEBP", "samsung-galaxy-a55-1a2b3c4d.webp")]
    [InlineData("../../etc/passwd", ".webp", "etc-passwd-1a2b3c4d.webp")]          // مسیر ← فقط نام ساده
    [InlineData("گوشی-سامسونگ", ".webp", null)]                                    // نامک غیرلاتین ← نام قبلی
    [InlineData("12345", ".webp", null)]                                            // بدون حرف
    [InlineData("", ".webp", null)]
    [InlineData("samsung-galaxy-a55", ".exe", null)]                                // پسوند غیرمجاز
    [InlineData("samsung-galaxy-a55", null, null)]
    public void Image_names_are_readable_english_and_safe(string? slug, string? extension, string? expected)
    {
        Assert.Equal(expected, SeoImageName.Build(slug, extension, "1a2b3c4d"));
    }

    [Fact]
    public void Long_slugs_are_cut_at_a_word_boundary_and_names_are_unique()
    {
        var slug = "xiaomi-redmi-note-13-pro-plus-5g-dual-sim-512gb-ram-12gb-aurora-purple-global-version";
        var name = SeoImageName.Build(slug, ".webp", "00000000")!;
        Assert.True(name.Length <= SeoImageName.MaxSlugLength + 14);
        Assert.StartsWith("xiaomi-redmi-note-13-pro-plus", name);
        Assert.DoesNotContain("--", name);
        Assert.NotEqual(SeoImageName.Build("abc-phone", ".webp"), SeoImageName.Build("abc-phone", ".webp"));
        Assert.Matches("^abc-phone-[0-9a-f]{8}\\.webp$", SeoImageName.Build("abc-phone", ".webp")!);
    }
}
