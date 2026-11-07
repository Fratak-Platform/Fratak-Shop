using Fratak_Shop.Data.Entities.Design;
using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

/// <summary>دور ۲۱: فهرست طرح‌ها همیشه با وضعیت/نوع خوانده می‌شود؛ ایده‌ها با کلیدشان (جلوگیری از تکرار) و کاور با شناسهٔ مقاله.</summary>
public class DesignDocConfiguration : IEntityTypeConfiguration<DesignDoc>
{
    public void Configure(EntityTypeBuilder<DesignDoc> builder)
    {
        builder.HasIndex(d => new { d.Status, d.Kind });
        builder.HasIndex(d => d.IdeaKey);
        builder.HasIndex(d => d.BlogPostId);
    }
}

/// <summary>دور ۲۱: نامک صفحهٔ کمپین یکتاست (فیلتر حذف نرم را DbContext خودش اضافه می‌کند).</summary>
public class LandingPageConfiguration : IEntityTypeConfiguration<LandingPage>
{
    public void Configure(EntityTypeBuilder<LandingPage> builder) =>
        builder.HasIndex(p => p.Slug).IsUnique();
}

/// <summary>دور ۲۱: گزارش رشد خودکار با روز و نوع خوانده می‌شود («امروز اجرا شده؟»).</summary>
public class GrowthActionConfiguration : IEntityTypeConfiguration<GrowthAction>
{
    public void Configure(EntityTypeBuilder<GrowthAction> builder) =>
        builder.HasIndex(a => new { a.Day, a.Kind });
}
