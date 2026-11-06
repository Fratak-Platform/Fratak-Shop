using Fratak_Shop.Data.Entities.Analytics;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

// پیکربندی جدول‌های آمار بازدید مرحلهٔ هفتم (فاز ۷).

/// <summary>
/// یک ردیف برای هر بازدیدکننده در هر روز (ایندکس یکتا با شرط IsDeleted).
/// ایندکس‌های Day و (Day, Source) برای گزارش‌های بازه‌ای قیف و منبع ورودی هستند.
/// </summary>
public class VisitorDayConfiguration : IEntityTypeConfiguration<VisitorDay>
{
    public void Configure(EntityTypeBuilder<VisitorDay> builder)
    {
        builder.HasIndex(v => new { v.Day, v.VisitorKey }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(v => new { v.Day, v.Source, v.Medium });
        builder.HasIndex(v => v.VisitorKey);
        builder.ToTable(t => t.HasCheckConstraint("CK_VisitorDays_FirstHour", "[FirstHour] BETWEEN 0 AND 23"));
    }
}

/// <summary>شمارندهٔ روزانه: کلید یکتا (Day, Metric, Key)؛ ایندکس (Metric, Day) برای جمع بازه‌ای یک متریک.</summary>
public class AnalyticsCounterConfiguration : IEntityTypeConfiguration<AnalyticsCounter>
{
    public void Configure(EntityTypeBuilder<AnalyticsCounter> builder)
    {
        builder.HasIndex(c => new { c.Day, c.Metric, c.Key }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(c => new { c.Metric, c.Day }).IncludeProperties(c => new { c.Key, c.Count });
    }
}

/// <summary>
/// ایندکس منبع ورودی سفارش‌ها برای گزارش کمپین‌ها. (بقیهٔ پیکربندی سفارش در فایل‌های قبلی است؛
/// EF چند پیکربندی یک موجودیت را با هم ترکیب می‌کند.)
/// </summary>
public class OrderTrafficConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasIndex(o => new { o.TrafficSource, o.CreatedAt });
        builder.HasIndex(o => o.VisitorKey).HasFilter("[VisitorKey] IS NOT NULL");
    }
}
