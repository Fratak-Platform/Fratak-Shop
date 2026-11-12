using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.DTOs.Users.Query;

namespace Fratak_Shop.Web.Models;

/// <summary>
/// Address form used by the checkout modal and the customer address book.
/// </summary>
public class AddressInput
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "عنوان آدرس را وارد کنید.")]
    [MaxLength(100)]
    public string Title { get; set; } = "خانه";

    [Required(ErrorMessage = "نام گیرنده را وارد کنید.")]
    [MaxLength(100)]
    public string? FirstName { get; set; }

    [Required(ErrorMessage = "نام خانوادگی گیرنده را وارد کنید.")]
    [MaxLength(100)]
    public string? LastName { get; set; }

    [Required(ErrorMessage = "استان را انتخاب کنید.")]
    public Guid? ProvinceId { get; set; }

    [Required(ErrorMessage = "شهر را انتخاب کنید.")]
    public Guid? CityId { get; set; }

    [Required(ErrorMessage = "نشانی را وارد کنید.")]
    [MaxLength(500)]
    public string AddressLine { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? BuildingNumber { get; set; }

    [MaxLength(50)]
    public string? Unit { get; set; }

    [Required(ErrorMessage = "کد پستی را وارد کنید.")]
    [RegularExpression(@"^\d{10}$", ErrorMessage = "کد پستی باید ۱۰ رقم باشد.")]
    public string PostalCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "شماره تماس را وارد کنید.")]
    [RegularExpression(@"^0\d{10}$", ErrorMessage = "شماره تماس باید ۱۱ رقم و با ۰ شروع شود.")]
    public string PhoneNumber { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    /// <summary>Optional map location (filled by the map picker).</summary>
    [Range(-90, 90)]
    [Microsoft.AspNetCore.Mvc.ModelBinder(typeof(Infrastructure.ModelBinding.InvariantDecimalModelBinder))]
    public decimal? Latitude { get; set; }

    [Range(-180, 180)]
    [Microsoft.AspNetCore.Mvc.ModelBinder(typeof(Infrastructure.ModelBinding.InvariantDecimalModelBinder))]
    public decimal? Longitude { get; set; }

    public CreateUserAddressCommand ToCreateCommand(Guid userId) => new()
    {
        UserId = userId,
        ProvinceId = ProvinceId ?? Guid.Empty,
        CityId = CityId ?? Guid.Empty,
        Title = Title,
        FirstName = FirstName,
        LastName = LastName,
        AddressLine = AddressLine,
        BuildingNumber = BuildingNumber,
        Unit = Unit,
        PostalCode = PostalCode,
        PhoneNumber = PhoneNumber,
        IsDefault = IsDefault,
        Latitude = Latitude,
        Longitude = Longitude,
    };

    public EditUserAddressCommand ToEditCommand(Guid userId) => new()
    {
        Id = Id ?? Guid.Empty,
        UserId = userId,
        ProvinceId = ProvinceId ?? Guid.Empty,
        CityId = CityId ?? Guid.Empty,
        Title = Title,
        FirstName = FirstName,
        LastName = LastName,
        AddressLine = AddressLine,
        BuildingNumber = BuildingNumber,
        Unit = Unit,
        PostalCode = PostalCode,
        PhoneNumber = PhoneNumber,
        IsDefault = IsDefault,
        Latitude = Latitude,
        Longitude = Longitude,
    };

    public static AddressInput From(UserAddressDto address) => new()
    {
        Id = address.Id,
        Title = address.Title,
        FirstName = address.FirstName,
        LastName = address.LastName,
        ProvinceId = address.ProvinceId,
        CityId = address.CityId,
        AddressLine = address.AddressLine,
        BuildingNumber = address.BuildingNumber,
        Unit = address.Unit,
        PostalCode = address.PostalCode,
        PhoneNumber = address.PhoneNumber,
        IsDefault = address.IsDefault,
        Latitude = address.Latitude,
        Longitude = address.Longitude,
    };
}

/// <summary>Model of the <c>_AddressFields</c> partial.</summary>
public record AddressFieldsModel(AddressInput Address, List<Fratak_Shop.Core.DTOs.Locations.Query.LocationDto> Provinces, string Prefix = "Address");
