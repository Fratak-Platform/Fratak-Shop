using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        // Restrict: a category that still has products cannot be deleted.
        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.Slug)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasIndex(p => p.SKU)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasIndex(p => p.CategoryId);

        builder.Property(p => p.Price)
            .HasPrecision(18, 0);

        builder.Property(p => p.DiscountPrice)
            .HasPrecision(18, 0);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Products_PriceAndStock",
            "[Price] >= 0 AND ([DiscountPrice] IS NULL OR [DiscountPrice] >= 0) AND [StockQuantity] >= 0 AND ([WarrantyMonths] IS NULL OR [WarrantyMonths] >= 0)"));

        builder.OwnsOne(p => p.Seo);
    }
}
