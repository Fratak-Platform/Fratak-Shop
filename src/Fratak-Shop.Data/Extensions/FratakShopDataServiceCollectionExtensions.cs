using Fratak_Shop.Data.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Data.Extensions;

public static class FratakShopDataServiceCollectionExtensions
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
}
