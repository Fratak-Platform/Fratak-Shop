using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Returns;

public sealed record AdminReturnItem(string ProductName, Guid? ProductId, string? Sku, int Quantity, int OrderedQuantity, decimal UnitRefundAmount);

[AdminPermission(AdminPermissions.ReturnsManage)]
public class DetailModel(IAdminDataService data, IReturnService returns, IMonitoringService monitoring) : AdminPageModel
{
    public ReturnRequest Return { get; private set; } = null!;
    public string OrderNumber { get; private set; } = "";
    public DateTimeOffset OrderDate { get; private set; }
    public string CustomerName { get; private set; } = "";
    public string CustomerPhone { get; private set; } = "";
    public List<AdminReturnItem> Items { get; private set; } = [];
    public List<string> Images { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var request = await data.Query<ReturnRequest>().Include(r => r.Order).Include(r => r.User).FirstOrDefaultAsync(r => r.Id == id);
        if (request is null)
            return NotFound();
        Return = request;
        OrderNumber = request.Order.OrderNumber;
        OrderDate = request.Order.CreatedAt;
        CustomerName = $"{request.User.FirstName} {request.User.LastName}".Trim();
        CustomerPhone = request.User.PhoneNumber;
        Items = await data.Query<ReturnRequestItem>().Where(i => i.ReturnRequestId == id)
            .Select(i => new AdminReturnItem(i.OrderItem.ProductName, i.OrderItem.ProductId, i.OrderItem.SKU, i.Quantity, i.OrderItem.Quantity, i.UnitRefundAmount))
            .ToListAsync();
        Images = await data.Query<ReturnRequestImage>().Where(i => i.ReturnRequestId == id).Select(i => i.FileName).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostApproveAsync(Guid id, decimal? amount, string? note)
    {
        var result = await returns.ApproveAsync(id, amount, string.IsNullOrWhiteSpace(note) ? null : note.Trim());
        await Log(result, id, "تأیید مرجوعی");
        return Back(id);
    }

    public async Task<IActionResult> OnPostRejectAsync(Guid id, string? note)
    {
        var result = await returns.RejectAsync(id, note?.Trim() ?? "");
        await Log(result, id, "رد مرجوعی");
        return Back(id);
    }

    public async Task<IActionResult> OnPostReceivedAsync(Guid id, bool restock = false)
    {
        var result = await returns.MarkReceivedAsync(id, restock);
        await Log(result, id, restock ? "دریافت کالای مرجوعی و برگشت به موجودی" : "دریافت کالای مرجوعی");
        return Back(id);
    }

    public async Task<IActionResult> OnPostRefundAsync(Guid id)
    {
        var result = await returns.RefundAsync(id);
        await Log(result, id, "بازپرداخت مرجوعی به کیف پول");
        return Back(id);
    }

    private async Task Log(OperationResult result, Guid id, string summary)
    {
        if (Succeeded(result))
            await monitoring.LogActionAsync("Return", "ReturnRequest", id, summary);
        ShowResult(result);
    }

    private RedirectResult Back(Guid id) => Redirect($"/admin/returns/{id}");
}
