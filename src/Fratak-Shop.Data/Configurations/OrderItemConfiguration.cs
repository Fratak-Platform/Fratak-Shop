using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.HasOne(i => i.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // SetNull: the order keeps its snapshot even when the product is removed.
        builder.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(i => i.OrderId);

        builder.Property(i => i.UnitPrice).HasPrecision(18, 0);
        builder.Property(i => i.DiscountAmount).HasPrecision(18, 0);
        builder.Property(i => i.TotalAmount).HasPrecision(18, 0);

        builder.HasCheckConstraint(
            "CK_OrderItems_Amounts",
            "[Quantity] > 0 AND [UnitPrice] >= 0 AND [DiscountAmount] >= 0 AND [TotalAmount] >= 0");
    }
}
