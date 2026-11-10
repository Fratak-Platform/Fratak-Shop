using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Entities.Assistant;

namespace Fratak_Shop.Core.Services.Assistant;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۳ – «دستیار فروشگاه» (چت‌بات بدون هوش مصنوعی بیرونی)
//  این فایل فقط «فهمیدن پیام» است: قاعده و واژه‌نامهٔ فارسی، بدون پایگاه داده و بدون اینترنت (کاملاً تست‌پذیر).
//   • AssistantNlu.Parse : پیام ← نیت (جست‌وجو، پرسش دربارهٔ کالا، سبد، تسویه، پیگیری، …) + عبارت جست‌وجو + موضوع + تعداد + «اولی/دومی».
//   • AssistantNlu.Mask  : پوشاندن موبایل، کارت بانکی، ایمیل و کد ملی پیش از ذخیرهٔ پیام (حریم خصوصی).
//  محل استفاده: AssistantService (همین پوشه) و تست‌های Unit/AssistantTests.cs.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// نتیجهٔ فهم یک پیام. مثال: «قیمت هدفون سونی چنده؟» ← Intent = ProductQuestion، Topic = price، Query = «هدفون سونی».
/// </summary>
/// <param name="Intent">نیت پیام.</param>
/// <param name="Normalized">متن نرمال‌شده (ی/ک فارسی، رقم انگلیسی، حروف کوچک).</param>
/// <param name="Query">باقی‌ماندهٔ معنادار پیام برای جست‌وجوی کالا (بدون واژه‌های پرکننده؛ ممکن است خالی باشد).</param>
/// <param name="Topic">کلید موضوع پرسش (مثلاً price، battery، shipping) یا null.</param>
/// <param name="Quantity">تعداد درخواستی برای افزودن به سبد (۱ تا ۲۰؛ پیش‌فرض ۱).</param>
/// <param name="Ordinal">«اولی/دومی/…» (۱ تا ۶) برای اشاره به کارت‌های نمایش‌داده‌شده، یا null.</param>
/// <param name="OrderNumber">شمارهٔ سفارش داخل پیام (مثل FR-20261022-123456) یا null.</param>
/// <param name="ShopCue">پیام نشانهٔ خرید دارد؟ («دارید»، «می‌خوام»، «قیمت»…) ← اگر کالا پیدا نشد «درخواست کالا» ثبت می‌شود.</param>
public sealed record AssistantParse(AssistantIntent Intent, string Normalized, string Query, string? Topic, int Quantity, int? Ordinal, string? OrderNumber, bool ShopCue);

/// <summary>
/// موضوع یک پرسش. مثال: باتری ← واژه‌های «باتری/شارژ» در پیام و کلیدهای مشخصات «باتری/شارژ» در کالا.
/// </summary>
/// <param name="Key">کلید انگلیسی (برای گروه‌بندی در پنل).</param>
/// <param name="Title">عنوان فارسی (در پنل و پاسخ).</param>
/// <param name="Words">واژه‌هایی که در پیام این موضوع را نشان می‌دهند (واژهٔ ≤۳ نویسه فقط به‌صورت کامل).</param>
/// <param name="FeatureKeys">بخشی از نام مشخصهٔ کالا که جواب در آن است (خالی = جواب از قیمت/موجودی/تنظیمات فروشگاه).</param>
/// <param name="StoreLevel">جواب از تنظیمات فروشگاه است، نه از کالا (ارسال، مرجوعی، پرداخت، تماس).</param>
public sealed record AssistantTopic(string Key, string Title, string[] Words, string[] FeatureKeys, bool StoreLevel = false);

/// <summary>
/// فهم پیام مشتری با قاعده‌های ساده و قابل پیش‌بینی (بدون هوش مصنوعی). ترتیب بررسی مهم است:
/// سلام/تشکر کوتاه ← پشتیبان ← پیگیری ← تسویه ← حذف از سبد ← افزودن به سبد ← دیدن سبد ← آدرس ← مقایسه ← راهنما ← موضوع کالا ← بی‌ربط ← جست‌وجو.
/// </summary>
public static partial class AssistantNlu
{
    /// <summary>بیشترین طول پیام (بقیه بریده می‌شود).</summary>
    public const int MaxLength = 300;

