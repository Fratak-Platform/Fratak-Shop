using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Settings;

public interface IStoreSettingService
{
    /// <summary>Current store settings (memory cached; created with defaults when missing).</summary>
    Task<StoreSettingDto> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the settings form of the admin panel and refreshes the cache.</summary>
    Task<OperationResult> UpdateAsync(StoreSettingCommand command);
}
