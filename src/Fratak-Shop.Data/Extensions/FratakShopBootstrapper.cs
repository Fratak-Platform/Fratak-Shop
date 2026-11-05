using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Data.Extensions;

/// <summary>
/// Single entry point for wiring the data layer into any application.
/// </summary>
public static class FratakShopBootstrapper
{
    /// <summary>
    /// Registers the data layer (EF Core DbContext on SQL Server) in the DI container.
    /// </summary>
    public static IServiceCollection AddFratakShopData(this IServiceCollection services, string connectionString)
        => services.AddFratakShopData(connectionString, poolSize: 0);

    /// <summary>
    /// دور نوزدهم (A16): ثبت لایهٔ داده با «استخر DbContext».
    /// <para>هر DbContext این پروژه بیش از ۲۰۰ DbSet دارد؛ ساختن یک نمونهٔ تازه برای هر درخواست حافظه و زمان CPU می‌برد.
    /// با <paramref name="poolSize"/> بزرگ‌تر از صفر نمونه‌ها بازیافت می‌شوند (EF وضعیت هر نمونه را هنگام برگشت به استخر پاک می‌کند).
    /// صفر = بدون استخر (رفتار قبلی؛ آزمون‌های Integration).</para>
    /// <para>رفتار پیش‌فرض «بدون رهگیری تغییرات» (NoTracking) اینجا هم صریح تنظیم می‌شود تا بعد از بازیافت نمونه هم همین بماند.
    /// رهگیر شمارش پرس‌وجو (<see cref="Diagnostics.QueryCountingInterceptor"/>) هم اضافه می‌شود.</para>
    /// <para>مثال (Program.cs): <c>builder.Services.AddFratakShopData(connectionString, poolSize: 32)</c>.</para>
    /// </summary>
    public static IServiceCollection AddFratakShopData(this IServiceCollection services, string connectionString, int poolSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // The audit interceptor is a stateless singleton: it only records changes while an
        // AuditContext actor is set (admin panel requests).
        void Configure(DbContextOptionsBuilder options) => options
            .UseSqlServer(connectionString, sql => sql.CommandTimeout(30))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .AddInterceptors(Auditing.AuditSaveChangesInterceptor.Instance, Diagnostics.QueryCountingInterceptor.Instance);

        if (poolSize > 0)
            services.AddDbContextPool<FratakShopDbContext>(Configure, poolSize);
        else
            services.AddDbContext<FratakShopDbContext>(Configure);

        return services;
    }