    private static readonly string[] Greetings = ["سلام", "درود", "وقت بخیر", "روز بخیر", "صبح بخیر", "عصر بخیر", "شب بخیر", "hello", "hi", "salam"];
    private static readonly string[] GreetingFillers = ["خوبی", "خوبید", "خوبین", "چطوری", "چطورید", "خسته", "نباشید", "عزیز", "دوست", "وقت", "بخیر", "علیکم", "اقا", "آقا", "خانم", "ممنون"];
    private static readonly string[] ThanksWords = ["ممنون", "مرسی", "سپاس", "متشکر", "تشکر", "دستت درد نکنه", "دستتون درد نکنه", "دمت گرم", "thanks", "thank you"];
    private static readonly string[] HumanWords = ["اپراتور", "کارشناس", "شماره تماس", "شماره تلفن", "تلفن پشتیبانی", "آدم واقعی", "تیکت", "شکایت", "با پشتیبانی", "پشتیبان فروشگاه"];
    private static readonly string[] TrackWords = ["پیگیری", "سفارشم", "سفارش من", "سفارشام", "سفارشهام", "سفارش هام", "سفارشهای من", "سفارش های من", "کد رهگیری", "رهگیری", "مرسوله", "بستم", "بسته من", "وضعیت سفارش"];
    private static readonly string[] CheckoutWords = ["تسویه", "پرداخت کنم", "پرداختش کنم", "نهایی کن", "نهایی کنم", "نهایی کردن", "ثبت سفارش", "ثبتش کن", "سفارش بدم", "سفارش بده", "تموم کنم", "تکمیل خرید", "ادامه خرید", "checkout"];
    private static readonly string[] RemoveWords = ["حذف", "حذفش", "بردار", "برش دار", "پاک کن", "پاکش کن", "درش بیار", "نمیخوامش", "نمی خوامش"];
    private static readonly string[] AddWords = ["اضافه کن", "اضافش کن", "اضافه اش کن", "اضافه بشه", "اضافه کنید", "بذار تو سبد", "بزار تو سبد", "بریز تو سبد", "به سبد", "میخرمش", "می خرمش", "بخرمش", "همینو میخوام", "همین رو میخوام", "افزودن"];
    private static readonly string[] CartShowWords = ["سبد من", "سبدم", "سبد خریدم", "سبد خرید من", "نمایش سبد", "توی سبد", "تو سبد", "داخل سبد", "سبد خرید", "سبد"];
    private static readonly string[] AddressWords = ["آدرس", "ادرس", "آدرسم", "ادرسم", "آدرسهام", "نشانی"];
    private static readonly string[] CompareWords = ["مقایسه", "فرق", "تفاوت", "کدوم بهتره", "کدام بهتر", "بهتره یا"];
    private static readonly string[] HelpWords = ["راهنما", "کمک", "چیکار میتونی", "چه کار میتونی", "چکار میکنی", "چه کمکی", "help"];

    /// <summary>
    /// واژه‌های فرمان افزودن/حذف که نباید جزو نام کالا در «Query» بمانند (فقط این دو نیت از Query برای پیدا کردن کالا استفاده می‌کنند).
    /// مثال: «۲ تا از این بذار تو سبد» ← Query خالی (نه «بذار»)، تا کالای همین صفحه به سبد برود.
    /// واژه‌هایی که در نام کالا هم می‌آیند («پاک» در «پاک‌کننده»، «دار» در «کیف‌دار») حذف نمی‌شوند.
    /// </summary>
    private static readonly HashSet<string> ActionTokens = AddWords.Concat(RemoveWords).SelectMany(w => w.Split(' '))
        .Where(w => w.Length >= 2 && w is not ("پاک" or "دار" or "بیار" or "برش" or "درش" or "سبد"))
        .ToHashSet(StringComparer.Ordinal);

    private static readonly string[] ShopCues =
    [
        "دارید", "داری", "دارین", "موجود", "میخوام", "می خوام", "میخواستم", "می خواستم", "میخواهم", "می خواهم", "دنبال", "خرید", "بخرم",
        "قیمت", "چنده", "سفارش", "تهیه", "پیدا کن", "نشون بده", "نشان بده", "پیشنهاد", "لازم دارم", "میفروشید", "می فروشید",
    ];

