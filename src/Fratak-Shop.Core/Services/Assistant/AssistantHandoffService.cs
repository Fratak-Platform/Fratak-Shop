using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Assistant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Assistant;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۴ (مورد ۲۱) – «گفت‌وگو با کارشناس» از داخل چت.
//  مشتری: «می‌خوام با یه آدم حرف بزنم» ← یک گفت‌وگوی باز + چند پیام اخیر چت (پوشانده‌شده) به‌عنوان پیش‌زمینه + اعلان به کارشناسان پشتیبانی.
//  کارشناس: پنل ‎/admin/smart/assistant/handoffs ← جواب ← ویجت مشتری (فقط وقتی گفت‌وگوی باز دارد) هر چند ثانیه پیام تازه را می‌گیرد.
//  ایمنی: متن‌ها ساده و حداکثر ۱۰۰۰ نویسه؛ هر مرورگر فقط گفت‌وگوی شناسهٔ خودش را می‌بیند؛ حداکثر ۳۰ پیام مشتری در هر گفت‌وگو در ساعت؛
//  گفت‌وگوی بی‌پیام بعد از ۲۴ ساعت خودکار بسته می‌شود.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>یک پیام گفت‌وگو برای ویجت یا پنل. At = زمان (میلی‌ثانیهٔ یونیکس، برای پرسیدن «پیام‌های بعد از»).</summary>
public sealed record HandoffMessageDto(Guid Id, bool FromStaff, string? Author, string Text, DateTimeOffset CreatedAt)
{
    public long At => CreatedAt.ToUnixTimeMilliseconds();
}

/// <summary>خلاصهٔ یک گفت‌وگو در فهرست پنل.</summary>
public sealed record HandoffSummaryDto(Guid Id, string? DisplayName, Guid? UserId, AssistantHandoffStatus Status, string? StaffName, int Unread, DateTimeOffset CreatedAt, DateTimeOffset LastMessageAt, string? LastText, string? PageUrl);

/// <summary>وضعیت گفت‌وگوی یک مرورگر برای ویجت.</summary>
public sealed record HandoffPollDto(bool Open, AssistantHandoffStatus? Status, IReadOnlyList<HandoffMessageDto> Messages);

/// <summary>گفت‌وگو با کارشناس.</summary>
public interface IAssistantHandoffService
{
    /// <summary>باز کردن (یا برگرداندن گفت‌وگوی باز موجود). خروجی: (شناسه، تازه ساخته شد؟).</summary>
    Task<(Guid Id, bool Created)> OpenAsync(string sessionKey, Guid? userId, string? pagePath, CancellationToken cancellationToken = default);

    /// <summary>گفت‌وگوی باز این مرورگر هست؟</summary>
    Task<bool> IsOpenAsync(string sessionKey, CancellationToken cancellationToken = default);

    /// <summary>پیام مشتری. false ← گفت‌وگوی باز نیست، متن خالی است یا سقف پیام پر شده.</summary>
    Task<bool> PostCustomerAsync(string sessionKey, string text, CancellationToken cancellationToken = default);

    /// <summary>پیام‌های کارشناس بعد از زمان <paramref name="afterUnixMs"/> (برای ویجت).</summary>
    Task<HandoffPollDto> PollAsync(string sessionKey, long afterUnixMs, CancellationToken cancellationToken = default);

    /// <summary>بستن به درخواست مشتری.</summary>
    Task<bool> CloseByCustomerAsync(string sessionKey, CancellationToken cancellationToken = default);

    /// <summary>فهرست پنل (باز‌ها اول).</summary>
    Task<IReadOnlyList<HandoffSummaryDto>> ListAsync(bool includeClosed, CancellationToken cancellationToken = default);

    /// <summary>یک گفت‌وگو با همهٔ پیام‌ها (و صفر کردن «خوانده‌نشده»).</summary>
    Task<(HandoffSummaryDto Summary, IReadOnlyList<HandoffMessageDto> Messages)?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>جواب کارشناس.</summary>
    Task<bool> ReplyAsync(Guid id, Guid staffUserId, string staffName, string text, CancellationToken cancellationToken = default);

