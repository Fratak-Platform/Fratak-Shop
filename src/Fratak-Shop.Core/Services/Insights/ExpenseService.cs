using System.Globalization;
using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.DTOs.Reports;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Insights;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Insights;

// ═══════════════════════════════════════════════════════════════════════════
//  هزینه‌های فروشگاه (مرحلهٔ هفتم – فاز ۷ب)
//  ثبت هزینه‌های جاری (هاست، دامنه، تبلیغات، حقوق …)، هزینه‌های تکراری با یادآوری سررسید،
//  و «سود خالص» = سود ناخالص واقعی (فروش − فاکتور خرید) − هزینه‌های همان بازه.
//  صفحه: /admin/finance/expenses ؛ یادآوری: Web/Infrastructure/Insights/InsightsWorker.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>قواعد خالص هزینه‌ها (بدون دیتابیس؛ تست‌پذیر).</summary>
public static class ExpenseRules
{
    private static readonly PersianCalendar Persian = new();

    /// <summary>دسته‌ها به ترتیب نمایش در فرم.</summary>
    public static readonly ExpenseCategory[] Categories =
    [
        ExpenseCategory.Hosting, ExpenseCategory.Domain, ExpenseCategory.Marketing, ExpenseCategory.Salary, ExpenseCategory.Software,
        ExpenseCategory.Shipping, ExpenseCategory.Packaging, ExpenseCategory.Rent, ExpenseCategory.Tax, ExpenseCategory.BankFees, ExpenseCategory.Other,
    ];

    public static string CategoryTitle(ExpenseCategory category) => category switch
    {
        ExpenseCategory.Hosting => "هاست و سرور",
        ExpenseCategory.Domain => "دامنه و SSL",
        ExpenseCategory.Marketing => "تبلیغات و بازاریابی",
        ExpenseCategory.Salary => "حقوق و دستمزد",
        ExpenseCategory.Software => "نرم‌افزار و سرویس‌ها",
        ExpenseCategory.Shipping => "هزینهٔ ارسال",
        ExpenseCategory.Packaging => "بسته‌بندی",
        ExpenseCategory.Rent => "اجاره",
        ExpenseCategory.Tax => "مالیات و عوارض",
        ExpenseCategory.BankFees => "کارمزد بانکی و درگاه",
        _ => "سایر",
    };

    public static string RecurrenceTitle(ExpenseRecurrence recurrence) => recurrence switch
    {
        ExpenseRecurrence.Monthly => "ماهانه",
        ExpenseRecurrence.Yearly => "سالانه",
        _ => "یک‌باره",
    };

    /// <summary>
    /// سررسید بعدی بر اساس تقویم شمسی. اگر روز در ماه بعد نباشد، آخرین روز آن ماه.
    /// مثال: ۱۴۰۵/۰۶/۳۱ ماهانه → ۱۴۰۵/۰۷/۳۰ ؛ ۱۴۰۵/۰۲/۱۰ سالانه → ۱۴۰۶/۰۲/۱۰.
    /// </summary>
    public static DateOnly? NextDue(DateOnly from, ExpenseRecurrence recurrence)
    {
        var date = from.ToDateTime(TimeOnly.MinValue);
        return recurrence switch
        {
            ExpenseRecurrence.Monthly => DateOnly.FromDateTime(Persian.AddMonths(date, 1)),
            ExpenseRecurrence.Yearly => DateOnly.FromDateTime(Persian.AddYears(date, 1)),
            _ => null,
        };
    }

    /// <summary>بررسی ورودی فرم؛ خروجی: پیام خطا یا null.</summary>
    public static string? Validate(ExpenseInput input, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Trim().Length is < 2 or > 150)
            return "عنوان هزینه باید ۲ تا ۱۵۰ حرف باشد.";
        if (input.Amount is <= 0 or > 1_000_000_000_000m)
            return "مبلغ هزینه باید بیشتر از صفر باشد.";
        if (decimal.Truncate(input.Amount) != input.Amount)
            return "مبلغ باید به تومان و بدون اعشار باشد.";
        if (!Enum.IsDefined(input.Category) || input.Category == ExpenseCategory.None)
            return "دستهٔ هزینه را انتخاب کنید.";
        if (!Enum.IsDefined(input.Recurrence))
            return "نوع تکرار نامعتبر است.";
        if (input.SpentOn > today.AddDays(366) || input.SpentOn < new DateOnly(2000, 1, 1))
            return "تاریخ هزینه نامعتبر است.";
        if (input.Vendor?.Trim().Length > 150 || input.Note?.Trim().Length > 1000)
            return "طرف حساب حداکثر ۱۵۰ و توضیح حداکثر ۱۰۰۰ حرف.";
        return null;
    }
}

