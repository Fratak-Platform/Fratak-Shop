using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Products;

namespace Fratak_Shop.Core.Services.Suppliers;

// ═══════════════════════════════════════════════════════════════════════════
//  خواندن اطلاعات کالا از سایت تأمین‌کننده (مرحلهٔ هفتم – فاز ۵)
//
//  ساختار قابل گسترش:
//   • ISupplierHttp        : فقط «یک درخواست GET» – پیاده‌سازی واقعی در لایهٔ Web (با محافظ SSRF و سقف حجم)؛
//                            در تست‌ها یک نسخهٔ جعلی داده می‌شود.
//   • ISupplierAdapter     : هر نوع سایت یک آداپتور دارد. فعلاً:
//        - WooCommerceStoreAdapter : API عمومی فروشگاه ووکامرس (پخش عبدی) – بدون نیاز به کلید.
//        - StructuredDataAdapter   : داده‌های schema.org داخل صفحهٔ محصول (JSON-LD) – اکثر فروشگاه‌ها.
//   • SupplierAdapterRegistry : انتخاب آداپتور بر اساس «روش بررسی» تأمین‌کننده (تشخیص خودکار در حالت Auto).
//
//  افزودن تأمین‌کنندهٔ تازه با سایت اختصاصی: یک کلاس ISupplierAdapter بنویسید و در Registry ثبت کنید؛
//  بقیهٔ سیستم (مقایسه، گزارش، تأیید) بدون تغییر کار می‌کند.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>پاسخ یک درخواست GET. <see cref="Status"/> صفر یعنی خطای شبکه (متن خطا در <see cref="Error"/>).</summary>
public sealed record SupplierHttpResponse(int Status, string? Body, string? Error = null)
{
    public bool IsSuccess => Status is >= 200 and < 300 && Body is not null;
}

/// <summary>
/// درخواست HTTP به سایت تأمین‌کننده. پیاده‌سازی Web باید: فقط http/https، بدون آدرس داخلی/خصوصی،
/// مهلت حداکثر ۲۵ ثانیه و سقف حجم پاسخ را رعایت کند.
/// </summary>
public interface ISupplierHttp
{
    Task<SupplierHttpResponse> GetAsync(string url, bool expectJson, CancellationToken cancellationToken);

    /// <summary>
    /// دور ۲۴: درخواست POST فرم (application/x-www-form-urlencoded) با همان محافظ‌ها؛ مثلاً جست‌وجوی ایمالز (<c>_Search.ashx</c>).
    /// پیاده‌سازی پیش‌فرض «پشتیبانی نمی‌شود» برمی‌گرداند تا جعلی‌های تست‌ها بدون تغییر کار کنند.
    /// </summary>
    Task<SupplierHttpResponse> PostFormAsync(string url, IReadOnlyDictionary<string, string> form, bool expectJson, CancellationToken cancellationToken) =>
        Task.FromResult(new SupplierHttpResponse(0, null, "ارسال فرم پشتیبانی نمی‌شود."));
}

/// <summary>یک محصول فروشگاه که باید در سایت منبع خوانده شود.</summary>
/// <param name="ProductId">شناسهٔ محصول ما.</param>
/// <param name="Url">لینک محصول در سایت تأمین‌کننده (همان SupplierUrl).</param>
/// <param name="ExternalId">شناسهٔ ذخیره‌شده از دفعهٔ قبل (اگر لینک عوض نشده باشد).</param>
public sealed record SupplierProductRef(Guid ProductId, string Url, string? ExternalId);

/// <summary>
/// نتیجهٔ خواندن یک کالا. قیمت همیشه به <b>تومان</b> است (آداپتور ریال را تبدیل می‌کند).
/// </summary>
public sealed record SupplierSnapshot(
    SupplierItemStatus Status,
    decimal? Price = null,
    int? Stock = null,
    string? ExternalId = null,
    string? Name = null,
    string? Error = null)
{
    public static SupplierSnapshot NotFound(string? message = null) => new(SupplierItemStatus.NotFound, Error: message);
    public static SupplierSnapshot Failed(string message) => new(SupplierItemStatus.Error, Error: message);
}

/// <summary>
/// زمینهٔ اجرای یک آداپتور: درخواست HTTP و مکث بین درخواست‌ها (تا سایت منبع ما را ربات مزاحم تشخیص ندهد).
/// </summary>
public sealed class SupplierFetchContext(ISupplierHttp http, TimeSpan delay, CancellationToken cancellationToken)
{
    private DateTimeOffset _last = DateTimeOffset.MinValue;

