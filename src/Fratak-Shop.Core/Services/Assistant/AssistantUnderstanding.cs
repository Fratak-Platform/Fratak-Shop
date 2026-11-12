using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Entities.Assistant;

namespace Fratak_Shop.Core.Services.Assistant;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۵ – «فهم پیام» در یک جا (لایهٔ ۸)
//  تا دور ۲۴ پیام خام مستقیم به AssistantNlu.Parse می‌رفت. حالا سه مرحله دارد:
//   ۱. آماده‌سازی (AssistantLanguage.Prepare): غلط تایپی، کیبورد انگلیسی، فینگلیش، محاوره.
//   ۲. خواسته‌های خرید (AssistantSlotParser.Extract): بودجه، مرتب‌سازی، تخفیف، کاربرد، مشاوره.
//   ۳. نیت (AssistantNlu.Parse) + چند قاعدهٔ تکمیلی:
//      • لغو/مرجوعی/فاکتور سفارش (کارهای بعد از خرید)،
//      • «بودجه/مرتب‌سازی + نام کالا» ← مشاور خرید (Advisor)،
//      • «هدفون با صدای خوب دارید؟» ← پرسش موجودی (نه پرسش صدا دربارهٔ کالای صفحه).
//  خروجی همهٔ این‌ها را با هم برمی‌گرداند تا سرویس دستیار و تست‌ها (AssistantEvalSet) یک مسیر واحد داشته باشند.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// نتیجهٔ فهم یک پیام.
/// </summary>
/// <param name="Prepared">متن آماده‌شده و اصلاح‌های انجام‌شده.</param>
/// <param name="Slots">خواسته‌های خرید (بودجه، مرتب‌سازی، …).</param>
/// <param name="Parse">نتیجهٔ قانون‌ها روی متن آماده‌شده (موضوع ممکن است به «stock» عوض شده باشد).</param>
/// <param name="Route">مسیر نهایی پاسخ (ممکن است با Parse.Intent فرق کند: Advisor، OrderCancel، …).</param>
public sealed record AssistantUnderstood(AssistantPrepared Prepared, AssistantSlots Slots, AssistantParse Parse, AssistantIntent Route)
{
    /// <summary>عبارت جست‌وجوی کالا بدون بودجه و مرتب‌سازی (برای مشاور خرید). مثال: «هدفون زیر ۲ میلیون» ← «هدفون».</summary>
    public string ProductQuery { get; init; } = Parse.Query;
}

/// <summary>فهم پیام مشتری. <see cref="Understand"/> را ببینید.</summary>
public static class AssistantUnderstanding
{
    private static readonly HashSet<string> OrderContext = new(StringComparer.Ordinal)
    {
        "سفارش", "سفارشم", "سفارشمو", "سفارشامو", "سفارشات", "سفارشهام", "خرید", "خریدم", "خریدمو", "خریدی", "بسته", "بستم", "مرسوله", "کالام", "کالامو", "سفارشی",
    };

    private static readonly string[] CancelWords = ["لغو", "کنسل", "انصراف", "منصرف", "لغوش", "کنسلش"];
    private static readonly string[] CancelPast = ["لغو شد", "لغو شده", "کنسل شد", "کنسل شده", "لغو کردید", "کنسل کردید", "لغو کردین", "کنسل کردین"];
    private static readonly string[] ReturnWords = ["مرجوع", "مرجوعی", "عودت", "پس بدم", "پس بدهم", "پسش بدم", "برگردونم", "برگردانم", "پس بفرستم"];

    /// <summary>جمله‌هایی که خودشان «می‌خواهم مرجوع کنم» هستند (نه پرسش از شرایط مرجوعی).</summary>
    private static readonly string[] ReturnActions =
    [
        "مرجوع کنم", "مرجوعش کنم", "مرجوع کردن", "مرجوعش", "ثبت مرجوعی", "درخواست مرجوعی", "میخوام مرجوع", "میخواستم مرجوع", "پس بدم", "پس بدهم", "پسش بدم",
        "برگردونم", "برگردانم", "عودت بدم", "پس بفرستم", "مرجوع میکنم",
    ];

    private static readonly string[] InvoiceWords = ["فاکتور", "فاکتورم", "صورتحساب", "صورت حساب", "رسید خرید", "پیش فاکتور"];
    private static readonly string[] StockCue = ["دارید", "دارین", "داری", "موجود", "موجوده", "میفروشید"];

