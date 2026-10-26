using System.Text.Json;
using AngleSharp.Html.Parser;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.SeoEngine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Seo.Engine;

// ═══════════════════════════════════════════════════════════════════════════
//  تحقیق کلمه و تحلیل رقبا (رایگان و خودکار؛ بدون هیچ API پولی و بدون واردکردن دستی آدرس)
//  ۱) ISeoWebSource (پیاده‌سازی در Web/Infrastructure/Seo با HttpClient):
//     • پیشنهادهای جست‌وجو (autocomplete گوگل و بینگ) → عبارت‌هایی که مردم واقعاً جست‌وجو می‌کنند.
//     • نتایج برتر جست‌وجو (صفحهٔ HTML بینگ / داک‌داک‌گو) → آدرس و عنوان رقبا.
//     • دریافت HTML صفحهٔ رقیب.
//  ۲) SeoParsers: تبدیل HTML/JSON این منابع به داده (در Core تا با نمونهٔ ثابت تست شود).
//  ۳) SeoResearchService: کش در دیتابیس (هر عبارت حداکثر هفته‌ای یک بار)، سقف درخواست در هر اجرا،
//     و ساخت «بینش رقبا» (CompetitorInsight): میانگین طول عنوان و متن، واژه‌های مشترک صفحه‌های برتر،
//     تیترهای رایج، زبان نامک‌ها و … که نویسنده و برنامه‌ریز کلمه از آن یاد می‌گیرند.
//  اگر سرور به این سایت‌ها دسترسی نداشته باشد (مثلاً تحریم/فیلتر)، همه چیز بدون خطا ادامه می‌یابد و فقط
//  داده‌های داخلی سایت استفاده می‌شود؛ وضعیت هر منبع در داشبورد موتور سئو دیده می‌شود.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>یک نتیجهٔ جست‌وجو.</summary>
public sealed record SerpItem(string Url, string Title, string? Snippet);

/// <summary>نتیجهٔ یک درخواست اینترنتی: موفق/ناموفق، مقدار، منبعی که جواب داد و متن خطا.</summary>
public sealed record WebResult<T>(bool Ok, T? Value, string? Source, string? Error)
{
    public static WebResult<T> Success(T value, string source) => new(true, value, source, null);
    public static WebResult<T> Fail(string error, string? source = null) => new(false, default, source, error);
}

/// <summary>
/// دسترسی به منابع رایگان اینترنتی. پیاده‌سازی: Web/Infrastructure/Seo/SeoWebSource (با محدودیت سرعت و حجم).
/// برای افزودن منبع تازه (مثلاً موتور جست‌وجوی دیگر) فقط همان کلاس را گسترش دهید.
/// </summary>
public interface ISeoWebSource
{
    /// <summary>پیشنهادهای جست‌وجو برای یک عبارت. مثال: «هدفون سونی» → [«هدفون سونی wh-1000xm5»، «هدفون سونی بی سیم» …].</summary>
    Task<WebResult<IReadOnlyList<string>>> SuggestAsync(string query, CancellationToken cancellationToken);

    /// <summary>نتایج برتر جست‌وجو (حداکثر ۱۰ مورد).</summary>
    Task<WebResult<IReadOnlyList<SerpItem>>> SearchAsync(string query, CancellationToken cancellationToken);

    /// <summary>HTML یک صفحهٔ رقیب (فقط text/html، با سقف حجم و زمان).</summary>
    Task<WebResult<string>> FetchPageAsync(string url, CancellationToken cancellationToken);
}

/// <summary>منبعی که همیشه «در دسترس نیست» برمی‌گرداند (برای تست و وقتی تحقیق اینترنتی خاموش است).</summary>
public sealed class OfflineSeoWebSource : ISeoWebSource
{
    public Task<WebResult<IReadOnlyList<string>>> SuggestAsync(string query, CancellationToken cancellationToken) =>
        Task.FromResult(WebResult<IReadOnlyList<string>>.Fail("offline"));

