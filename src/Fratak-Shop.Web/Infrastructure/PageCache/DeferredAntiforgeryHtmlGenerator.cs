using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;

namespace Fratak_Shop.Web.Infrastructure.PageCache;

/// <summary>
/// On pages that may be stored in the page cache, forms get an empty antiforgery field instead of a
/// real token (a token would set a cookie and be shared with everyone receiving the cached copy).
/// shop.js fills the field from <c>/api/antiforgery</c> right before the form is submitted.
/// Every other page renders the normal token.
/// </summary>
public sealed class DeferredAntiforgeryHtmlGenerator(
    IAntiforgery antiforgery,
    IOptions<MvcViewOptions> optionsAccessor,
    IModelMetadataProvider metadataProvider,
    IUrlHelperFactory urlHelperFactory,
    HtmlEncoder htmlEncoder,
    ValidationHtmlAttributeProvider validationAttributeProvider)
    : DefaultHtmlGenerator(antiforgery, optionsAccessor, metadataProvider, urlHelperFactory, htmlEncoder, validationAttributeProvider)
{
    private static readonly HtmlString Placeholder = new("<input name=\"__RequestVerificationToken\" type=\"hidden\" value=\"\" data-af-deferred>");

    public override IHtmlContent GenerateAntiforgery(ViewContext viewContext)
    {
        if (!PageCachePolicy.IsCacheable(viewContext.HttpContext))
            return base.GenerateAntiforgery(viewContext);

        var formContext = viewContext.FormContext;
        if (formContext.CanRenderAtEndOfForm)
            formContext.HasAntiforgeryToken = true;
        return Placeholder;
    }
}
