using Fratak_Shop.Data.Entities.Seo;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class SeoAuditRecordConfiguration : IEntityTypeConfiguration<SeoAuditRecord>
{
    public void Configure(EntityTypeBuilder<SeoAuditRecord> builder)
    {
        // SetNull: audit history survives the removal of the staff account.
        builder.HasOne(r => r.CreatedByUser)
            .WithMany()
            .HasForeignKey(r => r.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(r => r.AppliedByUser)
            .WithMany()
            .HasForeignKey(r => r.AppliedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(r => new { r.EntityType, r.EntityId });
        builder.HasIndex(r => r.Status);
    }
}
