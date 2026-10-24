using Fratak_Shop.Core.DTOs.Faqs.Command;
using Fratak_Shop.Core.DTOs.Faqs.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Faqs;

public interface IFaqItemService
{
    Task<OperationResult> CreateFaqItemAsync(CreateFaqItemCommand command);
    Task<OperationResult> EditFaqItemAsync(EditFaqItemCommand command);
    Task<OperationResult> DeleteFaqItemAsync(Guid id);
    List<FaqItemDto> GetAllFaqItemsAsync();
    Task<List<FaqItemDto>> GetActiveFaqItemsAsync();
    Task<FaqItemDto?> GetFaqItemByIdAsync(Guid id);

    /// <summary>Active categories with their active questions, for the FAQ page (cached).</summary>
    Task<List<FaqGroupDto>> GetFaqGroupsAsync(CancellationToken cancellationToken = default);

    // ── Categories ──
    Task<List<FaqCategoryDto>> GetCategoriesAsync();
    Task<OperationResult<Guid>> SaveCategoryAsync(FaqCategoryCommand command);
    Task<OperationResult> DeleteCategoryAsync(Guid id);
}
