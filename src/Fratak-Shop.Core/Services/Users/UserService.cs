using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Users;

public class UserService(FratakShopDbContext context) : IUserService
{
    public async Task<OperationResult> CreateUserAsync(User user)
    {
        if (user is null)
            return OperationResult.Error("کاربر نامعتبر است.");

        if (string.IsNullOrWhiteSpace(user.PhoneNumber))
            return OperationResult.Error("شماره موبایل را وارد کنید.");

        if (user.PhoneNumber.Length != 11)
            return OperationResult.Error("شماره موبایل باید 11 رقم باشد.");

        var exists = await context.Users.AnyAsync(x => x.PhoneNumber == user.PhoneNumber && !x.IsDeleted);
        if (exists)
            return OperationResult.Error("این شماره موبایل قبلاً ثبت شده است.");

        context.Users.Add(user);
        await context.SaveChangesAsync();

        return OperationResult.Success("کاربر با موفقیت ایجاد شد.");
    }

    public async Task<OperationResult> UpdateUserAsync(User user)
    {
        if (user is null)
            return OperationResult.Error("کاربر نامعتبر است.");

        var entity = await context.Users.AsTracking().FirstOrDefaultAsync(x => x.Id == user.Id && !x.IsDeleted);
        if (entity is null)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        if (string.IsNullOrWhiteSpace(user.PhoneNumber))
            return OperationResult.Error("شماره موبایل را وارد کنید.");

        var phoneExists = await context.Users
            .AnyAsync(x => x.Id != user.Id && x.PhoneNumber == user.PhoneNumber && !x.IsDeleted);
        if (phoneExists)
            return OperationResult.Error("این شماره موبایل قبلاً توسط کاربر دیگری ثبت شده است.");

        entity.PhoneNumber = user.PhoneNumber;
        entity.Email = user.Email;
        entity.PasswordHash = user.PasswordHash;
        entity.FirstName = user.FirstName;
        entity.LastName = user.LastName;
        entity.Role = user.Role;
        entity.IsPhoneVerified = user.IsPhoneVerified;
        entity.IsEmailVerified = user.IsEmailVerified;
        entity.LastLoginAt = user.LastLoginAt;

        await context.SaveChangesAsync();
        return OperationResult.Success("کاربر با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteUserAsync(Guid id)
    {
        var entity = await context.Users.AsTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (entity is null)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        context.Users.Remove(entity);
        await context.SaveChangesAsync();

        return OperationResult.Success("کاربر با موفقیت حذف شد.");
    }

    public async Task<User?> GetUserByIdAsync(Guid id)
    {
        return await context.Users
            .Include(x => x.Addresses)
            .Include(x => x.Carts)
            .Include(x => x.Orders)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
    }

    public async Task<User?> GetUserByPhoneAsync(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return null;

        return await context.Users
            .FirstOrDefaultAsync(x => x.PhoneNumber == phoneNumber && !x.IsDeleted);
    }

    public async Task<List<User>> GetUsersAsync()
    {
        return await context.Users
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
    }
}
