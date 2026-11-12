using System.Text.Json;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// یک ویدیوی پیدا‌شده. Source: supplier / aparat / youtube. PageUrl = جایی که مدیر باز می‌کند و دانلود می‌کند.
/// مثال: («aparat»، «https://www.aparat.com/v/i7228or»، «هندزفری بلوتوثی شیائومی Redmi Buds 4»، پوستر، ۰).
/// </summary>
public sealed record VideoHit(string Source, string PageUrl, string? Title, string? ThumbnailUrl, int Score = 0);

/// <summary>ردیف پنل «ویدیوهای پیشنهادی».</summary>
public sealed record VideoSuggestionRow(Guid Id, Guid ProductId, string ProductName, string Source, string PageUrl, string? Title, string? ThumbnailUrl,
    int Score, VideoSuggestionStatus Status, DateTimeOffset CreatedAt);

/// <summary>
/// دور ۲۹ (#۱۸) – قواعد خالص «ویدیوی پیشنهادی کالا» (بدون شبکه؛ آزمون‌پذیر):
/// <list type="bullet">
/// <item><see cref="Score"/>: آیا عنوان ویدیو دربارهٔ <b>همین</b> کالاست؟ (مدل/شمارهٔ کالا باید باشد؛ نسخهٔ دیگر مثل Pro/Max جریمه می‌شود).</item>
/// <item><see cref="FromSupplierPage"/>: ویدیوهای جاسازی‌شده در صفحهٔ تأمین‌کننده (آپارات، یوتیوب، فایل mp4) – مطمئن‌ترین منبع.</item>
/// <item><see cref="FromAparat"/> و <see cref="FromYouTube"/>: خواندن نتیجهٔ جست‌وجوی عمومی (بدون کلید API و رایگان).</item>
/// </list>
/// </summary>
public static partial class VideoSuggestionRules
{
    /// <summary>کمترین امتیاز تطبیق برای پیشنهاد (زیر این ← احتمالاً کالای دیگری است).</summary>
    public const int MinScore = 70;

    /// <summary>امتیاز ویدیوی جاسازی‌شده در صفحهٔ خود تأمین‌کننده (همان صفحهٔ همین کالا).</summary>
    public const int SupplierScore = 95;

    /// <summary>بیشترین پیشنهاد نگه‌داشته‌شده برای هر کالا در هر نوبت.</summary>
    public const int MaxPerProduct = 5;

    /// <summary>واژه‌های نسخه: اگر در عنوان ویدیو باشند ولی در نام کالا نه ← احتمالاً نسخهٔ دیگری است.</summary>
    private static readonly HashSet<string> VariantWords = new(StringComparer.Ordinal)
    {
        "pro", "max", "plus", "ultra", "lite", "mini", "se", "neo", "prime", "fe", "air", "پرو", "مکس", "پلاس", "اولترا", "لایت", "مینی",
    };

    /// <summary>واژه‌های عمومی که چیزی دربارهٔ «کدام کالا» نمی‌گویند (در شمارش پوشش نمی‌آیند).</summary>
    private static readonly HashSet<string> Generic = new(StringComparer.Ordinal)
    {
        "و", "با", "برای", "از", "به", "در", "مدل", "خرید", "قیمت", "اصل", "اورجینال", "جدید", "رنگ", "the", "and", "with", "for", "new", "original",
        "model", "edition", "version", "-", "|",
    };

