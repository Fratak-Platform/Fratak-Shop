using Fratak_Shop.Core.Services.Learning;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Comments;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Comments;

/// <summary>
/// دور ۲۴ (پیشنهاد ۵۴): تحلیل احساس دیدگاه‌ها با مدلی که از دیدگاه‌های تأییدشدهٔ خود سایت یاد می‌گیرد.
/// <para>مدل هر ۶ ساعت یک‌بار (حداکثر ۵۰۰۰ دیدگاه اخیر) دوباره ساخته و در حافظه نگه داشته می‌شود؛ ساختش چند میلی‌ثانیه است.</para>
/// <para>محل استفاده: صفحهٔ /admin/comments (نشان «منفی» و موضوع شکایت).</para>
/// </summary>
public interface IReviewSentimentService
{
    /// <summary>تحلیل گروهی. مثال: AnalyzeAsync([(id, "خراب رسید", 1, false)]) ← {id: Negative}.</summary>
    Task<Dictionary<Guid, SentimentResult>> AnalyzeAsync(IEnumerable<(Guid Id, string? Text, byte? Rating, bool? Recommended)> reviews,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ReviewSentimentService(FratakShopDbContext context) : IReviewSentimentService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private sealed record Snapshot(NaiveBayesText? Model, DateTimeOffset BuiltAt);

    private static volatile Snapshot? _cache;

    /// <summary>عمر مدل در حافظه.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);

    /// <inheritdoc />
    public async Task<Dictionary<Guid, SentimentResult>> AnalyzeAsync(IEnumerable<(Guid Id, string? Text, byte? Rating, bool? Recommended)> reviews,
        CancellationToken cancellationToken = default)
    {
        var model = await ModelAsync(cancellationToken);
        var result = new Dictionary<Guid, SentimentResult>();
        foreach (var review in reviews)
            result[review.Id] = ReviewSentiment.Analyze(review.Text, review.Rating, review.Recommended, model);
        return result;
    }

    private async Task<NaiveBayesText?> ModelAsync(CancellationToken cancellationToken)
    {
        if (_cache is { } cached && DateTimeOffset.UtcNow - cached.BuiltAt < Lifetime)
            return cached.Model;
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache is { } again && DateTimeOffset.UtcNow - again.BuiltAt < Lifetime)
                return again.Model;
            var rows = await context.Comments.AsNoTracking()
                .Where(c => c.Type == CommentType.Review && c.Status == CommentStatus.Approved && c.Rating != null && c.ParentId == null)
                .OrderByDescending(c => c.CreatedAt)
                .Take(5000)
                .Select(c => new { c.Content, Rating = c.Rating!.Value })
                .ToListAsync(cancellationToken);
            var model = ReviewSentiment.Train(rows.Select(r => (r.Content, r.Rating)));
            _cache = new Snapshot(model, DateTimeOffset.UtcNow);
            return model;
        }
        finally
        {
            Gate.Release();
        }
    }
}