    /// <summary>
    /// فهم کامل یک پیام. <paramref name="vocabulary"/> = واژه‌های کاتالوگ (برای اصلاح غلط تایپی نام کالا؛ null = فقط واژه‌نامهٔ پایه).
    /// مثال‌ها:
    /// «هدفن بلوتوثی زیر ۲ ملیون» ← Route = Advisor، ProductQuery = «هدفون بلوتوثی»، MaxPrice = ۲٬۰۰۰٬۰۰۰؛
    /// «سفارشمو کنسل کن» ← OrderCancel؛ «فاکتور سفارشم» ← Invoice؛ «شرایط مرجوعی چیه» ← ProductQuestion (موضوع مرجوعی، نه ثبت مرجوعی)؛
    /// «منو علی صدا کن» ← Preference (اسم مشتری هرگز «اصلاح» نمی‌شود).
    /// </summary>
    public static AssistantUnderstood Understand(string? text, AssistantVocabulary? vocabulary = null)
    {
        var original = AssistantNlu.Cut(text);
        // سلیقهٔ گفت‌وگو («منو آرمین صدا کن») دست نمی‌خورد تا اسم به واژهٔ دیگری «اصلاح» نشود.
        var prepared = AssistantPersona.ParsePreference(original) is not null
            ? new AssistantPrepared(original, original, [])
            : AssistantLanguage.Prepare(original, vocabulary);
        var parse = AssistantNlu.Parse(prepared.Text);
        var slots = AssistantSlotParser.Extract(prepared.Text);
        var route = parse.Intent;
        var tokens = parse.Normalized.Length == 0 ? Array.Empty<string>() : parse.Normalized.Split(' ');
        var padded = " " + parse.Normalized + " ";

        // ── ۱. کارهای بعد از خرید ──
        if (route is not (AssistantIntent.Human or AssistantIntent.Preference or AssistantIntent.Greeting or AssistantIntent.Thanks)
            && PostPurchase(tokens, padded, parse) is { } post)
            return new AssistantUnderstood(prepared, slots, parse, post);

        // ── ۲. موجودی یک نوع کالا با ویژگی: «هدفون با صدای خوب دارید؟» ← پرسش موجودی (نه پرسش صدای کالای صفحه) ──
        if (route == AssistantIntent.ProductQuestion && AssistantNlu.Topic(parse.Topic) is { StoreLevel: false } topic
            && topic.Key is not ("price" or "stock" or "about" or "specs" or "warranty" or "original")
            && Contains(tokens, padded, StockCue) && parse.Query.Length >= 2 && !PointsHere(tokens))
        {
            parse = parse with { Topic = "stock" };
        }

        // ── ۳. مشاور خرید: خواسته‌های خرید + نام کالا (یا بودجه/تخفیف بدون نام) ──
        if (slots.Any)
        {
            var rest = AssistantNlu.Parse(slots.Rest);
            var productQuery = rest.Query;
            var searchLike = route is AssistantIntent.Search or AssistantIntent.Offers or AssistantIntent.Finder
                             || route == AssistantIntent.ProductQuestion && parse.Topic is "price" or "stock" or "reviews";
            var hasProduct = AssistantNlu.Words(productQuery).Count > 0;
            // «پیشنهاد ویژه»، «کمکم کن انتخاب کنم» بدون نام کالا همان مسیر قبلی می‌ماند.
            var advisor = searchLike && (hasProduct && (slots.Strong || slots.Advice && route != AssistantIntent.Offers)
                                         || slots.HasBudget || slots.Discounted && route != AssistantIntent.Offers);
            // «کالاهای پرفروش» / «ارزون‌ترین‌ها» بی‌نام کالا: فهرست مرتب‌شدهٔ کل فروشگاه (مشاور بدون عبارت).
            if (!advisor && searchLike && slots.Sort is not null && !hasProduct && route != AssistantIntent.Finder)
                advisor = true;
            if (advisor)
                return new AssistantUnderstood(prepared, slots, parse, AssistantIntent.Advisor) { ProductQuery = productQuery };
        }

        return new AssistantUnderstood(prepared, slots, parse, route);
    }

    /// <summary>
    /// لغو / مرجوعی / فاکتور. قانون‌ها:
    /// لغو فقط با نشانهٔ سفارش («سفارشمو کنسل کن»؛ «کنسل شد؟» پرسش وضعیت است نه درخواست لغو)؛
    /// مرجوعی با جملهٔ فعلی («می‌خوام مرجوع کنم») یا نشانهٔ سفارش («خریدمو مرجوع»)؛ «شرایط مرجوعی چیه» همان پرسش فروشگاهی می‌ماند؛
    /// فاکتور همیشه.
    /// </summary>
    private static AssistantIntent? PostPurchase(string[] tokens, string padded, AssistantParse parse)
    {
        var order = parse.OrderNumber is not null || tokens.Any(OrderContext.Contains);
        if (Contains(tokens, padded, CancelWords) && order && !Contains(tokens, padded, CancelPast))
            return AssistantIntent.OrderCancel;
        var policy = tokens.Contains("شرایط") || tokens.Contains("قوانین") || tokens.Contains("چند") || tokens.Contains("مهلت");
        if (Contains(tokens, padded, ReturnWords) && (Contains(tokens, padded, ReturnActions) && !policy || order && !policy))
            return AssistantIntent.OrderReturn;
        if (Contains(tokens, padded, InvoiceWords))
            return AssistantIntent.Invoice;
        return null;
    }

    private static bool PointsHere(string[] tokens) => tokens.Any(t => t is "این" or "اینو" or "همین" or "همینو" or "اینه" or "اون" or "اونو");

    /// <summary>عبارت چندواژه‌ای ← وجود کامل؛ تک‌واژه ← واژهٔ کامل یا با پسوند کوتاه (≤۳ حرف) برای واژه‌های ≥۴ حرفی.</summary>
    private static bool Contains(string[] tokens, string padded, IEnumerable<string> words) => words.Any(w =>
        w.Contains(' ')
            ? padded.Contains(" " + w + " ", StringComparison.Ordinal) || padded.Contains(" " + w, StringComparison.Ordinal) && w.Length >= 6
            : tokens.Any(t => t == w || w.Length >= 4 && t.StartsWith(w, StringComparison.Ordinal) && t.Length <= w.Length + 3));

    /// <summary>متن نرمال‌شده برای مقایسهٔ دو پیام (آموزش‌ها و پرسش‌های تکراری).</summary>
    public static string Key(string? text) => PersianText.Normalize(AssistantNlu.Cut(text));
}
