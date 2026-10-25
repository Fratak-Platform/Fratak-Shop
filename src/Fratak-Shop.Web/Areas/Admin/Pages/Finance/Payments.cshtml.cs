using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Entities.Payments;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Finance;

[AdminPermission(AdminPermissions.FinanceView)]
public class PaymentsModel(IAdminReportService reports) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "status")] public PaymentTransactionStatus? Status { get; set; }
    [BindProperty(SupportsGet = true, Name = "purpose")] public PaymentPurpose? Purpose { get; set; }
    [BindProperty(SupportsGet = true, Name = "from")] public string? From { get; set; }
    [BindProperty(SupportsGet = true, Name = "to")] public string? To { get; set; }
    [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;

    public PagedList<AdminPaymentRow> Payments { get; private set; } = new([], 0, 1, 30);

    public async Task OnGetAsync() => Payments = await reports.GetPaymentsAsync(Filter(Math.Max(1, PageNumber), 30));

    [AdminPermission(AdminPermissions.FinanceExport)]
    public async Task<IActionResult> OnGetExportAsync(string format = "xlsx")
    {
        var table = new ExportTable("تراکنش‌های درگاه", "تاریخ", "درگاه", "نوع", "مبلغ (تومان)", "وضعیت", "کد پیگیری", "کارت", "Authority", "کد نتیجه", "پیام", "تأیید", "کاربر", "سفارش");
        for (var pageNumber = 1; pageNumber <= 250; pageNumber++)
        {
            var chunk = await reports.GetPaymentsAsync(Filter(pageNumber, 200));
            foreach (var p in chunk.Items)
                table.Add(p.CreatedAt, p.Gateway, AdminUi.PaymentPurpose(p.Purpose), p.Amount, AdminUi.TransactionStatus(p.Status), p.RefId, p.CardPan, p.Authority,
                    p.ResultCode, p.Message, p.VerifiedAt, p.UserName, p.OrderNumber);
            if (pageNumber >= chunk.PageCount)
                break;
        }
        return Exporter.File(table, format, "payments");
    }

    private PaymentFilter Filter(int pageNumber, int take)
    {
        var period = PersianDateInput.Period(From, To);
        return new PaymentFilter { Search = Search, Status = Status, Purpose = Purpose, From = period.From, To = period.To, Page = pageNumber, Take = take };
    }
}
