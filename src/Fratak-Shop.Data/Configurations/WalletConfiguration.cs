using Fratak_Shop.Data.Entities.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class WalletTransactionConfiguration : IEntityTypeConfiguration<WalletTransaction>
{
    public void Configure(EntityTypeBuilder<WalletTransaction> builder)
    {
        builder.HasOne(t => t.User)
            .WithMany(u => u.WalletTransactions)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.UserId, t.CreatedAt });
        builder.HasIndex(t => t.OrderId);
        builder.HasIndex(t => t.PaymentTransactionId);

        builder.Property(t => t.Amount).HasPrecision(18, 0);
        builder.Property(t => t.BalanceAfter).HasPrecision(18, 0);
        builder.Property(t => t.BonusAmount).HasPrecision(18, 0);
        builder.Property(t => t.BonusBalanceAfter).HasPrecision(18, 0);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_WalletTransactions_Values", "[Amount] <> 0 AND [BalanceAfter] >= 0");
            t.HasCheckConstraint("CK_WalletTransactions_Bonus", "[BonusBalanceAfter] >= 0 AND [BonusBalanceAfter] <= [BalanceAfter] AND ABS([BonusAmount]) <= ABS([Amount])");
        });
    }
}

public class WithdrawalRequestConfiguration : IEntityTypeConfiguration<WithdrawalRequest>
{
    public void Configure(EntityTypeBuilder<WithdrawalRequest> builder)
    {
        builder.HasOne(r => r.User)
            .WithMany(u => u.WithdrawalRequests)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.UserId, r.Status });
        builder.HasIndex(r => r.Status);

        builder.Property(r => r.Amount).HasPrecision(18, 0);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_WithdrawalRequests_Amount", "[Amount] > 0");
            t.HasCheckConstraint("CK_WithdrawalRequests_Destination", "[Iban] IS NOT NULL OR [CardNumber] IS NOT NULL");
        });
    }
}
