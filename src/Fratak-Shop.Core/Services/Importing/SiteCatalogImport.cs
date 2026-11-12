using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Suppliers;

namespace Fratak_Shop.Core.Services.Importing;

// ═══════════════════════════════════════════════════════════════════════════
//  درون‌ریزی گروهی محصولات از یک سایت دیگر (دور ۱۰)
//   • SiteCatalogService : تشخیص نوع سایت و خواندن همهٔ محصولات (عنوان، توضیح، مشخصات، قیمت، آدرس تصاویر).
//   • SiteCatalogParsers : خواندن خالص پاسخ‌ها (ووکامرس، شاپیفای، JSON-LD، نقشهٔ سایت) – تست‌پذیر بدون اینترنت.
//  ساختن محصول و دانلود تصاویر (ذخیرهٔ محلی، هیچ‌وقت لینک مستقیم به سایت مبدأ) در صفحهٔ پنل انجام می‌شود:
//  Web/Areas/Admin/Pages/Products/SiteImport.cshtml.cs
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// یک محصول خوانده‌شده از سایت مبدأ (هنوز در فروشگاه ما ذخیره نشده).
/// </summary>
/// <param name="SourceUrl">آدرس صفحهٔ محصول در سایت مبدأ (به‌عنوان «لینک منبع خرید» ذخیره می‌شود تا بررسی تأمین‌کننده هم کار کند).</param>
/// <param name="Name">نام محصول. مثال: «هدفون بی‌سیم هایلو مدل T15».</param>
/// <param name="Sku">کد کالا در سایت مبدأ (به‌عنوان «کد تأمین‌کننده» ذخیره می‌شود).</param>
/// <param name="ShortDescription">توضیح کوتاه (متن ساده).</param>
/// <param name="Description">توضیح کامل (HTML خام؛ پیش از ذخیره با <see cref="SiteCatalogParsers.CleanDescription"/> و پاک‌ساز XSS تمیز می‌شود).</param>
/// <param name="Price">قیمت به تومان (ریال تبدیل شده)؛ null یعنی قیمت اعلام نشده.</param>
/// <param name="InStock">در سایت مبدأ موجود است.</param>
/// <param name="Brand">نام برند (برای نمایش در پیش‌نمایش).</param>
/// <param name="Category">نام دسته در سایت مبدأ (برای نمایش در پیش‌نمایش).</param>
/// <param name="Attributes">مشخصات فنی: (عنوان، مقدار). مثال: («رنگ»، «مشکی، سفید»).</param>
/// <param name="Images">آدرس کامل تصاویر (حداکثر ۱۰)؛ فقط برای دانلود، هیچ‌وقت مستقیم نمایش داده نمی‌شوند.</param>
public sealed record ImportedProduct(
    string SourceUrl,
    string Name,
    string? Sku,
    string? ShortDescription,
    string? Description,
    decimal? Price,
    bool InStock,
    string? Brand,
    string? Category,
    IReadOnlyList<KeyValuePair<string, string>> Attributes,
    IReadOnlyList<string> Images);

/// <summary>
/// نتیجهٔ خواندن یک سایت.
/// </summary>
/// <param name="Platform">نوع سایت به فارسی. مثال: «ووکامرس (وردپرس)».</param>
/// <param name="BaseUrl">نشانی اصلی سایت. مثال: https://example.com</param>
/// <param name="Products">محصولات خوانده‌شده.</param>
/// <param name="Problems">هشدارها و خطاها به فارسی.</param>
/// <param name="Truncated">به سقف تعداد رسیدیم و ممکن است محصولات بیشتری باشد.</param>
public sealed record SiteCatalog(string Platform, string BaseUrl, IReadOnlyList<ImportedProduct> Products, IReadOnlyList<string> Problems, bool Truncated);

