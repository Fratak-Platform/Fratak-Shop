using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Services.Assistant;
using Fratak_Shop.Core.Services.Comments;
using Fratak_Shop.Core.Services.Favorites;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.Assistant;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های واحد دور ۲۵: «دستیار حرفه‌ای»، «پیش‌نویس پاسخ پرسش کالا» و «خبر ارزان شدن علاقه‌مندی‌ها».
/// همه بدون پایگاه داده (فقط منطق خالص).
/// <list type="bullet">
/// <item>آماده‌سازی متن (<see cref="AssistantLanguage"/>): کیبورد اشتباه، فینگلیش، کشیدهٔ حروف، محاوره، و اینکه متن درست دست نخورد.</item>
/// <item>خواسته‌های خرید (<see cref="AssistantSlotParser"/>): بودجه (زیر/بین/حدود)، مرتب‌سازی، کاربرد، و عددی که قیمت نیست («۲۰ وات»).</item>
/// <item>فهم کامل (<see cref="AssistantUnderstanding"/>): لغو/مرجوعی/فاکتور، مشاور خرید، و اینکه «شرایط مرجوعی» ثبت مرجوعی نیست.</item>
/// <item>مشاور خرید (<see cref="AssistantAdvisor"/>) و خلاصهٔ نظر خریداران (<see cref="AssistantReviews"/>).</item>
/// <item>آموزش دستیار (<see cref="AssistantTeachingService.Validate"/>): ورودی خراب، لینک بیرونی و کار غیرمجاز رد می‌شود.</item>
/// <item>پیش‌نویس پاسخ پرسش (<see cref="QuestionAnswerAdvisor.Build"/>) و آستانهٔ کاهش قیمت (<see cref="FavoritePriceWatch.IsDrop"/>).</item>
/// </list>
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Assistant")]
public class AssistantRound25Tests
{
    // ── ۱. آماده‌سازی متن ───────────────────────────────────────

    [Fact]
    public void Prepare_converts_wrong_keyboard_layout()
    {
        // «sghl» روی کیبورد فارسی همان «سلام» است.
        var prepared = AssistantLanguage.Prepare("sghl");
        Assert.Equal("سلام", prepared.Text);
        Assert.Contains("layout", prepared.Fixes);
        Assert.True(prepared.Changed);
    }

    [Fact]
    public void Prepare_converts_finglish_shopping_words()
    {
        var prepared = AssistantLanguage.Prepare("gheymat powerbank chande");
        Assert.Equal("قیمت پاوربانک چنده", prepared.Text);
        Assert.Contains("finglish", prepared.Fixes);
    }

    [Fact]
    public void Prepare_collapses_stretched_letters_and_colloquial_words()
    {
        Assert.Equal("سلام", AssistantLanguage.Prepare("سلاااام").Text);
        Assert.Equal("باتری", AssistantLanguage.Prepare("باطری").Text);
    }

    [Theory]
    [InlineData("پاوربانک شیائومی")]
    [InlineData("iPhone 15 Pro")]
    [InlineData("سفارش FR-20261022-123456 کجاست")]
    public void Prepare_leaves_correct_text_untouched(string text)
    {
        // متن درست (یا نام مدل انگلیسی یا شمارهٔ سفارش) هرگز «اصلاح» نمی‌شود.
        var prepared = AssistantLanguage.Prepare(text);
        Assert.False(prepared.Changed);
        Assert.Equal(text, prepared.Text);
    }

    [Fact]
    public void Prepare_handles_empty_and_hostile_input()
    {
        Assert.Equal("", AssistantLanguage.Prepare(null).Text);
        Assert.Equal("", AssistantLanguage.Prepare("   ").Text);
        // متن خیلی بلند بریده می‌شود و خطا نمی‌دهد.
        var huge = AssistantLanguage.Prepare(new string('ب', 5000) + " <script>");
        Assert.True(huge.Text.Length <= 5000);
    }

