using Fratak_Shop.Data.Entities.Assistant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

/// <summary>دور ۲۳: گزارش‌های پنل دستیار همیشه بر اساس نوع و زمان (و کالا) خوانده می‌شوند.</summary>
public class AssistantMessageConfiguration : IEntityTypeConfiguration<AssistantMessage>
{
    public void Configure(EntityTypeBuilder<AssistantMessage> builder)
    {
        builder.HasIndex(m => new { m.Intent, m.CreatedAt });
        builder.HasIndex(m => m.CreatedAt);
        builder.HasIndex(m => m.ProductId);
    }
}

/// <summary>دور ۲۴: هر کاربر فقط یک ردیف سلیقه دارد.</summary>
public class AssistantProfileConfiguration : IEntityTypeConfiguration<AssistantProfile>
{
    public void Configure(EntityTypeBuilder<AssistantProfile> builder)
    {
        builder.HasIndex(p => p.UserId).IsUnique();
    }
}

/// <summary>دور ۲۴: گفت‌وگوی باز هر مرورگر سریع پیدا شود؛ صف پنل بر اساس وضعیت و آخرین پیام.</summary>
public class AssistantHandoffConfiguration : IEntityTypeConfiguration<AssistantHandoff>
{
    public void Configure(EntityTypeBuilder<AssistantHandoff> builder)
    {
        builder.HasIndex(h => new { h.SessionKey, h.Status });
        builder.HasIndex(h => new { h.Status, h.LastMessageAt });
        builder.HasMany(h => h.Messages).WithOne(m => m.Handoff).HasForeignKey(m => m.HandoffId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>دور ۲۴: پیام‌های یک گفت‌وگو به ترتیب زمان.</summary>
public class AssistantHandoffMessageConfiguration : IEntityTypeConfiguration<AssistantHandoffMessage>
{
    public void Configure(EntityTypeBuilder<AssistantHandoffMessage> builder)
    {
        builder.HasIndex(m => new { m.HandoffId, m.CreatedAt });
    }
}
