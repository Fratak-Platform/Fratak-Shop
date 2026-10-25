using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Orders;

[AdminPermission(AdminPermissions.PurchaseInvoices, AdminPermissions.FinanceView)]
public class PurchaseInvoicesModel(IAdminOrderService orders, IAdminDataService data) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Supplier { get; set; }
    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }

    public PagedList<PurchaseInvoiceDto> Invoices { get; private set; } = new([], 0, 1, 30);

    public List<(Guid Id, string Name)> Suppliers { get; private set; } = [];

    public async Task OnGetAsync([FromQuery(Name = "page")] int pageNumber = 1)
    {
        Invoices = await orders.GetPurchaseInvoicesAsync(Filter(pageNumber, 30));
        var suppliers = await data.Query<Supplier>().OrderBy(s => s.Name).Select(s => new { s.Id, s.Name }).ToListAsync();
        Suppliers = suppliers.Select(s => (s.Id, s.Name)).ToList();
    }

    public async Task<IActionResult> OnGetExportAsync(string format = "xlsx")
    {
        var table = new ExportTable("فاکتورهای خرید", "شماره سفارش", "تأمین‌کننده", "شماره فاکتور", "مبلغ", "تاریخ خرید", "تاریخ ثبت", "ثبت‌کننده", "یادداشت");
        for (var page = 1; page <= 250; page++)
        {
            var batch = await orders.GetPurchaseInvoicesAsync(Filter(page, 200));
            foreach (var i in batch.Items)
                table.Add(i.OrderNumber, i.SupplierName, i.InvoiceNumber, i.Amount, i.PurchasedAt, i.CreatedAt, i.UploadedByName, i.Notes);
            if (page >= batch.PageCount)
                break;
        }
        return Exporter.File(table, format, "purchase-invoices");
    }

    private PurchaseInvoiceFilter Filter(int page, int take)
    {
        var period = PersianDateInput.Period(From, To);
        return new PurchaseInvoiceFilter { Search = Search, SupplierId = Supplier, From = period.From, To = period.To, Page = page, Take = take };
    }
}
