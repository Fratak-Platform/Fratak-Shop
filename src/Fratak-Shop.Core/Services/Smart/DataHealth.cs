using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Smart;

// ─────────────────────────────────────────────────────────────────────────────
// دور ۲۸ (#۵۱ تا #۵۹) – «سلامت داده»: هر کالا یک امتیاز اطمینان ۰ تا ۱۰۰ می‌گیرد.
// هر ایراد (نام بد، نام انگلیسی فارسی‌دار، قیمت زیر خرید، تخفیف نامعقول، عکس آدم‌دار/واترمارک‌دار …) امتیاز کم می‌کند.
// ایرادهای بی‌خطر (نام انگلیسی نامعتبر، تخفیف نامعتبر) خودکار درست می‌شوند (فقط پیش‌نویس‌ها)؛ بقیه با دلیل و راه‌حل در صفحهٔ «سلامت داده» نشان داده می‌شوند.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>شدت یک ایراد داده. هرچه شدیدتر، امتیاز بیشتری کم می‌شود (بحرانی ۳۰، هشدار ۱۲، نکته ۴).</summary>
public enum DataSeverity
{
    /// <summary>نکته (مثلاً عکس هنوز بررسی نشده).</summary>
    Info = 1,

    /// <summary>هشدار (مثلاً توضیح کوتاه است).</summary>
    Warning = 2,

    /// <summary>بحرانی (مثلاً فروش زیر قیمت خرید یا عکس آدم‌دار).</summary>
    Critical = 3,
}

/// <summary>یک ایراد: کد ثابت (برای شمارش و آزمون)، عنوان فارسی، شدت و راه‌حل کوتاه.</summary>
public sealed record DataIssue(string Code, string Title, DataSeverity Severity, string Fix);

/// <summary>نتیجهٔ سنجش یک کالا: امتیاز ۰ تا ۱۰۰ و فهرست ایرادها (بدترین اول).</summary>
public sealed record DataScore(int Score, IReadOnlyList<DataIssue> Issues)
{
    /// <summary>سطح خوانا: ۸۰ به بالا «سالم»، ۵۰ تا ۷۹ «ضعیف»، کمتر «نامعتبر».</summary>
    public string Level => Score >= DataConfidence.Healthy ? "سالم" : Score >= DataConfidence.Weak ? "ضعیف" : "نامعتبر";
}

/// <summary>
/// واقعیت‌های لازم برای سنجش یک کالا (از پایگاه داده خوانده می‌شود؛ این‌جا خالص و آزمون‌پذیر).
/// ImageScores/ImageFlags = امتیاز و نشانه‌های «بررسی سوژه» هر عکس (null = هنوز بررسی نشده).
/// </summary>
public sealed record ProductDataFacts(
    string Name,
    string? EnglishName,
    bool CategoryIsSystem,
    decimal Price,
    decimal? DiscountPrice,
    decimal? PurchasePrice,
    int Stock,
    int DescriptionLength,
    int ShortLength,
    IReadOnlyList<byte?> ImageScores,
    IReadOnlyList<byte?> ImageFlags,
    string? SupplierUrl = null);

/// <summary>
/// موتور خالص «امتیاز اطمینان داده». مثال: کالای کامل با عکس روی سفید ← ۱۰۰؛ قیمت فروش زیر قیمت خرید ← بحرانی «فروش با ضرر».
/// </summary>
public static class DataConfidence
{
    /// <summary>از این امتیاز به بالا «سالم».</summary>
    public const int Healthy = 80;

    /// <summary>از این امتیاز به بالا «ضعیف»؛ کمتر «نامعتبر».</summary>
    public const int Weak = 50;

    /// <summary>کمترین امتیاز «بررسی سوژه» (درصد) که عکس را قابل قبول می‌داند (همان ۰٫۴۵ بررسی سوژه).</summary>
    public const int MinImageScore = 45;

