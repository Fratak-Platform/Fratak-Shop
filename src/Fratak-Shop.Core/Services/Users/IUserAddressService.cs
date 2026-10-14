using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.DTOs.Users.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Users;

public interface IUserAddressService
{
    Task<OperationResult> CreateAddressAsync(CreateUserAddressCommand command);
    Task<OperationResult> EditAddressAsync(EditUserAddressCommand command);
    Task<OperationResult> DeleteAddressAsync(Guid id);

    /// <summary>آدرس انتخابی را پیش‌فرض کاربر می‌کند و پیش‌فرض قبلی را برمی‌دارد.</summary>
    Task<OperationResult> SetDefaultAddressAsync(Guid id);

    Task<UserAddressDto?> GetAddressByIdAsync(Guid id);
    Task<List<UserAddressDto>> GetAddressesByUserAsync(Guid userId);
}
