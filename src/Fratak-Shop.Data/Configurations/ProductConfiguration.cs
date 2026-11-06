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

        // SetNull: deleting a brand keeps its products (without a brand).
        builder.HasOne(p => p.Brand)
            .WithMany(b => b.Products)
            .HasForeignKey(p => p.BrandId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(p => p.BrandId);

        builder.HasIndex(p => p.Slug)
            .IsUnique();

        builder.HasIndex(p => p.SKU)
            .IsUnique();

        builder.HasIndex(p => p.CategoryId);

        builder.Property(p => p.Price)
            .HasPrecision(18, 0);

        builder.Property(p => p.DiscountPrice)
            .HasPrecision(18, 0);

        builder.Property(p => p.PurchasePrice).HasPrecision(18, 0);
        builder.ToTable(t => t.HasCheckConstraint("CK_Products_PurchasePrice", "[PurchasePrice] IS NULL OR [PurchasePrice] >= 0"));

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Products_PriceAndStock",
            "[Price] >= 0 AND ([DiscountPrice] IS NULL OR [DiscountPrice] >= 0) AND [StockQuantity] >= 0 AND ([WarrantyMonths] IS NULL OR [WarrantyMonths] >= 0)"));

        builder.OwnsOne(p => p.Seo);
    }
}
