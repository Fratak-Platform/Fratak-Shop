using Fratak_Shop.Core.Services.Locations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fratak_Shop.Web.Pages.Api;

/// <summary>City list of a province for the cascading selects of the checkout/address forms.</summary>
public class LocationsModel(ILocationService locations) : PageModel
{
    public async Task<IActionResult> OnGetProvincesAsync()
    {
        var roots = await locations.GetActiveLocationsAsync();
        var country = roots.FirstOrDefault(item => item.ParentId is null);

        var provinces = country is null
            ? roots
            : await locations.GetChildrenByParentIdAsync(country.Id);

        return new JsonResult(provinces.Select(item => new { item.Id, item.Name }));
    }

    public async Task<IActionResult> OnGetCitiesAsync(Guid provinceId)
    {
        var cities = await locations.GetChildrenByParentIdAsync(provinceId);
        return new JsonResult(cities.Select(item => new { item.Id, item.Name }));
    }
}
