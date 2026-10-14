namespace Fratak_Shop.Core.DTOs.Faqs.Command;

public class CreateFaqItemCommand
{
    public string Question { get; set; } = null!;
    public string Answer { get; set; } = null!;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class EditFaqItemCommand
{
    public Guid Id { get; set; }
    public string Question { get; set; } = null!;
    public string Answer { get; set; } = null!;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
