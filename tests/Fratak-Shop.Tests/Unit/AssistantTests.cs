using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Services.Assistant;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Entities.Assistant;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Web.Infrastructure.Assistant;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های واحد دور ۲۳ برای «دستیار فروشگاه» (چت‌بات بدون هوش مصنوعی بیرونی).
/// <list type="bullet">
/// <item>فهم پیام (<see cref="AssistantNlu"/>): سلام، تشکر، سبد، افزودن با ترتیب/تعداد، پیگیری با شمارهٔ سفارش، پرسش موضوعی، بی‌ربط، جست‌وجو.</item>
/// <item>«پشتیبانی می‌کنه» پرسش سازگاری است نه درخواست پشتیبان؛ «هواوی» بی‌ربط نیست («هوا» فقط واژهٔ کامل).</item>
/// <item>پوشاندن موبایل/کارت/ایمیل/کد ملی پیش از ذخیره؛ شمارهٔ سفارش دست نمی‌خورد.</item>
/// <item>جواب اینترنتی (<see cref="AssistantWeb"/>): فقط نتیجه‌ای که دقیقاً همین مدل است؛ ۲ منبع ← مطمئن، ۱ منبع ← «مطمئن نیستم ولی…».</item>
/// <item>امنیت ورودی: شناسهٔ گفت‌وگو فقط حرف و رقم، لینک دکمه‌ها فقط مسیر داخلی، توکن ناشناخته.</item>
/// <item>جست‌وجوی پس‌زمینه هرگز منتظر نمی‌ماند، جواب حافظه می‌شود و خطای منبع به «Busy» ختم می‌شود.</item>
/// </list>
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Assistant")]
public class AssistantTests
{
    // ── فهم پیام: بهترین حالت‌ها ─────────────────────────────────

    [Theory]
    [InlineData("سلام", AssistantIntent.Greeting)]
    [InlineData("سلام وقت بخیر", AssistantIntent.Greeting)]
    [InlineData("ممنون", AssistantIntent.Thanks)]
    [InlineData("سبد خریدم", AssistantIntent.CartShow)]
    [InlineData("تسویه حساب", AssistantIntent.Checkout)]
    [InlineData("حذفش کن", AssistantIntent.CartRemove)]
    [InlineData("پشتیبانی", AssistantIntent.Human)]
    [InlineData("هوا فردا چطوره", AssistantIntent.OffTopic)]
    [InlineData("پاوربانک شیائومی", AssistantIntent.Search)]
    [InlineData("گوشی هواوی", AssistantIntent.Search)]
    [InlineData("", AssistantIntent.Help)]
    public void Parse_detects_intent(string text, AssistantIntent expected)
    {
        Assert.Equal(expected, AssistantNlu.Parse(text).Intent);
    }

    [Fact]
    public void Parse_reads_ordinal_and_quantity_for_cart_add()
    {
        var second = AssistantNlu.Parse("دومی رو اضافه کن");
        Assert.Equal(AssistantIntent.CartAdd, second.Intent);
        Assert.Equal(2, second.Ordinal);
        Assert.Equal(1, second.Quantity);

        var two = AssistantNlu.Parse("۲ تا از این بذار تو سبد");
        Assert.Equal(AssistantIntent.CartAdd, two.Intent);
        Assert.Equal(2, two.Quantity);
        Assert.Equal(string.Empty, two.Query);
    }

    [Fact]
    public void Parse_caps_quantity_to_twenty()
    {
        Assert.Equal(20, AssistantNlu.Parse("99 تا اضافه کن").Quantity);
    }

    [Fact]
    public void Parse_extracts_order_number_for_tracking()
    {
        var parse = AssistantNlu.Parse("سفارش FR-20261022-123456 کجاست");
        Assert.Equal(AssistantIntent.Track, parse.Intent);
        Assert.Equal("FR-20261022-123456", parse.OrderNumber);
    }

    [Fact]
    public void Parse_picks_longest_topic_and_keeps_product_words_in_query()
    {
        var battery = AssistantNlu.Parse("باتری این چقدر دوام داره");
        Assert.Equal(AssistantIntent.ProductQuestion, battery.Intent);
        Assert.Equal("battery", battery.Topic);

        var price = AssistantNlu.Parse("قیمت هدفون سونی چنده؟");
        Assert.Equal(AssistantIntent.ProductQuestion, price.Intent);
        Assert.Equal("price", price.Topic);
        Assert.Equal("هدفون سونی", price.Query);
        Assert.True(price.ShopCue);

        var shipping = AssistantNlu.Parse("هزینه ارسال چقدره");
        Assert.Equal("shipping", shipping.Topic);
        Assert.True(AssistantNlu.Topic("shipping")!.StoreLevel);
    }

