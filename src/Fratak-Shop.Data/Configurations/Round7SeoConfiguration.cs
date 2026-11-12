using Fratak_Shop.Data.Entities.SeoEngine;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

// پیکربندی جدول‌های موتور سئوی هوشمند (مرحلهٔ هفتم – فاز ۸).

/// <summary>هر صفحه فقط یک ردیف نمره دارد؛ ایندکس نمره برای فهرست «ضعیف‌ترین صفحه‌ها».</summary>
public class SeoScoreConfiguration : IEntityTypeConfiguration<SeoScore>
{
    public void Configure(EntityTypeBuilder<SeoScore> builder)
    {
        builder.HasIndex(s => new { s.Kind, s.EntityId }).IsUnique();
        builder.HasIndex(s => new { s.Score, s.Views });
        builder.ToTable(t => t.HasCheckConstraint("CK_SeoScores_Score", "[Score] BETWEEN 0 AND 100"));
    }
}

/// <summary>ایندکس (وضعیت، تاریخ) برای صف بررسی و (نوع، شناسه) برای تاریخچهٔ هر صفحه.</summary>
public class SeoSuggestionConfiguration : IEntityTypeConfiguration<SeoSuggestion>
{
    public void Configure(EntityTypeBuilder<SeoSuggestion> builder)
    {
        builder.HasIndex(s => new { s.Status, s.CreatedAt });
        builder.HasIndex(s => new { s.Kind, s.EntityId, s.Status });
    }
}

/// <summary>
/// جدول دانش سئو (مرحلهٔ شانزدهم): هر (نوع، واژه، موضوع) یکتاست؛ ایندکس (نوع، منبع) برای بارگذاری و صفحهٔ پنل.
/// مقدار پیش‌فرض IsActive = true با Sentinel تا false هم واقعاً ذخیره شود.
/// </summary>
public class SeoKnowledgeEntryConfiguration : IEntityTypeConfiguration<SeoKnowledgeEntry>
{
    public void Configure(EntityTypeBuilder<SeoKnowledgeEntry> builder)
    {
        builder.HasIndex(e => new { e.Kind, e.Term, e.Topic }).IsUnique();
        builder.HasIndex(e => new { e.Kind, e.Source });
        builder.Property(e => e.Topic).HasDefaultValue("").UseCollation(SeoKnowledgeEntry.ExactCollation);
        // مقایسهٔ دقیق (باینری): وگرنه SQL Server نیم‌فاصله و بزرگی/کوچکی حروف را نادیده می‌گیرد و «نرم‌افزار» و «نرمافزار»
        // (که هر دو در دانش لازم‌اند: یکی شکل درست و دیگری شکل غلط برای اصلاح) تکراری حساب می‌شوند.
        builder.Property(e => e.Term).UseCollation(SeoKnowledgeEntry.ExactCollation);
        builder.Property(e => e.IsActive).HasDefaultValue(true).HasSentinel(true);
    }
}

/// <summary>ایندکس (نوع فیلد، نسخه) برای آمار «کدام نسخه بیشتر پذیرفته می‌شود».</summary>
public class SmartTextFeedbackConfiguration : IEntityTypeConfiguration<SmartTextFeedback>
{
    public void Configure(EntityTypeBuilder<SmartTextFeedback> builder) =>
        builder.HasIndex(f => new { f.Purpose, f.Variant });
}

/// <summary>هر (نوع، کلید) فقط یک ردیف کش.</summary>
public class SeoResearchCacheConfiguration : IEntityTypeConfiguration<SeoResearchCache>
{
    public void Configure(EntityTypeBuilder<SeoResearchCache> builder)
    {
        builder.HasIndex(c => new { c.Kind, c.Key }).IsUnique();
        builder.HasIndex(c => c.ExpiresAt);
    }
}

/// <summary>نوبت زمان‌بندی‌شده یکتاست (اجرای دستی SlotKey ندارد).</summary>
public class SeoEngineRunConfiguration : IEntityTypeConfiguration<SeoEngineRun>
{
    public void Configure(EntityTypeBuilder<SeoEngineRun> builder)
    {
        // فیلتر IsDeleted باید در خود فیلتر باشد، وگرنه DbContext آن را با «[IsDeleted] = 0» جایگزین می‌کند.
        builder.HasIndex(r => r.SlotKey).IsUnique().HasFilter("[SlotKey] IS NOT NULL AND [IsDeleted] = 0");
        builder.HasIndex(r => r.StartedAt);
    }
}

// ── ابزارهای سئو (فاز ۸ب) ──

/// <summary>هر (صفحه، آدرس) فقط یک ردیف؛ ایندکس (مشکل، نادیده) برای فیلتر صفحهٔ «لینک‌های شکسته».</summary>
public class SeoLinkIssueConfiguration : IEntityTypeConfiguration<SeoLinkIssue>
{
    public void Configure(EntityTypeBuilder<SeoLinkIssue> builder)
    {
        builder.HasIndex(i => new { i.Kind, i.EntityId, i.UrlHash }).IsUnique();
        builder.HasIndex(i => new { i.Problem, i.IsIgnored });
    }
}

/// <summary>ایندکس مسیر برای «عبارت‌های این صفحه» و ایندکس نمایش برای پرنمایش‌ترین‌ها.</summary>
public class SearchConsoleQueryStatConfiguration : IEntityTypeConfiguration<SearchConsoleQueryStat>
{
    public void Configure(EntityTypeBuilder<SearchConsoleQueryStat> builder)
    {
        builder.HasIndex(s => s.Path);
        builder.HasIndex(s => s.Impressions);
    }
}

/// <summary>برای هر روز یک ردیف.</summary>
public class SearchConsoleDailyStatConfiguration : IEntityTypeConfiguration<SearchConsoleDailyStat>
{
    public void Configure(EntityTypeBuilder<SearchConsoleDailyStat> builder) =>
        builder.HasIndex(s => s.Date).IsUnique();
}

/// <summary>پیش‌فرض‌های روشن برای ستون‌های bool (با Sentinel تا مقدار false هم واقعاً ذخیره شود).</summary>
public class SeoToolsConfigConfiguration : IEntityTypeConfiguration<SeoToolsConfig>
{
    public void Configure(EntityTypeBuilder<SeoToolsConfig> builder)
    {
        builder.Property(c => c.IsLinkCheckEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(c => c.CheckExternalLinks).HasDefaultValue(true).HasSentinel(true);
    }
}
