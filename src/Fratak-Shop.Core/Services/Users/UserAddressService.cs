using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.DTOs.Users.Query;
using Fratak_Shop.Core.Mappers.Users;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Locations;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Users;

/// <summary>
/// مدیریت آدرس‌های کاربران. استان و شهر از دیکشنری Locations اعتبارسنجی می‌شوند و
/// همیشه فقط یک آدرس پیش‌فرض برای هر کاربر باقی می‌ماند.
/// </summary>
public class UserAddressService(FratakShopDbContext context) : IUserAddressService
{
    public async Task<OperationResult> CreateAddressAsync(CreateUserAddressCommand command)
    {
        var error = Validate(command.Title, command.AddressLine, command.PostalCode, command.PhoneNumber)
                    ?? ValidateGeo(command.Latitude, command.Longitude);
        if (error is not null)
            return OperationResult.Error(error);

        if (command.UserId == Guid.Empty)
            return OperationResult.Error("شناسه کاربر معتبر نیست.");

        var userExists = await context.Users.AnyAsync(x => x.Id == command.UserId);
        if (!userExists)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        var locationError = await ValidateLocationAsync(command.ProvinceId, command.CityId);
        if (locationError is not null)
            return OperationResult.Error(locationError);

        // اولین آدرس کاربر به‌صورت خودکار پیش‌فرض می‌شود.
        var hasAnyAddress = await context.UserAddresses.AnyAsync(x => x.UserId == command.UserId);
        var isDefault = command.IsDefault || !hasAnyAddress;

        if (isDefault)
            await UnsetOtherDefaultsAsync(command.UserId, Guid.Empty);

        var address = new UserAddress
        {
            UserId = command.UserId,
            ProvinceId = command.ProvinceId,
            CityId = command.CityId,
            Title = command.Title.Trim(),
            FirstName = command.FirstName?.Trim(),
            LastName = command.LastName?.Trim(),
            AddressLine = command.AddressLine.Trim(),
            BuildingNumber = command.BuildingNumber?.Trim(),
            Unit = command.Unit?.Trim(),
            PostalCode = command.PostalCode.Trim(),
            PhoneNumber = command.PhoneNumber.Trim(),
            IsDefault = isDefault,
            Latitude = RoundGeo(command.Latitude),
            Longitude = RoundGeo(command.Longitude),
        };

        context.UserAddresses.Add(address);
        await context.SaveChangesAsync();

        return OperationResult.Success("آدرس با موفقیت ثبت شد.");
    }