    /// <summary>
    /// سنجش یک کالا. هر بررسی مستقل است و فقط با داده‌ای که واقعاً هست کار می‌کند (قیمت خرید نامعلوم ← بررسی ضرر انجام نمی‌شود).
    /// </summary>
    public static DataScore Product(ProductDataFacts facts)
    {
        var issues = new List<DataIssue>();
        void Add(string code, string title, DataSeverity severity, string fix) => issues.Add(new DataIssue(code, title, severity, fix));

        // ── نام ──
        var name = (facts.Name ?? "").Trim();
        if (name.Length < 5)
            Add("name-short", "نام کالا خیلی کوتاه است", DataSeverity.Critical, "نام کامل (نوع + برند + مدل) بنویسید.");
        if (name.Contains("http", StringComparison.OrdinalIgnoreCase) || name.Contains('<') || name.Contains("www.", StringComparison.OrdinalIgnoreCase))
            Add("name-junk", "نام کالا آدرس یا کد HTML دارد", DataSeverity.Critical, "آدرس/برچسب را از نام بردارید.");

        // ── نام انگلیسی ──
        if (string.IsNullOrWhiteSpace(facts.EnglishName))
            Add("english-missing", "نام انگلیسی ندارد", DataSeverity.Warning, "«آماده‌سازی دوباره» خودش نام انگلیسی را پیدا می‌کند.");
        else if (!ProductNaming.IsUsableEnglish(facts.EnglishName))
            Add("english-invalid", "نام انگلیسی نامعتبر است (حرف فارسی یا خیلی کوتاه)", DataSeverity.Critical, "«درست کردن خودکار» آن را پاک می‌کند تا دوباره پیدا شود.");

        // ── دسته ──
        if (facts.CategoryIsSystem)
            Add("category", "دستهٔ واقعی ندارد", DataSeverity.Critical, "دسته‌بندی هوشمند یا انتخاب دستی دسته.");

        // ── قیمت ──
        var final = facts.DiscountPrice is > 0 && facts.DiscountPrice < facts.Price ? facts.DiscountPrice.Value : facts.Price;
        if (facts.Price <= 0)
            Add("price-missing", "قیمت ندارد", DataSeverity.Critical, "قیمت‌گذاری هوشمند یا قیمت دستی.");
        if (facts.DiscountPrice is { } discount && facts.Price > 0)
        {
            if (discount <= 0 || discount >= facts.Price)
                Add("discount-invalid", "قیمت تخفیف نامعتبر است (صفر یا بیشتر از قیمت اصلی)", DataSeverity.Critical, "«درست کردن خودکار» تخفیف نامعتبر را برمی‌دارد.");
            else if (discount < facts.Price * 0.1m)
                Add("discount-suspicious", "تخفیف بیش از ۹۰٪ است", DataSeverity.Warning, "قیمت تخفیف را بررسی کنید (شاید یک صفر جا افتاده).");
        }
        if (facts.PurchasePrice is > 0 && facts.Price > 0)
        {
            if (final < facts.PurchasePrice.Value)
                Add("loss", "قیمت فروش کمتر از قیمت خرید است", DataSeverity.Critical, "قیمت فروش یا قیمت خرید را اصلاح کنید.");
            else if (facts.Price > facts.PurchasePrice.Value * 5)
                Add("price-high", "قیمت فروش بیش از ۵ برابر قیمت خرید است", DataSeverity.Warning, "شاید یک صفر اضافه است؛ بررسی کنید.");
        }

        // ── موجودی ──
        if (facts.Stock < 0)
            Add("stock-negative", "موجودی منفی است", DataSeverity.Critical, "موجودی را از صفحهٔ کالا یا همگام‌سازی تأمین‌کننده درست کنید.");

        // ── متن ──
        if (facts.DescriptionLength < ProductCompleteness.ReadyDescriptionLength)
            Add("description", "توضیحات کامل کوتاه است", DataSeverity.Warning, "«آماده‌سازی دوباره» توضیحات را کامل می‌کند.");
        if (facts.ShortLength < ProductCompleteness.ReadyShortLength)
            Add("short", "توضیح کوتاه ندارد", DataSeverity.Warning, "از اول توضیحات ساخته می‌شود.");

        // ── عکس ──
        var flags = facts.ImageFlags.Aggregate(0, (all, f) => all | (f ?? 0));
        if (facts.ImageScores.Count == 0)
            Add("image-missing", "عکس ندارد", DataSeverity.Critical, "یابندهٔ عکس هوشمند یا بارگذاری دستی.");
        if ((flags & 4) != 0)
            Add("image-people", "عکس آدم دارد", DataSeverity.Critical, "عکس را حذف و عکس تازه بگیرید.");
        if ((flags & 8) != 0)
            Add("image-watermark", "عکس واترمارک یا لوگوی سایت دیگر دارد", DataSeverity.Critical, "عکس را حذف و عکس تازه بگیرید.");
        if ((flags & 16) != 0)
            Add("image-scene", "عکس صحنه‌دار است (نه خود کالا روی زمینهٔ ساده)", DataSeverity.Warning, "عکس روی زمینهٔ سفید بهتر است.");
        var checkedScores = facts.ImageScores.Where(s => s is not null).Select(s => (int)s!.Value).ToList();
        if (checkedScores.Count > 0 && checkedScores.Max() < MinImageScore)
            Add("image-weak", "هیچ عکسی خود کالا را واضح نشان نمی‌دهد", DataSeverity.Warning, "عکس بهتر بگیرید.");
        if (facts.ImageScores.Any(s => s is null))
            Add("image-unchecked", "عکس هنوز بررسی نشده", DataSeverity.Info, "بررسی خودکار هر ساعت انجام می‌شود.");

        // ── تأمین‌کننده ──
        if (!string.IsNullOrWhiteSpace(facts.SupplierUrl)
            && !(Uri.TryCreate(facts.SupplierUrl.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)))
            Add("supplier-url", "آدرس تأمین‌کننده نامعتبر است", DataSeverity.Warning, "آدرس کامل صفحهٔ کالا در سایت تأمین‌کننده را بگذارید.");

        var penalty = issues.Sum(i => i.Severity switch { DataSeverity.Critical => 30, DataSeverity.Warning => 12, _ => 4 });
        var ordered = issues.OrderByDescending(i => i.Severity).ToList();
        return new DataScore(Math.Clamp(100 - penalty, 0, 100), ordered);
    }
}

