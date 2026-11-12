using Fratak_Shop.Core.Services.Assistant;
using Fratak_Shop.Core.Services.Learning;
using Fratak_Shop.Data.Entities.Assistant;
using Fratak_Shop.Web.Infrastructure.Assistant;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های واحد دور ۲۴ برای «دستیار هوشمند فروشگاه».
/// <list type="bullet">
/// <item>شخصیت و لحن (<see cref="AssistantPersona"/>): نسل از تاریخ تولد، لحن، خطاب (نام/نام خانوادگی/رسمی)، اسم امن، طرز نوشتن، «منو علی صدا کن».</item>
/// <item>راهنمای زنده (<see cref="AssistantGuide"/>): فقط مسیر داخلی، فقط عنصرهای فهرست سفید، رمز هرگز پر نمی‌شود، سقف قدم‌ها.</item>
/// <item>یادگیری ماشین (<see cref="NaiveBayesText"/> و <see cref="AssistantIntentModel"/>): آموزش، احتمال، واژهٔ ناآشنا ← بی‌نظر.</item>
/// <item>کالایاب و پرسش‌های صفحه (<see cref="AssistantFinder"/>، <see cref="AssistantPages"/>) و دانش سایت (<see cref="KnowledgeIndex"/>).</item>
/// <item>فهم پیام‌های تازه (<see cref="AssistantNlu"/>) و ورودی امن وب (<see cref="AssistantEndpoints"/>).</item>
/// </list>
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Assistant")]
public class AssistantRound24Tests
{
    private static readonly DateOnly Today = new(2026, 10, 22);

    // ── نسل و لحن ───────────────────────────────────────────────

    [Theory]
    [InlineData(2001, AssistantGeneration.Young)]
    [InlineData(1990, AssistantGeneration.Adult)]
    [InlineData(1970, AssistantGeneration.Middle)]
    [InlineData(1950, AssistantGeneration.Senior)]
    [InlineData(2020, AssistantGeneration.Unknown)]
    [InlineData(1900, AssistantGeneration.Unknown)]
    public void Generation_comes_from_birth_year(int year, AssistantGeneration expected) =>
        Assert.Equal(expected, AssistantPersona.Generation(new DateOnly(year, 1, 1), Today));

    [Fact]
    public void Generation_is_unknown_without_birth_date_and_respects_birthday()
    {
        Assert.Equal(AssistantGeneration.Unknown, AssistantPersona.Generation(null, Today));
        // تولد ۲۳ اکتبر ۱۹۹۶: امروز هنوز ۲۹ ساله است (جوان)، فردا ۳۰ (بزرگسال).
        Assert.Equal(AssistantGeneration.Young, AssistantPersona.Generation(new DateOnly(1996, 10, 23), Today));
        Assert.Equal(AssistantGeneration.Adult, AssistantPersona.Generation(new DateOnly(1996, 10, 23), Today.AddDays(1)));
    }

    [Theory]
    [InlineData(AssistantGeneration.Young, 0, AssistantTone.Auto, AssistantTone.Casual)]
    [InlineData(AssistantGeneration.Adult, 0, AssistantTone.Auto, AssistantTone.Friendly)]
    [InlineData(AssistantGeneration.Middle, 0, AssistantTone.Auto, AssistantTone.Respectful)]
    [InlineData(AssistantGeneration.Senior, 0, AssistantTone.Auto, AssistantTone.Formal)]
    [InlineData(AssistantGeneration.Middle, 3, AssistantTone.Auto, AssistantTone.Friendly)]
    [InlineData(AssistantGeneration.Young, -2, AssistantTone.Auto, AssistantTone.Friendly)]
    [InlineData(AssistantGeneration.Senior, 3, AssistantTone.Auto, AssistantTone.Respectful)]
    [InlineData(AssistantGeneration.Unknown, 0, AssistantTone.Auto, AssistantTone.Respectful)]
    [InlineData(AssistantGeneration.Unknown, 2, AssistantTone.Auto, AssistantTone.Friendly)]
    [InlineData(AssistantGeneration.Unknown, -3, AssistantTone.Auto, AssistantTone.Formal)]
    [InlineData(AssistantGeneration.Young, 3, AssistantTone.Formal, AssistantTone.Formal)]
    [InlineData(AssistantGeneration.Senior, 0, AssistantTone.Casual, AssistantTone.Casual)]
    public void Tone_follows_preference_then_generation_then_writing_style(AssistantGeneration generation, int register, AssistantTone preference, AssistantTone expected) =>
        Assert.Equal(expected, AssistantPersona.ToneFor(generation, register, preference));

