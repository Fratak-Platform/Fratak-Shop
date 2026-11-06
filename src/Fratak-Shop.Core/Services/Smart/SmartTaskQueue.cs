using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>اولویت‌های صف (B21): کار دستی ادمین جلوتر از کار گروهی، و کار گروهی جلوتر از کار شبانه.</summary>
public static class SmartPriority
{
    public const int Manual = 10;
    public const int Batch = 5;
    public const int Nightly = 0;
}

/// <summary>یک ردیف صف برای نمایش در پنل.</summary>
public sealed record SmartTaskRow(
    Guid Id, SmartTaskKind Kind, SmartTarget TargetKind, Guid? TargetId, string? TargetTitle, SmartTaskStatus Status, int Priority,
    string? Summary, string? Error, int Attempts, string? RequestedByName, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt);

/// <summary>شمارش وضعیت‌ها برای کارت‌های بالای صفحهٔ صف.</summary>
public sealed record SmartQueueStats(int Queued, int Running, int NeedsReview, int Failed, int AppliedToday);

/// <summary>
/// کار برداشته‌شده برای اجرا. RequestedById/CreatedAt (دور بیستم) برای اعلان نتیجه: درخواست‌دهنده و زمان ساخت (برای پیدا کردن «دستهٔ» کارهای گروهی).
/// </summary>
public sealed record ClaimedSmartTask(Guid Id, SmartTaskKind Kind, SmartTarget TargetKind, Guid? TargetId, string? TargetTitle, string? PayloadJson, int Attempts, int Priority,
    Guid? RequestedById = null, DateTimeOffset CreatedAt = default);

/// <summary>
/// خلاصهٔ یک «دسته» کار گروهی (دور بیستم): همهٔ کارهای یک درخواست‌دهنده با یک اولویت که در فاصلهٔ ±۱۰ دقیقه ساخته شده‌اند.
/// مثال: «تکمیل همهٔ ناقص‌ها» برای ۲۰ محصول ← Total=20، Applied=14، NeedsReview=5، Failed=1.
/// </summary>
public sealed record SmartBatchSummary(int Total, int Applied, int NeedsReview, int Failed, int Skipped, int Pending);

/// <summary>
/// صف کارهای هوشمند (B20–B21) روی جدول SmartTasks.
/// <para>چرا صف؟ کارهای هوشمند (جست‌وجوی عکس، خواندن مشخصات، تحلیل نظرها) چند ثانیه تا یک دقیقه طول می‌کشند؛ اگر داخل
/// درخواست ادمین اجرا شوند روی هاست ضعیف صف درخواست‌ها پر و سایت کند می‌شود. دکمهٔ ادمین فقط کار را در صف می‌گذارد و
/// کارگر پس‌زمینه (SmartWorker) یکی‌یکی و با اولویت اجرا می‌کند.</para>
/// <para>ایمنی: دو کار یکسان (همان نوع و همان هدف) هم‌زمان در صف نمی‌ماند؛ برداشتن کار اتمی است (دو نمونهٔ برنامه یک کار را
/// برنمی‌دارند)؛ کاری که کارگرش وسط کار مرده بعد از پایان مهلت دوباره برداشته می‌شود (حداکثر ۳ بار).</para>
/// </summary>
public interface ISmartTaskQueue
{
    /// <summary>
    /// افزودن کار. اگر همین کار (نوع + هدف) در صف یا در حال اجراست، همان برگردانده می‌شود و کار تکراری ساخته نمی‌شود.
    /// مثال: EnqueueAsync(ProductComplete, Product, id, "هدفون X", SmartPriority.Manual).
    /// </summary>
    Task<Guid> EnqueueAsync(SmartTaskKind kind, SmartTarget target, Guid? targetId, string? title, int priority,
        string? payloadJson = null, Guid? userId = null, string? userName = null, CancellationToken cancellationToken = default);

