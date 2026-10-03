using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Users;

namespace Fratak_Shop.Core.Services.Users;

public interface IUserService
{
    Task<OperationResult> CreateUserAsync(User user);
    Task<OperationResult> UpdateUserAsync(User user);
    Task<OperationResult> DeleteUserAsync(Guid id);
    Task<User?> GetUserByIdAsync(Guid id);
    Task<User?> GetUserByPhoneAsync(string phoneNumber);
    Task<List<User>> GetUsersAsync();
}