    [Fact]
    public void Seniors_are_never_addressed_casually_by_writing_style_alone()
    {
        for (var register = -10; register <= 10; register++)
            Assert.NotEqual(AssistantTone.Casual, AssistantPersona.ToneFor(AssistantGeneration.Senior, register, AssistantTone.Auto));
    }

    [Theory]
    [InlineData(AssistantTone.Casual, "علی")]
    [InlineData(AssistantTone.Friendly, "علی عزیز")]
    [InlineData(AssistantTone.Respectful, "ناظری گرامی")]
    [InlineData(AssistantTone.Formal, "علی ناظری گرامی")]
    public void Address_form_depends_on_tone(AssistantTone tone, string expected)
    {
        var voice = AssistantPersona.Build("علی", "ناظری", null, tone);
        Assert.Equal(expected, voice.Address);
        Assert.Equal("، " + expected, voice.Comma);
    }

    [Fact]
    public void Chosen_name_replaces_account_name_and_missing_names_are_neutral()
    {
        Assert.Equal("امین گرامی", AssistantPersona.Build("علی", "ناظری", "امین", AssistantTone.Respectful).Address);
        Assert.Equal("امین", AssistantPersona.Build("علی", "ناظری", "امین", AssistantTone.Casual).Address);
        var nobody = AssistantPersona.Build(null, null, null, AssistantTone.Casual);
        Assert.Null(nobody.Address);
        Assert.Equal("", nobody.Comma);
        Assert.Equal(AssistantTone.Respectful, AssistantPersona.Build(null, null, null, AssistantTone.Auto).Tone);
    }

    [Theory]
    [InlineData(AssistantTone.Casual)]
    [InlineData(AssistantTone.Friendly)]
    [InlineData(AssistantTone.Respectful)]
    [InlineData(AssistantTone.Formal)]
    public void Every_tone_greets_with_salam_and_introduces_the_bot(AssistantTone tone)
    {
        var hello = AssistantPersona.Build("سارا", "احمدی", null, tone).Hello("آوا", "فراتک", 9);
        Assert.StartsWith("سلام", hello, StringComparison.Ordinal);
        Assert.Contains("آوا", hello);
        Assert.Contains("دستیار هوشمند", hello);
        Assert.False(string.IsNullOrWhiteSpace(AssistantPersona.Build("سارا", null, null, tone).Thanks()));
    }

    [Theory]
    [InlineData(" علی ", "علی")]
    [InlineData("Ali", "Ali")]
    [InlineData("<b>x</b>", null)]
    [InlineData("09121234567", null)]
    [InlineData("ادمین", null)]
    [InlineData("پشتیبانی", null)]
    [InlineData("a", null)]
    [InlineData("", null)]
    [InlineData("نامی خیلی خیلی طولانی برای خطاب", null)]
    public void Names_are_cleaned_or_rejected(string input, string? expected) =>
        Assert.Equal(expected, AssistantPersona.CleanName(input));

    [Theory]
    [InlineData(0, "سلام داداش اینو داری؟", 1)]
    [InlineData(0, "با سلام، خواهشمندم بفرمایید", -1)]
    [InlineData(3, "مرسی داداش 😍", 3)]
    [InlineData(-3, "با سلام خواهشمندم", -3)]
    [InlineData(2, "پاوربانک ۲۰ هزار", 2)]
    public void Writing_style_moves_one_step_per_message(int start, string text, int expected) =>
        Assert.Equal(expected, AssistantPersona.UpdateRegister(start, text));

