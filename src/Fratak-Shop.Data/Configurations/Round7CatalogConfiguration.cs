using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

// پیکربندی جدول‌های «ابزارهای کاتالوگ» مرحلهٔ هفتم (فاز ۵): دفتر موجودی و دسته‌های تغییر قیمت.

/// <summary>
/// دفتر موجودی: بیشتر به شکل «گردش یک محصول به ترتیب زمان» یا «همهٔ گردش‌ها به ترتیب زمان» خوانده می‌شود.
/// </summary>
public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.HasOne(m => m.Product).WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => new { m.ProductId, m.CreatedAt });
        builder.HasIndex(m => new { m.Reason, m.CreatedAt });
        builder.HasIndex(m => m.CreatedAt);
    }
}

public class PriceChangeBatchConfiguration : IEntityTypeConfiguration<PriceChangeBatch>
{
    public void Configure(EntityTypeBuilder<PriceChangeBatch> builder)
    {
        builder.HasIndex(b => b.CreatedAt);
        builder.HasMany(b => b.Items).WithOne(i => i.Batch).HasForeignKey(i => i.BatchId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PriceChangeItemConfiguration : IEntityTypeConfiguration<PriceChangeItem>
{
    public void Configure(EntityTypeBuilder<PriceChangeItem> builder)
    {
        // حذف محصول نباید با دو مسیر Cascade (دسته و محصول) تداخل کند؛ از سمت محصول NoAction.
        builder.HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(i => i.BatchId);
        builder.HasIndex(i => i.ProductId);
        foreach (var name in new[]
                 {
                     nameof(PriceChangeItem.OldPrice), nameof(PriceChangeItem.NewPrice),
                     nameof(PriceChangeItem.OldDiscountPrice), nameof(PriceChangeItem.NewDiscountPrice),
                     nameof(PriceChangeItem.OldPurchasePrice), nameof(PriceChangeItem.NewPurchasePrice),
                 })
            builder.Property(name).HasPrecision(18, 0);
    }
}
