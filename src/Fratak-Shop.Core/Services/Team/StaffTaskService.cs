using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Team;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Team;

// ═══════════════════════════════════════════════════════════════════════════
//  فهرست کارهای شخصی کارکنان (مرحلهٔ هفتم – فاز ۱۰)
//  هر کارمند در /admin/tasks کارهایش را می‌بیند؛ دکمهٔ «＋ کار» بالای هر صفحهٔ پنل، کار تازه با لینک همان صفحه می‌سازد.
//  کار را می‌شود به همکار سپرد (اعلان «کار تازه» می‌گیرد) و سر موعد یادآوری (اعلان + پوش) فرستاده می‌شود
//  (TeamWorker در Web/Infrastructure/Team هر ۵ دقیقه SendDueRemindersAsync را صدا می‌زند).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>ورودی فرم ساخت/ویرایش کار. Id خالی = کار تازه. AssigneeId خالی = خودم.</summary>
public sealed record StaffTaskInput(Guid? Id, Guid? AssigneeId, string? Title, string? Notes, DateTimeOffset? DueAt, StaffTaskPriority Priority, string? Url);

/// <summary>شمارش برای نشان منو و سربرگ صفحه.</summary>
public sealed record StaffTaskCounts(int Open, int Overdue, int DueToday);

/// <summary>نمای فهرست: open = کارهای باز من، done = انجام‌شده‌های من، delegated = کارهایی که به دیگران سپرده‌ام.</summary>
public enum StaffTaskView
{
    Open,
    Done,
    Delegated,
}

public interface IStaffTaskService
{
    Task<List<StaffTask>> ListAsync(Guid userId, StaffTaskView view, int take = 300);

    /// <summary>شمارش کارهای باز/دیرشده/امروز (۳۰ ثانیه کش؛ با هر تغییر پاک می‌شود).</summary>
    Task<StaffTaskCounts> CountsAsync(Guid userId, DateTimeOffset now);

    Task<OperationResult<Guid>> SaveAsync(StaffTaskInput input, AdminAccess actor);

    Task<OperationResult> SetDoneAsync(Guid id, bool done, AdminAccess actor);

    Task<OperationResult> DeleteAsync(Guid id, AdminAccess actor);

