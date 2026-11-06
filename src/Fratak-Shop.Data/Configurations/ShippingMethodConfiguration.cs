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

public class ShippingRateConfiguration : IEntityTypeConfiguration<ShippingRate>
{
    public void Configure(EntityTypeBuilder<ShippingRate> builder)
    {
        builder.HasOne(r => r.ShippingMethod)
            .WithMany(m => m.Rates)
            .HasForeignKey(r => r.ShippingMethodId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Province)
            .WithMany()
            .HasForeignKey(r => r.ProvinceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.City)
            .WithMany()
            .HasForeignKey(r => r.CityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.ShippingMethodId, r.ProvinceId, r.CityId });
        builder.HasIndex(r => r.CityId);

        builder.Property(r => r.Cost).HasPrecision(18, 0);
        builder.Property(r => r.FreeShippingThreshold).HasPrecision(18, 0);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_ShippingRates_Values",
            "([ProvinceId] IS NOT NULL OR [CityId] IS NOT NULL) AND ([Cost] IS NULL OR [Cost] >= 0) " +
            "AND ([FreeShippingThreshold] IS NULL OR [FreeShippingThreshold] >= 0) AND [ExtraDeliveryDays] >= 0"));
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
