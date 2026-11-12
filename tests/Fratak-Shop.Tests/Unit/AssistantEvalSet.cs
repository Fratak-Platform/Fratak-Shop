using Fratak_Shop.Core.Services.Assistant;
using Fratak_Shop.Data.Entities.Assistant;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// دور ۲۵ – «مجموعهٔ سنجش» فهم دستیار: صدها پیام واقعی‌نما (رسمی، محاوره، غلط تایپی، فینگلیش، کیبورد اشتباه) با مسیر درست.
/// <para>
/// هدف این تست «همه درست» نیست (زبان آزاد است)، بلکه اندازه گرفتن کیفیت است: دقت کل باید دست‌کم ۸۵٪ باشد و هیچ دسته‌ای
/// زیر ۶۰٪ نیفتد. اگر کسی بعداً قانونی را عوض کند و فهم دستیار بدتر شود، این تست با فهرست پیام‌های اشتباه شکست می‌خورد.
/// </para>
/// <para>چند مورد «حساس» (لغو/مرجوعی/پرداخت) جدا و بدون تحمل خطا بررسی می‌شوند: کار اشتباه در این‌ها هزینهٔ واقعی دارد.</para>
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Assistant")]
public class AssistantEvalSet
{
    /// <summary>کمترین دقت قابل قبول برای کل مجموعه.</summary>
    private const double MinAccuracy = 0.85;

    /// <summary>کمترین دقت هر دسته (تا یک دسته به‌کلی خراب نشود و پشت دقت کل پنهان بماند).</summary>
    private const double MinCategoryAccuracy = 0.6;

