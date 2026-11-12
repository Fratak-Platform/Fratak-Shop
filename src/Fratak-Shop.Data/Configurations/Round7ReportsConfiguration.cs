using Fratak_Shop.Data.Entities.Insights;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

// پیکربندی جدول‌های گزارش‌های مدیریتی مرحلهٔ هفتم (فاز ۷ب): هزینه، هدف فروش، گزارش زمان‌بندی‌شده.

/// <summary>هزینه: مبلغ مثبت؛ ایندکس تاریخ برای گزارش بازه‌ای و ایندکس سررسید برای یادآوری‌ها.</summary>
public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.Property(e => e.Amount).HasPrecision(18, 0); // تومان، بدون اعشار (مثل مبالغ سفارش)
        builder.HasIndex(e => new { e.SpentOn, e.Category });
        builder.HasIndex(e => e.NextDueOn).HasFilter("[NextDueOn] IS NOT NULL AND [IsDeleted] = 0");
        builder.HasIndex(e => e.RecurringParentId);
        builder.ToTable(t => t.HasCheckConstraint("CK_Expenses_Amount", "[Amount] > 0"));
    }
}

/// <summary>هر ماه شمسی فقط یک هدف دارد.</summary>
public class SalesGoalConfiguration : IEntityTypeConfiguration<SalesGoal>
{
    public void Configure(EntityTypeBuilder<SalesGoal> builder)
    {
        builder.Property(g => g.TargetAmount).HasPrecision(18, 0);
        builder.HasIndex(g => new { g.Year, g.Month }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_SalesGoals_Month", "[Month] BETWEEN 1 AND 12");
            t.HasCheckConstraint("CK_SalesGoals_Target", "[TargetAmount] >= 0");
        });
    }
}

/// <summary>گزارش زمان‌بندی‌شده: ایندکس (فعال، زمان اجرای بعدی) برای پیدا کردن گزارش‌های سررسیده.</summary>
public class ScheduledReportConfiguration : IEntityTypeConfiguration<ScheduledReport>
{
    public void Configure(EntityTypeBuilder<ScheduledReport> builder)
    {
        builder.HasIndex(r => new { r.IsActive, r.NextRunAt });
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_ScheduledReports_Hour", "[Hour] BETWEEN 0 AND 23");
            t.HasCheckConstraint("CK_ScheduledReports_WeekDay", "[WeekDay] BETWEEN 0 AND 6");
        });
    }
}

public class ReportDeliveryConfigConfiguration : IEntityTypeConfiguration<ReportDeliveryConfig>
{
    public void Configure(EntityTypeBuilder<ReportDeliveryConfig> builder) =>
        builder.ToTable(t => t.HasCheckConstraint("CK_ReportDeliveryConfigs_Port", "[SmtpPort] BETWEEN 1 AND 65535"));
}