    [Fact]
    public void Correct_never_guesses_far_words_and_distance_is_bounded()
    {
        Assert.Null(AssistantLanguage.Correct("زیگزاگ", AssistantVocabulary.Base));
        // واژهٔ آشنا دست نمی‌خورد.
        Assert.Null(AssistantLanguage.Correct("پاوربانک", AssistantVocabulary.Base));
        Assert.Equal(1, AssistantLanguage.Distance("باتری", "باطری"));
        Assert.Equal(0, AssistantLanguage.Distance("هدفون", "هدفون"));
        Assert.True(AssistantLanguage.Distance("پاوربانک", "هندزفری") > 2);
    }

    [Fact]
    public void Catalog_vocabulary_knows_product_words()
    {
        var vocabulary = new AssistantVocabulary(["پاوربانک انکر مدل A1234", "هدفون سونی"]);
        Assert.True(vocabulary.HasCatalog);
        Assert.True(vocabulary.Contains("انکر"));
        Assert.True(vocabulary.Contains("سونی"));
        Assert.False(AssistantVocabulary.Base.HasCatalog);
    }

    // ── ۲. خواسته‌های خرید ──────────────────────────────────────

    [Fact]
    public void Slots_read_max_budget()
    {
        var slots = AssistantSlotParser.Extract("هدفون زیر ۲ میلیون");
        Assert.Equal(2_000_000m, slots.MaxPrice);
        Assert.Null(slots.MinPrice);
        Assert.Equal("هدفون", slots.Rest);
        Assert.True(slots.HasBudget);
    }

    [Fact]
    public void Slots_read_budget_range_and_approximate()
    {
        var range = AssistantSlotParser.Extract("گوشی بین ۵ تا ۸ میلیون");
        Assert.Equal(5_000_000m, range.MinPrice);
        Assert.Equal(8_000_000m, range.MaxPrice);

        var approx = AssistantSlotParser.Extract("ساعت هوشمند حدود ۳ میلیون");
        Assert.True(approx.Approximate);
        Assert.Equal(2_400_000m, approx.MinPrice);
        Assert.Equal(3_600_000m, approx.MaxPrice);
    }

    [Fact]
    public void Slots_read_min_budget()
    {
        var slots = AssistantSlotParser.Extract("هدفون بالای ۵۰۰ هزار تومان");
        Assert.Equal(500_000m, slots.MinPrice);
        Assert.Null(slots.MaxPrice);
    }

    [Theory]
    [InlineData("پاوربانک ۲۰ هزار")]
    [InlineData("شارژر زیر ۲۰ وات")]
    [InlineData("پاوربانک شیائومی")]
    public void Slots_do_not_mistake_capacity_or_power_for_price(string text)
    {
        // «۲۰ هزار» در پاوربانک ظرفیت است و «۲۰ وات» توان؛ هیچ‌کدام بودجه نیست.
        Assert.False(AssistantSlotParser.Extract(text).HasBudget);
    }

