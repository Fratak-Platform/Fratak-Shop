using Fratak_Shop.Data.Entities.Pages;

namespace Fratak_Shop.Core.DTOs.Content.Command;

public class SaveContentPageCommand
{
    public Guid? Id { get; set; }
    public SitePage Page { get; set; }
    public string Title { get; set; } = null!;
    public string? Summary { get; set; }
    public string? Content { get; set; }
    public bool IsPublished { get; set; } = true;
    public int DisplayOrder { get; set; }
}
