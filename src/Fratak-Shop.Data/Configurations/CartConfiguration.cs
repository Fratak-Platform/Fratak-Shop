using Fratak_Shop.Data.Entities.Shopping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        // SetNull: deleting the user keeps the cart (it becomes a guest cart).
        builder.HasOne(c => c.User)
            .WithMany(u => u.Carts)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(c => c.UserId);
        builder.HasIndex(c => c.SessionKey);
        builder.HasIndex(c => c.LastActivityAt);
    }
}

public class AbandonedCartConfiguration : IEntityTypeConfiguration<AbandonedCart>
{
    public void Configure(EntityTypeBuilder<AbandonedCart> builder)
    {
        builder.HasOne(a => a.Cart)
            .WithMany()
            .HasForeignKey(a => a.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Coupon)
            .WithMany()
            .HasForeignKey(a => a.CouponId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.Status, a.AbandonedAt });
        builder.HasIndex(a => new { a.CartId, a.Status });
        builder.HasIndex(a => a.UserId);

        builder.Property(a => a.CartAmount).HasPrecision(18, 0);
        builder.Property(a => a.RecoveredAmount).HasPrecision(18, 0);

        builder.ToTable(t => t.HasCheckConstraint("CK_AbandonedCarts_Values",
            "[CartAmount] >= 0 AND [RecoveredAmount] >= 0 AND [ItemCount] >= 0 AND [RemindersSent] >= 0"));
    }
}

public class AbandonedCartReminderStepConfiguration : IEntityTypeConfiguration<AbandonedCartReminderStep>
{
    public void Configure(EntityTypeBuilder<AbandonedCartReminderStep> builder)
    {
        builder.HasIndex(s => new { s.IsActive, s.StepNumber });

        builder.Property(s => s.CouponValue).HasPrecision(18, 0);
        builder.Property(s => s.CouponMaxDiscountAmount).HasPrecision(18, 0);
        builder.Property(s => s.CouponMinOrderAmount).HasPrecision(18, 0);

        builder.ToTable(t => t.HasCheckConstraint("CK_AbandonedCartReminderSteps_Values",
            "[StepNumber] > 0 AND [DelayMinutes] > 0 AND [CouponValue] >= 0 AND [CouponMinOrderAmount] >= 0 AND [CouponValidDays] > 0 " +
            "AND ([IncludeCoupon] = 0 OR [CouponType] <> 1 OR ([CouponValue] > 0 AND [CouponValue] <= 100))"));
    }
}
