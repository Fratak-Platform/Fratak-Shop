using Fratak_Shop.Data.Entities.SeoRedirects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class SeoRedirectConfiguration : IEntityTypeConfiguration<SeoRedirect>
{
    public void Configure(EntityTypeBuilder<SeoRedirect> builder)
    {
        builder.HasIndex(r => r.FromPath)
            .IsUnique();

        builder.HasIndex(r => r.IsActive);
    }
}
