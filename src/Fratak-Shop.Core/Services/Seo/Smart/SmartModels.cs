using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Seo.Knowledge;

namespace Fratak_Shop.Core.Services.Seo.Smart;

// ═══════════════════════════════════════════════════════════════════════════
//  مدل‌های «پیشنهاد هوشمند» متن (مرحلهٔ شانزدهم)
//  دکمهٔ «پیشنهاد هوشمند» کنار هر فیلد متنی پنل: متن فعلی را می‌گیرد و ۲ تا ۳ نسخهٔ بهتر با توضیح می‌دهد؛
//  کاربر یکی را «استفاده» می‌کند و فیلد پر می‌شود (ذخیره با خود کاربر است).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// نوع فیلد (هر نوع قاعده‌های خودش را دارد).
/// مثال: نام محصول ← ProductName، عنوان سئو ← SeoTitle، توضیحات متا ← MetaDescription، توضیح کوتاه/چکیده ← ShortText،
/// متن اصلی محصول یا مقاله (ویرایشگر) ← Body، هر فیلد دیگر ← Plain.
/// </summary>
public enum SmartPurpose
{
    Plain = 0,
    ProductName = 1,
    SeoTitle = 2,
    MetaDescription = 3,
    ShortText = 4,
    Body = 5,
}

/// <summary>هدف اصلی بازنویسی (ترتیب نسخه‌ها را تعیین می‌کند). Balanced = همه با هم.</summary>
public enum SmartGoal
{
    Balanced = 0,
    Seo = 1,
    Persuasive = 2,
    Clarity = 3,
}

/// <summary>درخواست پیشنهاد هوشمند برای یک فیلد.</summary>
public sealed class SmartTextRequest
{
    public SmartPurpose Purpose { get; init; }
    public SmartGoal Goal { get; init; }

    /// <summary>متن فعلی فیلد (برای Body ممکن است HTML باشد).</summary>
    public string Text { get; init; } = "";
    public bool IsHtml { get; init; }

    /// <summary>کلمهٔ کلیدی کانونی صفحه (اگر هست).</summary>
    public string? Keyword { get; init; }

    /// <summary>نام صفحه (نام محصول، عنوان مقاله، نام دسته …).</summary>
    public string? Subject { get; init; }
    public string? EnglishName { get; init; }
    public string? Category { get; init; }
    public string? Brand { get; init; }

    /// <summary>نوع صفحه برای انتخاب الگوها: product، blog، category، brand، page، home، contact.</summary>
    public string PageType { get; init; } = "product";

    public StoreFacts Store { get; init; } = new();
    public ProductFacts? Product { get; init; }

    /// <summary>عبارت‌های جست‌وجوی واقعی آموخته برای این موضوع (از جدول دانش).</summary>
    public IReadOnlyList<string> Phrases { get; init; } = [];

    /// <summary>واژه‌های خاص سایت که غلط املایی حساب نمی‌شوند (نام برندها و …).</summary>
    public IReadOnlySet<string>? Vocabulary { get; init; }
}

/// <summary>یک نسخهٔ پیشنهادی.</summary>
public sealed class SmartVariant
{
    /// <summary>شناسهٔ نسخه: clean، concise، structured، seo، persuasive، engine.</summary>
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Text { get; set; } = "";
    public bool IsHtml { get; init; }

    /// <summary>کارهایی که انجام شد (برای کاربر). مثال: «املا: باطری ← باتری».</summary>
    public List<string> Changes { get; } = [];

    /// <summary>پیشنهادهایی که خودکار اعمال نشد (کاربر خودش تصمیم بگیرد).</summary>
    public List<string> Notes { get; } = [];

    /// <summary>شناسهٔ قاعده‌های جدول دانش که استفاده شد (برای یادگیری از پذیرش/رد).</summary>
    public List<string> RuleIds { get; } = [];

    /// <summary>امتیاز کیفیت ۰ تا ۱۰۰.</summary>
    public int Score { get; set; }
    public int Length => IsHtml ? PersianText.HtmlToText(Text).Length : Text.Length;
    public int Words => PersianText.WordCount(IsHtml ? PersianText.HtmlToText(Text) : Text);
}

/// <summary>تحلیل متن فعلی (پیش از پیشنهاد).</summary>
public sealed class SmartTextAnalysis
{
    public int Score { get; init; }
    public int Words { get; init; }
    public int Characters { get; init; }
    public List<string> Issues { get; init; } = [];
}

/// <summary>خروجی کامل «پیشنهاد هوشمند».</summary>
public sealed class SmartTextResult
{
    public List<SmartVariant> Variants { get; } = [];
    public SmartTextAnalysis Before { get; set; } = new();
    public List<string> Warnings { get; } = [];
}

/// <summary>
/// واقعیت‌های درست فروشگاه/محصول برای جمله‌های ترغیبی. هیچ ادعایی بدون پشتوانهٔ تنظیمات ساخته نمی‌شود:
/// «۷ روز ضمانت بازگشت» فقط وقتی بازگشت فعال است؛ «ضمانت اصالت» هرگز (قابل تأیید نیست).
/// </summary>
public sealed class SmartFacts(SmartTextRequest request)
{
    /// <summary>
    /// شرط یک الگو برقرار است؟ any همیشه؛ stock/warranty از محصول؛ return/cod/free/delivery از تنظیمات؛ original هرگز.
    /// شرط چندگانه با «|» یعنی «یکی کافی است».
    /// </summary>
    public bool Holds(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return true;
        return condition.Split('|').Any(c => c.Trim() switch
        {
            "any" or "" => true,
            "stock" => request.Product?.InStock == true,
            "warranty" => request.Product is { } p && (p.WarrantyMonths > 0 || !string.IsNullOrWhiteSpace(p.WarrantyTitle)),
            "return" => request.Store.ReturnEnabled && request.Store.ReturnDays > 0,
            "cod" => request.Store.CashOnDelivery,
            "free" => request.Store.FreeShippingThreshold > 0,
            "delivery" => request.Store.DeliveryMaxDays > 0,
            "brand" => !string.IsNullOrWhiteSpace(request.Brand),
            "category" => !string.IsNullOrWhiteSpace(request.Category),
            "specs" => request.Product?.Features.Count > 0,
            _ => false, // original و هر شرط ناشناخته: ادعای تأییدنشده ساخته نمی‌شود.
        });
    }