    public Task<WebResult<IReadOnlyList<SerpItem>>> SearchAsync(string query, CancellationToken cancellationToken) =>
        Task.FromResult(WebResult<IReadOnlyList<SerpItem>>.Fail("offline"));

    public Task<WebResult<string>> FetchPageAsync(string url, CancellationToken cancellationToken) =>
        Task.FromResult(WebResult<string>.Fail("offline"));
}

/// <summary>تحلیل یک صفحهٔ رقیب.</summary>
public sealed class CompetitorPage
{
    public string Url { get; set; } = "";
    public string Host { get; set; } = "";
    public string Title { get; set; } = "";
    public string? MetaDescription { get; set; }
    public string? H1 { get; set; }
    public List<string> Headings { get; set; } = [];
    public int Words { get; set; }
    /// <summary>مهم‌ترین ریشه‌ها با تعداد تکرار (حداکثر ۴۰).</summary>
    public Dictionary<string, int> Terms { get; set; } = [];
    /// <summary>نمونهٔ واژهٔ کامل هر ریشه (برای نمایش و نوشتن). مثال: «هدفون» → «هدفون‌ها».</summary>
    public Dictionary<string, string> Surface { get; set; } = [];
    public bool HasFaq { get; set; }
    public bool LatinSlug { get; set; }
}

/// <summary>یک واژهٔ مهم صفحه‌های برتر: ریشه، شکل نمایشی و تعداد صفحه‌هایی که آن را دارند.</summary>
public sealed record CompetitorTerm(string Stem, string Word, int Pages, double Weight);

/// <summary>
/// چیزی که موتور از صفحه‌های برتر نتایج یاد می‌گیرد. همه چیز آماری است و هیچ متنی از رقیب کپی نمی‌شود.
/// </summary>
public sealed class CompetitorInsight
{
    public string Query { get; set; } = "";
    public int PagesAnalyzed { get; set; }
    public double AverageTitleLength { get; set; }
    public int MedianWords { get; set; }
    /// <summary>واژه‌هایی که حداقل در دو صفحهٔ برتر آمده‌اند (به ترتیب اهمیت).</summary>
    public List<CompetitorTerm> CommonTerms { get; set; } = [];
    /// <summary>واژه‌های رایج در «عنوان» رقبا. مثال: «قیمت»، «خرید»، «مشخصات».</summary>
    public List<CompetitorTerm> TitleTerms { get; set; } = [];
    /// <summary>سهم نامک‌های انگلیسی در نتایج (۰ تا ۱).</summary>
    public double LatinSlugRatio { get; set; }
    public double FaqRatio { get; set; }
    public List<string> SampleTitles { get; set; } = [];
    public List<string> Hosts { get; set; } = [];
    /// <summary>پیشنهادهای جست‌وجوی مرتبط (autocomplete).</summary>
    public List<string> Suggestions { get; set; } = [];
    /// <summary>منبع‌هایی که در دسترس نبودند (برای نمایش در پیشنهاد).</summary>
    public List<string> Errors { get; set; } = [];

    /// <summary>هیچ دادهٔ بیرونی به دست نیامد.</summary>
    public bool IsEmpty => PagesAnalyzed == 0 && Suggestions.Count == 0 && SampleTitles.Count == 0;
}

