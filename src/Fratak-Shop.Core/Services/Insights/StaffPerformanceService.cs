using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.DTOs.Reports;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Insights;

// ═══════════════════════════════════════════════════════════════════════════
//  عملکرد کارکنان (مرحلهٔ هفتم – فاز ۷ب)
//  برای هر کارمند پنل در یک بازه می‌شمارد: تغییرات ثبت‌شده (لاگ ممیزی)، تغییر وضعیت و یادداشت سفارش،
//  سفارش ثبت‌شده توسط کارمند، پاسخ تیکت و میانگین زمان اولین پاسخ، فاکتور خرید، پیام به مشتری و مقالهٔ نوشته‌شده.
//  هدف: دید منصفانه از حجم کار – نه رتبه‌بندی خشک؛ برای همین «امتیاز» ترکیبی ساخته نمی‌شود.
//  صفحه: /admin/reports/staff (مجوز مدیریت کارکنان یا مشاهدهٔ لاگ).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>آمار یک کارمند در بازه.</summary>
public sealed class StaffPerformanceRow
{
    public Guid UserId { get; init; }
    public string Name { get; init; } = "";
    public string? PhoneNumber { get; init; }
    public List<string> Roles { get; init; } = [];

    /// <summary>کل تغییرات ثبت‌شده در لاگ ممیزی (ایجاد/ویرایش/حذف هر چیزی در پنل).</summary>
    public int Actions { get; set; }
    public int StatusChanges { get; set; }
    public int OrderNotes { get; set; }
    public int OrdersCreated { get; set; }
    public decimal OrdersCreatedAmount { get; set; }
    public int TicketReplies { get; set; }

    /// <summary>تیکت‌هایی که این کارمند اولین پاسخ‌دهنده‌شان بوده.</summary>
    public int FirstResponses { get; set; }

    /// <summary>میانگین دقیقه تا اولین پاسخ (فقط تیکت‌هایی که او اولین پاسخ را داده).</summary>
    public double? AvgFirstResponseMinutes { get; set; }
    public int Invoices { get; set; }
    public int CustomerMessages { get; set; }
    public int Posts { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; init; }

    public int Total => Actions + StatusChanges + OrderNotes + OrdersCreated + TicketReplies + Invoices + CustomerMessages + Posts;
}

/// <summary>جزئیات یک کارمند: فعالیت روزانه و پرتکرارترین کارها.</summary>
public sealed class StaffActivityDetail
{
    public StaffPerformanceRow Row { get; init; } = null!;
    public List<ChartPoint> Daily { get; init; } = [];

    /// <summary>پرتکرارترین نوع تغییر. مثال: «ویرایش · Product» ۴۲ بار.</summary>
    public List<NamedAmount> TopActions { get; init; } = [];

    /// <summary>ساعت‌های فعالیت (به وقت ایران) برای دیدن الگوی شیفت.</summary>
    public List<ChartPoint> Hours { get; init; } = [];
}

public interface IStaffPerformanceService
{
    Task<List<StaffPerformanceRow>> GetAsync(ReportPeriod period, CancellationToken cancellationToken = default);