    public CancellationToken CancellationToken { get; } = cancellationToken;

    /// <summary>تعداد درخواست‌های فرستاده‌شده (برای گزارش اجرا).</summary>
    public int Requests { get; private set; }

    /// <summary>GET با رعایت فاصلهٔ زمانی از درخواست قبلی.</summary>
    public async Task<SupplierHttpResponse> GetAsync(string url, bool expectJson)
    {
        var wait = _last + delay - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, CancellationToken);
        Requests++;
        try
        {
            return await http.GetAsync(url, expectJson, CancellationToken);
        }
        finally
        {
            _last = DateTimeOffset.UtcNow;
        }
    }
}

/// <summary>آداپتور یک نوع سایت تأمین‌کننده.</summary>
public interface ISupplierAdapter
{
    /// <summary>عنوان فارسی برای گزارش‌ها؛ مثلاً «API ووکامرس».</summary>
    string Title { get; }

    /// <summary>
    /// خواندن گروهی محصولات یک سایت. خروجی برای هر ProductId ورودی یک نتیجه دارد
    /// (آداپتور هرگز استثنا به بیرون نمی‌دهد؛ خطا را به‌صورت <see cref="SupplierItemStatus.Error"/> برمی‌گرداند).
    /// </summary>
    Task<Dictionary<Guid, SupplierSnapshot>> FetchAsync(IReadOnlyList<SupplierProductRef> products, SupplierFetchContext context);
}

/// <summary>توابع مشترک تبدیل عدد و قیمت.</summary>
public static partial class SupplierParsing
{
    /// <summary>
    /// تبدیل مبلغ به تومان بر اساس کد ارز. «IRR» (ریال) تقسیم بر ۱۰؛ «IRT» یا «TOMAN» همان تومان.
    /// ارز ناشناخته: null (تا قیمت اشتباه گزارش نشود).
    /// مثال: (4150000, "IRR") → 415000 ؛ (415000, "IRT") → 415000.
    /// </summary>
    public static decimal? ToToman(decimal amount, string? currency)
    {
        if (amount <= 0)
            return null;
        var code = (currency ?? "").Trim().ToUpperInvariant();
        return code switch
        {
            "IRR" or "RIAL" or "ریال" => Math.Round(amount / 10m, 0, MidpointRounding.AwayFromZero),
            "IRT" or "TOMAN" or "TOM" or "تومان" or "" => Math.Round(amount, 0, MidpointRounding.AwayFromZero),
            _ => null,
        };
    }

    /// <summary>عدد از متن (با ارقام فارسی/عربی و جداکننده). مثال: «۴۱۵٬۰۰۰» → 415000.</summary>
    public static decimal? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var digits = SearchText.NormalizeDigits(text).Replace(",", "").Replace("٬", "").Replace("،", "").Trim();
        return decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    /// <summary>
    /// موجودی عددی از متن وضعیت انبار ووکامرس. مثال: «۲۷۶ عدد در انبار» → 276 ؛ «276 in stock» → 276 ؛ «ناموجود» → null.
    /// </summary>
    public static int? ParseStockText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var normalized = SearchText.NormalizeDigits(WebUtility.HtmlDecode(text));
        var match = StockText().Match(normalized);
        return match.Success && int.TryParse(match.Groups[1].Value, out var stock) ? stock : null;
    }

    [GeneratedRegex(@"(\d+)\s*(?:عدد|تا|قلم|items?|pcs)?\s*(?:در انبار|موجود|in stock)", RegexOptions.IgnoreCase)]
    private static partial Regex StockText();
}

/// <summary>
/// آداپتور API عمومی فروشگاه ووکامرس (<c>/wp-json/wc/store/v1/products</c>) – بدون کلید و رایگان.
/// <para>
/// روند: بار اول محصول با «نامک» (slug) آدرس پیدا و شناسه‌اش ذخیره می‌شود؛ دفعه‌های بعد ۵۰ محصول در یک درخواست
/// با <c>include=</c> خوانده می‌شوند (برای ۵۰۰ محصول فقط ۱۰ درخواست).
/// </para>
/// <para>
/// موجودی دقیق به ترتیب از: متن «۲۷۶ عدد در انبار» (stock_availability)، low_stock_remaining و
/// add_to_cart.maximum (اگر کمتر از ۹۹۹۹ باشد). قیمت از prices.price با در نظر گرفتن currency_minor_unit و ارز.
/// </para>
/// مثال آدرس: https://pakhshabdi.com/product/شامپو-کراتینه/ → slug=«شامپو-کراتینه».
/// </summary>
public sealed class WooCommerceStoreAdapter : ISupplierAdapter
{
    private const int BatchSize = 50;
    private const string Fields = "id,name,slug,permalink,prices,is_in_stock,is_purchasable,add_to_cart,stock_availability,low_stock_remaining";