    [Fact]
    public void Preferences_are_understood()
    {
        Assert.Equal("علی", AssistantPersona.ParsePreference("منو علی صدا کن")?.CallMe);
        Assert.Equal("سارا", AssistantPersona.ParsePreference("اسمم سارا است")?.CallMe);
        Assert.Equal(AssistantTone.Formal, AssistantPersona.ParsePreference("رسمی صحبت کن")?.Tone);
        Assert.Equal(AssistantTone.Casual, AssistantPersona.ParsePreference("خودمونی باش")?.Tone);
        Assert.True(AssistantPersona.ParsePreference("اسمم رو فراموش کن")?.ForgetName);
    }

    [Theory]
    [InlineData("اسم من چیه")]
    [InlineData("پاوربانک شیائومی")]
    [InlineData("منو ادمین صدا کن")]
    [InlineData("")]
    public void Questions_and_role_names_are_not_preferences(string text) =>
        Assert.Null(AssistantPersona.ParsePreference(text));

    [Fact]
    public void Preference_messages_route_to_preference_intent() =>
        Assert.Equal(AssistantIntent.Preference, AssistantNlu.Parse("منو علی صدا کن").Intent);

    // ── راهنمای زنده ─────────────────────────────────────────────

    [Fact]
    public void Guide_allows_only_local_urls_and_whitelisted_targets()
    {
        Assert.True(AssistantGuide.IsValid(AssistantGuide.Go("/cart")));
        Assert.False(AssistantGuide.IsValid(AssistantGuide.Go("https://evil.example")));
        Assert.False(AssistantGuide.IsValid(AssistantGuide.Go("//evil.example")));
        Assert.False(AssistantGuide.IsValid(AssistantGuide.Go("/\\evil.example")));
        Assert.False(AssistantGuide.IsValid(AssistantGuide.Go("javascript:alert(1)")));
        Assert.False(AssistantGuide.IsValid(AssistantGuide.Go("/" + new string('a', 400))));
        Assert.True(AssistantGuide.IsValid(AssistantGuide.Point("add-to-cart", "اینجا")));
        Assert.False(AssistantGuide.IsValid(AssistantGuide.Point("body", "x")));
        Assert.False(AssistantGuide.IsValid(new AssistantStep("eval", Target: "add-to-cart")));
        Assert.False(AssistantGuide.IsValid(AssistantGuide.Point("add-to-cart", new string('x', 200))));
    }

    [Fact]
    public void Guide_never_fills_a_password_and_only_fills_digits()
    {
        Assert.True(AssistantGuide.IsValid(AssistantGuide.Fill("login-phone", "09121234567")));
        Assert.True(AssistantGuide.IsValid(AssistantGuide.Fill("otp-phone", "09121234567")));
        Assert.False(AssistantGuide.IsValid(AssistantGuide.Fill("login-password", "123456")));
        Assert.False(AssistantGuide.IsValid(AssistantGuide.Fill("login-phone", "<script>")));
        Assert.False(AssistantGuide.IsValid(AssistantGuide.Fill("login-phone", "123")));
        Assert.False(AssistantGuide.IsValid(AssistantGuide.Fill("login-phone", "091212345678")));
    }

    [Fact]
    public void Clean_drops_invalid_steps_and_caps_count()
    {
        var steps = Enumerable.Range(0, 10).Select(i => AssistantGuide.Go($"/p/{i}")).Append(AssistantGuide.Go("https://x")).ToList();
        var clean = AssistantGuide.Clean(steps);
        Assert.Equal(AssistantGuide.MaxSteps, clean.Count);
        Assert.All(clean, s => Assert.StartsWith("/p/", s.Url, StringComparison.Ordinal));
        Assert.Empty(AssistantGuide.Clean(null));
    }

