using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// دور ۲۲: نویسندهٔ «توضیح کوتاه» کالا فقط از واقعیت‌های ثبت‌شده (مشخصات فنی، برند، گارانتی) — بدون هیچ ادعای ساختگی.
/// <para>
/// خواستهٔ کاربر: «هر محصولی باید توضیح کوتاه داشته باشد». متن یک یا دو جملهٔ کوتاه است، مهم‌ترین مشخصات را جلوی چشم خریدار می‌گذارد
/// و برای جلوگیری از متن تکراری در سایت (خطر گوگل) سه قالب جمله دارد که با «هش» پایدار نام کالا انتخاب می‌شود
/// (هر کالا همیشه همان قالب را می‌گیرد؛ کالاهای مختلف قالب‌های مختلف).
/// </para>
/// <para>مثال: «پاوربانک انکر مدل A1234» + [ظرفیت: ۱۰۰۰۰ میلی‌آمپر ساعت، خروجی: ۲۰ وات، شارژ سریع: دارد] ←
/// «پاوربانک انکر مدل A1234 با ظرفیت ۱۰۰۰۰ میلی‌آمپر ساعت، خروجی ۲۰ وات و قابلیت شارژ سریع عرضه می‌شود.»</para>
/// <para>محل استفاده: کار «تکمیل هوشمند کالا» (SmartOrchestrator) از طریق SmartCatalogService.FillShortDescriptionAsync (فقط وقتی خالی است).</para>
/// </summary>
public static class ShortDescriptionWriter
{
    /// <summary>بیشترین طول خروجی (ستون ShortDescription تا ۱۰۰۰ حرف است؛ کوتاه بهتر خوانده می‌شود).</summary>
    public const int MaxLength = 300;

    /// <summary>کلیدهای مهم به ترتیب اهمیت (بخشی از نام کلید کافی است). مشخصات دیگر فقط اگر این‌ها نبودند.</summary>
    private static readonly string[] Priority =
    [
        "ظرفیت", "توان", "خروجی", "نوع اتصال", "اتصال", "عمر باتری", "باتری", "حافظه داخلی", "حافظه", "رم", "پردازنده", "اندازه صفحه",
        "صفحه نمایش", "رزولوشن", "دوربین", "جنس", "متریال", "وزن", "ابعاد", "مقاومت", "استاندارد", "ولتاژ", "کاربرد", "نوع",
    ];

    private static readonly HashSet<string> Yes = new(StringComparer.Ordinal) { "دارد", "بله", "هست", "yes", "true", "✓" };
    private static readonly HashSet<string> No = new(StringComparer.Ordinal) { "ندارد", "خیر", "نیست", "no", "false", "-", "—", "ندارد." };

    /// <summary>
    /// نوشتن توضیح کوتاه. خروجی null یعنی واقعیت کافی نبود (بهتر است خالی بماند تا متن پوچ). حداقل ۲۰ حرف.
    /// </summary>
    /// <param name="name">نام کالا.</param>
    /// <param name="brand">نام برند (فارسی) یا null.</param>
    /// <param name="categoryName">نام دسته (نه «دسته‌بندی نشده») یا null.</param>
    /// <param name="features">مشخصات فنی ثبت‌شده.</param>
    /// <param name="warranty">عنوان گارانتی («گارانتی ۱۸ ماهه») یا null.</param>
    public static string? Write(string? name, string? brand, string? categoryName, IReadOnlyList<SeoFeature>? features, string? warranty = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var title = name.Trim();
        var facts = PickFacts(features ?? [], title);
        var sentence = facts.Count switch
        {
            0 => null,
            _ => Template(title, JoinFacts(facts)),
        };

        if (sentence is null)
        {
            // بدون مشخصات: فقط اگر برند یا دستهٔ واقعی معلوم است، یک جملهٔ معرفی واقعی (بدون صفت و ادعا).
            var known = new List<string>();
            if (!string.IsNullOrWhiteSpace(brand) && !title.Contains(brand.Trim(), StringComparison.Ordinal))
                known.Add($"ساخت برند {brand.Trim()}");
            if (!string.IsNullOrWhiteSpace(categoryName) && !CategoryClassifier.IsCatchAll(categoryName))
                known.Add($"از دستهٔ {categoryName.Trim()}");
            if (known.Count == 0)
                return null;
            sentence = $"{title}، {string.Join(" و ", known)}.";
        }

        if (!string.IsNullOrWhiteSpace(warranty) && !sentence.Contains(warranty.Trim(), StringComparison.Ordinal))
            sentence += $" این کالا با {warranty.Trim()} ارائه می‌شود.";

        sentence = Cut(sentence, MaxLength);
        return sentence.Length >= 20 ? sentence : null;
    }