/// <summary>تبدیل پاسخ منابع اینترنتی به داده. ایستا و قابل تست با نمونه HTML/JSON.</summary>
public static class SeoParsers
{
    /// <summary>
    /// ارتباط یک صفحهٔ رقیب با عبارت جست‌وجو: سهم ریشه‌های معنادار عبارت که در عنوان، H1 یا آدرس صفحه آمده‌اند (۰ تا ۱).
    /// مثال: عبارت «شامپو کراتینه مو» و عنوان «خرید شامپو کراتینه + قیمت» → ۲ از ۳ = ۰٫۶۷ (مرتبط).
    /// عنوان «صفحهٔ اصلی فروشگاه X» → ۰ (کنار گذاشته می‌شود). عبارت بدون ریشهٔ معنادار → ۱ (فیلتر نمی‌شود).
    /// </summary>
    public static double Relevance(string query, CompetitorPage page)
    {
        var wanted = PersianText.ContentStems(query).Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
            return 1;
        string path;
        try
        {
            path = Uri.UnescapeDataString(Uri.TryCreate(page.Url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : "").Replace('-', ' ').Replace('_', ' ');
        }
        catch (UriFormatException)
        {
            path = "";
        }
        var have = PersianText.ContentStems($"{page.Title} {page.H1} {path}").ToHashSet(StringComparer.Ordinal);
        return wanted.Count(have.Contains) / (double)wanted.Count;
    }

    /// <summary>
    /// پاسخ autocomplete گوگل (client=firefox) و بینگ (osjson) قالب یکسانی دارد: ["عبارت", ["پیشنهاد۱", "پیشنهاد۲", …]].
    /// </summary>
    public static List<string> ParseSuggestions(string json)
    {
        var result = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() >= 2
                && document.RootElement[1].ValueKind == JsonValueKind.Array)
            {
                foreach (var item in document.RootElement[1].EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                        result.Add(PersianText.Clean(item.GetString()));
            }
        }
        catch (JsonException)
        {
            // پاسخ غیر JSON (صفحهٔ خطا/کپچا) = بدون پیشنهاد
        }
        return result.Distinct(StringComparer.Ordinal).Take(10).ToList();
    }

    /// <summary>نتایج صفحهٔ HTML بینگ: هر نتیجه «li.b_algo» با «h2 a» (آدرس و عنوان) و «.b_caption p» (خلاصه).</summary>
    public static List<SerpItem> ParseBing(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        var items = new List<SerpItem>();
        foreach (var result in document.QuerySelectorAll("li.b_algo"))
        {
            var link = result.QuerySelector("h2 a");
            var href = link?.GetAttribute("href");
            if (link is null || !IsHttp(href))
                continue;
            items.Add(new SerpItem(href!, PersianText.Clean(link.TextContent),
                PersianText.Clean(result.QuerySelector(".b_caption p, p")?.TextContent)));
        }
        return Distinct(items);
    }

    /// <summary>
    /// نتایج نسخهٔ HTML داک‌داک‌گو: «a.result__a» (آدرس واقعی در پارامتر uddg آدرس ریدایرکت) و «.result__snippet».
    /// </summary>
    public static List<SerpItem> ParseDuckDuckGo(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        var items = new List<SerpItem>();
        foreach (var result in document.QuerySelectorAll(".result"))
        {
            if (result.ClassList.Contains("result--ad"))
                continue; // تبلیغ
            var link = result.QuerySelector("a.result__a");
            var href = link?.GetAttribute("href");
            if (link is null || string.IsNullOrWhiteSpace(href))
                continue;
            var url = UnwrapDuckDuckGo(href);
            if (!IsHttp(url))
                continue;
            items.Add(new SerpItem(url!, PersianText.Clean(link.TextContent), PersianText.Clean(result.QuerySelector(".result__snippet")?.TextContent)));
        }
        return Distinct(items);
    }

    /// <summary>«//duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2F&amp;rut=…» → «https://example.com/».</summary>
    public static string? UnwrapDuckDuckGo(string href)
    {
        var marker = href.IndexOf("uddg=", StringComparison.Ordinal);
        if (marker < 0)
            return href.StartsWith("//", StringComparison.Ordinal) ? "https:" + href : href;
        var value = href[(marker + 5)..];
        var end = value.IndexOf('&');
        if (end >= 0)
            value = value[..end];
        return Uri.UnescapeDataString(value);
    }

