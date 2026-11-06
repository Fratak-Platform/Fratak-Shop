using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Fratak_Shop.Web.Infrastructure.Admin;

public sealed record OnlineVisitor(string Key, Guid? UserId, string? UserName, string Path, string? Referrer, string Device, string IpMasked,
    DateTimeOffset FirstSeen, DateTimeOffset LastSeen, int PageViews, bool IsStaffArea);

public sealed record MinuteStat(DateTimeOffset Minute, int Requests, int ServerErrors, int ClientErrors, double AverageMs, double MaxMs);

public sealed record SlowRequest(DateTimeOffset At, string Method, string Path, int StatusCode, double Milliseconds);

public sealed record FailedRequest(DateTimeOffset At, string Method, string Path, int StatusCode, string? TraceId);

/// <summary>
/// In-memory live view of the site: visitors seen in the last few minutes, requests per minute
/// (count, 4xx / 5xx, timings) for the last hour, slowest and failed requests. Nothing is stored in
/// the database and IP addresses are kept masked only.
/// </summary>
public sealed class ActivityTracker
{
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(5);
    private const int MinuteSlots = 60;
    private const int MaxVisitors = 20_000;

    private readonly ConcurrentDictionary<string, OnlineVisitor> _visitors = new(StringComparer.Ordinal);
    private readonly MinuteBucket[] _minutes = Enumerable.Range(0, MinuteSlots).Select(_ => new MinuteBucket()).ToArray();
    private readonly ConcurrentQueue<SlowRequest> _slow = new();
    private readonly ConcurrentQueue<FailedRequest> _failed = new();
    private long _lastSweepTicks;
    private long _totalRequests;

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    public long TotalRequests => Interlocked.Read(ref _totalRequests);

    public void RecordVisit(HttpContext http, Guid? userId, string? userName, bool staffArea)
    {
        var now = DateTimeOffset.UtcNow;
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "-";
        var agent = http.Request.Headers.UserAgent.ToString();
        var key = userId?.ToString("N") ?? Hash(ip + "|" + agent);
        var path = http.Request.Path.Value ?? "/";
        if (path.Length > 200)
            path = path[..200];

        string? referrer = null;
        if (Uri.TryCreate(http.Request.Headers.Referer.ToString(), UriKind.Absolute, out var refUri)
            && !string.Equals(refUri.Host, http.Request.Host.Host, StringComparison.OrdinalIgnoreCase))
            referrer = refUri.Host;

        if (_visitors.Count >= MaxVisitors && !_visitors.ContainsKey(key))
            return;

        _visitors.AddOrUpdate(key,
            _ => new OnlineVisitor(key, userId, userName, path, referrer, Helpers.Display.Device(agent), MaskIp(ip), now, now, 1, staffArea),
            (_, old) => old with
            {
                UserId = userId ?? old.UserId,
                UserName = userName ?? old.UserName,
                Path = path,
                LastSeen = now,
                PageViews = old.PageViews + 1,
                IsStaffArea = staffArea,
                Referrer = old.Referrer ?? referrer,
            });

        Sweep(now);
    }

    public void RecordRequest(HttpContext http, double milliseconds)
    {
        Interlocked.Increment(ref _totalRequests);
        var now = DateTimeOffset.UtcNow;
        var minute = now.ToUnixTimeSeconds() / 60;
        var bucket = _minutes[minute % MinuteSlots];
        var status = http.Response.StatusCode;
        lock (bucket)
        {
            if (bucket.Minute != minute)
            {
                bucket.Minute = minute;
                bucket.Requests = bucket.ServerErrors = bucket.ClientErrors = 0;
                bucket.TotalMs = bucket.MaxMs = 0;
            }

            bucket.Requests++;
            bucket.TotalMs += milliseconds;
            bucket.MaxMs = Math.Max(bucket.MaxMs, milliseconds);
            if (status >= 500)
                bucket.ServerErrors++;
            else if (status >= 400 && status != 404)
                bucket.ClientErrors++;
        }

        var path = http.Request.Path.Value ?? "/";
        if (path.Length > 200)
            path = path[..200];

        if (milliseconds >= 1500)
            Enqueue(_slow, new SlowRequest(now, http.Request.Method, path, status, Math.Round(milliseconds)), 50);
        if (status >= 500)
            Enqueue(_failed, new FailedRequest(now, http.Request.Method, path, status, Activity.Current?.TraceId.ToString() ?? http.TraceIdentifier), 50);
    }

    public IReadOnlyList<OnlineVisitor> Online()
    {
        var since = DateTimeOffset.UtcNow - OnlineWindow;
        return _visitors.Values.Where(v => v.LastSeen >= since).OrderByDescending(v => v.LastSeen).ToList();
    }

