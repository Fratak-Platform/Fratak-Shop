using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Infrastructure.Security;
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

    // ── Smart captcha ───────────────────────────────────────────────
    // Pages call PrepareCaptcha on GET, VerifyCaptcha first thing in the POST handler, and
    // CaptchaFailed / CaptchaPassed with the outcome; the form renders the "_Captcha" partial
    // when Captcha is not null.

    /// <summary>Challenge to show on the form (null = not needed).</summary>
    public CaptchaChallenge? Captcha { get; protected set; }

    private ICaptchaService CaptchaService => HttpContext.RequestServices.GetRequiredService<ICaptchaService>();

    protected bool IsCaptchaRequired(string action) =>
        CaptchaService.IsRequired(action, HttpContext, HttpContext.RequestServices.GetRequiredService<ISiteContext>().Settings);

    protected void PrepareCaptcha(string action)
    {
        if (IsCaptchaRequired(action))
            Captcha = CaptchaService.Create();
    }

    /// <summary>False (with a form error) when a captcha is required and the answer is wrong.</summary>
    protected bool VerifyCaptcha(string action)
    {
        if (!IsCaptchaRequired(action))
            return true;

        var valid = Request.HasFormContentType
                    && CaptchaService.Validate(Request.Form["CaptchaToken"], Request.Form["CaptchaAnswer"]);
        Captcha = CaptchaService.Create(); // the posted token is used up either way
        if (!valid)
            ModelState.AddModelError(string.Empty, "کد امنیتی (عدد داخل تصویر) درست وارد نشده است.");
        return valid;
    }

    /// <summary>Counts a failed / suspicious attempt; shows the captcha once the limit is reached.</summary>
    protected void CaptchaFailed(string action)
    {
        CaptchaService.RecordFailure(action, HttpContext);
        if (Captcha is null)
            PrepareCaptcha(action);
    }

    protected void CaptchaPassed(string action) => CaptchaService.Reset(action, HttpContext);

    /// <summary>Sends guests to the login page and brings them back afterwards.</summary>
    protected IActionResult RedirectToLogin(string? returnUrl = null)
    {
        return RedirectToPage("/Login", new { returnUrl = returnUrl ?? (Request.Path + Request.QueryString) });
    }
}