    private static readonly (string Text, AssistantIntent Expected)[] Cases =
    [
        // ── سلام و تشکر ──
        ("سلام", AssistantIntent.Greeting),
        ("سلام وقت بخیر", AssistantIntent.Greeting),
        ("درود", AssistantIntent.Greeting),
        ("salam", AssistantIntent.Greeting),
        ("سلاااام", AssistantIntent.Greeting),
        ("صبح بخیر", AssistantIntent.Greeting),
        ("sghl", AssistantIntent.Greeting),
        ("ممنون", AssistantIntent.Thanks),
        ("مرسی", AssistantIntent.Thanks),
        ("خیلی ممنون", AssistantIntent.Thanks),
        ("دمت گرم", AssistantIntent.Thanks),
        ("سپاس", AssistantIntent.Thanks),
        ("دستت درد نکنه", AssistantIntent.Thanks),
        ("mamnoon merci", AssistantIntent.Thanks),

        // ── کارشناس ──
        ("میخوام با کارشناس صحبت کنم", AssistantIntent.Human),
        ("اپراتور", AssistantIntent.Human),
        ("شماره تماس پشتیبانی", AssistantIntent.Human),
        ("یه آدم واقعی میخوام", AssistantIntent.Human),
        ("پشتیبانی", AssistantIntent.Human),
        ("میخوام شکایت کنم", AssistantIntent.Human),
        ("تیکت بزنم", AssistantIntent.Human),
        ("وصلم کن به کارشناس", AssistantIntent.Human),

        // ── پیگیری ──
        ("سفارشم کجاست", AssistantIntent.Track),
        ("پیگیری سفارش", AssistantIntent.Track),
        ("کد رهگیری", AssistantIntent.Track),
        ("وضعیت سفارش", AssistantIntent.Track),
        ("سفارش FR-20261022-123456", AssistantIntent.Track),
        ("بستم کی میرسه", AssistantIntent.Track),
        ("سفارشهای من", AssistantIntent.Track),
        ("مرسوله من کجاست", AssistantIntent.Track),
        ("peygiri sefaresh", AssistantIntent.Track),

        // ── سبد و پرداخت ──
        ("سبد خریدم", AssistantIntent.CartShow),
        ("سبدم", AssistantIntent.CartShow),
        ("نمایش سبد", AssistantIntent.CartShow),
        ("چی تو سبدمه", AssistantIntent.CartShow),
        ("میخوام تسویه کنم", AssistantIntent.Checkout),
        ("ثبت سفارش", AssistantIntent.Checkout),
        ("پرداخت کنم", AssistantIntent.Checkout),
        ("تکمیل خرید", AssistantIntent.Checkout),
        ("سفارشمو نهایی کن", AssistantIntent.Checkout),

        // ── ورود ──
        ("میخوام وارد بشم", AssistantIntent.Login),
        ("با رمز عبور وارد بشم", AssistantIntent.Login),
        ("ثبت نام", AssistantIntent.Login),
        ("رمزمو فراموش کردم", AssistantIntent.Login),
        ("کد ورود نمیاد", AssistantIntent.Login),
        ("حساب کاربری بسازم", AssistantIntent.Login),

        // ── آدرس ──
        ("آدرسم رو عوض کنم", AssistantIntent.Address),
        ("آدرس جدید اضافه کنم", AssistantIntent.Address),
        ("نشانی تحویل", AssistantIntent.Address),

        // ── کالایاب، مقایسه، راهنما ──
        ("کمکم کن انتخاب کنم", AssistantIntent.Finder),
        ("نمیدونم چی بخرم", AssistantIntent.Finder),
        ("مشاوره خرید میخوام", AssistantIntent.Finder),
        ("مقایسه ایرپاد ۳ با گلکسی بادز", AssistantIntent.Compare),
        ("فرق این دوتا چیه", AssistantIntent.Compare),
        ("تفاوت شیائومی و سامسونگ", AssistantIntent.Compare),
        ("راهنما", AssistantIntent.Help),
        ("چیکار میتونی بکنی", AssistantIntent.Help),
        ("تو کی هستی", AssistantIntent.Help),
        ("کمک", AssistantIntent.Help),

        // ── تخفیف ──
        ("کد تخفیف دارید؟", AssistantIntent.Offers),
        ("تخفیفات امروز چیه", AssistantIntent.Offers),
        ("جشنواره دارید؟", AssistantIntent.Offers),
        ("امتیاز باشگاه من چقدره", AssistantIntent.Offers),
        ("ارسال رایگان دارید", AssistantIntent.Offers),

        // ── بی‌ربط ──
        ("هوا فردا چطوره", AssistantIntent.OffTopic),
        ("یه جوک بگو", AssistantIntent.OffTopic),
        ("فوتبال دیشب رو دیدی", AssistantIntent.OffTopic),
        ("قیمت دلار چنده", AssistantIntent.OffTopic),
        ("یه شعر بگو", AssistantIntent.OffTopic),

        // ── جست‌وجوی کالا ──
        ("پاوربانک شیائومی", AssistantIntent.Search),
        ("گوشی هواوی", AssistantIntent.Search),
        ("شارژر سامسونگ", AssistantIntent.Search),
        ("هندزفری بلوتوثی", AssistantIntent.Search),
        ("ساعت هوشمند", AssistantIntent.Search),
        ("کابل تایپ سی", AssistantIntent.Search),
        ("پاوربانک ۲۰ هزار", AssistantIntent.Search),
        ("هدفون سونی", AssistantIntent.Search),
        ("اسپیکر بلوتوثی", AssistantIntent.Search),
        ("قاب گوشی آیفون", AssistantIntent.Search),
        ("گلس سامسونگ", AssistantIntent.Search),
        ("پاور بانک انکر", AssistantIntent.Search),
        ("هنزفری", AssistantIntent.Search),
        ("مموری ۶۴ گیگ", AssistantIntent.Search),
        ("ماوس بی سیم", AssistantIntent.Search),
        ("powerbank xiaomi", AssistantIntent.Search),

        // ── موجودی و پرسش کالا ──
        ("تلفن ثابت پاناسونیک دارید", AssistantIntent.ProductQuestion),
        ("گارانتی داره؟", AssistantIntent.ProductQuestion),
        ("باتریش چقدر دوام داره", AssistantIntent.ProductQuestion),
        ("وزنش چقدره", AssistantIntent.ProductQuestion),
        ("اصله؟", AssistantIntent.ProductQuestion),
        ("رنگ مشکی هم داره؟", AssistantIntent.ProductQuestion),
        ("هزینه ارسال چقدره", AssistantIntent.ProductQuestion),
        ("شرایط مرجوعی چیه", AssistantIntent.ProductQuestion),
        ("روشهای پرداخت چیه", AssistantIntent.ProductQuestion),
        ("قیمتش چنده", AssistantIntent.ProductQuestion),
        ("موجوده؟", AssistantIntent.ProductQuestion),
        ("مشخصاتش چیه", AssistantIntent.ProductQuestion),
        ("در مورد این کالا توضیح بده", AssistantIntent.ProductQuestion),
        ("کیفیتش چطوره؟", AssistantIntent.ProductQuestion),
        ("نظر خریداران چیه", AssistantIntent.ProductQuestion),
        ("صداش خوبه؟", AssistantIntent.ProductQuestion),
        ("چند روزه میرسه", AssistantIntent.ProductQuestion),
        ("پرداخت در محل دارید", AssistantIntent.ProductQuestion),
        ("ابعادش چقدره", AssistantIntent.ProductQuestion),

        // ── مشاور خرید (دور ۲۵) ──
        ("هدفون زیر ۲ میلیون", AssistantIntent.Advisor),
        ("گوشی بین ۵ تا ۸ میلیون", AssistantIntent.Advisor),
        ("ارزونترین پاوربانک", AssistantIntent.Advisor),
        ("ارزون‌ترین پاوربانک", AssistantIntent.Advisor),
        ("بهترین هندزفری برای ورزش", AssistantIntent.Advisor),
        ("ساعت هوشمند حدود ۳ میلیون", AssistantIntent.Advisor),
        ("پرفروشترین شارژرها", AssistantIntent.Advisor),
        ("پاوربانک برای سفر", AssistantIntent.Advisor),
        ("هدیه تولد زیر یک میلیون", AssistantIntent.Advisor),
        ("قیمت پاوربانک شیائومی زیر یک میلیون", AssistantIntent.Advisor),
        ("هدست برای بازی", AssistantIntent.Advisor),
        ("هدفن بلوتوثی زیر ۲ ملیون", AssistantIntent.Advisor),
        ("گوشی تا ۱۰ میلیون تومان", AssistantIntent.Advisor),
        ("اسپیکر ارزون", AssistantIntent.Advisor),
        ("جدیدترین ساعت هوشمند", AssistantIntent.Advisor),
        ("کالاهای پرفروش", AssistantIntent.Advisor),
        ("هندزفری بالای ۵۰۰ هزار تومان", AssistantIntent.Advisor),
        ("هدفون ۸۰۰ تومنی", AssistantIntent.Advisor),
        ("gooshi zire 5 million", AssistantIntent.Advisor),

        // ── کارهای بعد از خرید (دور ۲۵) ──
        ("سفارشمو کنسل کن", AssistantIntent.OrderCancel),
        ("میخوام سفارشم رو لغو کنم", AssistantIntent.OrderCancel),
        ("سفارش FR-20261022-123456 رو لغو کن", AssistantIntent.OrderCancel),
        ("خریدمو کنسل کنید", AssistantIntent.OrderCancel),
        ("از خریدم منصرف شدم", AssistantIntent.OrderCancel),
        ("میخوام مرجوع کنم", AssistantIntent.OrderReturn),
        ("میخوام خریدمو پس بدم", AssistantIntent.OrderReturn),
        ("درخواست مرجوعی دارم", AssistantIntent.OrderReturn),
        ("کالام خراب بود میخوام برگردونم", AssistantIntent.OrderReturn),
        ("سفارشم رو مرجوع کنم", AssistantIntent.OrderReturn),
        ("فاکتور سفارشم", AssistantIntent.Invoice),
        ("فاکتور میخوام", AssistantIntent.Invoice),
        ("صورتحساب خریدم رو میخوام", AssistantIntent.Invoice),
        ("فاکتور رسمی سفارش", AssistantIntent.Invoice),

        // ── تکمیلی: جست‌وجو، پرسش و مشاوره با نوشتار متفاوت ──
        ("لپ تاپ ایسوس", AssistantIntent.Search),
        ("کیبورد مکانیکال", AssistantIntent.Search),
        ("فلش ۳۲ گیگ", AssistantIntent.Search),
        ("مچبند هوشمند شیائومی", AssistantIntent.Search),
        ("دوربین مداربسته", AssistantIntent.Search),
        ("آداپتور شارژر لپ تاپ", AssistantIntent.Search),
        ("رینگ لایت", AssistantIntent.Search),
        ("هارد اکسترنال", AssistantIntent.Search),
        ("گارانتیش چند ماهه؟", AssistantIntent.ProductQuestion),
        ("جنسش چیه؟", AssistantIntent.ProductQuestion),
        ("با آیفون سازگاره؟", AssistantIntent.ProductQuestion),
        ("اورجیناله؟", AssistantIntent.ProductQuestion),
        ("دوربینش چند مگاپیکسله؟", AssistantIntent.ProductQuestion),
        ("حافظش چقدره؟", AssistantIntent.ProductQuestion),
        ("هزینه ارسال به شیراز چقدره", AssistantIntent.ProductQuestion),
        ("قسطی هم میفروشید", AssistantIntent.ProductQuestion),
        ("ساعت کاری فروشگاه", AssistantIntent.ProductQuestion),
        ("ارزانترین هدفون", AssistantIntent.Advisor),
        ("گوشی خوب زیر ۱۵ میلیون", AssistantIntent.Advisor),
        ("هدفون مناسب برای کلاس آنلاین", AssistantIntent.Advisor),
        ("پرتخفیف‌ترین هدفون‌ها", AssistantIntent.Advisor),
        ("شارژر فندکی برای ماشین", AssistantIntent.Advisor),
        ("ساعت هوشمند ضدآب برای شنا", AssistantIntent.Advisor),
        ("وضعیت سفارش FR-20261022-654321", AssistantIntent.Track),
        ("سفارش هام کجاست", AssistantIntent.Track),
        ("فاکتور خریدم رو بده", AssistantIntent.Invoice),
        ("سفارش FR-20261022-111111 رو مرجوع کنم", AssistantIntent.OrderReturn),
        ("سفارشم رو کنسل کنید لطفا", AssistantIntent.OrderCancel),

        // ── سلیقهٔ گفت‌وگو ──
        ("منو علی صدا کن", AssistantIntent.Preference),
        ("منو نگار صدا کن", AssistantIntent.Preference),
    ];

