using Fratak_Shop.Core.DTOs.Locations.Query;
using Fratak_Shop.Core.DTOs.Users.Query;
using Fratak_Shop.Core.Services.Locations;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Models;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

/// <summary>Customer address book (add, edit, delete, choose default).</summary>
public class AddressesModel(
    IUserAddressService addressService,
    ILocationService locationService,
    IPageMetaService pageMeta) : ShopPageModel
{
    public List<UserAddressDto> Addresses { get; private set; } = [];
    public List<LocationDto> Provinces { get; private set; } = [];

    /// <summary>True when the edit modal must be open on page load (edit link or validation error).</summary>
    public bool OpenModal { get; private set; }

    [BindProperty]
    public AddressInput Address { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid? edit)
    {
        await LoadAsync();

        if (edit.HasValue)
        {
            var address = Addresses.FirstOrDefault(a => a.Id == edit.Value);
            if (address is null)
                return NotFound();

            Address = AddressInput.From(address);
            OpenModal = true;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            OpenModal = true;
            ErrorMessage = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault();
            return Page();
        }

        var userId = CurrentUserId!.Value;

        if (Address.Id.HasValue && Address.Id != Guid.Empty)
        {
            if (!await OwnsAsync(Address.Id.Value))
                return NotFound();

            ShowResult(await addressService.EditAddressAsync(Address.ToEditCommand(userId)));
        }
        else
        {
            ShowResult(await addressService.CreateAddressAsync(Address.ToCreateCommand(userId)));
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        if (!await OwnsAsync(id))
            return NotFound();

        ShowResult(await addressService.DeleteAddressAsync(id));
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDefaultAsync(Guid id)
    {
        if (!await OwnsAsync(id))
            return NotFound();

        ShowResult(await addressService.SetDefaultAddressAsync(id));
        return RedirectToPage();
    }

    private async Task<bool> OwnsAsync(Guid addressId)
    {
        var address = await addressService.GetAddressByIdAsync(addressId);
        return address is not null && address.UserId == CurrentUserId;
    }

    private async Task LoadAsync()
    {
        Addresses = await addressService.GetAddressesByUserAsync(CurrentUserId!.Value);
        Provinces = await locationService.GetProvincesAsync();

        await pageMeta.ApplyAsync(ViewData, SitePage.UserAddresses, "آدرس های من");
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
    }
}