    [Fact]
    public void Parse_treats_support_verb_as_compatibility_not_human()
    {
        var parse = AssistantNlu.Parse("این هدفون از بلوتوث ۵ پشتیبانی میکنه؟");
        Assert.Equal(AssistantIntent.ProductQuestion, parse.Intent);
        Assert.Equal("compat", parse.Topic);
    }

    [Fact]
    public void Parse_asks_about_stock_and_keeps_product_name()
    {
        var parse = AssistantNlu.Parse("تلفن ثابت پاناسونیک دارید");
        Assert.Equal(AssistantIntent.ProductQuestion, parse.Intent);
        Assert.Equal("stock", parse.Topic);
        Assert.Equal("تلفن ثابت پاناسونیک", parse.Query);
    }

    [Fact]
    public void Parse_feature_word_without_question_is_a_search()
    {
        var charger = AssistantNlu.Parse("شارژر سامسونگ");
        Assert.Equal(AssistantIntent.Search, charger.Intent);
        Assert.Equal("شارژر سامسونگ", charger.Query);

        var cable = AssistantNlu.Parse("کابل شارژ دارید");
        Assert.Equal(AssistantIntent.ProductQuestion, cable.Intent);
        Assert.Equal("stock", cable.Topic);
        Assert.Equal("کابل شارژ", cable.Query);

        var weight = AssistantNlu.Parse("وزنش چقدره");
        Assert.Equal("weight", weight.Topic);
    }

    [Fact]
    public void Parse_does_not_mistake_product_names_for_commands()
    {
        // «پاک‌کننده» فرمان «پاک کن» نیست.
        var cleaner = AssistantNlu.Parse("پاک کننده شیشه");
        Assert.Equal(AssistantIntent.Search, cleaner.Intent);
        Assert.Equal("پاک کننده شیشه", cleaner.Query);
        Assert.Equal(AssistantIntent.Search, AssistantNlu.Parse("کالاهای پرفروش").Intent);
        Assert.Equal(AssistantIntent.Track, AssistantNlu.Parse("سفارشم کجاست").Intent);
    }

    // ── فهم پیام: بدترین حالت‌ها و حمله ─────────────────────────

    [Fact]
    public void Parse_cuts_long_input_and_survives_garbage()
    {
        var parse = AssistantNlu.Parse(new string('ب', 5000));
        Assert.True(parse.Normalized.Length <= AssistantNlu.MaxLength);
        Assert.Equal(AssistantIntent.Help, AssistantNlu.Parse("!!!???<>").Intent);
        Assert.Equal(AssistantIntent.Help, AssistantNlu.Parse(null).Intent);
    }

    [Fact]
    public void Parse_strips_markup_from_query()
    {
        var parse = AssistantNlu.Parse("<script>alert(1)</script> هدفون");
        Assert.DoesNotContain("<", parse.Query);
        Assert.DoesNotContain(">", parse.Query);
        Assert.Contains("هدفون", parse.Query);
    }

    [Fact]
    public void Mask_hides_personal_data_but_keeps_order_number()
    {
        Assert.Equal("شمارم *** است", AssistantNlu.Mask("شمارم ۰۹۱۲۱۲۳۴۵۶۷ است"));
        Assert.Equal("کارت ***", AssistantNlu.Mask("کارت 6037 9911 2233 4455"));
        Assert.Equal("ایمیل ***", AssistantNlu.Mask("ایمیل a.b@example.com"));
        Assert.Equal("کد ***", AssistantNlu.Mask("کد 0012345678"));
        Assert.Equal("سفارش FR-20261022-123456", AssistantNlu.Mask("سفارش FR-20261022-123456"));
        Assert.Equal(string.Empty, AssistantNlu.Mask(null));
    }

    [Fact]
    public void Similarity_matches_faq_variants_and_rejects_unrelated()
    {
        Assert.True(AssistantNlu.Similarity("هزینه ارسال چقدره", "هزینهٔ ارسال چقدر است؟") >= 0.99);
        Assert.Equal(0d, AssistantNlu.Similarity("پاوربانک", "هزینه ارسال"));
        Assert.Equal(0d, AssistantNlu.Similarity("", "هزینه ارسال"));
    }

    [Fact]
    public void CompareParts_splits_two_products()
    {
        var parts = AssistantNlu.CompareParts("مقایسه ایرپاد ۳ با گلکسی بادز");
        Assert.NotNull(parts);
        Assert.Equal("ایرپاد 3", parts.Value.First);
        Assert.Equal("گلکسی بادز", parts.Value.Second);
        Assert.Null(AssistantNlu.CompareParts("مقایسه ایرپاد"));
    }

    // ── جواب اینترنتی ───────────────────────────────────────────

