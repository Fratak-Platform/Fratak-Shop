using Fratak_Shop.Data.Entities.SeoEngine;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

// پیکربندی جدول‌های موتور سئوی هوشمند (مرحلهٔ هفتم – فاز ۸).

/// <summary>هر صفحه فقط یک ردیف نمره دارد؛ ایندکس نمره برای فهرست «ضعیف‌ترین صفحه‌ها».</summary>
public class SeoScoreConfiguration : IEntityTypeConfiguration<SeoScore>
{
    public void Configure(EntityTypeBuilder<SeoScore> builder)
    {
        builder.HasIndex(s => new { s.Kind, s.EntityId }).IsUnique();
        builder.HasIndex(s => new { s.Score, s.Views });
        builder.ToTable(t => t.HasCheckConstraint("CK_SeoScores_Score", "[Score] BETWEEN 0 AND 100"));
    }
}

/// <summary>ایندکس (وضعیت، تاریخ) برای صف بررسی و (نوع، شناسه) برای تاریخچهٔ هر صفحه.</summary>
public class SeoSuggestionConfiguration : IEntityTypeConfiguration<SeoSuggestion>
{
    public void Configure(EntityTypeBuilder<SeoSuggestion> builder)
    {
        builder.HasIndex(s => new { s.Status, s.CreatedAt });
        builder.HasIndex(s => new { s.Kind, s.EntityId, s.Status });
    }
}

/// <summary>هر (نوع، کلید) فقط یک ردیف کش.</summary>
public class SeoResearchCacheConfiguration : IEntityTypeConfiguration<SeoResearchCache>
{
    public void Configure(EntityTypeBuilder<SeoResearchCache> builder)
    {
        builder.HasIndex(c => new { c.Kind, c.Key }).IsUnique();
        builder.HasIndex(c => c.ExpiresAt);
    }
}

/// <summary>نوبت زمان‌بندی‌شده یکتاست (اجرای دستی SlotKey ندارد).</summary>
public class SeoEngineRunConfiguration : IEntityTypeConfiguration<SeoEngineRun>
{
    public void Configure(EntityTypeBuilder<SeoEngineRun> builder)
    {
        // فیلتر IsDeleted باید در خود فیلتر باشد، وگرنه DbContext آن را با «[IsDeleted] = 0» جایگزین می‌کند.
        builder.HasIndex(r => r.SlotKey).IsUnique().HasFilter("[SlotKey] IS NOT NULL AND [IsDeleted] = 0");
        builder.HasIndex(r => r.StartedAt);
    }
}