    /// <summary>واژه‌های بی‌ربط به فروشگاه (فقط تطبیق کامل واژه؛ مثلاً «هوا» با «هواوی» یکی نیست).</summary>
    private static readonly HashSet<string> OffTopicWords = new(StringComparer.Ordinal)
    {
        "هوا", "فوتبال", "والیبال", "سیاست", "سیاسی", "انتخابات", "جوک", "لطیفه", "شعر", "ترجمه", "ریاضی", "تکلیف", "مشق", "فال", "طالع",
        "دلار", "بورس", "بیتکوین", "رمزارز", "دعا", "اخبار", "عاشق", "عشق", "اسمت", "سالته", "ساختت", "chatgpt", "gpt", "کنکور", "پزشک", "دارو", "بیماری",
    };

    private static readonly string[] OffTopicPhrases = ["آب و هوا", "برنامه نویسی", "کد بنویس", "بیت کوین", "ارز دیجیتال", "دوستم داری", "هوش مصنوعی", "اسمت چیه", "چند سالته"];

    /// <summary>واژه‌هایی که در جست‌وجوی کالا معنا ندارند و از «Query» حذف می‌شوند.</summary>
    private static readonly HashSet<string> Fillers = new(StringComparer.Ordinal)
    {
        "این", "اینو", "اینه", "اون", "اونو", "همین", "همینو", "کالا", "کالای", "محصول", "محصولی", "چیه", "چی", "چیست", "هست", "هستش", "است", "داره", "دارد", "دارن",
        "میشه", "میشود", "لطفا", "لطفاً", "بگو", "بگید", "بگین", "بفرمایید", "ببخشید", "یه", "یک", "من", "برای", "برام", "واسه", "واسم", "رو", "را", "که", "از",
        "به", "با", "در", "تو", "توی", "چطوره", "چطور", "خوبه", "آیا", "ایا", "هم", "و", "یا", "دارید", "داری", "دارین", "میخوام", "خوام", "میخواستم", "خواستم",
        "میخواهم", "خواهم", "می", "دنبال", "هستم", "میگردم", "گردم", "نشون", "نشان", "بده", "بدید", "بدین", "پیدا", "کن", "کنید", "کنین", "قیمت", "قیمتش",
        "چنده", "چند", "چقدر", "موجود", "موجوده", "موجودی", "تا", "عدد", "دونه", "دانه", "اولی", "دومی", "سومی", "چهارمی", "پنجمی", "اول", "دوم", "سوم",
        "شماره", "سلام", "درود", "ممنون", "مرسی", "خرید", "بخرم", "تهیه", "لازم", "دارم", "میفروشید", "فروشید", "جنس", "مدل", "یکی", "چندتا", "سبد", "؟",
        "خیلی", "خوب", "خوبی", "بهترین", "ارزان", "ارزون", "چیزی", "مناسب",
        "چقدره", "کجاست", "چیا", "هستن", "هستند", "داریم",
    };

    private static readonly Dictionary<string, int> OrdinalWords = new(StringComparer.Ordinal)
    {
        ["اولی"] = 1, ["اول"] = 1, ["اولین"] = 1, ["دومی"] = 2, ["دوم"] = 2, ["دومین"] = 2, ["سومی"] = 3, ["سوم"] = 3, ["سومین"] = 3,
        ["چهارمی"] = 4, ["چهارم"] = 4, ["پنجمی"] = 5, ["پنجم"] = 5, ["ششمی"] = 6, ["ششم"] = 6,
    };

    private static readonly Dictionary<string, int> NumberWords = new(StringComparer.Ordinal)
    {
        ["یک"] = 1, ["یه"] = 1, ["دو"] = 2, ["سه"] = 3, ["چهار"] = 4, ["پنج"] = 5, ["شش"] = 6, ["هفت"] = 7, ["هشت"] = 8, ["نه"] = 9, ["ده"] = 10,
    };

