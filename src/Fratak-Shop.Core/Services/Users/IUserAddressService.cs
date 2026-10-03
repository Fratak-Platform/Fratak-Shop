using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Users;

namespace Fratak_Shop.Core.Services.Users;

public interface IUserAddressService
{
    Task<OperationResult> CreateAddressAsync(UserAddress address);
    Task<OperationResult> UpdateAddressAsync(UserAddress address);
    Task<OperationResult> DeleteAddressAsync(Guid id);
    Task<UserAddress?> GetAddressByIdAsync(Guid id);
    Task<List<UserAddress>> GetAddressesByUserAsync(Guid userId);
}