    /// <summary>
    /// امتیاز تطبیق ۰ تا ۱۰۰ عنوان ویدیو با کالا.
    /// <para>واژه‌های کلیدی: واژه‌های لاتین نام انگلیسی/نام کالا (برند و مدل)؛ اگر لاتین نداشت، واژه‌های ≥۳ حرفی فارسی.
    /// امتیاز = درصد واژه‌های کلیدی موجود در عنوان؛ نبودن «مدل» (واژهٔ دارای عدد) ← نصف؛ نسخهٔ اضافی (Pro، Max، …) ← ۰٫۶ برابر.</para>
    /// مثال: کالا «هندزفری Xiaomi Redmi Buds 4» و ویدیو «معرفی Xiaomi Redmi Buds 4» ← ۱۰۰؛ ویدیو «… Redmi Buds 4 Pro» ← ۶۰؛
    /// ویدیو «… Redmi Buds 5 Pro» ← زیر ۵۰.
    /// </summary>
    public static int Score(string? productName, string? englishName, string? videoTitle)
    {
        var title = Tokens(videoTitle);
        if (title.Count == 0)
            return 0;
        var latin = Tokens((englishName ?? "") + " " + (productName ?? "")).Where(IsLatinOrModel).Distinct().ToList();
        // فقط عدد (مثل «20000» در «پاوربانک 20000») کافی نیست ← واژه‌های فارسی ≥۳ حرفی هم کلیدی می‌شوند.
        var keys = latin.Any(t => t.Any(c => c is >= 'a' and <= 'z'))
            ? latin
            : latin.Concat(Tokens(productName).Where(t => t.Length >= 3 && !IsLatinOrModel(t))).Distinct().ToList();
        if (keys.Count == 0)
            return 0;
        var set = title.ToHashSet(StringComparer.Ordinal);
        var coverage = keys.Count(set.Contains) / (double)keys.Count;
        // مدل/شماره (مثل «4»، «t15»، «wh-1000xm5») باید عیناً در عنوان باشد.
        if (keys.Where(HasDigit).Any(k => !set.Contains(k)))
            coverage *= 0.5;
        var keySet = keys.ToHashSet(StringComparer.Ordinal);
        if (title.Any(t => VariantWords.Contains(t) && !keySet.Contains(t)))
            coverage *= 0.6;
        // عدد دیگری در عنوان کنار نام (مثل «Buds 5» به جای «Buds 4») ← کالای دیگر.
        if (keys.Any(HasDigit) && title.Any(t => t.All(char.IsDigit) && !keySet.Contains(t) && t.Length <= 3))
            coverage *= 0.8;
        return (int)Math.Round(Math.Clamp(coverage, 0, 1) * 100);
    }

    /// <summary>
    /// ویدیوهای جاسازی‌شده در HTML صفحهٔ تأمین‌کننده: آپارات (embed یا /v/)، یوتیوب (embed، watch، youtu.be) و فایل mp4/webm.
    /// آدرس نسبی فایل با آدرس صفحه کامل می‌شود. حداکثر ۶ مورد، بدون تکرار.
    /// مثال: &lt;iframe src="https://www.aparat.com/video/video/embed/videohash/abc12/vt/frame"&gt; ← «https://www.aparat.com/v/abc12».
    /// </summary>
    public static List<VideoHit> FromSupplierPage(string? html, string? pageUrl)
    {
        var hits = new List<VideoHit>();
        if (string.IsNullOrWhiteSpace(html))
            return hits;
        if (html.Length > 3_000_000)
            html = html[..3_000_000];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string url)
        {
            if (url.Length <= 500 && seen.Add(url) && hits.Count < 6)
                hits.Add(new VideoHit("supplier", url, null, null, SupplierScore));
        }

