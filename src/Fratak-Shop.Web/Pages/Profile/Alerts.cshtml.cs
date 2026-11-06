using Fratak_Shop.Core.DTOs.Engagement;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

/// <summary>«خبرم کن» list: back-in-stock and price-drop alerts of the customer.</summary>
public class AlertsModel(IProductAlertService alerts, ISiteContext site) : ShopPageModel
{
    public List<ProductAlertDto> Items { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!site.Settings.IsProductAlertEnabled)
            return NotFound();

        Items = await alerts.GetUserAlertsAsync(CurrentUserId!.Value);
        ViewData[PageMetaKeys.Title] = "خبرم کن";
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await alerts.DeleteAsync(CurrentUserId!.Value, id);
        if (result.IsSuccess())
            SuccessMessage = result.Message ?? "اطلاع‌رسانی حذف شد.";
        else
            ErrorMessage = result.Message ?? "اطلاع‌رسانی یافت نشد.";
        return RedirectToPage();
    }
}
