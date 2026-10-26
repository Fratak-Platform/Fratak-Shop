using System.Net;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Settings;
using Fratak_Shop.Tests.Integration;
using Fratak_Shop.Web.Services.Payments;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Tests.E2E.Infrastructure;

/// <summary>
/// کل برنامهٔ وب (همان Program.cs، همان میان‌افزارها، صفحه‌ها، احراز هویت JWT، کش صفحات، محدودیت نرخ و فایروال)
/// داخل حافظه با یک پایگاه دادهٔ SQL Server تازه و دادهٔ نمونه – برای آزمون‌های سرتاسری (E2E) و شبیه‌سازی حمله.
/// <para>
/// تفاوت‌ها با اجرای واقعی فقط در «مرزهای بیرونی» است:
/// <list type="bullet">
/// <item>پیامک‌ها ارسال نمی‌شوند و در <see cref="Sms"/> جمع می‌شوند (کد OTP از همین‌جا خوانده می‌شود).</item>
/// <item>درگاه زرین‌پال با <see cref="FakeZarinPal"/> شبیه‌سازی می‌شود (همان JSON نسخهٔ ۴، بدون اینترنت).</item>
/// <item>کارهای پس‌زمینه (همگام‌سازی تأمین‌کننده، ردیابی پست، موتور سئو، ...) خاموش‌اند تا به اینترنت وصل نشوند.</item>
/// <item>هر کلاینت آزمون با سرآیند <see cref="TestClientIp.Header"/> یک IP جدا دارد تا محدودیت نرخ/کپچا بین آزمون‌ها قاطی نشود.</item>
/// </list>
/// محیط «Production» است تا رفتار واقعی (HSTS، صفحهٔ خطای عمومی، بدون جزئیات استثنا) آزموده شود.
/// </para>
/// سرور SQL از متغیر محیطی <c>FRATAK_TEST_SQL</c> می‌آید (مثل آزمون‌های Integration).
/// </summary>
public sealed class ShopApp : IAsyncLifetime
{
    public const string BaseUrl = "https://fratak.test";

    /// <summary>کلید امضای JWT آزمون (حداقل ۳۲ نویسه؛ در آزمون‌های دستکاری توکن هم استفاده می‌شود).</summary>
    public const string SigningKey = "e2e-signing-key-0123456789-abcdefghijklmnopqrstuvwxyz";

    public const string Issuer = "fratak-shop";
    public const string Audience = "fratak-shop";

    // حساب‌های دادهٔ نمونه (DemoDataSeeder / Round6DemoSeeder) – رمز همه 123456.
    public const string DemoPassword = "123456";
    public const string DemoCustomerPhone = "09120000000";
    public const string OwnerPhone = "09120000009";
    public const string SalesManagerPhone = "09120000002";
    public const string SeoPhone = "09120000003";
    public const string WriterPhone = "09120000004";
    public const string AccountantPhone = "09120000006";

    private readonly string _database = "FratakE2E_" + Guid.NewGuid().ToString("N")[..12];
    private readonly string _keysPath = Path.Combine(Path.GetTempPath(), "fratak-e2e-keys-" + Guid.NewGuid().ToString("N"));
    private Factory? _factory;
    private int _ipCounter;

    public RecordingSmsSender Sms { get; } = new();

    public FakeZarinPal ZarinPal { get; } = new();

    public CapturedLogs Logs { get; } = new();

    public IServiceProvider Services => _factory!.Services;

    public async Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable(SqlServerFixture.EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(server))
            throw new InvalidOperationException(
                $"E2E tests need a SQL Server: set {SqlServerFixture.EnvironmentVariable} (or run `dotnet test --filter Category!=E2E`).");

        var connection = new SqlConnectionStringBuilder(server) { InitialCatalog = _database, MultipleActiveResultSets = true }.ConnectionString;

        // Program.cs رشتهٔ اتصال را پیش از Build از builder.Configuration می‌خواند؛ متغیر محیطی در همان لحظه خوانده
        // می‌شود (UseSetting هم برای اطمینان پایین‌تر تنظیم شده است).
        Environment.SetEnvironmentVariable("ConnectionDefault", connection);
        Environment.SetEnvironmentVariable("Shop__SeedDemoData", "true");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", SigningKey);
        Environment.SetEnvironmentVariable("Security__DataProtectionKeysPath", _keysPath);

