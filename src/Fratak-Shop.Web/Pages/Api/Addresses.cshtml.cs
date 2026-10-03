using Fratak_Shop.Core.Services.Locations;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fratak_Shop.Web.Pages.Api;

/// <summary>Saved address of the signed-in customer, used by the checkout form.</summary>
[Authorize(Policy = PolicyNames.CustomerOnly)]
public class AddressesModel(IUserAddressService addresses, ILocationService locations) : PageModel
{
    public async Task<IActionResult> OnGetAddressAsync(Guid id)
    {
        var userId = Guid.TryParse(User.FindFirst(FratakClaims.UserId)?.Value, out var parsed) ? parsed : Guid.Empty;
        var address = await addresses.GetAddressByIdAsync(id);

        if (address is null || address.UserId != userId)
            return NotFound();

        var province = await locations.GetLocationByIdAsync(address.ProvinceId);
        var city = await locations.GetLocationByIdAsync(address.CityId);

        return new JsonResult(new
        {
            fullName = $"{address.FirstName} {address.LastName}".Trim(),
            address.PhoneNumber,
            province = province?.Name,
            city = city?.Name,
            addressLine = address.AddressLine,
            postalCode = address.PostalCode,
            buildingNumber = address.BuildingNumber,
            unit = address.Unit,
        });
    }
}
