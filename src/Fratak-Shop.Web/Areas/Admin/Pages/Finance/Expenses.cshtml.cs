using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Insights;
using Fratak_Shop.Data.Entities.Insights;
using Fratak_Shop.Web.Areas.Admin.Pages.Reports;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Finance;

/// <summary>
/// صفحهٔ «هزینه‌ها» (/admin/finance/expenses، مرحلهٔ هفتم – فاز ۷ب).
/// <para>
/// ثبت هزینه‌های جاری فروشگاه (هاست، دامنه، تبلیغات، حقوق، …)، هزینه‌های تکراری ماهانه/سالانه با یادآوری سررسید،
/// و «سود خالص» = سود ناخالص واقعی (فروش − فاکتور خرید) − هزینه‌های همان بازه.
/// </para>
/// دسترسی: مشاهده با «گزارش مالی»؛ ثبت/ویرایش/حذف با «هزینه‌ها و هدف فروش»؛ خروجی اکسل با «خروجی مالی».
/// منطق: Core/Services/Insights/ExpenseService.
/// </summary>
[AdminPermission(AdminPermissions.FinanceView, AdminPermissions.ExpensesManage)]
public class ExpensesModel(IExpenseService expenses, IAdminReportService adminReports) : ReportPageModel
{
    public override string BasePath => "/admin/finance/expenses";

    protected override string DefaultPreset => "month";

    /// <summary>تب فعال: «list» (پیش‌فرض) یا «recurring».</summary>
    [BindProperty(SupportsGet = true, Name = "tab")] public string? Tab { get; set; }

