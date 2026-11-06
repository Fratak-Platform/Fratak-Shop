using System.Text;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>یک ردیف مشخصات که از یک منبع خوانده شده. مثال: («digikala»، «وزن»، «۱۸۵ گرم»).</summary>
public sealed record SpecObservation(string Source, string Key, string Value);

/// <summary>
/// نتیجهٔ رأی‌گیری یک ردیف بین منبع‌ها. <c>Agreement</c> = تعداد منبع‌های موافق مقدار برنده؛
/// <c>Conflict</c> = منبعی مقدار متفاوت گفته (این ردیف هرگز خودکار اعمال نمی‌شود).
/// </summary>
public sealed record MergedSpec(string Key, string Value, IReadOnlyList<string> Sources, int Agreement, bool Conflict, IReadOnlyList<string> Alternatives);

/// <summary>مقایسهٔ ردیف پیشنهادی با مشخصات فعلی محصول (B9).</summary>
public enum SpecDiffKind : byte
{
    /// <summary>این ویژگی را محصول ندارد ← افزودنی.</summary>
    New = 0,
    /// <summary>همان مقدار را دارد ← کاری لازم نیست.</summary>
    Same = 1,
    /// <summary>مقدار متفاوت است ← فقط با تأیید کارشناس جایگزین می‌شود.</summary>
    Different = 2,
}

/// <summary>یک ردیف پیشنهادی برای صفحهٔ «مشخصات» استودیوی هوشمند.</summary>
public sealed record SpecSuggestion(string Key, string Value, string? Current, SpecDiffKind Diff, IReadOnlyList<string> Sources, int Agreement, bool Conflict, bool AutoApplicable);

/// <summary>
/// ادغام مشخصات فنی از چند منبع (B7–B9): یکسان‌سازی کلید و واحد، حذف ردیف‌های بی‌ربط (قیمت، فروشنده، ارسال…)،
/// رأی‌گیری بین منبع‌ها و مقایسه با مشخصات فعلی.
/// <para>چرا رأی‌گیری؟ یک سایت ممکن است اشتباه نوشته باشد؛ ردیفی که دو منبع مستقل روی آن توافق دارند قابل اعتماد است.</para>
/// <para>محل استفاده: SmartOrchestrator (کار «مشخصات» و «تکمیل هوشمند») و صفحهٔ استودیوی محصول.</para>
/// </summary>
public static partial class SpecMerge
{
    /// <summary>کلیدهای هم‌معنی ← یک نام استاندارد. (کلیدها پیش از جست‌وجو یکسان‌سازی می‌شوند.)</summary>
    public static readonly IReadOnlyDictionary<string, string> KeyAliases = BuildAliases(new()
    {
        ["وزن"] = ["وزن", "وزن محصول", "وزن دستگاه", "weight", "وزن خالص"],
        ["ابعاد"] = ["ابعاد", "اندازه", "ابعاد محصول", "dimensions", "size", "ابعاد دستگاه"],
        ["رنگ"] = ["رنگ", "رنگ بندی", "رنگ‌بندی", "color", "colour"],
        ["جنس"] = ["جنس", "جنس بدنه", "متریال", "material", "جنس محصول"],
        ["ظرفیت باتری"] = ["ظرفیت باتری", "باتری", "battery", "battery capacity", "ظرفیت باطری"],
        ["توان"] = ["توان", "توان مصرفی", "قدرت", "power", "وات", "توان خروجی"],
        ["ولتاژ"] = ["ولتاژ", "ولتاژ ورودی", "voltage"],
        ["حافظه داخلی"] = ["حافظه داخلی", "حافظه", "storage", "internal storage", "ظرفیت حافظه"],
        ["رم"] = ["رم", "حافظه رم", "ram", "memory"],
        ["اندازه صفحه نمایش"] = ["اندازه صفحه نمایش", "سایز صفحه", "اندازه نمایشگر", "screen size", "display size", "سایز نمایشگر"],
        ["رزولوشن"] = ["رزولوشن", "وضوح تصویر", "resolution", "دقت صفحه نمایش"],
        ["پردازنده"] = ["پردازنده", "سی پی یو", "cpu", "processor", "تراشه"],
        ["سیستم عامل"] = ["سیستم عامل", "os", "operating system"],
        ["نوع اتصال"] = ["نوع اتصال", "اتصال", "connectivity", "connection", "رابط اتصال"],
        ["بلوتوث"] = ["بلوتوث", "نسخه بلوتوث", "bluetooth"],
        ["گارانتی سازنده"] = ["گارانتی سازنده"],
        ["کشور سازنده"] = ["کشور سازنده", "ساخت کشور", "made in", "کشور مبدا", "country of origin"],
        ["مدل"] = ["مدل", "model", "شماره مدل", "model number"],
        ["درجه حفاظت"] = ["درجه حفاظت", "مقاومت در برابر آب", "ip rating", "استاندارد ip", "ضد آب"],
        ["مدت شارژدهی"] = ["مدت شارژدهی", "زمان شارژدهی", "battery life", "عمر باتری"],
        ["زمان شارژ"] = ["زمان شارژ", "مدت زمان شارژ", "charging time", "زمان شارژ کامل"],
    });

