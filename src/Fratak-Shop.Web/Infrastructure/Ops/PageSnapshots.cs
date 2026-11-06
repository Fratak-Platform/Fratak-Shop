using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Fratak_Shop.Web.Infrastructure.PageCache;

namespace Fratak_Shop.Web.Infrastructure.Ops;

/// <summary>
/// دور نوزدهم (A13): «آخرین نسخهٔ سالم» صفحه‌های عمومی روی دیسک (stale-if-error).
/// <para>هر صفحهٔ عمومی که برای مهمان (بدون ورود) با موفقیت ساخته می‌شود، حداکثر هر ۱۰ دقیقه یک بار فشرده (gzip) در
/// <c>App_Data/page-snapshots</c> ذخیره می‌شود. اگر بعداً ساختن همان صفحه شکست بخورد (پایگاه داده در دسترس نیست، برنامه تازه
/// روشن شده و هنوز آماده نیست، یا سرور آن‌قدر شلوغ است که درخواست در صف جا نمی‌شود)، به‌جای صفحهٔ خطا/۵۰۳ همین نسخه با
/// کد ۲۰۰ نشان داده می‌شود. بازدیدکننده و ربات گوگل سایت را «بالا» می‌بینند.</para>
/// <para>فقط صفحه‌هایی که PageCachePolicy آن‌ها را «قابل کش برای مهمان» دانسته ذخیره می‌شوند؛ پس هیچ دادهٔ شخصی در فایل‌ها نیست
/// (همان قاعدهٔ کش صفحه). صفحهٔ کاربر واردشده هرگز ذخیره یا از اینجا نمایش داده نمی‌شود.</para>
/// <para>مثال: <c>store.TryRead(PageSnapshotStore.Key(http.Request), out var gz)</c>.</para>
/// <para>ثبت: Singleton در WebServiceCollectionExtensions؛ استفاده: PageSnapshotMiddleware، StartupGateMiddleware، LoadSheddingMiddleware.</para>
/// </summary>
public sealed class PageSnapshotStore
{
    /// <summary>سرآیند پاسخی که از نسخهٔ ذخیره‌شده آمده است (برای عیب‌یابی و آزمون‌ها).</summary>
    public const string Header = "X-Fratak-Snapshot";

    /// <summary>بیشترین حجم HTML قابل ذخیره (بزرگ‌تر = ذخیره نمی‌شود).</summary>
    public const int MaxHtmlBytes = 1024 * 1024;

    /// <summary>اندازهٔ بافر باز کردن gzip برای مرورگری که gzip نمی‌پذیرد (زیر ۸۵ کیلوبایت = بیرون از هیپ اشیای بزرگ).</summary>
    public const int DecompressBufferBytes = 16 * 1024;

    private readonly ConcurrentDictionary<string, DateTimeOffset> _captured = new(StringComparer.Ordinal);
    private readonly string _directory;
    private readonly int _maxFiles;
    private readonly TimeSpan _refresh;
    private int _saves;
    private long _served;

    /// <param name="directory">پوشهٔ فایل‌ها (معمولاً App_Data/page-snapshots).</param>
    /// <param name="maxFiles">سقف تعداد فایل؛ بیشتر شود قدیمی‌ترها پاک می‌شوند.</param>
    /// <param name="refresh">فاصلهٔ حداقل دو ذخیرهٔ یک صفحه.</param>
    /// <param name="enabled">خاموش = هیچ ذخیره/نمایشی.</param>
    public PageSnapshotStore(string directory, int maxFiles, TimeSpan refresh, bool enabled = true)
    {
        _directory = directory;
        _maxFiles = Math.Max(50, maxFiles);
        _refresh = refresh;
        Enabled = enabled;
    }

    public bool Enabled { get; }

    /// <summary>تعداد دفعاتی که نسخهٔ ذخیره‌شده به‌جای خطا نشان داده شد (صفحهٔ «منابع سرور»).</summary>
    public long Served => Interlocked.Read(ref _served);

    /// <summary>
    /// کلید یک درخواست: مسیر + رشتهٔ پرس‌وجو (حروف کوچک مسیر)، به شکل SHA-256 کوتاه.
    /// مثال: «/product/iphone-15?x=1» → «3f2a…» (۳۲ نویسه).
    /// </summary>
    public static string Key(HttpRequest request) => Key(request.Path.Value ?? "/", request.QueryString.Value);