    Task<StaffActivityDetail?> GetDetailAsync(Guid userId, ReportPeriod period, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public class StaffPerformanceService(FratakShopDbContext context) : IStaffPerformanceService
{
    /// <summary>سقف ردیف‌های پیام تیکت که برای زمان اولین پاسخ خوانده می‌شود (حفاظت از حافظه در بازه‌های خیلی بزرگ).</summary>
    private const int MaxTicketMessages = 50_000;

    public async Task<List<StaffPerformanceRow>> GetAsync(ReportPeriod period, CancellationToken cancellationToken = default)
    {
        var staff = await LoadStaffAsync(null, cancellationToken);
        if (staff.Count == 0)
            return [];
        var ids = staff.Select(s => s.UserId).ToList();

        var audit = await Between(context.AuditLogs.AsNoTracking().Where(a => a.UserId != null && ids.Contains(a.UserId.Value)), period, a => a.CreatedAt)
            .GroupBy(a => a.UserId!.Value)
            .Select(g => new { g.Key, Count = g.Count(), Last = g.Max(a => a.CreatedAt) })
            .ToListAsync(cancellationToken);

        var notes = await Between(context.OrderNotes.AsNoTracking().Where(n => n.AuthorUserId != null && ids.Contains(n.AuthorUserId.Value)), period, n => n.CreatedAt)
            .GroupBy(n => new { User = n.AuthorUserId!.Value, n.Kind })
            .Select(g => new { g.Key.User, g.Key.Kind, Count = g.Count(), Last = g.Max(n => n.CreatedAt) })
            .ToListAsync(cancellationToken);

        var orders = await Between(context.Orders.AsNoTracking().Where(o => o.CreatedByStaffId != null && ids.Contains(o.CreatedByStaffId.Value)), period, o => o.CreatedAt)
            .GroupBy(o => o.CreatedByStaffId!.Value)
            .Select(g => new { g.Key, Count = g.Count(), Amount = g.Sum(o => o.PayableAmount), Last = g.Max(o => o.CreatedAt) })
            .ToListAsync(cancellationToken);

        var replies = await Between(context.SupportTicketMessages.AsNoTracking().Where(m => m.IsStaff && m.UserId != null && ids.Contains(m.UserId.Value)), period, m => m.CreatedAt)
            .GroupBy(m => m.UserId!.Value)
            .Select(g => new { g.Key, Count = g.Count(), Last = g.Max(m => m.CreatedAt) })
            .ToListAsync(cancellationToken);

        var invoices = await Between(context.PurchaseInvoices.AsNoTracking().Where(i => i.UploadedByUserId != null && ids.Contains(i.UploadedByUserId.Value)), period, i => i.CreatedAt)
            .GroupBy(i => i.UploadedByUserId!.Value)
            .Select(g => new { g.Key, Count = g.Count(), Last = g.Max(i => i.CreatedAt) })
            .ToListAsync(cancellationToken);

        var messages = await Between(context.OrderMessages.AsNoTracking().Where(m => m.AuthorUserId != null && ids.Contains(m.AuthorUserId.Value)), period, m => m.CreatedAt)
            .GroupBy(m => m.AuthorUserId!.Value)
            .Select(g => new { g.Key, Count = g.Count(), Last = g.Max(m => m.CreatedAt) })
            .ToListAsync(cancellationToken);

        var posts = await Between(context.BlogPosts.AsNoTracking().Where(p => p.AuthorId != null && ids.Contains(p.AuthorId.Value)), period, p => p.CreatedAt)
            .GroupBy(p => p.AuthorId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var firstResponses = await FirstResponsesAsync(period, cancellationToken);

        foreach (var row in staff)
        {
            var id = row.UserId;
            var lasts = new List<DateTimeOffset?>();
            if (audit.FirstOrDefault(a => a.Key == id) is { } a)
            {
                row.Actions = a.Count;
                lasts.Add(a.Last);
            }
            foreach (var n in notes.Where(n => n.User == id))
            {
                if (n.Kind == OrderNoteKind.StatusChange)
                    row.StatusChanges += n.Count;
                else if (n.Kind == OrderNoteKind.Note)
                    row.OrderNotes += n.Count;
                lasts.Add(n.Last);
            }
            if (orders.FirstOrDefault(o => o.Key == id) is { } o)
            {
                row.OrdersCreated = o.Count;
                row.OrdersCreatedAmount = o.Amount;
                lasts.Add(o.Last);
            }
            if (replies.FirstOrDefault(r => r.Key == id) is { } r)
            {
                row.TicketReplies = r.Count;
                lasts.Add(r.Last);
            }
            if (invoices.FirstOrDefault(i => i.Key == id) is { } inv)
            {
                row.Invoices = inv.Count;
                lasts.Add(inv.Last);
            }
            if (messages.FirstOrDefault(m => m.Key == id) is { } msg)
            {
                row.CustomerMessages = msg.Count;
                lasts.Add(msg.Last);
            }
            row.Posts = posts.FirstOrDefault(p => p.Key == id)?.Count ?? 0;
            if (firstResponses.TryGetValue(id, out var fr))
            {
                row.FirstResponses = fr.Count;
                row.AvgFirstResponseMinutes = Math.Round(fr.TotalMinutes / fr.Count, 1);
            }
            row.LastActivityAt = lasts.Max();
        }

        return staff.OrderByDescending(s => s.Total).ThenBy(s => s.Name).ToList();
    }

    public async Task<StaffActivityDetail?> GetDetailAsync(Guid userId, ReportPeriod period, CancellationToken cancellationToken = default)
    {
        var rows = await GetAsync(period, cancellationToken);
        var row = rows.FirstOrDefault(r => r.UserId == userId);
        if (row is null)
            return null;

        // فعالیت روزانه = تغییرات ممیزی + یادداشت‌های سفارش + پاسخ تیکت (برای بازهٔ بی‌انتها، ۹۰ روز اخیر).
        var from = period.From ?? DateTimeOffset.UtcNow.AddDays(-89);
        var effective = period with { From = from };
        var stamps = new List<DateTimeOffset>();
        stamps.AddRange(await Between(context.AuditLogs.AsNoTracking().Where(a => a.UserId == userId), effective, a => a.CreatedAt)
            .OrderByDescending(a => a.CreatedAt).Take(20_000).Select(a => a.CreatedAt).ToListAsync(cancellationToken));
        stamps.AddRange(await Between(context.OrderNotes.AsNoTracking().Where(n => n.AuthorUserId == userId), effective, n => n.CreatedAt)
            .OrderByDescending(n => n.CreatedAt).Take(10_000).Select(n => n.CreatedAt).ToListAsync(cancellationToken));
        stamps.AddRange(await Between(context.SupportTicketMessages.AsNoTracking().Where(m => m.IsStaff && m.UserId == userId), effective, m => m.CreatedAt)
            .OrderByDescending(m => m.CreatedAt).Take(10_000).Select(m => m.CreatedAt).ToListAsync(cancellationToken));

        var first = IranClock.Today(from);
        var last = IranClock.Today((period.To ?? DateTimeOffset.UtcNow).AddSeconds(-1));
        var byDay = stamps.GroupBy(IranClock.Today).ToDictionary(g => g.Key, g => g.Count());
        var daily = new List<ChartPoint>();
        if (last.DayNumber - first.DayNumber <= 120)
        {
            for (var day = first; day <= last; day = day.AddDays(1))
                daily.Add(new ChartPoint(DayLabel(day), byDay.GetValueOrDefault(day)));
        }

        var byHour = stamps.GroupBy(s => IranClock.ToLocal(s).Hour).ToDictionary(g => g.Key, g => g.Count());
        var hours = Enumerable.Range(0, 24).Select(h => new ChartPoint(h.ToString("00"), byHour.GetValueOrDefault(h))).ToList();

        var top = await Between(context.AuditLogs.AsNoTracking().Where(a => a.UserId == userId), effective, a => a.CreatedAt)
            .GroupBy(a => new { a.Action, a.EntityType })
            .Select(g => new { g.Key.Action, g.Key.EntityType, Count = g.Count() })
            .OrderByDescending(g => g.Count).Take(10).ToListAsync(cancellationToken);

        return new StaffActivityDetail
        {
            Row = row,
            Daily = daily,
            Hours = hours,
            TopActions = top.Select(t => new NamedAmount($"{ActionTitle(t.Action)} · {t.EntityType}", t.Count, t.Count)).ToList(),
        };
    }

    /// <summary>
    /// زمان اولین پاسخ: برای تیکت‌های ساخته‌شده در بازه، اولین پیام کارمند و فاصله‌اش تا ساخت تیکت.
    /// خروجی: برای هر کارمند تعداد تیکت و جمع دقیقه‌ها.
    /// </summary>
    private async Task<Dictionary<Guid, (int Count, double TotalMinutes)>> FirstResponsesAsync(ReportPeriod period, CancellationToken cancellationToken)
    {
        var tickets = Between(context.SupportTickets.AsNoTracking(), period, t => t.CreatedAt);
        var rows = await context.SupportTicketMessages.AsNoTracking()
            .Where(m => m.IsStaff && m.UserId != null && tickets.Any(t => t.Id == m.TicketId))
            .OrderBy(m => m.CreatedAt).Take(MaxTicketMessages)
            .Select(m => new { m.TicketId, UserId = m.UserId!.Value, m.CreatedAt, TicketCreatedAt = m.Ticket.CreatedAt })
            .ToListAsync(cancellationToken);

        return rows.GroupBy(m => m.TicketId)
            .Select(g => g.OrderBy(m => m.CreatedAt).First())
            .GroupBy(m => m.UserId)
            .ToDictionary(g => g.Key, g => (g.Count(), g.Sum(m => Math.Max(0, (m.CreatedAt - m.TicketCreatedAt).TotalMinutes))));
    }

    /// <summary>کارکنان = کاربرانی که حداقل یک نقش پنل دارند (همراه نام نقش‌ها).</summary>
    private async Task<List<StaffPerformanceRow>> LoadStaffAsync(Guid? userId, CancellationToken cancellationToken)
    {
        var query = context.UserAdminRoles.AsNoTracking();
        if (userId is { } id)
            query = query.Where(r => r.UserId == id);
        var links = await query
            .Select(r => new { r.UserId, r.User.FirstName, r.User.LastName, r.User.PhoneNumber, r.User.LastLoginAt, Role = r.Role.Name })
            .ToListAsync(cancellationToken);
        return links.GroupBy(l => l.UserId).Select(g =>
        {
            var first = g.First();
            var name = $"{first.FirstName} {first.LastName}".Trim();
            return new StaffPerformanceRow
            {
                UserId = g.Key,
                Name = string.IsNullOrWhiteSpace(name) ? first.PhoneNumber ?? "کارمند" : name,
                PhoneNumber = first.PhoneNumber,
                LastLoginAt = first.LastLoginAt,
                Roles = g.Select(x => x.Role).Distinct().ToList(),
            };
        }).ToList();
    }

    private static IQueryable<T> Between<T>(IQueryable<T> query, ReportPeriod period, System.Linq.Expressions.Expression<Func<T, DateTimeOffset>> date)
    {
        // مقدارها از طریق closure خوانده می‌شوند تا EF آن‌ها را پارامتر SQL کند (نه ثابت؛ کش طرح کوئری حفظ می‌شود).
        if (period.From is { } from)
        {
            System.Linq.Expressions.Expression<Func<DateTimeOffset>> value = () => from;
            var ge = System.Linq.Expressions.Expression.GreaterThanOrEqual(date.Body, value.Body);
            query = query.Where(System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(ge, date.Parameters));
        }
        if (period.To is { } to)
        {
            System.Linq.Expressions.Expression<Func<DateTimeOffset>> value = () => to;
            var lt = System.Linq.Expressions.Expression.LessThan(date.Body, value.Body);
            query = query.Where(System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(lt, date.Parameters));
        }
        return query;
    }

    public static string ActionTitle(string action) => action switch
    {
        "create" => "ایجاد",
        "update" => "ویرایش",
        "delete" => "حذف",
        "restore" => "بازیابی",
        _ => action,
    };

    private static string DayLabel(DateOnly day)
    {
        var d = day.ToDateTime(TimeOnly.MinValue);
        var persian = new System.Globalization.PersianCalendar();
        return $"{persian.GetMonth(d):00}/{persian.GetDayOfMonth(d):00}";
    }
}
