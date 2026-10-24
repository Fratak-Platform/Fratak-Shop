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

        services.AddDbContext<FratakShopDbContext>(options => options.UseSqlServer(connectionString));

        return services;
    }

    /// <summary>
    /// Applies pending migrations and seeds the reference data
    /// (location dictionary, store settings, shipping methods, content pages, ...). Call once at application startup.
    /// </summary>
    public static async Task InitializeFratakShopDataAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();

        await context.Database.MigrateAsync(cancellationToken);
        await LocationSeeder.SeedAsync(context, cancellationToken);
        await StoreDefaultsSeeder.SeedAsync(context, cancellationToken);
    }
}
