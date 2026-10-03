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

        // SetNull: deleting a coupon keeps the orders that used it.
        builder.HasOne(o => o.Coupon)
            .WithMany()
            .HasForeignKey(o => o.CouponId)
            .OnDelete(DeleteBehavior.SetNull);

        // SetNull: removing a shipping option keeps the order snapshot intact.
        builder.HasOne(o => o.ShippingMethod)
            .WithMany()
            .HasForeignKey(o => o.ShippingMethodId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(o => o.UserId);
        builder.HasIndex(o => o.Status);
        builder.HasIndex(o => o.CouponId);
        builder.HasIndex(o => o.ShippingMethodId);

        // Keep the legacy database column name while exposing an explicit
        // snapshot property next to the ShippingMethod navigation.
        builder.Property(o => o.ShippingMethodName)
            .HasColumnName("ShippingMethod")
            .HasMaxLength(100);

        builder.Property(o => o.SubtotalAmount).HasPrecision(18, 0);
        builder.Property(o => o.DiscountAmount).HasPrecision(18, 0);
        builder.Property(o => o.ShippingAmount).HasPrecision(18, 0);
        builder.Property(o => o.PayableAmount).HasPrecision(18, 0);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Orders_Amounts",
            "[SubtotalAmount] >= 0 AND [DiscountAmount] >= 0 AND [ShippingAmount] >= 0 AND [PayableAmount] >= 0"));
    }
}
