using Fratak_Shop.Core.DTOs.Payments.Command;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.DTOs.Payments.Query;
using Fratak_Shop.Core.Services.Media;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

[Authorize(Policy = PolicyNames.CustomerOnly)]
public class OrderDetailModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IOrderService orders,
    IPaymentService payments,
    IBankTransferService bankTransfers,
    IMediaService media)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    public OrderDto Order { get; private set; } = null!;

    public BankTransferDetailDto? BankTransfer { get; private set; }

    public List<PaymentTransactionDto> Transactions { get; private set; } = [];

    [BindProperty]
    public BankTransferInput Transfer { get; set; } = new();

    [BindProperty]
    public string? CancelReason { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        if (CurrentUserId is not { } userId)
            return Redirect($"/login?returnUrl=/profile/orders/{id}");

        var order = await orders.GetDetailAsync(id);
        if (order is null || order.UserId != userId)
            return NotFound();

        Order = order;
        BankTransfer = await bankTransfers.GetForOrderAsync(id);
        Transactions = await payments.GetForOrderAsync(id);

        ApplySeo(null, new SeoFallback
        {
            SiteName = Site.SiteName,
            SiteUrl = Site.SiteUrl,
            Path = $"/profile/orders/{id}",
            ContentTitle = $"سفارش {order.OrderNumber}",
            EntityType = SeoEntityType.Page,
            ForceNoIndex = true,
        });

        return Page();
    }

    /// <summary>شروع پرداخت آنلاین (سندباکس) برای سفارش در انتظار پرداخت.</summary>
    public async Task<IActionResult> OnPostPayAsync(Guid id)
    {
        if (CurrentUserId is not { } userId)
            return Redirect("/login");

        var result = await payments.StartAsync(id, userId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            $"{Request.Scheme}://{Request.Host}");

        if (!result.IsSuccess() || result.Data is null)
        {
            Notify(result.Message, "error");
            return Redirect($"/profile/orders/{id}");
        }

        return Redirect(result.Data.RedirectUrl);
    }

    /// <summary>ثبت اطلاعات و فیش کارت به کارت.</summary>
    public async Task<IActionResult> OnPostSubmitTransferAsync(Guid id)
    {
        if (CurrentUserId is not { } userId)
            return Redirect("/login");

        var order = await orders.GetDetailAsync(id);
        if (order is null || order.UserId != userId)
            return NotFound();

        var receipts = new List<StoredReceipt>();
        var uploadedFiles = Request.Form.Files.Where(file => file.Name == "Transfer.Receipts").Take(4).ToList();

        if (uploadedFiles.Count > 0)
        {
            var saved = await media.SaveFilesAsync(uploadedFiles, MediaFolders.BankTransfers);
            if (saved.IsSuccess() && saved.Data is not null)
            {
                receipts.AddRange(saved.Data.Select(file => new StoredReceipt
                {
                    FileName = file.FileName,
                    OriginalName = file.OriginalName,
                    ContentType = file.ContentType,
                    SizeBytes = file.SizeBytes,
                }));
            }
        }

        var result = await bankTransfers.SubmitAsync(new SubmitBankTransferCommand
        {
            OrderId = id,
            PayerName = Transfer.PayerName,
            PayerCardNumber = Transfer.PayerCardNumber,
            PayerBankName = Transfer.PayerBankName,
            TrackingNumber = Transfer.TrackingNumber,
            TransferDate = Transfer.TransferDate,
            CustomerNote = Transfer.CustomerNote,
            Receipts = receipts,
        });

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return Redirect($"/profile/orders/{id}");
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id)
    {
        if (CurrentUserId is not { } userId)
            return Redirect("/login");

        var result = await orders.CancelByCustomerAsync(id, userId, CancelReason);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return Redirect($"/profile/orders/{id}");
    }

    public class BankTransferInput
    {
        public string PayerName { get; set; } = string.Empty;
        public string PayerCardNumber { get; set; } = string.Empty;
        public string? PayerBankName { get; set; }
        public string? TrackingNumber { get; set; }
        public DateTimeOffset? TransferDate { get; set; }
        public string? CustomerNote { get; set; }
    }
}
