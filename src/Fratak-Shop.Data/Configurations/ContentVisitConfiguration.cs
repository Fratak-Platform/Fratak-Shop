using Fratak_Shop.Data.Entities.Seo;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class ContentVisitConfiguration : IEntityTypeConfiguration<ContentVisit>
{
    public void Configure(EntityTypeBuilder<ContentVisit> builder)
    {
        // One row per entity per day.
        builder.HasIndex(v => new { v.EntityType, v.EntityId, v.VisitDate })
            .IsUnique();

        builder.HasIndex(v => v.VisitDate);
    }
}
