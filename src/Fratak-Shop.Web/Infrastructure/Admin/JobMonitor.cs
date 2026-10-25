using System.Collections.Concurrent;

namespace Fratak_Shop.Web.Infrastructure.Admin;

public sealed class JobState
{
    public required string Name { get; init; }
    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastSucceededAt { get; set; }
    public DateTimeOffset? LastFailedAt { get; set; }
    public string? LastError { get; set; }
    public int? LastCount { get; set; }
    public long Runs { get; set; }
    public long Failures { get; set; }
    public double? LastDurationMs { get; set; }

    /// <summary>Expected maximum time between two runs (for the "stale job" health check).</summary>
    public TimeSpan ExpectedInterval { get; set; } = TimeSpan.FromHours(1);

    public bool IsFailing => LastFailedAt is { } failed && (LastSucceededAt is null || failed > LastSucceededAt);
}

/// <summary>
/// Process-wide registry of the background jobs (last run, duration, failures, last error) shown on the
/// technical page of the admin panel and used by the health checks to spot stuck / failing jobs.
/// </summary>
public static class JobMonitor
{
    private static readonly ConcurrentDictionary<string, JobState> States = new(StringComparer.Ordinal);

    public static IReadOnlyList<JobState> All => States.Values.OrderBy(s => s.Name, StringComparer.Ordinal).ToList();

    public static void Register(string name, TimeSpan expectedInterval) =>
        States.GetOrAdd(name, n => new JobState { Name = n }).ExpectedInterval = expectedInterval;

    /// <summary>Runs one job iteration and records its outcome. Exceptions are recorded and rethrown.</summary>
    public static async Task<T> TrackAsync<T>(string name, Func<Task<T>> run)
    {
        var state = States.GetOrAdd(name, n => new JobState { Name = n });
        var started = DateTimeOffset.UtcNow;
        lock (state)
        {
            state.LastStartedAt = started;
            state.Runs++;
        }

        try
        {
            var result = await run();
            lock (state)
            {
                state.LastSucceededAt = DateTimeOffset.UtcNow;
                state.LastDurationMs = (DateTimeOffset.UtcNow - started).TotalMilliseconds;
                state.LastCount = result is int count ? count : null;
            }
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            lock (state)
            {
                state.LastFailedAt = DateTimeOffset.UtcNow;
                state.LastDurationMs = (DateTimeOffset.UtcNow - started).TotalMilliseconds;
                state.Failures++;
                var text = $"{exception.GetType().Name}: {exception.Message}";
                state.LastError = text.Length > 500 ? text[..500] : text;
            }
            throw;
        }
    }

    public static Task TrackAsync(string name, Func<Task> run) => TrackAsync(name, async () =>
    {
        await run();
        return 0;
    });
}
