using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Insights;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Insights;

// ═══════════════════════════════════════════════════════════════════════════
//  هدف فروش ماهانه (مرحلهٔ هفتم – فاز ۷ب)
//  مدیر برای هر ماه شمسی هدف مبلغ (و اختیاری تعداد سفارش) تعیین می‌کند؛ سیستم پیشرفت، پیش‌بینی پایان ماه
//  و «فروش لازم در هر روز باقی‌مانده» را حساب می‌کند.
//  فروش = جمع (مبلغ قابل پرداخت − مبلغ برگشتی) سفارش‌های قطعی (پرداخت‌شده یا تحویل‌شده، نه لغو/مرجوع) – همان قاعدهٔ گزارش مالی.
//  نمایش: ویجت داشبورد، صفحهٔ /admin/reports/goals و گزارش خودکار.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>وضعیت هدف یک ماه.</summary>
public sealed class GoalProgress
{
    public int Year { get; init; }
    public int Month { get; init; }
    public string Title => JalaliMonth.Title(Year, Month);

    /// <summary>null یعنی برای این ماه هدفی ثبت نشده (فقط فروش نمایش داده می‌شود).</summary>
    public decimal? TargetAmount { get; init; }
    public int? TargetOrders { get; init; }
    public string? Note { get; init; }

    public decimal Achieved { get; init; }
    public int Orders { get; init; }

    public int DaysInMonth { get; init; }

    /// <summary>روزهای سپری‌شده شامل امروز (برای ماه گذشته = همهٔ روزها، ماه آینده = ۰).</summary>
    public int DaysPassed { get; init; }

    public GoalForecast Forecast { get; init; } = GoalForecast.Empty;

    public bool HasTarget => TargetAmount is > 0;
    public int DaysLeft => Math.Max(0, DaysInMonth - DaysPassed);
}

/// <param name="Percent">درصد تحقق مبلغ (ممکن است بیش از ۱۰۰ باشد).</param>
/// <param name="Projected">پیش‌بینی فروش پایان ماه با سرعت فعلی.</param>
/// <param name="RequiredPerDay">فروش لازم در هر روز باقی‌مانده برای رسیدن به هدف (۰ اگر رسیده‌ایم).</param>
/// <param name="ExpectedByNow">فروش مورد انتظار تا امروز اگر هدف یکنواخت پخش شود.</param>
/// <param name="Status">«ahead» جلوتر از برنامه، «on-track» نزدیک برنامه (±۵٪)، «behind» عقب، «done» رسیده، «none» بدون هدف.</param>
public sealed record GoalForecast(decimal Percent, decimal Projected, decimal RequiredPerDay, decimal ExpectedByNow, string Status)
{
    public static readonly GoalForecast Empty = new(0, 0, 0, 0, "none");
}

/// <summary>قواعد خالص پیش‌بینی (بدون دیتابیس؛ تست‌پذیر).</summary>
public static class SalesGoalRules
{
    /// <summary>
    /// مثال: هدف ۳۰۰ میلیون، ماه ۳۰ روزه، ۱۰ روز گذشته، فروش ۱۲۰ میلیون → ۴۰٪، پیش‌بینی ۳۶۰ میلیون،
    /// لازم روزانه (۳۰۰−۱۲۰)/۲۰ = ۹ میلیون، انتظار تا امروز ۱۰۰ میلیون → «ahead».
    /// </summary>
    public static GoalForecast Compute(decimal? target, decimal achieved, int daysPassed, int daysInMonth)
    {
        daysInMonth = Math.Max(1, daysInMonth);
        daysPassed = Math.Clamp(daysPassed, 0, daysInMonth);
        var projected = daysPassed == 0 ? achieved : Math.Round(achieved / daysPassed * daysInMonth);
        if (target is not > 0)
            return GoalForecast.Empty with { Projected = projected };

        var goal = target.Value;
        var percent = Math.Round(achieved * 100 / goal, 1);
        var daysLeft = daysInMonth - daysPassed;
        var remaining = Math.Max(0, goal - achieved);
        var requiredPerDay = remaining == 0 ? 0 : daysLeft == 0 ? remaining : Math.Ceiling(remaining / daysLeft);
        var expected = Math.Round(goal * daysPassed / daysInMonth);

        string status;
        if (achieved >= goal)
            status = "done";
        else if (daysPassed == 0)
            status = "on-track";
        else if (achieved >= expected * 1.05m)
            status = "ahead";
        else if (achieved >= expected * 0.95m)
            status = "on-track";
        else
            status = "behind";
        return new GoalForecast(percent, projected, requiredPerDay, expected, status);
    }

