using Fratak_Shop.Core.DTOs.Contents;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Contents;

/// <summary>
/// Editable layout blocks: home page sections (order / visibility / titles),
/// purchase benefits ("ارسال سریع"، "پشتیبانی"، ...) and trust badges (اینماد، ساماندهی).
/// </summary>
public interface ISiteContentService
{
    Task<List<HomeSectionDto>> GetActiveHomeSectionsAsync(CancellationToken cancellationToken = default);
    Task<List<SiteFeatureDto>> GetActiveFeaturesAsync(CancellationToken cancellationToken = default);
    Task<List<TrustBadgeDto>> GetActiveTrustBadgesAsync(CancellationToken cancellationToken = default);

    // ── Admin panel ──
    Task<List<HomeSectionDto>> GetAllHomeSectionsAsync();
    Task<OperationResult<Guid>> SaveHomeSectionAsync(HomeSectionDto command);
    Task<OperationResult> DeleteHomeSectionAsync(Guid id);

    Task<List<SiteFeatureDto>> GetAllFeaturesAsync();
    Task<OperationResult<Guid>> SaveFeatureAsync(SiteFeatureDto command);
    Task<OperationResult> DeleteFeatureAsync(Guid id);

    Task<List<TrustBadgeDto>> GetAllTrustBadgesAsync();
    Task<OperationResult<Guid>> SaveTrustBadgeAsync(TrustBadgeDto command);
    Task<OperationResult> DeleteTrustBadgeAsync(Guid id);
}
