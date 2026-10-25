using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

// پیکربندی جدول‌های «رهگیری مرسوله» مرحلهٔ هفتم (فاز ۶).

/// <summary>
/// رهگیری مرسوله: هر سفارش حداکثر یک رهگیری فعال دارد (ایندکس یکتا با شرط IsDeleted تا حذف نرم مانع ثبت دوباره نشود).
/// ایندکس (State, LastCheckedAt) برای پیدا کردن سریع «مرسوله‌هایی که نوبت بررسی‌شان رسیده» است.
/// </summary>
public class ShipmentTrackingConfiguration : IEntityTypeConfiguration<ShipmentTracking>
{
    public void Configure(EntityTypeBuilder<ShipmentTracking> builder)
    {
        builder.HasOne(t => t.Order).WithMany().HasForeignKey(t => t.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => t.OrderId).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(t => new { t.State, t.LastCheckedAt });
        builder.HasMany(t => t.Events).WithOne(e => e.Tracking).HasForeignKey(e => e.ShipmentTrackingId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ShipmentEventConfiguration : IEntityTypeConfiguration<ShipmentEvent>
{
    public void Configure(EntityTypeBuilder<ShipmentEvent> builder)
    {
        builder.HasIndex(e => new { e.ShipmentTrackingId, e.Fingerprint }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}