    /// <summary>دسته‌بندی برای گزارش (هر مسیر یک دسته).</summary>
    private static string Category(AssistantIntent intent) => intent.ToString();

    [Fact]
    public void Understanding_accuracy_meets_the_bar()
    {
        var failures = new List<string>();
        var perCategory = new Dictionary<string, (int Ok, int All)>(StringComparer.Ordinal);
        foreach (var (text, expected) in Cases)
        {
            var actual = AssistantUnderstanding.Understand(text).Route;
            var key = Category(expected);
            var (ok, all) = perCategory.GetValueOrDefault(key);
            var correct = actual == expected;
            perCategory[key] = (ok + (correct ? 1 : 0), all + 1);
            if (!correct)
                failures.Add($"«{text}» ← {actual} (درست: {expected})");
        }

        var accuracy = 1 - failures.Count / (double)Cases.Length;
        var weak = perCategory.Where(c => c.Value.Ok / (double)c.Value.All < MinCategoryAccuracy)
            .Select(c => $"{c.Key}: {c.Value.Ok}/{c.Value.All}").ToList();
        var report = $"دقت {accuracy:P1} از {Cases.Length} پیام. دسته‌های ضعیف: {string.Join("، ", weak)}\nاشتباه‌ها:\n" + string.Join("\n", failures);

        Assert.True(accuracy >= MinAccuracy, report);
        Assert.True(weak.Count == 0, report);
    }

