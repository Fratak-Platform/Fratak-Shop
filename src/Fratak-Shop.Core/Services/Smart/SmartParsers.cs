using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>یک کالای پیدا شده در جست‌وجوی دیجی‌کالا. مثال: (12345، «هدفون بی‌سیم هایلو مدل T15»، «Haylou T15»).</summary>
public sealed record DigikalaHit(long Id, string Title, string? EnglishTitle);

/// <summary>
/// یک نتیجهٔ جست‌وجوی تصویر (دور بیستم): آدرس تصویر اصلی، عنوان کارت و صفحه‌ای که تصویر در آن است.
/// مثال: («https://shop.ir/img/t15.jpg»، «هدفون هایلو T15 مشکی»، «https://shop.ir/p/t15»).
/// </summary>
public sealed record ImageHit(string Url, string? Title, string? PageUrl);

/// <summary>جزئیات کالای دیجی‌کالا: ردیف‌های مشخصات و آدرس تصویرها.</summary>
public sealed record DigikalaDetails(IReadOnlyList<(string Key, string Value)> Specs, IReadOnlyList<string> Images);

/// <summary>
/// خواندن پاسخ منابع بیرونی برای محتوای هوشمند (B1–B13). همه «خالص» و بدون شبکه‌اند تا با نمونهٔ ثابت تست شوند.
/// <para>اصل ایمنی: هر پاسخ خراب/غیرمنتظره ← فهرست خالی (هرگز استثنا به بیرون نمی‌رود و دادهٔ ناقص ساخته نمی‌شود).</para>
/// <para>محل استفاده: Web/Infrastructure/Smart/SmartSources.cs.</para>
/// </summary>
public static partial class SmartParsers
{
    /// <summary>جست‌وجوی دیجی‌کالا (<c>api.digikala.com/v1/search</c>): data.products[] {id, title_fa, title_en}.</summary>
    public static List<DigikalaHit> DigikalaSearch(string? json)
    {
        var list = new List<DigikalaHit>();
        var root = Parse(json);
        if (root is not { } doc)
            return list;
        using (doc)
        {
            if (Path(doc.RootElement, "data", "products") is not { ValueKind: JsonValueKind.Array } products)
                return list;
            foreach (var p in products.EnumerateArray())
            {
                if (Prop(p, "id") is not { } idEl || !TryLong(idEl, out var id) || Str(p, "title_fa") is not { Length: > 2 } title)
                    continue;
                list.Add(new DigikalaHit(id, title.Trim(), Str(p, "title_en")?.Trim()));
            }
        }
        return list;
    }

