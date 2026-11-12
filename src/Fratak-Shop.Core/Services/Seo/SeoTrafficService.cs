using System.Collections.Concurrent;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.SeoRedirects;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Seo;

/// <summary>
/// SEO traffic signals for the SEO specialist: which redirects are still used and which URLs
/// return 404 (broken links, old addresses that need a redirect). Requests only touch an
/// in-memory buffer; a background job writes aggregated counts, so a flood of random URLs
/// can neither slow down pages nor grow the table without limit.
/// </summary>
public static class SeoTrafficBuffer
{
    private const int MaxPendingPaths = 2000;

    private static readonly ConcurrentDictionary<string, NotFoundHit> NotFound = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<Guid, int> RedirectHits = new();

    public sealed class NotFoundHit
    {
        public int Count;
        public string? Referrer;
    }

    public static void RecordNotFound(string path, string? referrer)
    {
        if (path.Length is 0 or > 300)
            return;
        if (NotFound.Count >= MaxPendingPaths && !NotFound.ContainsKey(path))
            return;

        var hit = NotFound.GetOrAdd(path, _ => new NotFoundHit());
        Interlocked.Increment(ref hit.Count);
        if (!string.IsNullOrEmpty(referrer))
            hit.Referrer = referrer.Length > 500 ? referrer[..500] : referrer;
    }

    public static void RecordRedirect(Guid redirectId) => RedirectHits.AddOrUpdate(redirectId, 1, (_, count) => count + 1);

    internal static List<KeyValuePair<string, NotFoundHit>> DrainNotFound()
    {
        var items = new List<KeyValuePair<string, NotFoundHit>>();
        foreach (var key in NotFound.Keys)
        {
            if (NotFound.TryRemove(key, out var hit))
                items.Add(new(key, hit));
        }
        return items;
    }

    internal static List<KeyValuePair<Guid, int>> DrainRedirects()
    {
        var items = new List<KeyValuePair<Guid, int>>();
        foreach (var key in RedirectHits.Keys)
        {
            if (RedirectHits.TryRemove(key, out var count))
                items.Add(new(key, count));
        }
        return items;
    }
}

public interface ISeoTrafficService
{
    /// <summary>Writes the buffered counters. Returns the number of rows touched.</summary>
    Task<int> FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes resolved / rarely hit old 404 rows.</summary>
    Task<int> PruneAsync(CancellationToken cancellationToken = default);
}

public class SeoTrafficService(FratakShopDbContext context) : ISeoTrafficService
{
    private const int MaxRows = 5000;

    public async Task<int> FlushAsync(CancellationToken cancellationToken = default)
    {
        var touched = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var (id, count) in SeoTrafficBuffer.DrainRedirects())
        {
            touched += await context.SeoRedirects
                .Where(r => r.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.HitCount, r => r.HitCount + count).SetProperty(r => r.LastHitAt, now), cancellationToken);
        }

        var notFound = SeoTrafficBuffer.DrainNotFound();
        if (notFound.Count == 0)
            return touched;

        var total = await context.SeoNotFoundLogs.CountAsync(cancellationToken);
        foreach (var (path, hit) in notFound)
        {
            var updated = await context.SeoNotFoundLogs
                .IgnoreQueryFilters()
                .Where(l => l.Path == path)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(l => l.HitCount, l => l.HitCount + hit.Count)
                    .SetProperty(l => l.LastHitAt, now)
                    .SetProperty(l => l.LastReferrer, l => hit.Referrer ?? l.LastReferrer)
                    .SetProperty(l => l.IsDeleted, false)
                    .SetProperty(l => l.UpdatedAt, now), cancellationToken);

            if (updated > 0)
            {
                touched++;
                continue;
            }

            if (total >= MaxRows)
                continue; // table full: only known paths are counted until the next prune

            context.SeoNotFoundLogs.Add(new SeoNotFoundLog { Path = path, HitCount = hit.Count, LastHitAt = now, LastReferrer = hit.Referrer });
            total++;
            touched++;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear(); // another instance inserted the same path
        }

        return touched;
    }

    public async Task<int> PruneAsync(CancellationToken cancellationToken = default)
    {
        var old = DateTimeOffset.UtcNow.AddDays(-90);
        return await context.SeoNotFoundLogs
            .IgnoreQueryFilters()
            .Where(l => l.LastHitAt < old && (l.IsResolved || l.HitCount < 5))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
