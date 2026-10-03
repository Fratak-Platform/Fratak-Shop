using Fratak_Shop.Core.DTOs.Navigation.Query;
using Fratak_Shop.Core.DTOs.Settings.Query;

namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// Everything the storefront layout needs in every request: site settings, the menus
/// managed from the panel, cart/notification counters and the current path (for the
/// active menu state). Loaded once by <see cref="FratakPageModel"/>.
/// </summary>
public class LayoutViewModel
{
    public StorefrontSettings Site { get; set; } = new();
    public List<MenuItemDto> HeaderMenu { get; set; } = [];
    public List<MenuItemDto> FooterColumns { get; set; } = [];
    public List<MenuItemDto> FooterBottom { get; set; } = [];
    public List<MenuItemDto> ProfileMenu { get; set; } = [];
    public List<SocialLinkDto> HeaderSocials { get; set; } = [];
    public List<SocialLinkDto> FooterSocials { get; set; } = [];
    public int CartCount { get; set; }
    public int UnreadNotifications { get; set; }
    public string CurrentPath { get; set; } = "/";
    public bool IsAuthenticated { get; set; }
    public string? UserFullName { get; set; }
    public bool IsStaff { get; set; }

    public bool IsActive(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var normalized = url.TrimEnd('/');
        if (normalized.Length == 0)
            normalized = "/";

        var current = CurrentPath.TrimEnd('/');
        if (current.Length == 0)
            current = "/";

        return current == normalized || (normalized != "/" && current.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase));
    }
}
