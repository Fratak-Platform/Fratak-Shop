using Fratak_Shop.Core;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Security;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Extensions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// A throw-away SQL Server database with the real migrations, seeders and Core services
/// (the same DI registrations the web app uses). The server comes from the
/// <c>FRATAK_TEST_SQL</c> environment variable (connection string without a database), e.g.
/// <c>Server=localhost,1433;User Id=sa;Password=...;TrustServerCertificate=True</c>.
/// Run only these tests with <c>dotnet test --filter Category=Integration</c>.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "FRATAK_TEST_SQL";

    private readonly string _database = "FratakTests_" + Guid.NewGuid().ToString("N")[..12];

    public ServiceProvider Services { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(server))
            throw new InvalidOperationException(
                $"Integration tests need a SQL Server: set {EnvironmentVariable} (or run `dotnet test --filter Category!=Integration`).");

        var connection = new SqlConnectionStringBuilder(server) { InitialCatalog = _database }.ConnectionString;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFratakShopData(connection);
        services.AddFratakShopCore();
        services.AddSingleton<ISmsSender, RecordingSmsSender>();
        services.AddSingleton<ISecretProtector, ReversibleTestProtector>();
        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await Services.InitializeFratakShopDataAsync();
        CacheInvalidation.Wire(Services.GetRequiredService<IMemoryCache>());
    }

    public async Task DisposeAsync()
    {
        if (Services is null)
            return;

        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<FratakShopDbContext>().Database.EnsureDeletedAsync();

        await Services.DisposeAsync();
    }

    /// <summary>A fresh DI scope (= fresh DbContext), like one web request.</summary>
    public AsyncServiceScope Scope()
    {
        // Unit tests may have pointed the process-wide eviction hook at their own cache in between.
        CacheInvalidation.Wire(Services.GetRequiredService<IMemoryCache>());
        return Services.CreateAsyncScope();
    }

    public async Task<Guid> CreateUserAsync()
    {
        await using var scope = Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var user = new User
        {
            PhoneNumber = "09" + Random.Shared.NextInt64(100_000_000, 999_999_999),
            FirstName = "کاربر",
            LastName = "آزمایشی",
            Role = UserRole.Customer,
            IsPhoneVerified = true,
            SecurityStamp = PasswordHasher.NewSecurityStamp(),
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    public async Task<Guid> CreateProductAsync(decimal price = 250_000, int stock = 10)
    {
        await using var scope = Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var key = Guid.NewGuid().ToString("N")[..10];

        var category = await context.Categories.FirstOrDefaultAsync(c => c.Slug == "test-category");
        if (category is null)
        {
            category = new Category { Name = "دسته آزمایشی", Slug = "test-category", IsActive = true };
            context.Categories.Add(category);
            await context.SaveChangesAsync();
        }

        var product = new Product
        {
            CategoryId = category.Id,
            Name = "محصول آزمایشی " + key,
            Slug = "test-" + key,
            SKU = "T-" + key,
            Price = price,
            StockQuantity = stock,
            IsActive = true,
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return product.Id;
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SQL Server";
}

/// <summary>Collects SMS messages instead of sending them.</summary>
public sealed class RecordingSmsSender : ISmsSender
{
    public List<(string Phone, string Message)> Sent { get; } = [];

    public Task SendAsync(string phoneNumber, string message)
    {
        lock (Sent)
            Sent.Add((phoneNumber, message));
        return Task.CompletedTask;
    }
}

/// <summary>Stand-in for the Data Protection based protector of the web app.</summary>
public sealed class ReversibleTestProtector : ISecretProtector
{
    private const string Prefix = "test-protected:";

    public string Protect(string plaintext) => Prefix + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext));

    public string? Unprotect(string protectedValue) =>
        protectedValue.StartsWith(Prefix, StringComparison.Ordinal)
            ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue[Prefix.Length..]))
            : null;
}