    /// <summary>برداشتن اتمی پراولویت‌ترین کار آماده (یا null). مهلت اجرا <paramref name="lease"/>.</summary>
    Task<ClaimedSmartTask?> ClaimNextAsync(string workerId, TimeSpan lease, CancellationToken cancellationToken = default);

    /// <summary>ثبت نتیجه. وضعیت باید یکی از Applied/NeedsReview/Failed/Skipped باشد.</summary>
    Task CompleteAsync(Guid id, SmartTaskStatus status, string? summary, string? resultJson = null, string? error = null, CancellationToken cancellationToken = default);

    /// <summary>کار ناموفق موقت (مثلاً منبع در دسترس نبود): دوباره در صف با تأخیر.</summary>
    Task RetryLaterAsync(Guid id, TimeSpan delay, string? error, CancellationToken cancellationToken = default);

    Task<SmartTaskRow?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<string?> GetResultJsonAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>آخرین نتیجهٔ یک نوع کار برای یک هدف (برای استودیوی محصول).</summary>
    Task<(SmartTaskRow Row, string? ResultJson)?> LatestForAsync(SmartTaskKind kind, SmartTarget target, Guid targetId, CancellationToken cancellationToken = default);

    Task<List<SmartTaskRow>> ListAsync(SmartTaskStatus? status, int take = 100, CancellationToken cancellationToken = default);

    Task<List<SmartTaskRow>> ForTargetAsync(SmartTarget target, Guid targetId, int take = 20, CancellationToken cancellationToken = default);

    Task<SmartQueueStats> StatsAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> CancelAsync(Guid id, CancellationToken cancellationToken = default);

    Task<OperationResult> RetryAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>حذف واقعی کارهای تمام‌شدهٔ قدیمی‌تر از ۳۰ روز (جدول کوچک بماند).</summary>
    Task<int> PurgeAsync(CancellationToken cancellationToken = default);

    /// <summary>بستن «نیاز به بررسی» پس از تصمیم ادمین (اعمال/رد پیشنهادها).</summary>
    Task MarkReviewedAsync(Guid id, string summary, CancellationToken cancellationToken = default);

    /// <summary>
    /// دور بیستم: تعداد کارهای هنوز تمام‌نشده (در صف/در حال اجرا) همین درخواست‌دهنده با همین اولویت.
    /// ۰ یعنی «دستهٔ» کار گروهی تمام شد و وقت فرستادن یک اعلان خلاصه است. محل استفاده: SmartNotifier.
    /// </summary>
    Task<int> PendingCountAsync(Guid? requestedById, int priority, CancellationToken cancellationToken = default);

    /// <summary>دور بیستم: خلاصهٔ دستهٔ کارهای گروهی (همان درخواست‌دهنده و اولویت، ساخته‌شده در ±۱۰ دقیقهٔ <paramref name="around"/>).</summary>
    Task<SmartBatchSummary> BatchSummaryAsync(Guid? requestedById, int priority, DateTimeOffset around, CancellationToken cancellationToken = default);

    /// <summary>
    /// دور بیستم: هدف‌هایی که از <paramref name="since"/> به بعد کاری از این نوع داشته‌اند (برای «تکمیل خودکار شبانه»: سقف روزانه و
    /// تکرار نکردن محصولی که در ۳۰ روز گذشته تکمیل شده). priority=null یعنی همهٔ اولویت‌ها.
    /// </summary>
    Task<List<Guid>> RecentTargetsAsync(SmartTaskKind kind, DateTimeOffset since, int? priority = null, CancellationToken cancellationToken = default);
}

public sealed class SmartTaskQueue(FratakShopDbContext context) : ISmartTaskQueue
{
    public const int MaxAttempts = 3;

