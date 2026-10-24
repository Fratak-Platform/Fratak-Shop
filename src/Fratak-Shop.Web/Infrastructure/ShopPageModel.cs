using Fratak_Shop.Core.Utilities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// Base class of every storefront page: current-user helpers and one-shot
/// status messages (shown by the <c>_StatusMessage</c> partial after a redirect).
/// </summary>
public abstract class ShopPageModel : PageModel
{
    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    protected Guid? CurrentUserId => User.GetUserId();

    protected bool IsSignedIn => User.IsSignedIn();

    /// <summary>Stores the service message as success / error toast.</summary>
    protected void ShowResult(OperationResult result)
    {
        if (result.Status == EnOperationResultStatus.Success)
            SuccessMessage = result.Message;
        else
            ErrorMessage = result.Message;
    }

    /// <summary>Stores the service message as success / error toast.</summary>
    protected void ShowResult<TData>(OperationResult<TData> result)
    {
        if (result.Status == EnOperationResultStatus.Success)
            SuccessMessage = result.Message;
        else
            ErrorMessage = result.Message;
    }

    protected static bool Succeeded(OperationResult result) => result.Status == EnOperationResultStatus.Success;

    protected static bool Succeeded<TData>(OperationResult<TData> result) => result.Status == EnOperationResultStatus.Success;

    /// <summary>Redirects back to a local return URL, or to the given page.</summary>
    protected IActionResult RedirectToLocal(string? returnUrl, string fallbackPage = "/Index")
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);

        return RedirectToPage(fallbackPage);
    }

    /// <summary>Sends guests to the login page and brings them back afterwards.</summary>
    protected IActionResult RedirectToLogin(string? returnUrl = null)
    {
        return RedirectToPage("/Login", new { returnUrl = returnUrl ?? (Request.Path + Request.QueryString) });
    }
}
