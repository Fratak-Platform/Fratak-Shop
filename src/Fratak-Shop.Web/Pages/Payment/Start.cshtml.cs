using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fratak_Shop.Web.Pages.Payment;

[Authorize(Policy = PolicyNames.CustomerOnly)]
public class StartModel(IPaymentService payments) : PageModel
{
    public async Task<IActionResult> OnGetAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirst(FratakClaims.UserId)?.Value;
        if (!Guid.TryParse(userIdValue, out var userId))
            return Redirect($"/auth/login?returnUrl=/payment/start?orderId={orderId}");

        var result = await payments.StartAsync(orderId, userId,
            HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        if (!result.IsSuccess() || result.Data is null)
            return Redirect($"/profile/orders/{orderId}?paymentError=1");

        return Redirect(result.Data.RedirectUrl);
    }
}
