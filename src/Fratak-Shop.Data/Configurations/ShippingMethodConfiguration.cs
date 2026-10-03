using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class ShippingMethodConfiguration : IEntityTypeConfiguration<ShippingMethod>
{
    public void Configure(EntityTypeBuilder<ShippingMethod> builder)
    {
        builder.Property(m => m.Cost).HasPrecision(18, 0);
        builder.Property(m => m.FreeShippingThreshold).HasPrecision(18, 0);

        builder.HasIndex(m => m.IsActive);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_ShippingMethods_Cost",
            "[Cost] >= 0 AND ([FreeShippingThreshold] IS NULL OR [FreeShippingThreshold] >= 0)"));
    }
}