    /// <summary>بستن از پنل.</summary>
    Task<bool> CloseAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>بستن گفت‌وگوهای بی‌پیام قدیمی‌تر از ۲۴ ساعت. خروجی: تعداد.</summary>
    Task<int> AutoCloseAsync(CancellationToken cancellationToken = default);

    /// <summary>تعداد گفت‌وگوهای منتظر (برای نشان پنل).</summary>
    Task<int> WaitingCountAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class AssistantHandoffService(
    FratakShopDbContext context,
    IStaffNotificationService notifications,
    ILogger<AssistantHandoffService> logger) : IAssistantHandoffService
{
    /// <summary>بیشترین طول یک پیام.</summary>
    public const int MaxText = 1000;

    /// <summary>بیشترین پیام مشتری در یک ساعت (جلوی پر کردن صف کارشناس).</summary>
    public const int MaxCustomerPerHour = 30;

    /// <summary>بعد از این مدت بی‌پیامی گفت‌وگو خودکار بسته می‌شود.</summary>
    public static readonly TimeSpan IdleClose = TimeSpan.FromHours(24);

    /// <summary>نام نویسندهٔ پیام‌های مشتری (پیام پیش‌زمینهٔ دستیار «دستیار» است و در سقف پیام شمرده نمی‌شود).</summary>
    public const string CustomerAuthor = "مشتری";

    /// <summary>مسیر پنل.</summary>
    public const string AdminUrl = "/admin/smart/assistant/handoffs";

    private IQueryable<AssistantHandoff> OpenOf(string sessionKey) =>
        context.AssistantHandoffs.Where(h => h.SessionKey == sessionKey && h.Status != AssistantHandoffStatus.Closed);

    /// <inheritdoc />
    public async Task<(Guid Id, bool Created)> OpenAsync(string sessionKey, Guid? userId, string? pagePath, CancellationToken cancellationToken = default)
    {
        var existing = await OpenOf(sessionKey).OrderByDescending(h => h.CreatedAt).Select(h => (Guid?)h.Id).FirstOrDefaultAsync(cancellationToken);
        if (existing is { } id)
            return (id, false);

        string? display = null;
        if (userId is { } uid)
        {
            var user = await context.Users.AsNoTracking().Where(u => u.Id == uid).Select(u => new { u.FirstName, u.LastName }).FirstOrDefaultAsync(cancellationToken);
            display = string.Join(' ', new[] { user?.FirstName, user?.LastName }.Where(n => !string.IsNullOrWhiteSpace(n))).Trim();
            if (display.Length == 0)
                display = "مشتری عضو";
        }
        var now = DateTimeOffset.UtcNow;
        var handoff = new AssistantHandoff
        {
            SessionKey = sessionKey,
            UserId = userId,
            DisplayName = Cut(display ?? "مهمان", 80),
            PageUrl = AssistantGuide.IsLocalUrl(pagePath) ? Cut(pagePath, 300) : null,
            Status = AssistantHandoffStatus.Waiting,
            LastMessageAt = now,
            UnreadForStaff = 1,
        };
        // پیش‌زمینه: چند پیام اخیر همین گفت‌وگو با دستیار (پیش‌تر پوشانده شده‌اند).
        var recent = await context.AssistantMessages.AsNoTracking()
            .Where(m => m.SessionKey == sessionKey && m.CreatedAt >= now.AddHours(-6))
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => m.Text)
            .Take(6)
            .ToListAsync(cancellationToken);
        var context_ = recent.Count == 0
            ? "مشتری درخواست گفت‌وگو با کارشناس داد."
            : "پیام‌های اخیر مشتری به دستیار:\n" + string.Join("\n", recent.AsEnumerable().Reverse().Select(t => "• " + t));
        handoff.Messages.Add(new AssistantHandoffMessage { FromStaff = false, AuthorName = "دستیار", Text = Cut(context_, MaxText)! });
        context.AssistantHandoffs.Add(handoff);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        try
        {
            await notifications.NotifyAsync(new StaffAlert(StaffNotificationKind.NewMessage,
                $"درخواست گفت‌وگو با کارشناس – {handoff.DisplayName}",
                recent.FirstOrDefault() is { } last ? Cut(last, 140) : "از دستیار فروشگاه",
                $"{AdminUrl}?id={handoff.Id}",
                [AdminPermissions.TicketsManage, AdminPermissions.MessagesManage]));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Handoff staff notification failed");
        }
        return (handoff.Id, true);
    }

