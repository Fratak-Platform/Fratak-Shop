using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class ProductFeatureConfiguration : IEntityTypeConfiguration<ProductFeature>
{
    public void Configure(EntityTypeBuilder<ProductFeature> builder)
    {
        builder.HasOne(f => f.Product)
            .WithMany(p => p.Features)
            .HasForeignKey(f => f.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // A product can hold each feature key only once.
        builder.HasIndex(f => new { f.ProductId, f.Key })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
    }
}
