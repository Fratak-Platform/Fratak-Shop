using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Orders;

[AdminPermission(AdminPermissions.OrdersView)]
[RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024)]
[RequestSizeLimit(12 * 1024 * 1024)]
public class DetailModel(
    IAdminOrderService orders,
    IUploadStore uploads,
    IAdminDataService data,
    IOrderMessageService messages,
    IOrderSlaService sla,
    IStaffOrderService staffOrders) : AdminPageModel
{
    /// <summary>گفت‌وگوی سفارش با مشتری (پیام‌ها و سؤال‌های «کالا ناموجود»).</summary>
    public List<OrderMessageDto> Messages { get; private set; } = [];

    /// <summary>مرحلهٔ فعلی زمان‌بندی (SLA) و بازهٔ تحویل تخمینی؛ سفارش بسته یا SLA خاموش ← null.</summary>
    public OrderSlaResult? Sla { get; private set; }
    public OrderDeliveryEstimate? Estimate { get; private set; }

    /// <summary>
    /// امکان پیام و سؤال حذف کالا: سفارش به حساب کاربری وصل باشد و هنوز ارسال نشده باشد
    /// (همان قانون سرویس؛ اینجا فقط برای نمایش/پنهان کردن فرم‌ها).
    /// </summary>
    public bool CanAskCustomer => Detail.Order.UserId is not null
        && Detail.Order.Status is OrderStatus.Pending or OrderStatus.Confirmed or OrderStatus.Processing;

    public AdminOrderDetail Detail { get; private set; } = null!;

    public List<(Guid Id, string Name)> Suppliers { get; private set; } = [];

    public bool HasInvoice => Detail.PurchaseInvoices.Count > 0;

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var detail = await orders.GetDetailAsync(id);
        if (detail is null)
            return NotFound();

        Detail = detail;
        var suppliers = await data.Query<Supplier>().Where(s => s.IsActive).OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
            .Select(s => new { s.Id, s.Name }).ToListAsync();
        Suppliers = suppliers.Select(s => (s.Id, s.Name)).ToList();
        Messages = await messages.ListAsync(id);
        (Sla, Estimate) = await sla.GetForOrderAsync(id, HttpContext.RequestAborted);
        if (Messages.Any(m => !m.FromStaff && m.ReadByStaffAt is null))
            await messages.MarkReadByStaffAsync(id);
        return Page();
    }

    /// <summary>پیام آزاد به مشتری (پیامک/اعلان + نمایش در صفحهٔ سفارش او).</summary>
    [AdminPermission(AdminPermissions.OrdersManage)]
    public async Task<IActionResult> OnPostMessageAsync(Guid id, string? body)
    {
        ShowResult(await messages.SendFromStaffAsync(id, body, ActorId, ActorName));
        return Redirect($"/admin/orders/{id}#messages");
    }

    /// <summary>سؤال «این کالا ناموجود است؛ حذف شود یا سفارش لغو شود؟» – با گزینهٔ صفر کردن موجودی سایت.</summary>
    [AdminPermission(AdminPermissions.OrdersManage)]
    public async Task<IActionResult> OnPostAskItemAsync(Guid id, Guid orderItemId, string? body, bool zeroStock)
    {
        ShowResult(await messages.AskItemUnavailableAsync(id, orderItemId, body, zeroStock, ActorId, ActorName));
        return Redirect($"/admin/orders/{id}#messages");
    }

    /// <summary>پس گرفتن سؤال بی‌پاسخ (مثلاً کالا پیدا شد).</summary>
    [AdminPermission(AdminPermissions.OrdersManage)]
    public async Task<IActionResult> OnPostWithdrawAsync(Guid id, Guid messageId)
    {
        ShowResult(await messages.WithdrawAsync(id, messageId, ActorId, ActorName));
        return Redirect($"/admin/orders/{id}#messages");
    }

    /// <summary>ارسال دوبارهٔ لینک پرداخت و تمدید مهلت (فقط سفارش‌های ثبت‌شده در پنل که هنوز پرداخت نشده‌اند).</summary>
    [AdminPermission(AdminPermissions.OrdersCreate, AdminPermissions.OrdersManage)]
    public async Task<IActionResult> OnPostResendLinkAsync(Guid id)
    {
        ShowResult(await staffOrders.ResendLinkAsync(id, ActorId, ActorName, $"{Request.Scheme}://{Request.Host}"));
        return Redirect($"/admin/orders/{id}#payment-link");
    }

    [AdminPermission(AdminPermissions.OrdersManage)]
    public async Task<IActionResult> OnPostStatusAsync(Guid id, OrderStatus status, string? note, IFormFile? invoiceFile,
        decimal? invoiceAmount, Guid? supplierId, string? supplierName, string? invoiceNumber)
    {
        if (status is OrderStatus.Cancelled or OrderStatus.Returned && !Can(AdminPermissions.OrdersCancel))
            return Redirect("/admin/denied?need=" + AdminPermissions.OrdersCancel);

        // Moving to «preparation» without an invoice: the invoice comes with the same form.
        if (invoiceFile is { Length: > 0 })
        {
            if (!Can(AdminPermissions.PurchaseInvoices))
                return Redirect("/admin/denied?need=" + AdminPermissions.PurchaseInvoices);

            var saved = await SaveInvoiceAsync(id, invoiceFile, invoiceAmount, supplierId, supplierName, invoiceNumber, null, null);
            if (!saved)
                return Redirect($"/admin/orders/{id}");
        }

        var result = await orders.ChangeStatusAsync(id, status, note, ActorId, ActorName);
        ShowResult(result);
        return Redirect($"/admin/orders/{id}");
    }

    [AdminPermission(AdminPermissions.PurchaseInvoices)]
    public async Task<IActionResult> OnPostInvoiceAsync(Guid id, IFormFile? invoiceFile, decimal? invoiceAmount, Guid? supplierId,
        string? supplierName, string? invoiceNumber, string? purchasedAt, string? invoiceNotes)
    {
        var date = PersianDateInput.Parse(purchasedAt);
        if (await SaveInvoiceAsync(id, invoiceFile, invoiceAmount, supplierId, supplierName, invoiceNumber,
                date is { } d ? Core.Utilities.IranClock.StartOfDay(d).AddHours(12) : null, invoiceNotes))
            SuccessMessage = "فاکتور خرید ثبت شد.";
        return Redirect($"/admin/orders/{id}#invoices");
    }

    [AdminPermission(AdminPermissions.PurchaseInvoices)]
    public async Task<IActionResult> OnPostDeleteInvoiceAsync(Guid id, Guid invoiceId)
    {
        var invoice = await orders.GetPurchaseInvoiceAsync(invoiceId);
        if (invoice is null || invoice.OrderId != id)
            return NotFound();

        var result = await orders.DeletePurchaseInvoiceAsync(invoiceId, ActorId, ActorName);
        if (Succeeded(result) && !string.IsNullOrEmpty(result.Data))
            uploads.Delete(UploadArea.PurchaseInvoices, [result.Data]);
        ShowResult(result);
        return Redirect($"/admin/orders/{id}#invoices");
    }

    /// <summary>Invoice file (inline for PDF / images). Private: never reachable from the storefront.</summary>
    [AdminPermission(AdminPermissions.PurchaseInvoices, AdminPermissions.FinanceView, AdminPermissions.OrdersManage)]
    public async Task<IActionResult> OnGetInvoiceFileAsync(Guid id, Guid invoiceId, bool download = false)
    {
        var invoice = await orders.GetPurchaseInvoiceAsync(invoiceId);
        if (invoice is null || invoice.OrderId != id)
            return NotFound();

        var path = uploads.GetPath(UploadArea.PurchaseInvoices, invoice.FileName);
        if (path is null)
            return NotFound();

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var extension = Path.GetExtension(invoice.FileName);
        var name = $"purchase-invoice-{invoice.OrderNumber}{extension}";
        return download
            ? PhysicalFile(path, invoice.ContentType, name)
            : PhysicalFile(path, invoice.ContentType);
    }

    public async Task<IActionResult> OnPostNoteAsync(Guid id, string? content)
    {
        ShowResult(await orders.AddNoteAsync(id, content, ActorId, ActorName));
        return Redirect($"/admin/orders/{id}#notes");
    }

    [AdminPermission(AdminPermissions.OrdersManage)]
    public async Task<IActionResult> OnPostTrackingAsync(Guid id, string? trackingCode)
    {
        ShowResult(await orders.SetTrackingCodeAsync(id, trackingCode, ActorId, ActorName));
        return Redirect($"/admin/orders/{id}");
    }

    [AdminPermission(AdminPermissions.OrdersManage)]
    public async Task<IActionResult> OnPostCashPaidAsync(Guid id, string? reference)
    {
        ShowResult(await orders.MarkCashPaidAsync(id, reference, ActorId, ActorName));
        return Redirect($"/admin/orders/{id}");
    }

    private async Task<bool> SaveInvoiceAsync(Guid orderId, IFormFile? file, decimal? amount, Guid? supplierId, string? supplierName,
        string? invoiceNumber, DateTimeOffset? purchasedAt, string? notes)
    {
        if (file is null || file.Length == 0)
        {
            ErrorMessage = "فایل فاکتور خرید (PDF یا تصویر) را انتخاب کنید.";
            return false;
        }

        if (amount is null or <= 0)
        {
            ErrorMessage = "مبلغ فاکتور خرید را وارد کنید.";
            return false;
        }

        var (files, error) = await uploads.SaveAsync(UploadArea.PurchaseInvoices, [file], 1, 10_240, HttpContext.RequestAborted);
        if (error is not null || files.Count == 0)
        {
            ErrorMessage = error ?? "ذخیره فایل انجام نشد.";
            return false;
        }

        var stored = files[0];
        var result = await orders.AddPurchaseInvoiceAsync(new AddPurchaseInvoiceCommand
        {
            OrderId = orderId,
            SupplierId = supplierId,
            SupplierName = supplierName,
            InvoiceNumber = invoiceNumber,
            Amount = amount.Value,
            PurchasedAt = purchasedAt,
            FileName = stored.FileName,
            ContentType = stored.ContentType,
            SizeBytes = stored.SizeBytes,
            Notes = notes,
            UploadedByUserId = ActorId,
            UploadedByName = ActorName,
        });

        if (!Succeeded(result))
        {
            uploads.Delete(UploadArea.PurchaseInvoices, [stored.FileName]);
            ErrorMessage = result.Message;
            return false;
        }

        return true;
    }
}