        _factory = new Factory(this, connection);
        // اولین دسترسی به Server برنامه را بالا می‌آورد: مهاجرت‌ها، دادهٔ پیش‌فرض، دادهٔ نمونه، نقش‌های پنل.
        _ = _factory.Server;

        Rewire();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_factory is null)
            return;

        try
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<FratakShopDbContext>().Database.EnsureDeletedAsync();
        }
        catch
        {
            // پاک کردن پایگاه داده آزمون را خراب نکند (سرور CI به هر حال دور ریخته می‌شود).
        }

        await _factory.DisposeAsync();
        try
        {
            Directory.Delete(_keysPath, recursive: true);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// بعضی زیرساخت‌ها عمداً در کل فرایند یکی‌اند (اتصال پاک‌سازی کش به داده‌ها). آزمون‌های Integration ممکن است
    /// آن را به کش خودشان وصل کرده باشند؛ پیش از هر کلاس E2E دوباره به کش همین برنامه وصل می‌شود.
    /// </summary>
    public void Rewire() => CacheInvalidation.Wire(Services.GetRequiredService<IMemoryCache>());

    /// <summary>یک کلاینت مرورگرمانند تازه (کوکی‌های خودش، IP خودش، بدون دنبال کردن خودکار ریدایرکت).</summary>
    public ShopClient CreateClient(string? ip = null, string? userAgent = null)
    {
        Rewire();
        var number = Interlocked.Increment(ref _ipCounter);
        ip ??= $"10.{80 + number / 65025 % 100}.{number / 255 % 255}.{number % 255 + 1}";

        var http = _factory!.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(BaseUrl),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        http.DefaultRequestHeaders.Add(TestClientIp.Header, ip);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent
            ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0 Safari/537.36");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("fa-IR,fa;q=0.9");
        return new ShopClient(this, http, ip);
    }

    /// <summary>یک DI scope تازه (= یک DbContext تازه)، مثل یک درخواست وب؛ برای آماده‌سازی داده و بررسی نتیجه در پایگاه داده.</summary>
    public AsyncServiceScope Scope()
    {
        Rewire();
        return Services.CreateAsyncScope();
    }

    public async Task<T> WithDbAsync<T>(Func<FratakShopDbContext, Task<T>> action)
    {
        await using var scope = Scope();
        return await action(scope.ServiceProvider.GetRequiredService<FratakShopDbContext>());
    }

    public async Task WithDbAsync(Func<FratakShopDbContext, Task> action)
    {
        await using var scope = Scope();
        await action(scope.ServiceProvider.GetRequiredService<FratakShopDbContext>());
    }

    /// <summary>
    /// تغییر تنظیمات فروشگاه با SaveChanges (نه ExecuteUpdate) تا کش تنظیمات و کش صفحات مثل پنل مدیریت پاک شوند.
    /// برمی‌گرداند: تابعی که تنظیمات را به حالت قبل برمی‌گرداند.
    /// </summary>
    public async Task<Func<Task>> ChangeSettingsAsync(Action<StoreSetting> change)
    {
        Dictionary<string, object?> before = [];
        await WithDbAsync(async db =>
        {
            var settings = await db.StoreSettings.AsTracking().FirstAsync();
            before = db.Entry(settings).Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
            change(settings);
            await db.SaveChangesAsync();
        });

        return () => WithDbAsync(async db =>
        {
            var settings = await db.StoreSettings.AsTracking().FirstAsync();
            foreach (var property in db.Entry(settings).Properties)
            {
                if (property.Metadata.IsPrimaryKey() || property.Metadata.IsConcurrencyToken)
                    continue;
                if (before.TryGetValue(property.Metadata.Name, out var value))
                    property.CurrentValue = value;
            }
            await db.SaveChangesAsync();
        });
    }

    /// <summary>آخرین کد OTP پیامک‌شده به یک شماره (کد ۴ تا ۶ رقمی داخل متن پیامک).</summary>
    public string? LastOtp(string phone)
    {
        lock (Sms.Sent)
        {
            for (var i = Sms.Sent.Count - 1; i >= 0; i--)
            {
                if (Sms.Sent[i].Phone != phone)
                    continue;
                var match = System.Text.RegularExpressions.Regex.Match(Sms.Sent[i].Message, @"\d{4,6}");
                if (match.Success)
                    return match.Value;
            }
        }
        return null;
    }

    public static string NewPhone() => "0935" + Random.Shared.Next(1_000_000, 9_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private sealed class Factory(ShopApp app, string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("ConnectionDefault", connection);
            builder.UseSetting("Shop:SeedDemoData", "true");
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting("Security:DataProtectionKeysPath", app._keysPath);
            // پورت HTTPS (مثل سرور واقعی پشت 443) تا UseHttpsRedirection درخواست http را واقعاً به https ببرد.
            builder.UseSetting("https_port", "443");
            builder.ConfigureLogging(logging => logging.AddProvider(app.Logs));

            builder.ConfigureTestServices(services =>
            {
                // کارهای پس‌زمینهٔ فروشگاه (نه میزبان وب خود ASP.NET Core) – بدون اینترنت و بدون کار هم‌زمان ناخواسته.
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService) && IsShopService(d)).ToList())
                    services.Remove(descriptor);

                services.RemoveAll<ISmsSender>();
                services.AddSingleton<ISmsSender>(app.Sms);

                services.AddHttpClient<IPaymentGateway, ZarinPalGateway>()
                    .ConfigurePrimaryHttpMessageHandler(() => new FakeZarinPalHandler(app.ZarinPal));

                services.AddTransient<IStartupFilter, TestClientIp>();
            });
        }

        private static bool IsShopService(ServiceDescriptor descriptor)
        {
            if (descriptor.IsKeyedService)
                return false;
            var type = descriptor.ImplementationType
                       ?? descriptor.ImplementationInstance?.GetType()
                       ?? descriptor.ImplementationFactory?.Method.DeclaringType;
            return type?.Namespace?.StartsWith("Fratak_Shop", StringComparison.Ordinal) == true;
        }
    }
}