/// <summary>
/// خواندن همهٔ محصولات یک سایت فروشگاهی.
/// <para>کجا استفاده می‌شود: پنل ← محصولات ← «درون‌ریزی از سایت».</para>
/// </summary>
public interface ISiteCatalogService
{
    /// <summary>
    /// تشخیص نوع سایت و خواندن محصولات. هیچ‌وقت استثنا نمی‌دهد؛ مشکل‌ها در <see cref="SiteCatalog.Problems"/> می‌آیند.
    /// <para>مثال: «https://shop.example» ← ووکامرس، ۲۳۰ محصول.</para>
    /// </summary>
    /// <param name="url">نشانی سایت (صفحهٔ اصلی) یا نشانی یک صفحهٔ محصول.</param>
    /// <param name="maxProducts">سقف تعداد (۱ تا ۵۰۰).</param>
    Task<SiteCatalog> DiscoverAsync(string url, int maxProducts, CancellationToken cancellationToken);
}

/// <summary>
/// پیاده‌سازی «هوشمند» با سه روش، به ترتیب (اولین روشی که جواب داد):
/// <list type="number">
/// <item><b>ووکامرس</b> (بیشتر فروشگاه‌های ایرانی): API عمومی فروشگاه <c>/wp-json/wc/store/v1/products</c> بدون نیاز به کلید.</item>
/// <item><b>شاپیفای</b>: <c>/products.json</c>.</item>
/// <item><b>هر سایت دیگر</b>: نقشهٔ سایت (robots.txt ← sitemap) ← صفحه‌های محصول ← دادهٔ ساختاریافتهٔ Product (JSON-LD) که
/// سایت‌ها برای گوگل می‌گذارند.</item>
/// </list>
/// اگر نشانی یک صفحهٔ محصول داده شود، همان صفحه با JSON-LD خوانده می‌شود.
/// <para>همهٔ درخواست‌ها از کلاینت امن تأمین‌کننده (<see cref="ISupplierHttp"/>: محافظ SSRF، سقف حجم و زمان) می‌روند.</para>
/// </summary>
public sealed class SiteCatalogService(ISupplierHttp http) : ISiteCatalogService
{
    /// <summary>بیشترین تعداد صفحهٔ HTML که در روش نقشهٔ سایت خوانده می‌شود (هر صفحه یک درخواست).</summary>
    public const int MaxHtmlPages = 60;

    /// <summary>فاصلهٔ بین درخواست‌های پشت‌سرهم تا سایت مبدأ اذیت/مسدود نشود.</summary>
    internal static TimeSpan Pause { get; set; } = TimeSpan.FromMilliseconds(250);