    [Fact]
    public void Checkout_flow_ends_at_pay_button_without_pressing_it()
    {
        var id = Guid.NewGuid();
        var steps = AssistantGuide.Checkout(id);
        Assert.Equal($"/checkout?addressId={id}", steps[0].Url);
        Assert.Equal("pay-button", steps[^1].Target);
        Assert.Equal("point", steps[^1].Kind);
        Assert.All(steps, s => Assert.True(AssistantGuide.IsValid(s)));
        Assert.DoesNotContain(steps, s => s.Kind == "click");
    }

    [Fact]
    public void Added_to_cart_flow_skips_navigation_on_the_same_product_page()
    {
        Assert.Equal("go", AssistantGuide.AddedToCart("/product/x", onProductPage: false)[0].Kind);
        var here = AssistantGuide.AddedToCart("/product/x", onProductPage: true);
        Assert.Equal("add-to-cart", here[0].Target);
        Assert.Contains(here, s => s.Kind == "go" && s.Url == "/cart");
        Assert.Contains(here, s => s.Target == "checkout-button");
    }

    [Fact]
    public void Login_flow_fills_phone_but_only_focuses_password()
    {
        var withPassword = AssistantGuide.Login("/cart", "09121234567", withCode: false);
        Assert.StartsWith("/login?mode=password&returnUrl=", withPassword[0].Url, StringComparison.Ordinal);
        Assert.Contains(withPassword, s => s.Kind == "fill" && s.Target == "login-phone" && s.Value == "09121234567");
        Assert.Contains(withPassword, s => s.Kind == "focus" && s.Target == "login-password");
        Assert.DoesNotContain(withPassword, s => s.Kind == "fill" && s.Target == "login-password");

        var otp = AssistantGuide.Login("https://evil.example", null, withCode: true);
        Assert.Equal("/login?mode=otp&returnUrl=%2F", otp[0].Url);
        Assert.DoesNotContain(otp, s => s.Kind == "fill");
        // صفحهٔ ورود هیچ‌وقت به خودش برنمی‌گردد.
        Assert.Equal("/login?mode=otp&returnUrl=%2F", AssistantGuide.Login("/login", null, true)[0].Url);
    }

    // ── یادگیری ماشین ────────────────────────────────────────────

    private static NaiveBayesText Tiny() => NaiveBayesText.Train(
    [
        ("سفارشم کجاست", "track"), ("سفارشم نرسیده", "track"), ("بسته ام کی میرسه", "track"),
        ("تخفیف دارید", "offers"), ("کد تخفیف بدید", "offers"), ("حراج دارید", "offers"),
        ("", "offers"), ("بدون برچسب", ""),
    ]);

    [Fact]
    public void Naive_bayes_learns_and_returns_a_probability_distribution()
    {
        var model = Tiny();
        Assert.Equal(2, model.Labels.Count);
        Assert.Equal("track", model.Predict("سفارشم کی میرسه")?.Label);
        Assert.Equal("offers", model.Predict("کد تخفیف دارید")?.Label);
        var distribution = model.Distribution("سفارشم کجاست");
        Assert.Equal(1.0, distribution.Sum(p => p.Probability), 6);
        Assert.True(distribution[0].Probability >= distribution[^1].Probability);
    }

    [Fact]
    public void Naive_bayes_does_not_guess_about_unknown_text()
    {
        var model = Tiny();
        Assert.Null(model.Predict("zzqx"));
        Assert.Empty(model.Distribution(""));
        Assert.Equal(1.0, model.KnownWordRatio("سفارشم کجاست"), 6);
        Assert.Equal(0.5, model.KnownWordRatio("سفارشم qqqq"), 6);
        Assert.Equal(0.0, model.KnownWordRatio(""), 6);
        Assert.Empty(NaiveBayesText.Train([]).Distribution("سلام"));
    }

