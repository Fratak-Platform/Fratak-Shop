using Fratak_Shop.Data.Entities.Shipping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class ShippingMethodConfiguration : IEntityTypeConfiguration<ShippingMethod>
{
    public void Configure(EntityTypeBuilder<ShippingMethod> builder)
    {
        builder.HasIndex(m => m.Code).IsUnique();
        builder.HasIndex(m => new { m.IsActive, m.DisplayOrder });

        builder.Property(m => m.Cost).HasPrecision(18, 0);
        builder.Property(m => m.FreeShippingThreshold).HasPrecision(18, 0);

        builder.HasOne(m => m.Province)
            .WithMany()
            .HasForeignKey(m => m.ProvinceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(m => m.TimeSlots)
            .WithOne(s => s.ShippingMethod)
            .HasForeignKey(s => s.ShippingMethodId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_ShippingMethods_Values",
            "[Cost] >= 0 AND [MinDeliveryDays] >= 0 AND [MaxDeliveryDays] >= [MinDeliveryDays]"));
    }
}

public class DeliveryTimeSlotConfiguration : IEntityTypeConfiguration<DeliveryTimeSlot>
{
    public void Configure(EntityTypeBuilder<DeliveryTimeSlot> builder)
    {
        builder.HasIndex(s => new { s.ShippingMethodId, s.DisplayOrder });

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_DeliveryTimeSlots_Values",
            "[EndTime] > [StartTime] AND ([Capacity] IS NULL OR [Capacity] > 0)"));
    }
}
