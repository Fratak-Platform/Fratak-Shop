using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Infrastructure.Commerce;

/// <summary>نتیجهٔ جست‌وجوی تصویری. Busy = سرور الان شلوغ است؛ Invalid = فایل تصویر معتبری نبود.</summary>
public sealed record ImageSearchResult(IReadOnlyList<ProductFilterData> Products, bool Busy = false, bool Invalid = false, bool Approximate = false);

/// <summary>
/// دور ۲۸: وضعیت امضاهای جست‌وجوی تصویری برای صفحهٔ «سلامت داده» پنل.
/// <para>Active = کالاهای فعال عکس‌دار؛ Signed = امضای سالم دارند؛ Broken = عکسشان خوانده نشد (فایل گم/خراب)؛ Pending = هنوز ساخته نشده.</para>
/// مثال: Active=120، Signed=118، Broken=2 ← دو کالا عکس خراب دارند و در جست‌وجوی تصویری پیدا نمی‌شوند.
/// </summary>
public sealed record ImageSearchHealth(int Active, int Signed, int Broken, int Pending);

/// <summary>
/// دور ۲۴ (پیشنهاد ۲۹ – جست‌وجو با عکس): مشتری عکسی می‌فرستد و کالاهای شبیه (شکل و رنگ) پیدا می‌شوند.
/// <para>
/// ساخت امضا (<see cref="BuildSignaturesAsync"/>): هر ۵ دقیقه حداکثر ۴۰ کالای فعال که امضای تصویر اصلی‌شان نیست یا تصویرشان عوض شده
/// امضا می‌گیرند (کار پس‌زمینه در EngagementMaintenanceService). امضای کالای حذف‌شده هم پاک می‌شود.
/// </para>
/// <para>
/// جست‌وجو (<see cref="SearchAsync"/>): امضای عکس مشتری ساخته و با امضای همهٔ کالاها (از حافظه، ۱۰ دقیقه) مقایسه می‌شود؛
/// حداکثر ۸ کالا با امتیاز دست‌کم <see cref="MinScore"/>. عکس مشتری هیچ‌جا ذخیره نمی‌شود.
/// </para>
/// <para>سبکی و ایمنی: حداکثر ۲ جست‌وجوی هم‌زمان (بقیه ۲ ثانیه صبر و بعد «شلوغ»)، حداکثر ۴ مگابایت، سقف پیکسل؛
/// سهمیهٔ جدا در RateLimitingSetup (۱۰ در دقیقه برای هر IP).</para>
/// <para>محل استفاده: POST /api/search/image (CommerceEndpoints) و assets/js/commerce.js (دکمهٔ دوربین کنار جست‌وجو).</para>
/// </summary>
public sealed class ImageSearchService(
    FratakShopDbContext context,
    IProductService products,
    IUploadStore uploads,
    IMemoryCache cache,
    ILogger<ImageSearchService> logger)
{
    /// <summary>
    /// کمترین امتیاز شباهت برای نشان دادن یک کالا. دور ۲۶: با امتیاز تازه (pHash + همهٔ عکس‌ها) همان عکس حدود ۰٫۹۵ و
    /// عکس بی‌ربط حدود ۰٫۳ می‌گیرد؛ ۰٫۵۵ یعنی «واقعاً شبیه».
    /// </summary>
    public const double MinScore = 0.55;

    /// <summary>دور ۲۶: اگر بهترین نتیجه «همان عکس» است (≥ این امتیاز)، نتیجه‌های خیلی ضعیف‌تر از آن (۰٫۲ کمتر) نشان داده نمی‌شوند.</summary>
    public const double ExactScore = 0.88;

    /// <summary>بیشترین تعداد نتیجه.</summary>
    public const int MaxResults = 8;

    /// <summary>تعداد امضای ساخته‌شده در هر نوبت پس‌زمینه (دور ۲۶: ۶۰؛ هر عکس کوچک‌شده باز می‌شود و سبک است).</summary>
    public const int BatchSize = 60;

    /// <summary>
    /// دور ۲۸: اگر هیچ کالایی به <see cref="MinScore"/> نرسید، تا ۴ کالا با امتیاز دست‌کم این عدد به‌عنوان «شبیه‌ترین‌ها»
    /// (برچسب «شاید») نشان داده می‌شود تا مشتری با صفحهٔ خالی روبه‌رو نشود. عکس‌های بی‌ربط معمولاً زیر ۰٫۴۵ می‌مانند.
    /// </summary>
    public const double ApproximateScore = 0.47;

    /// <summary>دور ۲۸: سقف نتیجه‌های «شاید».</summary>
    public const int MaxApproximate = 4;

    private const string CacheKey = "imgsig:all";
    private static readonly SemaphoreSlim Gate = new(2, 2);


    /// <summary>جست‌وجوی کالاهای شبیه به عکس (بایت‌های فایل).</summary>
    public Task<ImageSearchResult> SearchAsync(byte[] data, CancellationToken cancellationToken) => SearchAsync(data, null, cancellationToken);

    /// <summary>
    /// دور ۲۹ (#۱) – جست‌وجوی دقیق‌تر با عکس:
    /// <list type="number">
    /// <item>نسخه‌های عکس مشتری (بدون حاشیه، کل قاب، وسط، و تازه: «سوژهٔ جداشده» بدون زمینهٔ شلوغ) با همهٔ عکس‌های هر کالا مقایسه می‌شوند.</item>
    /// <item>عکسی که فقط هم‌رنگ است ولی شکلش فرق دارد دیگر نتیجه نمی‌شود (<see cref="ImageSignature.ShapeFloor"/>).</item>
    /// <item><paramref name="hint"/> (متنی که مشتری در کادر جست‌وجو نوشته، اختیاری): کالایی که نامش آن واژه را دارد امتیاز بیشتری می‌گیرد.</item>
    /// <item>هم‌خوانی دسته: وقتی بهترین نتیجه مطمئن است، کالای دستهٔ دیگر فقط اگر تقریباً هم‌امتیاز باشد می‌آید.</item>
    /// </list>
    /// </summary>
    public async Task<ImageSearchResult> SearchAsync(byte[] data, string? hint, CancellationToken cancellationToken)
    {
        if (!await Gate.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken))
            return new ImageSearchResult([], Busy: true);
        try
        {
            var variants = ImageSignature.ComputeVariants(data);
            if (variants.Count == 0)
                return new ImageSearchResult([], Invalid: true);

            var entries = await LoadAsync(cancellationToken);
            var scored = entries
                .Select(e => (e.ProductId, Score: ImageSignature.Score(variants, e)))
                .Where(x => x.Score >= CandidateFloor)
                .OrderByDescending(x => x.Score).ThenBy(x => x.ProductId)
                .Take(MaxCandidates)
                .ToList();
            if (scored.Count == 0)
                return new ImageSearchResult([]);
            // کارت‌ها (نام و دسته) فقط برای همین چند نامزد – یک پرس‌وجو.
            var cards = await products.GetCardsByIdsAsync(scored.Select(x => x.ProductId));
            var byId = cards.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
            var candidates = scored.Where(x => byId.ContainsKey(x.ProductId))
                .Select(x => new ImageCandidate(x.ProductId, x.Score, byId[x.ProductId].CategoryId, byId[x.ProductId].Name + " " + byId[x.ProductId].EnglishName))
                .ToList();
            var (ids, approximate) = Decide(candidates, HintTokens(hint));
            return new ImageSearchResult(ids.Select(id => byId[id]).ToList(), Approximate: approximate);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>دور ۲۹: یک نامزد امتیازدار برای تصمیم نهایی (خالص و آزمون‌پذیر).</summary>
    public sealed record ImageCandidate(Guid ProductId, double Score, Guid CategoryId, string Name);

    /// <summary>دور ۲۹: امتیاز اضافهٔ کالایی که نامش واژهٔ متن مشتری را دارد.</summary>
    public const double HintBoost = 0.12;

    /// <summary>دور ۲۹: کمترین امتیاز تصویری برای بررسی (با امتیاز متن هم به حد «شاید» نمی‌رسد اگر کمتر باشد).</summary>
    public const double CandidateFloor = ApproximateScore - HintBoost;

    /// <summary>دور ۲۹: بیشترین نامزد بررسی‌شده (کارت‌هایشان با یک پرس‌وجو خوانده می‌شود).</summary>
    public const int MaxCandidates = 30;

    /// <summary>دور ۲۹: وقتی بهترین نتیجه دست‌کم این‌قدر از حد اصلی بالاتر است، کالای دستهٔ دیگر باید حداکثر <see cref="CrossCategoryGap"/> ضعیف‌تر باشد.</summary>
    public const double ConfidentMargin = 0.1;

    /// <summary>دور ۲۹: بیشترین فاصلهٔ امتیاز کالای دستهٔ دیگر از بهترین نتیجهٔ مطمئن.</summary>
    public const double CrossCategoryGap = 0.08;

    /// <summary>
    /// دور ۲۹ (#۱) – تصمیم نهایی (خالص): امتیاز متن ← حد اصلی (همان عکس؛ هم‌خوانی دسته) ← اگر هیچ، «شاید»ها.
    /// مثال: عکس قوری با متن «قوری» ← قوری‌ها اول؛ بهترین نتیجه «قوری» با ۰٫۸ ← لیوانِ دستهٔ دیگر با ۰٫۶ کنار می‌رود.
    /// </summary>
    public static (List<Guid> Ids, bool Approximate) Decide(IReadOnlyList<ImageCandidate> candidates, IReadOnlyList<string> hintTokens)
    {
        var list = candidates
            .Select(c => hintTokens.Count > 0 && HintMatches(c.Name, hintTokens) ? c with { Score = c.Score + HintBoost } : c)
            .OrderByDescending(c => c.Score).ThenBy(c => c.ProductId)
            .ToList();
        var main = list.Where(c => c.Score >= MinScore).ToList();
        if (main.Count > 0)
        {
            var best = main[0];
            if (best.Score >= ExactScore)
                main = main.Where(c => c.Score >= best.Score - 0.2).ToList();
            if (best.Score >= MinScore + ConfidentMargin)
                main = main.Where(c => c.CategoryId == best.CategoryId || c.Score >= best.Score - CrossCategoryGap).ToList();
            return (main.Take(MaxResults).Select(c => c.ProductId).ToList(), false);
        }
        var approximate = list.Where(c => c.Score >= ApproximateScore).Take(MaxApproximate).Select(c => c.ProductId).ToList();
        return (approximate, approximate.Count > 0);
    }

    /// <summary>واژه‌هایی از متن مشتری که چیزی دربارهٔ «کدام کالا» نمی‌گویند.</summary>
    private static readonly HashSet<string> HintStopWords = new(StringComparer.Ordinal)
    {
        "خرید", "قیمت", "ارزان", "اصل", "و", "با", "برای", "مدل", "جدید", "عکس", "این", "همین", "شبیه", "مثل",
    };

    /// <summary>
    /// واژه‌های متن مشتری (نرمال‌شده، حداقل ۲ نویسه، بدون واژه‌های عمومی، حداکثر ۶). مثال: «خرید قوری چینی» ← [قوری، چینی].
    /// </summary>
    public static List<string> HintTokens(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
            return [];
        var text = Fratak_Shop.Core.Services.Seo.Engine.PersianText.Normalize(hint.Length > 60 ? hint[..60] : hint);
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2 && !HintStopWords.Contains(t))
            .Distinct(StringComparer.Ordinal)
            .Take(6)
            .ToList();
    }

    /// <summary>آیا نام کالا (نرمال‌شده) یکی از واژه‌ها را دارد؟ مثال: («قوری چینی گل‌دار»، [قوری]) ← true.</summary>
    public static bool HintMatches(string? name, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0 || string.IsNullOrWhiteSpace(name))
            return false;
        var normalized = Fratak_Shop.Core.Services.Seo.Engine.PersianText.Normalize(name);
        return tokens.Any(t => normalized.Contains(t, StringComparison.Ordinal));
    }

    /// <summary>
    /// رتبه‌بندی خالص (برای آزمون): شناسهٔ کالاهایی که امتیازشان دست‌کم <see cref="MinScore"/> است، از بیشترین به کمترین، حداکثر ۸ تا.
    /// </summary>
    public static List<Guid> Rank(ImageSignature.Signature query, IEnumerable<(Guid ProductId, long Hash, byte[] Colors)> candidates) =>
        candidates
            .Select(c => (c.ProductId, Score: ImageSignature.Score(query, c.Hash, c.Colors)))
            .Where(x => x.Score >= MinScore)
            .OrderByDescending(x => x.Score)
            .Take(MaxResults)
            .Select(x => x.ProductId)
            .ToList();

    /// <summary>
    /// دور ۲۶ – رتبه‌بندی خالص با امضای چندعکسی: امتیاز هر کالا = بهترین تطبیق (<see cref="ImageSignature.Score(IReadOnlyList{ImageSignature.Signature}, ImageSignature.Stored)"/>).
    /// اگر بهترین نتیجه «همان عکس» است، نتیجه‌هایی که ۰٫۲ ضعیف‌ترند کنار می‌روند (مشتری دنبال همان کالاست). حداکثر ۸ نتیجه.
    /// مثال: عکس دقیق کالای A ← A اول؛ کالای بی‌ربط هرگز.
    /// </summary>
    public static List<Guid> Rank(IReadOnlyList<ImageSignature.Signature> queries, IEnumerable<ImageSignature.Stored> candidates)
    {
        var scored = candidates
            .Select(c => (c.ProductId, Score: ImageSignature.Score(queries, c)))
            .Where(x => x.Score >= MinScore)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.ProductId)
            .Take(MaxResults)
            .ToList();
        if (scored.Count > 0 && scored[0].Score >= ExactScore)
            scored = scored.Where(x => x.Score >= scored[0].Score - 0.2).ToList();
        return scored.Select(x => x.ProductId).ToList();
    }

    /// <summary>
    /// دور ۲۸ – رتبه‌بندی «شاید»: وقتی هیچ کالایی به حد اصلی نرسید، حداکثر <see cref="MaxApproximate"/> کالا با امتیاز
    /// دست‌کم <see cref="ApproximateScore"/>. مثال: عکس دوربین گوشی از یک قوری روی میز ← قوری‌های فروشگاه با امتیاز ۰٫۵.
    /// </summary>
    public static List<Guid> RankApproximate(IReadOnlyList<ImageSignature.Signature> queries, IEnumerable<ImageSignature.Stored> candidates) =>
        candidates
            .Select(c => (c.ProductId, Score: ImageSignature.Score(queries, c)))
            .Where(x => x.Score >= ApproximateScore && x.Score < MinScore)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.ProductId)
            .Take(MaxApproximate)
            .Select(x => x.ProductId)
            .ToList();

    /// <summary>
    /// دور ۲۸: آیا یک ردیف امضا قابل مقایسه است؟ ردیف عکس خراب (هش صفر و بدون شکل) فقط «نویز» بود و
    /// به هر عکس تیره‌ای امتیاز بیخود می‌داد؛ حالا کنار گذاشته می‌شود.
    /// </summary>
    public static bool IsUsable(long hash, byte[]? hashes) => hash != 0 || ImageSignature.Unpack(hashes).Any(h => h.DHash != 0 || h.PHash != 0);

    /// <summary>دور ۲۸: آمار امضاها برای صفحهٔ سلامت داده.</summary>
    public async Task<ImageSearchHealth> HealthAsync(CancellationToken cancellationToken)
    {
        var active = await context.Products.CountAsync(p => p.IsActive && p.Images.Any(), cancellationToken);
        var rows = await context.ProductImageSignatures
            .Where(s => context.Products.Any(p => p.Id == s.ProductId && p.IsActive))
            .Select(s => new { s.Hash, s.Hashes })
            .ToListAsync(cancellationToken);
        var signed = rows.Count(r => IsUsable(r.Hash, r.Hashes));
        var broken = rows.Count - signed;
        return new ImageSearchHealth(active, signed, broken, Math.Max(0, active - rows.Count));
    }

    /// <summary>
    /// دور ۲۸: «بازسازی امضاها» از پنل – همهٔ امضاها نامعتبر می‌شوند تا کارگر پس‌زمینه (و همین متد، یک دسته) دوباره بسازد.
    /// خروجی: تعداد امضاهای ساخته‌شده در همین درخواست.
    /// </summary>
    public async Task<int> ResetAsync(CancellationToken cancellationToken)
    {
        await context.ProductImageSignatures.ExecuteUpdateAsync(s => s.SetProperty(x => x.ImageName, ""), cancellationToken);
        cache.Remove(CacheKey);
        return await BuildSignaturesAsync(cancellationToken);
    }

    /// <summary>
    /// امضای همهٔ کالاها از حافظه (۱۰ دقیقه). دور ۳۰ (#۱): کالای غیرفعال (مثلاً «آمادهٔ انتشار») هم امضا دارد تا به‌محض انتشار
    /// پیدا شود؛ نمایش فقط برای کالای فعال است (GetCardsByIdsAsync فقط کالای فعال را برمی‌گرداند).
    /// </summary>
    private async Task<List<ImageSignature.Stored>> LoadAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out List<ImageSignature.Stored>? cached) && cached is not null)
            return cached;
        var rows = await context.ProductImageSignatures
            .Select(s => new { s.ProductId, s.Hash, s.Colors, s.Hashes, s.Palettes })
            .ToListAsync(cancellationToken);
        // امضای قدیمی (بدون Hashes) فقط dHash دارد؛ تا ساخت دوباره با همان یک هش مقایسه می‌شود.
        // دور ۲۸: ردیف عکس خراب (همه‌چیز صفر) کنار می‌رود.
        var list = rows.Where(r => IsUsable(r.Hash, r.Hashes)).Select(r => ToStored(r.ProductId, r.Hash, r.Colors, r.Hashes, r.Palettes)).ToList();
        cache.Set(CacheKey, list, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10), Size = 1 });
        return list;
    }

    /// <summary>دور ۳۰ (#۱): ردیف ذخیره‌شده ← امضای قابل مقایسه (شکل‌ها + رنگ هر شکل). امضای قدیمی ← فقط همان یک هش.</summary>
    public static ImageSignature.Stored ToStored(Guid productId, long hash, byte[] colors, byte[]? hashes, byte[]? palettes)
    {
        var shapes = ImageSignature.Unpack(hashes);
        if (shapes.Count == 0)
            shapes.Add((hash, 0));
        var colorList = ImageSignature.UnpackPalettes(palettes);
        return new ImageSignature.Stored(productId, shapes, colors, colorList is not null && colorList.Count == shapes.Count ? colorList : null);
    }

    /// <summary>
    /// ساخت/به‌روزرسانی امضای حداکثر <see cref="BatchSize"/> کالا و پاک کردن امضای کالاهای حذف‌شده. خروجی: تعداد تغییرها.
    /// <para>دور ۳۰ (#۱) – چرا «عکس دقیق خود کالا» پیدا نمی‌شد و چه شد:</para>
    /// <list type="bullet">
    /// <item>فقط کالاهای فعال امضا می‌گرفتند ← حالا همهٔ کالاهای عکس‌دار (کالای تازه منتشرشده بی‌درنگ پیدا می‌شود).</item>
    /// <item>عوض شدن فایل یک عکس (تمیزکاری، عکس بدون زمینهٔ تازه) امضا را تازه نمی‌کرد ← حالا هر عکسی که بعد از ساخت امضا
    /// تغییر کرده/اضافه شده امضا را دوباره می‌سازد.</item>
    /// <item>هر عکس فقط با یک نسخه (بدون حاشیه) ذخیره می‌شد ← حالا ۳ نسخه و رنگ جدای هر عکس.</item>
    /// </list>
    /// </summary>
    public async Task<int> BuildSignaturesAsync(CancellationToken cancellationToken)
    {
        var removed = await context.ProductImageSignatures
            .Where(s => !context.Products.Any(p => p.Id == s.ProductId))
            .ExecuteDeleteAsync(cancellationToken);
        // کالایی که دیگر عکسی ندارد ← امضای کهنه‌اش هم پاک می‌شود (وگرنه با عکس قدیمی پیدا می‌شد).
        removed += await context.ProductImageSignatures
            .Where(s => !context.ProductImages.Any(i => i.ProductId == s.ProductId))
            .ExecuteDeleteAsync(cancellationToken);

        // امضا دوباره ساخته می‌شود اگر: عکس اصلی عوض شد، تعداد عکس‌ها تغییر کرد، امضا قدیمی است (بدون Hashes/Palettes)
        // یا یکی از عکس‌ها بعد از ساخت امضا اضافه/ویرایش شد. کالاهای فعال اول.
        var pending = await context.Products
            .Select(p => new
            {
                p.Id,
                p.IsActive,
                Image = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).FirstOrDefault(),
                Count = p.Images.Count,
            })
            .Where(p => p.Image != null
                        && !context.ProductImageSignatures.Any(s => s.ProductId == p.Id && s.ImageName == p.Image && s.ImageCount == p.Count
                            && s.Hashes != null && s.Palettes != null
                            && !context.ProductImages.Any(i => i.ProductId == p.Id && i.UpdatedAt > s.ComputedAt)))
            .OrderByDescending(p => p.IsActive)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var changed = 0;
        foreach (var item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // همهٔ عکس‌های کالا (اصلی اول؛ حداکثر ۸)، هر کدام تا ۳ نسخه با رنگ خودش.
            var names = await context.ProductImages.Where(i => i.ProductId == item.Id)
                .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName)
                .Take(ImageSignature.MaxImages).ToListAsync(cancellationToken);
            var primary = await SignAllAsync(item.Image!, item.Id, cancellationToken);
            var shapes = new List<ImageSignature.Signature>(primary);
            foreach (var name in names.Where(n => !string.Equals(n, item.Image, StringComparison.Ordinal)))
            {
                if (shapes.Count >= ImageSignature.MaxShapes)
                    break;
                foreach (var extra in await SignAllAsync(name, item.Id, cancellationToken))
                {
                    if (shapes.Count < ImageSignature.MaxShapes && shapes.All(x => x.Hash != extra.Hash || x.PHash != extra.PHash))
                        shapes.Add(extra);
                }
            }

            var row = await context.ProductImageSignatures.AsTracking().FirstOrDefaultAsync(s => s.ProductId == item.Id, cancellationToken);
            if (row is null)
            {
                row = new ProductImageSignature { ProductId = item.Id };
                context.ProductImageSignatures.Add(row);
            }
            // تصویر خراب/گمشده هم ثبت می‌شود (با امضای خالی) تا هر ۵ دقیقه دوباره امتحان نشود؛ با عوض شدن تصویر دوباره ساخته می‌شود.
            var main = primary.Count > 0 ? primary[0] : null;
            row.ImageName = item.Image!.Length > 200 ? item.Image[..200] : item.Image;
            row.Hash = main?.Hash ?? 0;
            row.Colors = main?.Colors ?? new byte[ImageSignature.Bins];
            row.Hashes = ImageSignature.Pack(shapes.Select(x => (x.Hash, x.PHash)));
            row.Palettes = ImageSignature.PackPalettes(shapes.Select(x => x.Colors));
            row.ImageCount = item.Count;
            row.ComputedAt = DateTimeOffset.UtcNow;
            changed++;
        }
        if (changed > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        if (changed + removed > 0)
            cache.Remove(CacheKey);
        return changed + removed;
    }

    /// <summary>دور ۳۰ (#۱): همهٔ نسخه‌های ذخیره‌ای یک فایل عکس کالا (<see cref="ImageSignature.ComputeStored"/>)؛ فایل نبود/خراب ← خالی.</summary>
    private async Task<List<ImageSignature.Signature>> SignAllAsync(string imageName, Guid productId, CancellationToken cancellationToken)
    {
        var path = uploads.GetPath(UploadArea.Products, imageName);
        if (path is null || !File.Exists(path))
            return [];
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            return ImageSignature.ComputeStored(bytes);
        }
        catch (IOException exception)
        {
            logger.LogDebug(exception, "Image signature read failed for {Product}", productId);
            return [];
        }
    }
}