/// <summary>ورودی فرم هزینه. Id خالی = هزینهٔ تازه.</summary>
public sealed record ExpenseInput(Guid? Id, string Title, ExpenseCategory Category, decimal Amount, DateOnly SpentOn,
    string? Vendor, string? Note, ExpenseRecurrence Recurrence);

public sealed record ExpenseRow(Guid Id, string Title, ExpenseCategory Category, decimal Amount, DateOnly SpentOn, string? Vendor, string? Note,
    ExpenseRecurrence Recurrence, DateOnly? NextDueOn, Guid? RecurringParentId, string? CreatedByName)
{
    public string CategoryTitle => ExpenseRules.CategoryTitle(Category);
    public bool IsRecurringActive => Recurrence != ExpenseRecurrence.None && NextDueOn.HasValue;
}

/// <param name="Category">null = همه.</param>
public sealed record ExpenseQuery(ReportPeriod Period, ExpenseCategory? Category, string? Search, int Page = 1, int Take = 30);

/// <summary>خلاصهٔ هزینه‌های یک بازه برای کارت‌ها و نمودار.</summary>
public sealed class ExpenseSummary
{
    public decimal Total { get; set; }
    public int Count { get; set; }
    public List<NamedAmount> ByCategory { get; set; } = [];

    /// <summary>جمع هزینهٔ هر ماه شمسی در ۱۲ ماه اخیر (برای نمودار).</summary>
    public List<ChartPoint> Monthly { get; set; } = [];

    /// <summary>هزینه‌های تکراری فعال به ترتیب سررسید.</summary>
    public List<ExpenseRow> Recurring { get; set; } = [];

    /// <summary>جمع ماهانهٔ تقریبی هزینه‌های تکراری (سالانه ÷ ۱۲).</summary>
    public decimal MonthlyRecurringCost { get; set; }
}

public interface IExpenseService
{
    Task<PagedList<ExpenseRow>> ListAsync(ExpenseQuery query, CancellationToken cancellationToken = default);

    Task<ExpenseSummary> GetSummaryAsync(ReportPeriod period, CancellationToken cancellationToken = default);

    /// <summary>جمع هزینه‌های بازه (برای سود خالص در گزارش مالی و گزارش خودکار).</summary>
    Task<decimal> GetTotalAsync(ReportPeriod period, CancellationToken cancellationToken = default);

    Task<ExpenseRow?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>نزدیک‌ترین سررسیدهای هزینه‌های تکراری (ویجت داشبورد).</summary>
    Task<List<ExpenseRow>> GetUpcomingAsync(int take = 5, CancellationToken cancellationToken = default);

    Task<OperationResult<Guid>> SaveAsync(ExpenseInput input, Guid? userId, string? userName, CancellationToken cancellationToken = default);

    Task<OperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// «ثبت پرداخت این دوره» برای هزینهٔ تکراری: یک هزینهٔ تازه با تاریخ سررسید ساخته و سررسید یک دوره جلو می‌رود.
    /// <param name="amount">مبلغ این دوره اگر با قبل فرق دارد (null = همان مبلغ).</param>
    /// </summary>
    Task<OperationResult<Guid>> RecordNextAsync(Guid id, decimal? amount, Guid? userId, string? userName, CancellationToken cancellationToken = default);