    public async Task<Guid> EnqueueAsync(SmartTaskKind kind, SmartTarget target, Guid? targetId, string? title, int priority,
        string? payloadJson = null, Guid? userId = null, string? userName = null, CancellationToken cancellationToken = default)
    {
        var existing = await context.SmartTasks
            .Where(t => t.Kind == kind && t.TargetKind == target && t.TargetId == targetId
                        && (t.Status == SmartTaskStatus.Queued || t.Status == SmartTaskStatus.Running))
            .Select(t => new { t.Id, t.Priority, t.Status })
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            // کار تکراری ساخته نمی‌شود؛ ولی اگر ادمین دستی خواسته، اولویت بالا می‌رود.
            if (existing.Status == SmartTaskStatus.Queued && priority > existing.Priority)
            {
                await context.SmartTasks.Where(t => t.Id == existing.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.Priority, priority), cancellationToken);
            }
            return existing.Id;
        }

        var task = new SmartTask
        {
            Kind = kind,
            TargetKind = target,
            TargetId = targetId,
            TargetTitle = Limit(title, 250),
            Status = SmartTaskStatus.Queued,
            Priority = priority,
            PayloadJson = payloadJson,
            RequestedById = userId,
            RequestedByName = Limit(userName, 150),
            RunAfter = DateTimeOffset.UtcNow,
        };
        context.SmartTasks.Add(task);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return task.Id;
    }

    public async Task<ClaimedSmartTask?> ClaimNextAsync(string workerId, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        // کارهای «در حال اجرا» که مهلتشان گذشته (کارگر مرده/برنامه ری‌استارت شده) به صف برمی‌گردند یا شکست می‌خورند.
        await context.SmartTasks
            .Where(t => t.Status == SmartTaskStatus.Running && t.LeaseUntil < now && t.Attempts >= MaxAttempts)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, SmartTaskStatus.Failed)
                .SetProperty(t => t.Error, "اجرای کار چند بار نیمه‌کاره ماند (احتمالاً ری‌استارت سرور).")
                .SetProperty(t => t.FinishedAt, now)
                .SetProperty(t => t.UpdatedAt, now), cancellationToken);
        await context.SmartTasks
            .Where(t => t.Status == SmartTaskStatus.Running && t.LeaseUntil < now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, SmartTaskStatus.Queued)
                .SetProperty(t => t.UpdatedAt, now), cancellationToken);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var candidate = await context.SmartTasks
                .Where(t => t.Status == SmartTaskStatus.Queued && t.RunAfter <= now)
                .OrderByDescending(t => t.Priority).ThenBy(t => t.CreatedAt)
                .Select(t => new ClaimedSmartTask(t.Id, t.Kind, t.TargetKind, t.TargetId, t.TargetTitle, t.PayloadJson, t.Attempts, t.Priority,
                    t.RequestedById, t.CreatedAt))
                .FirstOrDefaultAsync(cancellationToken);
            if (candidate is null)
                return null;

            var leaseUntil = now + lease;
            var worker = Limit(workerId, 100);
            var claimed = await context.SmartTasks
                .Where(t => t.Id == candidate.Id && t.Status == SmartTaskStatus.Queued)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, SmartTaskStatus.Running)
                    .SetProperty(t => t.StartedAt, now)
                    .SetProperty(t => t.LeaseUntil, leaseUntil)
                    .SetProperty(t => t.ClaimedBy, worker)
                    .SetProperty(t => t.Attempts, t => t.Attempts + 1)
                    .SetProperty(t => t.UpdatedAt, now), cancellationToken);
            if (claimed == 1)
                return candidate with { Attempts = candidate.Attempts + 1 };
        }
        return null;
    }

    public async Task CompleteAsync(Guid id, SmartTaskStatus status, string? summary, string? resultJson = null, string? error = null, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cleanSummary = Limit(summary, 1000);
        var cleanError = Limit(error, 1000);
        await context.SmartTasks.Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, status)
                .SetProperty(t => t.Summary, cleanSummary)
                .SetProperty(t => t.ResultJson, resultJson)
                .SetProperty(t => t.Error, cleanError)
                .SetProperty(t => t.FinishedAt, now)
                .SetProperty(t => t.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(t => t.UpdatedAt, now), cancellationToken);
    }

    public async Task RetryLaterAsync(Guid id, TimeSpan delay, string? error, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var runAfter = now + delay;
        var cleanError = Limit(error, 1000);
        await context.SmartTasks.Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, t => t.Attempts >= MaxAttempts ? SmartTaskStatus.Failed : SmartTaskStatus.Queued)
                .SetProperty(t => t.RunAfter, runAfter)
                .SetProperty(t => t.Error, cleanError)
                .SetProperty(t => t.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(t => t.UpdatedAt, now), cancellationToken);
    }

    public Task<SmartTaskRow?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Rows(context.SmartTasks.Where(t => t.Id == id)).FirstOrDefaultAsync(cancellationToken);

    public Task<string?> GetResultJsonAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.SmartTasks.Where(t => t.Id == id).Select(t => t.ResultJson).FirstOrDefaultAsync(cancellationToken);

    public async Task<(SmartTaskRow Row, string? ResultJson)?> LatestForAsync(SmartTaskKind kind, SmartTarget target, Guid targetId, CancellationToken cancellationToken = default)
    {
        var id = await context.SmartTasks
            .Where(t => t.Kind == kind && t.TargetKind == target && t.TargetId == targetId)
            .OrderByDescending(t => t.CreatedAt).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(cancellationToken);
        if (id is null)
            return null;
        var row = await GetAsync(id.Value, cancellationToken);
        return row is null ? null : (row, await GetResultJsonAsync(id.Value, cancellationToken));
    }

    public Task<List<SmartTaskRow>> ListAsync(SmartTaskStatus? status, int take = 100, CancellationToken cancellationToken = default)
    {
        var query = context.SmartTasks.AsQueryable();
        if (status is { } s)
            query = query.Where(t => t.Status == s);
        return Rows(query.OrderByDescending(t => t.CreatedAt).Take(Math.Clamp(take, 1, 500))).ToListAsync(cancellationToken);
    }

    public Task<List<SmartTaskRow>> ForTargetAsync(SmartTarget target, Guid targetId, int take = 20, CancellationToken cancellationToken = default) =>
        Rows(context.SmartTasks.Where(t => t.TargetKind == target && t.TargetId == targetId)
            .OrderByDescending(t => t.CreatedAt).Take(Math.Clamp(take, 1, 100))).ToListAsync(cancellationToken);

    public async Task<SmartQueueStats> StatsAsync(CancellationToken cancellationToken = default)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-1);
        var counts = await context.SmartTasks
            .Where(t => t.Status == SmartTaskStatus.Queued || t.Status == SmartTaskStatus.Running || t.Status == SmartTaskStatus.NeedsReview
                        || t.Status == SmartTaskStatus.Failed && t.FinishedAt >= since || t.Status == SmartTaskStatus.Applied && t.FinishedAt >= since)
            .GroupBy(t => t.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        int Count(SmartTaskStatus s) => counts.FirstOrDefault(c => c.Key == s)?.Count ?? 0;
        return new SmartQueueStats(Count(SmartTaskStatus.Queued), Count(SmartTaskStatus.Running), Count(SmartTaskStatus.NeedsReview),
            Count(SmartTaskStatus.Failed), Count(SmartTaskStatus.Applied));
    }

    public async Task<OperationResult> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var changed = await context.SmartTasks
            .Where(t => t.Id == id && (t.Status == SmartTaskStatus.Queued || t.Status == SmartTaskStatus.NeedsReview))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, SmartTaskStatus.Cancelled)
                .SetProperty(t => t.FinishedAt, now)
                .SetProperty(t => t.UpdatedAt, now), cancellationToken);
        return changed == 1 ? OperationResult.Success("کار لغو شد.") : OperationResult.Error("این کار قابل لغو نیست (در حال اجرا یا تمام‌شده).");
    }

    public async Task<OperationResult> RetryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var changed = await context.SmartTasks
            .Where(t => t.Id == id && (t.Status == SmartTaskStatus.Failed || t.Status == SmartTaskStatus.Cancelled || t.Status == SmartTaskStatus.Skipped))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, SmartTaskStatus.Queued)
                .SetProperty(t => t.Attempts, 0)
                .SetProperty(t => t.Error, (string?)null)
                .SetProperty(t => t.RunAfter, now)
                .SetProperty(t => t.Priority, SmartPriority.Manual)
                .SetProperty(t => t.UpdatedAt, now), cancellationToken);
        return changed == 1 ? OperationResult.Success("کار دوباره در صف قرار گرفت.") : OperationResult.Error("فقط کارهای ناموفق/لغوشده دوباره اجرا می‌شوند.");
    }

    public Task<int> PurgeAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        return context.SmartTasks.IgnoreQueryFilters()
            .Where(t => t.IsDeleted || (t.Status == SmartTaskStatus.Applied || t.Status == SmartTaskStatus.Failed || t.Status == SmartTaskStatus.Cancelled
                                        || t.Status == SmartTaskStatus.Skipped) && t.FinishedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task MarkReviewedAsync(Guid id, string summary, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var clean = Limit(summary, 1000);
        await context.SmartTasks.Where(t => t.Id == id && t.Status == SmartTaskStatus.NeedsReview)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, SmartTaskStatus.Applied)
                .SetProperty(t => t.Summary, clean)
                .SetProperty(t => t.UpdatedAt, now), cancellationToken);
    }

    public Task<int> PendingCountAsync(Guid? requestedById, int priority, CancellationToken cancellationToken = default) =>
        context.SmartTasks.CountAsync(t => t.RequestedById == requestedById && t.Priority == priority
                                           && (t.Status == SmartTaskStatus.Queued || t.Status == SmartTaskStatus.Running), cancellationToken);

    public async Task<SmartBatchSummary> BatchSummaryAsync(Guid? requestedById, int priority, DateTimeOffset around, CancellationToken cancellationToken = default)
    {
        var from = around.AddMinutes(-10);
        var to = around.AddMinutes(10);
        var counts = await context.SmartTasks
            .Where(t => t.RequestedById == requestedById && t.Priority == priority && t.CreatedAt >= from && t.CreatedAt <= to)
            .GroupBy(t => t.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        int Count(params SmartTaskStatus[] statuses) => counts.Where(c => statuses.Contains(c.Key)).Sum(c => c.Count);
        return new SmartBatchSummary(counts.Sum(c => c.Count), Count(SmartTaskStatus.Applied), Count(SmartTaskStatus.NeedsReview),
            Count(SmartTaskStatus.Failed), Count(SmartTaskStatus.Skipped, SmartTaskStatus.Cancelled), Count(SmartTaskStatus.Queued, SmartTaskStatus.Running));
    }

    public Task<List<Guid>> RecentTargetsAsync(SmartTaskKind kind, DateTimeOffset since, int? priority = null, CancellationToken cancellationToken = default)
    {
        var query = context.SmartTasks.Where(t => t.Kind == kind && t.CreatedAt >= since && t.TargetId != null);
        if (priority is { } p)
            query = query.Where(t => t.Priority == p);
        return query.Select(t => t.TargetId!.Value).Distinct().ToListAsync(cancellationToken);
    }

    private static IQueryable<SmartTaskRow> Rows(IQueryable<SmartTask> query) =>
        query.Select(t => new SmartTaskRow(t.Id, t.Kind, t.TargetKind, t.TargetId, t.TargetTitle, t.Status, t.Priority, t.Summary, t.Error,
            t.Attempts, t.RequestedByName, t.CreatedAt, t.StartedAt, t.FinishedAt));

    internal static string? Limit(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var trimmed = text.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
