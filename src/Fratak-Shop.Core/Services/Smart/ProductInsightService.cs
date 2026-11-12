using System.Text.Json;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// «خلاصهٔ خرید» برای نمایش (صفحهٔ محصول و استودیوی هوشمند).
/// </summary>
public sealed class ProductInsightView
{
    public Guid ProductId { get; init; }
    public ProductInsightStatus Status { get; init; }
    public string? Summary { get; init; }
    public List<string> ForWhom { get; init; } = [];
    public List<string> NotFor { get; init; } = [];
    public List<InsightPoint> Pros { get; init; } = [];
    public List<InsightPoint> Cons { get; init; } = [];
    public string? Verdict { get; init; }
    public List<InsightFaq> Faq { get; init; } = [];
    public List<string> Highlights { get; init; } = [];
    public string? ShortDescription { get; init; }
    public List<string> Sources { get; init; } = [];
    public int ReviewCount { get; init; }
    public int? RatingTenths { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public string? ApprovedByName { get; init; }

    /// <summary>امتیاز اعشاری (۴۳ ← ۴٫۳).</summary>
    public double? Rating => RatingTenths / 10.0;

    public bool HasContent => Pros.Count + Cons.Count + Highlights.Count + ForWhom.Count > 0 || !string.IsNullOrWhiteSpace(Summary);
}

/// <summary>ویرایش دستی خلاصهٔ خرید در استودیو (هر خط یک مورد).</summary>
public sealed class ProductInsightEdit
{
    public string? Summary { get; set; }
    public string? ForWhom { get; set; }
    public string? NotFor { get; set; }
    public string? Pros { get; set; }
    public string? Cons { get; set; }
    public string? Verdict { get; set; }
}

/// <summary>
/// ذخیره، تأیید و خواندن «خلاصهٔ خرید» محصول (B12) در جدول ProductInsights.
/// <para>قانون ایمنی: فقط خلاصهٔ «تأییدشده» در سایت دیده می‌شود. تأیید خودکار فقط وقتی است که داده کافی باشد
/// (SmartOptions.AutoApproveInsightMinReviews)؛ وگرنه پیش‌نویس منتظر یک کلیک کارشناس می‌ماند.
/// خلاصه‌ای که کارشناس تأیید یا ویرایش کرده، با تولید خودکار بعدی بازنویسی نمی‌شود.</para>
/// <para>محل استفاده: SmartOrchestrator، استودیوی محصول (پنل) و BuyerGuideService (صفحهٔ محصول).</para>
/// </summary>
public interface IProductInsightService
{
    Task<ProductInsightView?> GetAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>فقط تأییدشده (برای سایت).</summary>
    Task<ProductInsightView?> GetApprovedAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ذخیرهٔ پیش‌نویس تولیدشده. اگر خلاصهٔ تأییدشدهٔ دستی وجود دارد و approve=false، چیزی عوض نمی‌شود (خروجی false).
    /// </summary>
    Task<bool> SaveDraftAsync(Guid productId, InsightDraft draft, bool approve, string? byName, CancellationToken cancellationToken = default);

    Task<OperationResult> ApproveAsync(Guid productId, string? byName, CancellationToken cancellationToken = default);

    Task<OperationResult> RejectAsync(Guid productId, string? byName, CancellationToken cancellationToken = default);

    Task<OperationResult> UpdateAsync(Guid productId, ProductInsightEdit edit, string? byName, CancellationToken cancellationToken = default);

