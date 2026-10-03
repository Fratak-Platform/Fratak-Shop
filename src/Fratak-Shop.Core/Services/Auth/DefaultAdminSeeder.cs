using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Auth;

/// <summary>
/// Creates the single development/initial SuperAdmin account without ever
/// overwriting an existing account or its credentials.
/// Authentication is intentionally passwordless; the owner signs in through
/// the existing one-time-code flow.
/// </summary>
public static class DefaultAdminSeeder
{
    private const string PhoneNumber = "09913181090";

    public static async Task EnsureAsync(
        FratakShopDbContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var user = await context.Users
            .IgnoreQueryFilters()
            .Include(item => item.RoleAssignments)
            .SingleOrDefaultAsync(item => item.PhoneNumber == PhoneNumber, cancellationToken);

        if (user is null)
        {
            user = new User
            {
                PhoneNumber = PhoneNumber,
                FirstName = "محمد امین",
                LastName = "ناظری",
                Role = UserRole.SuperAdmin,
                IsActive = true,
                IsPhoneVerified = true,
                IsEmailVerified = false,
                PasswordHash = null,
            };

            context.Users.Add(user);
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        // Idempotent and non-destructive: never reset a password, name, status,
        // or roles if the owner has already customized this account.
        var changed = false;
        if (user.Role == UserRole.None)
        {
            user.Role = UserRole.SuperAdmin;
            changed = true;
        }

        if (!user.IsPhoneVerified)
        {
            user.IsPhoneVerified = true;
            changed = true;
        }

        if (changed)
            await context.SaveChangesAsync(cancellationToken);
    }
}
