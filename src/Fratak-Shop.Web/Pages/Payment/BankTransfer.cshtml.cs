using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fratak_Shop.Web.Pages.Payment;

[Authorize(Policy = PolicyNames.CustomerOnly)]
public class BankTransferModel : Microsoft.AspNetCore.Mvc.RazorPages.PageModel
{
    public IActionResult OnGet(Guid orderId) => Redirect($"/profile/orders/{orderId}");
}
