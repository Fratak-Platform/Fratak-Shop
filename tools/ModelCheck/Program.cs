using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Seeders;
using Microsoft.EntityFrameworkCore;

// Temporary build-time validation: forces EF model building so fluent
// configuration errors surface here instead of at first request.

var options = new DbContextOptionsBuilder<FratakShopDbContext>()
    .UseSqlServer("Server=localhost;Database=FratakModelCheck;TrustServerCertificate=True")
    .Options;

using var context = new FratakShopDbContext(options);

var entityCount = context.Model.GetEntityTypes().Count();
Console.WriteLine($"EF model built successfully. Entity types: {entityCount}");

if (entityCount < 21)
{
    throw new InvalidOperationException($"Expected at least 21 entity types, found {entityCount}.");
}

var assembly = typeof(LocationSeeder).Assembly;
var resourceName = "Fratak_Shop.Data.Seeders.Data.locations.json";
if (assembly.GetManifestResourceStream(resourceName) is null)
{
    var found = string.Join(", ", assembly.GetManifestResourceNames());
    throw new InvalidOperationException($"Embedded resource '{resourceName}' missing. Found: {found}");
}

Console.WriteLine("Location seed resource embedded correctly.");

// Exercises the seeder's deterministic GUID generation.
var id1 = Fratak_Shop.Data.Utilities.DeterministicGuid.Create("location:IR");
var id2 = Fratak_Shop.Data.Utilities.DeterministicGuid.Create("location:IR");
if (id1 != id2)
{
    throw new InvalidOperationException("Deterministic GUID generation is not stable.");
}

Console.WriteLine("Deterministic GUID generation is stable.");
Console.WriteLine("MODEL CHECK PASSED");
