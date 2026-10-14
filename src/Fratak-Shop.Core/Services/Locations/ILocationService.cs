using Fratak_Shop.Core.DTOs.Locations.Command;
using Fratak_Shop.Core.DTOs.Locations.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Locations;

public interface ILocationService
{
    Task<OperationResult> CreateLocationAsync(CreateLocationCommand command);
    Task<OperationResult> EditLocationAsync(EditLocationCommand command);
    Task<OperationResult> DeleteLocationAsync(Guid id);
    List<LocationDto> GetAllLocationsAsync();
    Task<List<LocationDto>> GetActiveLocationsAsync();
    Task<LocationDto?> GetLocationByIdAsync(Guid id);
    Task<List<LocationDto>> GetChildrenByParentIdAsync(Guid? parentId);
}
