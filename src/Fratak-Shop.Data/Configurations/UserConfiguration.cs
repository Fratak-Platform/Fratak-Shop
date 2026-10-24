using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasIndex(u => u.PhoneNumber)
            .IsUnique();

        builder.Property(u => u.WalletBalance).HasPrecision(18, 0);

        // The balance can never go negative – the database enforces it even if two
        // debits race each other.
        builder.ToTable(t => t.HasCheckConstraint("CK_Users_WalletBalance", "[WalletBalance] >= 0"));
    }
}
