using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Assistant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Assistant;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۵ – «آموزش دستیار» (لایهٔ ۷)
//  کارکنان در پنل /admin/smart/assistant/teach پرسش‌هایی را که دستیار جواب نداده یا مشتری برایشان 👎 زده می‌بینند
//  و برای هر کدام یک جواب ثابت (متن + لینک) یا یک «کار» (مثلاً پیگیری سفارش) تعیین می‌کنند.
//  از آن به بعد پیام‌های خیلی شبیه (شباهت دوطرفه ≥ ۰٫۷۵) همان جواب را می‌گیرند. آموزش‌های «کار‌دار» در آموزش شبانهٔ مدل نیت هم استفاده می‌شوند.
//  حافظهٔ موقت ۵ دقیقه‌ای؛ هر تغییر از پنل حافظه را پاک می‌کند.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>آموزش منطبق بر یک پیام. Score = شباهت (۰٫۷۵ تا ۱).</summary>
public sealed record AssistantTaught(Guid Id, string Question, string? Answer, string? Url, AssistantIntent? Intent, double Score);

/// <summary>یک ردیف پنل آموزش.</summary>
public sealed record AssistantTeachingRow(Guid Id, string Question, string? Answer, string? Url, AssistantIntent? Intent, bool IsActive, int HitCount, DateTimeOffset? LastHitAt, string? CreatedByName, DateTimeOffset CreatedAt);

/// <summary>ورودی ساخت/ویرایش آموزش از پنل.</summary>
public sealed record AssistantTeachingInput(string? Question, string? Answer, string? Url, AssistantIntent? Intent, bool IsActive = true);

/// <summary>
/// پیشنهاد برای آموزش: پیامی که چند بار بی‌جواب ماند یا 👎 گرفت. مثال: («ارسال به کیش دارید»، ۴ بار، ۱ 👎، آخرین بار امروز).
/// </summary>
public sealed record AssistantTeachCandidate(string Text, int Count, int Unhelpful, DateTimeOffset Last);

/// <summary>مدیریت و تطبیق آموزش‌های دستیار.</summary>
public interface IAssistantTeachingService
{
    /// <summary>آموزش منطبق با پیام (یا null). شمارندهٔ استفاده هم بالا می‌رود.</summary>
    Task<AssistantTaught?> MatchAsync(string? text, CancellationToken cancellationToken = default);

    /// <summary>همهٔ آموزش‌ها برای پنل (جدیدترها اول، حداکثر ۵۰۰).</summary>
    Task<IReadOnlyList<AssistantTeachingRow>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>پیام‌های بی‌جواب یا 👎 خورده‌ی <paramref name="days"/> روز اخیر که هنوز آموزشی ندارند (پرتکرارها اول).</summary>
    Task<IReadOnlyList<AssistantTeachCandidate>> CandidatesAsync(int days, CancellationToken cancellationToken = default);

    /// <summary>ساخت (id = null) یا ویرایش آموزش. خطاهای اعتبارسنجی با پیام فارسی.</summary>
    Task<OperationResult> SaveAsync(Guid? id, AssistantTeachingInput input, string? actorName, CancellationToken cancellationToken = default);