    /// <summary>پیش‌نویس‌های منتظر تأیید (برای صفحهٔ صف).</summary>
    Task<List<(Guid ProductId, string? ProductName, DateTimeOffset GeneratedAt, int ReviewCount)>> PendingAsync(int take = 50, CancellationToken cancellationToken = default);
}

public sealed class ProductInsightService(FratakShopDbContext context) : IProductInsightService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ProductInsightView?> GetAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var row = await context.ProductInsights.FirstOrDefaultAsync(i => i.ProductId == productId, cancellationToken);
        return row is null ? null : ToView(row);
    }

    public async Task<ProductInsightView?> GetApprovedAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var row = await context.ProductInsights
            .FirstOrDefaultAsync(i => i.ProductId == productId && i.Status == ProductInsightStatus.Approved, cancellationToken);
        return row is null ? null : ToView(row);
    }

    public async Task<bool> SaveDraftAsync(Guid productId, InsightDraft draft, bool approve, string? byName, CancellationToken cancellationToken = default)
    {
        var row = await context.ProductInsights.AsTracking().FirstOrDefaultAsync(i => i.ProductId == productId, cancellationToken);
        if (row is not null && row.Status == ProductInsightStatus.Approved && !string.IsNullOrEmpty(row.ApprovedByName))
            return false;

        if (row is null)
        {
            row = new ProductInsight { ProductId = productId };
            context.ProductInsights.Add(row);
        }
        row.Summary = Limit(draft.Summary, 1500);
        row.ForWhomJson = Serialize(draft.ForWhom);
        row.NotForJson = Serialize(draft.NotFor);
        row.ProsJson = Serialize(draft.Pros);
        row.ConsJson = Serialize(draft.Cons);
        row.Verdict = Limit(draft.Verdict, 1000);
        row.FaqJson = Serialize(draft.Faq);
        row.HighlightsJson = Serialize(draft.Highlights);
        row.ShortDescription = Limit(draft.ShortDescription, 1000);
        row.SourcesJson = Serialize(draft.Sources);
        row.ReviewCount = draft.ReviewCount;
        row.RatingTenths = draft.RatingTenths;
        row.GeneratedAt = DateTimeOffset.UtcNow;
        if (approve)
        {
            row.Status = ProductInsightStatus.Approved;
            row.ApprovedAt = DateTimeOffset.UtcNow;
            row.ApprovedByName = null; // تأیید خودکار؛ نام خالی یعنی «سیستم»
        }
        else if (row.Status != ProductInsightStatus.Approved)
        {
            row.Status = ProductInsightStatus.Draft;
        }
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return true;
    }

    public Task<OperationResult> ApproveAsync(Guid productId, string? byName, CancellationToken cancellationToken = default) =>
        SetStatusAsync(productId, ProductInsightStatus.Approved, byName, "خلاصهٔ خرید تأیید شد و در صفحهٔ محصول نمایش داده می‌شود.", cancellationToken);

    public Task<OperationResult> RejectAsync(Guid productId, string? byName, CancellationToken cancellationToken = default) =>
        SetStatusAsync(productId, ProductInsightStatus.Rejected, byName, "خلاصهٔ خرید رد شد و نمایش داده نمی‌شود.", cancellationToken);

    private async Task<OperationResult> SetStatusAsync(Guid productId, ProductInsightStatus status, string? byName, string message, CancellationToken cancellationToken)
    {
        var row = await context.ProductInsights.AsTracking().FirstOrDefaultAsync(i => i.ProductId == productId, cancellationToken);
        if (row is null)
            return OperationResult.NotFound("خلاصه‌ای برای این محصول ساخته نشده است.");
        row.Status = status;
        row.ApprovedAt = status == ProductInsightStatus.Approved ? DateTimeOffset.UtcNow : null;
        row.ApprovedByName = Limit(byName, 150) ?? "کارشناس";
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return OperationResult.Success(message);
    }

    public async Task<OperationResult> UpdateAsync(Guid productId, ProductInsightEdit edit, string? byName, CancellationToken cancellationToken = default)
    {
        var row = await context.ProductInsights.AsTracking().FirstOrDefaultAsync(i => i.ProductId == productId, cancellationToken);
        if (row is null)
            return OperationResult.NotFound("خلاصه‌ای برای این محصول ساخته نشده است.");
        row.Summary = Limit(XssSecurity.SanitizeText(edit.Summary ?? string.Empty), 1500);
        row.Verdict = Limit(XssSecurity.SanitizeText(edit.Verdict ?? string.Empty), 1000);
        row.ForWhomJson = Serialize(Lines(edit.ForWhom));
        row.NotForJson = Serialize(Lines(edit.NotFor));
        row.ProsJson = Serialize(Lines(edit.Pros).Select(p => new InsightPoint(p, 0)).ToList());
        row.ConsJson = Serialize(Lines(edit.Cons).Select(p => new InsightPoint(p, 0)).ToList());
        row.Status = ProductInsightStatus.Approved;
        row.ApprovedAt = DateTimeOffset.UtcNow;
        row.ApprovedByName = Limit(byName, 150) ?? "کارشناس";
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return OperationResult.Success("خلاصهٔ خرید ویرایش و تأیید شد.");
    }

    public async Task<List<(Guid ProductId, string? ProductName, DateTimeOffset GeneratedAt, int ReviewCount)>> PendingAsync(int take = 50, CancellationToken cancellationToken = default)
    {
        var rows = await context.ProductInsights
            .Where(i => i.Status == ProductInsightStatus.Draft)
            .OrderByDescending(i => i.GeneratedAt).Take(Math.Clamp(take, 1, 200))
            .Select(i => new { i.ProductId, Name = context.Products.Where(p => p.Id == i.ProductId).Select(p => p.Name).FirstOrDefault(), i.GeneratedAt, i.ReviewCount })
            .ToListAsync(cancellationToken);
        return rows.Select(r => (r.ProductId, r.Name, r.GeneratedAt, r.ReviewCount)).ToList();
    }

    /// <summary>هر خط یک مورد؛ خط خالی حذف، حداکثر ۸ مورد، هر مورد حداکثر ۱۵۰ نویسه و پاک‌سازی‌شده.</summary>
    public static List<string> Lines(string? text) =>
        (text ?? string.Empty).Split('\n')
            .Select(l => XssSecurity.SanitizeText(l.Trim()).Trim())
            .Where(l => l.Length > 0)
            .Select(l => l.Length > 150 ? l[..150] : l)
            .Distinct(StringComparer.Ordinal).Take(8).ToList();

    public static ProductInsightView ToView(ProductInsight row) => new()
    {
        ProductId = row.ProductId,
        Status = row.Status,
        Summary = row.Summary,
        ForWhom = Read<List<string>>(row.ForWhomJson) ?? [],
        NotFor = Read<List<string>>(row.NotForJson) ?? [],
        Pros = Read<List<InsightPoint>>(row.ProsJson) ?? [],
        Cons = Read<List<InsightPoint>>(row.ConsJson) ?? [],
        Verdict = row.Verdict,
        Faq = Read<List<InsightFaq>>(row.FaqJson) ?? [],
        Highlights = Read<List<string>>(row.HighlightsJson) ?? [],
        ShortDescription = row.ShortDescription,
        Sources = Read<List<string>>(row.SourcesJson) ?? [],
        ReviewCount = row.ReviewCount,
        RatingTenths = row.RatingTenths,
        GeneratedAt = row.GeneratedAt,
        ApprovedAt = row.ApprovedAt,
        ApprovedByName = row.ApprovedByName,
    };

    private static T? Read<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    private static string? Limit(string? text, int max) => SmartTaskQueue.Limit(text, max);
}
