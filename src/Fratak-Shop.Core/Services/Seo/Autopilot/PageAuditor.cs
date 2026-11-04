using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>یک تصویر صفحه در خزش.</summary>
public sealed record AuditImage(string Src, string? Alt, bool HasSize, bool Lazy);

/// <summary>یک لینک صفحه در خزش.</summary>
public sealed record AuditLink(string Href, string Text, bool Internal, bool NoFollow);

/// <summary>
/// نتیجهٔ بررسی یک صفحهٔ کامل HTML (همان چیزی که ربات گوگل می‌بیند).
/// </summary>
public sealed class PageAudit
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Canonical { get; set; }
    public bool NoIndex { get; set; }
    public List<string> H1 { get; } = [];
    public List<HtmlHeading> Headings { get; } = [];
    public int Words { get; set; }
    public List<AuditImage> Images { get; } = [];
    public List<AuditLink> Links { get; } = [];
    public List<string> JsonLd { get; } = [];
    public List<string> SchemaTypes { get; } = [];
    public int SchemaErrors { get; set; }
    public bool HasOgImage { get; set; }
    public bool HasViewport { get; set; }
    public bool HasLang { get; set; }
    public int Comments { get; set; }
    public int MixedContent { get; set; }
    public long SimHash { get; set; }

    /// <summary>کد مشکل‌ها (کلید <see cref="SeoIssueCatalog"/>). مثال: <c>["title-missing","img-alt"]</c>.</summary>
    public List<string> Issues { get; } = [];
}

/// <summary>
/// بازرس صفحه (موارد ۵۶ تا ۸۰ و ۲۰۶ پیشنهاد سئو): HTML نهایی یک صفحه را مثل ابزارهای Screaming Frog/Ahrefs بررسی می‌کند.
/// <para>کجا استفاده می‌شود؟ خزش شبانه (<c>SeoCrawlService</c>) و تست‌های خودکار خروجی صفحه‌ها.</para>
/// <example><code>
/// var audit = PageAuditor.Audit(html, "/product/sony-wh", "shop.fratak.ir");
/// if (audit.Issues.Contains("h1-missing")) ...
/// </code></example>
/// </summary>
public static class PageAuditor
{
    /// <summary>بررسی کامل. HTML خالی ← فقط مشکل «empty».</summary>
    public static PageAudit Audit(string? html, string path, string? host)
    {
        var audit = new PageAudit();
        if (string.IsNullOrWhiteSpace(html))
        {
            audit.Issues.Add("empty");
            return audit;
        }

        var document = new HtmlParser().ParseDocument(html);
        var head = document.Head;
        audit.Title = Clean(document.Title);
        audit.Description = Clean(head?.QuerySelector("meta[name=description]")?.GetAttribute("content"));
        audit.Canonical = head?.QuerySelector("link[rel=canonical]")?.GetAttribute("href");
        var robots = (head?.QuerySelector("meta[name=robots]")?.GetAttribute("content") ?? "").ToLowerInvariant();
        audit.NoIndex = robots.Contains("noindex", StringComparison.Ordinal);
        audit.HasOgImage = head?.QuerySelector("meta[property='og:image']") is not null;
        audit.HasViewport = head?.QuerySelector("meta[name=viewport]") is not null;
        audit.HasLang = !string.IsNullOrWhiteSpace(document.DocumentElement.GetAttribute("lang"));
        audit.Comments = document.Descendants().Count(n => n.NodeType == NodeType.Comment);

        var body = document.Body;
        if (body is not null)
        {
            foreach (var heading in body.QuerySelectorAll("h1,h2,h3,h4,h5,h6"))
            {
                var text = Clean(heading.TextContent) ?? "";
                var level = heading.LocalName[1] - '0';
                audit.Headings.Add(new HtmlHeading(level, text));
                if (level == 1)
                    audit.H1.Add(text);
            }

            var main = body.QuerySelector("main") ?? body;
            var textOnly = main.Clone(true) as IElement;
            foreach (var junk in textOnly!.QuerySelectorAll("script,style,noscript,svg,template").ToList())
                junk.Remove();
            audit.Words = PersianText.WordCount(textOnly.TextContent);
            audit.SimHash = SeoPenaltyGuard.SimHash(textOnly.TextContent);

            foreach (var img in body.QuerySelectorAll("img"))
            {
                audit.Images.Add(new AuditImage(img.GetAttribute("src") ?? img.GetAttribute("data-src") ?? "",
                    img.GetAttribute("alt"),
                    img.HasAttribute("width") && img.HasAttribute("height"),
                    string.Equals(img.GetAttribute("loading"), "lazy", StringComparison.OrdinalIgnoreCase)));
            }

            foreach (var a in body.QuerySelectorAll("a[href]"))
            {
                var href = a.GetAttribute("href")!.Trim();
                if (href.StartsWith('#') || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                    || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
                    continue;
                var rel = (a.GetAttribute("rel") ?? "").ToLowerInvariant();
                audit.Links.Add(new AuditLink(href, Clean(a.TextContent) ?? Clean(a.GetAttribute("aria-label")) ?? "",
                    IsInternal(href, host), rel.Contains("nofollow", StringComparison.Ordinal)));
            }
        }

        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            var json = script.TextContent;
            audit.JsonLd.Add(json);
            var result = SchemaValidator.Validate(json);
            audit.SchemaTypes.AddRange(result.Types);
            audit.SchemaErrors += result.Errors.Count;
        }

        audit.MixedContent = document.QuerySelectorAll("img[src^='http:'],script[src^='http:'],link[href^='http:'][rel=stylesheet],iframe[src^='http:']").Length;
        Diagnose(audit, path);
        return audit;
    }

