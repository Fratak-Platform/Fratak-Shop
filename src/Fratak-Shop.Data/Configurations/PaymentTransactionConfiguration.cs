using Fratak_Shop.Data.Entities.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.HasOne(t => t.Order)
            .WithMany(o => o.PaymentTransactions)
            .HasForeignKey(t => t.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.OrderId);
        builder.HasIndex(t => t.Status);
        builder.HasIndex(t => t.Authority);

        builder.Property(t => t.Amount).HasPrecision(18, 0);
    }
}