    /// <summary>
    /// کلیدهایی که «مشخصات فنی کالا» نیستند و هرگز وارد نمی‌شوند (اطلاعات فروشنده، قیمت، ارسال، گارانتی فروشنده…).
    /// </summary>
    public static readonly string[] JunkKeyParts =
    [
        "قیمت", "فروشنده", "ارسال", "موجودی", "تخفیف", "امتیاز", "نظر", "کد کالا", "شناسه", "sku", "price", "seller", "shipping",
        "گارانتی", "ضمانت", "warranty", "تماس", "تلفن", "آدرس", "لینک", "بارکد", "barcode", "بازدید", "پرداخت", "اقساط", "خرید",
        "توضیحات", "ویژگی های کلیدی", "سایر توضیحات", "سایر مشخصات", "مزایا", "معایب", "دیجی کالا", "ترب", "باسلام",
    ];

    /// <summary>
    /// یکسان‌سازی کلید: حروف عربی/فارسی، فاصله‌ها، علامت «:» آخر و نام‌های هم‌معنی.
    /// مثال: «وزن محصول:» ← «وزن»؛ «Weight» ← «وزن»؛ «جنس  بدنه» ← «جنس».
    /// </summary>
    public static string NormalizeKey(string? key)
    {
        var clean = PersianText.Clean(key).Trim().TrimEnd(':', '：', '-', '.').Trim();
        if (clean.Length == 0)
            return string.Empty;
        var lookup = LookupKey(clean);
        return KeyAliases.TryGetValue(lookup, out var canonical) ? canonical : clean;
    }

    private static string LookupKey(string key) => PersianText.Normalize(key).Replace('\u200c', ' ').Trim().ToLowerInvariant();

