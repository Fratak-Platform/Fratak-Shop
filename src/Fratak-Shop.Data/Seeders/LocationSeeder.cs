using System.Text.Json;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Locations;
using Fratak_Shop.Data.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Data.Seeders;

/// <summary>
/// Seeds the location dictionary: Iran (country) → provinces → cities.
/// IDs are deterministic, so re-running the seeder never duplicates rows and
/// references created in one environment stay valid in another.
/// </summary>
public static class LocationSeeder
{
    private const string CountryCode = "IR";
    private const string SeedResourceName = "Fratak_Shop.Data.Seeders.Data.locations.json";

    public static async Task SeedAsync(FratakShopDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var seed = await ReadSeedFileAsync(cancellationToken);

        var existingCodes = await context.Locations
            .IgnoreQueryFilters()
            .Select(l => l.Code)
            .ToHashSetAsync(cancellationToken);

        var pending = new List<Location>();

        if (!existingCodes.Contains(CountryCode))
        {
            pending.Add(Create(seed.Country.Name, CountryCode, LocationType.Country, parentCode: null));
        }

        foreach (var province in seed.Provinces)
        {
            if (!existingCodes.Contains(province.Code))
            {
                pending.Add(Create(province.Name, province.Code, LocationType.Province, CountryCode));
            }

            foreach (var city in province.Cities)
            {
                if (!existingCodes.Contains(city.Code))
                {
                    pending.Add(Create(city.Name, city.Code, LocationType.City, province.Code));
                }
            }
        }

        if (pending.Count > 0)
        {
            await context.Locations.AddRangeAsync(pending, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static Location Create(string name, string code, LocationType type, string? parentCode) => new()
    {
        Id = CreateId(code),
        ParentId = parentCode is null ? null : CreateId(parentCode),
        Type = type,
        Name = name,
        Code = code,
        IsActive = true,
    };

    private static Guid CreateId(string code) => DeterministicGuid.Create($"location:{code}");

    private static async Task<LocationSeedModel> ReadSeedFileAsync(CancellationToken cancellationToken)
    {
        var assembly = typeof(LocationSeeder).Assembly;

        await using var stream = assembly.GetManifestResourceStream(SeedResourceName)
            ?? throw new InvalidOperationException($"Embedded seed resource '{SeedResourceName}' was not found.");

        var model = await JsonSerializer
            .DeserializeAsync<LocationSeedModel>(stream, JsonSerializerOptions.Web, cancellationToken)
            ?? throw new InvalidOperationException("The location seed file is empty.");

        return model;
    }

    private sealed class LocationSeedModel
    {
        public CountrySeedModel Country { get; set; } = new();

        public List<ProvinceSeedModel> Provinces { get; set; } = [];
    }

    private sealed class CountrySeedModel
    {
        public string Name { get; set; } = null!;

        public string Code { get; set; } = null!;
    }

    private sealed class ProvinceSeedModel
    {
        public string Name { get; set; } = null!;

        public string Code { get; set; } = null!;

        public List<CitySeedModel> Cities { get; set; } = [];
    }

    private sealed class CitySeedModel
    {
        public string Name { get; set; } = null!;

        public string Code { get; set; } = null!;
    }
}
