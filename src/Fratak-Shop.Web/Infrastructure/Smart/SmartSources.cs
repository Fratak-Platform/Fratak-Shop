using Fratak_Shop.Core.Services.Pricing;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Services.Suppliers;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Web.Infrastructure.Smart;

/// <summary>ردیف مشخصات قابل ذخیره در کش تحقیق (تاپل در JSON ذخیره نمی‌شود).</summary>
public sealed record SpecRow(string Key, string Value);

/// <summary>جزئیات کش‌شدهٔ کالای دیجی‌کالا.</summary>
public sealed record CachedProductDetails(List<SpecRow> Specs, List<string> Images);

/// <summary>
/// دور بیستم: یک فروشگاه در نتایج جست‌وجو. Match = شباهت عنوان نتیجه با کالای ما (۰ تا ۱).
/// مثال: («https://mobile.ir/p/t15»، «خرید هدفون هایلو T15»، ۰٫۸).
/// </summary>
public sealed record SerpShop(string Url, string Title, double Match);

/// <summary>دور بیستم: نتیجهٔ جست‌وجوی تصویر قابل ذخیره در کش (تاپل/رکورد Core مستقیم ذخیره نمی‌شود).</summary>
public sealed record CachedImageHit(string Url, string? Title, string? PageUrl);