    public static string StatusTitle(string status) => status switch
    {
        "done" => "هدف محقق شد",
        "ahead" => "جلوتر از برنامه",
        "on-track" => "مطابق برنامه",
        "behind" => "عقب‌تر از برنامه",
        _ => "بدون هدف",
    };

    /// <summary>روزهای سپری‌شدهٔ ماه (شامل امروز). ماه گذشته = کل ماه؛ ماه آینده = ۰.</summary>
    public static int DaysPassed(DateOnly monthStart, DateOnly monthEnd, DateOnly today)
    {
        if (today < monthStart)
            return 0;
        if (today >= monthEnd)
            return monthEnd.DayNumber - monthStart.DayNumber;
        return today.DayNumber - monthStart.DayNumber + 1;
    }
}

/// <summary>ورودی فرم هدف.</summary>
public sealed record SalesGoalInput(int Year, int Month, decimal TargetAmount, int? TargetOrders, string? Note);

public interface ISalesGoalService
{
    /// <summary>وضعیت هدف یک ماه (null = ماه جاری).</summary>
    Task<GoalProgress> GetProgressAsync(int? year = null, int? month = null, CancellationToken cancellationToken = default);

    /// <summary>تاریخچهٔ <paramref name="months"/> ماه اخیر (جدیدترین اول) شامل ماه جاری.</summary>
    Task<List<GoalProgress>> GetHistoryAsync(int months = 12, CancellationToken cancellationToken = default);

    /// <summary>ثبت یا ویرایش هدف (برای هر ماه یک ردیف).</summary>
    Task<OperationResult> SaveAsync(SalesGoalInput input, string? userName, CancellationToken cancellationToken = default);

