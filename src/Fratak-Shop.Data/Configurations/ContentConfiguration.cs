using Fratak_Shop.Data.Entities.Contents;
using Fratak_Shop.Data.Entities.Faqs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class ContentPageConfiguration : IEntityTypeConfiguration<ContentPage>
{
    public void Configure(EntityTypeBuilder<ContentPage> builder)
    {
        builder.HasIndex(p => p.Slug).IsUnique();
        builder.HasIndex(p => p.PageKey);
        builder.HasIndex(p => new { p.IsActive, p.ShowInFooter, p.DisplayOrder });

        builder.OwnsOne(p => p.Seo);
    }
}

public class SiteFeatureConfiguration : IEntityTypeConfiguration<SiteFeature>
{
    public void Configure(EntityTypeBuilder<SiteFeature> builder)
    {
        builder.HasIndex(f => new { f.IsActive, f.DisplayOrder });
    }
}

public class HomeSectionConfiguration : IEntityTypeConfiguration<HomeSection>
{
    public void Configure(EntityTypeBuilder<HomeSection> builder)
    {
        builder.HasIndex(s => new { s.IsActive, s.DisplayOrder });

        builder.HasOne(s => s.Category)
            .WithMany()
            .HasForeignKey(s => s.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.ToTable(t => t.HasCheckConstraint("CK_HomeSections_ItemCount", "[ItemCount] >= 0 AND [ItemCount] <= 50"));
    }
}

public class TrustBadgeConfiguration : IEntityTypeConfiguration<TrustBadge>
{
    public void Configure(EntityTypeBuilder<TrustBadge> builder)
    {
        builder.HasIndex(b => new { b.IsActive, b.DisplayOrder });
    }
}

public class FaqCategoryConfiguration : IEntityTypeConfiguration<FaqCategory>
{
    public void Configure(EntityTypeBuilder<FaqCategory> builder)
    {
        builder.HasIndex(c => c.Slug).IsUnique();

        builder.HasMany(c => c.Items)
            .WithOne(i => i.Category)
            .HasForeignKey(i => i.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
