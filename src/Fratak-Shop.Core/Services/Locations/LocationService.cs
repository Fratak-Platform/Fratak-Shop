using Fratak_Shop.Core.DTOs.Locations.Command;
using Fratak_Shop.Core.DTOs.Locations.Query;
using Fratak_Shop.Core.Mappers.Location;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Locations;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Locations;

/// <summary>
/// مدیریت دیکشنری مکان‌ها (کشور ← استان ← شهر). ساختار درخت با
/// <see cref="LocationType"/> و والد هر گره اعتبارسنجی می‌شود.
/// </summary>
public class LocationService(FratakShopDbContext context) : ILocationService
{
    public async Task<OperationResult> CreateLocationAsync(CreateLocationCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            return OperationResult.Error("نام مکان را وارد کنید.");

        if (string.IsNullOrWhiteSpace(command.Code))
            return OperationResult.Error("کد مکان را وارد کنید.");

        var parentError = await ValidateParentAsync(command.ParentId, command.Type);
        if (parentError is not null)
            return OperationResult.Error(parentError);

        var code = command.Code.Trim();

        var codeExists = await context.Locations.AnyAsync(x => x.Code == code);
        if (codeExists)
            return OperationResult.Error("این کد مکان در سیستم موجود است.");

        var model = new Location
        {
            ParentId = command.Type == LocationType.Country ? null : command.ParentId,
            Name = command.Name.Trim(),
            Code = code,
            Type = command.Type,
            IsActive = command.IsActive,
        };

        context.Locations.Add(model);
        await context.SaveChangesAsync();

        return OperationResult.Success("مکان با موفقیت اضافه شد.");
    }

    public async Task<OperationResult> EditLocationAsync(EditLocationCommand command)
    {
        var location = await context.Locations.AsTracking().FirstOrDefaultAsync(x => x.Id == command.Id);
        if (location is null)
            return OperationResult.NotFound("مکان مورد نظر یافت نشد.");

        if (string.IsNullOrWhiteSpace(command.Name))
            return OperationResult.Error("نام مکان را وارد کنید.");

        if (string.IsNullOrWhiteSpace(command.Code))
            return OperationResult.Error("کد مکان را وارد کنید.");

        if (command.ParentId == command.Id)
            return OperationResult.Error("مکان نمی‌تواند والد خودش باشد.");

        var parentError = await ValidateParentAsync(command.ParentId, command.Type);
        if (parentError is not null)
            return OperationResult.Error(parentError);

        var code = command.Code.Trim();

        var codeExists = await context.Locations
            .AnyAsync(x => x.Id != command.Id && x.Code == code);

        if (codeExists)
            return OperationResult.Error("این کد مکان در سیستم موجود است.");

        location.ParentId = command.Type == LocationType.Country ? null : command.ParentId;
        location.Name = command.Name.Trim();
        location.Code = code;
        location.Type = command.Type;
        location.IsActive = command.IsActive;

        await context.SaveChangesAsync();

        return OperationResult.Success("تغییرات مکان با موفقیت اعمال شد.");
    }

    public async Task<OperationResult> DeleteLocationAsync(Guid id)
    {
        var location = await context.Locations.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (location is null)
            return OperationResult.NotFound("مکان مورد نظر یافت نشد.");

        var hasChildren = await context.Locations.AnyAsync(x => x.ParentId == id);
        if (hasChildren)
            return OperationResult.Error("این مکان زیرمجموعه دارد؛ ابتدا زیرمجموعه‌ها را حذف کنید.");

        var isUsedInAddress = await context.UserAddresses
            .AnyAsync(x => x.ProvinceId == id || x.CityId == id);

        if (isUsedInAddress)
            return OperationResult.Error("این مکان در آدرس کاربران استفاده شده است و قابل حذف نیست.");

        context.Locations.Remove(location);
        await context.SaveChangesAsync();

        return OperationResult.Success("مکان با موفقیت حذف شد.");
    }

    public List<LocationDto> GetAllLocationsAsync()
    {
        return context.Locations
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name)
            .ToList()
            .Map();
    }

    public async Task<List<LocationDto>> GetActiveLocationsAsync()
    {
        var roots = await context.Locations
            .Where(x => x.IsActive && x.ParentId == null)
            .Include(x => x.Children)
            .ThenInclude(child => child.Children)
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name)
            .ToListAsync();

        var tree = roots.Map();

        // درخت مکان برای نمایش عمومی فقط شامل گره‌های فعال است.
        foreach (var root in tree)
        {
            root.Province = root.Province
                .Where(province => province.IsActive)
                .ToList();

            foreach (var province in root.Province)
            {
                province.Cities = province.Cities
                    .Where(city => city.IsActive)
                    .ToList();
            }
        }

        return tree;
    }

    public async Task<LocationDto?> GetLocationByIdAsync(Guid id)
    {
        var item = await context.Locations
            .Include(x => x.Children)
            .ThenInclude(child => child.Children)
            .FirstOrDefaultAsync(x => x.Id == id);

        return item?.Map();
    }

    public async Task<List<LocationDto>> GetChildrenByParentIdAsync(Guid? parentId)
    {
        var children = await context.Locations
            .Where(x => x.ParentId == parentId && x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync();

        return children.Map();
    }

    /// <summary>
    /// اعتبارسنجی والد بر اساس نوع مکان: کشور والد ندارد، استان زیر کشور و
    /// شهر زیر استان قرار می‌گیرد.
    /// </summary>
    private async Task<string?> ValidateParentAsync(Guid? parentId, LocationType type)
    {
        if (type == LocationType.None)
            return "نوع مکان را انتخاب کنید.";

        if (type == LocationType.Country)
            return parentId.HasValue && parentId.Value != Guid.Empty
                ? "کشور نمی‌تواند والد داشته باشد."
                : null;

        if (!parentId.HasValue || parentId.Value == Guid.Empty)
            return "والد مکان را انتخاب کنید.";

        var parent = await context.Locations.FirstOrDefaultAsync(x => x.Id == parentId.Value);
        if (parent is null)
            return "والد مکان مورد نظر یافت نشد.";

        var expectedParentType = type == LocationType.Province
            ? LocationType.Country
            : LocationType.Province;

        if (parent.Type != expectedParentType)
            return "ترتیب صحیح مکان‌ها کشور، استان و شهر است.";

        return null;
    }
}
