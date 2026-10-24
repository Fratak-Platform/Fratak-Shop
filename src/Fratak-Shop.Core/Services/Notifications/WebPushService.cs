using System.Security.Cryptography;
using System.Text;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Notifications;

/// <summary>
/// Browser push subscriptions and the shop's VAPID key pair (generated automatically on first
/// start). Push endpoints are restricted to the known browser push services: the server sends
/// requests to these URLs, so arbitrary addresses would be a server-side request forgery hole.
/// </summary>
public interface IWebPushService
{
    /// <summary>Creates the VAPID key pair when missing or invalid. Returns true when keys were generated.</summary>
    Task<bool> EnsureVapidKeysAsync();

    Task<VapidKeys?> GetVapidKeysAsync();

    Task<OperationResult> SubscribeAsync(Guid? userId, string? endpoint, string? p256dh, string? auth, string? userAgent);

    Task UnsubscribeAsync(string? endpoint);

    Task<List<PushTarget>> GetTargetsAsync(Guid userId);

    Task RecordDeliveryAsync(Guid subscriptionId, bool succeeded, bool gone);

    /// <summary>Allowed push service hosts (public for the tests).</summary>
    static bool IsAllowedEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Length > 800
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
            return false;

        var host = uri.IdnHost.ToLowerInvariant();
        return host is "fcm.googleapis.com" or "android.googleapis.com" or "updates.push.services.mozilla.com" or "web.push.apple.com"
               || host.EndsWith(".push.apple.com", StringComparison.Ordinal)
               || host.EndsWith(".notify.windows.com", StringComparison.Ordinal)
               || host.EndsWith(".push.services.mozilla.com", StringComparison.Ordinal);
    }
}

public sealed record VapidKeys(string PublicKey, string PrivateKey, string Subject);

public sealed record PushTarget(Guid Id, string Endpoint, string P256dh, string Auth);

public class WebPushService(FratakShopDbContext context, IStoreSettingService settingService, IMemoryCache cache) : IWebPushService
{
    private const string VapidCacheKey = "core:vapid";
    private const int MaxSubscriptionsPerUser = 10;

    public async Task<bool> EnsureVapidKeysAsync()
    {
        var current = await context.StoreSettings
            .OrderBy(s => s.CreatedAt)
            .Select(s => new { s.Id, s.VapidPublicKey, s.VapidPrivateKey })
            .FirstOrDefaultAsync();
        if (current is null || WebPushCrypto.IsValidVapidPair(current.VapidPublicKey, current.VapidPrivateKey))
            return false;

        var (publicKey, privateKey) = WebPushCrypto.GenerateVapidKeys();
        await context.StoreSettings
            .Where(s => s.Id == current.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.VapidPublicKey, publicKey)
                .SetProperty(x => x.VapidPrivateKey, privateKey));

        // Old subscriptions were made for the old key and can't receive anything any more.
        await context.WebPushSubscriptions.ExecuteDeleteAsync();

        cache.Remove(CacheKeys.StoreSettings);
        cache.Remove(VapidCacheKey);
        return true;
    }

    public async Task<VapidKeys?> GetVapidKeysAsync()
    {
        if (cache.TryGetValue(VapidCacheKey, out VapidKeys? cached) && cached is not null)
            return cached;

        var row = await context.StoreSettings
            .OrderBy(s => s.CreatedAt)
            .Select(s => new { s.VapidPublicKey, s.VapidPrivateKey, s.VapidSubject, s.SupportEmail })
            .FirstOrDefaultAsync();
        if (row?.VapidPublicKey is null || row.VapidPrivateKey is null)
            return null;

        var subject = !string.IsNullOrWhiteSpace(row.VapidSubject)
            ? row.VapidSubject.Trim()
            : !string.IsNullOrWhiteSpace(row.SupportEmail) ? "mailto:" + row.SupportEmail.Trim() : "mailto:support@fratak.ir";

        var keys = new VapidKeys(row.VapidPublicKey, row.VapidPrivateKey, subject);
        cache.Set(VapidCacheKey, keys, TimeSpan.FromMinutes(30));
        return keys;
    }

    public async Task<OperationResult> SubscribeAsync(Guid? userId, string? endpoint, string? p256dh, string? auth, string? userAgent)
    {
        var settings = await settingService.GetAsync();
        if (!settings.CanUseWebPush)
            return OperationResult.Error("اعلان مرورگر فعال نیست.");

        if (!IWebPushService.IsAllowedEndpoint(endpoint) || !WebPushCrypto.IsValidSubscriptionKeys(p256dh, auth))
            return OperationResult.Error("اشتراک اعلان معتبر نیست.");

        var hash = Hash(endpoint!);
        var agent = userAgent is { Length: > 300 } ? userAgent[..300] : userAgent;
        var existing = await context.WebPushSubscriptions.AsTracking().FirstOrDefaultAsync(s => s.EndpointHash == hash);

        if (existing is not null)
        {
            existing.UserId = userId ?? existing.UserId;
            existing.P256dh = p256dh!;
            existing.Auth = auth!;
            existing.UserAgent = agent;
            existing.FailureCount = 0;
        }
        else
        {
            if (userId.HasValue)
            {
                // Bounded per customer: drop the oldest devices.
                var extra = await context.WebPushSubscriptions
                    .Where(s => s.UserId == userId)
                    .OrderByDescending(s => s.UpdatedAt)
                    .Skip(MaxSubscriptionsPerUser - 1)
                    .Select(s => s.Id)
                    .ToListAsync();
                if (extra.Count > 0)
                    await context.WebPushSubscriptions.Where(s => extra.Contains(s.Id)).ExecuteDeleteAsync();
            }

            context.WebPushSubscriptions.Add(new WebPushSubscription
            {
                UserId = userId,
                Endpoint = endpoint!,
                EndpointHash = hash,
                P256dh = p256dh!,
                Auth = auth!,
                UserAgent = agent,
            });
        }

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
        }

        return OperationResult.Success("اعلان‌های مرورگر برای این دستگاه فعال شد.");
    }

    public async Task UnsubscribeAsync(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Length > 800)
            return;

        var hash = Hash(endpoint);
        await context.WebPushSubscriptions.Where(s => s.EndpointHash == hash).ExecuteDeleteAsync();
    }

    public async Task<List<PushTarget>> GetTargetsAsync(Guid userId) =>
        await context.WebPushSubscriptions
            .Where(s => s.UserId == userId && s.FailureCount < 5)
            .Select(s => new PushTarget(s.Id, s.Endpoint, s.P256dh, s.Auth))
            .ToListAsync();

    public async Task RecordDeliveryAsync(Guid subscriptionId, bool succeeded, bool gone)
    {
        if (gone)
        {
            await context.WebPushSubscriptions.Where(s => s.Id == subscriptionId).ExecuteDeleteAsync();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (succeeded)
            await context.WebPushSubscriptions.Where(s => s.Id == subscriptionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSuccessAt, now).SetProperty(x => x.FailureCount, 0));
        else
            await context.WebPushSubscriptions.Where(s => s.Id == subscriptionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.FailureCount, x => x.FailureCount + 1));
    }

    private static string Hash(string endpoint) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint.Trim())));
}