    public async Task<SiteCatalog> DiscoverAsync(string url, int maxProducts, CancellationToken cancellationToken)
    {
        maxProducts = Math.Clamp(maxProducts, 1, 500);
        url = (url ?? "").Trim();
        if (!url.Contains("://", StringComparison.Ordinal))
            url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host))
            return Empty("", "نشانی سایت معتبر نیست. مثال: https://shop.example.com");

        var baseUrl = uri.GetLeftPart(UriPartial.Authority);
        var problems = new List<string>();

        // ۱) نشانی یک صفحهٔ محصول؟
        if (uri.AbsolutePath.Length > 1)
        {
            var page = await http.GetAsync(uri.AbsoluteUri, expectJson: false, cancellationToken);
            if (Ok(page))
            {
                var single = SafeJsonLd(page.Body!, uri.AbsoluteUri);
                if (single.Count > 0)
                    return new SiteCatalog("صفحهٔ محصول (دادهٔ ساختاریافته)", baseUrl, single.Take(maxProducts).ToList(), problems, false);
            }
        }

        // ۲) ووکامرس
        var woo = await PagedAsync(page => $"{baseUrl}/wp-json/wc/store/v1/products?per_page=50&page={page}", 50,
            body => SiteCatalogParsers.Woo(body, baseUrl), maxProducts, cancellationToken);
        if (woo.Items is not null)
            return new SiteCatalog("ووکامرس (وردپرس)", baseUrl, woo.Items, problems, woo.Truncated);

        // ۳) شاپیفای
        var shopify = await PagedAsync(page => $"{baseUrl}/products.json?limit=100&page={page}", 100,
            body => SiteCatalogParsers.Shopify(body, baseUrl), maxProducts, cancellationToken);
        if (shopify.Items is not null)
            return new SiteCatalog("شاپیفای", baseUrl, shopify.Items, problems, shopify.Truncated);

        // ۴) نقشهٔ سایت + JSON-LD
        var urls = await ProductUrlsFromSitemapsAsync(baseUrl, cancellationToken);
        if (urls.Count == 0)
            return Empty(baseUrl, "نوع سایت شناخته نشد: API ووکامرس/شاپیفای در دسترس نبود و نقشهٔ سایت (sitemap) با صفحهٔ محصول پیدا نشد. " +
                                  "می‌توانید نشانی یک صفحهٔ محصول را وارد کنید.");

        var limit = Math.Min(maxProducts, MaxHtmlPages);
        var products = new List<ImportedProduct>();
        var failed = 0;
        foreach (var productUrl in urls.Take(limit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await http.GetAsync(productUrl, expectJson: false, cancellationToken);
            var found = Ok(page) ? SafeJsonLd(page.Body!, productUrl) : new List<ImportedProduct>();
            if (found.Count == 0)
                failed++;
            products.AddRange(found.Take(1));
            await Task.Delay(Pause, cancellationToken);
        }

        if (failed > 0)
            problems.Add($"{failed} صفحه دادهٔ ساختاریافتهٔ محصول نداشت یا باز نشد.");
        if (urls.Count > limit)
            problems.Add($"در این روش هر بار حداکثر {limit} صفحه خوانده می‌شود (از {urls.Count} نشانی). برای بقیه، بعد از ورود این دسته دوباره امتحان کنید.");
        return products.Count == 0
            ? Empty(baseUrl, "صفحه‌های محصول پیدا شدند ولی هیچ‌کدام دادهٔ ساختاریافتهٔ Product (JSON-LD) نداشتند.")
            : new SiteCatalog("نقشهٔ سایت + دادهٔ ساختاریافته", baseUrl, products, problems, urls.Count > limit);
    }

    /// <summary>
    /// خواندن صفحه‌به‌صفحهٔ یک API. اگر صفحهٔ اول جواب درستی نداد Items = null (یعنی این نوع سایت نیست).
    /// </summary>
    private async Task<(List<ImportedProduct>? Items, bool Truncated)> PagedAsync(
        Func<int, string> pageUrl, int pageSize, Func<string, List<ImportedProduct>?> parse, int max, CancellationToken cancellationToken)
    {
        var all = new List<ImportedProduct>();
        for (var page = 1; page <= 50; page++)
        {
            var response = await http.GetAsync(pageUrl(page), expectJson: true, cancellationToken);
            List<ImportedProduct>? items = null;
            if (Ok(response))
            {
                try
                {
                    items = parse(response.Body!);
                }
                catch (Exception ex) when (ex is JsonException or RegexMatchTimeoutException or InvalidOperationException)
                {
                    items = null;
                }
            }

            if (items is null)
                return page == 1 ? (null, false) : (all, false);
            all.AddRange(items);
            if (all.Count >= max)
                return (all.Take(max).ToList(), true);
            if (items.Count < pageSize)
                return (all, false);
            await Task.Delay(Pause, cancellationToken);
        }
        return (all, true);
    }

    /// <summary>
    /// نشانی صفحه‌های محصول از نقشهٔ سایت: اول خط‌های «Sitemap:» در robots.txt، بعد /sitemap.xml و /sitemap_index.xml.
    /// از فهرست نقشه‌ها (sitemap index) فقط آن‌هایی که «product» در نامشان است خوانده می‌شوند (Yoast و Rank Math همین‌طورند)؛
    /// اگر چنین نقشه‌ای نبود، نشانی‌هایی که /product/ دارند انتخاب می‌شوند.
    /// </summary>
    private async Task<List<string>> ProductUrlsFromSitemapsAsync(string baseUrl, CancellationToken cancellationToken)
    {
        var roots = new List<string>();
        var robots = await http.GetAsync(baseUrl + "/robots.txt", expectJson: false, cancellationToken);
        if (Ok(robots))
            roots.AddRange(SiteCatalogParsers.RobotsSitemaps(robots.Body!));
        roots.Add(baseUrl + "/sitemap.xml");
        roots.Add(baseUrl + "/sitemap_index.xml");
        roots.Add(baseUrl + "/product-sitemap.xml");

        var host = new Uri(baseUrl).Host;
        var productUrls = new List<string>();
        var otherUrls = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Url, bool ProductMap)>(roots.Distinct().Select(r => (r, r.Contains("product", StringComparison.OrdinalIgnoreCase))));
        while (queue.Count > 0 && visited.Count < 12 && productUrls.Count < 2000)
        {
            var (mapUrl, productMap) = queue.Dequeue();
            if (!visited.Add(mapUrl) || !SameSite(mapUrl, host))
                continue;
            var response = await http.GetAsync(mapUrl, expectJson: false, cancellationToken);
            if (!Ok(response))
                continue;
            var body = response.Body!;
            var locations = SiteCatalogParsers.SitemapLocations(body).Where(u => SameSite(u, host)).ToList();
            if (SiteCatalogParsers.IsSitemapIndex(body))
            {
                var children = locations.Where(l => l.Contains("product", StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var child in children)
                    queue.Enqueue((child, true));
                if (children.Count == 0)
                    foreach (var child in locations.Take(3))
                        queue.Enqueue((child, false));
                continue;
            }

            (productMap ? productUrls : otherUrls).AddRange(locations);
            if (productUrls.Count > 0 && !productMap)
                break;
        }

        if (productUrls.Count == 0)
            productUrls = otherUrls.Where(SiteCatalogParsers.LooksLikeProductUrl).ToList();
        return productUrls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<ImportedProduct> SafeJsonLd(string html, string url)
    {
        try
        {
            return SiteCatalogParsers.JsonLd(html, url);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or RegexMatchTimeoutException)
        {
            return new List<ImportedProduct>();
        }
    }

    private static bool SameSite(string url, string host)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https"
           && (u.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
               || u.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase)
               || host.EndsWith("." + u.Host, StringComparison.OrdinalIgnoreCase));

    private static bool Ok(SupplierHttpResponse response) => response.Status is >= 200 and < 300 && !string.IsNullOrWhiteSpace(response.Body);

    private static SiteCatalog Empty(string baseUrl, string problem) => new("ناشناخته", baseUrl, [], [problem], false);
}

