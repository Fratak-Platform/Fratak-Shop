using System.Net;
using System.Text;
using Fratak_Shop.Core.DTOs.Reports;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Reports;
using Fratak_Shop.Core.Services.Security;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Insights;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Insights;

// ═══════════════════════════════════════════════════════════════════════════
//  گزارش‌های زمان‌بندی‌شده (مرحلهٔ هفتم – فاز ۷ب)
//  مدیر چند گزارش تعریف می‌کند (مثلاً «خلاصهٔ روزانه» ساعت ۸ صبح به ایمیل و گروه بله)؛
//  سرویس پس‌زمینه (Web/Infrastructure/Insights/InsightsWorker) هر ۵ دقیقه RunDueAsync را صدا می‌زند.
//  ارسال واقعی (SMTP و API ربات‌ها) در لایهٔ Web پیاده شده: IReportDelivery.
//  صفحه: /admin/reports/scheduled
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>نتیجهٔ یک تلاش ارسال. Message برای نمایش به مدیر است (بدون توکن یا رمز).</summary>
public sealed record DeliveryAttempt(bool Ok, string Message);

/// <summary>تنظیمات SMTP رمزگشایی‌شده که فقط در لحظهٔ ارسال ساخته می‌شود.</summary>
public sealed record SmtpSettings(string Host, int Port, string? UserName, string? Password, string FromAddress, string? FromName);

/// <summary>
/// ارسال واقعی پیام. پیاده‌سازی: Web/Infrastructure/Insights/ReportDelivery (MailKit و HttpClient).
/// هرگز خطا پرتاب نمی‌کند؛ همیشه DeliveryAttempt برمی‌گرداند.
/// </summary>
public interface IReportDelivery
{
    Task<DeliveryAttempt> SendEmailAsync(SmtpSettings smtp, IReadOnlyList<string> to, string subject, string html, string text, CancellationToken cancellationToken);

    /// <param name="channel">فقط Bale یا Telegram.</param>
    Task<DeliveryAttempt> SendBotMessageAsync(ReportChannels channel, string token, string chatId, string text, CancellationToken cancellationToken);
}

/// <summary>یک خط گزارش. مثال: («فروش خالص»، «۱۲٬۰۰۰٬۰۰۰ تومان»، «▲ ۱۵٪»).</summary>
public sealed record DigestLine(string Label, string Value, string? Note = null);

public sealed record DigestSection(string Title, List<DigestLine> Lines);

/// <summary>محتوای آمادهٔ گزارش که هم به متن ساده (ربات) و هم HTML (ایمیل) تبدیل می‌شود.</summary>
public sealed class ReportDigest
{
    public string Title { get; init; } = "";
    public string ShopName { get; init; } = "";
    public string PeriodLabel { get; init; } = "";
    public DateTimeOffset GeneratedAt { get; init; }
    public List<DigestSection> Sections { get; init; } = [];