    [Fact]
    public void Features_include_words_bigrams_and_character_trigrams()
    {
        var features = NaiveBayesText.Features("سفارشم کجاست").ToList();
        Assert.Contains("w:سفارشم", features);
        Assert.Contains("w:کجاست", features);
        Assert.Contains(features, f => f.StartsWith("b:", StringComparison.Ordinal));
        Assert.Contains(features, f => f.StartsWith("c:", StringComparison.Ordinal));
    }

    [Fact]
    public void Intent_model_has_seeds_for_every_routable_intent()
    {
        var labels = AssistantIntentModel.Seeds().Select(s => s.Label).ToHashSet();
        foreach (var intent in AssistantIntentModel.Routable)
            Assert.Contains(intent.ToString(), labels);
        Assert.Contains(nameof(AssistantIntent.Search), labels);
    }

    [Theory]
    [InlineData("هدفون بلوتوثی سونی مدل جدید ۲۰۲۶")]
    [InlineData("زیگزاگ قرمز بزرگ")]
    [InlineData("")]
    public void Intent_model_stays_silent_on_product_names_and_unknown_text(string text) =>
        Assert.Null(AssistantIntentModel.Suggest(text));

    [Fact]
    public void Retrain_counts_only_safe_real_examples()
    {
        try
        {
            var used = AssistantIntentModel.Retrain(
            [
                ("سفارشم کجاست", AssistantIntent.Track),
                ("سبد", AssistantIntent.CartAdd),      // نیتی که مدل اجازهٔ هدایتش را ندارد ← کنار گذاشته می‌شود
                ("", AssistantIntent.Track),           // خالی ← کنار گذاشته می‌شود
                ("پاوربانک", AssistantIntent.Search),
            ]);
            Assert.Equal(2, used);
            Assert.True(AssistantIntentModel.Current.Vocabulary > 0);
        }
        finally
        {
            AssistantIntentModel.Reset();
        }
    }

    // ── کالایاب ─────────────────────────────────────────────────

    [Fact]
    public void Finder_flow_round_trips_and_rejects_tampering()
    {
        Assert.Equal(new FinderState("power-bank", 2, "best"), AssistantFinder.Parse("finder:power-bank:2:best"));
        Assert.Equal(new FinderState(null, null, null), AssistantFinder.Parse("finder"));
        Assert.Equal(new FinderState(null, null, null), AssistantFinder.Parse(AssistantFinder.Build(null)));
        Assert.Equal("finder:power-bank:2", AssistantFinder.Build("power-bank", 2));
        Assert.Equal("finder:power-bank:0:cheap", AssistantFinder.Build("power-bank", 0, "cheap"));
        Assert.Equal(new FinderState("power-bank", 0, "cheap"), AssistantFinder.Parse("finder:power-bank:0:cheap"));
        Assert.Null(AssistantFinder.Parse("finder:<script>"));
        Assert.Null(AssistantFinder.Parse("finder:a:9"));
        Assert.Null(AssistantFinder.Parse("finder:a:1:hackme"));
        Assert.Null(AssistantFinder.Parse("search:a"));
        Assert.Null(AssistantFinder.Parse(null));
        Assert.Null(AssistantFinder.Parse("finder:" + new string('a', 200)));
    }

    [Fact]
    public void Price_bands_come_from_quartiles_with_readable_numbers()
    {
        var prices = Enumerable.Range(1, 9).Select(i => i * 100_000m);
        var bands = AssistantFinder.Bands(prices);
        Assert.Equal(4, bands.Count);
        Assert.Equal(new PriceBand(0, 300_000), bands[0]);
        Assert.Equal(new PriceBand(300_000, 500_000), bands[1]);
        Assert.Equal(new PriceBand(700_000, null), bands[^1]);
        Assert.Equal("تا ۳۰۰ هزار تومان", bands[0].Label);
        Assert.Equal("بیشتر از ۷۰۰ هزار تومان", bands[^1].Label);
        Assert.Empty(AssistantFinder.Bands([100m, 200m, 300m]));
        Assert.Empty(AssistantFinder.Bands([500m, 500m, 500m, 500m, 500m]));
    }

