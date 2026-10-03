using Fratak_Shop.Core.DTOs.Dashboard.Query;

namespace Fratak_Shop.Core.Services.Dashboard;

/// <summary>
/// Aggregates every key indicator of the shop (sales, stock, content, messages, SEO)
/// for the admin dashboard, so the landing page of the panel is one call instead of
/// a dozen queries scattered in the page model.
/// </summary>
public interface IDashboardService
{
    Task<AdminDashboardDto> GetAsync(CancellationToken cancellationToken = default);
}