    /// <summary>نام صفحه برای الگوها: کلمهٔ کلیدی (اگر هست) یا نام.</summary>
    /// <para>برای محصول، نام کوتاه (بدون جزئیات انتهایی) تا تیترها طولانی نشوند. مثال: «هدفون سونی مدل WH-1000XM5 رنگ مشکی» → «هدفون سونی مدل WH-1000XM5».</para>
    public string Name(bool preferKeyword)
    {
        if (preferKeyword && !string.IsNullOrWhiteSpace(request.Keyword))
            return PersianText.Clean(request.Keyword);
        var name = PersianText.Clean(request.Subject ?? request.Keyword ?? "");
        return request.PageType == "product" && name.Length > 0 ? KeywordPlanner.ShortName(name) : name;
    }

    /// <summary>نوع کالا (اولین بخش نام دسته). مثال: «هدفون و هندزفری» → «هدفون».</summary>
    public string? Type
    {
        get
        {
            var category = PersianText.Clean(request.Category);
            if (category.Length == 0)
                return null;
            var head = category.Split([" و ", "،", ",", "|", "/"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
            return head.Length > 0 ? head : null;
        }
    }

    /// <summary>
    /// پر کردن جاهای خالی الگو. اگر جای خالی‌ای مقدار نداشت، null (الگو استفاده نمی‌شود).
    /// مثال: «{days} روز ضمانت بازگشت» با ReturnDays = 7 → «۷ روز ضمانت بازگشت».
    /// </summary>
    public string? Fill(string template, bool preferKeyword = false)
    {
        var name = Name(preferKeyword);
        var warranty = request.Product?.WarrantyTitle is { Length: > 0 } title
            ? PersianText.Clean(title)
            : request.Product?.WarrantyMonths is > 0 and var months ? $"{PersianText.Digits(months)} ماهه" : null;
        var values = new Dictionary<string, string?>
        {
            ["{name}"] = name.Length > 0 ? name : null,
            ["{type}"] = Type,
            ["{site}"] = PersianText.Clean(request.Store.SiteName) is { Length: > 0 } site ? site : null,
            ["{warranty}"] = warranty,
            ["{days}"] = request.Store.ReturnDays > 0 ? PersianText.Digits(request.Store.ReturnDays) : null,
            ["{min}"] = request.Store.DeliveryMinDays > 0 ? PersianText.Digits(request.Store.DeliveryMinDays) : null,
            ["{max}"] = request.Store.DeliveryMaxDays > 0 ? PersianText.Digits(request.Store.DeliveryMaxDays) : null,
            ["{free}"] = request.Store.FreeShippingThreshold > 0 ? PersianText.Money(request.Store.FreeShippingThreshold) + " تومان" : null,
        };
        var result = template;
        foreach (var (key, value) in values)
        {
            if (!result.Contains(key, StringComparison.Ordinal))
                continue;
            if (value is null)
                return null;
            result = result.Replace(key, value, StringComparison.Ordinal);
        }
        return result.Contains('{') ? null : result;
    }

    /// <summary>
    /// بهترین الگوهای یک فهرست (CTA، مزیت، تیتر …) که شرطشان برقرار است و برای این نوع صفحه‌اند، با تنوع ثابت
    /// بر اساس seed (همان متن ورودی همیشه همان پیشنهاد را می‌گیرد).
    /// </summary>
    public List<(KnowledgeRule Rule, string Text)> Pick(IEnumerable<KnowledgeRule> rules, Func<KnowledgeRule, bool> filter, int take, int seed, bool preferKeyword = false)
    {
        return rules
            .Where(r => r.Confidence >= 0.5 && filter(r))
            .Select(r => (Rule: r, Text: Fill(r.Term, preferKeyword)))
            .Where(x => x.Text is not null)
            .OrderByDescending(x => x.Rule.Confidence + Jitter(x.Rule.Term, seed))
            .Select(x => (x.Rule, x.Text!))
            .DistinctBy(x => x.Item2)
            .Take(take)
            .ToList();
    }

    private static double Jitter(string term, int seed) =>
        (TextModels.Fnv1a(term + ":" + seed) % 1000) / 1000.0 * 0.25;

    /// <summary>مزیت‌های کوتاه و درست (برای عنوان، متا و فهرست «چرا از ما بخرید»). مثال: [«۷ روز ضمانت بازگشت»، «پرداخت در محل»].</summary>
    public List<(KnowledgeRule Rule, string Text)> Benefits(SeoKnowledgeBase knowledge, string scope, int take, int seed) =>
        Pick(knowledge.Benefits, r => r.Topic == scope && Holds(r.Value) && r.Value != "any", take, seed)
            .Concat(Pick(knowledge.Benefits, r => r.Topic == scope && r.Value == "any", 1, seed))
            .DistinctBy(x => x.Rule.Value)
            .Take(take)
            .ToList();
}
