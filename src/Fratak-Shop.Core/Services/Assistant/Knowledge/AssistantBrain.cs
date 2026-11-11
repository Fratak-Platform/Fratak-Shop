using System.Globalization;
using System.IO.Compression;
using System.Text;
using Fratak_Shop.Data.Entities.Assistant;

namespace Fratak_Shop.Core.Services.Assistant;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۸ – «مغز» دستیار: جست‌وجو در جدول دانش (واژه‌نامه، راهنمای خرید، گپ کوتاه، هم‌معنی‌ها و یادگرفته‌ها).
//  همه‌چیز داخلی و بدون سرویس بیرونی است؛ فقط الگوهای پرسش فارسی + تطبیق کامل واژه.
//  محل استفاده: AssistantService (پیش از «متوجه نشدم/موجود نداریم»)، AssistantFactService (بذر و یادگیری) و تست‌های واحد.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// یک دانستنی آمادهٔ تطبیق. Forms = واژهٔ اصلی + شکل‌های دیگر (همه نرمال‌شده). Confidence = اطمینان یادگیری (۰ تا ۱).
/// Id = شناسهٔ ردیف دیتابیس (null = فقط از فایل بذر، پیش از ریختن در دیتابیس).
/// </summary>
public sealed record BrainFact(Guid? Id, AssistantFactKind Kind, string Term, IReadOnlyList<string> Forms, string Answer, string? Url, string? Topic, double Confidence);

/// <summary>نتیجهٔ تطبیق: دانستنی، واژه‌ای که در پیام پیدا شد و امتیاز (۰ تا ۱).</summary>
public sealed record BrainHit(BrainFact Fact, string Matched, double Score);

/// <summary>یک ردیف فایل بذر.</summary>
public sealed record BrainSeedRow(AssistantFactKind Kind, string Term, string? Aliases, string Answer, string? Topic, double Weight);

/// <summary>جست‌وجو و قالب‌بندی دانش دستیار (ایستا و بدون وابستگی؛ قابل تست).</summary>
public static class AssistantBrain
{
    /// <summary>نام منبع جاسازی‌شده (LogicalName در Fratak-Shop.Core.csproj).</summary>
    public const string ResourceName = "Fratak_Shop.Core.AssistantKnowledge.tsv.gz";

    /// <summary>نسخهٔ بذر؛ هر بار فایل عوض شد یکی بالا ببرید تا ردیف‌های تازه اضافه شوند (ردیف‌های ویرایش‌شده دست نمی‌خورند).</summary>
    public const int SeedVersion = 1;

    /// <summary>کمترین اطمینان برای استفاده از یک دانستنی.</summary>
    public const double MinConfidence = 0.3;

    /// <summary>پیشوند موضوع پیام‌هایی که از این جدول جواب گرفته‌اند (برای جمع 👍/👎). مثال: «دانش:3fa…».</summary>
    public const string TopicPrefix = "دانش:";

    /// <summary>الگوهای «X چیه؟». مثال: «ANC یعنی چی»، «منظور از میلی آمپر چیست».</summary>
    public static readonly string[] GlossaryCues =
    [
        "چیه", "چیست", "یعنی چی", "یعنی چه", "منظور از", "معنی", "معنای", "چی هست", "چه معنی", "تعریف", "یعنی چیه", "چی یعنی", "چه فرقی", "فرقش چیه", "فرق",
    ];

    /// <summary>الگوهای «راهنمای خرید». مثال: «برای خرید پاوربانک به چی دقت کنم».</summary>
    public static readonly string[] GuideCues =
    [
        "به چی دقت", "به چه چیزی دقت", "به چه دقت", "راهنمای خرید", "راهنمای انتخاب", "چطور انتخاب", "چجوری انتخاب", "چگونه انتخاب", "چه نکاتی", "نکات خرید",
        "نکته خرید", "موقع خرید", "هنگام خرید", "قبل از خرید", "چی بخرم", "کدوم بخرم", "کدام بخرم", "چه مدلی بخرم", "چی بگیرم", "مشاوره خرید",
    ];

