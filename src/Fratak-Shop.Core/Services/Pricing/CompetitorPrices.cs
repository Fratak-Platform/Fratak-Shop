using System.Globalization;
using System.Text.Json;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Suppliers;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Pricing;

/// <summary>
/// نتیجهٔ جست‌وجوی قیمت رقبا.
/// </summary>
/// <param name="Offers">قیمت فروشگاه‌ها (خام؛ پاک‌سازی و حذف پرت در <see cref="PriceAdvisor.Clean"/>).</param>
/// <param name="Problems">مشکل هر منبع به فارسی (مثلاً «ترب: کپچا»)؛ برای اینکه کارمند بداند چرا فهرست کوتاه است.</param>
/// <param name="MatchedTitle">عنوان کالایی که در ترب انتخاب شد (برای اطمینان از اینکه همان کالاست).</param>
public sealed record CompetitorLookup(IReadOnlyList<CompetitorOffer> Offers, IReadOnlyList<string> Problems, string? MatchedTitle);

/// <summary>
/// جست‌وجوی قیمت همان کالا در سایت‌های دیگر (رایگان، بدون کلید API و بدون سرویس پولی).
/// <para>کجا استفاده می‌شود: دکمهٔ «پیشنهاد قیمت فروش» در فرم محصول پنل مدیریت.</para>
/// </summary>
public interface ICompetitorPriceService
{
    /// <summary>
    /// قیمت فروشگاه‌ها برای یک عبارت. مثال: «هندزفری بلوتوثی هایلو T15» ← دیجی‌کالا ۳۰۰ هزار، فروشگاه X ۲۸۰ هزار، …
    /// <para>هیچ‌وقت استثنا نمی‌دهد؛ خطای هر منبع در <see cref="CompetitorLookup.Problems"/> می‌آید.</para>
    /// </summary>
    Task<CompetitorLookup> SearchAsync(string query, CancellationToken cancellationToken);
}

/// <summary>
/// پیاده‌سازی با دو منبع عمومی و رایگان:
/// <list type="number">
/// <item><b>ترب</b> (موتور مقایسهٔ قیمت): جست‌وجو در <c>api.torob.com/v4/base-product/search</c>، انتخاب نزدیک‌ترین کالا به نام ما،
/// سپس فهرست همهٔ فروشندگان آن کالا از صفحهٔ جزئیات (معمولاً ۵ تا ۳۰ فروشگاه). قیمت‌ها به تومان‌اند.</item>
/// <item><b>دیجی‌کالا</b>: جست‌وجو در <c>api.digikala.com/v1/search</c>؛ قیمت به ریال است و به تومان تبدیل می‌شود.</item>
/// </list>
/// <para>درخواست‌ها از همان کلاینت امن تأمین‌کننده (<see cref="ISupplierHttp"/>: محافظ SSRF، سقف حجم و زمان) می‌روند.
/// نتیجه ۳۰ دقیقه کش می‌شود تا کلیک‌های پشت‌سرهم باعث مسدود شدن نشوند.</para>
/// </summary>
public sealed class CompetitorPriceService(ISupplierHttp http, IMemoryCache cache) : ICompetitorPriceService
{
    /// <summary>بیشترین زمان انتظار برای هر منبع.</summary>
    private static readonly TimeSpan SourceTimeout = TimeSpan.FromSeconds(15);

    public async Task<CompetitorLookup> SearchAsync(string query, CancellationToken cancellationToken)
    {
        query = (query ?? "").Trim();
        if (query.Length > 120)
            query = query[..120];
        if (query.Length < 2)
            return new CompetitorLookup([], ["عبارت جست‌وجو خیلی کوتاه است."], null);

        var key = "competitor-prices:" + PersianText.Normalize(query);
        if (cache.TryGetValue(key, out CompetitorLookup? hit) && hit is not null)
            return hit;

        var torob = GuardAsync("ترب", token => TorobAsync(query, token), cancellationToken);
        var digikala = GuardAsync("دیجی‌کالا", token => DigikalaAsync(query, token), cancellationToken);
        var results = await Task.WhenAll(torob, digikala);

        var offers = results.SelectMany(r => r.Offers).ToList();
        var problems = results.SelectMany(r => r.Problems).ToList();
        var lookup = new CompetitorLookup(offers, problems, results[0].MatchedTitle);
        // فقط نتیجهٔ موفق کش می‌شود؛ خطای موقت (مثلاً کپچای ترب) با کلیک بعدی دوباره امتحان می‌شود.
        if (offers.Count > 0)
            cache.Set(key, lookup, TimeSpan.FromMinutes(30));
        return lookup;
    }

