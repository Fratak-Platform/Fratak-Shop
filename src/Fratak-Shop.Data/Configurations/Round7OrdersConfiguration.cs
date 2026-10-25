using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

// پیکربندی جدول‌های بخش «سفارش‌ها» در مرحلهٔ هفتم: پیام‌های سفارش و برچسب/یادداشت مشتری.
// ستون‌های جدید خود جدول Orders (زمان‌بندی و لینک پرداخت) در OrderConfiguration تنظیم شده‌اند.

/// <summary>پیام‌های هر سفارش به ترتیب زمان خوانده می‌شوند؛ ایندکس (سفارش، زمان) همین را سریع می‌کند.</summary>
public class OrderMessageConfiguration : IEntityTypeConfiguration<OrderMessage>
{
    public void Configure(EntityTypeBuilder<OrderMessage> builder)
    {
        builder.HasOne(m => m.Order).WithMany().HasForeignKey(m => m.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => new { m.OrderId, m.CreatedAt });
        // برای شمارش سریع «پیام‌های خوانده‌نشدهٔ مشتری» در نشان منوی پنل.
        builder.HasIndex(m => new { m.FromStaff, m.ReadByStaffAt });
    }
}

/// <summary>هر مشتری هر برچسب را فقط یک بار دارد؛ ایندکس روی Tag برای فیلتر فهرست مشتریان.</summary>
public class CustomerTagConfiguration : IEntityTypeConfiguration<CustomerTag>
{
    public void Configure(EntityTypeBuilder<CustomerTag> builder)
    {
        builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => new { t.UserId, t.Tag }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(t => t.Tag);
    }
}

public class CustomerNoteConfiguration : IEntityTypeConfiguration<CustomerNote>
{
    public void Configure(EntityTypeBuilder<CustomerNote> builder)
    {
        builder.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(n => new { n.UserId, n.IsPinned, n.CreatedAt });
    }
}
