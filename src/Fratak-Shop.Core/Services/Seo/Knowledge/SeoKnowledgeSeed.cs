using System.Globalization;
using System.IO.Compression;
using System.Text;
using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Core.Services.Seo.Knowledge;

/// <summary>
/// یک ردیف دادهٔ اولیهٔ دانش (از فایل جاسازی‌شده). Known = واژه‌ای که فقط برای «شناخته‌شده بودن» در املا لازم است
/// و در دیتابیس ذخیره نمی‌شود (حدود ۱۶۶ هزار واژهٔ کم‌بسامد واژه‌نامه).
/// </summary>
public sealed record SeoSeedRow(SeoKnowledgeKind Kind, string Term, string? Value, string Topic, double Weight, bool Known = false);

/// <summary>
/// خوانندهٔ دادهٔ اولیهٔ جدول دانش سئو (مرحلهٔ شانزدهم).
/// <para>فایل: Core/Services/Seo/Knowledge/Data/seo-knowledge.tsv.gz (ساختهٔ tools/seo-knowledge/build_seed.py از واژه‌نامهٔ
/// متن‌باز hazm با مجوز MIT + فهرست‌های دست‌نوشته). هر خط: «نوع ⇥ واژه ⇥ مقدار ⇥ موضوع ⇥ وزن».</para>
/// <para>استفاده: <see cref="SeoKnowledgeSeeder"/> (ریختن در دیتابیس) و <see cref="SeoKnowledgeBase.FromSeed"/> (وقتی دیتابیس
/// هنوز بذر نخورده یا در آزمون‌های واحد).</para>
/// </summary>
public static class SeoKnowledgeSeed
{
    /// <summary>
    /// نسخهٔ دادهٔ اولیه. هر بار فهرست‌ها یا واژه‌نامه عوض شد یکی بالا ببرید تا سرورها ردیف‌های تازه را اضافه کنند
    /// (شمارنده‌های یادگیری ردیف‌های قبلی دست نمی‌خورد).
    /// </summary>
    public const int SeedVersion = 1;

    /// <summary>نام منبع جاسازی‌شده (LogicalName در Fratak-Shop.Core.csproj).</summary>
    public const string ResourceName = "Fratak_Shop.Core.SeoKnowledge.tsv.gz";

    private static readonly Lazy<IReadOnlyList<SeoSeedRow>> Cached = new(() => Parse(OpenResource()).ToList(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>همهٔ ردیف‌ها (یک بار خوانده و در حافظه نگه داشته می‌شود؛ حدود ۲۰۰ هزار ردیف کوچک).</summary>
    public static IReadOnlyList<SeoSeedRow> Rows => Cached.Value;

    /// <summary>ردیف‌هایی که در دیتابیس ذخیره می‌شوند (همه به‌جز Known).</summary>
    public static IEnumerable<SeoSeedRow> StoredRows => Rows.Where(r => !r.Known);

    /// <summary>
    /// کد نوع در فایل → نوع ردیف. مثال: «correction» → <see cref="SeoKnowledgeKind.Correction"/>.
    /// «known» = واژهٔ شناخته‌شدهٔ کم‌بسامد (Kind = Word و Known = true).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, SeoKnowledgeKind> KindCodes = new Dictionary<string, SeoKnowledgeKind>(StringComparer.Ordinal)
    {
        ["word"] = SeoKnowledgeKind.Word,
        ["known"] = SeoKnowledgeKind.Word,
        ["compound"] = SeoKnowledgeKind.Compound,
        ["verb"] = SeoKnowledgeKind.Verb,
        ["stop"] = SeoKnowledgeKind.StopWord,
        ["informal"] = SeoKnowledgeKind.Informal,
        ["abbr"] = SeoKnowledgeKind.Abbreviation,
        ["correction"] = SeoKnowledgeKind.Correction,
        ["style"] = SeoKnowledgeKind.Style,
        ["unit"] = SeoKnowledgeKind.Unit,
        ["number"] = SeoKnowledgeKind.Number,
        ["modifier"] = SeoKnowledgeKind.Modifier,
        ["power"] = SeoKnowledgeKind.PowerWord,
        ["spam"] = SeoKnowledgeKind.Spam,
        ["transition"] = SeoKnowledgeKind.Transition,
        ["cta"] = SeoKnowledgeKind.CallToAction,
        ["benefit"] = SeoKnowledgeKind.Benefit,
        ["heading"] = SeoKnowledgeKind.Heading,
        ["question"] = SeoKnowledgeKind.Question,
        ["synonym"] = SeoKnowledgeKind.Synonym,
    };

    /// <summary>
    /// خواندن TSV (فشرده یا نه). خط خالی و خط با نوع ناشناخته نادیده گرفته می‌شود تا فایل خراب برنامه را از کار نیندازد.
    /// مثال خط: «correction ⇥ اطاق ⇥ اتاق ⇥ املا ⇥ 1».
    /// </summary>
    public static IEnumerable<SeoSeedRow> Parse(Stream stream, bool gzip = true)
    {
        using var source = gzip ? new GZipStream(stream, CompressionMode.Decompress) : stream;
        using var reader = new StreamReader(source, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            var parts = line.Split('\t');
            if (parts.Length < 2 || !KindCodes.TryGetValue(parts[0], out var kind) || string.IsNullOrWhiteSpace(parts[1]))
                continue;
            var value = parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null;
            var topic = parts.Length > 3 ? parts[3] : "";
            var weight = parts.Length > 4 && double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : 1;
            yield return new SeoSeedRow(kind, Cut(parts[1], 200), value is null ? null : Cut(value, 1000), Cut(topic, 100), weight, parts[0] == "known");
        }
    }

    private static Stream OpenResource() =>
        typeof(SeoKnowledgeSeed).Assembly.GetManifestResourceStream(ResourceName)
        ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing from Fratak-Shop.Core.");

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}
