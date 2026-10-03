using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class UserRoleAssignmentConfiguration : IEntityTypeConfiguration<UserRoleAssignment>
{
    public void Configure(EntityTypeBuilder<UserRoleAssignment> builder)
    {
        // Cascade: extra roles disappear with the user.
        builder.HasOne(r => r.User)
            .WithMany(u => u.RoleAssignments)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: the admin who granted the role cannot be removed while the
        // grant is still recorded.
        builder.HasOne(r => r.GrantedByUser)
            .WithMany()
            .HasForeignKey(r => r.GrantedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // A user cannot hold the same role twice.
        builder.HasIndex(r => new { r.UserId, r.Role })
            .IsUnique();

        builder.HasIndex(r => r.Role);
    }
}