    private static readonly Lazy<IReadOnlyList<BrainSeedRow>> CachedSeed = new(() => ParseSeed(Open()).ToList(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>همهٔ ردیف‌های بذر (یک بار خوانده می‌شود).</summary>
    public static IReadOnlyList<BrainSeedRow> Seed => CachedSeed.Value;

    private static readonly Lazy<IReadOnlyList<BrainFact>> SeedFacts = new(() => Seed.Select(FromSeed).ToList());

    /// <summary>دانستنی‌های بذر (وقتی دیتابیس هنوز بذر نخورده یا در تست‌ها).</summary>
    public static IReadOnlyList<BrainFact> FromSeedOnly => SeedFacts.Value;

    /// <summary>
    /// خواندن TSV بذر: «نوع ⇥ واژه ⇥ شکل‌ها ⇥ جواب ⇥ موضوع ⇥ وزن». خط خراب یا نوع ناشناخته نادیده گرفته می‌شود.
    /// </summary>
    public static IEnumerable<BrainSeedRow> ParseSeed(Stream stream, bool gzip = true)
    {
        using var source = gzip ? new GZipStream(stream, CompressionMode.Decompress) : stream;
        using var reader = new StreamReader(source, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            var parts = line.Split('\t');
            if (parts.Length < 6 || KindOf(parts[0]) is not { } kind || parts[1].Trim().Length == 0 || parts[3].Trim().Length == 0)
                continue;
            var weight = double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? Math.Clamp(w, 0, 1) : 1;
            yield return new BrainSeedRow(kind, parts[1].Trim(), parts[2].Trim().Length == 0 ? null : parts[2].Trim(), parts[3].Trim(),
                parts[4].Trim().Length == 0 ? null : parts[4].Trim(), weight);
        }
    }

    /// <summary>کد نوع فایل ← نوع. مثال: «guide» ← Guide.</summary>
    public static AssistantFactKind? KindOf(string code) => code.Trim() switch
    {
        "glossary" => AssistantFactKind.Glossary,
        "guide" => AssistantFactKind.Guide,
        "smalltalk" => AssistantFactKind.SmallTalk,
        "synonym" => AssistantFactKind.Synonym,
        "learned" => AssistantFactKind.Learned,
        _ => null,
    };

    private static Stream Open() =>
        typeof(AssistantBrain).Assembly.GetManifestResourceStream(ResourceName) ?? new MemoryStream(Gzip("# empty\n"));

    private static byte[] Gzip(string text)
    {
        using var memory = new MemoryStream();
        using (var gz = new GZipStream(memory, CompressionLevel.Fastest, leaveOpen: true))
            gz.Write(Encoding.UTF8.GetBytes(text));
        return memory.ToArray();
    }

    /// <summary>ردیف بذر ← دانستنی (اطمینان = وزن).</summary>
    public static BrainFact FromSeed(BrainSeedRow row) =>
        new(null, row.Kind, row.Term, Forms(row.Term, row.Aliases), row.Answer, null, row.Topic, row.Weight);

    /// <summary>ردیف دیتابیس ← دانستنی (اطمینان با شمارنده‌های 👍/👎).</summary>
    public static BrainFact FromEntity(AssistantFact fact) =>
        new(fact.Id, fact.Kind, fact.Term, Forms(fact.Term, fact.Aliases), fact.Answer, fact.Url, fact.Topic, Confidence(fact.Weight, fact.Helpful, fact.Unhelpful));

    /// <summary>
    /// اطمینان یادگیری (همان فرمول جدول دانش سئو): وزن × (H+4) / (H+4+1.5U).
    /// مثال: وزن ۱ بدون رأی ← ۱؛ ۱ 👍 و ۶ 👎 ← ۰٫۳۵؛ ۰ 👍 و ۱۰ 👎 ← ۰٫۲۱ (خاموش).
    /// </summary>
    public static double Confidence(double weight, int helpful, int unhelpful)
    {
        var h = Math.Max(0, helpful) + 4.0;
        return Math.Clamp(weight, 0, 1) * h / (h + 1.5 * Math.Max(0, unhelpful));
    }

    /// <summary>واژهٔ اصلی + شکل‌های دیگر (جداشده با «|»)، نرمال، بدون تکرار؛ بلندترها اول (تطبیق دقیق‌تر).</summary>
    public static IReadOnlyList<string> Forms(string term, string? aliases) =>
        new[] { term }.Concat((aliases ?? "").Split('|')).Select(Normalize).Where(f => f.Length >= 2)
            .Distinct(StringComparer.Ordinal).OrderByDescending(f => f.Length).ToList();

    /// <summary>
    /// نرمال‌سازی برای تطبیق: حروف عربی ← فارسی، نیم‌فاصله ← فاصله، حذف اعراب و نشانه‌ها، حروف کوچک لاتین، اعداد لاتین.
    /// مثال: «ANC يعني چی؟» ← «anc یعنی چی».
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            var c = ch switch
            {
                'ي' or 'ى' or 'ئ' => 'ی',
                'ك' => 'ک',
                'ة' or 'ۀ' => 'ه',
                'أ' or 'إ' or 'آ' => 'ا',
                'ؤ' => 'و',
                >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
                '\u200C' or '\u200F' or '\u200E' => ' ',
                _ => ch,
            };
            if (c is >= '\u064B' and <= '\u0655' or '\u0670')
                continue;
            if (char.IsPunctuation(c) || char.IsSymbol(c) || char.IsControl(c))
            {
                // «usb-c» و «m.2» یک واژه‌اند.
                if (c is '-' or '.' && builder.Length > 0 && char.IsLetterOrDigit(builder[^1]))
                {
                    builder.Append(c);
                    continue;
                }
                builder.Append(' ');
                continue;
            }
            builder.Append(char.ToLowerInvariant(c));
        }
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('-', '.');
    }