        foreach (Match m in AparatLink().Matches(html))
            Add("https://www.aparat.com/v/" + m.Groups[1].Value);
        foreach (Match m in YouTubeLink().Matches(html))
            Add("https://www.youtube.com/watch?v=" + m.Groups[1].Value);
        Uri.TryCreate(pageUrl, UriKind.Absolute, out var baseUri);
        foreach (Match m in VideoFile().Matches(html))
        {
            var raw = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
            if (baseUri is not null && Uri.TryCreate(baseUri, raw, out var absolute) && absolute.Scheme is "https" or "http")
                Add(absolute.ToString());
            else if (Uri.TryCreate(raw, UriKind.Absolute, out var direct) && direct.Scheme is "https" or "http")
                Add(direct.ToString());
        }
        return hits;
    }

    /// <summary>
    /// پاسخ JSON جست‌وجوی آپارات (<c>/etc/api/videoBySearch/text/{q}</c>) ← ویدیوها (حداکثر ۱۰). JSON خراب ← فهرست خالی.
    /// </summary>
    public static List<VideoHit> FromAparat(string? json)
    {
        var hits = new List<VideoHit>();
        if (string.IsNullOrWhiteSpace(json))
            return hits;
        try
        {
            using var doc = JsonDocument.Parse(json);
            // ریشه یا آیتم غیرشیء (آرایه، رشته، …) ← نادیده (TryGetProperty روی غیرشیء استثنا می‌دهد).
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("videobysearch", out var list) || list.ValueKind != JsonValueKind.Array)
                return hits;
            foreach (var item in list.EnumerateArray().Take(10))
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                var uid = item.TryGetProperty("uid", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                if (string.IsNullOrWhiteSpace(uid) || !SafeId().IsMatch(uid))
                    continue;
                var title = item.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                var poster = item.TryGetProperty("small_poster", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                hits.Add(new VideoHit("aparat", "https://www.aparat.com/v/" + uid, Cut(title, 300), Https(poster)));
            }
        }
        catch (JsonException)
        {
            // پاسخ غیرمنتظره ← بدون نتیجه از این منبع.
        }
        return hits;
    }

    /// <summary>
    /// HTML صفحهٔ نتیجهٔ جست‌وجوی یوتیوب ← ویدیوها (شناسهٔ ۱۱ نویسه‌ای + عنوان؛ حداکثر ۱۰). بدون کلید API.
    /// </summary>
    public static List<VideoHit> FromYouTube(string? html)
    {
        var hits = new List<VideoHit>();
        if (string.IsNullOrWhiteSpace(html))
            return hits;
        // سقف حجم (صفحهٔ نتیجهٔ یوتیوب ~۱ مگابایت است) تا عبارت باقاعده روی ورودی بزرگ کند نشود.
        if (html.Length > 3_000_000)
            html = html[..3_000_000];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in YouTubeResult().Matches(html))
        {
            var id = m.Groups[1].Value;
            if (!seen.Add(id))
                continue;
            string? title;
            try
            {
                title = JsonSerializer.Deserialize<string>("\"" + m.Groups[2].Value + "\"");
            }
            catch (JsonException)
            {
                title = null;
            }
            hits.Add(new VideoHit("youtube", "https://www.youtube.com/watch?v=" + id, Cut(title, 300), $"https://i.ytimg.com/vi/{id}/mqdefault.jpg"));
            if (hits.Count >= 10)
                break;
        }
        return hits;
    }

    /// <summary>
    /// امتیازدهی و انتخاب نهایی: ویدیوی تأمین‌کننده همیشه می‌ماند؛ بقیه فقط با امتیاز ≥ <see cref="MinScore"/>؛ بهترین‌ها اول، بدون تکرار،
    /// حداکثر <see cref="MaxPerProduct"/>.
    /// </summary>
    public static List<VideoHit> Rank(IEnumerable<VideoHit> hits, string? productName, string? englishName) =>
        hits.Select(h => h.Source == "supplier" ? h : h with { Score = Score(productName, englishName, h.Title) })
            .Where(h => h.Score >= MinScore)
            .GroupBy(h => h.PageUrl, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderByDescending(h => h.Score).ThenBy(h => h.Source == "supplier" ? 0 : h.Source == "aparat" ? 1 : 2)
            .Take(MaxPerProduct)
            .ToList();

    /// <summary>
    /// عبارت جست‌وجو: نام انگلیسی (اگر هست) وگرنه نام فارسی، کوتاه‌شده تا ۸۰ نویسه. مثال: («هندزفری شیائومی»، «Xiaomi Redmi Buds 4») ← «Xiaomi Redmi Buds 4».
    /// </summary>
    public static string Query(string? productName, string? englishName)
    {
        var q = !string.IsNullOrWhiteSpace(englishName) ? englishName.Trim() : (productName ?? "").Trim();
        return q.Length > 80 ? q[..80] : q;
    }

    /// <summary>شناسهٔ ویدیوی آپارات از آدرس /v/{hash}. مثال: «https://www.aparat.com/v/i7228or» ← «i7228or».</summary>
    public static string? AparatHash(string? pageUrl)
    {
        var match = pageUrl is null ? null : AparatPage().Match(pageUrl);
        return match is { Success: true } ? match.Groups[1].Value : null;
    }

    private static readonly char[] Separators = [' ', '،', ',', '(', ')', '[', ']', '/', '|', ':', '«', '»', '"', '\'', '!', '؟', '?', '.', '+'];

    private static List<string> Tokens(string? text) =>
        PersianText.Normalize(text).Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !Generic.Contains(t))
            .ToList();

    private static bool HasDigit(string token) => token.Any(char.IsDigit);

    private static bool IsLatinOrModel(string token) => token.Any(c => c is >= 'a' and <= 'z') || token.All(char.IsDigit);

    private static string? Cut(string? text, int max) => string.IsNullOrWhiteSpace(text) ? null : text.Length > max ? text[..max] : text.Trim();

    private static string? Https(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && url!.Length <= 500 ? url : null;

    [GeneratedRegex(@"aparat\.com/(?:v/|video/video/embed/videohash/)([A-Za-z0-9]{3,20})", RegexOptions.IgnoreCase)]
    private static partial Regex AparatLink();

    [GeneratedRegex(@"^https://www\.aparat\.com/v/([A-Za-z0-9]{3,20})$")]
    private static partial Regex AparatPage();

    [GeneratedRegex(@"(?:youtube(?:-nocookie)?\.com/(?:embed/|watch\?v=)|youtu\.be/)([\w-]{11})", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeLink();

    [GeneratedRegex(@"<(?:video|source)\b[^>]*?\bsrc\s*=\s*[""']([^""'<>]+?\.(?:mp4|webm)(?:\?[^""'<>]*)?)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex VideoFile();

    [GeneratedRegex(@"""videoRenderer"":\{""videoId"":""([\w-]{11})""[\s\S]{0,3000}?""title"":\{""runs"":\[\{""text"":""((?:[^""\\]|\\.){1,300})""")]
    private static partial Regex YouTubeResult();

    [GeneratedRegex(@"^[A-Za-z0-9]{3,20}$")]
    private static partial Regex SafeId();
}

/// <summary>
/// دور ۲۹ (#۱۸) – ذخیره و مدیریت «ویدیوهای پیشنهادی» کالا. جست‌وجو در لایهٔ وب است (VideoSuggestionFinder)؛ این‌جا فقط دیتابیس.
/// </summary>
public interface IVideoSuggestionService
{
    /// <summary>
    /// کالاهای نامزد جست‌وجوی ویدیو: کالای واقعی (منتشرشده یا پیش‌نویس، غیرسیستمی) که ویدیوی فعال ندارد، پیشنهاد منتظر ندارد و
    /// شناسه‌اش در <paramref name="recentlyScanned"/> نیست. پربازدیدها و تازه‌ها اول.
    /// </summary>
    Task<List<(Guid Id, string Name, string? EnglishName, string? SupplierUrl)>> CandidatesAsync(int take, IReadOnlyCollection<Guid> recentlyScanned,
        CancellationToken cancellationToken = default);

    /// <summary>اطلاعات جست‌وجوی یک کالا (برای دکمهٔ «جست‌وجوی همین کالا»). کالای ناموجود ← null.</summary>
    Task<(Guid Id, string Name, string? EnglishName, string? SupplierUrl)?> ProductAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>ذخیرهٔ پیشنهادهای تازه (لینک تکراری یا قبلاً ردشده دوباره ذخیره نمی‌شود). خروجی: تعداد ذخیره‌شده.</summary>
    Task<int> AddAsync(Guid productId, IReadOnlyList<VideoHit> hits, CancellationToken cancellationToken = default);

    /// <summary>فهرست پنل: پیش‌فرض فقط «تازه»ها (جدیدترین اول؛ حداکثر ۲۰۰).</summary>
    Task<List<VideoSuggestionRow>> ListAsync(VideoSuggestionStatus? status, Guid? productId = null, CancellationToken cancellationToken = default);

    /// <summary>تعداد پیشنهادهای تازه (نشان منو).</summary>
    Task<int> PendingCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// «استفاده شد» یا «رد». با useAsAparat=true و منبع آپارات، ویدیو همان لحظه به‌صورت جاسازی آپارات به کالا اضافه می‌شود
    /// (مدیر آن را دیده و تأیید کرده است).
    /// </summary>
    Task<OperationResult> SetStatusAsync(Guid id, VideoSuggestionStatus status, bool useAsAparat = false, CancellationToken cancellationToken = default);
}

public sealed class VideoSuggestionService(FratakShopDbContext context) : IVideoSuggestionService
{
    public async Task<List<(Guid Id, string Name, string? EnglishName, string? SupplierUrl)>> CandidatesAsync(int take, IReadOnlyCollection<Guid> recentlyScanned,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 0, 50);
        if (take == 0)
            return [];
        var skip = recentlyScanned.Take(5000).ToList();
        var rows = await context.Products.AsNoTracking()
            // کالای منتشرشده، یا پیش‌نویسِ واردشده از تأمین‌کننده (همهٔ کالاهای واقعی؛ نه پیش‌نویس خالی).
            .Where(p => p.IsActive || p.SupplierUrl != null)
            .Where(p => !context.ProductVideos.Any(v => v.ProductId == p.Id && v.IsActive))
            .Where(p => !context.ProductVideoSuggestions.Any(s => s.ProductId == p.Id && s.Status == VideoSuggestionStatus.New))
            .Where(p => !skip.Contains(p.Id))
            .OrderByDescending(p => p.IsActive).ThenByDescending(p => p.ViewCount).ThenByDescending(p => p.CreatedAt)
            .Take(take)
            .Select(p => new { p.Id, p.Name, p.EnglishName, p.SupplierUrl })
            .ToListAsync(cancellationToken);
        return rows.Select(r => (r.Id, r.Name, r.EnglishName, r.SupplierUrl)).ToList();
    }

    public async Task<(Guid Id, string Name, string? EnglishName, string? SupplierUrl)?> ProductAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var row = await context.Products.AsNoTracking().Where(p => p.Id == productId)
            .Select(p => new { p.Id, p.Name, p.EnglishName, p.SupplierUrl }).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : (row.Id, row.Name, row.EnglishName, row.SupplierUrl);
    }

    public async Task<int> AddAsync(Guid productId, IReadOnlyList<VideoHit> hits, CancellationToken cancellationToken = default)
    {
        if (hits.Count == 0)
            return 0;
        var urls = hits.Select(h => h.PageUrl).ToList();
        // IgnoreQueryFilters: لینکی که قبلاً پیشنهاد/رد/حذف شده دوباره نمی‌آید.
        var existing = await context.ProductVideoSuggestions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.ProductId == productId && urls.Contains(s.PageUrl))
            .Select(s => s.PageUrl).ToListAsync(cancellationToken);
        var known = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var hit in hits.Where(h => h.PageUrl.Length <= 500 && known.Add(h.PageUrl)))
        {
            context.ProductVideoSuggestions.Add(new ProductVideoSuggestion
            {
                ProductId = productId,
                Source = hit.Source.Length > 30 ? hit.Source[..30] : hit.Source,
                PageUrl = hit.PageUrl,
                Title = hit.Title is { Length: > 300 } t ? t[..300] : hit.Title,
                ThumbnailUrl = hit.ThumbnailUrl is { Length: <= 500 } ? hit.ThumbnailUrl : null,
                Score = Math.Clamp(hit.Score, 0, 100),
            });
            added++;
        }
        if (added > 0)
            await context.SaveChangesAsync(cancellationToken);
        return added;
    }

    public async Task<List<VideoSuggestionRow>> ListAsync(VideoSuggestionStatus? status, Guid? productId = null, CancellationToken cancellationToken = default)
    {
        var query = context.ProductVideoSuggestions.AsNoTracking();
        if (status is { } s)
            query = query.Where(v => v.Status == s);
        if (productId is { } id)
            query = query.Where(v => v.ProductId == id);
        var rows = await query.OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.Score).Take(200)
            .Select(v => new
            {
                v.Id, v.ProductId, v.Source, v.PageUrl, v.Title, v.ThumbnailUrl, v.Score, v.Status, v.CreatedAt,
                Name = context.Products.Where(p => p.Id == v.ProductId).Select(p => p.Name).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new VideoSuggestionRow(r.Id, r.ProductId, r.Name ?? "(کالای حذف‌شده)", r.Source, r.PageUrl, r.Title, r.ThumbnailUrl,
            r.Score, r.Status, r.CreatedAt)).ToList();
    }

    public Task<int> PendingCountAsync(CancellationToken cancellationToken = default) =>
        context.ProductVideoSuggestions.CountAsync(v => v.Status == VideoSuggestionStatus.New, cancellationToken);

    public async Task<OperationResult> SetStatusAsync(Guid id, VideoSuggestionStatus status, bool useAsAparat = false, CancellationToken cancellationToken = default)
    {
        var row = await context.ProductVideoSuggestions.AsTracking().FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (row is null)
            return OperationResult.NotFound("پیشنهاد پیدا نشد.");
        if (useAsAparat)
        {
            var hash = VideoSuggestionRules.AparatHash(row.PageUrl);
            if (row.Source != "aparat" && hash is null)
                return OperationResult.Error("فقط ویدیوی آپارات مستقیم اضافه می‌شود؛ بقیه را دانلود و در «ویدیوهای معرفی محصول» بارگذاری کنید.");
            if (hash is null)
                return OperationResult.Error("شناسهٔ ویدیوی آپارات خوانده نشد.");
            if (!await context.Products.AnyAsync(p => p.Id == row.ProductId, cancellationToken))
                return OperationResult.NotFound("کالای این پیشنهاد حذف شده است.");
            var order = await context.ProductVideos.Where(v => v.ProductId == row.ProductId).Select(v => (int?)v.DisplayOrder).MaxAsync(cancellationToken) ?? 0;
            context.ProductVideos.Add(new ProductVideo
            {
                ProductId = row.ProductId,
                Provider = ProductVideoProvider.Aparat,
                Source = hash,
                Title = row.Title is { Length: > 200 } t ? t[..200] : row.Title,
                DisplayOrder = order + 1,
                IsActive = true,
            });
            status = VideoSuggestionStatus.Used;
        }
        row.Status = status;
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success(useAsAparat
            ? "ویدیوی آپارات به کالا اضافه شد."
            : status == VideoSuggestionStatus.Dismissed ? "پیشنهاد رد شد و دوباره نمی‌آید." : "علامت «استفاده شد» خورد.");
    }
}
