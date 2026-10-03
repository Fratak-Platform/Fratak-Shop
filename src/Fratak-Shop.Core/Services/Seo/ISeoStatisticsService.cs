using Fratak_Shop.Data.Entities.Seo;

namespace Fratak_Shop.Core.Services.Seo;

/// <summary>
/// Aggregated content statistics used by the SEO centre: daily visits per entity
/// and the reports built on top of them (most visited content, content without any
/// visit, visit trend). Visits are stored one row per entity per day so the table
/// stays small even on a busy shop.
/// </summary>
public interface ISeoStatisticsService
{
    Task RegisterVisitAsync(SeoEntityType entityType, Guid entityId, CancellationToken cancellationToken = default);

    /// <summary>پربازدیدترین محتواها در بازه مشخص.</summary>
    Task<List<ContentVisitStat>> GetTopContentAsync(int days = 30, int take = 10, CancellationToken cancellationToken = default);

    /// <summary>محتوایی که در بازه مشخص هیچ بازدیدی نداشته است.</summary>
    Task<List<ContentVisitStat>> GetUnvisitedContentAsync(int days = 30, int take = 10, CancellationToken cancellationToken = default);

    /// <summary>روند بازدید روزانه برای نمودار پنل مدیریت.</summary>
    Task<List<DailyVisitStat>> GetDailyTrendAsync(int days = 14, CancellationToken cancellationToken = default);

    /// <summary>مجموع بازدیدهای بازه مشخص.</summary>
    Task<int> GetTotalVisitsAsync(int days = 30, CancellationToken cancellationToken = default);
}

public class ContentVisitStat
{
    public SeoEntityType EntityType { get; set; }
    public Guid EntityId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Url { get; set; }
    public int TotalVisits { get; set; }
    public DateTimeOffset? LastVisitAt { get; set; }
}

public class DailyVisitStat
{
    public DateOnly Date { get; set; }
    public int Visits { get; set; }
    public string PersianLabel { get; set; } = string.Empty;
}