    /// <summary>
    /// تحلیل HTML صفحهٔ رقیب: عنوان، توضیحات، تیترها، تعداد واژهٔ متن اصلی و واژه‌های مهم.
    /// منو، فوتر، اسکریپت و فرم‌ها حذف می‌شوند تا فقط متن اصلی شمرده شود.
    /// </summary>
    public static CompetitorPage ParsePage(string url, string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        foreach (var junk in document.QuerySelectorAll("script,style,noscript,template,svg,nav,footer,header,form,iframe,aside").ToList())
            junk.Remove();

        var page = new CompetitorPage
        {
            Url = url,
            Host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : "",
            Title = PersianText.Clean(document.Title),
            MetaDescription = PersianText.Clean(document.QuerySelector("meta[name=description]")?.GetAttribute("content")),
            H1 = PersianText.Clean(document.QuerySelector("h1")?.TextContent),
        };
        page.Headings = document.QuerySelectorAll("h2,h3")
            .Select(h => PersianText.Clean(h.TextContent))
            .Where(h => h.Length is > 2 and < 120)
            .Distinct(StringComparer.Ordinal)
            .Take(30)
            .ToList();
        page.HasFaq = html.Contains("FAQPage", StringComparison.Ordinal) || page.Headings.Any(h => h.Contains("سوالات متداول") || h.Contains("پرسش‌های متداول") || h.Contains("پرسش های متداول"));
        page.LatinSlug = uri is not null && PersianText.PersianRatio(Uri.UnescapeDataString(uri.AbsolutePath)) < 0.5;

        var main = document.QuerySelector("main, article, [role=main], #content, .content, .product") ?? document.Body;
        var text = PersianText.Clean(main?.TextContent);
        var tokens = PersianText.Tokenize(text);
        page.Words = tokens.Count;

        var terms = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            if (PersianText.IsStopWord(token) || token.All(char.IsDigit))
                continue;
            var stem = PersianText.Stem(token);
            terms[stem] = terms.GetValueOrDefault(stem) + 1;
            page.Surface.TryAdd(stem, token);
        }
        page.Terms = terms.OrderByDescending(t => t.Value).Take(40).ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
        page.Surface = page.Surface.Where(s => page.Terms.ContainsKey(s.Key)).ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
        return page;
    }

    /// <summary>
    /// ترکیب چند صفحهٔ رقیب به یک بینش آماری. واژه‌ای «مشترک» است که حداقل در دو صفحه (یا نیمی از صفحه‌ها) آمده باشد.
    /// </summary>
    public static CompetitorInsight BuildInsight(string query, IReadOnlyList<CompetitorPage> pages, IReadOnlyList<SerpItem> serp, IReadOnlyList<string> suggestions)
    {
        var insight = new CompetitorInsight
        {
            Query = query,
            PagesAnalyzed = pages.Count,
            Suggestions = suggestions.ToList(),
            SampleTitles = serp.Select(s => s.Title).Where(t => t.Length > 0).Take(6).ToList(),
            Hosts = serp.Select(s => HostOf(s.Url)).Where(h => h.Length > 0).Distinct().Take(6).ToList(),
        };

        var titles = serp.Select(s => s.Title).Where(t => t.Length > 0).ToList();
        if (titles.Count > 0)
            insight.AverageTitleLength = Math.Round(titles.Average(t => t.Length), 1);
        var serpUrls = serp.Select(s => s.Url).ToList();
        if (serpUrls.Count > 0)
            insight.LatinSlugRatio = Math.Round(serpUrls.Count(u => Uri.TryCreate(u, UriKind.Absolute, out var x)
                && PersianText.PersianRatio(Uri.UnescapeDataString(x.AbsolutePath)) < 0.5 && x.AbsolutePath.Length > 3) / (double)serpUrls.Count, 2);

        // واژه‌های رایج در عنوان نتایج (حتی بدون دریافت صفحه‌ها قابل محاسبه است).
        var queryStems = new HashSet<string>(PersianText.ContentStems(query), StringComparer.Ordinal);
        insight.TitleTerms = CountTerms(titles.Select(t => PersianText.Tokenize(t)), queryStems, Math.Max(2, titles.Count / 3));

        if (pages.Count > 0)
        {
            var words = pages.Select(p => p.Words).Where(w => w > 0).OrderBy(w => w).ToList();
            insight.MedianWords = words.Count == 0 ? 0 : words[words.Count / 2];
            insight.FaqRatio = Math.Round(pages.Count(p => p.HasFaq) / (double)pages.Count, 2);

            var minPages = pages.Count >= 3 ? 2 : 1;
            var stems = pages.SelectMany(p => p.Terms.Keys).Distinct(StringComparer.Ordinal);
            insight.CommonTerms = stems
                .Select(stem =>
                {
                    var present = pages.Where(p => p.Terms.ContainsKey(stem)).ToList();
                    var surface = present.Select(p => p.Surface.GetValueOrDefault(stem)).FirstOrDefault(s => s is not null) ?? stem;
                    var weight = present.Count * Math.Log(1 + present.Sum(p => p.Terms[stem]));
                    return new CompetitorTerm(stem, surface, present.Count, Math.Round(weight, 3));
                })
                .Where(t => t.Pages >= minPages && !queryStems.Contains(t.Stem) && t.Word.Length > 1)
                .OrderByDescending(t => t.Weight)
                .Take(25)
                .ToList();
        }

        return insight;
    }

    private static List<CompetitorTerm> CountTerms(IEnumerable<List<string>> documents, HashSet<string> exclude, int minDocs)
    {
        var counts = new Dictionary<string, (int Docs, string Word)>(StringComparer.Ordinal);
        foreach (var tokens in documents)
        {
            foreach (var token in tokens.Where(t => !PersianText.IsStopWord(t) && !t.All(char.IsDigit)).Distinct(StringComparer.Ordinal))
            {
                var stem = PersianText.Stem(token);
                if (exclude.Contains(stem))
                    continue;
                counts[stem] = counts.TryGetValue(stem, out var c) ? (c.Docs + 1, c.Word) : (1, token);
            }
        }
        return counts.Where(c => c.Value.Docs >= minDocs)
            .OrderByDescending(c => c.Value.Docs)
            .Take(12)
            .Select(c => new CompetitorTerm(c.Key, c.Value.Word, c.Value.Docs, c.Value.Docs))
            .ToList();
    }

    public static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant().Replace("www.", "") : "";

    private static bool IsHttp(string? url) =>
        url is not null && (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

    private static List<SerpItem> Distinct(List<SerpItem> items) =>
        items.GroupBy(i => i.Url, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).Take(10).ToList();
}