    /// <summary>حذف (نرم) یک آموزش.</summary>
    Task<OperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>نمونه‌های آموزش مدل نیت: آموزش‌های فعالی که «کار» دارند. مثال: («سفارشم نیومد هنوز»، Track).</summary>
    Task<IReadOnlyList<(string Text, AssistantIntent Intent)>> ExamplesAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class AssistantTeachingService(FratakShopDbContext context, IMemoryCache cache, ILogger<AssistantTeachingService> logger) : IAssistantTeachingService
{
    /// <summary>کلید حافظهٔ موقت آموزش‌های فعال.</summary>
    public const string CacheKey = "assistant:teachings";

    /// <summary>کمترین شباهت دوطرفه برای استفاده از یک آموزش.</summary>
    public const double MinScore = 0.75;

    /// <summary>کارهایی که می‌شود پیام را به آن‌ها فرستاد (همه بی‌خطر؛ افزودن/حذف سبد و لغو بدون تأیید هرگز).</summary>
    public static readonly IReadOnlySet<AssistantIntent> Teachable = new HashSet<AssistantIntent>
    {
        AssistantIntent.Track, AssistantIntent.CartShow, AssistantIntent.Checkout, AssistantIntent.Offers, AssistantIntent.Login, AssistantIntent.Finder,
        AssistantIntent.Help, AssistantIntent.Human, AssistantIntent.Address, AssistantIntent.OrderCancel, AssistantIntent.OrderReturn, AssistantIntent.Invoice,
    };

    private sealed record Cached(Guid Id, string Question, string? Answer, string? Url, AssistantIntent? Intent);

    /// <summary>
    /// شباهت دوطرفه: کمینهٔ شباهت پیام به پرسش و پرسش به پیام (تا یک پیام بلند که فقط چند واژهٔ پرسش را دارد منطبق نشود).
    /// مثال: («ارسال به کیش دارید؟»، «به کیش ارسال دارید») ← ۱؛ («کیش»، «ارسال به کیش دارید») ← کم.
    /// </summary>
    public static double Score(string? message, string? question) =>
        Math.Min(AssistantNlu.Similarity(message, question), AssistantNlu.Similarity(question, message));

    /// <inheritdoc />
    public async Task<AssistantTaught?> MatchAsync(string? text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || AssistantNlu.Words(text).Count == 0)
            return null;
        var items = await ActiveAsync(cancellationToken);
        var best = items
            .Select(t => (Item: t, Score: Score(text, t.Question)))
            .Where(x => x.Score >= MinScore)
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();
        if (best.Item is null)
            return null;
        try
        {
            var now = DateTimeOffset.UtcNow;
            await context.AssistantTeachings.Where(t => t.Id == best.Item.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.HitCount, t => t.HitCount + 1).SetProperty(t => t.LastHitAt, now), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // شمارنده مهم نیست؛ خطایش نباید جواب مشتری را خراب کند.
            logger.LogDebug(exception, "Assistant teaching hit counter failed");
        }
        return new AssistantTaught(best.Item.Id, best.Item.Question, best.Item.Answer, best.Item.Url, best.Item.Intent, Math.Round(best.Score, 3));
    }

