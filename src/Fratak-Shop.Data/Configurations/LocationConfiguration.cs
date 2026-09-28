using Fratak_Shop.Data.Entities.Locations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.HasOne(l => l.Parent)
            .WithMany(l => l.Children)
            .HasForeignKey(l => l.ParentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(l => l.Code)
            .IsUnique();

        builder.HasIndex(l => l.ParentId);
        builder.HasIndex(l => l.Type);
    }
}