    /// <summary>یادآوری کارهای سررسیده (هر کار یک بار). تعداد یادآوری‌ها برمی‌گردد.</summary>
    Task<int> SendDueRemindersAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

public sealed class StaffTaskService(
    FratakShopDbContext context,
    IAdminAccessService access,
    IStaffNotificationService notifications,
    IMemoryCache cache) : IStaffTaskService
{
    public async Task<List<StaffTask>> ListAsync(Guid userId, StaffTaskView view, int take = 300)
    {
        var query = context.StaffTasks.AsNoTracking();
        query = view switch
        {
            StaffTaskView.Done => query.Where(t => t.AssigneeId == userId && t.IsDone).OrderByDescending(t => t.DoneAt),
            StaffTaskView.Delegated => query.Where(t => t.CreatedById == userId && t.AssigneeId != userId)
                .OrderBy(t => t.IsDone).ThenBy(t => t.DueAt == null).ThenBy(t => t.DueAt).ThenByDescending(t => t.CreatedAt),
            // باز: اول دیرشده‌ها و نزدیک‌ترین موعد، بعد بی‌موعدها به ترتیب اولویت.
            _ => query.Where(t => t.AssigneeId == userId && !t.IsDone)
                .OrderBy(t => t.DueAt == null).ThenBy(t => t.DueAt).ThenByDescending(t => t.Priority).ThenByDescending(t => t.CreatedAt),
        };
        return await query.Take(Math.Clamp(take, 1, 1000)).ToListAsync();
    }

    public async Task<StaffTaskCounts> CountsAsync(Guid userId, DateTimeOffset now) =>
        (await cache.GetOrCreateAsync(CountsKey(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            var endOfDay = now.AddHours(24 - now.ToOffset(TehranOffset).TimeOfDay.TotalHours);
            var open = await context.StaffTasks.Where(t => t.AssigneeId == userId && !t.IsDone)
                .Select(t => t.DueAt).ToListAsync();
            return new StaffTaskCounts(open.Count, open.Count(d => d <= now), open.Count(d => d > now && d <= endOfDay));
        }))!;

    public async Task<OperationResult<Guid>> SaveAsync(StaffTaskInput input, AdminAccess actor)
    {
        var title = input.Title?.Trim();
        if (string.IsNullOrEmpty(title))
            return OperationResult<Guid>.Error("عنوان کار را بنویسید.");
        if (title.Length > 200)
            return OperationResult<Guid>.Error("عنوان کار حداکثر ۲۰۰ نویسه است.");
        var notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        if (notes is { Length: > 2000 })
            return OperationResult<Guid>.Error("توضیح کار حداکثر ۲۰۰۰ نویسه است.");
        if (!Enum.IsDefined(input.Priority))
            return OperationResult<Guid>.Error("اولویت نامعتبر است.");

        var assigneeId = input.AssigneeId is { } a && a != Guid.Empty ? a : actor.UserId;
        if (assigneeId != actor.UserId && !(await access.GetAccessAsync(assigneeId)).IsStaff)
            return OperationResult<Guid>.Error("کار را فقط به کارکنان پنل می‌توان سپرد.");

        StaffTask task;
        var isNew = input.Id is null || input.Id == Guid.Empty;
        Guid? previousAssignee = null;
        if (isNew)
        {
            task = new StaffTask { CreatedById = actor.UserId, CreatedByName = Cut(actor.DisplayName, 150) };
            context.StaffTasks.Add(task);
        }
        else
        {
            var existing = await context.StaffTasks.AsTracking().FirstOrDefaultAsync(t => t.Id == input.Id);
            if (existing is null || (existing.AssigneeId != actor.UserId && existing.CreatedById != actor.UserId))
                return OperationResult<Guid>.NotFound("کار یافت نشد.");
            task = existing;
            previousAssignee = existing.AssigneeId;
            if (existing.DueAt != input.DueAt)
                existing.ReminderSentAt = null;
        }

        task.AssigneeId = assigneeId;
        task.Title = title;
        task.Notes = notes;
        task.DueAt = input.DueAt;
        task.Priority = input.Priority;
        task.Url = CleanUrl(input.Url);
        await context.SaveChangesAsync();
        Invalidate(assigneeId);
        if (previousAssignee is { } previous && previous != assigneeId)
            Invalidate(previous);

        // به همکاری که کار تازه به او سپرده شده خبر بدهیم.
        if (assigneeId != actor.UserId && previousAssignee != assigneeId)
        {
            var due = task.DueAt is { } d ? $" – موعد: {Format(d)}" : "";
            await notifications.NotifyUserAsync(assigneeId, StaffNotificationKind.Task, $"کار تازه از {actor.DisplayName}", $"{title}{due}", "/admin/tasks");
        }

        return OperationResult<Guid>.Success(task.Id, isNew ? "کار اضافه شد." : "کار ذخیره شد.");
    }

    public async Task<OperationResult> SetDoneAsync(Guid id, bool done, AdminAccess actor)
    {
        var task = await context.StaffTasks.AsTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (task is null || (task.AssigneeId != actor.UserId && task.CreatedById != actor.UserId))
            return OperationResult.NotFound("کار یافت نشد.");
        if (task.IsDone == done)
            return OperationResult.Success();

        task.IsDone = done;
        task.DoneAt = done ? DateTimeOffset.UtcNow : null;
        await context.SaveChangesAsync();
        Invalidate(task.AssigneeId);

        // کسی که کار را سپرده بود باخبر شود.
        if (done && task.CreatedById != actor.UserId)
            await notifications.NotifyUserAsync(task.CreatedById, StaffNotificationKind.Task, $"{actor.DisplayName} کار را انجام داد", task.Title, "/admin/tasks?view=delegated");
        return OperationResult.Success(done ? "انجام شد ✓" : "کار دوباره باز شد.");
    }

    public async Task<OperationResult> DeleteAsync(Guid id, AdminAccess actor)
    {
        var task = await context.StaffTasks.AsTracking().FirstOrDefaultAsync(t => t.Id == id);
        // فقط سازندهٔ کار یا کسی که کار برای خودش است (اگر خودش ساخته) حذف می‌کند؛ کار سپرده‌شده را گیرنده نمی‌تواند پاک کند.
        if (task is null || task.CreatedById != actor.UserId)
            return OperationResult.Error(task is null ? "کار یافت نشد." : "کاری را که همکارتان به شما سپرده نمی‌توانید حذف کنید؛ «انجام شد» بزنید.");
        context.StaffTasks.Remove(task);
        await context.SaveChangesAsync();
        Invalidate(task.AssigneeId);
        return OperationResult.Success("کار حذف شد.");
    }

    public async Task<int> SendDueRemindersAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var due = await context.StaffTasks.AsNoTracking()
            .Where(t => !t.IsDone && t.DueAt != null && t.DueAt <= now && t.ReminderSentAt == null)
            .OrderBy(t => t.DueAt).Take(200)
            .Select(t => new { t.Id, t.AssigneeId, t.Title, t.Url })
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var task in due)
        {
            // اول علامت «فرستاده شد» (شرطی) تا با چند سرور یا اجرای هم‌زمان دو بار نرود.
            var claimed = await context.StaffTasks.Where(t => t.Id == task.Id && t.ReminderSentAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReminderSentAt, now), cancellationToken);
            if (claimed != 1)
                continue;
            await notifications.NotifyUserAsync(task.AssigneeId, StaffNotificationKind.Task, "موعد کار رسید", task.Title, task.Url ?? "/admin/tasks");
            Invalidate(task.AssigneeId);
            sent++;
        }

        return sent;
    }

    /// <summary>
    /// فقط آدرس‌های داخلی پنل پذیرفته می‌شوند (جلوگیری از لینک به سایت دیگر). مثال: «/admin/orders/…?tab=2» پذیرفته؛
    /// «https://evil.com» یا «//evil.com» ← null.
    /// </summary>
    public static string? CleanUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        var value = url.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
            value = absolute.PathAndQuery + absolute.Fragment;
        if (!value.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) || value.StartsWith("//") || value.Contains('\\'))
            return null;
        return value.Length > 400 ? null : value;
    }

    /// <summary>منطقهٔ زمانی ایران (بدون ساعت تابستانی از ۱۴۰۱) برای «امروز».</summary>
    private static readonly TimeSpan TehranOffset = TimeSpan.FromMinutes(210);

    private static string Format(DateTimeOffset value)
    {
        var local = value.ToOffset(TehranOffset);
        var calendar = new System.Globalization.PersianCalendar();
        return $"{calendar.GetYear(local.DateTime)}/{calendar.GetMonth(local.DateTime):00}/{calendar.GetDayOfMonth(local.DateTime):00} {local:HH:mm}";
    }

    private void Invalidate(Guid userId) => cache.Remove(CountsKey(userId));

    private static string CountsKey(Guid userId) => $"team:task-counts:{userId:N}";

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}
