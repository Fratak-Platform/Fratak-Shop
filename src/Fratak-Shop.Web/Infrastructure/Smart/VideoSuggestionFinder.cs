using Fratak_Shop.Core.Services.Smart;

namespace Fratak_Shop.Web.Infrastructure.Smart;

/// <summary>یک کالای جست‌وجوشده (برای کش «اخیراً بررسی‌شده»؛ تاپل در JSON ذخیره نمی‌شود).</summary>
public sealed record VideoScanMark(Guid ProductId, DateTimeOffset At);

/// <summary>
/// دور ۲۹ (#۱۸) – «ویدیوی پیشنهادی» هر کالا: پیدا کردن جایی که ویدیوی معرفی <b>همین</b> کالا هست تا مدیر دانلود و اضافه کند.
/// <list type="number">
/// <item>صفحهٔ تأمین‌کننده (SupplierUrl): ویدیوی جاسازی‌شده (آپارات/یوتیوب/mp4) – مطمئن‌ترین منبع.</item>
/// <item>جست‌وجوی عمومی آپارات (بدون کلید، رایگان) با نام انگلیسی/فارسی کالا.</item>
/// <item>جست‌وجوی یوتیوب (صفحهٔ عمومی نتایج؛ اگر در دسترس نبود بی‌صدا رد می‌شود).</item>
/// </list>
/// <para>عنوان هر ویدیو با نام کالا سنجیده می‌شود (<see cref="VideoSuggestionRules.Score"/>): مدل باید عیناً باشد و نسخهٔ دیگر (Pro/Max) جریمه
/// می‌شود؛ فقط امتیاز ≥۷۰ پیشنهاد می‌شود. هیچ ویدیویی خودکار دانلود یا منتشر نمی‌شود.</para>
/// <para>سبک برای سرور و مؤدب با منابع: هر نوبت چند کالا، همهٔ درخواست‌ها از دروازهٔ منبع (فاصلهٔ ۲ ثانیه، توقف خودکار بعد از مسدودی)
/// و هر کالا تا ۳۰ روز دوباره جست‌وجو نمی‌شود.</para>
/// </summary>
public sealed class VideoSuggestionFinder(SmartSources sources, IVideoSuggestionService suggestions, ISmartResearchStore store, ILogger<VideoSuggestionFinder> logger)
{
    public const string AparatSource = "آپارات";
    public const string YouTubeSource = "یوتیوب";
    private const string ScanKey = "video-scan:recent";
    private static readonly TimeSpan Rescan = TimeSpan.FromDays(30);

    /// <summary>ویدیوهای یک کالا (بدون ذخیره). خطای هر منبع ← همان منبع رد می‌شود.</summary>
    public async Task<List<VideoHit>> FindAsync(string name, string? englishName, string? supplierUrl, CancellationToken cancellationToken)
    {
        var hits = new List<VideoHit>();
        if (Uri.TryCreate(supplierUrl, UriKind.Absolute, out var page) && page.Scheme is "https" or "http")
        {
            var html = await sources.GetAsync(SmartSources.Supplier, page.ToString(), false, cancellationToken);
            hits.AddRange(VideoSuggestionRules.FromSupplierPage(html, page.ToString()));
        }
        var query = VideoSuggestionRules.Query(name, englishName);
        if (query.Length >= 3)
        {
            var q = Uri.EscapeDataString(query);
            var aparat = await sources.GetAsync(AparatSource, $"https://www.aparat.com/etc/api/videoBySearch/text/{q}/perpage/10", true, cancellationToken);
            hits.AddRange(VideoSuggestionRules.FromAparat(aparat));
            // یوتیوب فقط وقتی آپارات و تأمین‌کننده چیزی ندادند (کمتر درخواست بیرونی).
            if (VideoSuggestionRules.Rank(hits, name, englishName).Count == 0)
            {
                var youtube = await sources.GetAsync(YouTubeSource, $"https://www.youtube.com/results?search_query={q}&hl=fa", false, cancellationToken);
                hits.AddRange(VideoSuggestionRules.FromYouTube(youtube));
            }
        }
        return VideoSuggestionRules.Rank(hits, name, englishName);
    }

    /// <summary>
    /// جست‌وجوی فوری یک کالا (دکمهٔ پنل): نتیجه ذخیره می‌شود. خروجی: تعداد پیشنهاد تازه؛ کالای ناموجود ← null.
    /// </summary>
    public async Task<int?> ScanProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        if (await suggestions.ProductAsync(productId, cancellationToken) is not { } product)
            return null;
        var hits = await FindAsync(product.Name, product.EnglishName, product.SupplierUrl, cancellationToken);
        return await suggestions.AddAsync(product.Id, hits, cancellationToken);
    }

    /// <summary>
    /// یک نوبت: حداکثر <paramref name="take"/> کالای بی‌ویدیو که در ۳۰ روز گذشته بررسی نشده‌اند. خروجی: (پیشنهاد ذخیره‌شده، کالای دارای پیشنهاد).
    /// </summary>
    public async Task<(int Saved, int Products)> RunAsync(int take, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var marks = (await store.GetAsync<List<VideoScanMark>>(ScanKey, cancellationToken) ?? [])
            .Where(m => now - m.At < Rescan).ToList();
        var candidates = await suggestions.CandidatesAsync(take, marks.Select(m => m.ProductId).ToList(), cancellationToken);
        var saved = 0;
        var products = 0;
        foreach (var (id, name, englishName, supplierUrl) in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var hits = await FindAsync(name, englishName, supplierUrl, cancellationToken);
                var added = await suggestions.AddAsync(id, hits, cancellationToken);
                saved += added;
                if (added > 0)
                    products++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Video suggestions for {Product} failed", id);
            }
            marks.Add(new VideoScanMark(id, now));
        }
        if (candidates.Count > 0)
        {
            // فقط ۲۰۰۰ علامت آخر (یک ردیف کوچک کش؛ سنگین نمی‌شود).
            await store.SetAsync(ScanKey, "video", marks.OrderByDescending(m => m.At).Take(2000).ToList(), Rescan, cancellationToken);
        }
        return (saved, products);
    }
}
