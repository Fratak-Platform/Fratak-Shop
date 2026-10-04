using Fratak_Shop.Core.Services.Payments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fratak_Shop.Web.Pages.Payment;

public class ZarinPalCallbackModel(IPaymentService payments) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string? authority, string? status,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authority))
            return Redirect("/profile/orders");

        var result = await payments.HandleCallbackAsync(
            authority,
            string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase),
            cancellationToken);

        var orderId = result.Data?.OrderId;
        return orderId is { } id
            ? Redirect($"/profile/orders/{id}?highlight=payment")
            : Redirect("/profile/orders");
    }
}
