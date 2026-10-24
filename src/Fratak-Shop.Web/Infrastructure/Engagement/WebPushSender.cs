using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Channels;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Web.Infrastructure.Engagement;

/// <summary>
/// In-memory queue between the notification service and <see cref="WebPushDispatcher"/>.
/// Bounded: under a flood the oldest pushes are dropped instead of using unbounded memory.
/// </summary>
public sealed class ChannelPushSender : IPushSender
{
    private readonly Channel<PushMessage> _channel = Channel.CreateBounded<PushMessage>(new BoundedChannelOptions(2000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    public ChannelReader<PushMessage> Reader => _channel.Reader;

    public void Enqueue(PushMessage message) => _channel.Writer.TryWrite(message);
}

/// <summary>
/// Delivers queued pushes (RFC 8030 / 8291 / 8292): payload encrypted for each subscribed browser,
/// signed with the shop's VAPID key. Expired subscriptions (404/410) are removed; repeated failures
/// disable a subscription. Only push-service hosts on the allow list are ever contacted.
/// </summary>
public sealed class WebPushDispatcher(
    ChannelPushSender queue,
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<WebPushDispatcher> logger) : BackgroundService
{
    public const string HttpClientName = "webpush";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await DeliverAsync(message, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception, "Web push delivery failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private async Task DeliverAsync(PushMessage message, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var pushService = scope.ServiceProvider.GetRequiredService<IWebPushService>();

        var keys = await pushService.GetVapidKeysAsync();
        if (keys is null)
            return;

        var targets = await pushService.GetTargetsAsync(message.UserId);
        if (targets.Count == 0)
            return;

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            title = Truncate(message.Title, 120),
            body = Truncate(message.Body, 300),
            url = message.Url is { } url && url.StartsWith('/') && !url.StartsWith("//") ? url : "/",
            tag = message.Tag,
        }, Json);

        var client = httpClientFactory.CreateClient(HttpClientName);
        foreach (var target in targets)
        {
            if (!IWebPushService.IsAllowedEndpoint(target.Endpoint))
            {
                await pushService.RecordDeliveryAsync(target.Id, succeeded: false, gone: true);
                continue;
            }

            var (succeeded, gone) = await SendAsync(client, target, keys, payload, message.Tag, cancellationToken);
            await pushService.RecordDeliveryAsync(target.Id, succeeded, gone);
        }
    }

    private async Task<(bool Succeeded, bool Gone)> SendAsync(HttpClient client, PushTarget target, VapidKeys keys, byte[] payload, string? tag, CancellationToken cancellationToken)
    {
        try
        {
            var body = WebPushCrypto.Encrypt(payload, target.P256dh, target.Auth);
            using var request = new HttpRequestMessage(HttpMethod.Post, target.Endpoint);
            request.Headers.TryAddWithoutValidation("Authorization",
                WebPushCrypto.CreateVapidAuthorization(target.Endpoint, keys.Subject, keys.PublicKey, keys.PrivateKey, DateTimeOffset.UtcNow.AddHours(12)));
            request.Headers.TryAddWithoutValidation("TTL", "86400");
            request.Headers.TryAddWithoutValidation("Urgency", "normal");
            if (!string.IsNullOrEmpty(tag) && tag.Length <= 32 && tag.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                request.Headers.TryAddWithoutValidation("Topic", tag);

            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            request.Content.Headers.ContentEncoding.Add("aes128gcm");

            using var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return (true, false);

            var gone = response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone;
            if (!gone)
                logger.LogInformation("Push service answered {Status} for subscription {Id}", (int)response.StatusCode, target.Id);
            return (false, gone);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or FormatException or System.Security.Cryptography.CryptographicException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            logger.LogInformation(exception, "Push to subscription {Id} failed", target.Id);
            return (false, false);
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}
