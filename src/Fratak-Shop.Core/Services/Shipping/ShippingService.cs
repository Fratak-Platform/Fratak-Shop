using Fratak_Shop.Core.DTOs.Shipping;
using Fratak_Shop.Core.Mappers.Shipping;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Shipping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Shipping;

public class ShippingService(
    FratakShopDbContext context,
    IMemoryCache cache,
    IStoreSettingService settingService) : IShippingService
{
    public async Task<List<ShippingMethodDto>> GetActiveMethodsAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue<List<ShippingMethodDto>>(CacheKeys.ShippingMethods, out var cached) && cached is not null)
            return cached;

        var methods = await context.ShippingMethods
            .Where(m => m.IsActive)
            .Include(m => m.Province)
            .Include(m => m.TimeSlots.Where(s => s.IsActive))
            .OrderBy(m => m.DisplayOrder)
            .ThenBy(m => m.Cost)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var result = methods.Select(m => m.Map()).ToList();
        cache.Set(CacheKeys.ShippingMethods, result, CacheKeys.DefaultLifetime);
        return result;
    }

    public async Task<List<ShippingOption>> GetOptionsAsync(Guid? provinceId, decimal subtotal, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetAsync(cancellationToken);
        var methods = (await GetActiveMethodsAsync(cancellationToken))
            .Where(m => IsAvailableFor(m, provinceId))
            .Where(m => !m.RequiresTimeSlot || m.TimeSlots.Count > 0)
            .ToList();

        var options = new List<ShippingOption>(methods.Count);
        foreach (var method in methods)
        {
            var threshold = method.FreeShippingThreshold ?? settings.FreeShippingThreshold;
            var option = new ShippingOption
            {
                Method = method,
                Cost = CalculateCost(method.Cost, threshold, subtotal),
                RemainingForFreeShipping = threshold > 0 && method.Cost > 0 && subtotal < threshold ? threshold - subtotal : null,
            };

            if (method.RequiresTimeSlot)
            {
                option.Days = await BuildScheduleAsync(method, settings.DeliveryDaysAhead, settings.OffDays,
                    settings.SameDayLeadMinutes, now, cancellationToken);

                // A courier method without any free slot in the visible window can't be chosen.
                if (!option.Days.Any(day => day.HasAvailableSlot))
                    continue;
            }

            options.Add(option);
        }

        return options;
    }

    public async Task<OperationResult<ShippingSelection?>> ValidateSelectionAsync(
        Guid shippingMethodId, Guid? provinceId, decimal subtotal,
        DateOnly? deliveryDate, Guid? timeSlotId, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetAsync(cancellationToken);
        var method = (await GetActiveMethodsAsync(cancellationToken)).FirstOrDefault(m => m.Id == shippingMethodId);

        if (method is null)
            return OperationResult<ShippingSelection?>.Error("روش ارسال انتخاب‌شده معتبر نیست.");

        if (!IsAvailableFor(method, provinceId))
            return OperationResult<ShippingSelection?>.Error($"«{method.Title}» برای استان انتخاب‌شده در دسترس نیست.");

        var selection = new ShippingSelection
        {
            ShippingMethodId = method.Id,
            ShippingMethodTitle = method.Title,
            SupportsCashOnDelivery = method.SupportsCashOnDelivery,
            Cost = CalculateCost(method.Cost, method.FreeShippingThreshold ?? settings.FreeShippingThreshold, subtotal),
        };

        if (!method.RequiresTimeSlot)
            return OperationResult<ShippingSelection?>.Success(selection);

        if (deliveryDate is null || timeSlotId is null)
            return OperationResult<ShippingSelection?>.Error("روز و بازه زمانی تحویل را انتخاب کنید.");

        var days = await BuildScheduleAsync(method, settings.DeliveryDaysAhead, settings.OffDays,
            settings.SameDayLeadMinutes, now, cancellationToken);

        var slot = days.FirstOrDefault(day => day.Date == deliveryDate.Value)?
            .Slots.FirstOrDefault(s => s.SlotId == timeSlotId.Value);

        if (slot is null)
            return OperationResult<ShippingSelection?>.Error("زمان تحویل انتخاب‌شده معتبر نیست.");

        if (!slot.IsAvailable)
            return OperationResult<ShippingSelection?>.Error($"بازه «{slot.Title}» {slot.UnavailableReason ?? "در دسترس نیست"}؛ زمان دیگری انتخاب کنید.");

        selection.DeliveryTimeSlotId = slot.SlotId;
        selection.DeliveryTimeSlotTitle = slot.Title;
        selection.RequestedDeliveryDate = IranClock.StartOfDay(deliveryDate.Value);
        return OperationResult<ShippingSelection?>.Success(selection);
    }

    private static bool IsAvailableFor(ShippingMethodDto method, Guid? provinceId) =>
        method.ProvinceId is null || method.ProvinceId == provinceId;

    private static decimal CalculateCost(decimal cost, decimal threshold, decimal subtotal) =>
        threshold > 0 && subtotal >= threshold ? 0 : Math.Max(0, cost);

    private async Task<List<DeliveryDayOption>> BuildScheduleAsync(
        ShippingMethodDto method, int daysAhead, IReadOnlySet<DayOfWeek> offDays,
        int sameDayLeadMinutes, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = IranClock.Today(now);
        var first = today.AddDays(Math.Max(0, method.MinDeliveryDays));
        var dates = new List<DateOnly>();

        // Collect "daysAhead" working days, skipping the store's off days.
        for (var date = first; dates.Count < Math.Max(1, daysAhead) && date <= first.AddDays(60); date = date.AddDays(1))
        {
            if (!offDays.Contains(date.DayOfWeek))
                dates.Add(date);
        }

        if (dates.Count == 0)
            return [];

        var usage = await GetSlotUsageAsync(method, dates[0], dates[^1], cancellationToken);
        var cutOff = now.AddMinutes(sameDayLeadMinutes);

        return dates.Select(date => new DeliveryDayOption
        {
            Date = date,
            IsToday = date == today,
            Slots = method.TimeSlots.Select(slot =>
            {
                var used = usage.GetValueOrDefault((slot.Id, date));
                string? reason = null;

                if (IranClock.At(date, slot.StartTime) <= cutOff)
                    reason = "گذشته است";
                else if (slot.Capacity is > 0 && used >= slot.Capacity)
                    reason = "تکمیل ظرفیت";

                return new DeliverySlotOption
                {
                    SlotId = slot.Id,
                    Title = slot.Title,
                    IsAvailable = reason is null,
                    UnavailableReason = reason,
                };
            }).ToList(),
        }).ToList();
    }

    /// <summary>Number of live orders booked per (slot, local date) in the window.</summary>
    private async Task<Dictionary<(Guid SlotId, DateOnly Date), int>> GetSlotUsageAsync(
        ShippingMethodDto method, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var slotIds = method.TimeSlots.Where(s => s.Capacity is > 0).Select(s => s.Id).ToList();
        if (slotIds.Count == 0)
            return [];

        var start = IranClock.StartOfDay(from);
        var end = IranClock.StartOfDay(to.AddDays(1));

        var rows = await context.Orders
            .Where(o => o.DeliveryTimeSlotId != null && slotIds.Contains(o.DeliveryTimeSlotId.Value))
            .Where(o => o.RequestedDeliveryDate >= start && o.RequestedDeliveryDate < end)
            .Where(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Returned)
            .GroupBy(o => new { SlotId = o.DeliveryTimeSlotId!.Value, Date = o.RequestedDeliveryDate!.Value })
            .Select(g => new { g.Key.SlotId, g.Key.Date, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => (r.SlotId, IranClock.Today(r.Date)))
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Count));
    }

    // ── Admin panel ──

    public async Task<List<ShippingMethodDto>> GetAllMethodsAsync()
    {
        var methods = await context.ShippingMethods
            .Include(m => m.Province)
            .Include(m => m.TimeSlots)
            .OrderBy(m => m.DisplayOrder)
            .AsSplitQuery()
            .ToListAsync();

        return methods.Select(m => m.Map()).ToList();
    }

    public async Task<ShippingMethodDto?> GetMethodByIdAsync(Guid id)
    {
        var method = await context.ShippingMethods
            .Include(m => m.Province)
            .Include(m => m.TimeSlots)
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.Id == id);

        return method?.Map();
    }

    public async Task<OperationResult<Guid>> SaveMethodAsync(ShippingMethodCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Title) || string.IsNullOrWhiteSpace(command.Code))
            return OperationResult<Guid>.Error("عنوان و کد روش ارسال الزامی است.");

        if (command.Cost < 0 || command.FreeShippingThreshold < 0)
            return OperationResult<Guid>.Error("هزینه ارسال نمی‌تواند منفی باشد.");

        if (command.MinDeliveryDays < 0 || command.MaxDeliveryDays < command.MinDeliveryDays)
            return OperationResult<Guid>.Error("بازه زمان تحویل معتبر نیست.");

        var code = command.Code.Trim().ToLowerInvariant();
        if (await context.ShippingMethods.AnyAsync(m => m.Code == code && m.Id != command.Id))
            return OperationResult<Guid>.Error("کد روش ارسال تکراری است.");

        if (command.ProvinceId is { } provinceId && !await context.Locations.AnyAsync(l => l.Id == provinceId))
            return OperationResult<Guid>.Error("استان انتخاب‌شده معتبر نیست.");

        ShippingMethod? method;
        if (command.Id == Guid.Empty)
        {
            method = new ShippingMethod();
            context.ShippingMethods.Add(method);
        }
        else
        {
            method = await context.ShippingMethods.AsTracking().FirstOrDefaultAsync(m => m.Id == command.Id);
            if (method is null)
                return OperationResult<Guid>.NotFound();
        }

        method.Apply(command);
        await context.SaveChangesAsync();
        cache.Remove(CacheKeys.ShippingMethods);

        return OperationResult<Guid>.Success(method.Id, "روش ارسال ذخیره شد.");
    }

    public async Task<OperationResult> DeleteMethodAsync(Guid id)
    {
        var method = await context.ShippingMethods.AsTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (method is null)
            return OperationResult.NotFound();

        context.ShippingMethods.Remove(method);
        await context.SaveChangesAsync();
        cache.Remove(CacheKeys.ShippingMethods);
        return OperationResult.Success("روش ارسال حذف شد.");
    }

    public async Task<OperationResult<Guid>> SaveTimeSlotAsync(DeliveryTimeSlotCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Title))
            return OperationResult<Guid>.Error("عنوان بازه زمانی الزامی است.");

        if (command.EndTime <= command.StartTime)
            return OperationResult<Guid>.Error("ساعت پایان باید بعد از ساعت شروع باشد.");

        if (command.Capacity is < 0)
            return OperationResult<Guid>.Error("ظرفیت نمی‌تواند منفی باشد.");

        if (!await context.ShippingMethods.AnyAsync(m => m.Id == command.ShippingMethodId))
            return OperationResult<Guid>.Error("روش ارسال معتبر نیست.");

        DeliveryTimeSlot? slot;
        if (command.Id == Guid.Empty)
        {
            slot = new DeliveryTimeSlot();
            context.DeliveryTimeSlots.Add(slot);
        }
        else
        {
            slot = await context.DeliveryTimeSlots.AsTracking().FirstOrDefaultAsync(s => s.Id == command.Id);
            if (slot is null)
                return OperationResult<Guid>.NotFound();
        }

        slot.Apply(command);
        await context.SaveChangesAsync();
        cache.Remove(CacheKeys.ShippingMethods);

        return OperationResult<Guid>.Success(slot.Id, "بازه زمانی ذخیره شد.");
    }

    public async Task<OperationResult> DeleteTimeSlotAsync(Guid id)
    {
        var slot = await context.DeliveryTimeSlots.AsTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (slot is null)
            return OperationResult.NotFound();

        context.DeliveryTimeSlots.Remove(slot);
        await context.SaveChangesAsync();
        cache.Remove(CacheKeys.ShippingMethods);
        return OperationResult.Success("بازه زمانی حذف شد.");
    }
}
