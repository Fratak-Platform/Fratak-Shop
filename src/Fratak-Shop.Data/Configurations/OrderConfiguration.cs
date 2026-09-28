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
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasIndex(o => o.UserId);
        builder.HasIndex(o => o.Status);

        builder.Property(o => o.SubtotalAmount).HasPrecision(18, 0);
        builder.Property(o => o.DiscountAmount).HasPrecision(18, 0);
        builder.Property(o => o.ShippingAmount).HasPrecision(18, 0);
        builder.Property(o => o.PayableAmount).HasPrecision(18, 0);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Orders_Amounts",
            "[SubtotalAmount] >= 0 AND [DiscountAmount] >= 0 AND [ShippingAmount] >= 0 AND [PayableAmount] >= 0"));
    }
}