/// <summary>یک ردیف گزارش: کالا، امتیاز و ایرادها.</summary>
public sealed record DataHealthRow(Guid Id, string Name, bool IsActive, DataScore Result);

/// <summary>عکس مشکوک (آدم، واترمارک، صحنه یا امتیاز کم).</summary>
public sealed record DataImageRow(Guid ProductId, string ProductName, string ImageName, byte? Score, byte Flags);

/// <summary>شمار یک نوع ایراد در کل فروشگاه.</summary>
public sealed record DataIssueCount(string Code, string Title, DataSeverity Severity, int Count);

/// <summary>گزارش «سلامت داده».</summary>
public sealed record DataHealthReport(int Products, int Average, int Healthy, int Weak, int Invalid,
    IReadOnlyList<DataIssueCount> Issues, IReadOnlyList<DataHealthRow> Worst, IReadOnlyList<DataImageRow> BadImages, int UncheckedImages);

/// <summary>سرویس «سلامت داده»: گزارش، درست کردن خودکار، بررسی دوبارهٔ عکس‌ها و آماده‌سازی دوبارهٔ یک کالا.</summary>
public interface IDataHealthService
{
    /// <summary>گزارش (تازه‌ترین <paramref name="take"/> کالا؛ پیش‌فرض ۳۰۰۰). <paramref name="issue"/> = فقط کالاهای دارای این کد ایراد.</summary>
    Task<DataHealthReport> ReportAsync(string? issue = null, int take = 3000, CancellationToken cancellationToken = default);

