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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // The audit interceptor is a stateless singleton: it only records changes while an
        // AuditContext actor is set (admin panel requests).
        services.AddDbContext<FratakShopDbContext>(options => options
            .UseSqlServer(connectionString)
            .AddInterceptors(Auditing.AuditSaveChangesInterceptor.Instance));

        return services;
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
    public static async Task<DefaultDataOutcome> InitializeFratakShopDataAsync(this IServiceProvider serviceProvider, bool seedDefaults, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();

        await context.Database.MigrateAsync(cancellationToken);
        await LocationSeeder.SeedAsync(context, cancellationToken);
        var outcome = await DefaultDataSeeder.EnsureAsync(context, seedDefaults, cancellationToken);

        // راهنمای صفحه‌های پنل همراه نسخهٔ برنامه به‌روز می‌شود (مستندات نرم‌افزار است، نه دادهٔ فروشگاه؛ ردیف حذف‌شده برنمی‌گردد).
        if (outcome == DefaultDataOutcome.AlreadyInitialized)
            await Round7DefaultsSeeder.SyncHelpArticlesAsync(context, cancellationToken);
        return outcome;
    }
}
