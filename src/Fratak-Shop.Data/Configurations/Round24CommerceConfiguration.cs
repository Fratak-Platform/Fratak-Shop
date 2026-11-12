using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Searches;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

/// <summary>دور ۲۴: هر جفت (عبارت، کالا) یک ردیف دارد؛ مرتب‌سازی با همین ایندکس سریع است.</summary>
public class SearchClickStatConfiguration : IEntityTypeConfiguration<SearchClickStat>
{
    public void Configure(EntityTypeBuilder<SearchClickStat> builder)
    {
        builder.HasIndex(s => new { s.NormalizedQuery, s.ProductId }).IsUnique();
        builder.HasIndex(s => s.LastClickAt);
    }
}

/// <summary>دور ۲۴: هر کالا فقط یک امضای تصویر دارد.</summary>
public class ProductImageSignatureConfiguration : IEntityTypeConfiguration<ProductImageSignature>
{
    public void Configure(EntityTypeBuilder<ProductImageSignature> builder)
    {
        builder.HasIndex(s => s.ProductId).IsUnique();
    }
}

/// <summary>دور ۲۴: صف تضمین قیمت بر اساس وضعیت و زمان؛ محدودیت روزانهٔ هر کاربر با (UserId, CreatedAt).</summary>
public class PriceMatchRequestConfiguration : IEntityTypeConfiguration<PriceMatchRequest>
{
    public void Configure(EntityTypeBuilder<PriceMatchRequest> builder)
    {
        builder.Property(r => r.CompetitorPrice).HasPrecision(18, 0);
        builder.Property(r => r.OurPrice).HasPrecision(18, 0);
        builder.HasIndex(r => new { r.Status, r.CreatedAt });
        builder.HasIndex(r => new { r.UserId, r.CreatedAt });
    }
}