    Task<OperationResult> DeleteAsync(int year, int month, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public class SalesGoalService(FratakShopDbContext context) : ISalesGoalService
{
    public async Task<GoalProgress> GetProgressAsync(int? year = null, int? month = null, CancellationToken cancellationToken = default)
    {
        var today = IranClock.Today(DateTimeOffset.UtcNow);
        var current = JalaliMonth.Of(today);
        var y = year ?? current.Year;
        var m = month ?? current.Month;
        if (!JalaliMonth.IsValid(y, m))
            (y, m) = current;

        var goal = await context.SalesGoals.AsNoTracking().FirstOrDefaultAsync(g => g.Year == y && g.Month == m, cancellationToken);
        var sales = await SalesAsync(y, m, cancellationToken);
        return Build(y, m, goal, sales.Amount, sales.Count, today);
    }

    public async Task<List<GoalProgress>> GetHistoryAsync(int months = 12, CancellationToken cancellationToken = default)
    {
        months = Math.Clamp(months, 1, 36);
        var today = IranClock.Today(DateTimeOffset.UtcNow);
        var (year, month) = JalaliMonth.Of(today);
        var list = Enumerable.Range(0, months).Select(i => JalaliMonth.Add(year, month, -i)).ToList();
        var oldest = list[^1];
        var start = IranClock.StartOfDay(JalaliMonth.Range(oldest.Year, oldest.Month).Start);

        // یک کوئری برای همهٔ ماه‌ها: فقط تاریخ و مبلغ خالص سفارش‌ها.
        var orders = await Realized(start, null).Select(o => new { o.CreatedAt, Net = o.PayableAmount - o.RefundedAmount })
            .ToListAsync(cancellationToken);
        var minIndex = oldest.Year * 12 + oldest.Month;
        var goals = await context.SalesGoals.AsNoTracking().Where(g => g.Year * 12 + g.Month >= minIndex).ToListAsync(cancellationToken);

        return list.Select(item =>
        {
            var (from, to) = JalaliMonth.Range(item.Year, item.Month);
            var inMonth = orders.Where(o => IranClock.Today(o.CreatedAt) is var d && d >= from && d < to).ToList();
            var goal = goals.FirstOrDefault(g => g.Year == item.Year && g.Month == item.Month);
            return Build(item.Year, item.Month, goal, inMonth.Sum(o => o.Net), inMonth.Count, today);
        }).ToList();
    }

    public async Task<OperationResult> SaveAsync(SalesGoalInput input, string? userName, CancellationToken cancellationToken = default)
    {
        if (!JalaliMonth.IsValid(input.Year, input.Month))
            return OperationResult.Error("ماه انتخاب‌شده نامعتبر است.");
        if (input.TargetAmount is <= 0 or > 1_000_000_000_000m || decimal.Truncate(input.TargetAmount) != input.TargetAmount)
            return OperationResult.Error("مبلغ هدف باید عدد صحیح و بیشتر از صفر (تومان) باشد.");
        if (input.TargetOrders is < 1 or > 1_000_000)
            return OperationResult.Error("هدف تعداد سفارش نامعتبر است.");
        if (input.Note?.Trim().Length > 300)
            return OperationResult.Error("توضیح حداکثر ۳۰۰ حرف.");

        var goal = await context.SalesGoals.AsTracking().FirstOrDefaultAsync(g => g.Year == input.Year && g.Month == input.Month, cancellationToken);
        if (goal is null)
        {
            goal = new SalesGoal { Year = input.Year, Month = input.Month };
            context.SalesGoals.Add(goal);
        }
        goal.TargetAmount = input.TargetAmount;
        goal.TargetOrders = input.TargetOrders;
        goal.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        goal.SetByName = userName;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // دو نفر هم‌زمان برای یک ماه هدف ثبت کرده‌اند (ایندکس یکتا).
            context.ChangeTracker.Clear();
            return OperationResult.Error("هم‌زمان هدف دیگری برای این ماه ثبت شد؛ صفحه را تازه کنید.");
        }
        return OperationResult.Success($"هدف {JalaliMonth.Title(input.Year, input.Month)} ذخیره شد.");
    }

    public async Task<OperationResult> DeleteAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var goal = await context.SalesGoals.AsTracking().FirstOrDefaultAsync(g => g.Year == year && g.Month == month, cancellationToken);
        if (goal is null)
            return OperationResult.Error("هدفی برای این ماه ثبت نشده است.");
        context.SalesGoals.Remove(goal);
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success("هدف حذف شد.");
    }

    private IQueryable<Order> Realized(DateTimeOffset from, DateTimeOffset? to)
    {
        var query = context.Orders.AsNoTracking().Where(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Returned
                                                             && (o.PaymentStatus == PaymentStatus.Paid || o.Status == OrderStatus.Delivered)
                                                             && o.CreatedAt >= from);
        if (to is { } end)
            query = query.Where(o => o.CreatedAt < end);
        return query;
    }

    private async Task<(decimal Amount, int Count)> SalesAsync(int year, int month, CancellationToken cancellationToken)
    {
        var (start, end) = JalaliMonth.Range(year, month);
        var query = Realized(IranClock.StartOfDay(start), IranClock.StartOfDay(end));
        var amount = await query.SumAsync(o => (decimal?)(o.PayableAmount - o.RefundedAmount), cancellationToken) ?? 0;
        var count = await query.CountAsync(cancellationToken);
        return (amount, count);
    }

    private static GoalProgress Build(int year, int month, SalesGoal? goal, decimal achieved, int orders, DateOnly today)
    {
        var (start, end) = JalaliMonth.Range(year, month);
        var daysInMonth = end.DayNumber - start.DayNumber;
        var passed = SalesGoalRules.DaysPassed(start, end, today);
        return new GoalProgress
        {
            Year = year,
            Month = month,
            TargetAmount = goal?.TargetAmount,
            TargetOrders = goal?.TargetOrders,
            Note = goal?.Note,
            Achieved = achieved,
            Orders = orders,
            DaysInMonth = daysInMonth,
            DaysPassed = passed,
            Forecast = SalesGoalRules.Compute(goal?.TargetAmount, achieved, passed, daysInMonth),
        };
    }
}
