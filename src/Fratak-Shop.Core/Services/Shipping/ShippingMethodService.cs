using Fratak_Shop.Core.DTOs.Shipping.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Shipping;

public class ShippingMethodService(FratakShopDbContext context) : IShippingMethodService
{
    public async Task<List<ShippingMethodDto>> GetActiveAsync(string? city = null)
    {
        var methods = await context.ShippingMethods
            .Where(method => method.IsActive &&
                             (method.RestrictedToCity == null || city == null || method.RestrictedToCity == city))
            .OrderBy(method => method.DisplayOrder)
            .ToListAsync();

        return methods.Select(Map).ToList();
    }

    public async Task<List<ShippingMethodDto>> GetAllAsync()
    {
        var methods = await context.ShippingMethods
            .OrderBy(method => method.DisplayOrder)
            .ToListAsync();

        return methods.Select(Map).ToList();
    }

    public async Task<ShippingMethodDto?> GetAsync(Guid id)
    {
        var method = await context.ShippingMethods.FirstOrDefaultAsync(item => item.Id == id);
        return method is null ? null : Map(method);
    }

    public async Task<OperationResult> SaveAsync(Guid? id, ShippingMethodDto model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
            return OperationResult.Error("نام روش ارسال را وارد کنید.");

        if (model.Cost < 0)
            return OperationResult.Error("هزینه ارسال نمی‌تواند منفی باشد.");

        if (id.HasValue)
        {
            var method = await context.ShippingMethods.AsTracking().FirstOrDefaultAsync(item => item.Id == id.Value);
            if (method is null)
                return OperationResult.NotFound("روش ارسال مورد نظر یافت نشد.");

            method.Name = model.Name.Trim();
            method.Description = model.Description;
            method.Cost = model.Cost;
            method.FreeShippingThreshold = model.FreeShippingThreshold;
            method.EstimatedDeliveryDays = model.EstimatedDeliveryDays;
            method.RestrictedToCity = model.RestrictedToCity;
            method.Icon = model.Icon;
            method.DisplayOrder = model.DisplayOrder;
            method.IsActive = model.IsActive;

            await context.SaveChangesAsync();
            return OperationResult.Success("روش ارسال ویرایش شد.");
        }

        context.ShippingMethods.Add(new ShippingMethod
        {
            Name = model.Name.Trim(),
            Description = model.Description,
            Cost = model.Cost,
            FreeShippingThreshold = model.FreeShippingThreshold,
            EstimatedDeliveryDays = model.EstimatedDeliveryDays,
            RestrictedToCity = model.RestrictedToCity,
            Icon = model.Icon,
            DisplayOrder = model.DisplayOrder,
            IsActive = model.IsActive,
        });

        await context.SaveChangesAsync();
        return OperationResult.Success("روش ارسال ثبت شد.");
    }

    public async Task<OperationResult> ToggleActiveAsync(Guid id)
    {
        var method = await context.ShippingMethods.AsTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (method is null)
            return OperationResult.NotFound("روش ارسال مورد نظر یافت نشد.");

        method.IsActive = !method.IsActive;
        await context.SaveChangesAsync();

        return OperationResult.Success(method.IsActive ? "روش ارسال فعال شد." : "روش ارسال غیرفعال شد.");
    }

    public async Task<OperationResult> DeleteAsync(Guid id)
    {
        var method = await context.ShippingMethods.AsTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (method is null)
            return OperationResult.NotFound("روش ارسال مورد نظر یافت نشد.");

        var used = await context.Orders.AnyAsync(order => order.ShippingMethodId == id);
        if (used)
        {
            method.IsActive = false;
            await context.SaveChangesAsync();
            return OperationResult.Success("این روش ارسال در سفارش‌ها استفاده شده است؛ به‌جای حذف غیرفعال شد.");
        }

        context.ShippingMethods.Remove(method);
        await context.SaveChangesAsync();
        return OperationResult.Success("روش ارسال حذف شد.");
    }

    private static ShippingMethodDto Map(ShippingMethod method) => new()
    {
        Id = method.Id,
        CreatedAt = method.CreatedAt,
        UpdatedAt = method.UpdatedAt,
        Name = method.Name,
        Description = method.Description,
        Cost = method.Cost,
        FreeShippingThreshold = method.FreeShippingThreshold,
        EstimatedDeliveryDays = method.EstimatedDeliveryDays,
        RestrictedToCity = method.RestrictedToCity,
        Icon = method.Icon,
        DisplayOrder = method.DisplayOrder,
        IsActive = method.IsActive,
    };
}
