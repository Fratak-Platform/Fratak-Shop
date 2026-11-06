using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

// دور نوزدهم: ایندکس‌های جدول‌های «محتوای هوشمند» (Entities/Smart/SmartEntities.cs).
// ایندکس یکتا خودکار فیلتر «[IsDeleted] = 0» می‌گیرد (FratakShopDbContext.ApplySoftDeleteRules).

/// <summary>صف: کارگر کارهای «در صف و سررسیده» را به ترتیب برمی‌دارد؛ صفحهٔ محصول کارهای همان هدف را می‌خواند.</summary>
public class SmartTaskConfiguration : IEntityTypeConfiguration<SmartTask>
{
    public void Configure(EntityTypeBuilder<SmartTask> builder)
    {
        builder.HasIndex(t => new { t.Status, t.RunAfter });
        builder.HasIndex(t => new { t.TargetKind, t.TargetId });
    }
}

/// <summary>هر کلید تحقیق فقط یک ردیف؛ پاک‌سازی شبانه با ExpiresAt.</summary>
public class SmartResearchCacheConfiguration : IEntityTypeConfiguration<SmartResearchCache>
{
    public void Configure(EntityTypeBuilder<SmartResearchCache> builder)
    {
        builder.HasIndex(c => c.Key).IsUnique();
        builder.HasIndex(c => c.ExpiresAt);
    }
}

/// <summary>هر محصول حداکثر یک جمع‌بندی.</summary>
public class ProductInsightConfiguration : IEntityTypeConfiguration<ProductInsight>
{
    public void Configure(EntityTypeBuilder<ProductInsight> builder) =>
        builder.HasIndex(i => i.ProductId).IsUnique();
}

public class BlogPlanItemConfiguration : IEntityTypeConfiguration<BlogPlanItem>
{
    public void Configure(EntityTypeBuilder<BlogPlanItem> builder) =>
        builder.HasIndex(i => new { i.Status, i.PlannedFor });
}

/// <summary>یکتا بودن (نوع، سفارش، محصول) جلوی پیام تکراری را حتی با دو اجرای هم‌زمان می‌گیرد.</summary>
public class CustomerReminderConfiguration : IEntityTypeConfiguration<CustomerReminder>
{
    public void Configure(EntityTypeBuilder<CustomerReminder> builder)
    {
        builder.HasIndex(r => new { r.Kind, r.OrderId, r.ProductId }).IsUnique();
        builder.HasIndex(r => new { r.Status, r.DueAt });
    }
}
