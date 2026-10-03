using Fratak_Shop.Data.Entities.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class BankTransferReceiptConfiguration : IEntityTypeConfiguration<BankTransferReceipt>
{
    public void Configure(EntityTypeBuilder<BankTransferReceipt> builder)
    {
        builder.HasOne(r => r.BankTransferPayment)
            .WithMany(p => p.Receipts)
            .HasForeignKey(r => r.BankTransferPaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.BankTransferPaymentId);
    }
}