    public string Title => "API ووکامرس";

    public async Task<Dictionary<Guid, SupplierSnapshot>> FetchAsync(IReadOnlyList<SupplierProductRef> products, SupplierFetchContext context)
    {
        var result = new Dictionary<Guid, SupplierSnapshot>();
        if (products.Count == 0)
            return result;
        var origin = Origin(products[0].Url);
        if (origin is null)
        {
            foreach (var p in products)
                result[p.ProductId] = SupplierSnapshot.Failed("آدرس محصول معتبر نیست.");
            return result;
        }

        // ۱) محصولاتی که شناسه دارند: گروهی.
        var known = products.Where(p => long.TryParse(p.ExternalId, out _)).ToList();
        foreach (var chunk in known.Chunk(BatchSize))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var ids = string.Join(',', chunk.Select(p => p.ExternalId));
            var response = await context.GetAsync($"{origin}/wp-json/wc/store/v1/products?include={ids}&per_page={BatchSize}&_fields={Fields}", true);
            if (!response.IsSuccess)
            {
                foreach (var p in chunk)
                    result[p.ProductId] = SupplierSnapshot.Failed(Describe(response));
                continue;
            }

            var parsed = ParseList(response.Body!);
            if (parsed is null)
            {
                foreach (var p in chunk)
                    result[p.ProductId] = SupplierSnapshot.Failed("پاسخ API قابل خواندن نبود.");
                continue;
            }

            foreach (var p in chunk)
            {
                // محصولی که در پاسخ نیامده یا حذف/پیش‌نویس شده؛ با نامک دوباره جست‌وجو می‌شود.
                if (parsed.TryGetValue(p.ExternalId!, out var snapshot))
                    result[p.ProductId] = snapshot;
            }
        }