    /// <summary>
    /// درست کردن خودکار ایرادهای بی‌خطر (فقط پیش‌نویس‌های منتشرنشده تا صفحهٔ فروشگاه بی‌خبر عوض نشود):
    /// نام انگلیسی نامعتبر ← پاک (تا آماده‌سازی دوباره پیدا کند) و تخفیف نامعتبر (صفر یا نه کمتر از قیمت اصلی) ← برداشته.
    /// (موجودی منفی را خود پایگاه داده با قید CHECK رد می‌کند؛ این‌جا فقط برای دادهٔ واردشده از بیرون در امتیاز دیده می‌شود.)
    /// خروجی: شمار کالاهای اصلاح‌شده.
    /// </summary>
    Task<int> FixAsync(CancellationToken cancellationToken = default);

    /// <summary>امتیاز «بررسی سوژه» عکس‌ها (یک کالا یا همه) پاک می‌شود تا کارگر ساعتی دوباره بررسی کند. خروجی: شمار عکس‌ها.</summary>
    Task<int> RecheckImagesAsync(Guid? productId, CancellationToken cancellationToken = default);

    /// <summary>آماده‌سازی دوبارهٔ یک پیش‌نویس (شمار تلاش صفر می‌شود). کالای منتشرشده ← خطا؛ نبود ← NotFound.</summary>
    Task<OperationResult> ReprepareAsync(Guid productId, CancellationToken cancellationToken = default);
}

/// <summary>پیاده‌سازی <see cref="IDataHealthService"/> روی پایگاه داده.</summary>
public sealed class DataHealthService(FratakShopDbContext context) : IDataHealthService
{
    /// <summary>بیشترین شمار ردیف‌های «بدترین کالاها» و «عکس‌های مشکوک» در گزارش.</summary>
    public const int MaxRows = 60;

