using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Fratak_Shop.Core.Services.Suppliers;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Web.Infrastructure.Admin;

namespace Fratak_Shop.Web.Infrastructure.Suppliers;

// ═══════════════════════════════════════════════════════════════════════════
//  زیرساخت وب بررسی تأمین‌کننده (مرحلهٔ هفتم – فاز ۵)
//   • SupplierHttp       : پیاده‌سازی ISupplierHttp با HttpClient نام‌دار «supplier» و محافظ SSRF.
//   • SupplierSyncQueue  : صف «اجرای فوری» از صفحهٔ پنل.
//   • SupplierSyncWorker : سرویس پس‌زمینه که هر دقیقه نوبت زمان‌بندی و صف را بررسی می‌کند.
//  ثبت در Extensions/WebServiceCollectionExtensions.cs.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// درخواست GET به سایت تأمین‌کننده با رعایت نکات امنیتی:
/// <list type="bullet">
/// <item>فقط http/https؛ اتصال به آدرس‌های داخلی (localhost، شبکهٔ خصوصی، link-local) در لحظهٔ اتصال مسدود است
/// (حتی اگر دامنه به IP داخلی اشاره کند یا ریدایرکت شود) تا لینک تأمین‌کننده راهی برای دسترسی به سرور نباشد.</item>
/// <item>سقف حجم پاسخ ۳ مگابایت و مهلت ۲۵ ثانیه.</item>
/// </list>
/// </summary>
public sealed class SupplierHttp(IHttpClientFactory factory) : ISupplierHttp
{
    public const string ClientName = "supplier";

    /// <summary>سقف حجم پاسخ (بایت).</summary>
    private const int MaxBodyBytes = 3 * 1024 * 1024;

    public Task<SupplierHttpResponse> GetAsync(string url, bool expectJson, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, url, null, expectJson, cancellationToken);

    /// <summary>دور ۲۴: POST فرم با همان محافظ SSRF، سقف حجم و مهلت (برای جست‌وجوی ایمالز در پنل).</summary>
    public Task<SupplierHttpResponse> PostFormAsync(string url, IReadOnlyDictionary<string, string> form, bool expectJson, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, url, form, expectJson, cancellationToken);

    private async Task<SupplierHttpResponse> SendAsync(HttpMethod method, string url, IReadOnlyDictionary<string, string>? form, bool expectJson, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return new SupplierHttpResponse(0, null, "آدرس باید با http یا https شروع شود.");

        try
        {
            using var request = new HttpRequestMessage(method, uri);
            if (form is not null)
                request.Content = new FormUrlEncodedContent(form);
            request.Headers.Accept.Clear();
            request.Headers.Accept.ParseAdd(expectJson ? "application/json" : "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");
            using var response = await factory.CreateClient(ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var status = (int)response.StatusCode;
            if (response.Content.Headers.ContentLength > MaxBodyBytes)
                return new SupplierHttpResponse(status, null, "حجم پاسخ بیش از حد مجاز است.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxBodyBytes)
                    return new SupplierHttpResponse(status, null, "حجم پاسخ بیش از حد مجاز است.");
                buffer.Write(chunk, 0, read);
            }

            var charset = response.Content.Headers.ContentType?.CharSet;
            Encoding encoding;
            try
            {
                encoding = string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset.Trim('"'));
            }
            catch (ArgumentException)
            {
                encoding = Encoding.UTF8;
            }
            return new SupplierHttpResponse(status, encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SupplierHttpResponse(0, null, "مهلت پاسخ سایت منبع تمام شد.");
        }
        catch (HttpRequestException ex)
        {
            return new SupplierHttpResponse(0, null, ex.InnerException is BlockedAddressException ? ex.InnerException.Message : ex.Message);
        }
        catch (BlockedAddressException ex)
        {
            return new SupplierHttpResponse(0, null, ex.Message);
        }
    }

    /// <summary>هندلر HTTP با محافظ SSRF (در ثبت HttpClient استفاده می‌شود).</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        ConnectTimeout = TimeSpan.FromSeconds(10),
        ConnectCallback = ConnectSafelyAsync,
    };