        // ۲) بقیه (بار اول، لینک عوض‌شده یا در پاسخ گروهی نبود): پیدا کردن شناسهٔ محصول در سایت منبع.
        foreach (var p in products.Where(p => !result.ContainsKey(p.ProductId)))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            result[p.ProductId] = await ResolveAsync(origin, p.Url, context);
        }

        return result;
    }

    /// <summary>
    /// پیدا کردن محصولی که هنوز شناسه‌اش را نداریم. سه راه به ترتیب (اولین موفق برنده است):
    /// <list type="number">
    /// <item>نامک انگلیسی (ASCII): <c>?slug=</c> – سبک‌ترین راه.</item>
    /// <item>جست‌وجوی متنی API با کلمه‌های نامک (<c>?search=کاغذ سرخ کن</c>) و انتخاب نتیجه‌ای که permalink آن دقیقاً
    ///       همان لینک ماست. <b>علت:</b> فیلتر <c>?slug=</c> ووکامرس برای نامک فارسی (درصدی‌کدشده در پایگاه داده) همیشه
    ///       آرایهٔ خالی برمی‌گرداند (روی پخش عبدی آزموده شد) و به همین خاطر قبلاً هیچ محصولی پیدا نمی‌شد.</item>
    /// <item>خواندن خود صفحهٔ محصول و استخراج شناسه از HTML (کلاس <c>postid-123</c>، لینک کوتاه <c>?p=123</c> یا دکمهٔ
    ///       <c>add-to-cart</c>) و سپس <c>?include=123</c>. اگر شناسه هم نبود، داده‌های ساختاریافتهٔ همان صفحه خوانده می‌شود.</item>
    /// </list>
    /// شناسهٔ پیدا شده در <see cref="SupplierSnapshot.ExternalId"/> برمی‌گردد و ذخیره می‌شود؛ اجراهای بعدی گروهی و سریع‌اند.
    /// </summary>
    private static async Task<SupplierSnapshot> ResolveAsync(string origin, string url, SupplierFetchContext context)
    {
        var slug = Slug(url);
        var api = $"{origin}/wp-json/wc/store/v1/products";
        string? lastError = null;

        if (slug is not null)
        {
            // راه ۱: نامک لاتین.
            if (slug.All(char.IsAscii))
            {
                var bySlug = await context.GetAsync($"{api}?slug={Uri.EscapeDataString(slug)}&_fields={Fields}", true);
                if (bySlug.IsSuccess && ParseList(bySlug.Body!) is { Count: > 0 } found)
                    return found.Values.First();
                if (!bySlug.IsSuccess && bySlug.Status is not (404 or 400))
                    lastError = Describe(bySlug);
            }

            // راه ۲: جست‌وجوی متنی + تطبیق دقیق permalink.
            var words = SearchWords(slug);
            if (words.Length >= 2)
            {
                var search = await context.GetAsync($"{api}?search={Uri.EscapeDataString(words)}&per_page=20&_fields={Fields}", true);
                if (search.IsSuccess)
                {
                    if (MatchByPermalink(search.Body!, url) is { } matched)
                        return matched;
                }
                else if (search.Status is not (404 or 400))
                {
                    lastError = Describe(search);
                }
            }
        }

        // راه ۳: خود صفحهٔ محصول.
        var page = await context.GetAsync(url, false);
        if (page.Status is 404 or 410)
            return SupplierSnapshot.NotFound("صفحهٔ این محصول در سایت منبع وجود ندارد (حذف شده یا لینک عوض شده).");
        if (!page.IsSuccess)
            return SupplierSnapshot.Failed(lastError ?? Describe(page));

        if (ProductIdFromHtml(page.Body!) is { } id)
        {
            var byId = await context.GetAsync($"{api}?include={id}&per_page=1&_fields={Fields}", true);
            if (byId.IsSuccess && ParseList(byId.Body!) is { } list && list.TryGetValue(id, out var snapshot))
                return snapshot;
        }

        // API چیزی نداد ولی صفحه باز شد: وضعیت از خود صفحه (JSON-LD یا ساختار HTML ووکامرس).
        var fromPage = StructuredDataAdapter.ParseHtml(page.Body!);
        return fromPage.Status == SupplierItemStatus.Error
            ? SupplierSnapshot.NotFound("محصول در API فروشگاه منبع پیدا نشد و صفحه‌اش هم قیمت/موجودی قابل خواندن نداشت؛ لینک را بررسی کنید.")
            : fromPage;
    }

    /// <summary>
    /// کلمه‌های جست‌وجو از نامک: خط تیره → فاصله، اعداد و حروف حفظ می‌شوند. مثال: «کاغذ-سرخ-کن-گرد» → «کاغذ سرخ کن گرد».
    /// حداکثر ۸ کلمه (جست‌وجوی وردپرس همهٔ کلمه‌ها را با AND می‌خواهد؛ کلمه‌های زیاد فقط کندش می‌کند).
    /// </summary>
    public static string SearchWords(string slug)
    {
        var words = slug.Replace('_', '-').Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length > 0)
            .Take(8);
        return string.Join(' ', words);
    }

    /// <summary>
    /// از نتیجهٔ جست‌وجوی Store API محصولی را برمی‌گرداند که permalink آن همان لینک ماست (مقایسهٔ نامک رمزگشایی‌شده و
    /// بدون حساسیت به حروف/اسلش پایانی). اگر هیچ‌کدام دقیقاً یکی نبود null (حدس نمی‌زنیم تا محصول اشتباه گزارش نشود).
    /// </summary>
    public static SupplierSnapshot? MatchByPermalink(string json, string url)
    {
        var wanted = NormalizePath(url);
        if (wanted is null)
            return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                var permalink = Text(item, "permalink");
                if (permalink is not null && NormalizePath(permalink) == wanted)
                    return ParseProduct(item);
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    /// <summary>
    /// مسیر لینک برای مقایسه: رمزگشایی درصدی، حروف کوچک، بدون اسلش ابتدا/انتها و بدون querystring.
    /// مثال: «https://site.com/product/%DA%A9%D8%A7%D8%BA%D8%B0/» و «https://site.com/product/کاغذ» → «product/کاغذ».
    /// </summary>
    public static string? NormalizePath(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return null;
        var path = Uri.UnescapeDataString(uri.AbsolutePath).Trim('/');
        return path.Length == 0 ? null : path.ToLowerInvariant();
    }

    /// <summary>
    /// شناسهٔ عددی محصول ووکامرس از HTML صفحهٔ محصول. به ترتیب اطمینان:
    /// کلاس بدنه «postid-123»، لینک کوتاه «?p=123»، دکمهٔ فرم «name="add-to-cart" value="123"»، لینک «wp/v2/product/123».
    /// (data-product_id عمداً استفاده نمی‌شود چون در «محصولات مرتبط» پایین صفحه هم تکرار می‌شود.)
    /// </summary>
    public static string? ProductIdFromHtml(string html)
    {
        foreach (var regex in ProductIdPatterns)
        {
            var match = regex.Match(html);
            if (match.Success)
                return match.Groups[1].Value;
        }
        return null;
    }

    private static readonly Regex[] ProductIdPatterns =
    [
        new(@"<body[^>]*\bpostid-(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"<link[^>]*rel\s*=\s*[""']shortlink[""'][^>]*[?&]p=(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"name\s*=\s*[""']add-to-cart[""'][^>]*value\s*=\s*[""'](\d+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"value\s*=\s*[""'](\d+)[""'][^>]*name\s*=\s*[""']add-to-cart[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"/wp-json/wp/v2/product/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    /// <summary>آیا این سایت API فروشگاه ووکامرس دارد؟ (برای تشخیص خودکار؛ یک درخواست سبک).</summary>
    public static async Task<bool> DetectAsync(string url, SupplierFetchContext context)
    {
        var origin = Origin(url);
        if (origin is null)
            return false;
        var response = await context.GetAsync($"{origin}/wp-json/wc/store/v1/products?per_page=1&_fields=id", true);
        return response.IsSuccess && response.Body!.TrimStart().StartsWith('[');
    }

    /// <summary>«https://site.com/product/x/» → «https://site.com».</summary>
    public static string? Origin(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.GetLeftPart(UriPartial.Authority)
            : null;

    /// <summary>
    /// نامک محصول از آدرس: آخرین بخش غیرخالی مسیر، رمزگشایی‌شده.
    /// مثال: «/product/%D8%B4%D8%A7%D9%85%D9%BE%D9%88/» → «شامپو». آدرس‌هایی مثل «?p=123» نامک ندارند (null).
    /// </summary>
    public static string? Slug(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return null;
        var slug = Uri.UnescapeDataString(segments[^1]).Trim();
        return slug.Length is 0 or > 400 ? null : slug;
    }

    /// <summary>خواندن آرایهٔ محصولات Store API؛ کلید = id محصول. پاسخ خراب → null.</summary>
    public static Dictionary<string, SupplierSnapshot>? ParseList(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return null;
            var result = new Dictionary<string, SupplierSnapshot>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                var snapshot = ParseProduct(item);
                if (snapshot.ExternalId is not null)
                    result[snapshot.ExternalId] = snapshot;
            }
            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>تبدیل یک محصول Store API به <see cref="SupplierSnapshot"/>.</summary>
    public static SupplierSnapshot ParseProduct(JsonElement item)
    {
        var id = item.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number ? idElement.GetInt64().ToString(CultureInfo.InvariantCulture) : null;
        var name = Text(item, "name");
        if (name is not null)
            name = WebUtility.HtmlDecode(name);

        decimal? price = null;
        if (item.TryGetProperty("prices", out var prices) && prices.ValueKind == JsonValueKind.Object)
        {
            var raw = SupplierParsing.ParseNumber(Text(prices, "price"));
            var minor = prices.TryGetProperty("currency_minor_unit", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : 0;
            if (raw is { } amount)
            {
                for (var i = 0; i < Math.Clamp(minor, 0, 6); i++)
                    amount /= 10m;
                price = SupplierParsing.ToToman(amount, Text(prices, "currency_code"));
            }
        }

        var inStock = item.TryGetProperty("is_in_stock", out var s) && s.ValueKind == JsonValueKind.True;
        var purchasable = !item.TryGetProperty("is_purchasable", out var pu) || pu.ValueKind != JsonValueKind.False;
        if (!inStock || !purchasable)
            return new SupplierSnapshot(SupplierItemStatus.OutOfStock, price, 0, id, name);

        int? stock = null;
        if (item.TryGetProperty("stock_availability", out var availability) && availability.ValueKind == JsonValueKind.Object)
            stock = SupplierParsing.ParseStockText(Text(availability, "text"));
        if (stock is null && item.TryGetProperty("low_stock_remaining", out var low) && low.ValueKind == JsonValueKind.Number)
            stock = low.GetInt32();
        if (stock is null && item.TryGetProperty("add_to_cart", out var cart) && cart.ValueKind == JsonValueKind.Object
            && cart.TryGetProperty("maximum", out var max) && max.ValueKind == JsonValueKind.Number)
        {
            var value = max.GetInt64();
            if (value is > 0 and < 9999)
                stock = (int)value;
        }

        // موجودی صفر اعلام‌شده با «موجود» تناقض دارد؛ ناموجود در نظر گرفته می‌شود.
        if (stock == 0)
            return new SupplierSnapshot(SupplierItemStatus.OutOfStock, price, 0, id, name);
        return new SupplierSnapshot(SupplierItemStatus.InStock, price, stock, id, name);
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        } : null;

    internal static string Describe(SupplierHttpResponse response) =>
        response.Status == 0 ? $"خطای ارتباط: {response.Error}" : $"پاسخ HTTP {response.Status} از سایت منبع";
}

/// <summary>
/// آداپتور عمومی: خواندن صفحهٔ محصول و داده‌های ساختاریافتهٔ schema.org (JSON-LD) که اکثر فروشگاه‌ها برای گوگل دارند.
/// <para>
/// از Product → offers: price (یا lowPrice)، priceCurrency و availability (InStock / OutOfStock / SoldOut …)؛
/// اگر JSON-LD نبود از متاتگ‌های <c>product:price:amount</c> و <c>product:availability</c>.
/// موجودی عددی اگر در متن صفحه «N عدد در انبار» باشد خوانده می‌شود.
/// </para>
/// </summary>
public sealed partial class StructuredDataAdapter : ISupplierAdapter
{
    public string Title => "داده‌های صفحهٔ محصول";

    public async Task<Dictionary<Guid, SupplierSnapshot>> FetchAsync(IReadOnlyList<SupplierProductRef> products, SupplierFetchContext context)
    {
        var result = new Dictionary<Guid, SupplierSnapshot>();
        foreach (var p in products)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var response = await context.GetAsync(p.Url, false);
            if (response.Status is 404 or 410)
                result[p.ProductId] = SupplierSnapshot.NotFound("صفحهٔ محصول در سایت منبع وجود ندارد.");
            else if (!response.IsSuccess)
                result[p.ProductId] = SupplierSnapshot.Failed(WooCommerceStoreAdapter.Describe(response));
            else
                result[p.ProductId] = ParseHtml(response.Body!);
        }
        return result;
    }

    /// <summary>استخراج وضعیت از HTML صفحهٔ محصول (عمومی برای تست واحد).</summary>
    public static SupplierSnapshot ParseHtml(string html)
    {
        foreach (Match block in JsonLd().Matches(html))
        {
            var found = ParseJsonLd(WebUtility.HtmlDecode(block.Groups[1].Value.Trim()));
            if (found is not null)
                return WithPageStock(found, html);
        }

        // جایگزین: متاتگ‌های Open Graph محصول.
        var amount = SupplierParsing.ParseNumber(Meta(html, "product:price:amount"));
        var availability = Meta(html, "product:availability") ?? Meta(html, "og:availability");
        if (amount is not null && availability is not null)
        {
            var price = SupplierParsing.ToToman(amount.Value, Meta(html, "product:price:currency"));
            var snapshot = IsInStock(availability)
                ? new SupplierSnapshot(SupplierItemStatus.InStock, price)
                : new SupplierSnapshot(SupplierItemStatus.OutOfStock, price, 0);
            return WithPageStock(snapshot, html);
        }

        // جایگزین دوم: ساختار HTML استاندارد ووکامرس (بیشتر فروشگاه‌های وردپرسی ایران، از جمله پخش عبدی).
        if (ParseWooHtml(html) is { } woo)
            return woo;

        return SupplierSnapshot.Failed("اطلاعات قیمت و موجودی در صفحه پیدا نشد؛ روش بررسی این تأمین‌کننده را عوض کنید یا آداپتور اختصاصی لازم است.");
    }

    /// <summary>
    /// خواندن وضعیت از HTML قالب ووکامرس وقتی JSON-LD نیست:
    /// <list type="bullet">
    /// <item>موجودی: <c>&lt;p class="stock in-stock"&gt;276 عدد در انبار&lt;/p&gt;</c> یا <c>class="stock out-of-stock"</c>.</item>
    /// <item>قیمت: اولین <c>&lt;p class="price"&gt;</c>؛ اگر حراج باشد مبلغ داخل <c>&lt;ins&gt;</c> (قیمت فعلی) برداشته می‌شود.</item>
    /// </list>
    /// اولین تطبیق‌ها متعلق به محصول اصلی است (محصولات مرتبط پایین صفحه‌اند). چیزی پیدا نشد → null.
    /// </summary>
    public static SupplierSnapshot? ParseWooHtml(string html)
    {
        decimal? price = null;
        var priceBlock = WooPriceBlock().Match(html);
        if (priceBlock.Success)
        {
            var block = priceBlock.Groups[1].Value;
            var ins = Regex.Match(block, @"<ins\b[^>]*>(.*?)</ins>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var amountHtml = ins.Success ? ins.Groups[1].Value : block;
            var amounts = WooAmount().Matches(amountHtml);
            if (amounts.Count > 0)
            {
                var text = WebUtility.HtmlDecode(Regex.Replace(amounts[0].Groups[1].Value, "<[^>]+>", " "));
                var currency = text.Contains("ریال") ? "IRR" : "IRT";
                // قیمت تومان/ریال اعشار ندارد؛ هر جداکننده‌ای (, . ٬) حذف می‌شود. مثال: «۲۰۴٬۰۰۰ تومان» → 204000.
                var number = SupplierParsing.ParseNumber(Regex.Replace(SearchText.NormalizeDigits(text), @"[^\d]", ""));
                if (number is { } value)
                    price = SupplierParsing.ToToman(value, currency);
            }
        }

        var stockMatch = WooStock().Match(html);
        if (stockMatch.Success)
        {
            var css = stockMatch.Groups[1].Value.ToLowerInvariant();
            if (css is "out-of-stock")
                return new SupplierSnapshot(SupplierItemStatus.OutOfStock, price, 0);
            var stock = SupplierParsing.ParseStockText(stockMatch.Groups[2].Value);
            return css is "available-on-backorder"
                ? new SupplierSnapshot(SupplierItemStatus.OutOfStock, price, 0)
                : new SupplierSnapshot(SupplierItemStatus.InStock, price, stock is > 0 ? stock : null);
        }

        // بدون نوشتهٔ موجودی: اگر قیمت و دکمهٔ «افزودن به سبد» هست یعنی قابل خرید است.
        if (price is not null && Regex.IsMatch(html, @"name\s*=\s*[""']add-to-cart[""']", RegexOptions.IgnoreCase))
            return new SupplierSnapshot(SupplierItemStatus.InStock, price);
        return null;
    }

    [GeneratedRegex(@"<p[^>]*class\s*=\s*[""'](?:[^""']*\s)?price(?:\s[^""']*)?[""'][^>]*>(.*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex WooPriceBlock();

    [GeneratedRegex(@"woocommerce-Price-amount[^>]*>(.*?)</span>\s*(?:</bdi>|</span>)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex WooAmount();

    [GeneratedRegex(@"class\s*=\s*[""'][^""']*\bstock\s+(in-stock|out-of-stock|available-on-backorder)\b[^""']*[""'][^>]*>([^<]{0,80})<", RegexOptions.IgnoreCase)]
    private static partial Regex WooStock();

    private static SupplierSnapshot WithPageStock(SupplierSnapshot snapshot, string html)
    {
        if (snapshot.Status != SupplierItemStatus.InStock || snapshot.Stock is not null)
            return snapshot;
        var stockMatch = StockClass().Match(html);
        var stock = stockMatch.Success ? SupplierParsing.ParseStockText(stockMatch.Groups[1].Value) : null;
        return stock is > 0 ? snapshot with { Stock = stock } : snapshot;
    }

    /// <summary>پیدا کردن Product در یک بلوک JSON-LD (شیء، آرایه یا @graph).</summary>
    private static SupplierSnapshot? ParseJsonLd(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return FindProduct(document.RootElement, 0);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SupplierSnapshot? FindProduct(JsonElement element, int depth)
    {
        if (depth > 6)
            return null;
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                if (FindProduct(child, depth + 1) is { } found)
                    return found;
            return null;
        }
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (IsType(element, "Product") && element.TryGetProperty("offers", out var offers))
        {
            var offer = offers.ValueKind == JsonValueKind.Array ? offers.EnumerateArray().FirstOrDefault() : offers;
            if (offer.ValueKind == JsonValueKind.Object)
            {
                var amount = Number(offer, "price") ?? Number(offer, "lowPrice");
                if (amount is null && offer.TryGetProperty("priceSpecification", out var spec))
                {
                    var first = spec.ValueKind == JsonValueKind.Array ? spec.EnumerateArray().FirstOrDefault() : spec;
                    if (first.ValueKind == JsonValueKind.Object)
                        amount = Number(first, "price");
                }
                var currency = offer.TryGetProperty("priceCurrency", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                var availability = offer.TryGetProperty("availability", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                var name = element.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                var price = amount is { } value ? SupplierParsing.ToToman(value, currency) : null;
                if (availability is null && price is null)
                    return null;
                return availability is null || IsInStock(availability)
                    ? new SupplierSnapshot(SupplierItemStatus.InStock, price, null, null, name)
                    : new SupplierSnapshot(SupplierItemStatus.OutOfStock, price, 0, null, name);
            }
        }

        if (element.TryGetProperty("@graph", out var graph))
            return FindProduct(graph, depth + 1);
        return null;
    }

    private static bool IsType(JsonElement element, string type)
    {
        if (!element.TryGetProperty("@type", out var value))
            return false;
        return value.ValueKind switch
        {
            JsonValueKind.String => string.Equals(value.GetString(), type, StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => value.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.String && string.Equals(v.GetString(), type, StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    private static decimal? Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDecimal(),
            JsonValueKind.String => SupplierParsing.ParseNumber(value.GetString()),
            _ => null,
        } : null;

    /// <summary>
    /// «http://schema.org/InStock»، «InStock»، «LimitedAvailability»، «instock» → موجود.
    /// «OutOfStock»، «SoldOut»، «Discontinued»، «PreOrder»، «BackOrder» → ناموجود (فعلاً قابل خرید فوری نیست).
    /// </summary>
    public static bool IsInStock(string availability)
    {
        var value = availability.Trim().ToLowerInvariant();
        var last = value.Contains('/') ? value[(value.LastIndexOf('/') + 1)..] : value;
        return last.Replace(" ", "").Replace("_", "") is "instock" or "limitedavailability" or "instoreonly" or "onlineonly" or "موجود";
    }

    private static string? Meta(string html, string property)
    {
        foreach (Match tag in MetaTag().Matches(html))
        {
            var attributes = tag.Value;
            var key = Attribute(attributes, "property") ?? Attribute(attributes, "name");
            if (string.Equals(key, property, StringComparison.OrdinalIgnoreCase))
                return Attribute(attributes, "content") is { } content ? WebUtility.HtmlDecode(content) : null;
        }
        return null;
    }

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, $@"\b{Regex.Escape(name)}\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase);
        return match.Success ? (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) : null;
    }

    [GeneratedRegex(@"<script[^>]*type\s*=\s*[""']application/ld\+json[""'][^>]*>(.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex JsonLd();

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTag();

    /// <summary>متن «stock in-stock» ووکامرس در صفحهٔ محصول؛ مثلاً &lt;p class="stock in-stock"&gt;276 عدد در انبار&lt;/p&gt;.</summary>
    [GeneratedRegex(@"class\s*=\s*[""'][^""']*\bin-stock\b[^""']*[""'][^>]*>([^<]{1,80})<", RegexOptions.IgnoreCase)]
    private static partial Regex StockClass();
}

/// <summary>
/// انتخاب آداپتور برای هر تأمین‌کننده. در حالت «تشخیص خودکار» برای هر دامنه یک بار API ووکامرس آزموده می‌شود
/// و نتیجه تا پایان همان اجرا نگه داشته می‌شود.
/// </summary>
public sealed class SupplierAdapterRegistry
{
    private readonly WooCommerceStoreAdapter _woo = new();
    private readonly StructuredDataAdapter _page = new();
    private readonly Dictionary<string, ISupplierAdapter> _detected = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>آداپتور مناسب؛ برای حالت «بررسی نشود» null.</summary>
    public async Task<ISupplierAdapter?> ResolveAsync(SupplierSyncMode mode, string sampleUrl, SupplierFetchContext context)
    {
        switch (mode)
        {
            case SupplierSyncMode.Disabled:
                return null;
            case SupplierSyncMode.WooCommerce:
                return _woo;
            case SupplierSyncMode.StructuredData:
                return _page;
        }

        var origin = WooCommerceStoreAdapter.Origin(sampleUrl) ?? sampleUrl;
        if (!_detected.TryGetValue(origin, out var adapter))
        {
            adapter = await WooCommerceStoreAdapter.DetectAsync(sampleUrl, context) ? _woo : _page;
            _detected[origin] = adapter;
        }
        return adapter;
    }
}