/// <summary>
/// سرویس تحقیق با کش دیتابیسی و سقف درخواست. یک نمونه برای هر اجرای موتور (Scoped).
/// استفاده: SeoEngineService → <see cref="ResearchAsync"/> برای هر صفحه‌ای که پیشنهاد می‌گیرد.
/// </summary>
public sealed class SeoResearchService(FratakShopDbContext context, ISeoWebSource web, ILogger<SeoResearchService> logger)
{
    /// <summary>مدت اعتبار کش موفق (۷ روز) و ناموفق (۶ ساعت؛ تا منبع قطع، مدام امتحان نشود).</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);
    public static readonly TimeSpan FailureLifetime = TimeSpan.FromHours(6);

    /// <summary>حداکثر صفحهٔ رقیب که برای هر عبارت خوانده می‌شود.</summary>
    public const int PagesPerQuery = 4;

    /// <summary>
    /// سقف درخواست اینترنتی (غیر کش) در این نمونه. موتور در هر اجرا مقدار می‌دهد تا اجرا طولانی نشود.
    /// صفر = فقط از کش بخوان.
    /// </summary>
    public int NetworkBudget { get; set; } = 40;

    /// <summary>دامنه‌هایی که رقیب حساب نمی‌شوند (خود سایت، شبکه‌های اجتماعی، ویدیو، دانشنامه‌ها برای محصول).</summary>
    public HashSet<string> ExcludedHosts { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com", "aparat.com", "instagram.com", "facebook.com", "twitter.com", "x.com", "t.me", "pinterest.com",
        "linkedin.com", "tiktok.com", "reddit.com", "virgool.io", "namasha.com", "telewebion.com",
    };