    /// <summary>کلید از مسیر و پرس‌وجو (آزمون‌پذیر). مثال: <c>Key("/blog", "?page=2")</c>.</summary>
    public static string Key(string path, string? query)
    {
        var text = path.ToLowerInvariant() + (query ?? string.Empty);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    /// <summary>
    /// آیا این درخواست اصلاً واجد «نسخهٔ ذخیره‌شده» است؟ فقط GET/HEAD مهمان، غیر از مسیرهای فنی/پنل/API.
    /// مثال: GET /product/x بدون کوکی ورود → true؛ /admin/… → false.
    /// </summary>
    public static bool IsCandidate(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
            return false;
        var path = request.Path;
        if (path.StartsWithSegments("/admin") || path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs")
            || path.StartsWithSegments("/health") || path.StartsWithSegments("/healthz") || path.StartsWithSegments("/media")
            || path.StartsWithSegments("/feeds") || path.StartsWithSegments("/torob_api") || path.StartsWithSegments("/account")
            || path.StartsWithSegments("/profile") || path.StartsWithSegments("/checkout") || path.StartsWithSegments("/payment")
            || path.StartsWithSegments("/cart"))
            return false;
        // فایل‌ها (پسوند دار) و درخواست‌های handler فرم‌ها نه.
        if (Path.HasExtension(path.Value) || request.Query.ContainsKey("handler"))
            return false;
        // کوکی‌های شخصی‌سازی سبک (بازدیدهای اخیر و…) مانع نیستند؛ ذخیرهٔ نسخه فقط از پاسخ قابل کش (بدون هیچ کوکی) است.
        return !PageCachePolicy.HasPrivateState(request);
    }

    /// <summary>آیا وقت ذخیرهٔ دوبارهٔ این صفحه است؟ (هر صفحه حداکثر هر ۱۰ دقیقه.)</summary>
    public bool ShouldCapture(string key, DateTimeOffset now) =>
        Enabled && (!_captured.TryGetValue(key, out var last) || now - last >= _refresh);

    /// <summary>
    /// ذخیرهٔ HTML یک صفحه (فشرده، نوشتن اتمی: فایل موقت ← جابه‌جایی). خطای دیسک فقط نادیده گرفته می‌شود.
    /// مثال: <c>await store.SaveAsync(key, htmlBytes)</c>.
    /// </summary>
    public async Task SaveAsync(string key, ReadOnlyMemory<byte> html, CancellationToken cancellationToken = default)
    {
        if (!Enabled || html.Length == 0 || html.Length > MaxHtmlBytes)
            return;
        _captured[key] = DateTimeOffset.UtcNow;
        try
        {
            Directory.CreateDirectory(_directory);
            var file = FileFor(key);
            var temp = file + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, useAsync: true))
            await using (var gzip = new GZipStream(stream, CompressionLevel.Fastest))
            {
                await gzip.WriteAsync(html, cancellationToken);
            }
            File.Move(temp, file, overwrite: true);

            if (Interlocked.Increment(ref _saves) % 100 == 0)
                Prune();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // دیسک پر/بدون دسترسی: این قابلیت فقط «بیمه» است و نباید درخواست را خراب کند.
        }
    }

    /// <summary>
    /// خواندن نسخهٔ فشردهٔ یک صفحه (همان gzip روی دیسک). مثال: <c>if (store.TryRead(key, out var gz)) …</c>
    /// </summary>
    public bool TryRead(string key, out byte[] gzip)
    {
        gzip = [];
        if (!Enabled)
            return false;
        try
        {
            var file = FileFor(key);
            if (!File.Exists(file))
                return false;
            gzip = File.ReadAllBytes(file);
            return gzip.Length > 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// نوشتن نسخهٔ ذخیره‌شده در پاسخ (اگر وجود داشته باشد): کد ۲۰۰، بدون کش مرورگر/پراکسی، با سرآیند <see cref="Header"/>.
    /// اگر مرورگر gzip بپذیرد همان فایل فشرده فرستاده می‌شود؛ وگرنه باز می‌شود. خروجی: آیا پاسخ داده شد؟
    /// </summary>
    public async Task<bool> TryServeAsync(HttpContext http, string reason)
    {
        if (http.Response.HasStarted || !IsCandidate(http.Request) || !TryRead(Key(http.Request), out var gzip))
            return false;

        Interlocked.Increment(ref _served);
        var response = http.Response;
        response.Clear();
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/html; charset=utf-8";
        response.Headers.CacheControl = "no-store";
        response.Headers[Header] = reason;
        if (HttpMethods.IsHead(http.Request.Method))
            return true;

        var acceptsGzip = http.Request.Headers.AcceptEncoding.ToString().Contains("gzip", StringComparison.OrdinalIgnoreCase);
        if (acceptsGzip)
        {
            response.Headers.ContentEncoding = "gzip";
            response.Headers.Vary = "Accept-Encoding";
            response.ContentLength = gzip.Length;
            await response.Body.WriteAsync(gzip, http.RequestAborted);
            return true;
        }

        // بافر کوچک (۱۶ کیلوبایت): بافر پیش‌فرض CopyToAsync هشتاد کیلوبایتی است و در «هیپ اشیای بزرگ» گرفته می‌شود؛
        // آزمون بار نشان داد همین بافر درست وقتی حافظه پر است (و این نسخه باید نجات‌دهنده باشد) OutOfMemoryException می‌دهد.
        await using var input = new GZipStream(new MemoryStream(gzip), CompressionMode.Decompress);
        await input.CopyToAsync(response.Body, DecompressBufferBytes, http.RequestAborted);
        return true;
    }

    /// <summary>تعداد فایل‌های فعلی (صفحهٔ منابع).</summary>
    public int Count()
    {
        try
        {
            return Directory.Exists(_directory) ? Directory.EnumerateFiles(_directory, "*.html.gz").Count() : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private string FileFor(string key) => Path.Combine(_directory, key + ".html.gz");

    /// <summary>پاک کردن قدیمی‌ترین فایل‌ها وقتی از سقف گذشت (۱۰٪ زیر سقف می‌رود تا هر بار تکرار نشود).</summary>
    private void Prune()
    {
        try
        {
            var files = new DirectoryInfo(_directory).GetFiles("*.html.gz");
            if (files.Length <= _maxFiles)
                return;
            foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc).Take(files.Length - (int)(_maxFiles * 0.9)))
                file.Delete();
            foreach (var temp in new DirectoryInfo(_directory).GetFiles("*.tmp").Where(f => f.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-1)))
                temp.Delete();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// میان‌افزار «آخرین نسخهٔ سالم» (A13). درست پیش از کش صفحه (UseOutputCache) قرار می‌گیرد:
/// <list type="number">
/// <item>اگر وقت ذخیرهٔ دوبارهٔ صفحه رسیده باشد، خروجی را هم‌زمان با ارسال به مرورگر کپی و بعد ذخیره می‌کند (فقط صفحهٔ قابل کش مهمان).</item>
/// <item>اگر ساختن صفحه استثنا بدهد (مثلاً قطعی پایگاه داده) و هنوز چیزی ارسال نشده، نسخهٔ ذخیره‌شده را نشان می‌دهد؛ وگرنه
/// استثنا به صفحهٔ خطای معمول می‌رسد.</item>
/// </list>
/// </summary>
public sealed class PageSnapshotMiddleware(RequestDelegate next, PageSnapshotStore store, ILogger<PageSnapshotMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext http)
    {
        if (!store.Enabled || !PageSnapshotStore.IsCandidate(http.Request))
        {
            await next(http);
            return;
        }

        var key = PageSnapshotStore.Key(http.Request);
        var capture = HttpMethods.IsGet(http.Request.Method) && store.ShouldCapture(key, DateTimeOffset.UtcNow);
        var original = http.Response.Body;
        CaptureStream? tee = null;
        if (capture)
        {
            tee = new CaptureStream(original, PageSnapshotStore.MaxHtmlBytes);
            http.Response.Body = tee;
        }

        try
        {
            await next(http);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !http.RequestAborted.IsCancellationRequested)
        {
            if (tee is not null)
                http.Response.Body = original;
            if (await store.TryServeAsync(http, "error"))
            {
                logger.LogWarning(exception, "Page failed; served the last good snapshot of {Path}", http.Request.Path.Value);
                RequestMonitor.Record(http, RequestMonitor.Kind.Snapshot, "خطای صفحه: " + exception.GetType().Name, 200);
                return;
            }
            throw;
        }
        finally
        {
            if (tee is not null)
                http.Response.Body = original;
        }

        if (tee is { Overflowed: false } && tee.Length > 0
            && http.Response.StatusCode == StatusCodes.Status200OK
            && PageCachePolicy.IsCacheable(http)
            && !http.Response.Headers.ContainsKey("Set-Cookie")
            && (http.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) ?? false)
            && string.IsNullOrEmpty(http.Response.Headers.ContentEncoding))
        {
            var html = tee.ToArray();
            // نوشتن روی دیسک بیرون از مسیر پاسخ (کاربر منتظر نمی‌ماند).
            _ = Task.Run(() => store.SaveAsync(key, html));
        }
    }

    /// <summary>
    /// جریانی که هر چه نوشته می‌شود را به مقصد اصلی می‌فرستد و تا سقف مشخص یک کپی نگه می‌دارد.
    /// بیشتر از سقف ← Overflowed و کپی رها می‌شود (صفحهٔ خیلی بزرگ ذخیره نمی‌شود).
    /// </summary>
    public sealed class CaptureStream(Stream inner, int limit) : Stream
    {
        private MemoryStream? _copy = new();

        public bool Overflowed { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _copy?.Length ?? 0;

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public byte[] ToArray() => _copy?.ToArray() ?? [];

        private void Keep(ReadOnlySpan<byte> buffer)
        {
            if (_copy is null)
                return;
            if (_copy.Length + buffer.Length > limit)
            {
                Overflowed = true;
                _copy.Dispose();
                _copy = null;
                return;
            }
            _copy.Write(buffer);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            Keep(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            Keep(buffer);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
            Keep(buffer.AsSpan(offset, count));
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken);
            Keep(buffer.Span);
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _copy?.Dispose();
            base.Dispose(disposing);
        }
    }
}
