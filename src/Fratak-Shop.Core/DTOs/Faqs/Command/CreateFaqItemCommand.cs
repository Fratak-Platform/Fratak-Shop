namespace Fratak_Shop.Core.DTOs.Faqs.Command;

public class CreateFaqItemCommand
{
    public Guid? CategoryId { get; set; }
    public string Question { get; set; } = null!;

    /// <summary>Answer (safe HTML is allowed: links, lists, bold text).</summary>
    public string Answer { get; set; } = null!;

    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class EditFaqItemCommand : CreateFaqItemCommand
{
    public Guid Id { get; set; }
}

public class FaqCategoryCommand
{
    public Guid Id { get; set; }
    public string Title { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Description { get; set; }
    public string? IconKey { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}
