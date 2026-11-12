using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.DTOs.Faqs.Query;

public class FaqItemDto : BaseDto
{
    public Guid? CategoryId { get; set; }
    public string? CategoryTitle { get; set; }
    public string Question { get; set; } = null!;
    public string Answer { get; set; } = null!;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class FaqCategoryDto : BaseDto
{
    public string Title { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Description { get; set; }
    public string? IconKey { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public int ItemCount { get; set; }
}

/// <summary>An FAQ category with its active questions (FAQ page).</summary>
public class FaqGroupDto
{
    public Guid? CategoryId { get; set; }
    public string Title { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Description { get; set; }
    public string? IconKey { get; set; }
    public List<FaqItemDto> Items { get; set; } = [];
}
