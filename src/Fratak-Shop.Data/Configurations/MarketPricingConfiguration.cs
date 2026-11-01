using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

/// <summary>کش قیمت رقبا: هر عبارت فقط یک ردیف (ایندکس یکتا روی ردیف‌های حذف‌نشده).</summary>
public class CompetitorPriceCacheConfiguration : IEntityTypeConfiguration<CompetitorPriceCache>
{
    public void Configure(EntityTypeBuilder<CompetitorPriceCache> builder)
    {
        builder.HasIndex(c => c.QueryKey).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(c => c.FetchedAt);
    }
}

/// <summary>صف قیمت‌گذاری بازار: ایندکس (وضعیت، زمان تلاش بعدی) برای برداشتن ردیف بعدی و ایندکس محصول برای نمایش.</summary>
public class MarketPriceCheckConfiguration : IEntityTypeConfiguration<MarketPriceCheck>
{
    public void Configure(EntityTypeBuilder<MarketPriceCheck> builder)
    {
        builder.HasIndex(c => new { c.Status, c.NextAttemptAt });
        builder.HasIndex(c => c.ProductId);
        builder.Property(c => c.InitialPrice).HasPrecision(18, 0);
        builder.Property(c => c.PurchasePrice).HasPrecision(18, 0);
        builder.Property(c => c.LowestPrice).HasPrecision(18, 0);
        builder.Property(c => c.MedianPrice).HasPrecision(18, 0);
        builder.Property(c => c.SuggestedPrice).HasPrecision(18, 0);
    }
}
