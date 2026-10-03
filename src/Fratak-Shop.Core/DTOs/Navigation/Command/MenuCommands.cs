using Fratak_Shop.Data.Entities.Navigation;

namespace Fratak_Shop.Core.DTOs.Navigation.Command;

public class SaveMenuItemCommand
{
    public Guid? Id { get; set; }
    public MenuLocation Location { get; set; }
    public Guid? ParentId { get; set; }
    public string Title { get; set; } = null!;
    public string? Url { get; set; }
    public string? Icon { get; set; }
    public string? BadgeText { get; set; }
    public string? BadgeColor { get; set; }
    public bool OpenInNewTab { get; set; }
    public bool ShowOnMobile { get; set; } = true;
    public bool IsHighlighted { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class SaveSocialLinkCommand
{
    public Guid? Id { get; set; }
    public SocialPlatform Platform { get; set; }
    public string? Url { get; set; }
    public string? Title { get; set; }
    public bool ShowInHeader { get; set; }
    public bool ShowInFooter { get; set; } = true;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
