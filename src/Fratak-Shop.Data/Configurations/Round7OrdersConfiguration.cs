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

// ── بررسی خودکار تأمین‌کننده (فاز ۵) ──

/// <summary>هر محصول حداکثر یک ردیف وضعیت دارد.</summary>
public class SupplierProductStateConfiguration : IEntityTypeConfiguration<Fratak_Shop.Data.Entities.Products.SupplierProductState>
{
    public void Configure(EntityTypeBuilder<Fratak_Shop.Data.Entities.Products.SupplierProductState> builder)
    {
        builder.HasOne(s => s.Product).WithMany().HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => s.ProductId).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(s => new { s.Status, s.ConsecutiveErrors });
        builder.Property(s => s.SourcePrice).HasPrecision(18, 0);
    }
}

public class SupplierSyncRunConfiguration : IEntityTypeConfiguration<Fratak_Shop.Data.Entities.Products.SupplierSyncRun>
{
    public void Configure(EntityTypeBuilder<Fratak_Shop.Data.Entities.Products.SupplierSyncRun> builder)
    {
        builder.HasIndex(r => r.StartedAt);
    }
}

/// <summary>ایندکس (وضعیت، نوع) برای فهرست «منتظر تأیید» و (محصول، نوع، وضعیت) برای پیدا کردن ردیف باز هر محصول.</summary>
public class SupplierChangeConfiguration : IEntityTypeConfiguration<Fratak_Shop.Data.Entities.Products.SupplierChange>
{
    public void Configure(EntityTypeBuilder<Fratak_Shop.Data.Entities.Products.SupplierChange> builder)
    {
        builder.HasOne(c => c.Product).WithMany().HasForeignKey(c => c.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.Status, c.Kind, c.CreatedAt });
        builder.HasIndex(c => new { c.ProductId, c.Kind, c.Status });
        builder.Property(c => c.OldValue).HasPrecision(18, 0);
        builder.Property(c => c.NewValue).HasPrecision(18, 0);
    }
}