    public async Task<DataHealthReport> ReportAsync(string? issue = null, int take = 3000, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 10_000);
        var rows = await context.Products
            .OrderByDescending(p => p.CreatedAt)
            .Take(take)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.EnglishName,
                p.IsActive,
                CategoryIsSystem = p.Category.IsSystem,
                p.Price,
                p.DiscountPrice,
                p.PurchasePrice,
                p.StockQuantity,
                DescriptionLength = p.Description == null ? 0 : p.Description.Length,
                ShortLength = p.ShortDescription == null ? 0 : p.ShortDescription.Length,
                p.SupplierUrl,
                Images = p.Images.Select(i => new { i.SubjectScore, i.SubjectFlags }).ToList(),
            })
            .ToListAsync(cancellationToken);

        var scored = rows.Select(r => new DataHealthRow(r.Id, r.Name, r.IsActive, DataConfidence.Product(new ProductDataFacts(
                r.Name, r.EnglishName, r.CategoryIsSystem, r.Price, r.DiscountPrice, r.PurchasePrice, r.StockQuantity,
                r.DescriptionLength, r.ShortLength,
                r.Images.Select(i => i.SubjectScore).ToList(), r.Images.Select(i => i.SubjectFlags).ToList(), r.SupplierUrl))))
            .ToList();

        var counts = scored.SelectMany(s => s.Result.Issues)
            .GroupBy(i => i.Code)
            .Select(g => new DataIssueCount(g.Key, g.First().Title, g.First().Severity, g.Count()))
            .OrderByDescending(c => c.Severity).ThenByDescending(c => c.Count)
            .ToList();

        var filtered = string.IsNullOrWhiteSpace(issue) ? scored : scored.Where(s => s.Result.Issues.Any(i => i.Code == issue)).ToList();
        var worst = filtered.Where(s => s.Result.Issues.Count > 0)
            .OrderBy(s => s.Result.Score).ThenByDescending(s => s.IsActive)
            .Take(MaxRows).ToList();

        // عکس مشکوک: نشانهٔ آدم/واترمارک/صحنه (هر نشانهٔ ۴ به بالا) یا امتیاز کمتر از حد قبول.
        var badImages = await context.ProductImages
            .Where(i => i.SubjectFlags >= 4 || i.SubjectScore < DataConfidence.MinImageScore)
            .OrderBy(i => i.SubjectScore)
            .Take(MaxRows)
            .Select(i => new { i.ProductId, ProductName = i.Product.Name, i.ImageName, i.SubjectScore, i.SubjectFlags })
            .ToListAsync(cancellationToken);
        var unchecked_ = await context.ProductImages.CountAsync(i => i.SubjectScore == null, cancellationToken);

        return new DataHealthReport(
            scored.Count,
            scored.Count == 0 ? 100 : (int)Math.Round(scored.Average(s => s.Result.Score)),
            scored.Count(s => s.Result.Score >= DataConfidence.Healthy),
            scored.Count(s => s.Result.Score is >= DataConfidence.Weak and < DataConfidence.Healthy),
            scored.Count(s => s.Result.Score < DataConfidence.Weak),
            counts,
            worst,
            badImages.Select(i => new DataImageRow(i.ProductId, i.ProductName, i.ImageName, i.SubjectScore, i.SubjectFlags ?? (byte)0)).ToList(),
            unchecked_);
    }

    public async Task<int> FixAsync(CancellationToken cancellationToken = default)
    {
        var fixedIds = new HashSet<Guid>();

        // ۱) نام انگلیسی نامعتبر (حرف فارسی یا کمتر از ۳ نویسه) ← پاک؛ بررسی در حافظه چون SQL بازهٔ حروف فارسی را راحت نمی‌فهمد.
        var englishRows = await context.Products
            .Where(p => !p.IsActive && p.PublishedAt == null && p.EnglishName != null && p.EnglishName != "")
            .Select(p => new { p.Id, p.EnglishName })
            .Take(5000)
            .ToListAsync(cancellationToken);
        var badEnglish = englishRows.Where(r => !ProductNaming.IsUsableEnglish(r.EnglishName)).Select(r => r.Id).ToList();
        if (badEnglish.Count > 0)
        {
            await context.Products.Where(p => badEnglish.Contains(p.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.EnglishName, (string?)null)
                    .SetProperty(p => p.SmartReadyAttempts, 0).SetProperty(p => p.SmartReadyCheckedAt, (DateTimeOffset?)null), cancellationToken);
            fixedIds.UnionWith(badEnglish);
        }

        // ۲) تخفیف صفر/منفی یا بیشتر از قیمت اصلی ← برداشته شود.
        var badDiscount = await context.Products
            .Where(p => !p.IsActive && p.PublishedAt == null && p.DiscountPrice != null && (p.DiscountPrice <= 0 || p.DiscountPrice >= p.Price))
            .Select(p => p.Id).Take(5000).ToListAsync(cancellationToken);
        if (badDiscount.Count > 0)
        {
            await context.Products.Where(p => badDiscount.Contains(p.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.DiscountPrice, (decimal?)null), cancellationToken);
            fixedIds.UnionWith(badDiscount);
        }
        return fixedIds.Count;
    }

    public async Task<int> RecheckImagesAsync(Guid? productId, CancellationToken cancellationToken = default)
    {
        var query = context.ProductImages.Where(i => i.SubjectScore != null);
        if (productId is { } id)
            query = query.Where(i => i.ProductId == id);
        return await query.ExecuteUpdateAsync(s => s.SetProperty(i => i.SubjectScore, (byte?)null).SetProperty(i => i.SubjectFlags, (byte?)null), cancellationToken);
    }

    public async Task<OperationResult> ReprepareAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var row = await context.Products.Where(p => p.Id == productId).Select(p => new { p.IsActive, p.PublishedAt }).FirstOrDefaultAsync(cancellationToken);
        if (row is null)
            return OperationResult.NotFound();
        if (row.IsActive || row.PublishedAt != null)
            return OperationResult.Error("فقط پیش‌نویس منتشرنشده دوباره آماده‌سازی می‌شود؛ کالای منتشرشده را از صفحهٔ ویرایش اصلاح کنید.");
        await context.Products.Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.SmartReadyAttempts, 0).SetProperty(p => p.SmartReadyCheckedAt, (DateTimeOffset?)null), cancellationToken);
        return OperationResult.Success("کالا در صف آماده‌سازی دوباره قرار گرفت (حداکثر ۱۰ دقیقه).");
    }
}