    /// <summary>توقف تکرار (سررسید پاک می‌شود؛ تاریخچه می‌ماند).</summary>
    Task<OperationResult> StopRecurrenceAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// اعلان سررسید هزینه‌های تکراری که تا <paramref name="daysAhead"/> روز آینده سررسید دارند (هر روز یک بار برای هر هزینه).
    /// استفاده: سرویس پس‌زمینهٔ گزارش‌ها. خروجی: تعداد اعلان‌ها.
    /// </summary>
    Task<int> NotifyDueAsync(DateOnly today, int daysAhead = 3, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public class ExpenseService(FratakShopDbContext context, IStaffNotificationService notifications) : IExpenseService
{
    private static readonly PersianCalendar Persian = new();

    private IQueryable<Expense> InPeriod(ReportPeriod period)
    {
        var query = context.Expenses.AsNoTracking();
        if (period.From is { } from)
        {
            var start = IranClock.Today(from);
            query = query.Where(e => e.SpentOn >= start);
        }
        if (period.To is { } to)
        {
            var end = IranClock.Today(to);
            query = query.Where(e => e.SpentOn < end);
        }
        return query;
    }

    private static readonly System.Linq.Expressions.Expression<Func<Expense, ExpenseRow>> ToRow = e =>
        new ExpenseRow(e.Id, e.Title, e.Category, e.Amount, e.SpentOn, e.Vendor, e.Note, e.Recurrence, e.NextDueOn, e.RecurringParentId, e.CreatedByName);

    public async Task<PagedList<ExpenseRow>> ListAsync(ExpenseQuery query, CancellationToken cancellationToken = default)
    {
        var items = InPeriod(query.Period);
        if (query.Category is { } category)
            items = items.Where(e => e.Category == category);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            items = items.Where(e => e.Title.Contains(term) || (e.Vendor != null && e.Vendor.Contains(term)) || (e.Note != null && e.Note.Contains(term)));
        }

        var take = Math.Clamp(query.Take, 10, 200);
        var page = Math.Max(1, query.Page);
        var total = await items.CountAsync(cancellationToken);
        var rows = await items.OrderByDescending(e => e.SpentOn).ThenByDescending(e => e.CreatedAt)
            .Skip((page - 1) * take).Take(take).Select(ToRow).ToListAsync(cancellationToken);
        return new PagedList<ExpenseRow>(rows, total, page, take);
    }

    public async Task<ExpenseSummary> GetSummaryAsync(ReportPeriod period, CancellationToken cancellationToken = default)
    {
        var summary = new ExpenseSummary();
        var byCategory = await InPeriod(period).GroupBy(e => e.Category)
            .Select(g => new { g.Key, Count = g.Count(), Amount = g.Sum(e => e.Amount) })
            .ToListAsync(cancellationToken);
        summary.Total = byCategory.Sum(c => c.Amount);
        summary.Count = byCategory.Sum(c => c.Count);
        summary.ByCategory = byCategory.OrderByDescending(c => c.Amount)
            .Select(c => new NamedAmount(ExpenseRules.CategoryTitle(c.Key), c.Count, c.Amount)).ToList();

        // ۱۲ ماه شمسی اخیر (شامل ماه جاری).
        var today = IranClock.Today(DateTimeOffset.UtcNow);
        var (year, month) = JalaliMonth.Of(today);
        var months = Enumerable.Range(0, 12).Select(i => JalaliMonth.Add(year, month, -11 + i)).ToList();
        var firstStart = JalaliMonth.Range(months[0].Year, months[0].Month).Start;
        var spent = await context.Expenses.AsNoTracking().Where(e => e.SpentOn >= firstStart)
            .Select(e => new { e.SpentOn, e.Amount }).ToListAsync(cancellationToken);
        summary.Monthly = months.Select(m =>
        {
            var (start, end) = JalaliMonth.Range(m.Year, m.Month);
            return new ChartPoint($"{m.Year % 100:00}/{m.Month:00}", spent.Where(e => e.SpentOn >= start && e.SpentOn < end).Sum(e => e.Amount));
        }).ToList();

        summary.Recurring = await context.Expenses.AsNoTracking()
            .Where(e => e.NextDueOn != null && e.Recurrence != ExpenseRecurrence.None)
            .OrderBy(e => e.NextDueOn).Take(100).Select(ToRow).ToListAsync(cancellationToken);
        summary.MonthlyRecurringCost = summary.Recurring.Sum(r => r.Recurrence == ExpenseRecurrence.Yearly ? Math.Round(r.Amount / 12) : r.Amount);
        return summary;
    }

    public async Task<decimal> GetTotalAsync(ReportPeriod period, CancellationToken cancellationToken = default) =>
        await InPeriod(period).SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0;

    public Task<ExpenseRow?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Expenses.AsNoTracking().Where(e => e.Id == id).Select(ToRow).FirstOrDefaultAsync(cancellationToken);

    public Task<List<ExpenseRow>> GetUpcomingAsync(int take = 5, CancellationToken cancellationToken = default) =>
        context.Expenses.AsNoTracking().Where(e => e.NextDueOn != null && e.Recurrence != ExpenseRecurrence.None)
            .OrderBy(e => e.NextDueOn).Take(Math.Clamp(take, 1, 50)).Select(ToRow).ToListAsync(cancellationToken);

    public async Task<OperationResult<Guid>> SaveAsync(ExpenseInput input, Guid? userId, string? userName, CancellationToken cancellationToken = default)
    {
        var today = IranClock.Today(DateTimeOffset.UtcNow);
        if (ExpenseRules.Validate(input, today) is { } error)
            return OperationResult<Guid>.Error(error);

        Expense expense;
        if (input.Id is { } id)
        {
            var existing = await context.Expenses.AsTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
            if (existing is null)
                return OperationResult<Guid>.NotFound("هزینه پیدا نشد.");
            expense = existing;
            var scheduleChanged = expense.Recurrence != input.Recurrence || expense.SpentOn != input.SpentOn;
            if (scheduleChanged)
                expense.NextDueOn = NextDueAfter(input.SpentOn, input.Recurrence, today);
        }
        else
        {
            expense = new Expense { CreatedByUserId = userId, CreatedByName = userName };
            context.Expenses.Add(expense);
            expense.NextDueOn = NextDueAfter(input.SpentOn, input.Recurrence, today);
        }

        expense.Title = input.Title.Trim();
        expense.Category = input.Category;
        expense.Amount = input.Amount;
        expense.SpentOn = input.SpentOn;
        expense.Vendor = string.IsNullOrWhiteSpace(input.Vendor) ? null : input.Vendor.Trim();
        expense.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        expense.Recurrence = input.Recurrence;
        if (input.Recurrence == ExpenseRecurrence.None)
            expense.NextDueOn = null;

        await context.SaveChangesAsync(cancellationToken);
        return OperationResult<Guid>.Success(expense.Id, "هزینه ذخیره شد.");
    }

    /// <summary>
    /// اولین سررسید بعد از تاریخ پرداخت که هنوز نگذشته؛ برای ثبت هزینه‌ای که چند ماه پیش شروع شده.
    /// مثال: پرداخت ماهانه ۱۴۰۵/۰۱/۰۵ و امروز ۱۴۰۵/۰۴/۲۰ → ۱۴۰۵/۰۵/۰۵ (دوره‌های گذشته دستی ثبت می‌شوند).
    /// </summary>
    public static DateOnly? NextDueAfter(DateOnly spentOn, ExpenseRecurrence recurrence, DateOnly today)
    {
        var next = ExpenseRules.NextDue(spentOn, recurrence);
        // سقف ۶۰۰ دور برای اطمینان از پایان حلقه.
        for (var i = 0; next is { } due && due < today && i < 600; i++)
            next = ExpenseRules.NextDue(due, recurrence);
        return next;
    }

    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var expense = await context.Expenses.AsTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (expense is null)
            return OperationResult.Error("هزینه پیدا نشد.");
        context.Expenses.Remove(expense);
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success("هزینه حذف شد.");
    }

