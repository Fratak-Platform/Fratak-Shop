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

    public async Task<List<ShippingRateDto>> GetActiveRatesAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue<List<ShippingRateDto>>(CacheKeys.ShippingRates, out var cached) && cached is not null)
            return cached;

        var rates = await context.ShippingRates
            .Where(r => r.IsActive)
            .Select(r => new ShippingRateDto
            {
                Id = r.Id,
                ShippingMethodId = r.ShippingMethodId,
                ProvinceId = r.ProvinceId,
                CityId = r.CityId,
                Cost = r.Cost,
                FreeShippingThreshold = r.FreeShippingThreshold,
                ExtraDeliveryDays = r.ExtraDeliveryDays,
                IsBlocked = r.IsBlocked,
                Note = r.Note,
                IsActive = r.IsActive,
            })
            .ToListAsync(cancellationToken);

        cache.Set(CacheKeys.ShippingRates, rates, CacheKeys.DefaultLifetime);
        return rates;
    }

    public async Task<List<ShippingOption>> GetOptionsAsync(ShippingDestination destination, decimal subtotal, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetAsync(cancellationToken);
        var rates = await GetActiveRatesAsync(cancellationToken);
        var methods = (await GetActiveMethodsAsync(cancellationToken))
            .Where(m => !m.RequiresTimeSlot || m.TimeSlots.Count > 0)
            .ToList();

        var options = new List<ShippingOption>(methods.Count);
        foreach (var method in methods)
        {
            var resolved = Resolve(method, rates, destination, settings.FreeShippingThreshold);
            if (!resolved.IsAvailable)
                continue;

            var option = new ShippingOption
            {
                Method = method,
                Cost = CalculateCost(resolved.Cost, resolved.Threshold, subtotal),
                RemainingForFreeShipping = resolved.Threshold > 0 && resolved.Cost > 0 && subtotal < resolved.Threshold ? resolved.Threshold - subtotal : null,
                DeliveryEstimate = ShippingMethodDto.Estimate(method.MinDeliveryDays + resolved.ExtraDays, method.MaxDeliveryDays + resolved.ExtraDays),
            };

            if (method.RequiresTimeSlot)
            {
                option.Days = await BuildScheduleAsync(method, resolved.ExtraDays, settings.DeliveryDaysAhead, settings.OffDays,
                    settings.SameDayLeadMinutes, now, cancellationToken);

                // A courier method without any free slot in the visible window can't be chosen.
                if (!option.Days.Any(day => day.HasAvailableSlot))
                    continue;
            }

            options.Add(option);
        }

        return options;
    }

    public async Task<List<UnavailableShippingMethod>> GetUnavailableMethodsAsync(ShippingDestination destination, CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetAsync(cancellationToken);
        var rates = await GetActiveRatesAsync(cancellationToken);
        var methods = await GetActiveMethodsAsync(cancellationToken);

        return methods
            .Select(m => (Method: m, Resolved: Resolve(m, rates, destination, settings.FreeShippingThreshold)))
            .Where(x => !x.Resolved.IsAvailable && x.Resolved.Reason is not null)
            .Select(x => new UnavailableShippingMethod(x.Method.Title, x.Resolved.Reason!))
            .ToList();
    }

    public async Task<OperationResult<ShippingSelection?>> ValidateSelectionAsync(
        Guid shippingMethodId, ShippingDestination destination, decimal subtotal,
        DateOnly? deliveryDate, Guid? timeSlotId, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetAsync(cancellationToken);
        var method = (await GetActiveMethodsAsync(cancellationToken)).FirstOrDefault(m => m.Id == shippingMethodId);

        if (method is null)
            return OperationResult<ShippingSelection?>.Error("روش ارسال انتخاب‌شده معتبر نیست.");

        var rates = await GetActiveRatesAsync(cancellationToken);
        var resolved = Resolve(method, rates, destination, settings.FreeShippingThreshold);
        if (!resolved.IsAvailable)
            return OperationResult<ShippingSelection?>.Error($"«{method.Title}» برای آدرس انتخاب‌شده در دسترس نیست{(resolved.Reason is null ? "" : $" ({resolved.Reason})")}.");

        var selection = new ShippingSelection
        {
            ShippingMethodId = method.Id,
            ShippingMethodTitle = method.Title,
            SupportsCashOnDelivery = method.SupportsCashOnDelivery,
            Cost = CalculateCost(resolved.Cost, resolved.Threshold, subtotal),
        };

        if (!method.RequiresTimeSlot)
            return OperationResult<ShippingSelection?>.Success(selection);

        if (deliveryDate is null || timeSlotId is null)
            return OperationResult<ShippingSelection?>.Error("روز و بازه زمانی تحویل را انتخاب کنید.");

        var days = await BuildScheduleAsync(method, resolved.ExtraDays, settings.DeliveryDaysAhead, settings.OffDays,
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

    private readonly record struct ResolvedShipping(bool IsAvailable, string? Reason, decimal Cost, decimal Threshold, int ExtraDays);

    /// <summary>
    /// Cost / availability of a method for a destination. The most specific active rule wins:
    /// a city rule before a province rule, a rule of this method before a rule of all methods.
    /// A blocked winner means "no delivery"; a city rule can re-open a blocked province.
    /// </summary>
    private static ResolvedShipping Resolve(ShippingMethodDto method, List<ShippingRateDto> rates, ShippingDestination destination, decimal storeThreshold)
    {
        if (method.ProvinceId is not null && method.ProvinceId != destination.ProvinceId)
            return new ResolvedShipping(false, "فقط برای استان دیگری ارائه می‌شود", 0, 0, 0);

        var rate = rates
            .Where(r => r.ShippingMethodId is null || r.ShippingMethodId == method.Id)
            .Where(r => (r.CityId is not null && r.CityId == destination.CityId)
                        || (r.CityId is null && r.ProvinceId is not null && r.ProvinceId == destination.ProvinceId))
            .OrderByDescending(r => r.CityId is not null)
            .ThenByDescending(r => r.ShippingMethodId is not null)
            .FirstOrDefault();

        if (rate is { IsBlocked: true })
            return new ResolvedShipping(false, rate.Note ?? "ارسال به این منطقه امکان‌پذیر نیست", 0, 0, 0);

        if (rate is null && method.OnlyListedLocations)
            return new ResolvedShipping(false, "برای این منطقه ارائه نمی‌شود", 0, 0, 0);

        var cost = method.PricingMode == ShippingPricingMode.ByLocation && rate?.Cost is { } locationCost ? locationCost : method.Cost;
        var threshold = rate?.FreeShippingThreshold ?? method.FreeShippingThreshold ?? storeThreshold;
        return new ResolvedShipping(true, null, cost, threshold, Math.Max(0, rate?.ExtraDeliveryDays ?? 0));
    }

    private static decimal CalculateCost(decimal cost, decimal threshold, decimal subtotal) =>
        threshold > 0 && subtotal >= threshold ? 0 : Math.Max(0, cost);

    private async Task<List<DeliveryDayOption>> BuildScheduleAsync(
        ShippingMethodDto method, int extraDays, int daysAhead, IReadOnlySet<DayOfWeek> offDays,
        int sameDayLeadMinutes, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = IranClock.Today(now);
        var first = today.AddDays(Math.Max(0, method.MinDeliveryDays + extraDays));
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

    public async Task<List<ShippingRateDto>> GetRatesAsync(Guid? shippingMethodId = null)
    {
        var query = context.ShippingRates
            .Include(r => r.ShippingMethod)
            .Include(r => r.Province)
            .Include(r => r.City)
            .AsQueryable();

        if (shippingMethodId.HasValue)
            query = query.Where(r => r.ShippingMethodId == shippingMethodId || r.ShippingMethodId == null);

        var rates = await query
            .OrderBy(r => r.Province!.Name)
            .ThenBy(r => r.City!.Name)
            .ToListAsync();

        return rates.Select(r => r.Map()).ToList();
    }

    public async Task<OperationResult<Guid>> SaveRateAsync(ShippingRateCommand command)
    {
        if (command.ProvinceId is null && command.CityId is null)
            return OperationResult<Guid>.Error("استان یا شهر را انتخاب کنید.");

        if (command.Cost is < 0 || command.FreeShippingThreshold is < 0 || command.ExtraDeliveryDays is < 0 or > 60)
            return OperationResult<Guid>.Error("مقادیر هزینه / زمان ارسال معتبر نیست.");

        if (command.ShippingMethodId is { } methodId && !await context.ShippingMethods.AnyAsync(m => m.Id == methodId))
            return OperationResult<Guid>.Error("روش ارسال معتبر نیست.");

        if (command.CityId is { } cityId)
        {
            var city = await context.Locations.Where(l => l.Id == cityId).Select(l => new { l.ParentId }).FirstOrDefaultAsync();
            if (city is null)
                return OperationResult<Guid>.Error("شهر انتخاب‌شده معتبر نیست.");

            command.ProvinceId ??= city.ParentId;
            if (city.ParentId != command.ProvinceId)
                return OperationResult<Guid>.Error("شهر انتخاب‌شده متعلق به این استان نیست.");
        }
        else if (command.ProvinceId is { } provinceId && !await context.Locations.AnyAsync(l => l.Id == provinceId))
        {
            return OperationResult<Guid>.Error("استان انتخاب‌شده معتبر نیست.");
        }

        ShippingRate? rate;
        if (command.Id == Guid.Empty)
        {
            rate = new ShippingRate();
            context.ShippingRates.Add(rate);
        }
        else
        {
            rate = await context.ShippingRates.AsTracking().FirstOrDefaultAsync(r => r.Id == command.Id);
            if (rate is null)
                return OperationResult<Guid>.NotFound();
        }

        rate.Apply(command);
        await context.SaveChangesAsync();
        cache.Remove(CacheKeys.ShippingRates);

        return OperationResult<Guid>.Success(rate.Id, "قانون ارسال منطقه ذخیره شد.");
    }

    public async Task<OperationResult> DeleteRateAsync(Guid id)
    {
        var rate = await context.ShippingRates.AsTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (rate is null)
            return OperationResult.NotFound();

        context.ShippingRates.Remove(rate);
        await context.SaveChangesAsync();
        cache.Remove(CacheKeys.ShippingRates);
        return OperationResult.Success("قانون ارسال منطقه حذف شد.");
    }
}
