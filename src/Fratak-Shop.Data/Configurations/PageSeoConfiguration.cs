using Fratak_Shop.Data.Entities.Pages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class PageSeoConfiguration : IEntityTypeConfiguration<PageSeo>
{
    public void Configure(EntityTypeBuilder<PageSeo> builder)
    {
        // Exactly one SEO record per site page.
        builder.HasIndex(s => s.Page)
            .IsUnique();

        builder.OwnsOne(s => s.Seo);
    }
}
