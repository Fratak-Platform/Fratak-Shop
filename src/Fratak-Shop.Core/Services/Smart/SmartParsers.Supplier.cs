using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// دور ۳۱ (#۱) – «دستهٔ کالا در سایت تأمین‌کننده»: قانون پروژه این است که اگر سیستم خودش نتوانست کالا را دسته‌بندی کند، به صفحهٔ
/// تأمین‌کننده (SupplierUrl) می‌رود و دسته‌ای را که تأمین‌کننده گذاشته می‌خواند. این بخش فقط HTML را می‌خواند (خالص، بدون شبکه).
/// </summary>
public static partial class SmartParsers
{
    /// <summary>بخش‌هایی از مسیر که دسته نیستند (صفحهٔ اول، جست‌وجو، برچسب…). مقایسه بعد از یکسان‌سازی فارسی و حروف کوچک.</summary>
    private static readonly HashSet<string> SupplierPathNoise = new(StringComparer.Ordinal)
    {
        "خانه", "صفحه اصلی", "صفحه نخست", "فروشگاه", "فروشگاه اینترنتی", "محصولات", "همه محصولات", "همه کالاها", "کالاها", "جستجو", "جست و جو",
        "نتایج جستجو", "برچسب", "برچسب ها", "دسته بندی", "دسته بندی ها", "دسته ها", "بازگشت", "پیشنهاد ویژه", "حراج", "تخفیف ها", "جدیدترین ها",
        "home", "shop", "store", "products", "all products", "search", "category", "categories", "tag", "tags", "index", "main",
    };

    /// <summary>
    /// مسیر دستهٔ کالا در صفحهٔ تأمین‌کننده (از کلی به جزئی). منابع به ترتیب اطمینان:
    /// <list type="number">
    /// <item>JSON-LD از نوع BreadcrumbList (itemListElement به ترتیب position؛ نام از name یا item.name).</item>
    /// <item>فیلد category خود Product در JSON-LD (مثلاً «لوازم خانگی > آشپزخانه» یا «لوازم خانگی/آشپزخانه»).</item>
    /// <item>نوار مسیر HTML (هر عنصری که کلاسش breadcrumb دارد؛ ووکامرس، یوست، رنک‌مث و قالب‌های رایج ایرانی).</item>
    /// <item>«دسته:» ووکامرس (span.posted_in) – عمیق‌ترین پیوند product-category.</item>
    /// <item>متای product:category یا article:section.</item>
    /// </list>
    /// بخش‌های بی‌معنی («خانه»، «فروشگاه»، «جستجو»…)، نام خود کالا (آخرین بخش مسیر معمولاً نام کالاست)، بخش‌های خیلی بلند و
    /// تکراری حذف می‌شوند. حداکثر ۵ بخش. صفحهٔ خراب/بدون مسیر ← فهرست خالی (هرگز استثنا).
    /// مثال: مسیر «خانه › لوازم آشپزخانه › کتری و قوری › کتری برقی پارس خزر مدل KT-2000» برای کالای «کتری برقی پارس خزر مدل KT-2000»
    /// ← [لوازم آشپزخانه، کتری و قوری].
    /// </summary>
    public static IReadOnlyList<string> SupplierCategoryPath(string? html, string? productName)
    {
        if (string.IsNullOrWhiteSpace(html))
            return [];
        if (html.Length > 1_500_000)
            html = html[..1_500_000];
        try
        {
            foreach (var candidate in SupplierPathCandidates(html))
            {
                var clean = CleanSupplierPath(candidate, productName);
                if (clean.Count > 0)
                    return clean;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // صفحهٔ عجیب و خیلی بزرگ ← بدون مسیر.
        }
        return [];
    }

    /// <summary>
    /// تمیز کردن بخش‌های خام یک مسیر (خالص و آزمون‌پذیر). مثال: [«خانه»، «لوازم آشپزخانه»، «کتری برقی پارس خزر KT-2000»] با نام همان کالا
    /// ← [«لوازم آشپزخانه»].
    /// </summary>
    public static List<string> CleanSupplierPath(IEnumerable<string?> raw, string? productName)
    {
        var result = new List<string>();
        var product = PersianText.Normalize(PersianText.Clean(productName ?? "")).ToLowerInvariant();
        foreach (var item in raw)
        {
            if (string.IsNullOrWhiteSpace(item))
                continue;
            // «/» و «>» داخل نام بخش جداکنندهٔ مسیر ذخیره‌شده‌اند ← با «و» جایگزین می‌شوند («کتری/قوری» ← «کتری و قوری»).
            var text = PersianText.Clean(PersianText.HtmlToText(WebUtility.HtmlDecode(item)))
                .Replace('/', ' ').Replace('>', ' ').Replace('|', ' ').Replace('\\', ' ').Replace('»', ' ').Replace('›', ' ');
            text = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '-', '،', ',', '.', ':', '«', '»');
            if (text.Length is < 2 or > 50)
                continue;
            var normal = PersianText.Normalize(text).ToLowerInvariant();
            if (SupplierPathNoise.Contains(normal))
                continue;
            // نام خود کالا (یا بخش خیلی شبیه آن) دسته نیست.
            if (product.Length > 0 && (normal == product || (normal.Length >= 12 && TitleMatch(productName!, text) >= 0.75)))
                continue;
            // بخشی که عدد مدل/کد دارد معمولاً نام کالاست، نه دسته («کتری KT-2000»).
            if (text.Count(char.IsDigit) >= 3)
                continue;
            if (result.Count > 0 && PersianText.Normalize(result[^1]).ToLowerInvariant() == normal)
                continue;
            result.Add(text);
            if (result.Count == 5)
                break;
        }
        return result;
    }