    private static readonly string[] BatteryWords = ["باتری", "ظرفیت"];

    [Fact]
    public void Web_answer_is_confident_only_with_two_matching_sources()
    {
        var results = new List<SerpItem>
        {
            new("https://www.a.com/x", "پاوربانک شیائومی 20000 مدل PB2022", "ظرفیت باتری این پاوربانک 20000 میلی‌آمپر ساعت است. وزن 400 گرم."),
            new("https://www.a.com/y", "پاوربانک شیائومی 20000", "ظرفیت باتری 20000 میلی آمپر."),
            new("https://b.ir/y", "مشخصات پاوربانک شیائومی 20000", "این پاوربانک با ظرفیت 20000 میلی آمپر عرضه شده است."),
            new("https://c.ir/z", "پاوربانک انکر 10000", "ظرفیت باتری 10000 میلی آمپر."),
        };
        var answer = AssistantWeb.Evaluate("پاوربانک شیائومی 20000", "باتری", BatteryWords, results);
        Assert.True(answer.Confident);
        Assert.Equal(2, answer.Sources);
        Assert.Equal(new[] { "a.com", "b.ir" }, answer.Hosts);
        Assert.StartsWith("طبق چند منبع", answer.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("10000", answer.Text);
    }

    [Fact]
    public void Web_answer_with_one_source_says_not_sure()
    {
        var results = new List<SerpItem> { new("https://b.ir/y", "پاوربانک شیائومی 20000", "ظرفیت باتری 20000 میلی آمپر است.") };
        var answer = AssistantWeb.Evaluate("پاوربانک شیائومی 20000", "باتری", BatteryWords, results);
        Assert.False(answer.Confident);
        Assert.StartsWith("مطمئن نیستم ولی", answer.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Web_answer_rejects_other_models_and_unsafe_urls()
    {
        var results = new List<SerpItem>
        {
            new("https://c.ir/z", "پاوربانک شیائومی 10000", "ظرفیت باتری 10000 میلی آمپر."),
            new("javascript:alert(1)", "پاوربانک شیائومی 20000", "ظرفیت باتری 20000."),
            new("https://d.ir/q", "پاوربانک شیائومی 20000", "رنگ مشکی و بدنه پلاستیکی."),
        };
        var answer = AssistantWeb.Evaluate("پاوربانک شیائومی 20000", "باتری", BatteryWords, results);
        Assert.Equal(0, answer.Sources);
        Assert.Contains("جواب مطمئنی پیدا نکردم", answer.Text);
        Assert.Empty(AssistantWeb.Evaluate("پاوربانک", "باتری", BatteryWords, []).Hosts);
    }

    [Theory]
    [InlineData("https://www.digikala.com/p/1", "digikala.com")]
    [InlineData("http://shop.example.ir/x", "shop.example.ir")]
    [InlineData("ftp://x.com/a", null)]
    [InlineData("/local", null)]
    [InlineData(null, null)]
    public void Web_host_only_for_http_urls(string? url, string? expected)
    {
        Assert.Equal(expected, AssistantWeb.Host(url));
    }

    // ── متن‌های پاسخ ─────────────────────────────────────────────

    private static ProductFilterData Product(int stock, decimal price = 1_500_000m, decimal? final = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "هدفون آزمایشی",
        Slug = "test-headphone",
        SKU = "T-1",
        Price = price,
        FinalPrice = final ?? price,
        HasActiveDiscount = final is not null,
        StockQuantity = stock,
        IsActive = true,
    };

    [Fact]
    public void Price_and_stock_texts_use_real_numbers()
    {
        var discounted = AssistantService.PriceText(Product(10, 1_500_000m, 1_200_000m));
        Assert.Contains("تخفیف", discounted);
        Assert.Contains(PersianText.Money(1_200_000m), discounted);
        Assert.Contains(PersianText.Money(1_500_000m), discounted);
        Assert.DoesNotContain("تخفیف", AssistantService.PriceText(Product(10)));

        Assert.Contains("ناموجود", AssistantService.StockText(Product(0)));
        Assert.Contains(PersianText.Digits(3), AssistantService.StockText(Product(3)));
        Assert.Equal("«هدفون آزمایشی» موجود است.", AssistantService.StockText(Product(50)));
    }

    [Fact]
    public void Card_links_are_internal()
    {
        var card = AssistantService.ToCard(Product(2));
        Assert.Equal("/product/test-headphone", card.Url);
        Assert.Null(card.ImageUrl);
        Assert.True(card.InStock);
    }

    [Theory]
    [InlineData(OrderStatus.Pending, PaymentStatus.Pending, "در انتظار پرداخت")]
    [InlineData(OrderStatus.Pending, PaymentStatus.Paid, "ثبت شده")]
    [InlineData(OrderStatus.Processing, PaymentStatus.Paid, "در حال آماده‌سازی")]
    [InlineData(OrderStatus.Shipped, PaymentStatus.Paid, "ارسال شده")]
    [InlineData(OrderStatus.Cancelled, PaymentStatus.Refunded, "لغو شده")]
    public void Status_text_is_customer_friendly(OrderStatus status, PaymentStatus payment, string expected)
    {
        Assert.Equal(expected, AssistantService.StatusText(status, payment));
    }

    // ── امنیت ورودی ─────────────────────────────────────────────

    [Theory]
    [InlineData("a1b2c3d4e5", true)]
    [InlineData("abcdefabcdefabcdefabcdef", true)]
    [InlineData("short", false)]
    [InlineData("<script>alert(1)</script>", false)]
    [InlineData("abc def ghi", false)]
    [InlineData(null, false)]
    public void Session_accepts_only_alphanumeric_ids(string? value, bool kept)
    {
        var session = AssistantEndpoints.Session(value);
        Assert.Equal(kept, session == value);
        Assert.Matches("^[A-Za-z0-9]{8,32}$", session);
    }

    [Theory]
    [InlineData("/cart", "/cart")]
    [InlineData("/checkout?addressId=1", "/checkout?addressId=1")]
    [InlineData("//evil.com", null)]
    [InlineData("/\\evil.com", null)]
    [InlineData("https://evil.com", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData(null, null)]
    public void SafeUrl_allows_only_internal_paths(string? url, string? expected)
    {
        Assert.Equal(expected, AssistantEndpoints.SafeUrl(url));
    }

    // ── جست‌وجوی پس‌زمینه ───────────────────────────────────────

    private sealed class FakeWeb(Func<WebResult<IReadOnlyList<SerpItem>>> result) : ISeoWebSource
    {
        public int Calls;

        public Task<WebResult<IReadOnlyList<string>>> SuggestAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult(WebResult<IReadOnlyList<string>>.Fail("off"));

        public Task<WebResult<IReadOnlyList<SerpItem>>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(result());
        }

        public Task<WebResult<string>> FetchPageAsync(string url, CancellationToken cancellationToken) =>
            Task.FromResult(WebResult<string>.Fail("off"));
    }

    private static async Task<(bool Known, bool Done, string? Text)> WaitAsync(AssistantLookup lookup, string token)
    {
        for (var i = 0; i < 100; i++)
        {
            var state = lookup.Poll(token);
            if (state.Done)
                return state;
            await Task.Delay(50);
        }
        return lookup.Poll(token);
    }

    [Fact]
    public async Task Lookup_answers_in_background_and_caches_result()
    {
        var web = new FakeWeb(() => WebResult<IReadOnlyList<SerpItem>>.Success(
        [
            new SerpItem("https://a.com/1", "پاوربانک شیائومی 20000", "ظرفیت باتری 20000 میلی آمپر است."),
            new SerpItem("https://b.com/1", "پاوربانک شیائومی 20000", "ظرفیت 20000 میلی آمپر."),
        ], "test"));
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var lookup = new AssistantLookup(web, cache, NullLogger<AssistantLookup>.Instance);
        var need = new AssistantLookupNeed(Guid.NewGuid(), "پاوربانک شیائومی 20000", "باتری", BatteryWords);

        var token = lookup.Start(need);
        Assert.Matches("^[a-f0-9]{32}$", token);
        var state = await WaitAsync(lookup, token);
        Assert.True(state.Known);
        Assert.True(state.Done);
        Assert.StartsWith("طبق چند منبع", state.Text, StringComparison.Ordinal);

        var again = lookup.Start(need);
        Assert.True(lookup.Poll(again).Done);
        Assert.Equal(1, web.Calls);
        Assert.Equal($"assistant:web:{need.ProductId:N}:باتری", AssistantLookup.CacheKey(need));
    }

    [Fact]
    public async Task Lookup_failures_end_with_busy_text_and_unknown_tokens_are_safe()
    {
        var web = new FakeWeb(() => throw new HttpRequestException("down"));
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var lookup = new AssistantLookup(web, cache, NullLogger<AssistantLookup>.Instance);
        var token = lookup.Start(new AssistantLookupNeed(Guid.NewGuid(), "کالای آزمایشی", "باتری", BatteryWords));
        var state = await WaitAsync(lookup, token);
        Assert.True(state.Done);
        Assert.Equal(AssistantLookup.Busy, state.Text);

        var unknown = lookup.Poll(Guid.NewGuid().ToString("N"));
        Assert.False(unknown.Known);
        Assert.True(unknown.Done);
        Assert.Null(unknown.Text);
    }
}
