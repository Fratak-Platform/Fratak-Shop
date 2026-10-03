using Fratak_Shop.Data.Entities.Seo;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class NotFoundLogConfiguration : IEntityTypeConfiguration<NotFoundLog>
{
    public void Configure(EntityTypeBuilder<NotFoundLog> builder)
    {
        builder.HasIndex(l => l.Path)
            .IsUnique();

        builder.HasIndex(l => l.IsResolved);
    }
}
