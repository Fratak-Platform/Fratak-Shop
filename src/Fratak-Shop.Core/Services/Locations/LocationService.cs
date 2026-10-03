using Fratak_Shop.Core.DTOs.Locations.Command;
using Fratak_Shop.Core.DTOs.Locations.Query;
using Fratak_Shop.Core.Mappers.Location;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Locations;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Locations;

public class LocationService(FratakShopDbContext context) : ILocationService
{
    public async Task<OperationResult> CreateLocationAsync(CreateLocationCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            return OperationResult.Error("نام مکان را وارد کنید.");

        if (string.IsNullOrWhiteSpace(command.Code))
            return OperationResult.Error("کد مکان را وارد کنید.");

        if (command.ParentId.HasValue)
        {
            var parentExists = await context.Locations.AnyAsync(x => x.Id == command.ParentId.Value);
            if (!parentExists)
                return OperationResult.NotFound("والد مکان مورد نظر یافت نشد.");
        }

        var codeExists = await context.Locations.AnyAsync(x => x.Code == command.Code.Trim());
        if (codeExists)
            return OperationResult.Error("این کد مکان در سیستم موجود است.");

        var type = command.Type;

        var model = new Location
        {
            ParentId = command.ParentId,
            Name = command.Name.Trim(),
            Code = command.Code.Trim(),
            Type = type,
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

        if (command.ParentId.HasValue)
        {
            var parentExists = await context.Locations.AnyAsync(x => x.Id == command.ParentId.Value && x.Id != command.Id);
            if (!parentExists)
                return OperationResult.NotFound("والد مکان مورد نظر یافت نشد.");
        }

        var codeExists = await context.Locations.AnyAsync(x => x.Id != command.Id && x.Code == command.Code.Trim());
        if (codeExists)
            return OperationResult.Error("این کد مکان در سیستم موجود است.");

        location.ParentId = command.ParentId;
        location.Name = command.Name.Trim();
        location.Code = command.Code.Trim();
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

        context.Locations.Remove(location);
        await context.SaveChangesAsync();

        return OperationResult.Success("مکان با موفقیت حذف شد.");
    }

    public List<LocationDto> GetAllLocationsAsync()
    {
        return context.Locations
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name)
            .Select(x => x.Map()).ToList();
    }

    public async Task<List<LocationDto>> GetActiveLocationsAsync()
    {
        return await context.Locations
            .Where(x => x.IsActive && x.ParentId == null)
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name).Select(s => s.Map()).ToListAsync();

    }

    public async Task<LocationDto?> GetLocationByIdAsync(Guid id)
    {
        var item = await context.Locations
            .Include(x => x.Children)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (item is null)
            return null;

        return item.Map();
    }

    public async Task<List<LocationDto>> GetChildrenByParentIdAsync(Guid? parentId)
    {
        return await context.Locations
            .Where(x => x.ParentId == parentId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => x.Map()).ToListAsync();
    }
}