    /// <summary>
    /// موضوع‌های پرسش. بلندترین واژهٔ منطبق برنده است (قیمت/موجودی ۲ امتیاز کمتر)؛ مثلاً «چند روزه می‌رسه» ← ارسال (نه قیمت با «چند»)
    /// و «وزنش چقدره» ← وزن.
    /// </summary>
    public static readonly AssistantTopic[] Topics =
    [
        new("price", "قیمت", ["قیمت", "قیمتش", "چنده", "چقدره", "چقدر", "چند"], []),
        new("stock", "موجودی", ["موجود", "موجوده", "موجودی", "دارید", "دارین", "تموم شده", "ناموجود"], []),
        new("warranty", "گارانتی", ["گارانتی", "گارانتیش", "ضمانت", "وارانتی"], ["گارانتی", "ضمانت"]),
        new("shipping", "ارسال و تحویل", ["ارسال", "تحویل", "پست", "پیک", "کی میرسه", "کی می رسه", "چند روزه", "چند روز", "هزینه ارسال"], [], true),
        new("return", "مرجوعی", ["مرجوع", "مرجوعی", "برگشت", "پس بدم", "پس بدهم", "تعویض", "عودت"], [], true),
        new("payment", "روش پرداخت", ["پرداخت در محل", "قسط", "قسطی", "اقساط", "کارت به کارت", "درگاه", "روش پرداخت", "روشهای پرداخت"], [], true),
        new("contact", "ساعت کاری و تماس", ["ساعت کاری", "ساعت کار", "کی بازید", "آدرس فروشگاه", "حضوری", "ایمیل"], [], true),
        new("original", "اصالت", ["اصل", "اصله", "اورجینال", "اصالت", "فیک", "تقلبی"], ["اصالت", "اصل"]),
        new("specs", "مشخصات", ["مشخصات", "مشخصاتش", "ویژگی", "ویژگیها", "ویژگی ها", "مشخصه", "توضیحات"], []),
        new("color", "رنگ", ["رنگ", "رنگش", "رنگای", "رنگهای", "رنگبندی"], ["رنگ"]),
        new("size", "ابعاد", ["ابعاد", "ابعادش", "سایز", "سایزش", "اندازه", "اندازش", "طول", "عرض", "ارتفاع"], ["ابعاد", "اندازه", "سایز", "طول", "عرض", "ارتفاع"]),
        new("weight", "وزن", ["وزن", "وزنش", "سنگینه"], ["وزن"]),
        new("battery", "باتری", ["باتری", "باتریش", "شارژ", "میلی آمپر"], ["باتری", "شارژ"]),
        new("memory", "حافظه", ["حافظه", "حافظش", "رم", "ظرفیت", "گیگ"], ["حافظه", "رم", "ظرفیت"]),
        new("material", "جنس", ["جنس", "جنسش", "متریال"], ["جنس", "متریال", "بدنه"]),
        new("power", "توان", ["توان", "وات", "قدرت", "مصرف برق"], ["توان", "وات", "قدرت", "مصرف"]),
        new("compat", "سازگاری", ["سازگار", "سازگاری", "میخوره", "پشتیبانی میکنه", "پشتیبانی می کنه"], ["سازگار", "پشتیبانی"]),
        new("model", "مدل", ["کد مدل", "مدلش"], ["مدل"]),
    ];

    private static readonly AssistantTopic StockTopic = Topics.First(t => t.Key == "stock");

    private static readonly HashSet<string> QuestionWords = new(StringComparer.Ordinal)
    {
        "چقدر", "چقدره", "چنده", "چند", "چیه", "چی", "چه", "چطور", "چطوره", "آیا", "ایا", "داره", "دارد", "هست", "هستش", "است", "کدام", "کدوم",
        "مگه", "میشه", "میکنه", "میخوره", "بگو", "بگید", "بگین", "این", "اینو", "همین", "اون", "اونو", "سنگینه", "اصله",
    };

    /// <summary>بریدن پیام به حداکثر طول. مثال: متن ۴۰۰ نویسه‌ای ← ۳۰۰ نویسهٔ اول.</summary>
    public static string Cut(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        return value.Length > MaxLength ? value[..MaxLength] : value;
    }

    /// <summary>موضوع با کلید. مثال: Topic("battery").Title ← «باتری».</summary>
    public static AssistantTopic? Topic(string? key) => key is null ? null : Topics.FirstOrDefault(t => t.Key == key);

