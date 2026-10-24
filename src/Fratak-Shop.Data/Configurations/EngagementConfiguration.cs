using Fratak_Shop.Data.Entities.Contents;
using Fratak_Shop.Data.Entities.Loyalty;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

// Coupons, customer club, notifications, menus and listing filters.
// Unique indexes on nullable columns declare their own filter (including the
// soft-delete condition) because SQL Server treats NULLs as duplicates.

public class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.HasIndex(c => c.Code).IsUnique();
        builder.HasIndex(c => new { c.IsActive, c.IsPublic });
        builder.HasIndex(c => c.UserId);

        builder.Property(c => c.Value).HasPrecision(18, 0);
        builder.Property(c => c.MaxDiscountAmount).HasPrecision(18, 0);
        builder.Property(c => c.MinOrderAmount).HasPrecision(18, 0);
        builder.Property(c => c.MaxOrderAmount).HasPrecision(18, 0);

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t => t.HasCheckConstraint("CK_Coupons_Values",
            "[Value] >= 0 AND [MinOrderAmount] >= 0 AND [UsedCount] >= 0 AND [MinItemCount] >= 0 " +
            "AND ([Type] <> 1 OR ([Value] > 0 AND [Value] <= 100)) " +
            "AND ([MaxOrderAmount] IS NULL OR [MaxOrderAmount] >= [MinOrderAmount]) " +
            "AND ([UsageLimit] IS NULL OR [UsageLimit] >= 0) AND ([UsageLimitPerUser] IS NULL OR [UsageLimitPerUser] >= 0)"));
    }
}

public class CouponTargetConfiguration : IEntityTypeConfiguration<CouponTarget>
{
    public void Configure(EntityTypeBuilder<CouponTarget> builder)
    {
        builder.HasOne(t => t.Coupon)
            .WithMany(c => c.Targets)
            .HasForeignKey(t => t.CouponId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => new { t.CouponId, t.TargetType, t.TargetId });
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

        builder.HasOne(u => u.User)
            .WithMany()
            .HasForeignKey(u => u.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(u => u.Order)
            .WithMany()
            .HasForeignKey(u => u.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(u => new { u.CouponId, u.UserId, u.IsReverted });
        builder.HasIndex(u => u.OrderId);

        builder.Property(u => u.DiscountAmount).HasPrecision(18, 0);
    }
}

public class LoyaltyRuleConfiguration : IEntityTypeConfiguration<LoyaltyRule>
{
    public void Configure(EntityTypeBuilder<LoyaltyRule> builder)
    {
        builder.HasIndex(r => new { r.Action, r.IsActive });

        builder.Property(r => r.AmountStep).HasPrecision(18, 0);
        builder.Property(r => r.MinOrderAmount).HasPrecision(18, 0);

        builder.ToTable(t => t.HasCheckConstraint("CK_LoyaltyRules_Values",
            "[Points] >= 0 AND [PointsPerStep] >= 0 AND [AmountStep] >= 0 AND [MinOrderAmount] >= 0"));
    }
}

public class LoyaltyTierConfiguration : IEntityTypeConfiguration<LoyaltyTier>
{
    public void Configure(EntityTypeBuilder<LoyaltyTier> builder)
    {
        builder.HasIndex(t => new { t.IsActive, t.MinLifetimePoints });

        builder.Property(t => t.PointsMultiplier).HasPrecision(5, 2);

        builder.ToTable(t => t.HasCheckConstraint("CK_LoyaltyTiers_Values",
            "[MinLifetimePoints] >= 0 AND [PointsMultiplier] > 0 AND [PointsMultiplier] <= 10"));
    }
}

public class LoyaltyRewardConfiguration : IEntityTypeConfiguration<LoyaltyReward>
{
    public void Configure(EntityTypeBuilder<LoyaltyReward> builder)
    {
        builder.HasIndex(r => new { r.IsActive, r.DisplayOrder });

        builder.HasOne(r => r.MinTier)
            .WithMany()
            .HasForeignKey(r => r.MinTierId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(r => r.Value).HasPrecision(18, 0);
        builder.Property(r => r.MaxDiscountAmount).HasPrecision(18, 0);
        builder.Property(r => r.MinOrderAmount).HasPrecision(18, 0);

        builder.ToTable(t => t.HasCheckConstraint("CK_LoyaltyRewards_Values",
            "[PointsCost] > 0 AND [Value] >= 0 AND [CouponValidDays] > 0 AND ([Type] <> 2 OR [Value] <= 100)"));
    }
}

public class LoyaltyTransactionConfiguration : IEntityTypeConfiguration<LoyaltyTransaction>
{
    public void Configure(EntityTypeBuilder<LoyaltyTransaction> builder)
    {
        builder.HasOne(t => t.User)
            .WithMany(u => u.LoyaltyTransactions)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.UserId, t.CreatedAt });
        builder.HasIndex(t => t.OrderId);
        builder.HasIndex(t => new { t.Type, t.ExpiresAt });

        // The same event can never be rewarded twice (e.g. "order:{id}").
        builder.HasIndex(t => new { t.UserId, t.ReferenceKey })
            .IsUnique()
            .HasFilter("[ReferenceKey] IS NOT NULL AND [IsDeleted] = 0");

        builder.ToTable(t => t.HasCheckConstraint("CK_LoyaltyTransactions_Values", "[Points] <> 0 AND [BalanceAfter] >= 0"));
    }
}

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(n => n.LoyaltyTier)
            .WithMany()
            .HasForeignKey(n => n.LoyaltyTierId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(n => new { n.UserId, n.ReadAt, n.CreatedAt });
        builder.HasIndex(n => new { n.Audience, n.IsActive, n.PublishAt });

        builder.ToTable(t => t.HasCheckConstraint("CK_Notifications_Audience",
            "([Audience] = 0 AND [UserId] IS NOT NULL) OR ([Audience] <> 0 AND [UserId] IS NULL)"));
    }
}

public class NotificationReadConfiguration : IEntityTypeConfiguration<NotificationRead>
{
    public void Configure(EntityTypeBuilder<NotificationRead> builder)
    {
        builder.HasOne(r => r.Notification)
            .WithMany()
            .HasForeignKey(r => r.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.NotificationId, r.UserId }).IsUnique();
        builder.HasIndex(r => r.UserId);
    }
}

public class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.HasIndex(t => t.Event).IsUnique();
    }
}

public class MenuConfiguration : IEntityTypeConfiguration<Menu>
{
    public void Configure(EntityTypeBuilder<Menu> builder)
    {
        builder.HasIndex(m => m.Key).IsUnique();
        builder.HasIndex(m => new { m.Location, m.IsActive, m.DisplayOrder });

        builder.HasMany(m => m.Items)
            .WithOne(i => i.Menu)
            .HasForeignKey(i => i.MenuId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        builder.HasOne(i => i.Parent)
            .WithMany(i => i.Children)
            .HasForeignKey(i => i.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => new { i.MenuId, i.ParentId, i.DisplayOrder });
    }
}

public class ProductFilterAttributeConfiguration : IEntityTypeConfiguration<ProductFilterAttribute>
{
    public void Configure(EntityTypeBuilder<ProductFilterAttribute> builder)
    {
        builder.HasIndex(f => f.Slug).IsUnique();
        builder.HasIndex(f => new { f.IsActive, f.DisplayOrder });

        builder.HasOne(f => f.Category)
            .WithMany()
            .HasForeignKey(f => f.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