    /// <summary>
    /// تا سه واقعیت: اول کلیدهای مهم (به ترتیب <see cref="Priority"/>)، بعد بقیه. مقدار «ندارد» حذف، «دارد» ← «قابلیت {کلید}».
    /// مقدار خیلی بلند (بیش از ۴۰ حرف)، کلید خیلی بلند و واقعیتی که همین الان در نام آمده حذف می‌شود.
    /// </summary>
    public static IReadOnlyList<string> PickFacts(IReadOnlyList<SeoFeature> features, string? name = null)
    {
        var normalizedName = PersianText.Normalize(name);
        var usable = features
            .Where(f => !string.IsNullOrWhiteSpace(f.Key) && !string.IsNullOrWhiteSpace(f.Value))
            .Select(f => (Key: f.Key.Trim().TrimEnd(':', '：'), Value: f.Value.Trim()))
            .Where(f => f.Key.Length <= 30 && f.Value.Length <= 40 && !No.Contains(PersianText.Normalize(f.Value)))
            .Where(f => f.Value.Length < 4 || !normalizedName.Contains(PersianText.Normalize(f.Value), StringComparison.Ordinal))
            .DistinctBy(f => PersianText.Normalize(f.Key))
            .ToList();

        int Rank(string key)
        {
            var k = PersianText.Normalize(key);
            for (var i = 0; i < Priority.Length; i++)
            {
                if (k.Contains(PersianText.Normalize(Priority[i]), StringComparison.Ordinal))
                    return i;
            }
            return Priority.Length;
        }

        return usable
            .Select((f, index) => (f, Rank: Rank(f.Key), index))
            .OrderBy(x => x.Rank).ThenBy(x => x.index)
            .Take(3)
            .Select(x => Yes.Contains(PersianText.Normalize(x.f.Value)) ? $"قابلیت {x.f.Key}" : $"{x.f.Key} {x.f.Value}")
            .ToList();
    }

    /// <summary>«الف»، «الف و ب»، «الف، ب و ج».</summary>
    private static string JoinFacts(IReadOnlyList<string> facts) => facts.Count switch
    {
        1 => facts[0],
        2 => $"{facts[0]} و {facts[1]}",
        _ => $"{string.Join("، ", facts.Take(facts.Count - 1))} و {facts[^1]}",
    };

    /// <summary>سه قالب جملهٔ ساده و طبیعی؛ انتخاب با هش پایدار نام (نه تصادفی).</summary>
    private static string Template(string title, string facts) => (StableHash(title) % 3) switch
    {
        0 => $"{title} با {facts} عرضه می‌شود.",
        1 => $"{title} دارای {facts} است.",
        _ => $"مشخصات کلیدی {title}: {facts}.",
    };

    /// <summary>هش پایدار (FNV-1a) — برخلاف string.GetHashCode در هر اجرا یکسان است.</summary>
    internal static uint StableHash(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return hash;
    }

    private static string Cut(string text, int max)
    {
        if (text.Length <= max)
            return text;
        var cut = text[..max];
        var stop = cut.LastIndexOf('.');
        if (stop > max / 2)
            return cut[..(stop + 1)];
        var space = cut.LastIndexOf(' ');
        return (space > 0 ? cut[..space] : cut).TrimEnd('،', ',', ' ') + "…";
    }
}