    private static void Diagnose(PageAudit audit, string path)
    {
        var issues = audit.Issues;
        if (string.IsNullOrWhiteSpace(audit.Title)) issues.Add("title-missing");
        else if (audit.Title.Length > 65) issues.Add("title-long");
        else if (audit.Title.Length < 20) issues.Add("title-short");

        if (!audit.NoIndex)
        {
            if (string.IsNullOrWhiteSpace(audit.Description)) issues.Add("meta-missing");
            else if (audit.Description.Length > 170) issues.Add("meta-long");
            else if (audit.Description.Length < 70) issues.Add("meta-short");
            if (string.IsNullOrWhiteSpace(audit.Canonical)) issues.Add("canonical-missing");
            if (audit.H1.Count == 0) issues.Add("h1-missing");
            if (audit.H1.Count > 1) issues.Add("h1-multiple");
            if (audit.Words < 150 && IsContentPath(path)) issues.Add("thin-content");
            if (!audit.HasOgImage) issues.Add("og-image-missing");
        }

        if (audit.Images.Any(i => string.IsNullOrWhiteSpace(i.Alt))) issues.Add("img-alt");
        if (audit.Images.Any(i => IsLegacyImage(i.Src))) issues.Add("img-legacy-format");
        if (audit.Images.Count(i => !i.HasSize) > 0) issues.Add("img-size");
        if (HeadingSkips(audit.Headings)) issues.Add("heading-order");
        if (audit.Comments > 0) issues.Add("html-comments");
        if (audit.MixedContent > 0) issues.Add("mixed-content");
        if (audit.SchemaErrors > 0) issues.Add("schema-error");
        if (!audit.HasViewport) issues.Add("viewport-missing");
        if (!audit.HasLang) issues.Add("lang-missing");
        if (audit.Links.Any(l => l.Internal && l.Text.Length == 0)) issues.Add("link-empty-text");
        if (audit.Links.Any(l => l.Internal && l.NoFollow)) issues.Add("internal-nofollow");
    }