    public async Task<OperationResult<Guid>> RecordNextAsync(Guid id, decimal? amount, Guid? userId, string? userName, CancellationToken cancellationToken = default)
    {
        var parent = await context.Expenses.AsTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (parent is null || parent.Recurrence == ExpenseRecurrence.None || parent.NextDueOn is not { } due)
            return OperationResult<Guid>.Error("این هزینه تکراری فعال نیست.");
        var value = amount ?? parent.Amount;
        if (value <= 0 || decimal.Truncate(value) != value)
            return OperationResult<Guid>.Error("مبلغ نامعتبر است.");

        var child = new Expense
        {
            Title = parent.Title,
            Category = parent.Category,
            Amount = value,
            SpentOn = due,
            Vendor = parent.Vendor,
            Note = parent.Note,
            Recurrence = ExpenseRecurrence.None,
            RecurringParentId = parent.Id,
            CreatedByUserId = userId,
            CreatedByName = userName,
        };
        context.Expenses.Add(child);
        parent.NextDueOn = ExpenseRules.NextDue(due, parent.Recurrence);
        parent.LastReminderOn = null;
        if (amount is not null)
            parent.Amount = value; // مبلغ دوره‌های بعدی هم به‌روز می‌شود (مثلاً تمدید با قیمت تازه).
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult<Guid>.Success(child.Id, $"پرداخت دورهٔ {PersianDateText(due)} ثبت شد؛ سررسید بعدی {PersianDateText(parent.NextDueOn!.Value)}.");
    }

