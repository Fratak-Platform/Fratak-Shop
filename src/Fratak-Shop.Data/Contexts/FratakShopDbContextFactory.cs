using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fratak_Shop.Data.Contexts;

/// <summary>
/// Design-time factory used by the <c>dotnet ef</c> tooling so migrations can be
/// created from the data project alone:
/// <code>dotnet ef migrations add Name --project src/Fratak-Shop.Data</code>
/// The connection string can be overridden with the
/// <c>FRATAK_SHOP_CONNECTION</c> environment variable.
/// </summary>
public class FratakShopDbContextFactory : IDesignTimeDbContextFactory<FratakShopDbContext>
{
    private const string DefaultConnection =
        "Server=.;Database=Fratak-Shop-DB;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    public FratakShopDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("FRATAK_SHOP_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = DefaultConnection;

        var options = new DbContextOptionsBuilder<FratakShopDbContext>()
            .UseSqlServer(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        return new FratakShopDbContext(options);
    }
}