    private async Task<List<Cached>> ActiveAsync(CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            entry.Size = 1;
            return await context.AssistantTeachings.AsNoTracking()
                .Where(t => t.IsActive)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new Cached(t.Id, t.Question, t.Answer, t.Url, t.Intent))
                .Take(1000)
                .ToListAsync(cancellationToken);
        }) ?? [];

    /// <inheritdoc />
    public async Task<IReadOnlyList<AssistantTeachingRow>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.AssistantTeachings.AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new AssistantTeachingRow(t.Id, t.Question, t.Answer, t.Url, t.Intent, t.IsActive, t.HitCount, t.LastHitAt, t.CreatedByName, t.CreatedAt))
            .Take(500)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AssistantTeachCandidate>> CandidatesAsync(int days, CancellationToken cancellationToken = default)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days, 1, 90));
        var rows = await context.AssistantMessages.AsNoTracking()
            .Where(m => m.CreatedAt >= since && (!m.Answered || m.Helpful == false) && m.Intent != AssistantIntent.Handoff)
            .GroupBy(m => m.Text)
            .Select(g => new { Text = g.Key, Count = g.Count(), Unhelpful = g.Count(m => m.Helpful == false), Last = g.Max(m => m.CreatedAt) })
            .OrderByDescending(g => g.Unhelpful).ThenByDescending(g => g.Count).ThenByDescending(g => g.Last)
            .Take(80)
            .ToListAsync(cancellationToken);
        var taught = await ActiveAsync(cancellationToken);
        return rows
            .Where(r => AssistantNlu.Words(r.Text).Count > 0 && !taught.Any(t => Score(r.Text, t.Question) >= MinScore))
            .Take(40)
            .Select(r => new AssistantTeachCandidate(r.Text, r.Count, r.Unhelpful, r.Last))
            .ToList();
    }

    /// <summary>
    /// اعتبارسنجی ورودی. خروجی: پیام خطا یا null. قانون‌ها: پرسش ۳ تا ۳۰۰ نویسه و دست‌کم یک واژهٔ معنادار؛ جواب حداکثر ۱۰۰۰ نویسه؛
    /// لینک فقط مسیر داخلی («/…»، نه «//» یا «\»)؛ کار فقط از فهرست مجاز؛ دست‌کم جواب یا کار.
    /// </summary>
    public static string? Validate(AssistantTeachingInput input)
    {
        var question = input.Question?.Trim() ?? "";
        if (question.Length is < 3 or > 300 || AssistantNlu.Words(question).Count == 0)
            return "پرسش باید بین ۳ تا ۳۰۰ نویسه و معنادار باشد.";
        var answer = input.Answer?.Trim();
        if (answer is { Length: > 1000 })
            return "جواب حداکثر ۱۰۰۰ نویسه است.";
        if (input.Intent is { } intent && !Teachable.Contains(intent))
            return "این کار برای آموزش مجاز نیست.";
        if (string.IsNullOrEmpty(answer) && input.Intent is null)
            return "جواب یا کار دستیار را مشخص کنید.";
        var url = input.Url?.Trim();
        if (!string.IsNullOrEmpty(url) && (!url.StartsWith('/') || url.StartsWith("//", StringComparison.Ordinal) || url.Contains('\\') || url.Length > 300 || url.Any(char.IsWhiteSpace)))
            return "لینک باید یک مسیر داخلی سایت باشد (مثلاً /faq).";
        return null;
    }

    /// <inheritdoc />
    public async Task<OperationResult> SaveAsync(Guid? id, AssistantTeachingInput input, string? actorName, CancellationToken cancellationToken = default)
    {
        if (Validate(input) is { } error)
            return OperationResult.Error(error);
        AssistantTeaching? row;
        if (id is { } existing)
        {
            row = await context.AssistantTeachings.AsTracking().FirstOrDefaultAsync(t => t.Id == existing, cancellationToken);
            if (row is null)
                return OperationResult.NotFound("آموزش پیدا نشد.");
        }
        else
        {
            row = new AssistantTeaching { Id = Guid.NewGuid(), CreatedByName = actorName?.Trim() is { Length: > 0 } name ? (name.Length > 100 ? name[..100] : name) : null };
            context.AssistantTeachings.Add(row);
        }
        row.Question = input.Question!.Trim();
        row.Answer = string.IsNullOrWhiteSpace(input.Answer) ? null : input.Answer.Trim();
        row.Url = string.IsNullOrWhiteSpace(input.Url) ? null : input.Url.Trim();
        row.Intent = input.Intent;
        row.IsActive = input.IsActive;
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        cache.Remove(CacheKey);
        return OperationResult.Success(id is null ? "آموزش ثبت شد؛ از همین حالا دستیار این جواب را می‌دهد." : "آموزش ویرایش شد.");
    }

    /// <inheritdoc />
    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var count = await context.AssistantTeachings.Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDeleted, true).SetProperty(t => t.DeletedAt, now).SetProperty(t => t.IsActive, false), cancellationToken);
        cache.Remove(CacheKey);
        return count == 0 ? OperationResult.NotFound("آموزش پیدا نشد.") : OperationResult.Success("آموزش حذف شد.");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(string Text, AssistantIntent Intent)>> ExamplesAsync(CancellationToken cancellationToken = default) =>
        (await ActiveAsync(cancellationToken)).Where(t => t.Intent is not null).Select(t => (t.Question, t.Intent!.Value)).ToList();
}