    /// <summary>نام سایت خودمان (نتایجی که عنوانشان نام سایت را دارد، رقیب نیستند).</summary>
    public string? OwnSiteName { get; set; }

    /// <summary>
    /// تحقیق کامل برای یک عبارت: پیشنهادهای جست‌وجو + نتایج برتر + تحلیل حداکثر ۴ صفحهٔ رقیب.
    /// commercial=true برای محصول/دسته/برند: ویکی‌پدیا و دانشنامه‌ها رقیب فروش نیستند و حذف می‌شوند.
    /// </summary>
    public async Task<CompetitorInsight> ResearchAsync(string query, bool commercial, CancellationToken cancellationToken)
    {
        query = PersianText.Clean(query);
        var errors = new List<string>();
        var suggestions = await SuggestAsync(query, cancellationToken);
        if (suggestions is null) errors.Add("پیشنهاد جست‌وجو");

        var serp = await SearchAsync(query, cancellationToken);
        if (serp is null) errors.Add("نتایج جست‌وجو");

        var competitors = (serp ?? [])
            .Where(s => IsCompetitor(s, commercial))
            .ToList();

        var pages = new List<CompetitorPage>();
        foreach (var item in competitors.Take(PagesPerQuery + 2))
        {
            if (pages.Count >= PagesPerQuery)
                break;
            var page = await PageAsync(item.Url, cancellationToken);
            // کیفیت تحلیل (اصلاحیه): صفحهٔ کم‌محتوا یا بی‌ربط (عنوان/H1 با عبارت جست‌وجو هم‌پوشانی ندارد، مثل صفحهٔ اصلی
            // یک فروشگاه یا مقالهٔ نامرتبط) کنار گذاشته می‌شود تا کلمه‌های پرتکرار رقبا آلوده نشوند.
            if (page is not null && page.Words >= 80 && SeoParsers.Relevance(query, page) >= 0.5)
                pages.Add(page);
        }

        var insight = SeoParsers.BuildInsight(query, pages, competitors, suggestions ?? []);
        insight.Errors = errors;
        return insight;
    }

    /// <summary>پیشنهادهای جست‌وجو (کش‌شده). null = منبع در دسترس نبود.</summary>
    public async Task<List<string>?> SuggestAsync(string query, CancellationToken cancellationToken)
    {
        var key = PersianText.Normalize(query);
        if (key.Length < 2)
            return [];
        return await CachedAsync<List<string>>(SeoResearchKind.Suggest, key, CacheLifetime, async ct =>
        {
            var result = await web.SuggestAsync(query, ct);
            return result.Ok ? (true, result.Value?.ToList() ?? [], result.Source) : (false, null, result.Error);
        }, cancellationToken);
    }