    private static Dictionary<string, string> BuildAliases(Dictionary<string, string[]> groups)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (canonical, aliases) in groups)
        foreach (var alias in aliases)
            map.TryAdd(LookupKey(alias), canonical);
        return map;
    }

    /// <summary>
    /// مقدار تمیز برای نمایش: فاصله‌ها، ارقام فارسی، واحدهای رایج.
    /// مثال: «۱۸۵ gr» ← «185 گرم»؛ («5G» دست نمی‌خورد چون نام شبکه است)؛ «5000mAh» ← «5000 میلی‌آمپر ساعت»؛ «  دارد  » ← «دارد».
    /// </summary>
    public static string NormalizeValue(string? value)
    {
        var text = PersianText.Clean(value);
        if (text.Length == 0)
            return string.Empty;
        text = ToLatinDigits(text);
        try
        {
            text = ReplaceUnits(text);
            text = MultiSpace().Replace(text, " ").Trim();
        }
        catch (RegexMatchTimeoutException)
        {
            // متن عجیب/خیلی طولانی: همان متن تمیزشده بدون یکسان‌سازی واحد.
        }
        return text.Length > 300 ? text[..300].TrimEnd() : text;
    }

    private static string ReplaceUnits(string text) =>
        UnitRegex().Replace(text, m =>
        {
            var unit = m.Groups["u"].Value.ToLowerInvariant();
            var name = unit switch
            {
                "gr" or "گرم" => "گرم",
                "kg" or "کیلوگرم" => "کیلوگرم",
                "mm" or "میلیمتر" or "میلی‌متر" => "میلی‌متر",
                "cm" or "سانتیمتر" or "سانتی‌متر" => "سانتی‌متر",
                "mah" => "میلی‌آمپر ساعت",
                "w" or "وات" => "وات",
                "v" or "ولت" => "ولت",
                "gb" or "گیگابایت" => "گیگابایت",
                "tb" or "ترابایت" => "ترابایت",
                "mb" or "مگابایت" => "مگابایت",
                "hz" or "هرتز" => "هرتز",
                "inch" or "اینچ" or "\"" => "اینچ",
                _ => unit,
            };
            return m.Groups["n"].Value + " " + name;
        });

    /// <summary>
    /// کلید مقایسهٔ مقدار: «185 گرم» و «۱۸۵گرم» و «185gr» همه ← «185 گرم». برای رأی‌گیری و تشخیص «همان مقدار».
    /// </summary>
    public static string ValueKey(string? value) => PersianText.Normalize(NormalizeValue(value)).Replace(" ", string.Empty).ToLowerInvariant();

    /// <summary>آیا این ردیف ارزش ورود دارد؟ (کلید/مقدار معقول، بدون لینک/تلفن، کلید جزو فهرست ممنوع نباشد.)</summary>
    public static bool IsUseful(string key, string value)
    {
        if (key.Length is < 2 or > 60 || value.Length is < 1 or > 300)
            return false;
        var lookup = LookupKey(key);
        if (JunkKeyParts.Any(j => lookup.Contains(j, StringComparison.Ordinal)))
            return false;
        if (value.Contains("http", StringComparison.OrdinalIgnoreCase) || value.Contains("www.", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            if (PhoneRegex().IsMatch(ToLatinDigits(value)))
                return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        // ردیف‌های «-» یا «ندارد اطلاعات» بی‌ارزش‌اند.
        var v = PersianText.Normalize(value);
        return v is not ("-" or "—" or "نامشخص" or "ندارد اطلاعات" or "سایر" or "n/a" or "na" or "null");
    }

    /// <summary>
    /// رأی‌گیری: برای هر کلید، مقداری که بیشترین منبع مستقل گفته‌اند برنده است (تساوی ← منبع معتبرتر طبق ترتیب ورودی).
    /// مثال: دیجی‌کالا «185 گرم»، تأمین‌کننده «185gr»، فروشگاه سوم «190 گرم» ← («185 گرم»، توافق ۲، تعارض ✓).
    /// </summary>
    public static List<MergedSpec> Merge(IEnumerable<SpecObservation> observations)
    {
        var rows = new List<(string Source, string Key, string Value, int Order)>();
        var order = 0;
        foreach (var o in observations)
        {
            var key = NormalizeKey(o.Key);
            var value = NormalizeValue(o.Value);
            if (key.Length == 0 || value.Length == 0 || !IsUseful(key, value))
                continue;
            rows.Add((o.Source, key, value, order++));
        }

        var result = new List<MergedSpec>();
        foreach (var byKey in rows.GroupBy(r => r.Key, StringComparer.Ordinal).OrderBy(g => g.Min(r => r.Order)))
        {
            var groups = byKey
                .GroupBy(r => ValueKey(r.Value), StringComparer.Ordinal)
                .Select(g => new
                {
                    Sources = g.Select(r => r.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    Value = g.OrderBy(r => r.Order).First().Value,
                    FirstOrder = g.Min(r => r.Order),
                })
                .OrderByDescending(g => g.Sources.Count).ThenBy(g => g.FirstOrder).ToList();
            var winner = groups[0];
            var alternatives = groups.Skip(1).Select(g => g.Value).Take(3).ToList();
            result.Add(new MergedSpec(byKey.Key, winner.Value, winner.Sources, winner.Sources.Count, groups.Count > 1, alternatives));
        }
        return result;
    }

    /// <summary>
    /// مقایسه با مشخصات فعلی محصول. <c>AutoApplicable</c> فقط وقتی true است که ردیف «تازه» باشد، بدون تعارض،
    /// و دست‌کم <paramref name="minSources"/> منبع مستقل موافق باشند (B21: فقط اطمینان بالا خودکار).
    /// </summary>
    public static List<SpecSuggestion> Diff(IEnumerable<MergedSpec> merged, IEnumerable<(string Key, string Value)> current, int minSources = 2)
    {
        var existing = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in current)
            existing.TryAdd(LookupKey(NormalizeKey(key)), value);

        return merged.Select(m =>
        {
            var found = existing.TryGetValue(LookupKey(m.Key), out var now);
            var diff = !found ? SpecDiffKind.New
                : ValueKey(now) == ValueKey(m.Value) ? SpecDiffKind.Same
                : SpecDiffKind.Different;
            var auto = diff == SpecDiffKind.New && !m.Conflict && m.Agreement >= minSources;
            return new SpecSuggestion(m.Key, m.Value, now, diff, m.Sources, m.Agreement, m.Conflict, auto);
        }).OrderBy(s => s.Diff == SpecDiffKind.Same ? 2 : s.Diff == SpecDiffKind.Different ? 1 : 0).ToList();
    }

    /// <summary>
    /// کلید پیشنهادی را با «کلید فیلتر» موجود هماهنگ می‌کند تا فیلترهای دسته کار کنند (B9).
    /// مثال: فیلتر با FeatureKey «جنس بدنه» تعریف شده و پیشنهاد «جنس» است ← «جنس بدنه».
    /// </summary>
    public static string AlignToFilterKey(string key, IEnumerable<string> filterKeys)
    {
        var canonical = NormalizeKey(key);
        foreach (var filterKey in filterKeys)
        {
            if (string.Equals(NormalizeKey(filterKey), canonical, StringComparison.Ordinal))
                return filterKey;
        }
        return canonical;
    }

    private static string ToLatinDigits(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(ch switch
            {
                >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
                '٫' => '.',
                _ => ch,
            });
        }
        return builder.ToString();
    }

    [GeneratedRegex(@"(?<n>\d+(?:[.,]\d+)?)\s*(?<u>mah|kg|gr|mm|cm|gb|tb|mb|hz|inch|w|v|""|گرم|کیلوگرم|میلیمتر|میلی‌متر|سانتیمتر|سانتی‌متر|وات|ولت|گیگابایت|ترابایت|مگابایت|هرتز|اینچ)(?![a-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex UnitRegex();

    [GeneratedRegex(@"\s{2,}", RegexOptions.None, 200)]
    private static partial Regex MultiSpace();

    [GeneratedRegex(@"(?:\+98|0)9\d{9}|0\d{2,3}[-\s]?\d{7,8}", RegexOptions.None, 200)]
    private static partial Regex PhoneRegex();
}