    /// <summary>
    /// فهم پیام. مثال‌ها:
    /// «سلام» ← Greeting؛ «سبد خریدم» ← CartShow؛ «دومی رو اضافه کن» ← CartAdd با Ordinal = 2؛
    /// «۲ تا از این بذار تو سبد» ← CartAdd با Quantity = 2؛ «سفارش FR-20261022-123456 کجاست» ← Track با OrderNumber؛
    /// «باتری این چقدر دوام داره» ← ProductQuestion با Topic = battery؛ «هوا فردا چطوره» ← OffTopic؛ «پاوربانک شیائومی» ← Search.
    /// </summary>
    public static AssistantParse Parse(string? text)
    {
        var normalized = PersianText.Normalize(Cut(text));
        var orderMatch = OrderNumberRegex().Match(normalized);
        string? orderNumber = null;
        if (orderMatch.Success)
        {
            orderNumber = $"FR-{orderMatch.Groups[1].Value}-{orderMatch.Groups[2].Value}";
            normalized = normalized.Remove(orderMatch.Index, orderMatch.Length).Insert(orderMatch.Index, " ");
        }

        var clean = Spaces().Replace(NonWord().Replace(normalized, " "), " ").Trim();
        var tokens = clean.Length == 0 ? Array.Empty<string>() : clean.Split(' ');
        var padded = " " + clean + " ";
        var shopCue = HasAny(padded, tokens, ShopCues);
        var quantity = Quantity(clean);
        var ordinal = Ordinal(tokens);
        var topic = BestTopic(padded, tokens);
        // واژهٔ مشخصه بدون نشانهٔ پرسش یعنی جست‌وجو: «شارژر سامسونگ» ← جست‌وجو (نه پرسش باتری)؛ «کابل شارژ دارید» ← موجودی.
        if (topic is { StoreLevel: false } && topic.Key is not ("price" or "stock") && !IsQuestion(text, tokens, topic))
            topic = HasAny(padded, tokens, StockTopic.Words) ? StockTopic : null;
        var query = BuildQuery(tokens, topic);

        AssistantParse Result(AssistantIntent intent, string? topicKey = null) =>
            new(intent, clean, query, topicKey, quantity, ordinal, orderNumber, shopCue);

        if (tokens.Length == 0)
            return orderNumber is null ? Result(AssistantIntent.Help) : Result(AssistantIntent.Track);

        // سلام/تشکر خالی (بدون درخواست دیگر).
        if (HasAny(padded, tokens, Greetings) && tokens.All(t => Greetings.Contains(t) || GreetingFillers.Contains(t) || Fillers.Contains(t)))
            return Result(AssistantIntent.Greeting);
        if (tokens.Length <= 6 && HasAny(padded, tokens, ThanksWords) && query.Length == 0)
            return Result(AssistantIntent.Thanks);

        // «پشتیبان/پشتیبانی» تنها ← درخواست پشتیبان؛ ولی «از بلوتوث ۵ پشتیبانی می‌کنه؟» پرسش سازگاری است.
        if (HasAny(padded, tokens, HumanWords) || (tokens.Contains("پشتیبان") || tokens.Contains("پشتیبانی")) && topic?.Key != "compat")
            return Result(AssistantIntent.Human);
        if (orderNumber is not null || HasAny(padded, tokens, TrackWords))
            return Result(AssistantIntent.Track);
        if (HasAny(padded, tokens, CheckoutWords))
            return Result(AssistantIntent.Checkout);
        if (HasAny(padded, tokens, RemoveWords))
            return Result(AssistantIntent.CartRemove);
        if (HasAny(padded, tokens, AddWords))
            return Result(AssistantIntent.CartAdd);
        if (HasAny(padded, tokens, CartShowWords))
            return Result(AssistantIntent.CartShow);
        if (HasAny(padded, tokens, AddressWords) && topic?.Key != "contact")
            return Result(AssistantIntent.Address);
        if (HasAny(padded, tokens, CompareWords))
            return Result(AssistantIntent.Compare);
        if (tokens.Length <= 5 && HasAny(padded, tokens, HelpWords) && topic is null)
            return Result(AssistantIntent.Help);
        if (topic is not null)
            return Result(AssistantIntent.ProductQuestion, topic.Key);
        if (!shopCue && (tokens.Any(OffTopicWords.Contains) || HasAny(padded, tokens, OffTopicPhrases)))
            return Result(AssistantIntent.OffTopic);
        return Result(AssistantIntent.Search);
    }

