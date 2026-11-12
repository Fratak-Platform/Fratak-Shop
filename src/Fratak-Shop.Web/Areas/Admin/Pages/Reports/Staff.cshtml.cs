using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Insights;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Reports;

/// <summary>
/// صفحهٔ «عملکرد کارکنان» (/admin/reports/staff، مرحلهٔ هفتم – فاز ۷ب).
/// <para>
/// برای هر کارمند پنل در بازهٔ انتخابی: تغییرات ثبت‌شده، تغییر وضعیت و یادداشت سفارش، سفارش ثبت‌شده برای مشتری،
/// پاسخ تیکت و میانگین زمان اولین پاسخ، فاکتور خرید، پیام به مشتری، مقاله و آخرین فعالیت.
/// با کلیک روی هر نفر: نمودار فعالیت روزانه، ساعت‌های کاری و پرتکرارترین کارها.
/// </para>
/// دسترسی: «مدیریت کارکنان» یا «مشاهدهٔ گزارش فعالیت». منطق: Core/Services/Insights/StaffPerformanceService.
/// </summary>
[AdminPermission(AdminPermissions.StaffManage, AdminPermissions.AuditView)]
public class StaffModel(IStaffPerformanceService performance) : ReportPageModel
{
    public override string BasePath => "/admin/reports/staff";

    /// <summary>کارمند انتخاب‌شده برای جزئیات.</summary>
    [BindProperty(SupportsGet = true, Name = "user")] public Guid? UserId { get; set; }

    public List<StaffPerformanceRow> Rows { get; private set; } = [];
    public StaffActivityDetail? Detail { get; private set; }

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        ResolvePeriod();
        if (UserId is { } id)
        {
            Detail = await performance.GetDetailAsync(id, Period.Current, ct);
            Rows = Detail is null ? await performance.GetAsync(Period.Current, ct) : [];
        }
        else
        {
            Rows = await performance.GetAsync(Period.Current, ct);
        }
    }

    [AdminPermission(AdminPermissions.FinanceExport, AdminPermissions.StaffManage)]
    public async Task<IActionResult> OnGetExportAsync(string format = "xlsx")
    {
        ResolvePeriod();
        var rows = await performance.GetAsync(Period.Current, HttpContext.RequestAborted);
        var table = new ExportTable("عملکرد کارکنان", "نام", "موبایل", "نقش‌ها", "تغییرات ثبت‌شده", "تغییر وضعیت سفارش", "یادداشت سفارش", "سفارش ثبت‌شده",
            "مبلغ سفارش‌های ثبت‌شده", "پاسخ تیکت", "اولین پاسخ‌ها", "میانگین دقیقهٔ اولین پاسخ", "فاکتور خرید", "پیام به مشتری", "مقاله", "آخرین فعالیت");
        foreach (var r in rows)
            table.Add(r.Name, r.PhoneNumber, string.Join("، ", r.Roles), r.Actions, r.StatusChanges, r.OrderNotes, r.OrdersCreated, r.OrdersCreatedAmount,
                r.TicketReplies, r.FirstResponses, r.AvgFirstResponseMinutes, r.Invoices, r.CustomerMessages, r.Posts, r.LastActivityAt);
        return Exporter.File(table, format, "staff-performance");
    }

    /// <summary>متن خوانا برای میانگین زمان پاسخ. مثال: 95 → «1 ساعت و 35 دقیقه».</summary>
    public static string Minutes(double? minutes)
    {
        if (minutes is not { } m)
            return "—";
        if (m < 1)
            return "کمتر از ۱ دقیقه";
        var span = TimeSpan.FromMinutes(m);
        if (span.TotalHours < 1)
            return $"{(int)span.TotalMinutes} دقیقه";
        if (span.TotalDays < 1)
            return span.Minutes > 0 ? $"{(int)span.TotalHours} ساعت و {span.Minutes} دقیقه" : $"{(int)span.TotalHours} ساعت";
        return $"{(int)span.TotalDays} روز و {span.Hours} ساعت";
    }
}