    /// <summary>اجرای یک منبع با سقف زمان و تبدیل هر خطا به پیام فارسی (یک منبع خراب، بقیه را خراب نمی‌کند).</summary>
    private static async Task<CompetitorLookup> GuardAsync(string name, Func<CancellationToken, Task<CompetitorLookup>> run, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SourceTimeout);
        try
        {
            return await run(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CompetitorLookup([], [$"{name}: پاسخ در زمان مجاز نرسید."], null);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            return new CompetitorLookup([], [$"{name}: پاسخ قابل خواندن نبود."], null);
        }
    }

    private async Task<CompetitorLookup> TorobAsync(string query, CancellationToken cancellationToken)
    {
        var url = "https://api.torob.com/v4/base-product/search/?page=0&size=24&source=next_desktop&q=" + Uri.EscapeDataString(query);
        var search = await http.GetAsync(url, expectJson: true, cancellationToken);
        if (Problem("ترب", search) is { } problem)
            return new CompetitorLookup([], [problem], null);

        var cards = CompetitorParsers.TorobSearch(search.Body!);
        var best = cards
            .Select(c => (Card: c, Score: CompetitorParsers.TitleScore(query, c.Title)))
            .Where(x => x.Score >= CompetitorParsers.MinScore)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Card.Price is > 0 ? 0 : 1)
            .Select(x => x.Card)
            .FirstOrDefault();
        if (best is null)
            return new CompetitorLookup([], ["ترب: کالای مشابهی پیدا نشد (نام یا مدل را دقیق‌تر بنویسید)."], null);

        if (best.DetailsUrl is { } details && CompetitorParsers.IsTorobDetailsUrl(details))
        {
            var page = await http.GetAsync(details, expectJson: true, cancellationToken);
            if (Problem("ترب", page) is null)
            {
                var offers = CompetitorParsers.TorobOffers(page.Body!, best.Title);
                if (offers.Count > 0)
                    return new CompetitorLookup(offers, [], best.Title);
            }
        }

        // جزئیات در دسترس نبود: دست‌کم ارزان‌ترین قیمت کارت جست‌وجو.
        return best.Price is > 0
            ? new CompetitorLookup([new CompetitorOffer("ارزان‌ترین فروشگاه ترب", best.Price.Value, "ترب", best.WebUrl, best.Title)],
                ["ترب: فهرست فروشندگان در دسترس نبود؛ فقط ارزان‌ترین قیمت آمد."], best.Title)
            : new CompetitorLookup([], ["ترب: این کالا فعلاً فروشندهٔ موجود ندارد."], best.Title);
    }

    private async Task<CompetitorLookup> DigikalaAsync(string query, CancellationToken cancellationToken)
    {
        var url = "https://api.digikala.com/v1/search/?page=1&q=" + Uri.EscapeDataString(query);
        var response = await http.GetAsync(url, expectJson: true, cancellationToken);
        if (Problem("دیجی‌کالا", response) is { } problem)
            return new CompetitorLookup([], [problem], null);

        var best = CompetitorParsers.Digikala(response.Body!)
            .Select(p => (Item: p, Score: CompetitorParsers.TitleScore(query, p.Title)))
            .Where(x => x.Score >= CompetitorParsers.MinScore)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.Price)
            .Select(x => x.Item)
            .FirstOrDefault();
        return best is null
            ? new CompetitorLookup([], ["دیجی‌کالا: کالای مشابه موجود پیدا نشد."], null)
            : new CompetitorLookup([best], [], null);
    }

    /// <summary>پیام فارسی برای پاسخ ناموفق؛ null یعنی پاسخ سالم است.</summary>
    private static string? Problem(string source, SupplierHttpResponse response)
    {
        if (response.Status == 490 || response.Status == 403 || response.Status == 429)
            return $"{source}: موقتاً درخواست را محدود کرده است (کپچا/محدودیت). چند دقیقه بعد دوباره امتحان کنید.";
        if (response.Body is null || response.Status is < 200 or >= 300)
            return $"{source}: در دسترس نبود ({(response.Status == 0 ? response.Error ?? "خطای اتصال" : "کد " + response.Status)}).";
        return null;
    }
}

/// <summary>یک کارت نتیجهٔ جست‌وجوی ترب.</summary>
/// <param name="Title">نام کالا (name1).</param>
/// <param name="Price">ارزان‌ترین قیمت (تومان)؛ null یعنی ناموجود.</param>
/// <param name="DetailsUrl">آدرس API جزئیات (more_info_url).</param>
/// <param name="Shop">متن فروشگاه کارت (shop_text)، مثلاً «در ۱۲ فروشگاه».</param>
/// <param name="WebUrl">صفحهٔ کالا در torob.com.</param>
public sealed record TorobCard(string Title, decimal? Price, string? DetailsUrl, string? Shop, string? WebUrl);