    [Fact]
    public void Slots_read_sort_purpose_and_advice()
    {
        Assert.Equal(ProductSortBy.Cheapest, AssistantSlotParser.Extract("ارزون‌ترین پاوربانک").Sort);
        Assert.Equal(ProductSortBy.BestSelling, AssistantSlotParser.Extract("پرفروش‌ترین هدفون").Sort);
        Assert.Equal("sport", AssistantSlotParser.Extract("هدفون برای ورزش").Purpose);
        Assert.Equal("gaming", AssistantSlotParser.Extract("هدست برای بازی").Purpose);
        Assert.True(AssistantSlotParser.Extract("بهترین هندزفری").Advice);
        Assert.Equal("ورزش", AssistantSlotParser.Purpose("sport")!.Title);
        Assert.Null(AssistantSlotParser.Purpose("unknown"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("سلام")]
    [InlineData("!!!")]
    public void Slots_are_empty_for_plain_messages(string text)
    {
        var slots = AssistantSlotParser.Extract(text);
        Assert.False(slots.Any);
        Assert.False(slots.HasBudget);
    }

    [Fact]
    public void Slots_reject_absurd_amounts()
    {
        // مبلغ بیرون از بازهٔ معقول (بیش از ۱۰ میلیارد) قیمت حساب نمی‌شود.
        var slots = AssistantSlotParser.Extract("گوشی زیر 99999999999999 تومان");
        Assert.True(slots.MaxPrice is null || slots.MaxPrice <= AssistantSlotParser.MaxAmount);
    }

    [Fact]
    public void Budget_label_is_short_and_readable()
    {
        Assert.StartsWith("زیر", AssistantSlotParser.BudgetLabel(null, 2_000_000), StringComparison.Ordinal);
        Assert.StartsWith("بالای", AssistantSlotParser.BudgetLabel(500_000, null), StringComparison.Ordinal);
        Assert.Contains(" تا ", AssistantSlotParser.BudgetLabel(1_000_000, 2_000_000), StringComparison.Ordinal);
        Assert.Equal("", AssistantSlotParser.BudgetLabel(null, null));
    }

    // ── ۳. فهم کامل پیام ────────────────────────────────────────

    [Theory]
    [InlineData("سفارشمو کنسل کن", AssistantIntent.OrderCancel)]
    [InlineData("میخوام سفارشم رو لغو کنم", AssistantIntent.OrderCancel)]
    [InlineData("سفارش FR-20261022-123456 رو لغو کن", AssistantIntent.OrderCancel)]
    [InlineData("میخوام مرجوع کنم", AssistantIntent.OrderReturn)]
    [InlineData("میخوام خریدمو پس بدم", AssistantIntent.OrderReturn)]
    [InlineData("فاکتور سفارشم", AssistantIntent.Invoice)]
    [InlineData("فاکتور میخوام", AssistantIntent.Invoice)]
    [InlineData("منو علی صدا کن", AssistantIntent.Preference)]
    [InlineData("هدفون زیر ۲ میلیون", AssistantIntent.Advisor)]
    [InlineData("بهترین هندزفری برای ورزش", AssistantIntent.Advisor)]
    public void Understand_routes_new_intents(string text, AssistantIntent expected) =>
        Assert.Equal(expected, AssistantUnderstanding.Understand(text).Route);

    [Theory]
    [InlineData("شرایط مرجوعی چیه")]
    [InlineData("مهلت مرجوعی چند روزه")]
    public void Return_policy_question_is_not_a_return_request(string text)
    {
        var understood = AssistantUnderstanding.Understand(text);
        Assert.NotEqual(AssistantIntent.OrderReturn, understood.Route);
        Assert.Equal(AssistantIntent.ProductQuestion, understood.Route);
    }

    [Theory]
    [InlineData("کنسل شد؟")]
    [InlineData("کنسل")]
    public void Cancel_word_without_order_context_is_not_a_cancel_request(string text) =>
        Assert.NotEqual(AssistantIntent.OrderCancel, AssistantUnderstanding.Understand(text).Route);

    [Fact]
    public void Understand_keeps_product_query_and_budget_for_advisor()
    {
        var understood = AssistantUnderstanding.Understand("هدفن بلوتوثی زیر ۲ ملیون");
        Assert.Equal(AssistantIntent.Advisor, understood.Route);
        Assert.Equal(2_000_000m, understood.Slots.MaxPrice);
        Assert.Contains("هدفون", understood.ProductQuery, StringComparison.Ordinal);
    }

    [Fact]
    public void Understand_never_corrects_customer_name()
    {
        // اسم مشتری (حتی ناآشنا) نباید به واژهٔ دیگری «اصلاح» شود.
        var understood = AssistantUnderstanding.Understand("منو باطری صدا کن");
        Assert.Equal(AssistantIntent.Preference, understood.Route);
        Assert.False(understood.Prepared.Changed);
    }

    [Fact]
    public void Understand_key_normalizes_for_comparison()
    {
        Assert.Equal(AssistantUnderstanding.Key("ارسال  به كيش"), AssistantUnderstanding.Key("ارسال به کیش"));
        Assert.Equal("", AssistantUnderstanding.Key(null));
    }

    // ── ۴. مشاور خرید ───────────────────────────────────────────

    private static ProductFilterData Product(string name, decimal price, int stock = 10, double rating = 0, int reviews = 0, int sales = 0) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Slug = "p-" + Guid.NewGuid().ToString("N")[..8],
        SKU = "S",
        Price = price,
        FinalPrice = price,
        StockQuantity = stock,
        IsActive = true,
        AverageRating = rating,
        ReviewCount = reviews,
        SalesCount = sales,
    };

    [Fact]
    public void Advisor_prefers_in_stock_well_rated_and_best_selling()
    {
        var weak = Product("هدفون ساده", 500_000, stock: 0);
        var good = Product("هدفون محبوب", 900_000, rating: 4.6, reviews: 30, sales: 50);
        var mid = Product("هدفون معمولی", 700_000, rating: 3.5, reviews: 2, sales: 1);
        Assert.True(AssistantAdvisor.Score(good, null) > AssistantAdvisor.Score(mid, null));
        Assert.True(AssistantAdvisor.Score(mid, null) > AssistantAdvisor.Score(weak, null));

        var picks = AssistantAdvisor.Pick([weak, mid, good], null, keepOrder: false);
        Assert.Equal(3, picks.Count);
        Assert.Equal(good.Id, picks[0].Product.Id);
        Assert.Equal(weak.Id, picks[^1].Product.Id);
        Assert.Contains("فعلاً ناموجود", picks[^1].Cons);
    }

    [Fact]
    public void Advisor_keeps_database_order_when_customer_asked_a_sort()
    {
        var a = Product("الف", 100_000);
        var b = Product("ب", 200_000, rating: 5, reviews: 50, sales: 100);
        var picks = AssistantAdvisor.Pick([a, b], null, keepOrder: true);
        // «ارزان‌ترین» خواسته شده: ترتیب پایگاه داده (ارزان اول) حفظ می‌شود، نه امتیاز.
        Assert.Equal(a.Id, picks[0].Product.Id);
    }

    [Fact]
    public void Advisor_matches_purpose_and_explains_honestly()
    {
        var gaming = AssistantSlotParser.Purpose("gaming");
        var plain = Product("هدفون ساده", 800_000);
        var gamer = Product("هدفون گیمینگ RGB", 800_000);
        Assert.True(AssistantAdvisor.PurposeHits(gamer, gaming) >= 1);
        Assert.Equal(0, AssistantAdvisor.PurposeHits(plain, gaming));
        Assert.Equal(0, AssistantAdvisor.PurposeHits(gamer, null));

        var picks = AssistantAdvisor.Pick([plain, gamer], gaming, keepOrder: false);
        Assert.Equal(gamer.Id, picks[0].Product.Id);
        Assert.Equal("مناسب " + gaming!.Title, picks[0].Badge);

        // نکتهٔ منفی صادقانه: بدون نظر و بالاتر از بودجه.
        var cons = AssistantAdvisor.Cons(Product("x", 3_000_000, stock: 2), 2_000_000);
        Assert.Contains("هنوز نظری ثبت نشده", cons);
        Assert.True(cons.Count <= 2);
        Assert.Empty(AssistantAdvisor.Pick([], null, keepOrder: false));
    }

    [Fact]
    public void Advisor_line_and_price_bands()
    {
        var pick = AssistantAdvisor.Pick([Product("هدفون آزمون", 1_200_000, rating: 4.5, reviews: 12)], null, keepOrder: false)[0];
        var line = AssistantAdvisor.Line(1, pick);
        Assert.StartsWith("۱. هدفون آزمون", line, StringComparison.Ordinal);
        Assert.Contains("✓", line, StringComparison.Ordinal);

        var bands = AssistantAdvisor.Bands([400_000m, 800_000m, 1_200_000m, 1_800_000m, 2_500_000m, 3_000_000m]);
        Assert.Equal(3, bands.Count);
        Assert.Null(bands[0].Min);
        Assert.Null(bands[2].Max);
        Assert.Equal(bands[0].Max, bands[1].Min);
        // کمتر از سه قیمت یا قیمت یکسان ← بی‌دکمه.
        Assert.Empty(AssistantAdvisor.Bands([100_000m, 200_000m]));
        Assert.Empty(AssistantAdvisor.Bands([500_000m, 500_000m, 500_000m]));
        Assert.Equal(500_000m, AssistantAdvisor.Round(480_000m));
        Assert.Equal(1_500_000m, AssistantAdvisor.Round(1_400_000m));
    }

    [Fact]
    public void Variety_is_deterministic_and_stays_in_options()
    {
        var first = AssistantVariety.Pick("هدفون زیر ۲ میلیون", "الف", "ب", "ج");
        Assert.Equal(first, AssistantVariety.Pick("هدفون زیر ۲ میلیون", "الف", "ب", "ج"));
        Assert.Contains(first, new[] { "الف", "ب", "ج" });
        Assert.Equal("", AssistantVariety.Pick("x"));
        Assert.Equal(AssistantVariety.Hash(null), AssistantVariety.Hash(""));
    }

    // ── ۵. خلاصهٔ نظر خریداران ──────────────────────────────────

    [Fact]
    public void Reviews_are_summarized_per_aspect()
    {
        var summary = AssistantReviews.Summarize(
        [
            new ReviewInput("صدای این هدفون عالیه", 5, true),
            new ReviewInput("باتریش خیلی بد و ضعیفه", 1, false),
        ]);
        Assert.Equal(2, summary.Count);
        Assert.Equal(3.0, summary.AverageRating);
        Assert.Contains(summary.Aspects, a => a.Key == "sound" && a.Positive >= 1);
        Assert.Contains(summary.Aspects, a => a.Key == "battery" && a.Negative >= 1);

        var text = AssistantReviews.Text(summary, "هدفون آزمون", "sound");
        Assert.NotNull(text);
        Assert.Contains("هدفون آزمون", text, StringComparison.Ordinal);
        Assert.Contains("از ۲ نظر", text, StringComparison.Ordinal);

        // جنبه‌ای که کسی درباره‌اش ننوشته: صادقانه گفته می‌شود.
        Assert.Contains("چیزی ننوشته", AssistantReviews.Text(summary, "هدفون آزمون", "camera"), StringComparison.Ordinal);
    }

    [Fact]
    public void Reviews_without_comments_give_no_text()
    {
        var empty = AssistantReviews.Summarize([]);
        Assert.Equal(0, empty.Count);
        Assert.Null(empty.AverageRating);
        Assert.Null(AssistantReviews.Text(empty, "x"));
        Assert.Empty(AssistantReviews.Clauses(null));
        Assert.Equal(2, AssistantReviews.Clauses("صداش عالیه ولی باتریش ضعیفه.").Count);
    }

    // ── ۶. آموزش دستیار ────────────────────────────────────────

    [Fact]
    public void Teaching_accepts_valid_answer_or_safe_intent()
    {
        Assert.Null(AssistantTeachingService.Validate(new AssistantTeachingInput("ارسال به کیش دارید؟", "بله، به کیش هم ارسال داریم.", "/faq", null)));
        Assert.Null(AssistantTeachingService.Validate(new AssistantTeachingInput("بسته من کجاست", null, null, AssistantIntent.Track)));
        Assert.Null(AssistantTeachingService.Validate(new AssistantTeachingInput("میخوام فاکتور بگیرم", null, null, AssistantIntent.Invoice)));
    }

    public static TheoryData<string?, string?, string?, AssistantIntent?> BadTeachings => new()
    {
        { null, "جواب", null, null },                                       // بی‌پرسش
        { "ab", "جواب", null, null },                                       // پرسش خیلی کوتاه
        { new string('ب', 301), "جواب", null, null },                       // پرسش خیلی بلند
        { "؟؟؟ !!!", "جواب", null, null },                                 // پرسش بی‌واژه
        { "ارسال به کیش", new string('ج', 1001), null, null },             // جواب خیلی بلند
        { "ارسال به کیش", null, null, null },                               // نه جواب، نه کار
        { "ارسال به کیش", "بله", "https://evil.example/x", null },          // لینک بیرونی
        { "ارسال به کیش", "بله", "//evil.example", null },                  // لینک بی‌پروتکل بیرونی
        { "ارسال به کیش", "بله", "/a\\b", null },                           // بک‌اسلش
        { "ارسال به کیش", "بله", "/faq page", null },                       // فاصله در لینک
        { "ارسال به کیش", "بله", "javascript:alert(1)", null },             // حملهٔ اسکریپت
        { "سبد رو خالی کن", null, null, AssistantIntent.CartRemove },       // کار خطرناک (حذف سبد)
        { "اینو بخر", null, null, AssistantIntent.CartAdd },                // کار بدون تأیید مشتری
        { "سلام", null, null, AssistantIntent.Advisor },                    // کار خارج از فهرست
    };

    [Theory]
    [MemberData(nameof(BadTeachings))]
    public void Teaching_rejects_bad_or_dangerous_input(string? question, string? answer, string? url, AssistantIntent? intent) =>
        Assert.NotNull(AssistantTeachingService.Validate(new AssistantTeachingInput(question, answer, url, intent)));

    [Fact]
    public void Teaching_whitelist_has_only_safe_intents()
    {
        Assert.DoesNotContain(AssistantIntent.CartAdd, AssistantTeachingService.Teachable);
        Assert.DoesNotContain(AssistantIntent.CartRemove, AssistantTeachingService.Teachable);
        Assert.Contains(AssistantIntent.Track, AssistantTeachingService.Teachable);
        Assert.Contains(AssistantIntent.OrderCancel, AssistantTeachingService.Teachable);
    }

    [Fact]
    public void Teaching_score_is_two_way()
    {
        // همان واژه‌ها با ترتیب دیگر ← کاملاً منطبق.
        Assert.Equal(1d, AssistantTeachingService.Score("ارسال به کیش دارید؟", "به کیش ارسال دارید"));
        // یک واژه از یک پرسش بلند ← منطبق نیست (تا هر پیامی «کیش» داشت جواب آموزشی نگیرد).
        Assert.True(AssistantTeachingService.Score("کیش", "هزینه ارسال به جزیره کیش چقدره") < AssistantTeachingService.MinScore);
        Assert.Equal(0d, AssistantTeachingService.Score("", "ارسال به کیش"));
    }

    // ── ۷. پیش‌نویس پاسخ پرسش کالا ─────────────────────────────

    private const string Hello = "سلام {FirstName} عزیز، ";

    private static QuestionProductFacts Facts(int stock = 5, string? warranty = null, int? months = null, params (string Key, string Value)[] features) =>
        new("پاوربانک آزمون", stock, warranty, months, features);

    [Fact]
    public void Draft_answers_stock_from_real_data()
    {
        var inStock = QuestionAnswerAdvisor.Build("موجوده؟", Facts(stock: 5), [], null);
        var first = Assert.Single(inStock);
        Assert.StartsWith(Hello, first.Body, StringComparison.Ordinal);
        Assert.Contains("موجود است", first.Body, StringComparison.Ordinal);

        var outOfStock = QuestionAnswerAdvisor.Build("موجوده؟", Facts(stock: 0), [], null);
        Assert.Contains("خبرم کن", Assert.Single(outOfStock).Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Draft_answers_warranty_only_when_known()
    {
        var known = QuestionAnswerAdvisor.Build("گارانتی داره؟", Facts(warranty: "گارانتی اصالت و سلامت فیزیکی", months: 18), [], null);
        var draft = Assert.Single(known);
        Assert.Contains("گارانتی اصالت", draft.Body, StringComparison.Ordinal);
        Assert.Contains("۱۸", draft.Body, StringComparison.Ordinal);

        // گارانتی ثبت نشده ← حدسی نمی‌زنیم.
        Assert.Empty(QuestionAnswerAdvisor.Build("گارانتی داره؟", Facts(), [], null));
    }

    [Fact]
    public void Draft_uses_matching_product_specs()
    {
        var drafts = QuestionAnswerAdvisor.Build("باتریش چند میلی آمپره؟", Facts(features: [("ظرفیت باتری", "10000"), ("رنگ", "مشکی")]), [], null);
        var spec = Assert.Single(drafts, d => d.Source == "مشخصات کالا");
        Assert.Contains("ظرفیت باتری", spec.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("مشکی", spec.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Draft_reuses_answer_of_similar_question_as_plain_text()
    {
        var drafts = QuestionAnswerAdvisor.Build("با آیفون سازگاره؟", Facts(),
            [("هزینه ارسال به کیش چقدره؟", "پنجاه هزار تومان"), ("با آیفون سازگاره؟", "<p>بله، کاملا سازگار است.</p><script>x</script>")], null);
        var similar = Assert.Single(drafts, d => d.Source == "پرسش مشابه");
        Assert.StartsWith(Hello, similar.Body, StringComparison.Ordinal);
        Assert.Contains("کاملا سازگار است", similar.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("<p>", similar.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", similar.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("پنجاه هزار", similar.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Draft_does_not_double_the_greeting_and_ignores_unrelated_questions()
    {
        var drafts = QuestionAnswerAdvisor.Build("با آیفون سازگاره؟", Facts(), [("با آیفون سازگاره؟", "سلام، بله سازگار است.")], null);
        Assert.Equal("سلام، بله سازگار است.", Assert.Single(drafts).Body);

        Assert.Empty(QuestionAnswerAdvisor.Build("با آیفون سازگاره؟", Facts(), [("هزینه ارسال به کیش چقدره؟", "پنجاه هزار تومان")], null));
    }

    [Fact]
    public void Draft_quotes_buyer_opinion_for_aspect_questions()
    {
        var reviews = AssistantReviews.Summarize([new ReviewInput("صدای این هدفون عالیه", 5, true), new ReviewInput("صداش خیلی خوبه و راضیم", 5, true)]);
        var drafts = QuestionAnswerAdvisor.Build("صداش خوبه؟", Facts(), [], reviews);
        var opinion = Assert.Single(drafts, d => d.Source == "نظر خریداران");
        Assert.StartsWith(Hello, opinion.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", opinion.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Draft_is_capped_and_empty_without_facts()
    {
        var many = QuestionAnswerAdvisor.Build("موجوده؟", Facts(features: [("موجودی انبار", "زیاد")]),
            [("موجوده؟", "بله موجود است")], AssistantReviews.Summarize([new ReviewInput("موجود بود و سریع رسید", 5, true)]));
        Assert.True(many.Count <= QuestionAnswerAdvisor.MaxSuggestions);
        Assert.Empty(QuestionAnswerAdvisor.Build("رنگ قرمزش کی میاد؟", Facts(), [], null));
    }

    // ── ۸. خبر ارزان شدن علاقه‌مندی‌ها ─────────────────────────

    [Theory]
    [InlineData(1_000_000, 950_000, 5, true)]     // دقیقاً ۵٪
    [InlineData(1_000_000, 900_000, 5, true)]     // بیشتر از ۵٪
    [InlineData(1_000_000, 960_000, 5, false)]    // فقط ۴٪
    [InlineData(1_000_000, 1_100_000, 5, false)]  // گران‌تر شده
    [InlineData(0, 500_000, 5, false)]            // قیمت مرجع خراب
    [InlineData(1_000_000, 0, 5, false)]          // قیمت فعلی خراب
    [InlineData(1_000_000, -5, 5, false)]
    [InlineData(1_000_000, 980_000, 1, false)]    // درصد کمتر از ۳ ← ۳٪
    [InlineData(1_000_000, 970_000, 1, true)]
    [InlineData(1_000_000, 600_000, 90, false)]   // درصد بیشتر از ۵۰ ← ۵۰٪
    [InlineData(1_000_000, 500_000, 90, true)]
    public void Favorite_price_drop_threshold(int watch, int final, int percent, bool expected) =>
        Assert.Equal(expected, FavoritePriceWatch.IsDrop(watch, final, percent));

    [Theory]
    [InlineData(1, 3)]
    [InlineData(3, 3)]
    [InlineData(10, 10)]
    [InlineData(50, 50)]
    [InlineData(99, 50)]
    [InlineData(-7, 3)]
    public void Smart_options_clamp_favorite_drop_percent(int input, int expected)
    {
        var options = new SmartOptions { FavoritePriceDropPercent = input }.Normalize();
        Assert.Equal(expected, options.FavoritePriceDropPercent);
    }

    [Fact]
    public void Favorite_alert_defaults_are_gentle()
    {
        var options = new SmartOptions();
        Assert.True(options.FavoritePriceAlert);
        Assert.Equal(5, options.FavoritePriceDropPercent);
        Assert.Equal(TimeSpan.FromDays(7), FavoritePriceWatch.PerFavoriteGap);
        Assert.Equal(2, FavoritePriceWatch.PerUserDaily);
    }
}
