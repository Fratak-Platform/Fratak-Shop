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
}
