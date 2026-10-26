using Fratak_Shop.Data.Entities.Ops;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

// پیکربندی جدول‌های عملیات فنی (مرحلهٔ هفتم – فاز ۹). توضیح هر جدول: Entities/Ops/OpsEntities.cs.

/// <summary>فهرست نسخه‌ها به ترتیب تاریخ؛ کلید نوبت برای جلوگیری از اجرای دوبارهٔ یک نوبت با چند سرور یکتاست.</summary>
public class BackupRecordConfiguration : IEntityTypeConfiguration<BackupRecord>
{
    public void Configure(EntityTypeBuilder<BackupRecord> builder)
    {
        builder.HasIndex(b => new { b.Kind, b.CreatedAt });
        builder.HasIndex(b => new { b.Kind, b.SlotKey }).IsUnique().HasFilter("[SlotKey] IS NOT NULL AND [IsDeleted] = 0");
    }
}

/// <summary>هر مقدار IP فقط یک قانون.</summary>
public class IpRuleConfiguration : IEntityTypeConfiguration<IpRule>
{
    public void Configure(EntityTypeBuilder<IpRule> builder)
    {
        builder.HasIndex(r => r.Value).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(r => r.ExpiresAt);
    }
}

/// <summary>هر الگوی User-Agent فقط یک قانون.</summary>
public class BotRuleConfiguration : IEntityTypeConfiguration<BotRule>
{
    public void Configure(EntityTypeBuilder<BotRule> builder)
    {
        builder.HasIndex(r => r.Pattern).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.Property(r => r.IsActive).HasDefaultValue(true).HasSentinel(true);
    }
}

/// <summary>هر کار فقط یک ردیف تنظیم.</summary>
public class BackgroundJobSettingConfiguration : IEntityTypeConfiguration<BackgroundJobSetting>
{
    public void Configure(EntityTypeBuilder<BackgroundJobSetting> builder) =>
        builder.HasIndex(j => j.JobName).IsUnique().HasFilter("[IsDeleted] = 0");
}

/// <summary>هر شاخص فقط یک قانون؛ فاصلهٔ تکرار هشدار ۵ دقیقه تا ۷ روز.</summary>
public class AlertRuleConfiguration : IEntityTypeConfiguration<AlertRule>
{
    public void Configure(EntityTypeBuilder<AlertRule> builder)
    {
        builder.HasIndex(r => r.Metric).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.Property(r => r.IsEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(r => r.CooldownMinutes).HasDefaultValue(60).HasSentinel(60);
        builder.ToTable(t => t.HasCheckConstraint("CK_AlertRules_Cooldown", "[CooldownMinutes] BETWEEN 5 AND 10080"));
    }
}

/// <summary>تاریخچهٔ هشدارها به ترتیب زمان.</summary>
public class AlertEventConfiguration : IEntityTypeConfiguration<AlertEvent>
{
    public void Configure(EntityTypeBuilder<AlertEvent> builder) =>
        builder.HasIndex(e => e.CreatedAt);
}