/// <summary>
/// خواندن پاسخ‌های JSON ترب و دیجی‌کالا و سنجش شباهت عنوان (خالص و تست‌پذیر؛ نمونه‌ها در تست‌ها).
/// <para>ساختارها طبق API عمومی که خود سایت‌ها استفاده می‌کنند:
/// ترب: <c>results[].{name1, price, more_info_url, web_client_absolute_url, shop_text}</c> و
/// جزئیات <c>products_info.result[].{shop_name, shop_id, price, price_text, availability, is_price_unreliable, page_url}</c>؛
/// دیجی‌کالا: <c>data.products[].{id, title_fa, url.uri, default_variant.price.selling_price (ریال)}</c>.</para>
/// </summary>
public static class CompetitorParsers
{
    /// <summary>کمترین شباهت عنوان برای پذیرفتن یک نتیجه (۰ تا ۱).</summary>
    public const double MinScore = 0.6;

    // واژه‌هایی که در شباهت نام کالا معنایی ندارند.
    private static readonly HashSet<string> StopWords = ["و", "با", "از", "در", "به", "برای", "مدل", "رنگ", "اصلی", "اورجینال", "جدید", "the", "with", "for", "and", "model"];

    // نشانهٔ لوازم جانبی: «قاب هدفون T15» همهٔ واژه‌های «هدفون T15» را دارد ولی کالای دیگری است و قیمتش خیلی کمتر.
    private static readonly HashSet<string> AccessoryWords = ["قاب", "کاور", "محافظ", "گلس", "بند", "کیف", "لوازم", "جانبی", "استند", "case", "cover", "strap", "protector"];

