using System.Diagnostics;
using System.Threading.Channels;
using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;

namespace Fratak_Shop.Web.Infrastructure.Admin;

/// <summary>Bounded in-memory queue between the logger (any thread, must never block) and the database writer.</summary>
public sealed class ErrorLogQueue
{
    private readonly Channel<ErrorLogEntry> _channel = Channel.CreateBounded<ErrorLogEntry>(new BoundedChannelOptions(2000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    private long _dropped;

    public long Captured { get; private set; }

    public long Dropped => Interlocked.Read(ref _dropped);

    public ChannelReader<ErrorLogEntry> Reader => _channel.Reader;

    public void Add(ErrorLogEntry entry)
    {
        Captured++;
        if (!_channel.Writer.TryWrite(entry))
            Interlocked.Increment(ref _dropped);
    }
}

/// <summary>
/// Logger provider that sends every Error / Critical log (with request, user and exception details)
/// to the database, so the technical page of the admin panel shows what failed, where and why.
/// The writer marks its own work so a failing database can never cause a logging loop.
/// </summary>
public sealed class DbErrorLoggerProvider(ErrorLogQueue queue, IHttpContextAccessor accessor) : ILoggerProvider
{
    internal static readonly AsyncLocal<bool> Suppressed = new();

    public ILogger CreateLogger(string categoryName) => new DbErrorLogger(categoryName, queue, accessor);

    public void Dispose()
    {
    }

    private sealed class DbErrorLogger(string category, ErrorLogQueue queue, IHttpContextAccessor accessor) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error && !Suppressed.Value;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            try
            {
                string? template = null;
                if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
                    template = values.FirstOrDefault(v => v.Key == "{OriginalFormat}").Value as string;

                var http = accessor.HttpContext;
                var message = formatter(state, exception);
                var root = exception;
                while (root is AggregateException { InnerException: not null } aggregate)
                    root = aggregate.InnerException;

                queue.Add(new ErrorLogEntry
                {
                    Level = logLevel.ToString(),
                    Category = category,
                    Message = string.IsNullOrWhiteSpace(message) ? root?.Message ?? "-" : message,
                    MessageTemplate = template,
                    ExceptionType = root?.GetType().FullName,
                    StackTrace = exception?.ToString(),
                    Path = http?.Request.Path.Value,
                    Method = http?.Request.Method,
                    QueryString = http?.Request.QueryString.Value is { Length: > 0 } query ? MaskQuery(query) : null,
                    UserId = http?.User.GetUserId(),
                    IpAddress = http?.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = http?.Request.Headers.UserAgent.ToString(),
                    TraceId = Activity.Current?.TraceId.ToString() ?? http?.TraceIdentifier,
                    MachineName = Environment.MachineName,
                });
            }
            catch
            {
                // Logging must never throw.
            }
        }

        /// <summary>Hides values of sensitive query parameters (tokens, codes, passwords).</summary>
        private static string MaskQuery(string query)
        {
            var parts = query.TrimStart('?').Split('&');
            for (var i = 0; i < parts.Length; i++)
            {
                var index = parts[i].IndexOf('=');
                if (index <= 0)
                    continue;
                var key = parts[i][..index].ToLowerInvariant();
                if (key.Contains("token") || key.Contains("code") || key.Contains("password") || key.Contains("authority") || key.Contains("secret"))
                    parts[i] = parts[i][..index] + "=***";
            }
            var result = "?" + string.Join('&', parts);
            return result.Length > 1000 ? result[..1000] : result;
        }
    }
}

/// <summary>Writes queued errors to the database in small batches.</summary>
public sealed class ErrorLogWriter(ErrorLogQueue queue, IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DbErrorLoggerProvider.Suppressed.Value = true;
        var batch = new List<ErrorLogEntry>(100);
        var failures = 0;

        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                // Collect what arrives within a short window (bursts become one insert).
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                while (batch.Count < 100 && queue.Reader.TryRead(out var entry))
                    batch.Add(entry);

                try
                {
                    using var scope = scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IMonitoringService>().SaveErrorsAsync(batch, stoppingToken);
                    failures = 0;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Database unavailable: keep the newest entries in memory only, back off.
                    failures++;
                    Console.Error.WriteLine($"[ErrorLogWriter] storing {batch.Count} error(s) failed: {exception.GetType().Name}: {exception.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 5 * failures)), stoppingToken);
                }

                batch.Clear();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
