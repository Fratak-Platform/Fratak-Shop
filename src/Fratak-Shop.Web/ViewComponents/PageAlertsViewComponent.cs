using Fratak_Shop.Core.DTOs.Pages.Query;
using Fratak_Shop.Core.Services.Pages;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.ViewComponents;

/// <summary>Where on the page a group of alerts is rendered.</summary>
public enum AlertZone
{
    /// <summary>Above the page content (rendered by the layout).</summary>
    Top = 0,

    /// <summary>Below the page content, above the footer (rendered by the layout).</summary>
    Bottom = 1,

    /// <summary>Under the main product image (rendered by the product page).</summary>
    BelowMainImage = 2,
}

public class PageAlertsViewModel
{
    public AlertZone Zone { get; init; }
    public List<PageAlertDto> Alerts { get; init; } = [];
}

/// <summary>
/// DB-managed alerts (PageAlerts table) for the current page: the page is taken from
/// <see cref="PageMetaKeys.SitePage"/>, product / category targeting from the matching keys.
/// Alerts of "all pages" are shown everywhere. Failures never break the page.
/// </summary>
public class PageAlertsViewComponent(IPageAlertService alertService, ILogger<PageAlertsViewComponent> logger) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(AlertZone zone = AlertZone.Top)
    {
        // Error pages, JSON-like pages etc. don't set a page; they still get the "all pages" alerts.
        var page = ViewData[PageMetaKeys.SitePage] as SitePage? ?? SitePage.None;

        List<PageAlertDto> alerts;
        try
        {
            alerts = await alertService.GetVisibleAlertsAsync(new PageAlertContext(
                page,
                User.Identity?.IsAuthenticated == true,
                ViewData[PageMetaKeys.AlertProductId] as Guid?,
                ViewData[PageMetaKeys.AlertCategoryId] as Guid?));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Page alerts of {Page} could not be loaded.", page);
            return Content(string.Empty);
        }

        var selected = alerts.Where(a => ZoneOf(a.Position) == zone).ToList();
        if (selected.Count == 0)
            return Content(string.Empty);

        return View(new PageAlertsViewModel { Zone = zone, Alerts = selected });
    }

    private static AlertZone ZoneOf(AlertPosition position) => position switch
    {
        AlertPosition.Bottom or AlertPosition.BelowContent => AlertZone.Bottom,
        AlertPosition.BelowMainImage => AlertZone.BelowMainImage,
        _ => AlertZone.Top,
    };
}
