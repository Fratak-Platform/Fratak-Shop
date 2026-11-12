using Fratak_Shop.Data.Entities.Team;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

// پیکربندی جدول‌های امنیت و کار تیمی کارکنان (مرحلهٔ هفتم – فاز ۱۰). توضیح هر جدول: Entities/Team/TeamEntities.cs.

/// <summary>فهرست درخواست‌ها بر اساس وضعیت و زمان؛ و درخواست‌های هر کارمند.</summary>
public class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.Property(a => a.Amount).HasPrecision(18, 0);
        builder.HasIndex(a => new { a.Status, a.CreatedAt });
        builder.HasIndex(a => new { a.RequestedById, a.CreatedAt });
        builder.HasIndex(a => new { a.TargetKey, a.Status });
    }
}

/// <summary>کارهای باز هر کارمند به ترتیب موعد؛ و یادآوری‌های سررسیده.</summary>
public class StaffTaskConfiguration : IEntityTypeConfiguration<StaffTask>
{
    public void Configure(EntityTypeBuilder<StaffTask> builder)
    {
        builder.HasIndex(t => new { t.AssigneeId, t.IsDone, t.DueAt });
        builder.HasIndex(t => new { t.IsDone, t.DueAt, t.ReminderSentAt });
    }
}