[CollectionDefinition(Name)]
public sealed class E2ECollection : ICollectionFixture<ShopApp>
{
    public const string Name = "E2E";
}

/// <summary>
/// IP مشتری برای هر درخواست آزمون: TestServer خودش IP نمی‌گذارد؛ این فیلتر پیش از همهٔ میان‌افزارهای برنامه
/// مقدار سرآیند <see cref="Header"/> را به‌عنوان IP اتصال می‌نشاند (فقط در آزمون ثبت می‌شود).
/// </summary>
public sealed class TestClientIp : IStartupFilter
{
    public const string Header = "X-Test-Client-IP";

    public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress =
                IPAddress.TryParse(context.Request.Headers[Header].ToString(), out var ip) ? ip : IPAddress.Parse("10.255.255.254");
            return nextMiddleware(context);
        });
        next(app);
    };
}

/// <summary>خطاهای ثبت‌شده در لاگ برنامه (هر استثنای مدیریت‌نشده با سطح Error ثبت می‌شود).</summary>
public sealed class CapturedLogs : ILoggerProvider
{
    private readonly List<(DateTimeOffset At, string Category, LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(DateTimeOffset At, string Category, LogLevel Level, string Message)> Errors
    {
        get
        {
            lock (_entries)
                return _entries.Where(e => e.Level >= LogLevel.Error).ToList();
        }
    }

    /// <summary>خطاهای بعد از یک لحظه (برای «این درخواست هیچ استثنایی نداشت»).</summary>
    public List<string> ErrorsSince(DateTimeOffset since)
    {
        lock (_entries)
            return _entries.Where(e => e.Level >= LogLevel.Error && e.At >= since).Select(e => $"[{e.Category}] {e.Message}").ToList();
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturedLogs owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            var message = formatter(state, exception) + (exception is null ? "" : Environment.NewLine + exception);
            lock (owner._entries)
            {
                owner._entries.Add((DateTimeOffset.UtcNow, category, logLevel, message));
                if (owner._entries.Count > 5000)
                    owner._entries.RemoveRange(0, 1000);
            }
        }
    }
}