    /// <summary>همهٔ مسیرهای خام صفحه به ترتیب اطمینان (اولین مسیر غیرخالی بعد از تمیز کردن برنده است).</summary>
    private static IEnumerable<List<string?>> SupplierPathCandidates(string html)
    {
        var fromJson = new List<List<string?>>();
        var categoryField = new List<List<string?>>();
        foreach (var block in JsonLdBlocks(html))
        {
            var root = Parse(block);
            if (root is not { } doc)
                continue;
            using (doc)
            {
                foreach (var crumbs in BreadcrumbLists(doc.RootElement, 0))
                    fromJson.Add(crumbs);
                foreach (var product in Products(doc.RootElement, 0))
                {
                    if (Str(product, "category") is { Length: > 1 } category)
                        categoryField.Add(category.Split(['>', '/', '|', '»', '›', '\\'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Cast<string?>().ToList());
                }
            }
        }
        foreach (var path in fromJson)
            yield return path;
        foreach (var path in categoryField)
            yield return path;

        // نوار مسیر HTML: محتوای اولین عنصر با کلاس breadcrumb (تا ۶ هزار نویسه).
        foreach (Match match in BreadcrumbOpenRegex().Matches(html))
        {
            var start = match.Index + match.Length;
            var chunk = html.Substring(start, Math.Min(6000, html.Length - start));
            var tag = match.Groups["tag"].Value;
            var close = chunk.IndexOf("</" + tag, StringComparison.OrdinalIgnoreCase);
            if (close > 0)
                chunk = chunk[..close];
            var parts = new List<string?>();
            foreach (Match link in AnchorTextRegex().Matches(chunk))
                parts.Add(link.Groups["t"].Value);
            // بخش آخر (صفحهٔ فعلی) معمولاً پیوند نیست: متن بعد از آخرین پیوند.
            var lastAnchor = chunk.LastIndexOf("</a>", StringComparison.OrdinalIgnoreCase);
            var tail = lastAnchor >= 0 ? chunk[(lastAnchor + 4)..] : chunk;
            var tailText = PersianText.Clean(PersianText.HtmlToText(tail)).Trim(' ', '/', '>', '»', '›', '|', '-');
            if (tailText.Length > 1)
                parts.Add(tailText);
            if (parts.Count > 0)
                yield return parts;
        }

        // «دسته:» ووکامرس – عمیق‌ترین پیوند دستهٔ کالا.
        if (PostedInRegex().Match(html) is { Success: true } postedIn)
        {
            var best = AnchorHrefRegex().Matches(postedIn.Groups["c"].Value)
                .Select(m => (Href: m.Groups["h"].Value, Text: m.Groups["t"].Value))
                .OrderByDescending(a => a.Href.Count(c => c == '/'))
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(best.Text))
                yield return [best.Text];
        }

        if (CategoryMetaRegex().Match(html) is { Success: true } meta)
            yield return meta.Groups["c"].Value.Split(['>', '/', '|', '»', '›'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Cast<string?>().ToList();
    }

    /// <summary>گره‌های BreadcrumbList (ریشه، آرایه، @graph؛ حداکثر عمق ۴) ← نام بخش‌ها به ترتیب position.</summary>
    private static IEnumerable<List<string?>> BreadcrumbLists(JsonElement element, int depth)
    {
        if (depth > 4)
            yield break;
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray().Take(30))
                foreach (var found in BreadcrumbLists(item, depth + 1))
                    yield return found;
                break;
            case JsonValueKind.Object:
                if (IsType(element, "BreadcrumbList") && Prop(element, "itemListElement") is { ValueKind: JsonValueKind.Array } items)
                {
                    var ordered = items.EnumerateArray().Take(12)
                        .Where(i => i.ValueKind == JsonValueKind.Object)
                        .Select((i, index) => (Position: Prop(i, "position") is { ValueKind: JsonValueKind.Number } p && p.TryGetInt32(out var n) ? n : index + 1,
                            Name: Str(i, "name") ?? (Prop(i, "item") is { ValueKind: JsonValueKind.Object } inner ? Str(inner, "name") : null)))
                        .OrderBy(x => x.Position)
                        .Select(x => x.Name)
                        .ToList();
                    if (ordered.Count > 0)
                        yield return ordered;
                }
                if (Prop(element, "@graph") is { } graph)
                {
                    foreach (var found in BreadcrumbLists(graph, depth + 1))
                        yield return found;
                }
                if (Prop(element, "breadcrumb") is { ValueKind: JsonValueKind.Object } nested)
                {
                    foreach (var found in BreadcrumbLists(nested, depth + 1))
                        yield return found;
                }
                break;
        }
    }

    /// <summary>آیا @type این گره (متن یا آرایه) همان نوع است؟</summary>
    private static bool IsType(JsonElement element, string type)
    {
        if (Prop(element, "@type") is not { } value)
            return false;
        return value.ValueKind switch
        {
            JsonValueKind.String => string.Equals(value.GetString(), type, StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => value.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.String && string.Equals(v.GetString(), type, StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    [GeneratedRegex(@"<(?<tag>nav|div|ol|ul|p|span|section)\b[^>]*\bclass\s*=\s*[""'][^""']*breadcrumb[^""']*[""'][^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex BreadcrumbOpenRegex();

    [GeneratedRegex(@"<a\b[^>]*>(?<t>.{1,200}?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex AnchorTextRegex();

    [GeneratedRegex(@"<a\b[^>]*href\s*=\s*[""'](?<h>[^""']{1,500})[""'][^>]*>(?<t>.{1,200}?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex AnchorHrefRegex();

    [GeneratedRegex(@"<span[^>]*class\s*=\s*[""'][^""']*posted_in[^""']*[""'][^>]*>(?<c>.{1,3000}?)</span>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex PostedInRegex();

    [GeneratedRegex(@"<meta[^>]+(?:property|name)\s*=\s*[""'](?:product:category|article:section)[""'][^>]*content\s*=\s*[""'](?<c>[^""']{2,200})[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 500)]
    private static partial Regex CategoryMetaRegex();
}