    /// <summary>متن ساده برای بله/تلگرام (بدون Markdown تا نویسه‌های خاص خطا ندهند).</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        sb.Append("📊 ").Append(Title).Append(" – ").AppendLine(ShopName);
        sb.Append("🗓 ").AppendLine(PeriodLabel);
        foreach (var section in Sections)
        {
            sb.AppendLine();
            sb.Append("▪️ ").AppendLine(section.Title);
            foreach (var line in section.Lines)
            {
                sb.Append("• ").Append(line.Label).Append(": ").Append(line.Value);
                if (!string.IsNullOrEmpty(line.Note))
                    sb.Append(" (").Append(line.Note).Append(')');
                sb.AppendLine();
            }
        }
        sb.AppendLine();
        sb.Append("ساخته‌شده در ").Append(ScheduledReportRules.PersianDigits(PersianDate.Format(GeneratedAt)));
        var text = sb.ToString();
        return text.Length <= ScheduledReportRules.MaxBotMessageLength ? text : text[..(ScheduledReportRules.MaxBotMessageLength - 1)] + "…";
    }

    /// <summary>HTML ساده و راست‌به‌چپ برای ایمیل (استایل درون‌خطی چون کلاینت‌های ایمیل CSS خارجی را نمی‌خوانند). همهٔ متن‌ها encode می‌شوند.</summary>
    public string ToHtml()
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\"><title>").Append(E(Title)).Append("</title></head>");
        sb.Append("<body style=\"margin:0;background:#f4f5f7;font-family:Tahoma,Vazirmatn,Arial,sans-serif;color:#1f2937\">");
        sb.Append("<div style=\"max-width:640px;margin:0 auto;padding:24px 12px\">");
        sb.Append("<div style=\"background:#111827;color:#fff;border-radius:12px 12px 0 0;padding:18px 20px\">");
        sb.Append("<div style=\"font-size:18px;font-weight:bold\">").Append(E(Title)).Append("</div>");
        sb.Append("<div style=\"font-size:13px;opacity:.8;margin-top:4px\">").Append(E(ShopName)).Append(" · ").Append(E(PeriodLabel)).Append("</div></div>");
        sb.Append("<div style=\"background:#fff;border-radius:0 0 12px 12px;padding:8px 20px 20px\">");
        foreach (var section in Sections)
        {
            sb.Append("<h3 style=\"font-size:15px;margin:20px 0 8px;color:#111827;border-bottom:1px solid #e5e7eb;padding-bottom:6px\">").Append(E(section.Title)).Append("</h3>");
            sb.Append("<table role=\"presentation\" style=\"width:100%;border-collapse:collapse;font-size:13px\">");
            foreach (var line in section.Lines)
            {
                sb.Append("<tr><td style=\"padding:6px 0;color:#6b7280\">").Append(E(line.Label)).Append("</td>");
                sb.Append("<td style=\"padding:6px 0;text-align:left;font-weight:bold\">").Append(E(line.Value));
                if (!string.IsNullOrEmpty(line.Note))
                    sb.Append(" <span style=\"font-weight:normal;color:#6b7280\">(").Append(E(line.Note)).Append(")</span>");
                sb.Append("</td></tr>");
            }
            sb.Append("</table>");
        }
        sb.Append("<p style=\"font-size:11px;color:#9ca3af;margin-top:24px\">این گزارش به‌طور خودکار از پنل مدیریت ساخته شده است · ")
            .Append(E(ScheduledReportRules.PersianDigits(PersianDate.Format(GeneratedAt)))).Append("</p>");
        sb.Append("</div></div></body></html>");
        return sb.ToString();
    }
}

/// <summary>ورودی فرم گزارش. Id خالی = گزارش تازه.</summary>
public sealed record ScheduledReportInput(Guid? Id, string Title, ReportFrequency Frequency, int Hour, int WeekDay, ReportChannels Channels,
    ReportSections Sections, string? EmailTo, string? BaleChatId, string? TelegramChatId, bool IsActive);

public sealed record ScheduledReportRow(Guid Id, string Title, ReportFrequency Frequency, byte Hour, byte WeekDay, ReportChannels Channels,
    ReportSections Sections, string? EmailTo, string? BaleChatId, string? TelegramChatId, bool IsActive, DateTimeOffset? NextRunAt,
    DateTimeOffset? LastRunAt, bool? LastSucceeded, string? LastStatus);

/// <summary>نمای فرم تنظیمات کانال‌ها؛ رمز و توکن‌ها هرگز برگردانده نمی‌شوند (فقط ثبت‌شده/نشده).</summary>
public sealed record ReportDeliveryView(string? SmtpHost, int SmtpPort, string? SmtpUserName, string? FromAddress, string? FromName,
    bool HasSmtpPassword, bool HasBaleToken, bool HasTelegramToken)
{
    public bool EmailReady => !string.IsNullOrWhiteSpace(SmtpHost) && !string.IsNullOrWhiteSpace(FromAddress);
}

/// <summary>
/// ورودی ذخیرهٔ تنظیمات کانال‌ها. فیلد رمز/توکن خالی یعنی «بدون تغییر»؛ برای پاک کردن، Clear… را true کنید.
/// </summary>
public sealed record ReportDeliveryInput(string? SmtpHost, int SmtpPort, string? SmtpUserName, string? SmtpPassword, bool ClearSmtpPassword,
    string? FromAddress, string? FromName, string? BaleToken, bool ClearBaleToken, string? TelegramToken, bool ClearTelegramToken);