    /// <summary>
    /// اتصال فقط به IP عمومی. نام دامنه همین‌جا ترجمه و هر IP بررسی می‌شود تا ترفند «DNS rebinding» هم کار نکند.
    /// </summary>
    private static async ValueTask<Stream> ConnectSafelyAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal)
            ? new[] { literal }
            : await Dns.GetHostAddressesAsync(host, cancellationToken);
        var allowed = addresses.Where(IsPublic).ToList();
        if (allowed.Count == 0)
            throw new BlockedAddressException($"اتصال به «{host}» مجاز نیست (آدرس داخلی یا نامعتبر).");

        Exception? last = null;
        foreach (var address in allowed)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                socket.Dispose();
                last = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        throw last ?? new SocketException((int)SocketError.HostUnreachable);
    }

    /// <summary>آیا این IP عمومی است؟ (نه loopback، نه شبکهٔ خصوصی، نه link-local، نه CGNAT، نه multicast).</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None))
            return false;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0 || b[0] == 10 || b[0] == 127
                     || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)   // CGNAT
                     || (b[0] == 169 && b[1] == 254)                 // link-local / متادیتای ابری
                     || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                     || (b[0] == 192 && b[1] == 168)
                     || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                     || (b[0] == 198 && (b[1] == 18 || b[1] == 19))
                     || b[0] >= 224);                                 // multicast و رزرو
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6UniqueLocal)
                return false;
            return true;
        }
        return false;
    }

    /// <summary>خطای «آدرس مسدود» برای نمایش پیام روشن در گزارش.</summary>
    public sealed class BlockedAddressException(string message) : Exception(message);

    /// <summary>User-Agent مرورگر معمولی (بعضی سایت‌ها درخواست بدون آن را رد می‌کنند).</summary>
    public static void Configure(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(25);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("fa-IR"));
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("fa", 0.9));
    }
}

/// <summary>
/// صف «اجرای فوری» (دکمهٔ «همین الان بررسی کن» در پنل). حداکثر یک درخواست در صف می‌ماند.
/// </summary>
public sealed class SupplierSyncQueue
{
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    /// <summary>درخواست اجرا به نام کارمند. اگر درخواستی در صف باشد false.</summary>
    public bool Request(string staffName) => _channel.Writer.TryWrite(staffName);

    /// <summary>آیا درخواستی منتظر شروع است.</summary>
    public bool HasPending => _channel.Reader.Count > 0;

    internal ChannelReader<string> Reader => _channel.Reader;
}

/// <summary>
/// سرویس پس‌زمینهٔ بررسی تأمین‌کننده: ۲ دقیقه بعد از روشن شدن سرور شروع می‌کند، سپس هر دقیقه
/// اگر نوبت زمان‌بندی رسیده باشد یا کارمندی «اجرای فوری» زده باشد، یک بررسی کامل اجرا می‌کند.
/// وضعیت آن در «فنی ← کارهای پس‌زمینه» دیده می‌شود.
/// </summary>
public sealed class SupplierSyncWorker(IServiceScopeFactory scopeFactory, SupplierSyncQueue queue, ILogger<SupplierSyncWorker> logger) : BackgroundService
{
    public const string JobName = "بررسی تأمین‌کننده";

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // بازه‌ی مورد انتظار برای هشدار «کار گیرکرده»: حداکثر یک روز (حتی با ۱ اجرا در روز).
        JobMonitor.Register(JobName, TimeSpan.FromHours(26));
        JobMonitor.SetRunNow(JobName, staff => queue.Request(staff));
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            string? manualBy = null;
            try
            {
                // تا یک دقیقه منتظر درخواست فوری؛ بعد نوبت زمان‌بندی بررسی می‌شود.
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                wait.CancelAfter(CheckInterval);
                if (await queue.Reader.WaitToReadAsync(wait.Token) && queue.Reader.TryRead(out var name))
                    manualBy = name;
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RunOnceAsync(manualBy, stoppingToken);
        }
    }

    private async Task RunOnceAsync(string? manualBy, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sync = scope.ServiceProvider.GetRequiredService<ISupplierSyncService>();
            var trigger = manualBy is null ? SupplierSyncTrigger.Scheduled : SupplierSyncTrigger.Manual;
            if (manualBy is null && !await sync.IsDueAsync(DateTimeOffset.UtcNow, stoppingToken))
                return;

            var result = await JobMonitor.TrackAsync(JobName, async () =>
            {
                var r = await sync.RunAsync(trigger, manualBy, stoppingToken);
                return r?.Checked ?? 0;
            });
            if (result > 0)
                logger.LogInformation("Supplier sync checked {Count} products", result);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Supplier sync failed");
        }
    }
}
