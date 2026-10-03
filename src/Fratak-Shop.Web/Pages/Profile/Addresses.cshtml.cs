using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.Services.Locations;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

[Authorize(Policy = PolicyNames.CustomerOnly)]
public class AddressesModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IUserAddressService addresses,
    ILocationService locations)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    public List<UserAddress> Items { get; private set; } = [];

    public List<Core.DTOs.Locations.Query.LocationDto> Provinces { get; private set; } = [];

    public Dictionary<Guid, string> CityNames { get; private set; } = [];

    [BindProperty]
    public AddressInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public Guid? Edit { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (CurrentUserId is not { } userId)
            return Redirect("/login?returnUrl=/profile/addresses");

        await LoadAsync(userId);
        ApplySeo(null, BuildFallback());
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (CurrentUserId is not { } userId)
            return Redirect("/login");

        await LoadAsync(userId);

        var entity = Input.Id.HasValue ? await addresses.GetAddressByIdAsync(Input.Id.Value) : new UserAddress { UserId = userId };

        if (entity is null || entity.UserId != userId)
        {
            Notify("آدرس مورد نظر یافت نشد.", "error");
            return RedirectToPage("/Profile/Addresses");
        }

        entity.Title = Input.Title;
        entity.FirstName = Input.FirstName;
        entity.LastName = Input.LastName;
        entity.ProvinceId = Input.ProvinceId;
        entity.CityId = Input.CityId;
        entity.AddressLine = Input.AddressLine;
        entity.BuildingNumber = Input.BuildingNumber;
        entity.Unit = Input.Unit;
        entity.PostalCode = Core.Utilities.PriceHelper.ToEnglishDigits(Input.PostalCode ?? string.Empty);
        entity.PhoneNumber = Core.Utilities.PriceHelper.ToEnglishDigits(Input.PhoneNumber ?? string.Empty);
        entity.IsDefault = Input.IsDefault;

        if (!ModelState.IsValid)
        {
            ApplySeo(null, BuildFallback());
            return Page();
        }

        var result = Input.Id.HasValue
            ? await addresses.UpdateAddressAsync(entity)
            : await addresses.CreateAddressAsync(entity);

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Profile/Addresses");
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        if (CurrentUserId is not { } userId)
            return Redirect("/login");

        var address = await addresses.GetAddressByIdAsync(id);
        if (address is null || address.UserId != userId)
        {
            Notify("آدرس مورد نظر یافت نشد.", "error");
            return RedirectToPage("/Profile/Addresses");
        }

        var result = await addresses.DeleteAddressAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Profile/Addresses");
    }

    private async Task LoadAsync(Guid userId)
    {
        Items = await addresses.GetAddressesByUserAsync(userId);

        var roots = await locations.GetActiveLocationsAsync();
        var country = roots.FirstOrDefault(item => item.ParentId is null);
        Provinces = country is null ? roots : await locations.GetChildrenByParentIdAsync(country.Id);

        foreach (var province in Provinces)
        {
            foreach (var city in await locations.GetChildrenByParentIdAsync(province.Id))
                CityNames[city.Id] = city.Name;
        }

        if (Edit.HasValue)
        {
            var current = Items.FirstOrDefault(item => item.Id == Edit.Value);
            if (current is not null)
            {
                Input = new AddressInput
                {
                    Id = current.Id,
                    Title = current.Title,
                    FirstName = current.FirstName,
                    LastName = current.LastName,
                    ProvinceId = current.ProvinceId,
                    CityId = current.CityId,
                    AddressLine = current.AddressLine,
                    BuildingNumber = current.BuildingNumber,
                    Unit = current.Unit,
                    PostalCode = current.PostalCode,
                    PhoneNumber = current.PhoneNumber,
                    IsDefault = current.IsDefault,
                };
            }
        }
    }

    private SeoFallback BuildFallback() => new()
    {
        SiteName = Site.SiteName,
        SiteUrl = Site.SiteUrl,
        Path = "/profile/addresses",
        ContentTitle = "آدرس‌های من",
        EntityType = SeoEntityType.Page,
        ForceNoIndex = true,
    };

    public class AddressInput
    {
        public Guid? Id { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "عنوان آدرس را وارد کنید.")]
        public string Title { get; set; } = null!;

        public string? FirstName { get; set; }
        public string? LastName { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "استان را انتخاب کنید.")]
        public Guid ProvinceId { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "شهر را انتخاب کنید.")]
        public Guid CityId { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "نشانی را وارد کنید.")]
        public string AddressLine { get; set; } = null!;

        public string? BuildingNumber { get; set; }
        public string? Unit { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "کد پستی را وارد کنید.")]
        [System.ComponentModel.DataAnnotations.StringLength(10, MinimumLength = 10, ErrorMessage = "کد پستی باید ۱۰ رقم باشد.")]
        public string? PostalCode { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "شماره تماس را وارد کنید.")]
        public string? PhoneNumber { get; set; }

        public bool IsDefault { get; set; }
    }
}