    [BindProperty(SupportsGet = true, Name = "cat")] public ExpenseCategory? Category { get; set; }

    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }

    [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;

    /// <summary>شناسهٔ هزینه‌ای که فرمش برای ویرایش باز است.</summary>
    [FromQuery(Name = "edit")] public Guid? EditId { get; set; }

    [BindProperty] public ExpenseForm Form { get; set; } = new();

    public PagedList<ExpenseRow> Items { get; private set; } = new([], 0, 1, 30);
    public ExpenseSummary Summary { get; private set; } = new();

    /// <summary>سود ناخالص واقعی بازه (فقط برای دارندگان «گزارش مالی»).</summary>
    public decimal? GrossProfit { get; private set; }

    public bool CanManage => Can(AdminPermissions.ExpensesManage);
    public bool CanSeeFinance => Can(AdminPermissions.FinanceView);
    public bool IsRecurringTab => Tab == "recurring";

    /// <summary>فرم باز بماند؟ (ویرایش، خطای اعتبارسنجی یا ?new=1).</summary>
    public bool FormOpen { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        if (EditId is { } id && CanManage)
        {
            var row = await expenses.GetAsync(id, HttpContext.RequestAborted);
            if (row is null)
                return NotFound();
            Form = ExpenseForm.From(row);
            FormOpen = true;
        }
        else
        {
            Form = new ExpenseForm { SpentOn = PersianDateInput.Format(Fratak_Shop.Core.Utilities.IranClock.Today(DateTimeOffset.UtcNow)) };
            FormOpen = Request.Query.ContainsKey("new");
        }
        return Page();
    }

    [AdminPermission(AdminPermissions.ExpensesManage)]
    public async Task<IActionResult> OnPostSaveAsync()
    {
        var spentOn = PersianDateInput.Parse(Form.SpentOn);
        if (spentOn is null)
            return await InvalidAsync("تاریخ هزینه نامعتبر است (مثال: 1405/08/04).");

        var result = await expenses.SaveAsync(new ExpenseInput(Form.Id, Form.Title ?? "", Form.Category, Form.Amount, spentOn.Value,
            Form.Vendor, Form.Note, Form.Recurrence), ActorId, ActorName, HttpContext.RequestAborted);
        if (!Succeeded(result))
            return await InvalidAsync(result.Message ?? "ذخیره نشد.");
        ShowResult(result);
        return Redirect(AdminUi.With(Request, ("handler", null), ("edit", null), ("new", null)));
    }

    [AdminPermission(AdminPermissions.ExpensesManage)]
    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        ShowResult(await expenses.DeleteAsync(id, HttpContext.RequestAborted));
        return BackToList();
    }

    /// <summary>«ثبت پرداخت این دوره» برای هزینهٔ تکراری؛ مبلغ خالی = همان مبلغ قبلی.</summary>
    [AdminPermission(AdminPermissions.ExpensesManage)]
    public async Task<IActionResult> OnPostRecordAsync(Guid id, decimal? amount)
    {
        ShowResult(await expenses.RecordNextAsync(id, amount is > 0 ? amount : null, ActorId, ActorName, HttpContext.RequestAborted));
        return BackToList();
    }

    [AdminPermission(AdminPermissions.ExpensesManage)]
    public async Task<IActionResult> OnPostStopAsync(Guid id)
    {
        ShowResult(await expenses.StopRecurrenceAsync(id, HttpContext.RequestAborted));
        return BackToList();
    }

    [AdminPermission(AdminPermissions.FinanceExport)]
    public async Task<IActionResult> OnGetExportAsync(string format = "xlsx")
    {
        ResolvePeriod();
        var page = await expenses.ListAsync(new ExpenseQuery(Period.Current, Category, Search, 1, 200), HttpContext.RequestAborted);
        var table = new ExportTable("هزینه‌ها", "تاریخ", "عنوان", "دسته", "طرف حساب", "مبلغ", "تکرار", "سررسید بعدی", "توضیح", "ثبت‌کننده");
        // خروجی همهٔ صفحه‌ها (هر بار ۲۰۰ ردیف؛ سقف ۲۰٬۰۰۰).
        var pageIndex = 1;
        while (true)
        {
            foreach (var e in page.Items)
                table.Add(PersianDateInput.Format(e.SpentOn), e.Title, e.CategoryTitle, e.Vendor, e.Amount, ExpenseRules.RecurrenceTitle(e.Recurrence),
                    PersianDateInput.Format(e.NextDueOn), e.Note, e.CreatedByName);
            if (pageIndex * 200 >= page.Total || pageIndex >= 100)
                break;
            pageIndex++;
            page = await expenses.ListAsync(new ExpenseQuery(Period.Current, Category, Search, pageIndex, 200), HttpContext.RequestAborted);
        }
        return Exporter.File(table, format, "expenses");
    }

    private async Task<IActionResult> InvalidAsync(string message)
    {
        ErrorMessage = message;
        FormOpen = true;
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        var ct = HttpContext.RequestAborted;
        ResolvePeriod();
        Items = await expenses.ListAsync(new ExpenseQuery(Period.Current, Category, Search, Math.Max(1, PageNumber)), ct);
        Summary = await expenses.GetSummaryAsync(Period.Current, ct);
        if (CanSeeFinance)
            GrossProfit = (await adminReports.GetPurchasesAsync(Period.Current, ct)).ActualGrossProfit;
    }
}

/// <summary>فرم ثبت/ویرایش هزینه. تاریخ شمسی به‌صورت متن (ورودی data-jalali) و مبلغ خام (ورودی data-money) می‌آید.</summary>
public sealed class ExpenseForm
{
    public Guid? Id { get; set; }
    public string? Title { get; set; }
    public ExpenseCategory Category { get; set; } = ExpenseCategory.Hosting;
    public decimal Amount { get; set; }
    public string? SpentOn { get; set; }
    public string? Vendor { get; set; }
    public string? Note { get; set; }
    public ExpenseRecurrence Recurrence { get; set; }

    public static ExpenseForm From(ExpenseRow row) => new()
    {
        Id = row.Id,
        Title = row.Title,
        Category = row.Category,
        Amount = row.Amount,
        SpentOn = PersianDateInput.Format(row.SpentOn),
        Vendor = row.Vendor,
        Note = row.Note,
        Recurrence = row.Recurrence,
    };
}
