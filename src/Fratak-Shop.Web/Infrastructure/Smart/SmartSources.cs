using Fratak_Shop.Core.Services.Pricing;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Services.Suppliers;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Web.Infrastructure.Smart;

/// <summary>ردیف مشخصات قابل ذخیره در کش تحقیق (تاپل در JSON ذخیره نمی‌شود).</summary>
public sealed record SpecRow(string Key, string Value);

/// <summary>
/// جزئیات کش‌شدهٔ کالا (دیجی‌کالا یا صفحهٔ فروشگاه). دور ۲۱: Text = متن «توضیحات» صفحه برای تحقیق
/// (<see cref="ProductResearch"/>)؛ ردیف‌های قدیمی کش این فیلد را ندارند (null).
/// </summary>
public sealed record CachedProductDetails(List<SpecRow> Specs, List<string> Images, string? Text = null, string? Category = null);

/// <summary>
/// دور بیستم: یک فروشگاه در نتایج جست‌وجو. Match = شباهت عنوان نتیجه با کالای ما (۰ تا ۱).
/// مثال: («https://mobile.ir/p/t15»، «خرید هدفون هایلو T15»، ۰٫۸).
/// </summary>
public sealed record SerpShop(string Url, string Title, double Match);

/// <summary>دور بیستم: نتیجهٔ جست‌وجوی تصویر قابل ذخیره در کش (تاپل/رکورد Core مستقیم ذخیره نمی‌شود).</summary>
public sealed record CachedImageHit(string Url, string? Title, string? PageUrl);

/// <summary>دور ۲۶: یک متن کوتاه کش‌شده (مثلاً نام دستهٔ یک سایت). متن خالی = «این سایت نتیجه‌ای نداشت».</summary>
public sealed record CachedText(string Value);