    public async Task<OperationResult> EditAddressAsync(EditUserAddressCommand command)
    {
        var address = await context.UserAddresses
            .AsTracking()
            .FirstOrDefaultAsync(x => x.Id == command.Id);

        if (address is null)
            return OperationResult.NotFound("آدرس مورد نظر یافت نشد.");

        var error = Validate(command.Title, command.AddressLine, command.PostalCode, command.PhoneNumber)
                    ?? ValidateGeo(command.Latitude, command.Longitude);
        if (error is not null)
            return OperationResult.Error(error);

        var locationError = await ValidateLocationAsync(command.ProvinceId, command.CityId);
        if (locationError is not null)
            return OperationResult.Error(locationError);

        // تنها آدرس کاربر همیشه پیش‌فرض می‌ماند.
        var isOnlyAddress = !await context.UserAddresses
            .AnyAsync(x => x.UserId == address.UserId && x.Id != address.Id);

        var isDefault = command.IsDefault || isOnlyAddress;

        if (isDefault)
            await UnsetOtherDefaultsAsync(address.UserId, address.Id);

        address.ProvinceId = command.ProvinceId;
        address.CityId = command.CityId;
        address.Title = command.Title.Trim();
        address.FirstName = command.FirstName?.Trim();
        address.LastName = command.LastName?.Trim();
        address.AddressLine = command.AddressLine.Trim();
        address.BuildingNumber = command.BuildingNumber?.Trim();
        address.Unit = command.Unit?.Trim();
        address.PostalCode = command.PostalCode.Trim();
        address.PhoneNumber = command.PhoneNumber.Trim();
        address.IsDefault = isDefault;
        address.Latitude = RoundGeo(command.Latitude);
        address.Longitude = RoundGeo(command.Longitude);

        await context.SaveChangesAsync();

        return OperationResult.Success("آدرس با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteAddressAsync(Guid id)
    {
        var address = await context.UserAddresses
            .AsTracking()
            .FirstOrDefaultAsync(x => x.Id == id);

        if (address is null)
            return OperationResult.NotFound("آدرس مورد نظر یافت نشد.");

        var userId = address.UserId;

        // اگر آدرس پیش‌فرض حذف شود، جدیدترین آدرس باقی‌مانده پیش‌فرض می‌شود.
        if (address.IsDefault)
        {
            var replacement = await context.UserAddresses
                .AsTracking()
                .Where(x => x.UserId == userId && x.Id != address.Id)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

            if (replacement is not null)
                replacement.IsDefault = true;
        }

        context.UserAddresses.Remove(address);
        await context.SaveChangesAsync();

        return OperationResult.Success("آدرس با موفقیت حذف شد.");
    }

    public async Task<OperationResult> SetDefaultAddressAsync(Guid id)
    {
        var address = await context.UserAddresses
            .AsTracking()
            .FirstOrDefaultAsync(x => x.Id == id);

        if (address is null)
            return OperationResult.NotFound("آدرس مورد نظر یافت نشد.");

        if (address.IsDefault)
            return OperationResult.Success("این آدرس از قبل پیش‌فرض شماست.");

        await UnsetOtherDefaultsAsync(address.UserId, address.Id);

        address.IsDefault = true;

        await context.SaveChangesAsync();

        return OperationResult.Success("آدرس پیش‌فرض با موفقیت تغییر کرد.");
    }

    public async Task<UserAddressDto?> GetAddressByIdAsync(Guid id)
    {
        var address = await context.UserAddresses
            .Include(x => x.Province)
            .Include(x => x.City)
            .FirstOrDefaultAsync(x => x.Id == id);

        return address?.Map();
    }

    public async Task<List<UserAddressDto>> GetAddressesByUserAsync(Guid userId)
    {
        var addresses = await context.UserAddresses
            .Where(x => x.UserId == userId)
            .Include(x => x.Province)
            .Include(x => x.City)
            .OrderByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.CreatedAt)
            .ToListAsync();

        return addresses.Map();
    }

    private async Task UnsetOtherDefaultsAsync(Guid userId, Guid exceptId)
    {
        var defaultAddresses = await context.UserAddresses
            .AsTracking()
            .Where(x => x.UserId == userId && x.Id != exceptId && x.IsDefault)
            .ToListAsync();

        foreach (var address in defaultAddresses)
            address.IsDefault = false;
    }

    private async Task<string?> ValidateLocationAsync(Guid provinceId, Guid cityId)
    {
        if (provinceId == Guid.Empty)
            return "استان را انتخاب کنید.";

        if (cityId == Guid.Empty)
            return "شهر را انتخاب کنید.";

        var provinceExists = await context.Locations
            .AnyAsync(x => x.Id == provinceId && x.Type == LocationType.Province);

        if (!provinceExists)
            return "استان انتخابی یافت نشد.";

        var cityExists = await context.Locations
            .AnyAsync(x => x.Id == cityId && x.Type == LocationType.City && x.ParentId == provinceId);

        if (!cityExists)
            return "شهر انتخابی در استان انتخاب‌شده یافت نشد.";

        return null;
    }

    private static string? Validate(string? title, string? addressLine, string? postalCode, string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "عنوان آدرس (مثلاً خانه یا محل کار) را وارد کنید.";

        if (string.IsNullOrWhiteSpace(addressLine))
            return "نشانی کامل پستی را وارد کنید.";

        var normalizedPostalCode = postalCode?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedPostalCode)
            || normalizedPostalCode.Length != 10
            || !normalizedPostalCode.All(char.IsDigit))
            return "کد پستی باید ۱۰ رقم باشد.";

        if (string.IsNullOrWhiteSpace(phoneNumber) || !IsValidPhoneNumber(phoneNumber))
            return "شماره تماس آدرس معتبر نیست.";

        return null;
    }

    /// <summary>شماره موبایل (۰۹xxxxxxxxx) یا تلفن ثابت (۰۲۱xxxxxxxx) را می‌پذیرد.</summary>
    private static bool IsValidPhoneNumber(string phoneNumber)
    {
        var phone = phoneNumber.Trim();

        return phone.IsValidMobile() || phone.IsValidPhone();
    }

    /// <summary>Map location: both coordinates or none, inside the valid range.</summary>
    public static string? ValidateGeo(decimal? latitude, decimal? longitude)
    {
        if (latitude.HasValue != longitude.HasValue)
            return "موقعیت روی نقشه کامل نیست؛ دوباره روی نقشه انتخاب کنید.";
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
            return "موقعیت انتخاب‌شده روی نقشه معتبر نیست.";
        return null;
    }

    private static decimal? RoundGeo(decimal? value) => value.HasValue ? Math.Round(value.Value, 6) : null;
}
