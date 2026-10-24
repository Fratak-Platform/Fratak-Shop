using Fratak_Shop.Core.Services.Events;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Users;

/// <summary>
/// Remembers the browsers/devices a customer signed in from and raises a security alert on a
/// sign-in from a new one (the very first device of an account is not "new").
/// </summary>
public interface IKnownDeviceService
{
    /// <summary>Records a successful sign-in; returns true when the device was new and an alert was sent.</summary>
    Task<bool> RegisterSignInAsync(Guid userId, string? userAgent, string? ipAddress);

    Task<List<KnownDeviceDto>> GetDevicesAsync(Guid userId);

    Task ForgetAllAsync(Guid userId);
}

public sealed record KnownDeviceDto(Guid Id, string Name, string? LastIp, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt);

public class KnownDeviceService(FratakShopDbContext context, IStoreSettingService settingService, IShopEvents events) : IKnownDeviceService
{
    private const int MaxDevicesPerUser = 30;

    public async Task<bool> RegisterSignInAsync(Guid userId, string? userAgent, string? ipAddress)
    {
        var device = DeviceInfo.Parse(userAgent);
        var hash = device.Hash;
        var now = DateTimeOffset.UtcNow;
        var ip = ipAddress is { Length: > 50 } ? ipAddress[..50] : ipAddress;

        var updated = await context.UserKnownDevices
            .Where(d => d.UserId == userId && d.DeviceHash == hash)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.LastSeenAt, now)
                .SetProperty(d => d.LastIp, ip)
                .SetProperty(d => d.UpdatedAt, now));

        if (updated > 0)
            return false;

        var hadDevices = await context.UserKnownDevices.AnyAsync(d => d.UserId == userId);

        context.UserKnownDevices.Add(new UserKnownDevice
        {
            UserId = userId,
            DeviceHash = hash,
            DeviceName = device.DisplayName.Length > 150 ? device.DisplayName[..150] : device.DisplayName,
            LastIp = ip,
            LastSeenAt = now,
        });

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear(); // parallel sign-in from the same device
            return false;
        }

        // Keep the list bounded: forget the devices not seen for the longest time.
        var stale = await context.UserKnownDevices
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.LastSeenAt)
            .Skip(MaxDevicesPerUser)
            .Select(d => d.Id)
            .ToListAsync();
        if (stale.Count > 0)
            await context.UserKnownDevices.Where(d => stale.Contains(d.Id)).ExecuteDeleteAsync();

        var settings = await settingService.GetAsync();
        if (!hadDevices || !settings.IsNewDeviceAlertEnabled)
            return false;

        await events.NewDeviceSignInAsync(userId, device.DisplayName, ip);
        return true;
    }

    public async Task<List<KnownDeviceDto>> GetDevicesAsync(Guid userId) =>
        await context.UserKnownDevices
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.LastSeenAt)
            .Select(d => new KnownDeviceDto(d.Id, d.DeviceName, d.LastIp, d.CreatedAt, d.LastSeenAt))
            .ToListAsync();

    /// <summary>"It wasn't me": after securing the account every device counts as new again.</summary>
    public async Task ForgetAllAsync(Guid userId) =>
        await context.UserKnownDevices.Where(d => d.UserId == userId).ExecuteDeleteAsync();
}