public interface IScheduledReportService
{
    Task<List<ScheduledReportRow>> ListAsync(CancellationToken cancellationToken = default);

    Task<ScheduledReportRow?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<OperationResult<Guid>> SaveAsync(ScheduledReportInput input, Guid? userId, CancellationToken cancellationToken = default);

    Task<OperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<OperationResult> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default);

    /// <summary>ساخت محتوای گزارش (برای پیش‌نمایش در صفحه و برای ارسال).</summary>
    Task<ReportDigest> BuildDigestAsync(string title, ReportFrequency frequency, ReportSections sections, DateTimeOffset runAt, CancellationToken cancellationToken = default);

    /// <summary>ارسال فوری (دکمهٔ «ارسال آزمایشی»)؛ زمان اجرای بعدی تغییر نمی‌کند ولی وضعیت آخرین ارسال ثبت می‌شود.</summary>
    Task<OperationResult> SendNowAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>اجرای گزارش‌های سررسیده؛ سرویس پس‌زمینه هر ۵ دقیقه صدا می‌زند. خروجی: تعداد گزارش اجراشده.</summary>
    Task<int> RunDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<ReportDeliveryView> GetDeliveryAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> SaveDeliveryAsync(ReportDeliveryInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// ارسال یک هشدار فنی کوتاه به گیرنده‌های داده‌شده با همان SMTP/ربات‌های گزارش خودکار (مرحلهٔ هفتم – فاز ۹).
    /// خروجی: خلاصهٔ نتیجه برای نمایش. مثال: «ایمیل: ارسال شد · بله: توکن ربات ثبت نشده».
    /// </summary>
    Task<string> SendAlertAsync(string title, string body, string? emailTo, string? baleChatId, string? telegramChatId, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public class ScheduledReportService(
    FratakShopDbContext context,
    IAdminReportService adminReports,
    IAnalyticsReportService analytics,
    IExpenseService expenses,
    ISalesGoalService goals,
    IReportDelivery delivery,
    ISecretProtector secrets,
    IStaffNotificationService notifications,
    Fratak_Shop.Core.Services.Settings.IStoreSettingService settings,
    ILogger<ScheduledReportService> logger) : IScheduledReportService
{
    /// <summary>در هر دور حداکثر این تعداد گزارش اجرا می‌شود (بقیه در دور بعد).</summary>
    private const int MaxPerRun = 10;

    private static readonly System.Linq.Expressions.Expression<Func<ScheduledReport, ScheduledReportRow>> ToRow = r =>
        new ScheduledReportRow(r.Id, r.Title, r.Frequency, r.Hour, r.WeekDay, r.Channels, r.Sections, r.EmailTo, r.BaleChatId, r.TelegramChatId,
            r.IsActive, r.NextRunAt, r.LastRunAt, r.LastSucceeded, r.LastStatus);

    public Task<List<ScheduledReportRow>> ListAsync(CancellationToken cancellationToken = default) =>
        context.ScheduledReports.AsNoTracking().OrderBy(r => r.Title).Select(ToRow).ToListAsync(cancellationToken);

    public Task<ScheduledReportRow?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.ScheduledReports.AsNoTracking().Where(r => r.Id == id).Select(ToRow).FirstOrDefaultAsync(cancellationToken);

    public async Task<OperationResult<Guid>> SaveAsync(ScheduledReportInput input, Guid? userId, CancellationToken cancellationToken = default)
    {
        if (ScheduledReportRules.Validate(input) is { } error)
            return OperationResult<Guid>.Error(error);

        ScheduledReport report;
        if (input.Id is { } id)
        {
            var existing = await context.ScheduledReports.AsTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (existing is null)
                return OperationResult<Guid>.NotFound("گزارش پیدا نشد.");
            report = existing;
        }
        else
        {
            if (await context.ScheduledReports.CountAsync(cancellationToken) >= 30)
                return OperationResult<Guid>.Error("حداکثر ۳۰ گزارش زمان‌بندی‌شده مجاز است.");
            report = new ScheduledReport { CreatedByUserId = userId };
            context.ScheduledReports.Add(report);
        }

        report.Title = input.Title.Trim();
        report.Frequency = input.Frequency;
        report.Hour = (byte)input.Hour;
        report.WeekDay = (byte)input.WeekDay;
        report.Channels = input.Channels & (ReportChannels.Email | ReportChannels.Bale | ReportChannels.Telegram);
        report.Sections = input.Sections & ReportSections.All;
        // فقط مقدار کانال‌های انتخاب‌شده نگه داشته می‌شود.
        report.EmailTo = report.Channels.HasFlag(ReportChannels.Email) ? string.Join(", ", ScheduledReportRules.ParseEmails(input.EmailTo).Valid) : null;
        report.BaleChatId = report.Channels.HasFlag(ReportChannels.Bale) ? input.BaleChatId?.Trim() : null;
        report.TelegramChatId = report.Channels.HasFlag(ReportChannels.Telegram) ? input.TelegramChatId?.Trim() : null;
        report.IsActive = input.IsActive;
        report.NextRunAt = input.IsActive ? ScheduledReportRules.NextRun(report.Frequency, report.Hour, report.WeekDay, DateTimeOffset.UtcNow) : null;

        await context.SaveChangesAsync(cancellationToken);
        return OperationResult<Guid>.Success(report.Id, "گزارش ذخیره شد.");
    }

    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var report = await context.ScheduledReports.AsTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (report is null)
            return OperationResult.NotFound("گزارش پیدا نشد.");
        context.ScheduledReports.Remove(report);
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success("گزارش حذف شد.");
    }

    public async Task<OperationResult> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var report = await context.ScheduledReports.AsTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (report is null)
            return OperationResult.NotFound("گزارش پیدا نشد.");
        report.IsActive = active;
        report.NextRunAt = active ? ScheduledReportRules.NextRun(report.Frequency, report.Hour, report.WeekDay, DateTimeOffset.UtcNow) : null;
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success(active ? "گزارش فعال شد." : "گزارش غیرفعال شد.");
    }

    public async Task<ReportDigest> BuildDigestAsync(string title, ReportFrequency frequency, ReportSections sections, DateTimeOffset runAt, CancellationToken cancellationToken = default)
    {
        var (current, previous, label) = ScheduledReportRules.Window(frequency, runAt);
        var shop = (await settings.GetAsync(cancellationToken)).SiteName;
        var list = new List<DigestSection>();
        var N = ScheduledReportRules.Number;
        var T = ScheduledReportRules.Toman;

        // داده‌های مشترک فقط یک بار خوانده می‌شوند.
        Fratak_Shop.Core.DTOs.Admin.SalesReportDto? sales = null;
        if ((sections & (ReportSections.Sales | ReportSections.TopProducts)) != 0)
            sales = await adminReports.GetSalesAsync(current, cancellationToken);
        Fratak_Shop.Core.DTOs.Admin.DashboardDto? dashboard = null;
        if ((sections & (ReportSections.Orders | ReportSections.Stock | ReportSections.Support)) != 0)
            dashboard = await adminReports.GetDashboardAsync(cancellationToken);

        if (sections.HasFlag(ReportSections.Sales) && sales is not null)
        {
            var before = await adminReports.GetSalesAsync(previous, cancellationToken);
            list.Add(new DigestSection("فروش", [
                new("فروش خالص", T(sales.NetSales), ScheduledReportRules.Change(sales.NetSales, before.NetSales)),
                new("سفارش قطعی", N(sales.OrderCount), ScheduledReportRules.Change(sales.OrderCount, before.OrderCount)),
                new("میانگین سبد", T(sales.AverageOrderValue)),
                new("مشتری جدید", N(sales.NewCustomers)),
                new("لغو / مرجوعی", $"{N(sales.CancelledCount)} / {N(sales.ReturnedCount)}"),
            ]));
        }

        if (sections.HasFlag(ReportSections.Orders) && dashboard is not null)
        {
            list.Add(new DigestSection("کارهای مانده (اکنون)", [
                new("سفارش منتظر تأیید", N(dashboard.OrdersToConfirm)),
                new("منتظر فاکتور خرید", N(dashboard.OrdersAwaitingInvoice)),
                new("آمادهٔ ارسال", N(dashboard.OrdersToShip)),
                new("درخواست برداشت", N(dashboard.PendingWithdrawals)),
            ]));
        }

        if (sections.HasFlag(ReportSections.Visitors))
        {
            var funnel = await analytics.GetFunnelAsync(current, cancellationToken);
            var lines = new List<DigestLine>
            {
                new("بازدیدکنندهٔ یکتا", N(funnel.Visitors)),
                new("بازدید صفحه", N(funnel.PageViews)),
            };
            if (funnel.Steps.Count > 0 && funnel.Visitors > 0)
            {
                var last = funnel.Steps[^1];
                lines.Add(new("نرخ تبدیل", ScheduledReportRules.PersianDigits(last.RateFromStart.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)) + "٪"));
            }
            if (funnel.Sources.OrderByDescending(s => s.Visitors).FirstOrDefault() is { } top)
                lines.Add(new("منبع برتر", top.SourceTitle, N(top.Visitors) + " بازدیدکننده"));
            list.Add(new DigestSection("بازدید", lines));
        }

        if (sections.HasFlag(ReportSections.TopProducts) && sales is not null)
        {
            var lines = sales.TopProducts.Take(5).Select((p, i) => new DigestLine($"{ScheduledReportRules.PersianDigits((i + 1).ToString())}. {p.Name}", T(p.Amount), N(p.Count) + " عدد")).ToList();
            if (lines.Count == 0)
                lines.Add(new DigestLine("—", "فروشی در این بازه ثبت نشد"));
            list.Add(new DigestSection("پرفروش‌ترین‌ها", lines));
        }

        if (sections.HasFlag(ReportSections.Stock) && dashboard is not null)
        {
            var lines = new List<DigestLine>
            {
                new("ناموجود", N(dashboard.OutOfStock)),
                new("رو به اتمام", N(dashboard.LowStock)),
            };
            lines.AddRange(dashboard.MostWantedOutOfStock.Take(3).Select(w => new DigestLine("پرتقاضای ناموجود", w.Name, N(w.Count) + " درخواست")));
            list.Add(new DigestSection("موجودی", lines));
        }

        if (sections.HasFlag(ReportSections.Expenses))
        {
            var spent = await expenses.GetTotalAsync(current, cancellationToken);
            var purchases = await adminReports.GetPurchasesAsync(current, cancellationToken);
            list.Add(new DigestSection("هزینه و سود", [
                new("سود ناخالص واقعی", T(purchases.ActualGrossProfit), "سفارش‌های دارای فاکتور خرید"),
                new("هزینه‌های جاری", T(spent)),
                new("سود خالص", T(purchases.ActualGrossProfit - spent)),
            ]));
        }

        if (sections.HasFlag(ReportSections.Goal))
        {
            var goal = await goals.GetProgressAsync(cancellationToken: cancellationToken);
            var lines = new List<DigestLine> { new("فروش ماه", T(goal.Achieved), goal.Title) };
            if (goal.HasTarget)
            {
                lines.Add(new("هدف", T(goal.TargetAmount!.Value), ScheduledReportRules.PersianDigits(goal.Forecast.Percent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)) + "٪ محقق"));
                lines.Add(new("پیش‌بینی پایان ماه", T(goal.Forecast.Projected), SalesGoalRules.StatusTitle(goal.Forecast.Status)));
                if (goal.Forecast.RequiredPerDay > 0)
                    lines.Add(new("فروش لازم در هر روز", T(goal.Forecast.RequiredPerDay), N(goal.DaysLeft) + " روز مانده"));
            }
            else
            {
                lines.Add(new("هدف", "برای این ماه هدفی ثبت نشده"));
            }
            list.Add(new DigestSection("هدف فروش", lines));
        }

        if (sections.HasFlag(ReportSections.Support) && dashboard is not null)
        {
            list.Add(new DigestSection("پشتیبانی (اکنون)", [
                new("تیکت باز", N(dashboard.OpenTickets)),
                new("مرجوعی در جریان", N(dashboard.PendingReturns)),
                new("پیام تماس جدید", N(dashboard.NewMessages)),
                new("دیدگاه منتظر تأیید", N(dashboard.PendingComments)),
            ]));
        }

        return new ReportDigest
        {
            Title = title,
            ShopName = shop,
            PeriodLabel = $"{ScheduledReportRules.FrequencyTitle(frequency)} · {label}",
            GeneratedAt = runAt,
            Sections = list,
        };
    }

    public async Task<OperationResult> SendNowAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var report = await context.ScheduledReports.AsTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (report is null)
            return OperationResult.NotFound("گزارش پیدا نشد.");
        var (ok, status) = await ExecuteAsync(report, DateTimeOffset.UtcNow, cancellationToken);
        report.LastRunAt = DateTimeOffset.UtcNow;
        report.LastSucceeded = ok;
        report.LastStatus = Truncate(status, 1000);
        await context.SaveChangesAsync(cancellationToken);
        return ok ? OperationResult.Success("ارسال شد: " + status) : OperationResult.Error("ارسال ناموفق: " + status);
    }

    public async Task<int> RunDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var due = await context.ScheduledReports.AsNoTracking()
            .Where(r => r.IsActive && r.NextRunAt != null && r.NextRunAt <= now)
            .OrderBy(r => r.NextRunAt).Take(MaxPerRun)
            .Select(r => new { r.Id, r.NextRunAt, r.Frequency, r.Hour, r.WeekDay })
            .ToListAsync(cancellationToken);

        var executed = 0;
        foreach (var item in due)
        {
            // «رزرو» اجرا: زمان بعدی را قبل از ارسال جلو می‌بریم و فقط اگر کسی پیش‌تر برنداشته باشد (چند نمونهٔ سرور هم‌زمان).
            // اگر سرور مدتی خاموش بوده، فقط یک بار اجرا می‌شود (نه یک بار برای هر نوبت از دست رفته).
            var next = ScheduledReportRules.NextRun(item.Frequency, item.Hour, item.WeekDay, now);
            var claimed = await context.ScheduledReports
                .Where(r => r.Id == item.Id && r.NextRunAt == item.NextRunAt)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.NextRunAt, (DateTimeOffset?)next), cancellationToken);
            if (claimed == 0)
                continue;

            var report = await context.ScheduledReports.AsTracking().FirstOrDefaultAsync(r => r.Id == item.Id, cancellationToken);
            if (report is null)
                continue;
            var (ok, status) = await ExecuteAsync(report, now, cancellationToken);
            report.LastRunAt = now;
            report.LastSucceeded = ok;
            report.LastStatus = Truncate(status, 1000);
            await context.SaveChangesAsync(cancellationToken);
            executed++;

            if (!ok)
            {
                await notifications.NotifyAsync(new StaffAlert(StaffNotificationKind.ReportFailed,
                    $"ارسال گزارش «{report.Title}» ناموفق بود", Truncate(status, 300), "/admin/reports/scheduled",
                    [AdminPermissions.SettingsManage, AdminPermissions.FinanceView]));
            }
        }
        return executed;
    }

    /// <summary>ساخت گزارش و ارسال به همهٔ کانال‌های انتخابی. موفق = حداقل یک کانال موفق و هیچ کانالی ناموفق.</summary>
    private async Task<(bool Ok, string Status)> ExecuteAsync(ScheduledReport report, DateTimeOffset runAt, CancellationToken cancellationToken)
    {
        ReportDigest digest;
        try
        {
            digest = await BuildDigestAsync(report.Title, report.Frequency, report.Sections, runAt, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Building scheduled report {ReportId} failed", report.Id);
            return (false, "خطا در ساخت گزارش؛ جزئیات در لاگ خطاها.");
        }

        var config = await context.ReportDeliveryConfigs.AsNoTracking().OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        var results = new List<(string Channel, DeliveryAttempt Attempt)>();

        if (report.Channels.HasFlag(ReportChannels.Email))
        {
            var recipients = ScheduledReportRules.ParseEmails(report.EmailTo).Valid;
            var smtp = BuildSmtp(config);
            results.Add(("ایمیل", smtp is null
                ? new DeliveryAttempt(false, "تنظیمات SMTP کامل نیست")
                : recipients.Count == 0
                    ? new DeliveryAttempt(false, "گیرنده‌ای ثبت نشده")
                    : await delivery.SendEmailAsync(smtp, recipients, $"{digest.Title} – {digest.PeriodLabel}", digest.ToHtml(), digest.ToText(), cancellationToken)));
        }

        var text = digest.ToText();
        if (report.Channels.HasFlag(ReportChannels.Bale))
            results.Add(("بله", await SendBotAsync(ReportChannels.Bale, config?.BaleTokenProtected, report.BaleChatId, text, cancellationToken)));
        if (report.Channels.HasFlag(ReportChannels.Telegram))
            results.Add(("تلگرام", await SendBotAsync(ReportChannels.Telegram, config?.TelegramTokenProtected, report.TelegramChatId, text, cancellationToken)));

        if (results.Count == 0)
            return (false, "هیچ کانالی انتخاب نشده است.");
        var status = string.Join(" · ", results.Select(r => $"{r.Channel}: {r.Attempt.Message}"));
        return (results.All(r => r.Attempt.Ok), status);
    }

    private async Task<DeliveryAttempt> SendBotAsync(ReportChannels channel, string? protectedToken, string? chatId, string text, CancellationToken cancellationToken)
    {
        var token = Unprotect(protectedToken);
        if (string.IsNullOrWhiteSpace(token))
            return new DeliveryAttempt(false, "توکن ربات ثبت نشده");
        if (!ScheduledReportRules.IsValidChatId(chatId))
            return new DeliveryAttempt(false, "شناسهٔ گفت‌وگو نامعتبر");
        return await delivery.SendBotMessageAsync(channel, token, chatId!.Trim(), text, cancellationToken);
    }

    private SmtpSettings? BuildSmtp(ReportDeliveryConfig? config)
    {
        if (config is null || string.IsNullOrWhiteSpace(config.SmtpHost) || string.IsNullOrWhiteSpace(config.FromAddress))
            return null;
        return new SmtpSettings(config.SmtpHost.Trim(), config.SmtpPort, config.SmtpUserName, Unprotect(config.SmtpPasswordProtected),
            config.FromAddress.Trim(), config.FromName);
    }

    private string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        try
        {
            return secrets.Unprotect(value);
        }
        catch (Exception ex)
        {
            // کلید Data Protection عوض شده (مثلاً سرور جدید)؛ مدیر باید رمز/توکن را دوباره وارد کند.
            logger.LogWarning(ex, "Could not unprotect a report delivery secret");
            return null;
        }
    }

    public async Task<ReportDeliveryView> GetDeliveryAsync(CancellationToken cancellationToken = default)
    {
        var c = await context.ReportDeliveryConfigs.AsNoTracking().OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        return c is null
            ? new ReportDeliveryView(null, 587, null, null, null, false, false, false)
            : new ReportDeliveryView(c.SmtpHost, c.SmtpPort, c.SmtpUserName, c.FromAddress, c.FromName,
                !string.IsNullOrEmpty(c.SmtpPasswordProtected), !string.IsNullOrEmpty(c.BaleTokenProtected), !string.IsNullOrEmpty(c.TelegramTokenProtected));
    }

    public async Task<string> SendAlertAsync(string title, string body, string? emailTo, string? baleChatId, string? telegramChatId,
        CancellationToken cancellationToken = default)
    {
        var config = await context.ReportDeliveryConfigs.AsNoTracking().OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        var results = new List<string>();
        var emails = ScheduledReportRules.ParseEmails(emailTo).Valid;
        if (emails.Count > 0)
        {
            var smtp = BuildSmtp(config);
            var html = $"<div dir=\"rtl\" style=\"font-family:Tahoma,sans-serif\"><h3>{WebUtility.HtmlEncode(title)}</h3><p>{WebUtility.HtmlEncode(body).Replace("\n", "<br>")}</p></div>";
            var attempt = smtp is null ? new DeliveryAttempt(false, "تنظیمات SMTP کامل نیست") : await delivery.SendEmailAsync(smtp, emails, title, html, body, cancellationToken);
            results.Add("ایمیل: " + attempt.Message);
        }
        var text = title + "\n" + body;
        if (text.Length > ScheduledReportRules.MaxBotMessageLength)
            text = text[..(ScheduledReportRules.MaxBotMessageLength - 1)] + "…";
        if (!string.IsNullOrWhiteSpace(baleChatId))
            results.Add("بله: " + (await SendBotAsync(ReportChannels.Bale, config?.BaleTokenProtected, baleChatId, text, cancellationToken)).Message);
        if (!string.IsNullOrWhiteSpace(telegramChatId))
            results.Add("تلگرام: " + (await SendBotAsync(ReportChannels.Telegram, config?.TelegramTokenProtected, telegramChatId, text, cancellationToken)).Message);
        return results.Count == 0 ? "گیرندهٔ بیرونی ثبت نشده است." : string.Join(" · ", results);
    }

    public async Task<OperationResult> SaveDeliveryAsync(ReportDeliveryInput input, CancellationToken cancellationToken = default)
    {
        if (input.SmtpPort is < 1 or > 65535)
            return OperationResult.Error("درگاه SMTP باید بین ۱ تا ۶۵۵۳۵ باشد.");
        var host = input.SmtpHost?.Trim();
        if (!string.IsNullOrEmpty(host) && (host.Length > 200 || Uri.CheckHostName(host) == UriHostNameType.Unknown))
            return OperationResult.Error("نام سرور SMTP نامعتبر است. مثال: mail.example.com");
        var from = input.FromAddress?.Trim();
        if (!string.IsNullOrEmpty(from) && ScheduledReportRules.ParseEmails(from).Valid.Count != 1)
            return OperationResult.Error("ایمیل فرستنده نامعتبر است.");
        if (input.BaleToken?.Trim() is { Length: > 0 } bale && !IsBotToken(bale))
            return OperationResult.Error("توکن ربات بله نامعتبر است (قالب: 123456:ABC…).");
        if (input.TelegramToken?.Trim() is { Length: > 0 } telegram && !IsBotToken(telegram))
            return OperationResult.Error("توکن ربات تلگرام نامعتبر است (قالب: 123456:ABC…).");

        var config = await context.ReportDeliveryConfigs.AsTracking().OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (config is null)
        {
            config = new ReportDeliveryConfig();
            context.ReportDeliveryConfigs.Add(config);
        }

        config.SmtpHost = string.IsNullOrEmpty(host) ? null : host;
        config.SmtpPort = input.SmtpPort;
        config.SmtpUserName = Clean(input.SmtpUserName, 200);
        config.FromAddress = string.IsNullOrEmpty(from) ? null : from;
        config.FromName = Clean(input.FromName, 100);
        config.SmtpPasswordProtected = Secret(config.SmtpPasswordProtected, input.SmtpPassword, input.ClearSmtpPassword);
        config.BaleTokenProtected = Secret(config.BaleTokenProtected, input.BaleToken, input.ClearBaleToken);
        config.TelegramTokenProtected = Secret(config.TelegramTokenProtected, input.TelegramToken, input.ClearTelegramToken);
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success("تنظیمات کانال‌های ارسال ذخیره شد.");

        string? Secret(string? current, string? entered, bool clear)
        {
            if (clear)
                return null;
            var value = entered?.Trim();
            return string.IsNullOrEmpty(value) ? current : secrets.Protect(value);
        }
    }

    /// <summary>قالب توکن ربات تلگرام/بله: «شناسهٔ عددی:رشتهٔ تصادفی». مثال: 123456789:AAH-xyz…</summary>
    public static bool IsBotToken(string value) =>
        value.Length is >= 20 and <= 200 && System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{3,20}:[A-Za-z0-9_\-]{10,180}$");

    private static string? Clean(string? value, int max)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length <= max ? text : text[..max];
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}