    /// <summary>
    /// پوشاندن اطلاعات حساس پیش از ذخیره: موبایل، شمارهٔ کارت (۱۶ رقم)، ایمیل و عدد ۱۰ رقمی (کد ملی). رقم‌ها انگلیسی می‌شوند.
    /// مثال: «شمارم ۰۹۱۲۱۲۳۴۵۶۷ است» ← «شمارم *** است». شمارهٔ سفارش (FR-…) دست نمی‌خورد.
    /// </summary>
    public static string Mask(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
            builder.Append(ch switch
            {
                >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
                _ => ch,
            });
        var value = EmailRegex().Replace(builder.ToString(), "***");
        value = CardRegex().Replace(value, "***");
        value = PhoneRegex().Replace(value, "***");
        return NationalIdRegex().Replace(value, "***");
    }

    /// <summary>واژه‌های معنادار برای مقایسهٔ دو متن (برای تطبیق پرسش با «پرسش‌های متداول»).</summary>
    public static HashSet<string> Words(string? text) =>
        Spaces().Replace(NonWord().Replace(PersianText.Normalize(text), " "), " ").Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2 && !Fillers.Contains(t))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// شباهت دو متن (۰ تا ۱) بر اساس واژه‌های مشترک نسبت به واژه‌های پرسش. مثال: «هزینه ارسال چقدره» و «هزینهٔ ارسال چقدر است؟» ← ۱.
    /// </summary>
    public static double Similarity(string? question, string? candidate)
    {
        var a = Words(question);
        var b = Words(candidate);
        if (a.Count == 0 || b.Count == 0)
            return 0;
        var common = a.Count(w => b.Contains(w) || b.Any(x => x.Length >= 4 && w.Length >= 4 && (x.StartsWith(w, StringComparison.Ordinal) || w.StartsWith(x, StringComparison.Ordinal))));
        return common / (double)Math.Max(a.Count, Math.Min(b.Count, a.Count + 2));
    }

    /// <summary>
    /// دو بخش پیام مقایسه. مثال: «مقایسه ایرپاد ۳ با گلکسی بادز» ← («ایرپاد 3»، «گلکسی بادز»). نبود جداکننده ← null.
    /// </summary>
    public static (string First, string Second)? CompareParts(string? text)
    {
        var clean = Spaces().Replace(NonWord().Replace(PersianText.Normalize(text), " "), " ").Trim();
        foreach (var word in CompareWords)
            clean = (" " + clean + " ").Replace(" " + word + " ", " ", StringComparison.Ordinal).Trim();
        foreach (var separator in new[] { " با ", " یا ", " و ", " vs " })
        {
            var index = clean.IndexOf(separator, StringComparison.Ordinal);
            if (index <= 0)
                continue;
            var first = BuildQuery(clean[..index].Split(' ', StringSplitOptions.RemoveEmptyEntries), null);
            var second = BuildQuery(clean[(index + separator.Length)..].Split(' ', StringSplitOptions.RemoveEmptyEntries), null);
            if (first.Length > 0 && second.Length > 0)
                return (first, second);
        }
        return null;
    }

    /// <summary>
    /// آیا پیام دربارهٔ یک ویژگی «می‌پرسد»؟ علامت سؤال، واژهٔ پرسشی («چقدر»، «چیه»، «داره»…)، اشاره («این») یا ضمیر چسبیده («باتریش»).
    /// مثال: «باتریش چقدره» ← بله؛ «شارژر سامسونگ» ← خیر.
    /// </summary>
    private static bool IsQuestion(string? raw, string[] tokens, AssistantTopic topic) =>
        (raw ?? string.Empty).IndexOfAny(['?', '؟']) >= 0
        || tokens.Any(QuestionWords.Contains)
        || tokens.Any(t => t.Length > 2 && t[^1] == 'ش' && topic.Words.Contains(t));

    private static AssistantTopic? BestTopic(string padded, string[] tokens)
    {
        AssistantTopic? best = null;
        var bestScore = 0;
        foreach (var topic in Topics)
        {
            // «چقدر/چنده» عمومی است: «باتریش چقدره» یا «وزنش چقدره» پرسش باتری/وزن است نه قیمت؛ پس موضوع‌های دیگر ۲ امتیاز اضافه می‌گیرند.
            var bonus = topic.Key is "price" or "stock" ? 0 : 2;
            foreach (var word in topic.Words)
                if (word.Length + bonus > bestScore && Has(padded, tokens, word))
                {
                    best = topic;
                    bestScore = word.Length + bonus;
                }
        }
        return best;
    }

    private static string BuildQuery(string[] tokens, AssistantTopic? topic)
    {
        var topicWords = topic?.Words.SelectMany(w => w.Split(' ')).ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<string>();
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (Fillers.Contains(token) || ActionTokens.Contains(token) || topicWords.Contains(token) || Greetings.Contains(token) || OrdinalWords.ContainsKey(token))
                continue;
            // «۲ تا» / «دو عدد»: عدد تعداد است، نه بخشی از نام کالا.
            var next = i + 1 < tokens.Length ? tokens[i + 1] : null;
            if (next is "تا" or "عدد" or "دونه" or "دانه" && (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out _) || NumberWords.ContainsKey(token)))
                continue;
            kept.Add(token);
        }
        var query = string.Join(' ', kept);
        return query.Length > 100 ? query[..100].Trim() : query;
    }

    private static int Quantity(string clean)
    {
        var match = QuantityRegex().Match(clean);
        if (!match.Success)
            return 1;
        var raw = match.Groups[1].Value;
        var value = int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : NumberWords.GetValueOrDefault(raw, 1);
        return Math.Clamp(value, 1, 20);
    }

    private static int? Ordinal(string[] tokens)
    {
        foreach (var token in tokens)
            if (OrdinalWords.TryGetValue(token, out var value))
                return value;
        for (var i = 0; i + 1 < tokens.Length; i++)
            if (tokens[i] == "شماره" && int.TryParse(tokens[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 6)
                return n;
        return null;
    }

    private static bool HasAny(string padded, string[] tokens, IEnumerable<string> words) => words.Any(w => Has(padded, tokens, w));

    /// <summary>
    /// واژهٔ چندبخشی ← وجود عبارت کامل؛ واژهٔ ≤۳ نویسه ← فقط واژهٔ کامل؛ بقیه ← شروع یک واژه (برای پسوندها: «سبدم»، «گارانتیش»).
    /// </summary>
    private static bool Has(string padded, string[] tokens, string word)
    {
        if (word.Contains(' '))
        {
            // عبارت کامل، با حداکثر ۲ نویسه پسوند روی واژهٔ آخر: «اضافه کنید» ✓ ولی «پاک کننده» ≠ «پاک کن».
            var probe = " " + word;
            for (var at = padded.IndexOf(probe, StringComparison.Ordinal); at >= 0; at = padded.IndexOf(probe, at + 1, StringComparison.Ordinal))
            {
                var end = padded.IndexOf(' ', at + probe.Length);
                if (end >= 0 && end - (at + probe.Length) <= 2)
                    return true;
            }
            return false;
        }
        if (word.Length <= 3)
            return tokens.Contains(word);
        return tokens.Any(t => t.StartsWith(word, StringComparison.Ordinal) && t.Length <= word.Length + 3);
    }

    [GeneratedRegex(@"\bfr-?(\d{8})-?(\d{4,6})\b", RegexOptions.CultureInvariant)]
    private static partial Regex OrderNumberRegex();

    [GeneratedRegex(@"[^\p{L}\p{N}\s]", RegexOptions.CultureInvariant)]
    private static partial Regex NonWord();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(\d{1,2}|یک|یه|دو|سه|چهار|پنج|شش|هفت|هشت|نه|ده) (?:تا|عدد|دونه|دانه)(?![\p{L}\p{N}])", RegexOptions.CultureInvariant)]
    private static partial Regex QuantityRegex();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?<!\d)(?:\d[ -]?){15}\d(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex CardRegex();

    [GeneratedRegex(@"(?<!\d)(?:\+98|0098|0)?9\d{9}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"(?<![\d-])\d{10}(?![\d-])", RegexOptions.CultureInvariant)]
    private static partial Regex NationalIdRegex();
}
