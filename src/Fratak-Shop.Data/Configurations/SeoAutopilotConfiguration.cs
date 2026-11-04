using Fratak_Shop.Data.Entities.SeoEngine;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

// ایندکس‌های جدول‌های سئوی خودکار (Entities/SeoEngine/SeoAutopilotEntities.cs).
// ایندکس یکتا خودکار فیلتر «[IsDeleted] = 0» می‌گیرد (FratakShopDbContext.ApplySoftDeleteRules).

/// <summary>هر صفحه یک ردیف وضعیت.</summary>
public class SeoAutoStateConfiguration : IEntityTypeConfiguration<SeoAutoState>
{
    public void Configure(EntityTypeBuilder<SeoAutoState> builder) =>
        builder.HasIndex(s => new { s.Kind, s.EntityId }).IsUnique();
}

/// <summary>تاریخچهٔ هر صفحه و فهرست تازه‌ترین تغییرها.</summary>
public class SeoRevisionConfiguration : IEntityTypeConfiguration<SeoRevision>
{
    public void Configure(EntityTypeBuilder<SeoRevision> builder)
    {
        builder.HasIndex(r => new { r.Kind, r.EntityId });
        builder.HasIndex(r => r.CreatedAt);
    }
}

public class SeoCrawlRunConfiguration : IEntityTypeConfiguration<SeoCrawlRun>
{
    public void Configure(EntityTypeBuilder<SeoCrawlRun> builder) =>
        builder.HasIndex(r => r.StartedAt);
}

public class SeoCrawlPageConfiguration : IEntityTypeConfiguration<SeoCrawlPage>
{
    public void Configure(EntityTypeBuilder<SeoCrawlPage> builder) =>
        builder.HasIndex(p => new { p.RunId, p.Path });
}

/// <summary>هر مشکل (اثر انگشت) فقط یک کار؛ ایندکس وضعیت برای فهرست کارهای باز.</summary>
public class SeoTaskConfiguration : IEntityTypeConfiguration<SeoTask>
{
    public void Configure(EntityTypeBuilder<SeoTask> builder)
    {
        builder.HasIndex(t => t.Fingerprint).IsUnique();
        builder.HasIndex(t => new { t.Status, t.Impact });
    }
}

public class SeoBotVisitConfiguration : IEntityTypeConfiguration<SeoBotVisit>
{
    public void Configure(EntityTypeBuilder<SeoBotVisit> builder) =>
        builder.HasIndex(v => new { v.Date, v.Bot, v.Path, v.Status }).IsUnique();
}

public class SeoVitalStatConfiguration : IEntityTypeConfiguration<SeoVitalStat>
{
    public void Configure(EntityTypeBuilder<SeoVitalStat> builder) =>
        builder.HasIndex(v => new { v.Date, v.PageType, v.Device, v.Metric }).IsUnique();
}

public class SeoTitleTestConfiguration : IEntityTypeConfiguration<SeoTitleTest>
{
    public void Configure(EntityTypeBuilder<SeoTitleTest> builder)
    {
        builder.HasIndex(t => new { t.Kind, t.EntityId });
        builder.HasIndex(t => t.Status);
    }
}