/// <summary>
/// خواندن خالص پاسخ‌های سایت‌های فروشگاهی (بدون اینترنت؛ نمونه‌ها در تست‌ها).
/// </summary>
public static partial class SiteCatalogParsers
{
    /// <summary>بیشترین تعداد تصویر هر محصول.</summary>
    public const int MaxImages = 10;

    /// <summary>بیشترین تعداد مشخصه هر محصول.</summary>
    public const int MaxAttributes = 40;

    /// <summary>
    /// API فروشگاه ووکامرس (Store API). قیمت‌ها رشتهٔ عددی به «کوچک‌ترین واحد» پول‌اند (currency_minor_unit)؛ ریال ÷ ۱۰ = تومان.
    /// <para>null یعنی پاسخ آرایهٔ محصولات نیست (این سایت ووکامرس نیست).</para>
    /// <para>مثال: {"prices":{"price":"2500000","currency_code":"IRR","currency_minor_unit":0}} ← ۲۵۰٬۰۰۰ تومان.</para>
    /// </summary>
    public static List<ImportedProduct>? Woo(string json, string baseUrl)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return null;
        var list = new List<ImportedProduct>();
        foreach (var p in doc.RootElement.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object)
                continue;
            var name = Decode(Text(p, "name"));
            var url = Absolute(Text(p, "permalink"), baseUrl);
            if (string.IsNullOrWhiteSpace(name) || url is null)
                continue;

            decimal? price = null;
            if (p.TryGetProperty("prices", out var prices) && prices.ValueKind == JsonValueKind.Object)
            {
                var minor = prices.TryGetProperty("currency_minor_unit", out var m) && m.ValueKind == JsonValueKind.Number && m.TryGetInt32(out var mu) ? Math.Clamp(mu, 0, 4) : 0;
                var raw = Number(Text(prices, "price"));
                if (raw is > 0)
                    price = ToToman(raw.Value / Pow10(minor), Text(prices, "currency_code"));
            }

