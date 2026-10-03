using Fratak_Shop.Data.Entities.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class BankTransferPaymentConfiguration : IEntityTypeConfiguration<BankTransferPayment>
{
    public void Configure(EntityTypeBuilder<BankTransferPayment> builder)
    {
        builder.HasOne(p => p.Order)
            .WithOne(o => o.BankTransferPayment)
            .HasForeignKey<BankTransferPayment>(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // SetNull: removing a published card keeps the payment history.
        builder.HasOne(p => p.PaymentCard)
            .WithMany()
            .HasForeignKey(p => p.PaymentCardId)
            .OnDelete(DeleteBehavior.SetNull);

        // SetNull: the reviewer account can be removed without losing the record.
        builder.HasOne(p => p.ReviewedByUser)
            .WithMany()
            .HasForeignKey(p => p.ReviewedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(p => p.OrderId)
            .IsUnique();

        builder.HasIndex(p => p.Status);

        builder.Property(p => p.Amount).HasPrecision(18, 0);
    }
}
