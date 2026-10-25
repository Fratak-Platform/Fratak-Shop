using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

// پیکربندی جدول‌های مرحلهٔ هفتم (اعلان کارکنان، تنظیمات شخصی پنل، پیام‌های آماده).

/// <summary>ایندکس روی (گیرنده، خوانده‌نشده، تاریخ) برای شمارش سریع اعلان‌های خوانده‌نشده.</summary>
public class StaffNotificationConfiguration : IEntityTypeConfiguration<StaffNotification>
{
    public void Configure(EntityTypeBuilder<StaffNotification> builder)
    {
        builder.HasOne(n => n.Recipient).WithMany().HasForeignKey(n => n.RecipientUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(n => new { n.RecipientUserId, n.ReadAt, n.CreatedAt });
    }
}

/// <summary>هر کاربر برای هر کلید فقط یک مقدار دارد.</summary>
public class AdminUserPreferenceConfiguration : IEntityTypeConfiguration<AdminUserPreference>
{
    public void Configure(EntityTypeBuilder<AdminUserPreference> builder)
    {
        builder.HasIndex(p => new { p.UserId, p.Key }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public class ReadyMessageConfiguration : IEntityTypeConfiguration<ReadyMessage>
{
    public void Configure(EntityTypeBuilder<ReadyMessage> builder)
    {
        builder.HasIndex(m => new { m.IsActive, m.SortOrder });
    }
}
