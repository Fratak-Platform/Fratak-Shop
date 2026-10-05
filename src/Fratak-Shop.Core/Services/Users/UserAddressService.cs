using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Users;

public class UserAddressService(FratakShopDbContext context) : IUserAddressService
{
    public async Task<OperationResult> CreateAddressAsync(UserAddress address)
    {
        if (address is null)
            return OperationResult.Error("آدرس نامعتبر است.");

        if (address.UserId == Guid.Empty)
            return OperationResult.Error("شناسه کاربر معتبر نیست.");

        if (string.IsNullOrWhiteSpace(address.Title))
            return OperationResult.Error("عنوان آدرس را وارد کنید.");

        if (string.IsNullOrWhiteSpace(address.AddressLine))
            return OperationResult.Error("آدرس را وارد کنید.");

        if (address.ProvinceId == Guid.Empty || address.CityId == Guid.Empty)
            return OperationResult.Error("استان و شهر را انتخاب کنید.");

        var cityBelongsToProvince = await context.Locations
            .AnyAsync(x => x.Id == address.CityId && x.ParentId == address.ProvinceId && x.IsActive);
        if (!cityBelongsToProvince)
            return OperationResult.Error("شهر انتخاب‌شده با استان انتخابی هماهنگ نیست.");

        var userExists = await context.Users.AnyAsync(x => x.Id == address.UserId && !x.IsDeleted);
        if (!userExists)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        context.Addresses.Add(address);
        await context.SaveChangesAsync();

        return OperationResult.Success("آدرس با موفقیت ایجاد شد.");
    }

    public async Task<OperationResult> UpdateAddressAsync(UserAddress address)
    {
        if (address is null)
            return OperationResult.Error("آدرس نامعتبر است.");

        var entity = await context.Addresses.AsTracking().FirstOrDefaultAsync(x => x.Id == address.Id && !x.IsDeleted);
        if (entity is null)
            return OperationResult.NotFound("آدرس مورد نظر یافت نشد.");

        if (string.IsNullOrWhiteSpace(address.Title))
            return OperationResult.Error("عنوان آدرس را وارد کنید.");

        if (string.IsNullOrWhiteSpace(address.AddressLine))
            return OperationResult.Error("آدرس را وارد کنید.");

        if (address.ProvinceId == Guid.Empty || address.CityId == Guid.Empty)
            return OperationResult.Error("استان و شهر را انتخاب کنید.");

        var cityBelongsToProvince = await context.Locations
            .AnyAsync(x => x.Id == address.CityId && x.ParentId == address.ProvinceId && x.IsActive);
        if (!cityBelongsToProvince)
            return OperationResult.Error("شهر انتخاب‌شده با استان انتخابی هماهنگ نیست.");

        entity.UserId = address.UserId;
        entity.ProvinceId = address.ProvinceId;
        entity.CityId = address.CityId;
        entity.Title = address.Title;
        entity.FirstName = address.FirstName;
        entity.LastName = address.LastName;
        entity.AddressLine = address.AddressLine;
        entity.BuildingNumber = address.BuildingNumber;
        entity.Unit = address.Unit;
        entity.PostalCode = address.PostalCode;
        entity.PhoneNumber = address.PhoneNumber;
        entity.IsDefault = address.IsDefault;

        await context.SaveChangesAsync();

        return OperationResult.Success("آدرس با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteAddressAsync(Guid id)
    {
        var entity = await context.Addresses.AsTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (entity is null)
            return OperationResult.NotFound("آدرس مورد نظر یافت نشد.");

        context.Addresses.Remove(entity);
        await context.SaveChangesAsync();

        return OperationResult.Success("آدرس با موفقیت حذف شد.");
    }

    public async Task<UserAddress?> GetAddressByIdAsync(Guid id)
    {
        return await context.Addresses
            .Include(x => x.Province)
            .Include(x => x.City)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
    }

    public async Task<List<UserAddress>> GetAddressesByUserAsync(Guid userId)
    {
        return await context.Addresses
            .Where(x => x.UserId == userId && !x.IsDeleted)
            .OrderByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.CreatedAt)
            .ToListAsync();
    }
}
