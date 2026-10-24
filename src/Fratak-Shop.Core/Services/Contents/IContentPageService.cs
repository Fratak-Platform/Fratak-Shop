using Fratak_Shop.Core.DTOs.Contents;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Contents;

/// <summary>Editable static pages: about us, terms, privacy, return policy, guides, ...</summary>
public interface IContentPageService
{
    /// <summary>Active page by slug (null when missing or disabled).</summary>
    Task<ContentPageDto?> GetActiveBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Active pages flagged for the footer, ordered (cached).</summary>
    Task<List<ContentPageLinkDto>> GetActiveLinksAsync(CancellationToken cancellationToken = default);

    // ── Admin panel ──
    Task<List<ContentPageDto>> GetAllAsync();
    Task<ContentPageDto?> GetByIdAsync(Guid id);
    Task<OperationResult<Guid>> SaveAsync(ContentPageCommand command);
    Task<OperationResult> DeleteAsync(Guid id);
}
