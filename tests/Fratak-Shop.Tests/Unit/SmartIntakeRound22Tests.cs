using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Smart;
using ProductsIndexModel = Fratak_Shop.Web.Areas.Admin.Pages.Products.IndexModel;
using SmartProductModel = Fratak_Shop.Web.Areas.Admin.Pages.SmartContent.ProductModel;
using Fratak_Shop.Web.Infrastructure.Smart;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های واحد دور ۲۲ («ورود هوشمند کالا» و «تکمیل هوشمند»)، بدون دیتابیس و بدون اینترنت.
/// <list type="bullet">
/// <item>نام کالا: فقط تمیز می‌شود (تبلیغ، «خرید»، شکلک، تکرار، نیم‌فاصله)؛ نام خوب دست نمی‌خورد و نام هرگز خراب نمی‌شود.</item>
/// <item>نام انگلیسی هرگز حدسی نیست (بدون واژهٔ لاتین ← null)؛ نامک فقط انگلیسی تمیز.</item>
/// <item>توضیح کوتاه فقط از واقعیت‌های ثبت‌شده؛ واقعیت کافی نبود ← null (متن پوچ ساخته نمی‌شود).</item>
/// <item>نام دسته: درست و کامل؛ نام نصفه/تبلیغاتی/برند/لاتین/عدد رد می‌شود.</item>
/// <item>مسیر دستهٔ سایت دیگر و «دستهٔ کلی» برای کالای تک (نه دستهٔ یک‌کالایی).</item>
/// <item>تصویرهای پیشنهادی دسته: فقط نام فایل امن (حملهٔ مسیر «../» و آدرس بیرونی رد می‌شود).</item>
/// <item>تغییر گروهی تأمین‌کننده: مقصد «none» یا شناسهٔ معتبر؛ بقیه رد.</item>
/// </list>
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Smart")]
public class SmartIntakeRound22Tests
{
    private const char Zwnj = '\u200c';

    // ── نام کالا ─────────────────────────────────────────────────────

    [Fact]
    public void Refine_removes_buy_prefix_and_ads_adds_model_word_and_fixes_spacing()
    {
        var result = ProductNaming.Refine("خرید هدفون بی سیم شیائومی Redmi Buds 4 اورجینال");

        Assert.Equal($"هدفون بی{Zwnj}سیم شیائومی مدل Redmi Buds 4", result.Name);
        Assert.True(result.Changed);
        Assert.Equal(4, result.Changes.Count);
        Assert.Contains("حذف عبارت تبلیغاتی", result.Changes);
    }