            var attributes = new List<KeyValuePair<string, string>>();
            if (p.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Array)
                foreach (var a in attrs.EnumerateArray())
                {
                    if (a.ValueKind != JsonValueKind.Object)
                        continue;
                    var terms = a.TryGetProperty("terms", out var t) && t.ValueKind == JsonValueKind.Array
                        ? t.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Select(x => Decode(Text(x, "name"))).Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
                        : new List<string?>();
                    AddAttribute(attributes, Decode(Text(a, "name")), string.Join("، ", terms));
                }

            var images = p.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Array
                ? imgs.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object).Select(i => Text(i, "src"))
                : Enumerable.Empty<string?>();
            var inStock = !(p.TryGetProperty("is_in_stock", out var s) && s.ValueKind == JsonValueKind.False);

            list.Add(new ImportedProduct(url, Clip(name, 250)!, Clip(Text(p, "sku"), 100), Clip(HtmlToText(Text(p, "short_description")), 1000),
                Text(p, "description"), price, inStock, FirstName(p, "brands"), FirstName(p, "categories"), attributes, Images(images, baseUrl)));
        }
        return list;
    }

    /// <summary>
    /// <c>/products.json</c> فروشگاه‌های شاپیفای. واحد پول در این پاسخ نیست؛ قیمت همان‌طور که هست (واحد پول فروشگاه) خوانده می‌شود.
    /// <para>null یعنی پاسخ {"products":[…]} نیست.</para>
    /// </summary>
    public static List<ImportedProduct>? Shopify(string json, string baseUrl)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("products", out var products)
            || products.ValueKind != JsonValueKind.Array)
            return null;
        var list = new List<ImportedProduct>();
        foreach (var p in products.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object)
                continue;
            var name = Text(p, "title");
            var handle = Text(p, "handle");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(handle))
                continue;

            decimal? price = null;
            string? sku = null;
            var inStock = true;
            if (p.TryGetProperty("variants", out var variants) && variants.ValueKind == JsonValueKind.Array)
            {
                var all = variants.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Object).ToList();
                var first = all.FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object)
                {
                    price = Number(Text(first, "price")) is > 0 and var value ? value : null;
                    sku = Text(first, "sku");
                }
                if (all.Count > 0 && all.All(v => v.TryGetProperty("available", out var av) && av.ValueKind == JsonValueKind.False))
                    inStock = false;
            }

            var attributes = new List<KeyValuePair<string, string>>();
            if (p.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
                foreach (var o in options.EnumerateArray())
                {
                    if (o.ValueKind != JsonValueKind.Object)
                        continue;
                    var key = Text(o, "name");
                    if (key is null || key.Equals("Title", StringComparison.OrdinalIgnoreCase))
                        continue; // گزینهٔ پیش‌فرض بی‌معنای شاپیفای
                    var values = o.TryGetProperty("values", out var vs) && vs.ValueKind == JsonValueKind.Array
                        ? vs.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList()
                        : new List<string>();
                    AddAttribute(attributes, key, string.Join("، ", values));
                }

            var images = p.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Array
                ? imgs.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object).Select(i => Text(i, "src"))
                : Enumerable.Empty<string?>();
            var body = Text(p, "body_html");
            list.Add(new ImportedProduct($"{baseUrl}/products/{Uri.EscapeDataString(handle)}", Clip(name, 250)!, Clip(sku, 100),
                Clip(HtmlToText(body), 300), body, price, inStock, Clip(Text(p, "vendor"), 100), Clip(Text(p, "product_type"), 100),
                attributes, Images(images, baseUrl)));
        }
        return list;
    }

    /// <summary>
    /// دادهٔ ساختاریافتهٔ Product (schema.org در قالب JSON-LD) داخل HTML یک صفحه. همان چیزی که گوگل برای نتایج غنی می‌خواند.
    /// <para>پشتیبانی: آرایه، @graph، تصویر رشته/آرایه/شیء، برند رشته/شیء، offers تکی/آرایه/AggregateOffer، additionalProperty.</para>
    /// </summary>
    public static List<ImportedProduct> JsonLd(string html, string pageUrl)
    {
        var list = new List<ImportedProduct>();
        foreach (Match match in JsonLdScript().Matches(html))
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(match.Groups[1].Value.Trim(), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            }
            catch (JsonException)
            {
                continue; // بعضی سایت‌ها JSON نامعتبر می‌گذارند؛ بلوک بعدی
            }

            using (doc)
                foreach (var node in Nodes(doc.RootElement, 0))
                    if (IsType(node, "Product") && ProductFromLd(node, pageUrl) is { } product)
                        list.Add(product);
        }
        return list;
    }

    /// <summary>خط‌های «Sitemap: …» فایل robots.txt.</summary>
    public static List<string> RobotsSitemaps(string robots)
        => robots.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("sitemap:", StringComparison.OrdinalIgnoreCase))
            .Select(l => l["sitemap:".Length..].Trim())
            .Where(l => Uri.TryCreate(l, UriKind.Absolute, out var u) && u.Scheme is "http" or "https")
            .Take(10)
            .ToList();

    /// <summary>همهٔ &lt;loc&gt; های یک نقشهٔ سایت (یا فهرست نقشه‌ها).</summary>
    public static List<string> SitemapLocations(string xml)
        => SitemapLoc().Matches(xml).Select(m => WebUtility.HtmlDecode(m.Groups[1].Value.Trim()))
            .Select(v => v.StartsWith("<![CDATA[", StringComparison.Ordinal) ? v[9..].TrimEnd(']', '>') : v)
            .Where(v => v.Length is > 0 and < 1000)
            .ToList();

    /// <summary>آیا این XML فهرست نقشه‌هاست (sitemapindex)؟</summary>
    public static bool IsSitemapIndex(string xml) => xml.Contains("<sitemapindex", StringComparison.OrdinalIgnoreCase);

    /// <summary>حدس صفحهٔ محصول از روی نشانی. مثال: /product/haylou-t15/ یا /p/123 ← بله؛ /blog/… ← خیر.</summary>
    public static bool LooksLikeProductUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u))
            return false;
        var path = Uri.UnescapeDataString(u.AbsolutePath).ToLowerInvariant();
        return path.Contains("/product/") || path.Contains("/products/") || path.Contains("/p/") || path.Contains("/dkp-") || path.Contains("/محصول/");
    }

    /// <summary>
    /// تمیز کردن توضیح HTML مبدأ پیش از پاک‌ساز XSS: تصویر/ویدیو/iframe حذف می‌شوند (تصویر سایت دیگر هرگز مستقیم نمایش داده نمی‌شود)
    /// و پیوندها به متن ساده تبدیل می‌شوند (لینک به سایت مبدأ برای سئو بد است).
    /// <para>مثال: «&lt;p&gt;عالی &lt;a href="https://x"&gt;خرید&lt;/a&gt;&lt;img src="https://x/1.jpg"&gt;&lt;/p&gt;» ← «&lt;p&gt;عالی خرید&lt;/p&gt;».</para>
    /// </summary>
    public static string? CleanDescription(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;
        var text = DangerousBlocks().Replace(html, "");
        text = MediaTags().Replace(text, "");
        text = AnchorTags().Replace(text, "");
        text = text.Trim();
        return HtmlToText(text) is { Length: > 0 } ? text : null;
    }

    /// <summary>متن سادهٔ یک HTML (برای توضیح کوتاه). مثال: «&lt;p&gt;سلام&amp;nbsp;دنیا&lt;/p&gt;» ← «سلام دنیا».</summary>
    public static string? HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;
        var text = WebUtility.HtmlDecode(AnyTag().Replace(DangerousBlocks().Replace(html, " "), " "));
        text = Whitespace().Replace(text, " ").Trim();
        return text.Length == 0 ? null : text;
    }

    // ── JSON-LD ──

    private static IEnumerable<JsonElement> Nodes(JsonElement element, int depth)
    {
        if (depth > 4)
            yield break;
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                foreach (var node in Nodes(item, depth + 1))
                    yield return node;
            yield break;
        }
        if (element.ValueKind != JsonValueKind.Object)
            yield break;
        yield return element;
        if (element.TryGetProperty("@graph", out var graph))
            foreach (var node in Nodes(graph, depth + 1))
                yield return node;
        if (element.TryGetProperty("mainEntity", out var main))
            foreach (var node in Nodes(main, depth + 1))
                yield return node;
    }

    private static bool IsType(JsonElement node, string type)
    {
        if (!node.TryGetProperty("@type", out var t))
            return false;
        return t.ValueKind switch
        {
            JsonValueKind.String => string.Equals(t.GetString(), type, StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => t.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && string.Equals(x.GetString(), type, StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    private static ImportedProduct? ProductFromLd(JsonElement p, string pageUrl)
    {
        var name = Decode(Text(p, "name"));
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var url = Absolute(Text(p, "url"), pageUrl) ?? pageUrl;

        decimal? price = null;
        bool? inStock = null;
        if (p.TryGetProperty("offers", out var offers))
        {
            var offer = offers.ValueKind == JsonValueKind.Array ? offers.EnumerateArray().FirstOrDefault(o => o.ValueKind == JsonValueKind.Object) : offers;
            if (offer.ValueKind == JsonValueKind.Object)
            {
                var amount = Number(Text(offer, "price")) ?? Number(Text(offer, "lowPrice"));
                if (amount is null && offer.TryGetProperty("priceSpecification", out var spec) && spec.ValueKind == JsonValueKind.Object)
                    amount = Number(Text(spec, "price"));
                if (amount is > 0)
                    price = ToToman(amount.Value, Text(offer, "priceCurrency"));
                if (Text(offer, "availability") is { } availability)
                    inStock = !availability.Contains("OutOfStock", StringComparison.OrdinalIgnoreCase) && !availability.Contains("SoldOut", StringComparison.OrdinalIgnoreCase)
                              && !availability.Contains("Discontinued", StringComparison.OrdinalIgnoreCase);
            }
        }

        var attributes = new List<KeyValuePair<string, string>>();
        if (p.TryGetProperty("additionalProperty", out var props) && props.ValueKind == JsonValueKind.Array)
            foreach (var prop in props.EnumerateArray())
                if (prop.ValueKind == JsonValueKind.Object)
                    AddAttribute(attributes, Decode(Text(prop, "name")), Decode(Text(prop, "value")));
        foreach (var (key, title) in new[] { ("color", "رنگ"), ("material", "جنس"), ("model", "مدل"), ("weight", "وزن") })
            if (p.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                AddAttribute(attributes, title, Decode(v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()));

        var images = new List<string?>();
        if (p.TryGetProperty("image", out var image))
            CollectImages(image, images);

        var description = Text(p, "description");
        var htmlDescription = description is null ? null
            : HtmlTagLike().IsMatch(description) ? description
            : string.Join("", description.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line => $"<p>{WebUtility.HtmlEncode(line)}</p>"));

        return new ImportedProduct(url, Clip(name, 250)!, Clip(Text(p, "sku") ?? Text(p, "mpn"), 100), Clip(HtmlToText(description), 300), htmlDescription,
            price, inStock ?? true, Clip(NameOf(p, "brand"), 100), Clip(Text(p, "category"), 100), attributes, Images(images, pageUrl));
    }

    private static void CollectImages(JsonElement image, List<string?> into)
    {
        switch (image.ValueKind)
        {
            case JsonValueKind.String:
                into.Add(image.GetString());
                break;
            case JsonValueKind.Array:
                foreach (var item in image.EnumerateArray().Take(MaxImages))
                    CollectImages(item, into);
                break;
            case JsonValueKind.Object:
                into.Add(Text(image, "url") ?? Text(image, "contentUrl"));
                break;
        }
    }

    // ── کمکی‌ها ──

    /// <summary>
    /// تبدیل به تومان: ریال (IRR) ÷ ۱۰؛ تومان (IRT/TOMAN) همان؛ هر واحد دیگر همان‌طور که هست.
    /// </summary>
    public static decimal ToToman(decimal amount, string? currency)
        => currency?.Trim().ToUpperInvariant() == "IRR" ? Math.Round(amount / 10m) : Math.Round(amount);

    /// <summary>عدد از متن (رقم فارسی و جداکنندهٔ هزارگان پذیرفته می‌شود). مثال: «۱٬۲۵۰٬۰۰۰» ← ۱۲۵۰۰۰۰؛ «99.50» ← ۹۹٫۵.</summary>
    public static decimal? Number(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var normalized = PersianText.Normalize(text).Replace(",", "").Replace("٬", "").Replace("٫", ".").Replace(" ", "");
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && value < 1_000_000_000_000m ? value : null;
    }

    private static decimal Pow10(int n) => n switch { 0 => 1m, 1 => 10m, 2 => 100m, 3 => 1000m, _ => 10000m };

    private static void AddAttribute(List<KeyValuePair<string, string>> into, string? key, string? value)
    {
        key = Clip(key?.Trim().TrimEnd(':'), 250);
        value = Clip(value?.Trim(), 1000);
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value) || into.Count >= MaxAttributes
            || into.Any(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
            return;
        into.Add(new(key, value));
    }

    private static List<string> Images(IEnumerable<string?> urls, string baseUrl)
        => urls.Select(u => Absolute(u, baseUrl)).OfType<string>().Distinct(StringComparer.Ordinal).Take(MaxImages).ToList();

    /// <summary>نشانی کامل http(s) یا null. «//cdn.x/a.jpg» ← https://cdn.x/a.jpg؛ «/a.jpg» ← نسبت به نشانی پایه.</summary>
    public static string? Absolute(string? url, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        url = url.Trim();
        if (url.StartsWith("//", StringComparison.Ordinal))
            url = "https:" + url;
        // «/a.jpg» در لینوکس به‌عنوان مسیر فایل (file:///a.jpg) «مطلق» حساب می‌شود؛ پس اول نسبی بودن بررسی می‌شود.
        if (url.StartsWith('/') || !Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var root) || !Uri.TryCreate(root, url, out absolute))
                return null;
        }
        return absolute.Scheme is "http" or "https" && absolute.AbsoluteUri.Length <= 1000 ? absolute.AbsoluteUri : null;
    }

    private static string? FirstName(JsonElement p, string property)
        => p.TryGetProperty(property, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Select(x => Clip(Decode(Text(x, "name")), 100)).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            : null;

    private static string? NameOf(JsonElement p, string property)
    {
        if (!p.TryGetProperty(property, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => Decode(value.GetString()),
            JsonValueKind.Object => Decode(Text(value, "name")),
            JsonValueKind.Array => value.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Object ? Text(v, "name") : v.ValueKind == JsonValueKind.String ? v.GetString() : null)
                .Select(Decode).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)),
            _ => null,
        };
    }

    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        } : null;

    private static string? Decode(string? text) => text is null ? null : WebUtility.HtmlDecode(text).Trim();

    private static string? Clip(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim();
        return text.Length <= max ? text : text[..max].Trim();
    }

    [GeneratedRegex(@"<script[^>]*type\s*=\s*[""']?application/ld\+json[""']?[^>]*>(.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex JsonLdScript();

    [GeneratedRegex(@"<loc>(.*?)</loc>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex SitemapLoc();

    [GeneratedRegex(@"<(script|style|iframe|object|embed|video|audio|noscript|form)\b.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex DangerousBlocks();

    [GeneratedRegex(@"<(img|picture|source|iframe|video|audio|embed|object|svg)\b[^>]*/?>|</(picture|svg)\s*>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex MediaTags();

    [GeneratedRegex(@"</?a\b[^>]*>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex AnchorTags();

    [GeneratedRegex(@"</?[a-zA-Z][a-zA-Z0-9]*(\s[^>]*)?/?>", RegexOptions.None, 2000)]
    private static partial Regex HtmlTagLike();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.None, 2000)]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"\s+", RegexOptions.None, 2000)]
    private static partial Regex Whitespace();
}