    /// <summary>
    /// جزئیات کالای دیجی‌کالا (<c>api.digikala.com/v2/product/{id}/</c>): data.product.specifications[].attributes[]{title, values[]}
    /// و images.main.url[] / images.list[].url[]. ردیف‌های بی‌فایده (کد، شناسه، توضیح بلند) با <see cref="SpecMerge.IsUseful"/> حذف می‌شوند.
    /// </summary>
    public static DigikalaDetails DigikalaProduct(string? json)
    {
        var specs = new List<(string, string)>();
        var images = new List<string>();
        var root = Parse(json);
        if (root is not { } doc)
            return new DigikalaDetails(specs, images);
        using (doc)
        {
            var product = Path(doc.RootElement, "data", "product");
            if (product is not { ValueKind: JsonValueKind.Object } p)
                return new DigikalaDetails(specs, images);
            if (Prop(p, "specifications") is { ValueKind: JsonValueKind.Array } groups)
            {
                foreach (var group in groups.EnumerateArray())
                {
                    if (Prop(group, "attributes") is not { ValueKind: JsonValueKind.Array } attributes)
                        continue;
                    foreach (var attribute in attributes.EnumerateArray())
                    {
                        var key = Str(attribute, "title")?.Trim();
                        if (string.IsNullOrWhiteSpace(key) || Prop(attribute, "values") is not { ValueKind: JsonValueKind.Array } values)
                            continue;
                        var value = string.Join("، ", values.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String)
                            .Select(v => v.GetString()!.Trim()).Where(v => v.Length > 0).Take(6));
                        if (value.Length > 0 && value.Length <= 200 && SpecMerge.IsUseful(key, value))
                            specs.Add((key, value));
                    }
                }
            }
            if (Path(p, "images", "main", "url") is { ValueKind: JsonValueKind.Array } main)
                images.AddRange(main.EnumerateArray().Where(u => u.ValueKind == JsonValueKind.String).Select(u => u.GetString()!).Take(1));
            if (Path(p, "images", "list") is { ValueKind: JsonValueKind.Array } gallery)
            {
                foreach (var item in gallery.EnumerateArray())
                {
                    if (Prop(item, "url") is { ValueKind: JsonValueKind.Array } urls && urls.GetArrayLength() > 0 && urls[0].ValueKind == JsonValueKind.String)
                        images.Add(urls[0].GetString()!);
                }
            }
        }
        return new DigikalaDetails(specs.DistinctBy(s => SpecMerge.NormalizeKey(s.Item1)).Take(80).ToList(),
            images.Select(CleanImageUrl).Where(u => u is not null).Select(u => u!).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList());
    }

    /// <summary>
    /// نظرهای دیجی‌کالا (<c>v1/product/{id}/comments/?order=most_liked</c>): data.comments[]{title, body, rate, advantages[], disadvantages[], recommendation_status}.
    /// امتیاز دیجی‌کالا ۰ تا ۵ است؛ ۰ یعنی «بدون امتیاز».
    /// </summary>
    public static List<SourceReview> DigikalaComments(string? json)
    {
        var list = new List<SourceReview>();
        var root = Parse(json);
        if (root is not { } doc)
            return list;
        using (doc)
        {
            if (Path(doc.RootElement, "data", "comments") is not { ValueKind: JsonValueKind.Array } comments)
                return list;
            foreach (var c in comments.EnumerateArray())
            {
                var body = Str(c, "body");
                if (string.IsNullOrWhiteSpace(body) || body.Length < 8)
                    continue;
                int? rate = Prop(c, "rate") is { ValueKind: JsonValueKind.Number } r && r.TryGetInt32(out var rv) && rv is >= 1 and <= 5 ? rv : null;
                bool? recommends = Str(c, "recommendation_status") switch
                {
                    "recommended" => true,
                    "not_recommended" => false,
                    _ => null,
                };
                list.Add(new SourceReview("digikala", Str(c, "title"), Truncate(body, 1200), rate, Strings(c, "advantages"), Strings(c, "disadvantages"), recommends));
            }
        }
        return list.Take(60).ToList();
    }

    /// <summary>
    /// جدول مشخصات صفحهٔ HTML فروشگاه/تأمین‌کننده: ردیف‌های <c>&lt;tr&gt;&lt;th|td&gt;کلید&lt;/…&gt;&lt;td&gt;مقدار&lt;/td&gt;</c>
    /// و فهرست‌های <c>&lt;li&gt;&lt;span&gt;کلید&lt;/span&gt;&lt;span&gt;مقدار&lt;/span&gt;</c> (رایج در ووکامرس و فروشگاه‌ساز‌های ایرانی).
    /// </summary>
    public static List<(string Key, string Value)> HtmlSpecs(string? html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;
        if (html.Length > 1_500_000)
            html = html[..1_500_000];
        try
        {
            foreach (Match m in RowRegex().Matches(html))
                Add(list, m.Groups["k"].Value, m.Groups["v"].Value);
            foreach (Match m in SpanPairRegex().Matches(html))
                Add(list, m.Groups["k"].Value, m.Groups["v"].Value);
            // دور بیستم: فهرست تعریف <dl><dt>کلید</dt><dd>مقدار</dd> (رایج در قالب‌های فروشگاهی) و
            // داده‌های ساخت‌یافتهٔ JSON-LD (Product.additionalProperty) که خیلی از فروشگاه‌ها برای گوگل می‌گذارند.
            foreach (Match m in DefinitionRegex().Matches(html))
                Add(list, m.Groups["k"].Value, m.Groups["v"].Value);
            foreach (var (key, value) in JsonLdSpecs(html))
                Add(list, WebUtility.HtmlEncode(key), WebUtility.HtmlEncode(value));
        }
        catch (RegexMatchTimeoutException)
        {
            // بخشی که خوانده شد کافی است.
        }
        return list.DistinctBy(s => SpecMerge.NormalizeKey(s.Item1)).Take(60).ToList();

        static void Add(List<(string, string)> into, string rawKey, string rawValue)
        {
            var key = PersianText.Clean(PersianText.HtmlToText(rawKey)).Trim(':', ' ', '：');
            var value = PersianText.Clean(PersianText.HtmlToText(rawValue));
            if (key.Length is >= 2 and <= 60 && value.Length is >= 1 and <= 200 && SpecMerge.IsUseful(key, value))
                into.Add((key, value));
        }
    }

    /// <summary>تصویر اصلی صفحه از og:image یا twitter:image. مثال: «&lt;meta property="og:image" content="https://a.ir/x.jpg"&gt;» ← آن آدرس.</summary>
    public static string? OgImage(string? html, string? pageUrl = null)
    {
        if (string.IsNullOrEmpty(html))
            return null;
        try
        {
            var m = OgImageRegex().Match(html.Length > 400_000 ? html[..400_000] : html);
            if (!m.Success)
                return null;
            var raw = WebUtility.HtmlDecode(m.Groups["u"].Value).Trim();
            if (pageUrl is not null && Uri.TryCreate(pageUrl, UriKind.Absolute, out var basis) && Uri.TryCreate(basis, raw, out var absolute))
                raw = absolute.AbsoluteUri;
            return CleanImageUrl(raw);
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>
    /// نتایج جست‌وجوی تصویر بینگ (HTML): هر کارت <c>&lt;a class="iusc" m="{…&quot;murl&quot;:&quot;…&quot;…}"&gt;</c> آدرس تصویر اصلی (murl) را دارد.
    /// </summary>
    public static List<string> BingImages(string? html) => BingImageResults(html).Select(h => h.Url).ToList();

    /// <summary>
    /// دور بیستم: نتایج کامل جست‌وجوی تصویر بینگ – آدرس تصویر (murl)، عنوان کارت (t) و صفحهٔ منبع (purl).
    /// عنوان برای سنجش «همین کالاست؟» (TitleMatch) و صفحه برای امتیاز جداگانهٔ هر سایت استفاده می‌شود.
    /// <para>مثال m: {"murl":"https://a.ir/x.jpg","purl":"https://a.ir/p/1","t":"هدفون هایلو T15"} ← ImageHit(آدرس تصویر، عنوان، صفحه).</para>
    /// </summary>
    public static List<ImageHit> BingImageResults(string? html)
    {
        var list = new List<ImageHit>();
        if (string.IsNullOrEmpty(html))
            return list;
        try
        {
            foreach (Match m in BingCardRegex().Matches(html.Length > 1_500_000 ? html[..1_500_000] : html))
            {
                var meta = WebUtility.HtmlDecode(m.Groups["m"].Value);
                var root = Parse(meta);
                if (root is not { } doc)
                    continue;
                using (doc)
                {
                    if (Str(doc.RootElement, "murl") is { } url && CleanImageUrl(url) is { } clean)
                    {
                        var title = Str(doc.RootElement, "t") is { } t ? Truncate(PersianText.Clean(PersianText.HtmlToText(t)), 300) : null;
                        var page = Str(doc.RootElement, "purl") is { } purl && Uri.TryCreate(purl, UriKind.Absolute, out var pageUri)
                                   && pageUri.Scheme is "http" or "https" ? pageUri.AbsoluteUri : null;
                        list.Add(new ImageHit(clean, string.IsNullOrWhiteSpace(title) ? null : title, page));
                    }
                }
                if (list.Count >= 40)
                    break;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // همان نتایج خوانده‌شده.
        }
        return list.DistinctBy(h => h.Url, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// دور بیستم: تصویرهای کالا از داده‌های ساخت‌یافتهٔ JSON-LD صفحه (Product.image: رشته، آرایه یا ImageObject؛ داخل @graph هم).
    /// مثال: {"@type":"Product","image":["https://a.ir/1.jpg",{"@type":"ImageObject","url":"/2.jpg"}]} ← دو آدرس کامل.
    /// </summary>
    public static List<string> JsonLdImages(string? html, string? pageUrl = null)
    {
        var list = new List<string>();
        Uri? basis = pageUrl is not null && Uri.TryCreate(pageUrl, UriKind.Absolute, out var b) ? b : null;
        foreach (var block in JsonLdBlocks(html))
        {
            var root = Parse(block);
            if (root is not { } doc)
                continue;
            using (doc)
            {
                foreach (var product in Products(doc.RootElement, 0))
                {
                    if (Prop(product, "image") is not { } image)
                        continue;
                    foreach (var raw in ImageValues(image))
                    {
                        var value = WebUtility.HtmlDecode(raw).Trim();
                        if (basis is not null && Uri.TryCreate(basis, value, out var absolute))
                            value = absolute.AbsoluteUri;
                        if (CleanImageUrl(value) is { } clean)
                            list.Add(clean);
                    }
                }
            }
            if (list.Count >= 12)
                break;
        }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();

        static IEnumerable<string> ImageValues(JsonElement image)
        {
            switch (image.ValueKind)
            {
                case JsonValueKind.String:
                    yield return image.GetString()!;
                    break;
                case JsonValueKind.Object when (Str(image, "url") ?? Str(image, "contentUrl")) is { } url:
                    yield return url;
                    break;
                case JsonValueKind.Array:
                    foreach (var item in image.EnumerateArray().Take(12))
                    {
                        if (item.ValueKind == JsonValueKind.String)
                            yield return item.GetString()!;
                        else if (item.ValueKind == JsonValueKind.Object && (Str(item, "url") ?? Str(item, "contentUrl")) is { } itemUrl)
                            yield return itemUrl;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// دور بیستم: ردیف‌های مشخصات از JSON-LD (Product.additionalProperty[] با name و value؛ داخل @graph هم).
    /// مثال: {"@type":"PropertyValue","name":"وزن","value":"45 گرم"} ← («وزن»، «45 گرم»). عدد هم به متن تبدیل می‌شود.
    /// </summary>
    public static List<(string Key, string Value)> JsonLdSpecs(string? html)
    {
        var list = new List<(string, string)>();
        foreach (var block in JsonLdBlocks(html))
        {
            var root = Parse(block);
            if (root is not { } doc)
                continue;
            using (doc)
            {
                foreach (var product in Products(doc.RootElement, 0))
                {
                    if (Prop(product, "additionalProperty") is not { ValueKind: JsonValueKind.Array } properties)
                        continue;
                    foreach (var property in properties.EnumerateArray().Take(80))
                    {
                        var name = Str(property, "name");
                        var value = Prop(property, "value") is { } v
                            ? v.ValueKind switch
                            {
                                JsonValueKind.String => v.GetString(),
                                JsonValueKind.Number => v.GetRawText(),
                                JsonValueKind.True => "دارد",
                                JsonValueKind.False => "ندارد",
                                _ => null,
                            }
                            : null;
                        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
                            list.Add((PersianText.Clean(name), PersianText.Clean(value)));
                    }
                }
            }
        }
        return list.Take(80).ToList();
    }

    /// <summary>متن بلوک‌های &lt;script type="application/ld+json"&gt; (حداکثر ۱۰ بلوک، هر کدام ≤ ۲۰۰ کیلوبایت).</summary>
    private static List<string> JsonLdBlocks(string? html)
    {
        var blocks = new List<string>();
        if (string.IsNullOrEmpty(html))
            return blocks;
        try
        {
            foreach (Match m in JsonLdRegex().Matches(html.Length > 1_500_000 ? html[..1_500_000] : html))
            {
                var json = m.Groups["j"].Value.Trim();
                if (json.Length is > 1 and <= 200_000)
                    blocks.Add(json);
                if (blocks.Count >= 10)
                    break;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // همان بلوک‌های خوانده‌شده.
        }
        return blocks;
    }

    /// <summary>گره‌های @type=Product (یا آرایه‌ای که Product دارد) در ریشه، آرایه یا @graph؛ حداکثر عمق ۴.</summary>
    private static IEnumerable<JsonElement> Products(JsonElement element, int depth)
    {
        if (depth > 4)
            yield break;
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray().Take(50))
                foreach (var product in Products(item, depth + 1))
                    yield return product;
            yield break;
        }
        if (element.ValueKind != JsonValueKind.Object)
            yield break;
        if (IsProduct(Prop(element, "@type")))
            yield return element;
        if (Prop(element, "@graph") is { ValueKind: JsonValueKind.Array } graph)
            foreach (var product in Products(graph, depth + 1))
                yield return product;
        if (Prop(element, "mainEntity") is { ValueKind: JsonValueKind.Object } main)
            foreach (var product in Products(main, depth + 1))
                yield return product;

        static bool IsProduct(JsonElement? type) => type switch
        {
            { ValueKind: JsonValueKind.String } t => t.GetString() is "Product" or "ProductModel" or "IndividualProduct",
            { ValueKind: JsonValueKind.Array } a => a.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() is "Product" or "ProductModel"),
            _ => false,
        };
    }

    /// <summary>
    /// تصویرهای جست‌وجوی باسلام (<c>search.basalam.com … product/search</c>): products[].photo.LARGE یا MEDIUM.
    /// </summary>
    public static List<(string Title, string Image)> BasalamImages(string? json)
    {
        var list = new List<(string, string)>();
        var root = Parse(json);
        if (root is not { } doc)
            return list;
        using (doc)
        {
            if (Prop(doc.RootElement, "products") is not { ValueKind: JsonValueKind.Array } products)
                return list;
            foreach (var p in products.EnumerateArray())
            {
                var photo = Prop(p, "photo");
                var url = photo is { } ph ? Str(ph, "LARGE") ?? Str(ph, "MEDIUM") : null;
                if (Str(p, "name") is { } name && CleanImageUrl(url) is { } clean)
                    list.Add((name, clean));
            }
        }
        return list.Take(20).ToList();
    }

    /// <summary>
    /// آدرس تصویر امن و تمیز: فقط http/https، بدون data: و svg، و پارامترهای تغییر اندازهٔ دیجی‌کالا (?x-oss-process=…) حذف
    /// تا نسخهٔ بزرگ دریافت شود. مثال: «https://dkstatics-public.digikala.com/a.jpg?x-oss-process=image/resize,m_lfit,h_300» ← بدون پارامتر.
    /// </summary>
    public static string? CleanImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 1500)
            return null;
        url = url.Trim();
        if (url.StartsWith("//", StringComparison.Ordinal))
            url = "https:" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return null;
        if (uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            return null;
        if (uri.Query.Contains("x-oss-process", StringComparison.OrdinalIgnoreCase))
            return uri.GetLeftPart(UriPartial.Path);
        return uri.AbsoluteUri;
    }

    /// <summary>
    /// امتیاز شباهت عنوان پیدا شده با کالای ما (۰ تا ۱) بر پایهٔ واژه‌ها و عددهای مدل. عددهای مدل (مثل T15، 128GB) وزن بیشتری دارند؛
    /// کالایی که عدد مدل متفاوت دارد پذیرفته نمی‌شود. مثال: «Haylou T15» و «هدفون هایلو مدل T15» ← بالا؛ «Haylou T16» ← ۰.
    /// </summary>
    public static double TitleMatch(string ours, string found)
    {
        var a = Tokens(ours);
        var b = Tokens(found);
        if (a.Count == 0 || b.Count == 0)
            return 0;
        var modelA = a.Where(t => t.Any(char.IsDigit)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var modelB = b.Where(t => t.Any(char.IsDigit)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (modelA.Count > 0 && !modelA.Overlaps(modelB))
            return 0;
        var common = a.Intersect(b, StringComparer.OrdinalIgnoreCase).Count();
        return Math.Round((double)common / Math.Min(a.Count, b.Count), 3);
    }

    private static HashSet<string> Tokens(string text) =>
        PersianText.Normalize(text).ToLowerInvariant()
            .Split([' ', '-', '_', '/', '(', ')', '،', ',', '|', '«', '»', ':'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2 && !PersianText.IsStopWord(t) && t is not ("مدل" or "model" or "اورجینال" or "original"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // ─────────────────────────── ابزار JSON امن ───────────────────────────

    private static JsonDocument? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 5_000_000)
            return null;
        try
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>TryGetProperty روی غیرشیء استثنا می‌دهد؛ این نسخه امن است.</summary>
    private static JsonElement? Prop(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : null;

    private static JsonElement? Path(JsonElement element, params string[] names)
    {
        JsonElement? current = element;
        foreach (var name in names)
        {
            if (current is not { } c)
                return null;
            current = Prop(c, name);
        }
        return current;
    }

    private static string? Str(JsonElement element, string name) =>
        Prop(element, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    private static bool TryLong(JsonElement element, out long value)
    {
        value = 0;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt64(out value),
            JsonValueKind.String => long.TryParse(element.GetString(), out value),
            _ => false,
        };
    }

    private static List<string> Strings(JsonElement element, string name) =>
        Prop(element, name) is { ValueKind: JsonValueKind.Array } array
            ? array.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!.Trim()).Where(v => v.Length is > 1 and <= 200).Take(8).ToList()
            : [];

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    [GeneratedRegex(@"<tr[^>]*>\s*<t[hd][^>]*>(?<k>[^<]{2,80}(?:<[^/][^>]*>[^<]{0,80}</[^>]+>)?)\s*</t[hd]>\s*<td[^>]*>(?<v>.{1,400}?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex RowRegex();

    [GeneratedRegex(@"<li[^>]*>\s*<span[^>]*>(?<k>[^<]{2,60})</span>\s*<span[^>]*>(?<v>[^<]{1,200})</span>\s*</li>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SpanPairRegex();

    [GeneratedRegex(@"<dt[^>]*>\s*(?<k>[^<]{2,80})\s*</dt>\s*<dd[^>]*>(?<v>.{1,300}?)</dd>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex DefinitionRegex();

    [GeneratedRegex(@"<script[^>]+type\s*=\s*[""']application/ld\+json[""'][^>]*>(?<j>.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex JsonLdRegex();

    [GeneratedRegex(@"<meta[^>]+(?:property|name)\s*=\s*[""'](?:og:image(?::secure_url)?|twitter:image)[""'][^>]*content\s*=\s*[""'](?<u>[^""']{8,1500})[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 500)]
    private static partial Regex OgImageRegex();

    [GeneratedRegex(@"class=""iusc""[^>]*\sm=""(?<m>[^""]{10,6000})""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex BingCardRegex();
}