    [Theory]
    [InlineData("گوشی موبایل سامسونگ مدل Galaxy A55")]
    [InlineData("کابل شارژ USB-C 1 متری")] // واژهٔ فنی نام مدل نیست ← «مدل» اضافه نمی‌شود
    [InlineData("لیوان سرامیکی")]
    public void Refine_leaves_good_names_untouched(string name)
    {
        var result = ProductNaming.Refine(name);
        Assert.Equal(name, result.Name);
        Assert.False(result.Changed);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public void Refine_never_puts_model_word_before_the_brand()
    {
        const string name = "پاوربانک انکر Anker A1263 10000";
        var result = ProductNaming.Refine(name, null, "Anker");
        Assert.Equal(name, result.Name);
        Assert.False(result.Changed);
    }

    [Fact]
    public void Refine_adds_persian_type_to_latin_only_names()
    {
        var result = ProductNaming.Refine("Redmi Buds 4", ProductTypes.Find("headphone"));
        Assert.Equal("هدفون مدل Redmi Buds 4", result.Name);
        Assert.Equal(2, result.Changes.Count);
    }

    [Fact]
    public void Refine_strips_emoji_and_repeated_marks()
    {
        var result = ProductNaming.Refine("🔥 پاوربانک انکر 10000 !!!");
        Assert.Equal("پاوربانک انکر 10000", result.Name);
        Assert.True(result.Changed);
    }

    [Fact]
    public void Refine_removes_repeated_words()
    {
        var result = ProductNaming.Refine("کابل کابل شارژ");
        Assert.Equal("کابل شارژ", result.Name);
        Assert.Contains("مرتب کردن جداکننده‌ها و حذف تکرار", result.Changes);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("خرید اورجینال", "خرید اورجینال")] // بعد از تمیزکاری چیزی نمی‌ماند ← نام اصلی بدون تغییر
    public void Refine_never_destroys_a_name(string? name, string expected)
    {
        var result = ProductNaming.Refine(name);
        Assert.Equal(expected, result.Name);
        Assert.False(result.Changed);
    }

    [Fact]
    public void Refine_survives_hostile_input()
    {
        var hostile = string.Concat(Enumerable.Repeat("<script>alert(1)</script> 🔥!!! ", 200));
        var result = ProductNaming.Refine(hostile);
        Assert.True(result.Name.Length <= 150 || !result.Changed);
        Assert.DoesNotContain("🔥", result.Name, StringComparison.Ordinal);
    }

    // ── نام انگلیسی و نامک ───────────────────────────────────────────

    [Fact]
    public void English_name_prefers_the_real_english_title()
    {
        Assert.Equal("Xiaomi Redmi Buds 4 Wireless Earbuds",
            ProductNaming.EnglishName("هدفون شیائومی", "Xiaomi", null, "  Xiaomi Redmi Buds 4   Wireless Earbuds "));
    }

    [Fact]
    public void English_name_is_built_from_latin_words_brand_and_type()
    {
        Assert.Equal("Samsung Galaxy A55 Mobile Phone",
            ProductNaming.EnglishName("گوشی موبایل سامسونگ مدل Galaxy A55", "Samsung", ProductTypes.Find("mobile-phone")));
        // برند همین الان در نام لاتین هست ← تکرار نمی‌شود.
        Assert.Equal("Xiaomi Buds 4 Headphone",
            ProductNaming.EnglishName("هدفون شیائومی مدل Xiaomi Buds 4", "Xiaomi", ProductTypes.Find("headphone")));
    }

    [Theory]
    [InlineData("لیوان سرامیکی")] // بدون واژهٔ لاتین ← ترجمهٔ حدسی ساخته نمی‌شود
    [InlineData("کابل شارژ USB-C")] // فقط واژهٔ فنی
    [InlineData(null)]
    [InlineData("")]
    public void English_name_is_never_guessed(string? name)
    {
        Assert.Null(ProductNaming.EnglishName(name, null, ProductTypes.Find("cable"), "عنوان فارسی بدون حرف لاتین"));
    }

    [Fact]
    public void Slug_is_clean_english()
    {
        var slug = ProductNaming.Slug("گوشی موبایل سامسونگ مدل Galaxy A55", "Samsung Galaxy A55 Mobile Phone");
        Assert.NotNull(slug);
        Assert.True(Fratak_Shop.Data.Utilities.EnglishSlug.IsEnglish(slug), slug);
        Assert.Contains("galaxy", slug, StringComparison.Ordinal);
        Assert.Contains("a55", slug, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("ab", null)]
    public void Slug_is_null_when_nothing_useful_remains(string? name, string? english)
    {
        Assert.Null(ProductNaming.Slug(name, english));
    }

    // ── توضیح کوتاه ──────────────────────────────────────────────────

    private static readonly SeoFeature[] BankFeatures =
    [
        new("ظرفیت", "10000 میلی‌آمپر ساعت"),
        new("وزن", "180 گرم"),
        new("رنگ", "مشکی"),
        new("خروجی", "20 وات"),
    ];

    [Fact]
    public void Short_description_uses_the_three_most_important_real_facts()
    {
        var text = ShortDescriptionWriter.Write("پاوربانک انکر مدل A1263", "انکر", "پاوربانک", BankFeatures);

        Assert.NotNull(text);
        Assert.Contains("ظرفیت 10000 میلی‌آمپر ساعت", text, StringComparison.Ordinal);
        Assert.Contains("خروجی 20 وات", text, StringComparison.Ordinal);
        Assert.Contains("وزن 180 گرم", text, StringComparison.Ordinal);
        Assert.DoesNotContain("مشکی", text, StringComparison.Ordinal); // فقط سه واقعیت مهم‌تر
        Assert.StartsWith("پاوربانک انکر مدل A1263", text.Replace("مشخصات کلیدی ", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(text.Length <= ShortDescriptionWriter.MaxLength);
    }

    [Fact]
    public void Short_description_is_deterministic_and_mentions_warranty()
    {
        var first = ShortDescriptionWriter.Write("پاوربانک انکر مدل A1263", null, null, BankFeatures, "گارانتی ۱۸ ماهه");
        var second = ShortDescriptionWriter.Write("پاوربانک انکر مدل A1263", null, null, BankFeatures, "گارانتی ۱۸ ماهه");
        Assert.Equal(first, second);
        Assert.Contains("با گارانتی ۱۸ ماهه ارائه می‌شود", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Short_description_without_specs_only_states_known_facts()
    {
        var text = ShortDescriptionWriter.Write("لیوان سرامیکی", "لومیس", null, []);
        Assert.NotNull(text);
        Assert.StartsWith("لیوان سرامیکی", text, StringComparison.Ordinal);
        Assert.Contains("ساخت برند لومیس", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Short_description_turns_yes_into_a_feature_and_drops_no()
    {
        var text = ShortDescriptionWriter.Write("ساعت هوشمند شیائومی مدل Band 8", null, null,
            [new SeoFeature("ضدآب", "ندارد"), new SeoFeature("شارژ سریع", "دارد")]);
        Assert.NotNull(text);
        Assert.Contains("قابلیت شارژ سریع", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ندارد", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("لیوان سرامیکی", null, null)]
    [InlineData("لیوان سرامیکی", null, "دسته‌بندی نشده")] // دستهٔ «بی‌دسته» واقعیت نیست
    [InlineData("لیوان سرامیکی", null, "سایر")]
    public void Short_description_is_null_without_real_facts(string? name, string? brand, string? category)
    {
        Assert.Null(ShortDescriptionWriter.Write(name, brand, category, [new SeoFeature("مدل", "ندارد"), new SeoFeature("", "x")]));
    }

    [Fact]
    public void Short_description_skips_facts_already_in_the_name_and_overlong_values()
    {
        var facts = ShortDescriptionWriter.PickFacts(
            [new SeoFeature("مدل", "A1263"), new SeoFeature("توضیح", new string('ب', 60)), new SeoFeature("ظرفیت", "20000")],
            "پاوربانک انکر مدل A1263");
        Assert.Equal(new[] { "ظرفیت 20000" }, facts);
    }

    [Fact]
    public void Short_description_is_capped_for_very_long_names()
    {
        var name = string.Join(' ', Enumerable.Repeat("هدفون", 60));
        var text = ShortDescriptionWriter.Write(name, null, null, BankFeatures);
        Assert.NotNull(text);
        Assert.True(text.Length <= ShortDescriptionWriter.MaxLength, text.Length.ToString());
    }

    // ── نام دسته ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("ماساژور", "ماساژور")]
    [InlineData("لوازم شخصی برقی", "لوازم شخصی برقی")]
    [InlineData("پاور بانک", "پاوربانک")]
    [InlineData(" «زیبایی و سلامت» ", "زیبایی و سلامت")]
    public void Proper_accepts_complete_category_names(string raw, string expected)
    {
        Assert.Equal(expected, CategoryNames.Proper(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("سایر")]
    [InlineData("سایر لوازم")]
    [InlineData("دسته‌بندی نشده")]
    [InlineData("حراج ویژه")]
    [InlineData("شگفت‌انگیز")]
    [InlineData("هدفون و")] // نام نصفه
    [InlineData("مدل")]
    [InlineData("لوازم برای")]
    [InlineData("A55")]
    [InlineData("گوشی 5G")]
    [InlineData("گوشی ۵ جی")]
    [InlineData("قرمز")]
    [InlineData("<script>")]
    [InlineData("هدفون، اسپیکر")]
    [InlineData("الف ب")]
    [InlineData("یک دو سه چهار پنج شش")]
    public void Proper_rejects_half_junk_or_marketing_names(string? raw)
    {
        Assert.Null(CategoryNames.Proper(raw));
    }

    [Fact]
    public void Proper_rejects_brand_names_and_overlong_names()
    {
        Assert.Null(CategoryNames.Proper("سامسونگ", ["سامسونگ", "Samsung"]));
        Assert.Null(CategoryNames.Proper(new string('ک', CategoryNames.MaxLength + 1)));
    }

    [Theory]
    [InlineData("هندزفری بی سیم", "هندزفری بی\u200cسیم")]
    [InlineData("لپ تاپ ها", "لپ\u200cتاپ\u200cها")]
    [InlineData("كيف", "کیف")]
    [InlineData("  سه راهی   برق ", "سه\u200cراهی برق")]
    [InlineData("Power Bank", "Power Bank")]
    [InlineData(null, "")]
    public void Fix_spacing_uses_correct_persian_spelling(string? raw, string expected)
    {
        Assert.Equal(expected, CategoryNames.FixSpacing(raw));
    }

    [Fact]
    public void Path_drops_site_noise_and_duplicates()
    {
        Assert.Equal(new[] { "زیبایی و سلامت", "لوازم شخصی برقی", "ماساژور" },
            CategoryNames.Path("خانه > زیبایی و سلامت > لوازم شخصی برقی > ماساژور"));
        Assert.Equal(new[] { "ماساژور" }, CategoryNames.Path("خانه / فروشگاه / ماساژور"));
        Assert.Equal(new[] { "کیف", "کوله" }, CategoryNames.Path("کیف » کیف » کوله"));
        Assert.Equal(6, CategoryNames.Path("الف۱ > بب > پپ > تت > ثث > جج > چچ > حح").Count);
        Assert.Empty(CategoryNames.Path(null));
        Assert.Empty(CategoryNames.Path(" > / | "));
    }

    [Theory]
    [InlineData("ماساژورها", "ماساژور", true)]
    [InlineData("هندزفری بی سیم", "هندزفری بی\u200cسیم", true)]
    [InlineData("پاوربانک", "پاور بانک", true)]
    [InlineData("لوازم جانبی", "لوازم جانبی موبایل", false)]
    [InlineData("ماساژور", "هدفون", false)]
    [InlineData(null, "ماساژور", false)]
    [InlineData("ماساژور", null, false)]
    public void Same_matches_spelling_variants_but_not_broader_categories(string? segment, string? name, bool expected)
    {
        Assert.Equal(expected, CategoryNames.Same(segment, name));
    }

    // ── دستهٔ کلی برای کالای تک ──────────────────────────────────────

    [Fact]
    public void General_level_is_the_middle_of_long_paths_and_the_top_of_short_ones()
    {
        Assert.Equal(("لوازم شخصی برقی", (string?)"زیبایی و سلامت"),
            SmartCategoryService.GeneralLevel("خانه > زیبایی و سلامت > لوازم شخصی برقی > ماساژور"));
        Assert.Equal(("لوازم شخصی برقی", (string?)null), SmartCategoryService.GeneralLevel("لوازم شخصی برقی > ماساژور"));
        // سطح میانی نام بدی دارد ← یک سطح بالاتر.
        Assert.Equal(("زیبایی و سلامت", (string?)null), SmartCategoryService.GeneralLevel("زیبایی و سلامت > حراج ویژه > ماساژور"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ماساژور")] // فقط برگ ← دستهٔ یک‌کالایی ساخته نمی‌شود
    [InlineData("حراج ویژه > ماساژور")]
    [InlineData("سایر > ماساژور")]
    public void General_level_is_null_when_there_is_no_proper_broader_level(string? path)
    {
        Assert.Null(SmartCategoryService.GeneralLevel(path));
    }

    // ── نوع‌های تازه ─────────────────────────────────────────────────

    [Theory]
    [InlineData("ماساژور گردن برقی مدل X5", "massager")]
    [InlineData("سه راهی برق ۴ خانه", "power-strip")]
    [InlineData("اتو مو فیلیپس", "hair-straightener")]
    [InlineData("اتو بخار فیلیپس", "iron")]
    [InlineData("چراغ قوه شارژی", "flashlight")]
    public void New_product_types_are_detected(string name, string key)
    {
        Assert.Equal(key, ProductTypes.Detect(name)?.Key);
    }

    [Theory]
    [InlineData("روغن ماساژ بدن")] // «ماساژ» تنها ماساژور نیست
    [InlineData("لیوان سرامیکی")]
    public void Massage_oil_is_not_a_massager(string name)
    {
        Assert.Null(ProductTypes.Detect(name));
    }

    [Fact]
    public void Product_type_heads_and_english_names_are_clean()
    {
        Assert.Equal("هدفون", ProductTypes.Find("headphone")!.Head);
        Assert.Equal("Mobile Phone", ProductTypes.Find("mobile-phone")!.English);
        Assert.All(ProductTypes.All, t => Assert.DoesNotContain("  ", t.Name, StringComparison.Ordinal));
    }

    // ── تصویرهای پیشنهادی دسته ───────────────────────────────────────

    [Theory]
    [InlineData("9f3c.webp", true)]
    [InlineData("cat_1-a.jpg", true)]
    [InlineData("../x.webp", false)]
    [InlineData("..\\x.webp", false)]
    [InlineData("x..webp", false)]
    [InlineData("/uploads/categories/x.webp", false)]
    [InlineData("https://evil.example/x.webp", false)]
    [InlineData("x.svg", false)]
    [InlineData("x.webp.exe", false)]
    [InlineData("x.webp\n", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Category_image_names_must_be_simple_safe_files(string? name, bool expected)
    {
        Assert.Equal(expected, CategoryImages.IsSafeName(name));
    }

    [Fact]
    public void Category_image_list_is_parsed_safely()
    {
        Assert.Null(CategoryImages.Parse(null));
        Assert.Null(CategoryImages.Parse("  "));
        Assert.Empty(CategoryImages.Parse("[]")!); // [] یعنی ادمین همه را حذف کرده (نه «هنوز ساخته نشده»)
        Assert.Empty(CategoryImages.Parse("{broken")!);
        Assert.Empty(CategoryImages.Parse("[1,2]")!);
        Assert.Equal(new[] { "a.webp", "c.webp", "d.webp", "e.webp" },
            CategoryImages.Parse("[\"a.webp\",\"../b.webp\",\"a.webp\",\"c.webp\",\"d.webp\",\"e.webp\",\"f.webp\"]")!);
        Assert.Equal("[\"a.webp\"]", CategoryImages.Serialize(["a.webp", "../x.webp", "a.webp", "https://x/y.webp"]));
        Assert.Equal("[]", CategoryImages.Serialize([]));
    }

    // ── گزارش‌ها ─────────────────────────────────────────────────────

    [Fact]
    public void Reports_summarise_in_persian()
    {
        Assert.Equal("کالای تازه‌ای نبود.", IntakeReport.Empty.Summary);
        var report = new IntakeReport(5, 4, 5, [Guid.NewGuid()], [new CreatedCategory(Guid.NewGuid(), "ماساژور", "massager", 3, null)], 1, 0);
        Assert.Contains("۵ کالای تازه", report.Summary, StringComparison.Ordinal);
        Assert.Contains("بی‌دسته ماند", report.Summary, StringComparison.Ordinal);
        Assert.Contains("ماساژور", report.Summary, StringComparison.Ordinal);

        var categorize = new CategorizeReport(3, 3, [], 0, [], 2);
        Assert.Contains("دستهٔ کلی‌تر", categorize.Summary, StringComparison.Ordinal);
        Assert.Contains("کالای بی‌دسته‌ای نبود", CategorizeReport.Empty.Summary, StringComparison.Ordinal);
    }

    // ── دیجی‌کالا: مسیر دسته ──────────────────────────────────────────

    [Fact]
    public void Digikala_category_path_comes_from_breadcrumb_without_site_and_product_names()
    {
        const string json = """
            {"data":{"product":{"title_fa":"ماساژور گردن مدل X5","breadcrumb":[
              {"title":"دیجی‌کالا","url":{"uri":"/"}},{"title":"زیبایی و سلامت"},{"title":"لوازم شخصی برقی"},{"title":"ماساژور"},
              {"title":"ماساژور گردن مدل X5"}]}}}
            """;
        Assert.Equal("زیبایی و سلامت > لوازم شخصی برقی > ماساژور", SmartParsers.DigikalaCategory(json));
    }

    [Fact]
    public void Digikala_category_falls_back_to_the_leaf_category()
    {
        const string json = """{"data":{"product":{"title_fa":"x","category":{"title_fa":"ماساژور"}}}}""";
        Assert.Equal("ماساژور", SmartParsers.DigikalaCategory(json));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"data\":{}}")]
    [InlineData("{\"data\":{\"product\":[]}}")]
    [InlineData("{\"data\":{\"product\":{\"breadcrumb\":[1,\"x\",null]}}}")]
    public void Digikala_category_never_throws_on_broken_responses(string? json)
    {
        Assert.Null(SmartParsers.DigikalaCategory(json));
    }

    // ── پنل ──────────────────────────────────────────────────────────

    [Fact]
    public void Supplier_target_accepts_none_or_a_real_id_only()
    {
        Assert.True(ProductsIndexModel.TryParseSupplierTarget("none", out var none));
        Assert.Null(none);

        var id = Guid.NewGuid();
        Assert.True(ProductsIndexModel.TryParseSupplierTarget(id.ToString(), out var parsed));
        Assert.Equal(id, parsed);

        foreach (var bad in new[] { null, "", " ", "NONE", "garbage", Guid.Empty.ToString(), "1; DROP TABLE Products" })
        {
            Assert.False(ProductsIndexModel.TryParseSupplierTarget(bad, out var value), bad);
            Assert.Null(value);
        }
    }

    [Fact]
    public void Pending_identity_needs_an_unapplied_real_name()
    {
        Assert.False(SmartProductModel.HasPendingIdentity(new SmartTaskResult()));
        Assert.True(SmartProductModel.HasPendingIdentity(new SmartTaskResult { Identity = new IdentitySuggestion { Name = "هدفون مدل X", BaseName = "هدفون X" } }));
        Assert.False(SmartProductModel.HasPendingIdentity(new SmartTaskResult { Identity = new IdentitySuggestion { Name = "هدفون مدل X", Applied = true } }));
        Assert.False(SmartProductModel.HasPendingIdentity(new SmartTaskResult { Identity = new IdentitySuggestion { Name = "ab" } }));
        Assert.False(SmartProductModel.HasPendingIdentity(new SmartTaskResult { Identity = new IdentitySuggestion() }));
    }
}