    public async Task<OperationResult> StopRecurrenceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var changed = await context.Expenses.Where(e => e.Id == id && e.NextDueOn != null)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.NextDueOn, (DateOnly?)null), cancellationToken);
        return changed == 0 ? OperationResult.Error("هزینهٔ تکراری فعال پیدا نشد.") : OperationResult.Success("تکرار این هزینه متوقف شد.");
    }

    public async Task<int> NotifyDueAsync(DateOnly today, int daysAhead = 3, CancellationToken cancellationToken = default)
    {
        var limit = today.AddDays(daysAhead);
        var due = await context.Expenses.AsTracking()
            .Where(e => e.NextDueOn != null && e.NextDueOn <= limit && (e.LastReminderOn == null || e.LastReminderOn < today))
            .OrderBy(e => e.NextDueOn).Take(50).ToListAsync(cancellationToken);
        foreach (var expense in due)
        {
            var date = expense.NextDueOn!.Value;
            var dueText = date < today ? $"از {PersianDateText(date)} گذشته است" : date == today ? "امروز است" : $"{PersianDateText(date)} است";
            await notifications.NotifyAsync(new StaffAlert(StaffNotificationKind.ExpenseDue,
                $"سررسید هزینه: {expense.Title}",
                $"{ExpenseRules.CategoryTitle(expense.Category)} · {expense.Amount.ToString("#,0", CultureInfo.InvariantCulture)} تومان · سررسید {dueText}.",
                "/admin/finance/expenses?tab=recurring",
                [AdminPermissions.ExpensesManage, AdminPermissions.FinanceView]));
            expense.LastReminderOn = today;
        }
        if (due.Count > 0)
            await context.SaveChangesAsync(cancellationToken);
        return due.Count;
    }

    private static string PersianDateText(DateOnly date)
    {
        var d = date.ToDateTime(TimeOnly.MinValue);
        return $"{Persian.GetYear(d):0000}/{Persian.GetMonth(d):00}/{Persian.GetDayOfMonth(d):00}";
    }
}

/// <summary>
/// ابزار ماه‌های شمسی برای هدف فروش و نمودار ماهانه. مثال: Of(2026-10-26) → (1405, 8)؛ Range(1405, 8) → [۱ آبان، ۱ آذر).
/// </summary>
public static class JalaliMonth
{
    private static readonly PersianCalendar Persian = new();

    public static readonly string[] Names = ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];

    public static (int Year, int Month) Of(DateOnly day)
    {
        var d = day.ToDateTime(TimeOnly.MinValue);
        return (Persian.GetYear(d), Persian.GetMonth(d));
    }

    /// <summary>بازهٔ روزهای ماه [شروع، شروع ماه بعد).</summary>
    public static (DateOnly Start, DateOnly End) Range(int year, int month)
    {
        var start = DateOnly.FromDateTime(Persian.ToDateTime(year, month, 1, 0, 0, 0, 0));
        var (nextYear, nextMonth) = Add(year, month, 1);
        var end = DateOnly.FromDateTime(Persian.ToDateTime(nextYear, nextMonth, 1, 0, 0, 0, 0));
        return (start, end);
    }

    /// <summary>جمع/تفریق ماه. مثال: Add(1405, 12, 1) → (1406, 1)؛ Add(1405, 1, -1) → (1404, 12).</summary>
    public static (int Year, int Month) Add(int year, int month, int months)
    {
        var index = year * 12 + (month - 1) + months;
        return (index / 12, index % 12 + 1);
    }

    public static string Title(int year, int month) => $"{Names[Math.Clamp(month, 1, 12) - 1]} {year}";

    public static bool IsValid(int year, int month) => year is >= 1300 and <= 1500 && month is >= 1 and <= 12;
}