    /// <summary>نتایج برتر (کش‌شده). null = منبع در دسترس نبود.</summary>
    public async Task<List<SerpItem>?> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var key = PersianText.Normalize(query);
        if (key.Length < 2)
            return [];
        return await CachedAsync<List<SerpItem>>(SeoResearchKind.Serp, key, CacheLifetime, async ct =>
        {
            var result = await web.SearchAsync(query, ct);
            return result.Ok ? (true, result.Value?.ToList() ?? [], result.Source) : (false, null, result.Error);
        }, cancellationToken);
    }

    /// <summary>تحلیل یک صفحهٔ رقیب (کش‌شده ۱۴ روز). null = دریافت نشد.</summary>
    public async Task<CompetitorPage?> PageAsync(string url, CancellationToken cancellationToken)
    {
        var key = url.Length > 440 ? url[..440] : url;
        return await CachedAsync<CompetitorPage>(SeoResearchKind.Page, key, CacheLifetime * 2, async ct =>
        {
            var result = await web.FetchPageAsync(url, ct);
            if (!result.Ok || string.IsNullOrWhiteSpace(result.Value))
                return (false, null, result.Error);
            return (true, SeoParsers.ParsePage(url, result.Value), result.Source);
        }, cancellationToken);
    }

    private bool IsCompetitor(SerpItem item, bool commercial)
    {
        var host = SeoParsers.HostOf(item.Url);
        if (host.Length == 0 || ExcludedHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal)))
            return false;
        if (commercial && (host.EndsWith("wikipedia.org", StringComparison.Ordinal) || host.EndsWith("wikiwand.com", StringComparison.Ordinal)))
            return false;
        if (!string.IsNullOrWhiteSpace(OwnSiteName) && item.Title.Contains(OwnSiteName, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(SeoEngineHost.Current) && host == SeoEngineHost.Current.Replace("www.", ""))
            return false;
        return true;
    }

    /// <summary>
    /// خواندن از کش یا اجرای درخواست و ذخیرهٔ نتیجه. بودجهٔ شبکه که تمام شود، فقط کش خوانده می‌شود (null).
    /// خطای ذخیره (مثلاً دو اجرای هم‌زمان با یک کلید) نادیده گرفته می‌شود؛ کش فقط برای سرعت است.
    /// </summary>
    private async Task<T?> CachedAsync<T>(SeoResearchKind kind, string key, TimeSpan lifetime,
        Func<CancellationToken, Task<(bool Ok, T? Value, string? SourceOrError)>> fetch, CancellationToken cancellationToken) where T : class
    {
        // ستون Key حداکثر ۴۵۰ نویسه است؛ عبارت/آدرس خیلی بلند کوتاه می‌شود تا ذخیرهٔ کش خطای SQL ندهد.
        key = Truncate(key, 440)!;
        var now = DateTimeOffset.UtcNow;
        var cached = await context.SeoResearchCaches.AsTracking()
            .FirstOrDefaultAsync(c => c.Kind == kind && c.Key == key, cancellationToken);
        if (cached is not null && cached.ExpiresAt > now)
            return cached.Ok ? SeoJson.Deserialize<T>(cached.Json) : null;

        if (NetworkBudget <= 0)
            return cached is { Ok: true } ? SeoJson.Deserialize<T>(cached.Json) : null; // کهنه ولی بهتر از هیچ
        NetworkBudget--;

        (bool Ok, T? Value, string? SourceOrError) outcome;
        try
        {
            outcome = await fetch(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "SEO research request failed for {Kind} {Key}", kind, key);
            outcome = (false, null, ex.GetType().Name);
        }

        cached ??= context.SeoResearchCaches.Add(new SeoResearchCache { Kind = kind, Key = key }).Entity;
        cached.Ok = outcome.Ok;
        cached.Json = outcome.Ok ? SeoJson.Serialize(outcome.Value) : "null";
        cached.Source = Truncate(outcome.SourceOrError, 40);
        cached.FetchedAt = now;
        cached.ExpiresAt = now + (outcome.Ok ? lifetime : FailureLifetime);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.LogDebug(ex, "SEO research cache save skipped for {Key}", key);
            context.Entry(cached).State = EntityState.Detached;
        }

        return outcome.Ok ? outcome.Value : null;
    }

    private static string? Truncate(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];
}

/// <summary>
/// دامنهٔ خود سایت (برای حذف از فهرست رقبا). صفحه‌های پنل هنگام باز شدن مقدار می‌دهند؛ چون اجرای پس‌زمینه
/// درخواست HTTP ندارد که دامنه را از آن بخواند.
/// </summary>
public static class SeoEngineHost
{
    private static volatile string? _current;

    public static string? Current
    {
        get => _current;
        set => _current = string.IsNullOrWhiteSpace(value) ? _current : value.Trim().ToLowerInvariant();
    }
}