/// <summary>
/// دریافت دادهٔ بیرونی برای محتوای هوشمند (B1–B13) با رعایت کامل ادب و ایمنی:
/// <list type="bullet">
/// <item>هر منبع از راه <see cref="IExternalSourceGate"/>: فاصلهٔ حداقل بین درخواست‌ها و «استراحت» خودکار بعد از محدودیت (۴۲۹/۴۰۳/کپچا).</item>
/// <item>همهٔ درخواست‌ها با محافظ SSRF (<see cref="ISupplierHttp"/>) و سقف حجم.</item>
/// <item>نتیجه‌ها در <see cref="ISmartResearchStore"/> کش می‌شوند (جست‌وجو ۷ روز، جزئیات ۳۰ روز) تا یک کالا دو بار پرسیده نشود (B22).</item>
/// <item>اگر «دستیار محلی» روشن باشد، اول از اینترنت کامپیوتر مدیر امتحان می‌شود (A31) و در صورت شکست مستقیم.</item>
/// </list>
/// <para>هیچ متدی استثنا بیرون نمی‌دهد؛ شکست ← فهرست خالی/null و ادامهٔ کار بدون آن منبع.</para>
/// <para>محل استفاده: <see cref="SmartImageFinder"/> و <see cref="SmartOrchestrator"/>.</para>
/// </summary>
public sealed class SmartSources(
    ISupplierHttp http,
    IExternalSourceGate gate,
    ISeoWebSource web,
    HelperBroker broker,
    ISmartConfigService config,
    ISmartResearchStore store,
    ILogger<SmartSources> logger)
{
    public const string Digikala = "دیجی‌کالا (هوشمند)";
    public const string BingImages = "bing-images";
    public const string Basalam = "باسلام (هوشمند)";
    public const string Supplier = "صفحهٔ فروشگاه";

    private static readonly TimeSpan PoliteInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SearchTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan DetailsTtl = TimeSpan.FromDays(30);

    /// <summary>
    /// یک درخواست مؤدب. ترتیب: دستیار محلی (اگر روشن و مجاز) ← مستقیم از سرور با دروازهٔ منبع.
    /// </summary>
    public async Task<string?> GetAsync(string source, string url, bool json, CancellationToken cancellationToken)
    {
        try
        {
            var options = await config.GetAsync(cancellationToken);
            if (options.UseHelperFetch && broker.IsOnline(DateTimeOffset.UtcNow))
            {
                var viaHelper = await broker.RunAsync("fetch", url, json ? "application/json" : "text/html", null, cancellationToken);
                if (viaHelper is { Status: >= 200 and < 300, Body.Length: > 0 })
                    return viaHelper.Body;
            }

            using var lease = await gate.TryEnterAsync(source, PoliteInterval, cancellationToken);
            if (lease is null)
                return null;
            var response = await http.GetAsync(url, json, cancellationToken);
            if (json)
            {
                switch (CompetitorPriceService.Classify(response))
                {
                    case SourceVerdict.Ok:
                        gate.ReportSuccess(source);
                        return response.Body;
                    case SourceVerdict.Blocked:
                        gate.ReportBlocked(source, null, $"کد {response.Status}");
                        return null;
                    default:
                        gate.ReportFailure(source, response.Error ?? "کد " + response.Status);
                        return null;
                }
            }
            if (response.IsSuccess)
            {
                if (CompetitorPriceService.LooksLikeChallenge(response.Body!))
                {
                    gate.ReportBlocked(source, null, "صفحهٔ کپچا");
                    return null;
                }
                gate.ReportSuccess(source);
                return response.Body;
            }
            if (response.Status is 403 or 429 or 503)
                gate.ReportBlocked(source, null, $"کد {response.Status}");
            else
                gate.ReportFailure(source, response.Error ?? "کد " + response.Status);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Smart source {Source} failed for {Url}", source, url);
            return null;
        }
    }

    /// <summary>
    /// پیدا کردن همین کالا در دیجی‌کالا: جست‌وجوی نام (انگلیسی اگر هست) و پذیرش فقط شباهت ≥ ۰٫۶ با عدد مدل یکسان.
    /// مثال: «Haylou T15» ← کالای «هدفون بی‌سیم هایلو مدل T15»؛ «Haylou T16» پذیرفته نمی‌شود.
    /// </summary>
    public async Task<DigikalaHit?> FindDigikalaAsync(SmartProductSubject subject, CancellationToken cancellationToken)
    {
        var query = subject.SearchQuery;
        var key = "dk:search:" + query;
        var hits = await store.GetAsync<List<DigikalaHit>>(key, cancellationToken);
        if (hits is null)
        {
            var body = await GetAsync(Digikala, "https://api.digikala.com/v1/search/?page=1&q=" + Uri.EscapeDataString(query), true, cancellationToken);
            if (body is null)
                return null;
            hits = SmartParsers.DigikalaSearch(body).Take(15).ToList();
            await store.SetAsync(key, "digikala", hits, SearchTtl, cancellationToken);
        }
        return hits
            .Select(h => (Hit: h, Score: Math.Max(SmartParsers.TitleMatch(subject.Name, h.Title),
                h.EnglishTitle is null ? 0 : SmartParsers.TitleMatch(subject.EnglishName ?? subject.Name, h.EnglishTitle))))
            .Where(x => x.Score >= 0.6)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Hit)
            .FirstOrDefault();
    }

    public async Task<CachedProductDetails?> DigikalaDetailsAsync(long id, CancellationToken cancellationToken)
    {
        var key = "dk:product:" + id;
        var cached = await store.GetAsync<CachedProductDetails>(key, cancellationToken);
        if (cached is not null)
            return cached;
        var body = await GetAsync(Digikala, $"https://api.digikala.com/v2/product/{id}/", true, cancellationToken);
        if (body is null)
            return null;
        var details = SmartParsers.DigikalaProduct(body);
        var result = new CachedProductDetails(details.Specs.Select(s => new SpecRow(s.Key, s.Value)).ToList(), details.Images.ToList());
        await store.SetAsync(key, "digikala", result, DetailsTtl, cancellationToken);
        return result;
    }

    public async Task<List<SourceReview>> DigikalaReviewsAsync(long id, CancellationToken cancellationToken)
    {
        var key = "dk:reviews:" + id;
        var cached = await store.GetAsync<List<SourceReview>>(key, cancellationToken);
        if (cached is not null)
            return cached;
        var body = await GetAsync(Digikala, $"https://api.digikala.com/v1/product/{id}/comments/?page=1&order=most_liked", true, cancellationToken);
        if (body is null)
            return [];
        var reviews = SmartParsers.DigikalaComments(body);
        await store.SetAsync(key, "digikala", reviews, SearchTtl, cancellationToken);
        return reviews;
    }

    /// <summary>
    /// مشخصات و تصویرهای یک صفحهٔ فروشگاه/تأمین‌کننده: جدول/فهرست مشخصات + JSON-LD، و تصویرها از og:image و JSON-LD
    /// (اولی = تصویر اصلی صفحه). مثال: صفحهٔ ووکامرس ← ۱۲ ردیف مشخصات و ۴ تصویر.
    /// </summary>
    public async Task<(List<SpecRow> Specs, List<string> Images)> PageAsync(string url, CancellationToken cancellationToken)
    {
        var key = "page:" + url;
        var cached = await store.GetAsync<CachedProductDetails>(key, cancellationToken);
        if (cached is not null)
            return (cached.Specs ?? new List<SpecRow>(), cached.Images ?? new List<string>()); // ردیف کش قدیمی ممکن است فهرست نداشته باشد
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return (new List<SpecRow>(), new List<string>());
        var html = await GetAsync(Supplier + ":" + uri.Host, url, false, cancellationToken);
        if (html is null)
            return (new List<SpecRow>(), new List<string>());
        var specs = SmartParsers.HtmlSpecs(html).Select(s => new SpecRow(s.Key, s.Value)).ToList();
        var images = new List<string>();
        if (SmartParsers.OgImage(html, url) is { } og)
            images.Add(og);
        images.AddRange(SmartParsers.JsonLdImages(html, url));
        images = images.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
        await store.SetAsync(key, uri.Host, new CachedProductDetails(specs, images), DetailsTtl, cancellationToken);
        return (specs, images);
    }

    /// <summary>
    /// فروشگاه‌های نتایج جست‌وجو برای یک کالا (B7، گسترش دور بیستم): دو جست‌وجو («… مشخصات» و «خرید …») تا داده از سایت‌های
    /// بیشتری بیاید، فقط صفحه‌های فروشگاهی، هر دامنه یک بار، و فقط عنوان‌هایی با شباهت ≥ <paramref name="minMatch"/> (همان کالا).
    /// </summary>
    public async Task<List<SerpShop>> SerpShopHitsAsync(string query, int take, double minMatch, CancellationToken cancellationToken)
    {
        var list = new List<SerpShop>();
        foreach (var phrase in new[] { query + " مشخصات", "خرید " + query })
        {
            if (list.Count >= take)
                break;
            try
            {
                var serp = await web.SearchAsync(phrase, cancellationToken);
                if (!serp.Ok || serp.Value is null)
                    continue;
                foreach (var r in serp.Value)
                {
                    if (!CompetitorParsers.IsShopCandidate(r.Url) || !Uri.TryCreate(r.Url, UriKind.Absolute, out var uri))
                        continue;
                    var match = SmartParsers.TitleMatch(query, r.Title);
                    if (match >= minMatch && list.All(x => !string.Equals(new Uri(x.Url).Host, uri.Host, StringComparison.OrdinalIgnoreCase)))
                        list.Add(new SerpShop(r.Url, r.Title, match));
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogDebug(exception, "SERP lookup failed");
            }
        }
        return list.OrderByDescending(x => x.Match).Take(take).ToList();
    }

    /// <summary>آدرس فروشگاه‌های برتر نتایج جست‌وجو برای مشخصات (B7؛ شباهت عنوان ≥ ۰٫۵).</summary>
    public async Task<List<string>> SerpShopsAsync(string query, int take, CancellationToken cancellationToken) =>
        (await SerpShopHitsAsync(query, take, 0.5, cancellationToken)).Select(s => s.Url).ToList();

    /// <summary>
    /// دور بیستم: نتایج کامل جست‌وجوی تصویر بینگ (تصویر + عنوان + صفحه). round = شمارهٔ دور «جست‌وجوی دوباره»:
    /// هر دور صفحهٔ بعدی نتایج (first=1، ۳۶، ۷۱ …) تا عکس‌های تازه بیاید.
    /// </summary>
    public async Task<List<ImageHit>> BingImageResultsAsync(string query, int round, CancellationToken cancellationToken)
    {
        round = Math.Clamp(round, 0, 20);
        var key = "img:bing2:" + round + ":" + query;
        var cached = await store.GetAsync<List<CachedImageHit>>(key, cancellationToken);
        if (cached is not null)
            return cached.Select(h => new ImageHit(h.Url, h.Title, h.PageUrl)).ToList();
        var first = 1 + round * 35;
        var html = await GetAsync(BingImages, $"https://www.bing.com/images/search?form=HDRSC2&first={first}&q=" + Uri.EscapeDataString(query), false, cancellationToken);
        if (html is null)
            return [];
        var hits = SmartParsers.BingImageResults(html);
        await store.SetAsync(key, "bing", hits.Select(h => new CachedImageHit(h.Url, h.Title, h.PageUrl)).ToList(), DetailsTtl, cancellationToken);
        return hits;
    }

    /// <summary>تصویرهای جست‌وجوی تصویر بینگ (B1).</summary>
    public async Task<List<string>> BingImagesAsync(string query, CancellationToken cancellationToken)
    {
        var key = "img:bing:" + query;
        var cached = await store.GetAsync<List<string>>(key, cancellationToken);
        if (cached is not null)
            return cached;
        var html = await GetAsync(BingImages, "https://www.bing.com/images/search?form=HDRSC2&first=1&q=" + Uri.EscapeDataString(query), false, cancellationToken);
        if (html is null)
            return [];
        var urls = SmartParsers.BingImages(html);
        await store.SetAsync(key, "bing", urls, DetailsTtl, cancellationToken);
        return urls;
    }

    /// <summary>تصویرهای باسلام برای کالاهای هم‌نام (B1).</summary>
    public async Task<List<string>> BasalamImagesAsync(string query, CancellationToken cancellationToken)
    {
        var key = "img:basalam:" + query;
        var cached = await store.GetAsync<List<string>>(key, cancellationToken);
        if (cached is not null)
            return cached;
        var body = await GetAsync(Basalam, "https://search.basalam.com/ai-engine/api/v2.0/product/search?q=" + Uri.EscapeDataString(query), true, cancellationToken);
        if (body is null)
            return [];
        var urls = SmartParsers.BasalamImages(body).Where(x => SmartParsers.TitleMatch(query, x.Title) >= 0.6).Select(x => x.Image).Take(6).ToList();
        await store.SetAsync(key, "basalam", urls, DetailsTtl, cancellationToken);
        return urls;
    }

    /// <summary>
    /// بازنویسی روان یک متن با Ollama روی کامپیوتر مدیر (اختیاری، A31). فقط وقتی پذیرفته می‌شود که
    /// <see cref="LlmGuard.Accept"/> تأیید کند (همهٔ عددها حفظ شده، طول نزدیک، فارسی، بدون الگوی ماشینی)؛ وگرنه متن اصلی برمی‌گردد.
    /// </summary>
    public async Task<string> PolishAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            var options = await config.GetAsync(cancellationToken);
            if (!options.UseHelperLlm || string.IsNullOrWhiteSpace(text) || !broker.IsOnline(DateTimeOffset.UtcNow))
                return text;
            var prompt = "این متن فارسی را روان‌تر و طبیعی‌تر بازنویسی کن. هیچ اطلاعات، عدد یا ادعای تازه‌ای اضافه نکن، چیزی حذف نکن و فقط متن بازنویسی‌شده را برگردان:\n\n" + text;
            var result = await broker.RunAsync("llm", null, null, prompt, cancellationToken);
            return result is { Status: >= 200 and < 300, Body: { } body } && LlmGuard.Accept(text, body) ? PersianText.Clean(body) : text;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Helper LLM polish failed");
            return text;
        }
    }
}
