using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Seeders;
using Fratak_Shop.Data.Utilities;
using Microsoft.EntityFrameworkCore;

// Temporary build-time validation: forces EF model building so configuration
// errors surface here instead of at first request.

var options = new DbContextOptionsBuilder<FratakShopDbContext>()
    .UseSqlServer("Server=localhost;Database=FratakModelCheck;TrustServerCertificate=True")
    .Options;

using var context = new FratakShopDbContext(options);

var model = context.Model;
var entityCount = model.GetEntityTypes().Count();
Console.WriteLine($"EF model built successfully. Entity types: {entityCount}");

if (entityCount < 21)
{
    throw new InvalidOperationException($"Expected at least 21 entity types, found {entityCount}.");
}

// 1) Every unique index of every BaseEntity entity must carry the central
//    "[IsDeleted] = 0" filter — applied by the DbContext, not by configurations.
// 2) Every BaseEntity entity must have the global soft-delete query filter.
var uniqueIndexes = 0;
foreach (var entityType in model.GetEntityTypes())
{
    if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
    {
        continue;
    }

    foreach (var index in entityType.GetIndexes().Where(i => i.IsUnique))
    {
        uniqueIndexes++;
        if (index.GetFilter() is not "[IsDeleted] = 0")
        {
            throw new InvalidOperationException(
                $"Unique index '{index.Name}' on '{entityType.Name}' has filter '{index.GetFilter() ?? "<null>"}' but the central soft-delete filter was expected.");
        }
    }

    if (entityType.GetQueryFilter() is null)
    {
        throw new InvalidOperationException($"Entity '{entityType.Name}' has no soft-delete query filter.");
    }
}

Console.WriteLine($"Unique indexes verified with central IsDeleted filter: {uniqueIndexes}");

if (uniqueIndexes < 10)
{
    throw new InvalidOperationException($"Expected at least 10 unique indexes, found {uniqueIndexes}.");
}

Console.WriteLine("Soft-delete query filters present on all BaseEntity types.");

var assembly = typeof(LocationSeeder).Assembly;
const string resourceName = "Fratak_Shop.Data.Seeders.Data.locations.json";
if (assembly.GetManifestResourceStream(resourceName) is null)
{
    var found = string.Join(", ", assembly.GetManifestResourceNames());
    throw new InvalidOperationException($"Embedded resource '{resourceName}' missing. Found: {found}");
}

Console.WriteLine("Location seed resource embedded correctly.");

var id1 = DeterministicGuid.Create("location:IR");
var id2 = DeterministicGuid.Create("location:IR");
if (id1 != id2)
{
    throw new InvalidOperationException("Deterministic GUID generation is not stable.");
}

Console.WriteLine("Deterministic GUID generation is stable.");
Console.WriteLine("MODEL CHECK PASSED");