    /// <summary>
    /// آیا <paramref name="form"/> به‌صورت واژهٔ کامل در <paramref name="text"/> (هر دو نرمال) آمده؟
    /// مثال: («anc یعنی چی», «anc») ← true؛ («بالانس», «لانس») ← false.
    /// </summary>
    public static bool ContainsWord(string text, string form)
    {
        if (form.Length == 0 || text.Length < form.Length)
            return false;
        var index = 0;
        while ((index = text.IndexOf(form, index, StringComparison.Ordinal)) >= 0)
        {
            var before = index == 0 || text[index - 1] == ' ';
            var end = index + form.Length;
            var after = end == text.Length || text[end] == ' ';
            if (before && after)
                return true;
            index++;
        }
        return false;
    }

    /// <summary>پیام الگوی «X چیه؟» دارد؟</summary>
    public static bool AsksMeaning(string normalized) => GlossaryCues.Any(c => ContainsWord(normalized, Normalize(c)));

    /// <summary>نشانه‌های خرید/قیمت/موجودی (برای جدا کردن «قیمت X چیه» از «X چیه»). مثال: «قیمت پاوربانک چیه» ← true.</summary>
    public static bool AsksShopping(string normalized) => ShoppingCues.Any(c => ContainsWord(normalized, c));

    private static readonly string[] ShoppingCues = ["قیمت", "چند", "چنده", "موجود", "موجوده", "دارید", "داری", "میخوام", "میخواستم", "ارزان", "ارزون", "تخفیف دار", "سفارش", "سفارشم", "بخرم"];

    /// <summary>پیام الگوی «راهنمای خرید» دارد؟</summary>
    public static bool AsksGuide(string normalized) => GuideCues.Any(c => normalized.Contains(Normalize(c), StringComparison.Ordinal));