    [Fact]
    public void Eval_set_is_large_and_covers_every_new_intent()
    {
        Assert.True(Cases.Length >= 150);
        foreach (var intent in new[] { AssistantIntent.Advisor, AssistantIntent.OrderCancel, AssistantIntent.OrderReturn, AssistantIntent.Invoice })
            Assert.True(Cases.Count(c => c.Expected == intent) >= 4, intent.ToString());
        // هیچ پیام تکراری (تا دقت با تکرار باد نکند).
        Assert.Equal(Cases.Length, Cases.Select(c => c.Text).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// پیام‌هایی که اشتباه در آن‌ها هزینه دارد: هرگز نباید به لغو/مرجوعی برسند (بدون تحمل خطا).
    /// </summary>
    [Theory]
    [InlineData("کنسل شد؟")]
    [InlineData("سفارشم لغو شده؟")]
    [InlineData("چرا سفارشم کنسل شد")]
    [InlineData("شرایط مرجوعی چیه")]
    [InlineData("مهلت مرجوعی چند روزه")]
    [InlineData("قوانین مرجوعی")]
    [InlineData("پاوربانک شیائومی")]
    [InlineData("سلام")]
    [InlineData("فاکتور")]
    public void Sensitive_actions_are_never_triggered_by_mistake(string text)
    {
        var route = AssistantUnderstanding.Understand(text).Route;
        Assert.NotEqual(AssistantIntent.OrderCancel, route);
        Assert.NotEqual(AssistantIntent.OrderReturn, route);
    }
}