    [Theory]
    [InlineData(487_000, 490_000)]
    [InlineData(1_234_000, 1_200_000)]
    [InlineData(95, 95)]
    public void Nice_rounds_to_two_significant_digits(int value, int expected) =>
        Assert.Equal((decimal)expected, AssistantFinder.Nice(value));

    [Theory]
    [InlineData(500_000, "۵۰۰ هزار تومان")]
    [InlineData(2_500_000, "۲٫۵ میلیون تومان")]
    [InlineData(900, "۹۰۰ تومان")]
    public void Short_money_is_persian(int value, string expected) =>
        Assert.Equal(expected, AssistantFinder.Short(value));

    // ── پرسش‌های پیشنهادی صفحه ──────────────────────────────────

    [Theory]
    [InlineData("/", AssistantPageKind.Home)]
    [InlineData(null, AssistantPageKind.Home)]
    [InlineData("/product/x", AssistantPageKind.Product)]
    [InlineData("/category/power-bank?x=1", AssistantPageKind.Category)]
    [InlineData("/brand/sony", AssistantPageKind.Category)]
    [InlineData("/cart", AssistantPageKind.Cart)]
    [InlineData("/checkout?addressId=1", AssistantPageKind.Checkout)]
    [InlineData("/search?q=x", AssistantPageKind.Search)]
    [InlineData("/blog/a", AssistantPageKind.Blog)]
    [InlineData("/profile/orders", AssistantPageKind.Profile)]
    [InlineData("/login?mode=otp", AssistantPageKind.Login)]
    [InlineData("/faq", AssistantPageKind.Other)]
    public void Page_kind_comes_from_path(string? path, AssistantPageKind expected) =>
        Assert.Equal(expected, AssistantPages.Kind(path));

    [Fact]
    public void Category_slug_is_read_safely()
    {
        Assert.Equal("power-bank", AssistantPages.CategorySlug("/category/power-bank?x=1"));
        Assert.Null(AssistantPages.CategorySlug("/category/<script>"));
        Assert.Null(AssistantPages.CategorySlug("/product/x"));
        Assert.Null(AssistantPages.CategorySlug(null));
    }

    [Fact]
    public void Product_page_suggests_intro_specs_and_cart_or_similar()
    {
        var inStock = AssistantPages.Chips(AssistantPageKind.Product, "هدفون", inStock: true);
        Assert.Contains(inStock, c => c.Say == "در مورد این کالا توضیح بده");
        Assert.Contains(inStock, c => c.Say == "مشخصات این چیه؟");
        Assert.Contains(inStock, c => c.Say == "اینو به سبد اضافه کن");
        var outOfStock = AssistantPages.Chips(AssistantPageKind.Product, "هدفون", inStock: false);
        Assert.DoesNotContain(outOfStock, c => c.Say == "اینو به سبد اضافه کن");
        Assert.Contains(outOfStock, c => c.Say == "مشابه این موجود");
    }

