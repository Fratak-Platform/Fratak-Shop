using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        // SetNull: deleting the user preserves the order history.
        builder.HasOne(o => o.User)
            .WithMany(u => u.Orders)
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(o => o.OrderNumber)
            .IsUnique();

        builder.HasIndex(o => o.UserId);
        builder.HasIndex(o => o.Status);

        builder.Property(o => o.SubtotalAmount).HasPrecision(18, 0);
        builder.Property(o => o.DiscountAmount).HasPrecision(18, 0);
        builder.Property(o => o.ShippingAmount).HasPrecision(18, 0);
        builder.Property(o => o.PayableAmount).HasPrecision(18, 0);
        builder.Property(o => o.RefundedAmount).HasPrecision(18, 0);
        builder.Property(o => o.CouponDiscountAmount).HasPrecision(18, 0);
        builder.Property(o => o.WalletBonusAmount).HasPrecision(18, 0);
        builder.Property(o => o.RefundedBonusAmount).HasPrecision(18, 0);
        builder.Property(o => o.ShippingLatitude).HasPrecision(9, 6);
        builder.Property(o => o.ShippingLongitude).HasPrecision(9, 6);

        builder.HasIndex(o => o.InvoiceNumber)
            .IsUnique()
            .HasFilter("[InvoiceNumber] IS NOT NULL AND [IsDeleted] = 0");

        builder.HasIndex(o => o.CouponId);

        // Used by the background job that expires unpaid online orders.
        builder.HasIndex(o => new { o.PaymentStatus, o.PaymentDeadline });
        builder.HasIndex(o => new { o.DeliveryTimeSlotId, o.RequestedDeliveryDate });

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Orders_Amounts",
            "[SubtotalAmount] >= 0 AND [DiscountAmount] >= 0 AND [ShippingAmount] >= 0 AND [PayableAmount] >= 0 AND [RefundedAmount] >= 0 AND [CouponDiscountAmount] >= 0"));

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Orders_WalletBonus",
            "[WalletBonusAmount] >= 0 AND [WalletBonusAmount] <= [PayableAmount] AND [RefundedBonusAmount] >= 0 AND [RefundedBonusAmount] <= [RefundedAmount]"));
    }
}