    /// <summary>کارت‌های نتیجهٔ جست‌وجوی ترب.</summary>
    public static List<TorobCard> TorobSearch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<TorobCard>();
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var item in results.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var title = Text(item, "name1") ?? Text(item, "name2");
            if (string.IsNullOrWhiteSpace(title))
                continue;
            var web = Text(item, "web_client_absolute_url");
            list.Add(new TorobCard(
                Clip(title, 200)!,
                Money(item, "price") ?? FromText(Text(item, "price_text")),
                Text(item, "more_info_url"),
                Clip(Text(item, "shop_text"), 80),
                web is null ? null : web.StartsWith('/') ? "https://torob.com" + web : web));
        }
        return list;
    }

    /// <summary>
    /// فروشندگان یک کالای ترب. فروشندهٔ ناموجود و قیمتی که خود ترب «غیرقابل اعتماد» علامت زده کنار می‌روند.
    /// </summary>
    public static List<CompetitorOffer> TorobOffers(string json, string? title)
    {
        using var doc = JsonDocument.Parse(json);
        var offers = new List<CompetitorOffer>();
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("products_info", out var info) || info.ValueKind != JsonValueKind.Object
            || !info.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
            return offers;
        foreach (var row in result.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
                continue;
            var shop = Clip(Text(row, "shop_name"), 80);
            if (string.IsNullOrWhiteSpace(shop))
                continue;
            if (row.TryGetProperty("availability", out var available) && available.ValueKind == JsonValueKind.False)
                continue;
            if (row.TryGetProperty("is_price_unreliable", out var unreliable) && unreliable.ValueKind == JsonValueKind.True)
                continue;
            var price = Money(row, "price") ?? FromText(Text(row, "price_text") ?? Text(row, "price_string"));
            if (price is not > 0)
                continue;
            var shopId = Text(row, "shop_id");
            var url = !string.IsNullOrEmpty(shopId) && shopId.All(char.IsDigit) ? $"https://torob.com/shop/{shopId}/" : null;
            offers.Add(new CompetitorOffer(shop, price.Value, "ترب", url, Clip(title, 200)));
        }
        return offers;
    }

    /// <summary>کالاهای موجود جست‌وجوی دیجی‌کالا (قیمت از ریال به تومان).</summary>
    public static List<CompetitorOffer> Digikala(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<CompetitorOffer>();
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("products", out var products) || products.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var p in products.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object)
                continue;
            var title = Text(p, "title_fa") ?? Text(p, "title_en");
            if (string.IsNullOrWhiteSpace(title))
                continue;
            if (!p.TryGetProperty("default_variant", out var variant) || variant.ValueKind != JsonValueKind.Object)
                continue; // ناموجود: دیجی‌کالا تنوع پیش‌فرض نمی‌فرستد
            if (variant.TryGetProperty("price", out var price) && price.ValueKind == JsonValueKind.Object
                && Money(price, "selling_price") is > 0 and var rial)
            {
                var uri = p.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.Object ? Text(u, "uri") : null;
                var id = p.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number ? idElement.GetRawText() : null;
                var link = uri is not null && uri.StartsWith('/') ? "https://www.digikala.com" + uri
                    : id is not null ? $"https://www.digikala.com/product/dkp-{id}/" : null;
                list.Add(new CompetitorOffer("دیجی‌کالا", Math.Round(rial / 10m), "دیجی‌کالا", link, Clip(title, 200)));
            }
        }
        return list;
    }

    /// <summary>
    /// شباهت عنوان یک نتیجه با نام کالای ما (۰ تا ۱): چه سهمی از واژه‌های نام ما در عنوان آمده.
    /// اگر کد مدل/عدد نام ما (مثل T15 یا 256gb) در عنوان نباشد، امتیاز نصف می‌شود (مدل دیگر است)؛
    /// اگر عنوان واژهٔ لوازم جانبی (قاب، کاور، محافظ …) داشته باشد که در نام ما نیست هم نصف می‌شود.
    /// «256gb» با «256 گیگابایت» یکی حساب می‌شود.
    /// <para>مثال: («هدفون هایلو T15»، «هدفون بی‌سیم هایلو مدل T15») ← ۱؛ («هدفون هایلو T15»، «هدفون هایلو GT1») ← ۰٫۳۳؛
    /// («هدفون هایلو T15»، «قاب هدفون هایلو T15») ← ۰٫۵.</para>
    /// </summary>
    public static double TitleScore(string query, string? title)
    {
        var wanted = Words(query);
        if (wanted.Count == 0 || string.IsNullOrWhiteSpace(title))
            return 0;
        var have = Words(title);
        var matched = 0;
        var missingCode = false;
        foreach (var word in wanted)
        {
            if (Found(word, have))
                matched++;
            else if (word.Any(char.IsDigit))
                missingCode = true;
        }
        var score = (double)matched / wanted.Count;
        if (missingCode)
            score /= 2;
        if (have.Any(h => AccessoryWords.Contains(h) && !wanted.Contains(h)))
            score /= 2;
        return score;
    }

    /// <summary>
    /// آیا واژهٔ نام ما در عنوان هست؟ برابر، پیشوند (برای واژه‌های ۴ حرفی به بالا: «هایلو» در «هایلوی»)،
    /// یا عدد با واحد: «256gb» ↔ «256»، و «256» ↔ «256gb». «t15» با «t150» برابر نیست.
    /// </summary>
    private static bool Found(string word, HashSet<string> have)
    {
        if (have.Contains(word))
            return true;
        if (word.Length >= 4 && have.Any(h => h.StartsWith(word, StringComparison.Ordinal)))
            return true;
        var digits = new string(word.TakeWhile(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0)
            return false;
        var unit = word[digits.Length..];
        if (unit.Length > 0 && unit.All(char.IsLetter) && have.Contains(digits))
            return true;
        return unit.Length == 0 && have.Any(h => h.Length > word.Length && h.StartsWith(word, StringComparison.Ordinal) && h[word.Length..].All(char.IsLetter));
    }

    /// <summary>فقط آدرس جزئیات خود ترب دنبال می‌شود (آدرس داخل پاسخ نباید ما را به جای دیگری بفرستد).</summary>
    public static bool IsTorobDetailsUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
           && uri.Host == "api.torob.com" && uri.AbsolutePath.StartsWith("/v4/base-product/details", StringComparison.Ordinal);

    /// <summary>
    /// عدد قیمت از متن فارسی. مثال: «۲۸۰٬۰۰۰ تومان» ← ۲۸۰۰۰۰. متن بدون رقم ← null.
    /// </summary>
    public static decimal? FromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var normalized = PersianText.Normalize(text);
        var digits = new string(normalized.SkipWhile(c => !char.IsAsciiDigit(c))
            .TakeWhile(c => char.IsAsciiDigit(c) || c is ',' or '٬' or '٫' or '.' or ' ')
            .Where(char.IsAsciiDigit).ToArray());
        return digits.Length is > 0 and <= 15 ? decimal.Parse(digits, CultureInfo.InvariantCulture) : null;
    }

    private static HashSet<string> Words(string text)
        => PersianText.Tokenize(text).Where(w => !StopWords.Contains(w) && (w.Length > 1 || w.Any(char.IsDigit))).ToHashSet();

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        } : null;

    private static decimal? Money(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            return number > 0 ? number : null;
        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return parsed > 0 ? parsed : null;
        return null;
    }

    private static string? Clip(string? text, int max)
        => text is null ? null : text.Length <= max ? text.Trim() : text[..max].Trim();
}