    public int OnlineCount()
    {
        var since = DateTimeOffset.UtcNow - OnlineWindow;
        return _visitors.Values.Count(v => v.LastSeen >= since);
    }

    /// <summary>Last <paramref name="count"/> minutes, oldest first (missing minutes are zero).</summary>
    public IReadOnlyList<MinuteStat> Minutes(int count = 60)
    {
        var current = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var list = new List<MinuteStat>(count);
        for (var m = current - Math.Clamp(count, 1, MinuteSlots) + 1; m <= current; m++)
        {
            var bucket = _minutes[m % MinuteSlots];
            lock (bucket)
            {
                var at = DateTimeOffset.FromUnixTimeSeconds(m * 60);
                list.Add(bucket.Minute == m
                    ? new MinuteStat(at, bucket.Requests, bucket.ServerErrors, bucket.ClientErrors,
                        bucket.Requests == 0 ? 0 : Math.Round(bucket.TotalMs / bucket.Requests, 1), Math.Round(bucket.MaxMs))
                    : new MinuteStat(at, 0, 0, 0, 0, 0));
            }
        }
        return list;
    }

    public IReadOnlyList<SlowRequest> SlowRequests() => _slow.Reverse().ToList();

    public IReadOnlyList<FailedRequest> FailedRequests() => _failed.Reverse().ToList();

    private static void Enqueue<T>(ConcurrentQueue<T> queue, T item, int max)
    {
        queue.Enqueue(item);
        while (queue.Count > max && queue.TryDequeue(out _))
        {
        }
    }

    private void Sweep(DateTimeOffset now)
    {
        var last = Interlocked.Read(ref _lastSweepTicks);
        if (now.UtcTicks - last < TimeSpan.TicksPerMinute || Interlocked.CompareExchange(ref _lastSweepTicks, now.UtcTicks, last) != last)
            return;

        var expired = now - TimeSpan.FromMinutes(30);
        foreach (var (key, visitor) in _visitors)
        {
            if (visitor.LastSeen < expired)
                _visitors.TryRemove(key, out _);
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0, 12);

    public static string MaskIp(string ip)
    {
        if (System.Net.IPAddress.TryParse(ip, out var address))
        {
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();
            var text = address.ToString();
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var parts = text.Split('.');
                return $"{parts[0]}.{parts[1]}.{parts[2]}.x";
            }

            var groups = text.Split(':');
            return string.Join(':', groups.Take(3)) + ":…";
        }

        return "-";
    }

    private sealed class MinuteBucket
    {
        public long Minute = -1;
        public int Requests;
        public int ServerErrors;
        public int ClientErrors;
        public double TotalMs;
        public double MaxMs;
    }
}

/// <summary>Feeds <see cref="ActivityTracker"/>: request timings for everything, visits for page GETs.</summary>
public sealed class ActivityTrackingMiddleware(RequestDelegate next, ActivityTracker tracker)
{
    public async Task InvokeAsync(HttpContext http)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(http);
        }
        finally
        {
            var path = http.Request.Path;
            var isAsset = path.StartsWithSegments("/assets") || path.StartsWithSegments("/media") || path.StartsWithSegments("/uploads")
                          || path.StartsWithSegments("/lib") || path.StartsWithSegments("/favicon.ico") || path.StartsWithSegments("/health")
                          // اتصال زندهٔ SignalR ساعت‌ها باز می‌ماند؛ نباید «درخواست کند» شمرده شود.
                          || path.StartsWithSegments("/hubs")
                          // دستیار محلی (دور ۱۹): long-poll عمداً تا ۲۵ ثانیه طول می‌کشد.
                          || path.StartsWithSegments("/api/helper");
            if (!isAsset)
            {
                tracker.RecordRequest(http, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

                if (HttpMethods.IsGet(http.Request.Method) && http.Response.StatusCode < 400 && !path.StartsWithSegments("/api")
                    && http.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true
                    && !IsBot(http.Request.Headers.UserAgent.ToString()))
                {
                    var userId = http.User.GetUserId();
                    tracker.RecordVisit(http, userId, userId is null ? null : http.User.GetDisplayName(), path.StartsWithSegments("/admin"));
                }
            }
        }
    }

    private static bool IsBot(string agent) =>
        string.IsNullOrEmpty(agent)
        || agent.Contains("bot", StringComparison.OrdinalIgnoreCase)
        || agent.Contains("crawl", StringComparison.OrdinalIgnoreCase)
        || agent.Contains("spider", StringComparison.OrdinalIgnoreCase)
        || agent.Contains("Lighthouse", StringComparison.OrdinalIgnoreCase)
        || agent.Contains("curl", StringComparison.OrdinalIgnoreCase);
}
