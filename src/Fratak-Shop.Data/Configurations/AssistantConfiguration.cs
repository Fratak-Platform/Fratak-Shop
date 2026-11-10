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
