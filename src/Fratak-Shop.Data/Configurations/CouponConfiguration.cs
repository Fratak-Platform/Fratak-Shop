using Fratak_Shop.Data.Entities.Marketing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.HasIndex(c => c.Code)
            .IsUnique();

        builder.HasIndex(c => c.IsActive);

        builder.Property(c => c.Value).HasPrecision(18, 0);
        builder.Property(c => c.MinOrderAmount).HasPrecision(18, 0);
        builder.Property(c => c.MaxDiscountAmount).HasPrecision(18, 0);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Coupons_Value",
            "[Value] >= 0 AND [UsedCount] >= 0 AND [PerUserLimit] >= 0 AND ([UsageLimit] IS NULL OR [UsageLimit] >= 0)"));
    }
}

public class CouponUsageConfiguration : IEntityTypeConfiguration<CouponUsage>
{
    public void Configure(EntityTypeBuilder<CouponUsage> builder)
    {
        builder.HasOne(u => u.Coupon)
            .WithMany(c => c.Usages)
            .HasForeignKey(u => u.CouponId)
            .OnDelete(DeleteBehavior.Cascade);

        // SetNull: deleting the customer keeps the campaign statistics.
        builder.HasOne(u => u.User)
            .WithMany()
            .HasForeignKey(u => u.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(u => u.Order)
            .WithMany()
            .HasForeignKey(u => u.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(u => u.CouponId);
        builder.HasIndex(u => u.OrderId);

        builder.Property(u => u.DiscountAmount).HasPrecision(18, 0);
    }
}
