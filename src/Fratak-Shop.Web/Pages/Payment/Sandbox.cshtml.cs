using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Payment;

/// <summary>
/// درگاه پرداخت سندباکس: صفحه‌ای که رفتار درگاه واقعی را شبیه‌سازی می‌کند.
/// کاربر می‌تواند پرداخت را موفق یا ناموفق اعلام کند تا کل چرخه سفارش آزمایش شود.
/// </summary>
public class SandboxModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IPaymentService payments)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    public string Authority { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }

    public string OrderNumber { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(string? authority)
    {
        if (string.IsNullOrWhiteSpace(authority))
            return NotFound();

        var transaction = await payments.GetByAuthorityAsync(authority);
        if (transaction is null)
            return NotFound();

        Authority = authority;
        Amount = transaction.Amount;

        ApplySeo(null, new SeoFallback
        {
            SiteName = Site.SiteName,
            SiteUrl = Site.SiteUrl,
            Path = "/payment/sandbox",
            ContentTitle = "درگاه پرداخت آزمایشی",
            EntityType = SeoEntityType.Page,
            ForceNoIndex = true,
        });

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string authority, bool success)
    {
        var result = await payments.HandleCallbackAsync(authority, success, CurrentUserId, HttpContext.Connection.RemoteIpAddress?.ToString());

        Notify(result.Message, result.IsSuccess() ? "success" : "error");

        var orderId = result.Data?.OrderId;
        if (orderId is { } id)
            return Redirect($"/profile/orders/{id}?highlight=payment");

        return Redirect("/profile/orders");
    }
}
