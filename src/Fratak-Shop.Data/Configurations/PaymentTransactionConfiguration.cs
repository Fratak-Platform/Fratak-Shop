using Fratak_Shop.Data.Entities.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.Order)
            .WithMany()
            .HasForeignKey(p => p.OrderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(p => p.Authority)
            .IsUnique()
            .HasFilter("[Authority] IS NOT NULL AND [IsDeleted] = 0");

        builder.HasIndex(p => new { p.UserId, p.CreatedAt });
        builder.HasIndex(p => p.OrderId);
        builder.HasIndex(p => p.Status);

        builder.Property(p => p.Amount).HasPrecision(18, 0);

        builder.ToTable(t => t.HasCheckConstraint("CK_PaymentTransactions_Amount", "[Amount] > 0"));
    }
}