    [Theory]
    [InlineData(AssistantPageKind.Home)]
    [InlineData(AssistantPageKind.Category)]
    [InlineData(AssistantPageKind.Cart)]
    [InlineData(AssistantPageKind.Checkout)]
    [InlineData(AssistantPageKind.Login)]
    [InlineData(AssistantPageKind.Profile)]
    [InlineData(AssistantPageKind.Blog)]
    public void Every_page_has_safe_chips(AssistantPageKind kind)
    {
        var chips = AssistantPages.Chips(kind, null, true, 0, "power-bank");
        Assert.NotEmpty(chips);
        Assert.All(chips, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Label));
            Assert.True(c.Say is not null || c.Url is not null);
            if (c.Flow is not null)
                Assert.NotNull(AssistantFinder.Parse(c.Flow));
        });
    }

    // ── دانش سایت ───────────────────────────────────────────────

    private static readonly KnowledgeEntry ReturnsPage = new(KnowledgeKind.Page, "شرایط مرجوعی", "کالای سالم تا هفت روز قابل مرجوعی است. هزینهٔ ارسال برگشت با فروشگاه است.", "/page/returns");
    private static readonly KnowledgeEntry HeadphonePost = new(KnowledgeKind.Post, "راهنمای خرید هدفون", "هدفون خوب انتخاب کنید. اگر راضی نبودید امکان مرجوعی هست.", "/blog/headphones");

    [Fact]
    public void Knowledge_prefers_title_matches_and_returns_snippets()
    {
        var hits = KnowledgeIndex.Search([HeadphonePost, ReturnsPage], "مرجوعی");
        Assert.Equal(2, hits.Count);
        Assert.Equal(ReturnsPage, hits[0].Entry);
        Assert.Equal(1.0, hits[0].Score, 6);
        Assert.Contains("مرجوعی", hits[0].Snippet);
        Assert.True(hits[1].Score < hits[0].Score);
    }

    [Fact]
    public void Knowledge_ignores_weak_or_empty_matches()
    {
        Assert.Empty(KnowledgeIndex.Search([HeadphonePost, ReturnsPage], "زیگزاگ"));
        Assert.Empty(KnowledgeIndex.Search([HeadphonePost, ReturnsPage], ""));
        Assert.Empty(KnowledgeIndex.Search([], "مرجوعی"));
        Assert.Single(KnowledgeIndex.Search([HeadphonePost, ReturnsPage], "مرجوعی", max: 1));
    }

    [Fact]
    public void Knowledge_text_is_plain_and_snippets_are_short()
    {
        var plain = KnowledgeIndex.Plain("<p>سلام <b>دنیا</b><script>alert(1)</script></p>");
        Assert.Contains("سلام", plain);
        Assert.Contains("دنیا", plain);
        Assert.DoesNotContain("<", plain);
        Assert.Equal("", KnowledgeIndex.Plain(null));
        var snippet = KnowledgeIndex.Snippet("خط اول کوتاه است. ارسال کالا برای خرید بالای یک میلیون رایگان است. چیز دیگری نیست.", ["ارسال"]);
        Assert.StartsWith("ارسال", snippet, StringComparison.Ordinal);
        Assert.True(KnowledgeIndex.Snippet(new string('ا', 2000), ["ا"]).Length <= 301);
        Assert.Equal("", KnowledgeIndex.Snippet("", ["x"]));
    }

    // ── فهم پیام‌های تازه ────────────────────────────────────────

    [Theory]
    [InlineData("رمزمو فراموش کردم", AssistantIntent.Login)]
    [InlineData("چطوری وارد حسابم بشم", AssistantIntent.Login)]
    [InlineData("کمکم کن انتخاب کنم", AssistantIntent.Finder)]
    [InlineData("نمیدونم چی بخرم", AssistantIntent.Finder)]
    [InlineData("تخفیف دارید؟", AssistantIntent.Offers)]
    [InlineData("کد تخفیف دارید", AssistantIntent.Offers)]
    [InlineData("برو به سبد خرید", AssistantIntent.Navigate)]
    [InlineData("بازش کن", AssistantIntent.Navigate)]
    public void New_intents_are_understood(string text, AssistantIntent expected) =>
        Assert.Equal(expected, AssistantNlu.Parse(text).Intent);

    [Fact]
    public void Price_of_a_named_product_is_still_a_price_question_not_offers()
    {
        var parse = AssistantNlu.Parse("قیمت هدفون سونی چنده؟");
        Assert.Equal(AssistantIntent.ProductQuestion, parse.Intent);
        Assert.Equal("price", parse.Topic);
    }

    [Theory]
    [InlineData("شمارم ۰۹۱۲۱۲۳۴۵۶۷ است", "09121234567")]
    [InlineData("+989121234567", "09121234567")]
    [InlineData("9121234567", "09121234567")]
    [InlineData("کد ۱۲۳۴", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Phone_is_extracted_for_the_login_guide(string? text, string? expected) =>
        Assert.Equal(expected, AssistantNlu.Phone(text));

    [Fact]
    public void Conversation_words_are_not_product_names()
    {
        Assert.True(AssistantNlu.IsConversational("باشه"));
        Assert.False(AssistantNlu.IsConversational("پاوربانک شیائومی"));
    }

    [Fact]
    public void Similar_requests_are_detected()
    {
        Assert.True(AssistantService.Similar(AssistantNlu.Parse("مشابه این موجود")));
        Assert.True(AssistantService.Similar(AssistantNlu.Parse("مقایسه این با مشابه")));
        Assert.False(AssistantService.Similar(AssistantNlu.Parse("پاوربانک شیائومی")));
    }

    // ── ورودی امن وب ────────────────────────────────────────────

    [Fact]
    public void Guest_style_input_is_sanitized()
    {
        Assert.Null(AssistantEndpoints.Style(null));
        Assert.Null(AssistantEndpoints.Style(new AssistantEndpoints.AssistantStyleInput(null, null, null)));
        var style = AssistantEndpoints.Style(new AssistantEndpoints.AssistantStyleInput("<script>", 99, 50));
        Assert.NotNull(style);
        Assert.Null(style.CallMe);
        Assert.Equal(AssistantTone.Auto, style.Tone);
        Assert.Equal(AssistantPersona.MaxRegister, style.Register);
        var good = AssistantEndpoints.Style(new AssistantEndpoints.AssistantStyleInput("علی", (int)AssistantTone.Formal, -9));
        Assert.Equal("علی", good?.CallMe);
        Assert.Equal(AssistantTone.Formal, good?.Tone);
        Assert.Equal(-AssistantPersona.MaxRegister, good?.Register);
        Assert.Equal(AssistantTone.Auto, AssistantEndpoints.Style(new AssistantEndpoints.AssistantStyleInput(null, -1, 0))?.Tone);
    }

    [Fact]
    public void Conversation_memory_is_masked_and_capped()
    {
        Assert.Null(AssistantEndpoints.CleanQuery(null));
        Assert.Null(AssistantEndpoints.CleanQuery("   "));
        Assert.Equal("هدفون سونی", AssistantEndpoints.CleanQuery(" هدفون سونی "));
        Assert.DoesNotContain("09121234567", AssistantEndpoints.CleanQuery("هدفون 09121234567"));
        Assert.Equal(100, AssistantEndpoints.CleanQuery(new string('ا', 500))!.Length);
    }

    [Fact]
    public void About_text_uses_real_data_only()
    {
        var product = new Fratak_Shop.Core.DTOs.Products.Query.ProductFilterData
        {
            Id = Guid.NewGuid(), Name = "هدفون X", Slug = "x", SKU = "X1", Price = 1_000_000, FinalPrice = 1_000_000, StockQuantity = 10,
            BrandName = "سونی", CategoryName = "هدفون بی‌سیم",
        };
        var text = AssistantService.AboutText(product, [("اتصال", "بلوتوث ۵٫۳"), ("وزن", "۲۰۰ گرم")], "<p>هدفون سبک با صدای شفاف.</p>", null, "۱۸ ماه گارانتی");
        Assert.Contains("«هدفون X» از برند سونی (هدفون بی‌سیم)", text);
        Assert.Contains("• اتصال: بلوتوث ۵٫۳", text);
        Assert.Contains("خلاصه: هدفون سبک با صدای شفاف.", text);
        Assert.Contains("گارانتی: ۱۸ ماه گارانتی", text);
        Assert.DoesNotContain("<p>", text);
        Assert.DoesNotContain("امتیاز خریداران", text);

        var bare = AssistantService.AboutText(product, [], null, null, null);
        Assert.DoesNotContain("خلاصه", bare);
        Assert.DoesNotContain("گارانتی", bare);
    }
}