/// <summary>
/// دریافت دادهٔ بیرونی برای محتوای هوشمند (B1–B13) با رعایت کامل ادب و ایمنی:
/// <list type="bullet">
/// <item>هر منبع از راه <see cref="IExternalSourceGate"/>: فاصلهٔ حداقل بین درخواست‌ها و «استراحت» خودکار بعد از محدودیت (۴۲۹/۴۰۳/کپچا).</item>
/// <item>همهٔ درخواست‌ها با محافظ SSRF (<see cref="ISupplierHttp"/>) و سقف حجم.</item>
/// <item>نتیجه‌ها در <see cref="ISmartResearchStore"/> کش می‌شوند (جست‌وجو ۷ روز، جزئیات ۳۰ روز) تا یک کالا دو بار پرسیده نشود (B22).</item>
/// </list>
/// <para>هیچ متدی استثنا بیرون نمی‌دهد؛ شکست ← فهرست خالی/null و ادامهٔ کار بدون آن منبع.</para>
/// <para>محل استفاده: <see cref="SmartImageFinder"/> و <see cref="SmartOrchestrator"/>.</para>
/// </summary>
public sealed class SmartSources(
    ISupplierHttp http,
    IExternalSourceGate gate,
    ISeoWebSource web,
    ISmartResearchStore store,
    ILogger<SmartSources> logger)
{
    public const string Digikala = "دیجی‌کالا (هوشمند)";
    public const string BingImages = "bing-images";
    public const string Basalam = "باسلام (هوشمند)";
    public const string Supplier = "صفحهٔ فروشگاه";
    public const string Openverse = "openverse";
    public const string Torob = "ترب (هوشمند)";

    private static readonly TimeSpan PoliteInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SearchTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan DetailsTtl = TimeSpan.FromDays(30);

    /// <summary>
    /// یک درخواست مؤدب، مستقیم از سرور و از پشت دروازهٔ منبع (فاصلهٔ مؤدبانه + توقف خودکار وقتی منبع ما را مسدود کرد).
    /// </summary>
    public async Task<string?> GetAsync(string source, string url, bool json, CancellationToken cancellationToken)
    {
        try
        {
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
        var key = "dk:product:v3:" + id; // v2 (دور ۲۱): متن معرفی هم کش می‌شود؛ v3 (دور ۲۲): مسیر دستهٔ کالا هم
        var cached = await store.GetAsync<CachedProductDetails>(key, cancellationToken);
        if (cached is not null)
            return cached;
        var body = await GetAsync(Digikala, $"https://api.digikala.com/v2/product/{id}/", true, cancellationToken);
        if (body is null)
            return null;
        var details = SmartParsers.DigikalaProduct(body);
        var result = new CachedProductDetails(details.Specs.Select(s => new SpecRow(s.Key, s.Value)).ToList(), details.Images.ToList(),
            ProductResearch.DigikalaText(body), details.Category);
        await store.SetAsync(key, "digikala", result, DetailsTtl, cancellationToken);
        return result;
    }

    /// <summary>
    /// دور ۲۲: «سایت‌های دیگر این کالا را در کدام دسته گذاشته‌اند؟» — همین کالا در دیجی‌کالا پیدا می‌شود (همان تطبیق سخت‌گیرانهٔ
    /// <see cref="FindDigikalaAsync"/>) و مسیر دستهٔ آن برگردانده می‌شود. مثال: «ماساژور گردن برقی مدل X5» ←
    /// «زیبایی و سلامت > لوازم شخصی برقی > ماساژور». پیدا نشد/منبع در استراحت ← null. نتیجه ۳۰ روز کش می‌شود.
    /// <para>محل استفاده: ورود هوشمند کالا (<see cref="IProductIntakeService"/>) از SmartWorker.</para>
    /// </summary>
    public async Task<string?> SourceCategoryAsync(IntakeProduct product, CancellationToken cancellationToken)
    {
        var subject = new SmartProductSubject(product.Id, product.Name, product.EnglishName, "", "", null, null, null, product.BrandEnglishName,
            Guid.Empty, null, null, null, 0, [], [], 0, null, false);
        var hit = await FindDigikalaAsync(subject, cancellationToken);
        if (hit is null)
            return null;
        return (await DigikalaDetailsAsync(hit.Id, cancellationToken))?.Category;
    }

    /// <summary>
    /// دور ۲۶ – «رأی رقبا برای دستهٔ کالا»: همین کالا در دیجی‌کالا (دستهٔ غالب نتیجه‌ها + مسیر کامل صفحهٔ کالا)، ترب (اولین دستهٔ پاسخ)
    /// و باسلام (دستهٔ کالاهای هم‌نام) جست‌وجو می‌شود و <see cref="CategoryVote.Decide"/> اکثریت (≥ ۲ سایت موافق) را برمی‌گزیند.
    /// <list type="bullet">
    /// <item>Path: مسیر کامل دیجی‌کالا (برای والدها)؛ اگر برگِ دیجی‌کالا با رأی اکثریت فرق داشت، برگ با نام اکثریت عوض می‌شود.</item>
    /// <item>MarketCategory: نام دستهٔ اکثریت یا null (یک سایت تنها رأی حساب نمی‌شود).</item>
    /// </list>
    /// <para>همه مؤدبانه (فاصلهٔ ۲ ثانیه برای هر منبع، استراحت خودکار بعد از مسدود شدن) و ۷ روز کش؛ هر منبعی شکست بخورد، بقیه رأی می‌دهند.</para>
    /// مثال: «هدفون بی سیم هایلو T15» ← دیجی‌کالا و ترب «هدفون، هدست و هندزفری»، باسلام «هدفون و هندزفری بی سیم و بلوتوثی»
    /// ← MarketLookup(«کالای دیجیتال > … > هدفون، هدست و هندزفری»، «هدفون هدست و هندزفری»).
    /// </summary>
    public async Task<MarketLookup?> MarketCategoryAsync(IntakeProduct product, CancellationToken cancellationToken)
    {
        var subject = new SmartProductSubject(product.Id, product.Name, product.EnglishName, "", "", null, null, null, product.BrandEnglishName,
            Guid.Empty, null, null, null, 0, [], [], 0, null, false);
        var votes = new List<(string Site, string? Title)>();

        // ۱) دیجی‌کالا: دستهٔ غالب جست‌وجو با نام فارسی (دقیق‌تر برای دسته) + مسیر کامل صفحهٔ همین کالا.
        votes.Add(("digikala", await CachedTextAsync("vote:dk:" + product.Name, Digikala,
            "https://api.digikala.com/v1/search/?page=1&q=" + Uri.EscapeDataString(product.Name), SmartParsers.DigikalaSearchCategory, cancellationToken)));
        string? path = null;
        if (await FindDigikalaAsync(subject, cancellationToken) is { } hit)
            path = (await DigikalaDetailsAsync(hit.Id, cancellationToken))?.Category;

        // ۲) ترب: اولین دستهٔ پاسخ جست‌وجو.
        votes.Add(("torob", await CachedTextAsync("vote:torob:" + product.Name, Torob,
            "https://api.torob.com/v4/base-product/search/?page=0&size=24&source=next_desktop&q=" + Uri.EscapeDataString(product.Name),
            SmartParsers.TorobSearchCategory, cancellationToken)));

        // ۳) باسلام: دستهٔ کالاهایی که واقعاً همین کالا هستند.
        votes.Add(("basalam", await CachedTextAsync("vote:basalam:" + product.Name, Basalam,
            "https://search.basalam.com/ai-engine/api/v2.0/product/search?q=" + Uri.EscapeDataString(product.Name),
            body => SmartParsers.BasalamCategory(body, product.Name), cancellationToken)));

        // ۴) بخش آخر مسیر دیجی‌کالا هم رأی خود دیجی‌کالاست اگر جست‌وجو دسته نداد.
        var segments = CategoryNames.Path(path);
        if (votes[0].Title is null && segments.Count > 0)
            votes[0] = ("digikala", segments[^1]);

        var decision = CategoryVote.Decide(votes);
        if (decision is null)
            return path is null ? null : new MarketLookup(path);
        if (segments.Count >= 2 && !CategoryVote.Similar(CategoryVote.KeyTerms(segments[^1]), CategoryVote.KeyTerms(decision.Name)))
            path = CategoryNames.Join(segments.Take(segments.Count - 1).Append(decision.Name));
        return new MarketLookup(path, decision.Name);
    }

    /// <summary>
    /// دور ۳۱ (#۱) – «دستهٔ کالا در صفحهٔ تأمین‌کننده» (قانون پروژه: سیستم دسته را نفهمید ← لینک تأمین‌کننده را باز کن و دستهٔ آن را بخوان).
    /// <list type="bullet">
    /// <item>صفحهٔ کالای دیجی‌کالا (dkp-123) ← مسیر دسته از API رسمی همان کالا (دقیق‌تر از HTML).</item>
    /// <item>هر فروشگاه دیگر ← HTML صفحه: JSON-LD BreadcrumbList، فیلد category، نوار مسیر، «دسته:» ووکامرس، متای product:category
    /// (<see cref="SmartParsers.SupplierCategoryPath"/>).</item>
    /// </list>
    /// مؤدبانه (دروازهٔ هر میزبان، فاصلهٔ ۲ ثانیه، توقف خودکار بعد از مسدود شدن) و ۱۴ روز کش (نبود مسیر هم کش می‌شود).
    /// مثال: «https://shop.ir/product/kettle-kt2000» ← [«لوازم آشپزخانه»، «کتری و قوری»]. آدرس نامعتبر/صفحهٔ خراب ← null.
    /// </summary>
    public async Task<IReadOnlyList<string>?> SupplierCategoryAsync(string url, string productName, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return null;
        var key = "supcat:v1:" + url;
        var cached = await store.GetAsync<CachedText>(key, cancellationToken);
        if (cached is not null)
            return string.IsNullOrWhiteSpace(cached.Value) ? null : CategoryNames.Path(cached.Value);

        IReadOnlyList<string> path = [];
        var dkp = System.Text.RegularExpressions.Regex.Match(url, @"digikala\.com/product/dkp-(?<id>\d{3,12})", System.Text.RegularExpressions.RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(200));
        if (dkp.Success && long.TryParse(dkp.Groups["id"].Value, out var id))
        {
            path = CategoryNames.Path((await DigikalaDetailsAsync(id, cancellationToken))?.Category);
        }
        else
        {
            var html = await GetAsync(Supplier + ":" + uri.Host, url, false, cancellationToken);
            if (html is null)
                return null; // شکست شبکه کش نمی‌شود تا دفعهٔ بعد دوباره امتحان شود.
            path = SmartParsers.SupplierCategoryPath(html, productName);
        }
        var text = path.Count > 0 ? CategoryNames.Join(path) : "";
        await store.SetAsync(key, uri.Host, new CachedText(text), TimeSpan.FromDays(14), cancellationToken);
        return path.Count > 0 ? path : null;
    }

    /// <summary>
    /// دور ۲۶ – «جای خالی داده را از سایت‌های دیگر پر کن»: نام انگلیسی کالا از ترب (name2) وقتی دیجی‌کالا نام انگلیسی نداد.
    /// فقط نتیجه با شباهت نام ≥ ۷۵٪ (<see cref="SmartParsers.TorobEnglishName"/>). ۷ روز کش؛ شکست ← null.
    /// </summary>
    public Task<string?> TorobEnglishNameAsync(string productName, CancellationToken cancellationToken) =>
        CachedTextAsync("torob:en:" + productName, Torob,
            "https://api.torob.com/v4/base-product/search/?page=0&size=24&source=next_desktop&q=" + Uri.EscapeDataString(productName),
            body => SmartParsers.TorobEnglishName(productName, SmartParsers.TorobSearchResults(body)), cancellationToken);

    /// <summary>
    /// یک درخواست JSON کش‌شده که فقط یک متن کوتاه (مثلاً نام دسته) از آن لازم است. شکست ← null (و کش نمی‌شود تا دفعهٔ بعد دوباره امتحان شود).
    /// «نبود نتیجه» با متن خالی کش می‌شود تا هر ۳ دقیقه دوباره پرسیده نشود.
    /// </summary>
    private async Task<string?> CachedTextAsync(string key, string source, string url, Func<string, string?> parse, CancellationToken cancellationToken)
    {
        var cached = await store.GetAsync<CachedText>(key, cancellationToken);
        if (cached is not null)
            return string.IsNullOrWhiteSpace(cached.Value) ? null : cached.Value;
        var body = await GetAsync(source, url, true, cancellationToken);
        if (body is null)
            return null;
        string? value;
        try
        {
            value = parse(body);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Parsing {Source} failed", source);
            value = null;
        }
        await store.SetAsync(key, source, new CachedText(value ?? ""), SearchTtl, cancellationToken);
        return value;
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
        var key = "page:v2:" + url; // v2 (دور ۲۱): متن توضیحات صفحه هم کش می‌شود
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
        await store.SetAsync(key, uri.Host, new CachedProductDetails(specs, images, ProductResearch.PageText(html)), DetailsTtl, cancellationToken);
        return (specs, images);
    }

    /// <summary>
    /// دور ۲۱: متن «توضیحات» یک صفحهٔ فروشگاه برای تحقیق (از همان کش <see cref="PageAsync"/>؛ اگر کش نبود یک بار دریافت می‌شود).
    /// مثال: صفحهٔ ووکامرس ← متن پنل توضیحات + description JSON-LD؛ صفحهٔ نامعتبر/در دسترس نبود ← رشتهٔ خالی.
    /// </summary>
    public async Task<string> PageTextAsync(string url, CancellationToken cancellationToken)
    {
        await PageAsync(url, cancellationToken);
        return (await store.GetAsync<CachedProductDetails>("page:v2:" + url, cancellationToken))?.Text ?? "";
    }

    /// <summary>
    /// دور ۲۱: جمع کردن متن توضیحات همین کالا از چند سایت برای «توضیحات محصول»: دیجی‌کالا (معتبر)، صفحهٔ تأمین‌کننده (معتبر)
    /// و فروشگاه‌های نتایج جست‌وجو (با شباهت عنوان ≥ ۰٫۶). همه از کش ۳۰ روزه و با فاصلهٔ مؤدبانه؛ شکست هر منبع ← بدون آن ادامه.
    /// </summary>
    public async Task<List<ResearchDoc>> ResearchDocsAsync(SmartProductSubject subject, int shops, CancellationToken cancellationToken)
    {
        var docs = new List<ResearchDoc>();
        try
        {
            if (await FindDigikalaAsync(subject, cancellationToken) is { } hit && await DigikalaDetailsAsync(hit.Id, cancellationToken) is { Text.Length: > 0 } dk)
                docs.Add(new ResearchDoc("digikala.com", dk.Text!, true));
            if (!string.IsNullOrWhiteSpace(subject.SupplierUrl) && Uri.TryCreate(subject.SupplierUrl, UriKind.Absolute, out var supplier))
            {
                var text = await PageTextAsync(subject.SupplierUrl, cancellationToken);
                if (text.Length > 0)
                    docs.Add(new ResearchDoc(supplier.Host, text, true));
            }
            foreach (var shop in await SerpShopHitsAsync(subject.SearchQuery, shops, 0.6, cancellationToken))
            {
                var text = await PageTextAsync(shop.Url, cancellationToken);
                if (text.Length > 0)
                    docs.Add(new ResearchDoc(new Uri(shop.Url).Host, text, false));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Research gathering failed");
        }
        return docs;
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

    /// <summary>
    /// دور ۲۶ – عکس فضاسازی دسته از Openverse: فقط عکس واقعی (photograph)، افقی، بزرگ و با مجوز آزاد CC0/مالکیت عمومی
    /// (بدون نیاز به ذکر منبع). رایگان و بدون کلید؛ محدودیت ناشناس ۲۰ درخواست در دقیقه ← نتیجه ۷ روز کش می‌شود.
    /// مثال: «kitchen appliances lifestyle» ← تا ۲۰ عکس با ابعاد. شکست ← فهرست خالی.
    /// </summary>
    public async Task<List<PhotoHit>> OpenversePhotosAsync(string query, CancellationToken cancellationToken)
    {
        query = (query ?? "").Trim();
        if (query.Length < 2)
            return [];
        var key = "photo:ov:" + query;
        var cached = await store.GetAsync<List<PhotoHit>>(key, cancellationToken);
        if (cached is not null)
            return cached;
        var json = await GetAsync(Openverse, "https://api.openverse.org/v1/images/?license=cc0,pdm&category=photograph&aspect_ratio=wide&size=large&mature=false&page_size=20&q="
                                             + Uri.EscapeDataString(query), true, cancellationToken);
        if (json is null)
            return [];
        var hits = SmartParsers.OpenverseImages(json);
        await store.SetAsync(key, Openverse, hits, SearchTtl, cancellationToken);
        return hits;
    }

    /// <summary>
    /// دور ۲۸ – «عکس سوژهٔ دسته» از Openverse: مثل <see cref="OpenversePhotosAsync"/> ولی بدون قید «افقی» (عکس کاتالوگی معمولاً مربع است).
    /// مثال: «headphones white background» ← تا ۲۰ عکس CC0. نتیجه ۷ روز کش می‌شود؛ شکست ← فهرست خالی.
    /// </summary>
    public async Task<List<PhotoHit>> OpenverseSubjectAsync(string query, CancellationToken cancellationToken)
    {
        query = (query ?? "").Trim();
        if (query.Length < 2)
            return [];
        var key = "photo:ovs:" + query;
        var cached = await store.GetAsync<List<PhotoHit>>(key, cancellationToken);
        if (cached is not null)
            return cached;
        var json = await GetAsync(Openverse, "https://api.openverse.org/v1/images/?license=cc0,pdm&category=photograph&mature=false&page_size=20&q="
                                             + Uri.EscapeDataString(query), true, cancellationToken);
        if (json is null)
            return [];
        var hits = SmartParsers.OpenverseImages(json);
        await store.SetAsync(key, Openverse, hits, SearchTtl, cancellationToken);
        return hits;
    }

    /// <summary>
    /// دور ۲۶ – عکس فضاسازی دسته از جست‌وجوی تصویر بینگ با فیلترهای «فقط عکس واقعی، بزرگ، افقی»
    /// (qft=+filterui:photo-photo+filterui:imagesize-large+filterui:aspect-wide). ابعاد اعلام نمی‌شود (۰)؛ بعد از دانلود سنجیده می‌شود.
    /// مثال: «لوازم خانگی در خانه» ← تا ۴۰ نتیجه. شکست ← فهرست خالی.
    /// </summary>
    public async Task<List<PhotoHit>> BingPhotosAsync(string query, CancellationToken cancellationToken)
    {
        query = (query ?? "").Trim();
        if (query.Length < 2)
            return [];
        var key = "photo:bing:" + query;
        var cached = await store.GetAsync<List<PhotoHit>>(key, cancellationToken);
        if (cached is not null)
            return cached;
        var html = await GetAsync(BingImages, "https://www.bing.com/images/search?form=HDRSC2&first=1&qft=+filterui:photo-photo+filterui:imagesize-large+filterui:aspect-wide&q="
                                              + Uri.EscapeDataString(query), false, cancellationToken);
        if (html is null)
            return [];
        var hits = SmartParsers.BingImageResults(html).Select(h => new PhotoHit(h.Url, h.Title, h.PageUrl, 0, 0, "bing")).ToList();
        await store.SetAsync(key, "bing", hits, SearchTtl, cancellationToken);
        return hits;
    }

    /// <summary>
    /// دور ۲۶ – «تحقیق پیش از نوشتن مقاله»: جست‌وجوهای واقعی مرتبط مردم برای یک موضوع (پیشنهاد جست‌وجوی موتورها، رایگان).
    /// خود عبارت و پیشنهادهای خیلی کوتاه/بلند کنار می‌روند؛ نتیجه ۷ روز کش می‌شود. شکست ← فهرست خالی.
    /// مثال: «هدفون بی سیم» ← [«هدفون بی سیم ارزان»، «هدفون بی سیم سامسونگ» …].
    /// </summary>
    public async Task<List<string>> RelatedSearchesAsync(string query, CancellationToken cancellationToken)
    {
        query = (query ?? "").Trim();
        if (query.Length < 2)
            return [];
        if (query.Length > 80)
            query = query[..80];
        var key = "suggest:" + query;
        var cached = await store.GetAsync<List<string>>(key, cancellationToken);
        if (cached is not null)
            return cached;
        try
        {
            var result = await web.SuggestAsync(query, cancellationToken);
            if (!result.Ok || result.Value is null)
                return [];
            var normalized = PersianText.Normalize(query);
            var list = result.Value
                .Select(PersianText.Clean)
                .Where(x => x.Length is >= 3 and <= 60 && PersianText.Normalize(x) != normalized)
                .Distinct(StringComparer.Ordinal).Take(8).ToList();
            await store.SetAsync(key, "suggest", list, SearchTtl, cancellationToken);
            return list;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Related searches failed for {Query}", query);
            return [];
        }
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
}