    /// <summary>
    /// بهترین دانستنی برای یک پیام (یا null). ترتیب:
    /// <list type="number">
    /// <item>راهنمای خرید: الگوی «به چی دقت کنم/راهنمای خرید» + نام دسته.</item>
    /// <item>واژه‌نامه: الگوی «چیه/یعنی چی» + واژه؛ یا پرسشی که فقط خودِ واژه است با علامت سؤال («ANC؟»).</item>
    /// <item>گپ کوتاه: پیام تقریباً همان عبارت است (حداکثر ۲ واژهٔ اضافه).</item>
    /// <item>یادگرفته: همهٔ واژه‌های عبارت در پیام هست.</item>
    /// </list>
    /// دانستنی با اطمینان کمتر از <see cref="MinConfidence"/> هرگز انتخاب نمی‌شود.
    /// </summary>
    public static BrainHit? Match(string? text, IReadOnlyList<BrainFact> facts)
    {
        var normalized = Normalize(text);
        if (normalized.Length < 2 || normalized.Length > 300)
            return null;
        var words = normalized.Split(' ').Length;
        var usable = facts.Where(f => f.Confidence >= MinConfidence).ToList();

        if (AsksGuide(normalized) && Best(usable, AssistantFactKind.Guide, normalized) is { } guide)
            return guide with { Score = 0.9 * guide.Fact.Confidence };

        // «قیمت پاوربانک چیه» پرسش خرید است نه معنی واژه؛ نشانهٔ خرید ← واژه‌نامه کنار می‌رود. پیام بلند هم معنی‌پرسی نیست.
        var meaning = AsksMeaning(normalized) && !AsksShopping(normalized);
        if (Best(usable, AssistantFactKind.Glossary, normalized) is { } glossary
            && (meaning && words <= glossary.Matched.Split(' ').Length + 4
                || text!.IndexOfAny(['؟', '?']) >= 0 && words <= glossary.Matched.Split(' ').Length + 1 && !AsksShopping(normalized)))
            return glossary with { Score = (meaning ? 0.9 : 0.7) * glossary.Fact.Confidence };

        foreach (var fact in usable.Where(f => f.Kind == AssistantFactKind.SmallTalk))
        {
            foreach (var form in fact.Forms)
            {
                if (ContainsWord(normalized, form) && words <= form.Split(' ').Length + 2)
                    return new BrainHit(fact, form, 0.85 * fact.Confidence);
            }
        }

        foreach (var fact in usable.Where(f => f.Kind == AssistantFactKind.Learned))
        {
            foreach (var form in fact.Forms)
            {
                var parts = form.Split(' ');
                if (parts.Length >= 2 && parts.All(p => ContainsWord(normalized, p)) || ContainsWord(normalized, form) && words <= parts.Length + 3)
                    return new BrainHit(fact, form, 0.8 * fact.Confidence);
            }
        }
        return null;
    }

    /// <summary>بلندترین شکلِ پیداشده از یک نوع (بلندتر = دقیق‌تر؛ «هارد اکسترنال» بر «هارد» مقدم است).</summary>
    private static BrainHit? Best(IEnumerable<BrainFact> facts, AssistantFactKind kind, string normalized)
    {
        BrainHit? best = null;
        foreach (var fact in facts.Where(f => f.Kind == kind))
        {
            foreach (var form in fact.Forms)
            {
                if ((best is null || form.Length > best.Matched.Length) && ContainsWord(normalized, form))
                    best = new BrainHit(fact, form, fact.Confidence);
            }
        }
        return best;
    }

    /// <summary>
    /// هم‌معنی‌ها ← واژهٔ اصلی (برای جست‌وجوی دوباره وقتی چیزی پیدا نشد). مثال: «لبتاپ ارزان» ← «لپ تاپ ارزان». تغییری نبود ← null.
    /// </summary>
    public static string? Canonical(string? query, IReadOnlyList<BrainFact> facts)
    {
        var normalized = Normalize(query);
        if (normalized.Length < 2)
            return null;
        foreach (var fact in facts.Where(f => f.Kind == AssistantFactKind.Synonym && f.Confidence >= MinConfidence))
        {
            var target = Normalize(fact.Term);
            foreach (var form in fact.Forms.Where(f => f != target))
            {
                if (ContainsWord(normalized, form))
                {
                    var replaced = (" " + normalized + " ").Replace(" " + form + " ", " " + target + " ", StringComparison.Ordinal).Trim();
                    return replaced == normalized ? null : replaced;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// گپ کوتاه چند جواب دارد (جداشده با «||»)؛ یکی با چرخش پایدار انتخاب می‌شود تا جواب‌ها تکراری و رباتی نباشند.
    /// مثال: Pick("الف||ب", 3) ← «ب».
    /// </summary>
    public static string Pick(string answer, int turn)
    {
        var options = answer.Split("||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return options.Length == 0 ? answer : options[Math.Abs(turn) % options.Length];
    }

    /// <summary>
    /// نکته‌های راهنمای خرید (جداشده با «؛») به‌صورت فهرست شماره‌دار. مثال: «الف؛ ب» ← «۱. الف\n۲. ب».
    /// </summary>
    public static IReadOnlyList<string> Tips(string answer) =>
        answer.Split('؛', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(t => t.Length > 0).ToList();
}