    /// <summary>صفحه‌هایی که «محتوای اصلی» دارند (کم بودن متنشان مهم است).</summary>
    public static bool IsContentPath(string path) =>
        path.StartsWith("/product/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/blog/", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWith("/blog/category/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/page/", StringComparison.OrdinalIgnoreCase);

    /// <summary>آیا آدرس تصویر هنوز JPG/PNG/GIF است؟ (هدف: فقط WebP؛ SVG آیکون برداری است و مجاز است).</summary>
    public static bool IsLegacyImage(string src)
    {
        if (string.IsNullOrWhiteSpace(src) || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return false;
        var clean = src.Split('?', '#')[0];
        return clean.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || clean.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
               || clean.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || clean.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
               || clean.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>پرش در ترتیب تیترها (مثلاً H2 بعد مستقیم H4).</summary>
    public static bool HeadingSkips(IReadOnlyList<HtmlHeading> headings)
    {
        var previous = 0;
        foreach (var heading in headings)
        {
            if (previous > 0 && heading.Level > previous + 1)
                return true;
            previous = heading.Level;
        }

        return false;
    }

    /// <summary>لینک داخلی است؟ (نسبی، یا مطلق به همین دامنه)</summary>
    public static bool IsInternal(string href, string? host)
    {
        if (href.StartsWith('/') && !href.StartsWith("//", StringComparison.Ordinal))
            return true;
        if (!Uri.TryCreate(href.StartsWith("//", StringComparison.Ordinal) ? "https:" + href : href, UriKind.Absolute, out var uri))
            return !href.Contains(':', StringComparison.Ordinal);
        return !string.IsNullOrWhiteSpace(host) && (string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase)
                                                    || string.Equals(uri.Host, "www." + host, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>نتیجهٔ بررسی یک بلوک JSON-LD.</summary>
public sealed record SchemaCheck(IReadOnlyList<string> Types, IReadOnlyList<string> Errors);

/// <summary>
/// اعتبارسنج دادهٔ ساختاریافته (موارد ۸۱ تا ۹۵): فیلدهای «الزامی» گوگل برای هر نوع را بررسی می‌کند
/// (همان چیزی که Rich Results Test خطا می‌گیرد). مثال: Product بدون offers/review/aggregateRating ← خطا.
/// </summary>
public static class SchemaValidator
{
    private static readonly Dictionary<string, string[]> Required = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Product"] = ["name", "image"],
        ["Offer"] = ["price", "priceCurrency"],
        ["BlogPosting"] = ["headline", "datePublished", "author"],
        ["Article"] = ["headline", "datePublished", "author"],
        ["BreadcrumbList"] = ["itemListElement"],
        ["FAQPage"] = ["mainEntity"],
        ["Question"] = ["name", "acceptedAnswer"],
        ["Organization"] = ["name"],
        ["OnlineStore"] = ["name"],
        ["WebSite"] = ["name", "url"],
        ["ItemList"] = ["itemListElement"],
        ["Review"] = ["author", "reviewRating"],
        ["AggregateRating"] = ["ratingValue"],
        ["VideoObject"] = ["name", "thumbnailUrl", "uploadDate"],
        ["HowTo"] = ["name", "step"],
    };

    /// <summary>بررسی یک رشتهٔ JSON-LD (یک شیء، آرایه یا @graph). JSON خراب ← خطای «invalid-json».</summary>
    public static SchemaCheck Validate(string? json)
    {
        var types = new List<string>();
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(json))
            return new SchemaCheck(types, errors);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            Walk(document.RootElement, types, errors, 0, top: true);
        }
        catch (JsonException)
        {
            errors.Add("invalid-json");
        }

        return new SchemaCheck(types, errors);
    }

    private static void Walk(JsonElement element, List<string> types, List<string> errors, int depth, bool top)
    {
        if (depth > 12)
            return;
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                Walk(item, types, errors, depth + 1, top);
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (element.TryGetProperty("@type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
        {
            var type = typeElement.GetString()!;
            if (top)
                types.Add(type);
            if (Required.TryGetValue(type, out var fields))
            {
                foreach (var field in fields.Where(f => !HasValue(element, f)))
                    errors.Add($"{type}.{field}");
            }

            if (type.Equals("Product", StringComparison.OrdinalIgnoreCase)
                && !HasValue(element, "offers") && !HasValue(element, "review") && !HasValue(element, "aggregateRating"))
                errors.Add("Product.offers|review|aggregateRating");
        }

        foreach (var property in element.EnumerateObject())
        {
            // فقط اعضای @graph «نوع اصلی» صفحه حساب می‌شوند؛ بقیه (offers، author ...) نوع فرعی‌اند.
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                Walk(property.Value, types, errors, depth + 1, top && property.Name == "@graph");
        }
    }

    private static bool HasValue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => false,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
            JsonValueKind.Array => value.GetArrayLength() > 0,
            _ => true,
        };
}
