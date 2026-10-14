using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.DTOs.Faqs.Query;

public class FaqItemDto : BaseDto
{
    public string Question { get; set; } = null!;
    public string Answer { get; set; } = null!;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