    /// <summary>
    /// دور نوزدهم (A16): اگر رشتهٔ اتصال سقف استخر اتصال (Max Pool Size) نداشته باشد، سقف مناسب هاست را اضافه می‌کند تا
    /// موج بازدید هزاران اتصال روی SQL Server مشترک باز نکند. رشتهٔ دارای مقدار دست نمی‌خورد.
    /// مثال: <c>WithPoolLimit("Server=.;Database=Shop;Trusted_Connection=True", 40)</c> → «…;Max Pool Size=40».
    /// </summary>
    public static string WithPoolLimit(string connectionString, int maxPoolSize)
    {
        if (maxPoolSize <= 0)
            return connectionString;
        try
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
            if (connectionString.Contains("Max Pool Size", StringComparison.OrdinalIgnoreCase))
                return connectionString;
            builder.MaxPoolSize = maxPoolSize;
            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            // رشتهٔ ناشناخته را دست نمی‌زنیم؛ SqlClient خودش خطای واضح می‌دهد.
            return connectionString;
        }
    }

    /// <summary>
    /// Applies pending migrations, keeps the location dictionary complete and seeds the default store data
    /// on a brand-new database. Call once at application startup.
    /// <para>هم‌ارز <c>InitializeFratakShopDataAsync(seedDefaults: true)</c> (آزمون‌های Integration از همین استفاده می‌کنند).</para>
    /// </summary>
    public static Task<DefaultDataOutcome> InitializeFratakShopDataAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        => serviceProvider.InitializeFratakShopDataAsync(seedDefaults: true, cancellationToken);

    /// <summary>
    /// آماده‌سازی پایگاه داده در شروع برنامه:
    /// <list type="number">
    /// <item>اعمال مهاجرت‌ها (ساخت پایگاه داده اگر وجود نداشته باشد).</item>
    /// <item>فهرست استان‌ها و شهرهای ایران (دادهٔ مرجع؛ فقط کدهای جاافتاده اضافه می‌شوند و ردیف حذف‌شده برنمی‌گردد).</item>
    /// <item>دادهٔ پیش‌فرض سیستم — فقط روی پایگاه دادهٔ کاملاً تازه و فقط اگر <paramref name="seedDefaults"/> (Shop:SeedDefaultData) روشن باشد؛
    /// اجراهای بعدی هرگز چیزی را دوباره نمی‌سازند. جزئیات: <see cref="DefaultDataSeeder"/>.</item>
    /// </list>
    /// <para>مثال (Program.cs): <c>var outcome = await app.Services.InitializeFratakShopDataAsync(shop.SeedDefaultData);</c></para>
    /// </summary>
    public static Task<DefaultDataOutcome> InitializeFratakShopDataAsync(this IServiceProvider serviceProvider, bool seedDefaults, CancellationToken cancellationToken = default)
        => serviceProvider.InitializeFratakShopDataAsync(seedDefaults, syncMarkerFile: null, cancellationToken);

    /// <summary>
    /// دور نوزدهم (A23): همان آماده‌سازی، با «شروع سبک».
    /// <para>اگر <paramref name="syncMarkerFile"/> داده شود، فهرست شهرها و همگام‌سازی راهنمای پنل فقط وقتی اجرا می‌شوند که امضای
    /// «نسخهٔ لایهٔ داده + پایگاه داده» با آخرین اجرای موفق فرق کند (انتشار نسخهٔ جدید یا عوض شدن پایگاه داده). ری‌استارت‌های
    /// معمولی (بیکاری/بازیافت استخر IIS) فقط مهاجرت‌ها و بررسی «پایگاه دادهٔ تازه» را انجام می‌دهند.</para>
    /// <para>مثال (Program.cs): <c>InitializeFratakShopDataAsync(true, "App_Data/startup-sync.txt")</c>.</para>
    /// </summary>
    public static async Task<DefaultDataOutcome> InitializeFratakShopDataAsync(this IServiceProvider serviceProvider, bool seedDefaults, string? syncMarkerFile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();

        var signature = StartupSignature(context);
        var unchanged = syncMarkerFile is not null && ReadMarker(syncMarkerFile) == signature;

        await context.Database.MigrateAsync(cancellationToken);
        // پایگاه دادهٔ خالیِ هم‌نام (حذف و ساخت دوباره) هم با یک پرس‌وجوی سبک تشخیص داده می‌شود.
        if (!unchanged || !await context.Locations.IgnoreQueryFilters().AnyAsync(cancellationToken))
        {
            unchanged = false;
            await LocationSeeder.SeedAsync(context, cancellationToken);
        }
        var outcome = await DefaultDataSeeder.EnsureAsync(context, seedDefaults, cancellationToken);

        // راهنمای صفحه‌های پنل همراه نسخهٔ برنامه به‌روز می‌شود (مستندات نرم‌افزار است، نه دادهٔ فروشگاه؛ ردیف حذف‌شده برنمی‌گردد).
        if (outcome == DefaultDataOutcome.AlreadyInitialized && !unchanged)
            await Round7DefaultsSeeder.SyncHelpArticlesAsync(context, cancellationToken);

        if (syncMarkerFile is not null && !unchanged && outcome != DefaultDataOutcome.Disabled)
            WriteMarker(syncMarkerFile, signature);
        return outcome;
    }

    /// <summary>
    /// امضای شروع: شناسهٔ ساخت لایهٔ داده (با هر انتشار عوض می‌شود) + سرور و نام پایگاه داده.
    /// مثال: «5f1c…|.\SQLEXPRESS|FratakShop».
    /// </summary>
    public static string StartupSignature(FratakShopDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        return typeof(FratakShopDbContext).Assembly.ManifestModule.ModuleVersionId.ToString("N") + "|" + connection.DataSource + "|" + connection.Database;
    }

    private static string? ReadMarker(string file)
    {
        try
        {
            return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteMarker(string file, string signature)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, signature);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // نوشتن نشد ← دفعهٔ بعد هم کامل اجرا می‌شود (فقط کمی کندتر).
        }
    }
}