    /// <inheritdoc />
    public Task<bool> IsOpenAsync(string sessionKey, CancellationToken cancellationToken = default) =>
        OpenOf(sessionKey).AnyAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> PostCustomerAsync(string sessionKey, string text, CancellationToken cancellationToken = default)
    {
        text = Cut(text, MaxText) ?? "";
        if (text.Length == 0)
            return false;
        var handoff = await OpenOf(sessionKey).AsTracking().OrderByDescending(h => h.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (handoff is null)
            return false;
        var hourAgo = DateTimeOffset.UtcNow.AddHours(-1);
        // فقط پیام‌های خود مشتری شمرده می‌شود (نه پیام پیش‌زمینه‌ای که دستیار هنگام باز شدن گذاشته).
        var recent = await context.AssistantHandoffMessages.CountAsync(m => m.HandoffId == handoff.Id && !m.FromStaff && m.AuthorName == CustomerAuthor && m.CreatedAt >= hourAgo, cancellationToken);
        if (recent >= MaxCustomerPerHour)
            return false;
        context.AssistantHandoffMessages.Add(new AssistantHandoffMessage { HandoffId = handoff.Id, FromStaff = false, AuthorName = CustomerAuthor, Text = text });
        handoff.LastMessageAt = DateTimeOffset.UtcNow;
        handoff.UnreadForStaff++;
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return true;
    }

    /// <inheritdoc />
    public async Task<HandoffPollDto> PollAsync(string sessionKey, long afterUnixMs, CancellationToken cancellationToken = default)
    {
        var handoff = await context.AssistantHandoffs.AsNoTracking()
            .Where(h => h.SessionKey == sessionKey)
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => new { h.Id, h.Status })
            .FirstOrDefaultAsync(cancellationToken);
        if (handoff is null)
            return new HandoffPollDto(false, null, []);
        // «At» پیام‌ها تا میلی‌ثانیه گرد شده ولی زمان ذخیره‌شده دقیق‌تر است؛ پس «بعد از» یعنی از میلی‌ثانیهٔ بعد (وگرنه آخرین پیام هر بار تکرار می‌شد).
        var after = DateTimeOffset.FromUnixTimeMilliseconds(Math.Clamp(afterUnixMs, 0, 253402300799998) + 1);
        var messages = await context.AssistantHandoffMessages.AsNoTracking()
            .Where(m => m.HandoffId == handoff.Id && m.FromStaff && m.CreatedAt >= after)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new HandoffMessageDto(m.Id, m.FromStaff, m.AuthorName, m.Text, m.CreatedAt))
            .Take(20)
            .ToListAsync(cancellationToken);
        return new HandoffPollDto(handoff.Status != AssistantHandoffStatus.Closed, handoff.Status, messages);
    }

    /// <inheritdoc />
    public async Task<bool> CloseByCustomerAsync(string sessionKey, CancellationToken cancellationToken = default) =>
        await OpenOf(sessionKey).ExecuteUpdateAsync(s => s.SetProperty(h => h.Status, AssistantHandoffStatus.Closed).SetProperty(h => h.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken) > 0;

    /// <inheritdoc />
    public async Task<IReadOnlyList<HandoffSummaryDto>> ListAsync(bool includeClosed, CancellationToken cancellationToken = default)
    {
        var query = context.AssistantHandoffs.AsNoTracking();
        if (!includeClosed)
            query = query.Where(h => h.Status != AssistantHandoffStatus.Closed);
        return await query
            .OrderBy(h => h.Status == AssistantHandoffStatus.Closed ? 1 : 0)
            .ThenBy(h => h.Status == AssistantHandoffStatus.Waiting ? 0 : 1)
            .ThenByDescending(h => h.LastMessageAt)
            .Take(200)
            .Select(h => new HandoffSummaryDto(h.Id, h.DisplayName, h.UserId, h.Status, h.StaffName, h.UnreadForStaff, h.CreatedAt, h.LastMessageAt,
                h.Messages.OrderByDescending(m => m.CreatedAt).Select(m => m.Text).FirstOrDefault(), h.PageUrl))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(HandoffSummaryDto Summary, IReadOnlyList<HandoffMessageDto> Messages)?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var summary = (await ListOneAsync(id, cancellationToken));
        if (summary is null)
            return null;
        var messages = await context.AssistantHandoffMessages.AsNoTracking()
            .Where(m => m.HandoffId == id)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new HandoffMessageDto(m.Id, m.FromStaff, m.AuthorName, m.Text, m.CreatedAt))
            .Take(500)
            .ToListAsync(cancellationToken);
        if (summary.Unread > 0)
            await context.AssistantHandoffs.Where(h => h.Id == id).ExecuteUpdateAsync(s => s.SetProperty(h => h.UnreadForStaff, 0), cancellationToken);
        return (summary, messages);
    }

    private Task<HandoffSummaryDto?> ListOneAsync(Guid id, CancellationToken cancellationToken) =>
        context.AssistantHandoffs.AsNoTracking()
            .Where(h => h.Id == id)
            .Select(h => new HandoffSummaryDto(h.Id, h.DisplayName, h.UserId, h.Status, h.StaffName, h.UnreadForStaff, h.CreatedAt, h.LastMessageAt, null, h.PageUrl))
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> ReplyAsync(Guid id, Guid staffUserId, string staffName, string text, CancellationToken cancellationToken = default)
    {
        text = Cut(text, MaxText) ?? "";
        if (text.Length == 0)
            return false;
        var handoff = await context.AssistantHandoffs.AsTracking().FirstOrDefaultAsync(h => h.Id == id, cancellationToken);
        if (handoff is null || handoff.Status == AssistantHandoffStatus.Closed)
            return false;
        var name = Cut(staffName, 80) ?? "کارشناس";
        context.AssistantHandoffMessages.Add(new AssistantHandoffMessage { HandoffId = id, FromStaff = true, AuthorName = name, Text = text });
        handoff.Status = AssistantHandoffStatus.Active;
        handoff.StaffUserId = staffUserId;
        handoff.StaffName = name;
        handoff.LastMessageAt = DateTimeOffset.UtcNow;
        handoff.UnreadForStaff = 0;
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> CloseAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.AssistantHandoffs.Where(h => h.Id == id && h.Status != AssistantHandoffStatus.Closed)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.Status, AssistantHandoffStatus.Closed).SetProperty(h => h.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken) > 0;

    /// <inheritdoc />
    public async Task<int> AutoCloseAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - IdleClose;
        return await context.AssistantHandoffs.Where(h => h.Status != AssistantHandoffStatus.Closed && h.LastMessageAt < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.Status, AssistantHandoffStatus.Closed).SetProperty(h => h.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> WaitingCountAsync(CancellationToken cancellationToken = default) =>
        context.AssistantHandoffs.CountAsync(h => h.Status == AssistantHandoffStatus.Waiting, cancellationToken);

    private static string? Cut(string? value, int max)
    {
        value = value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value.Length > max ? value[..max] : value;
    }
}
