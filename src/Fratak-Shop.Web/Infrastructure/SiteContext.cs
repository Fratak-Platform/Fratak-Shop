using Fratak_Shop.Core.DTOs.Contents;
using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Services.Contents;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Contents;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// Per-request snapshot of the database-backed store settings and shared layout content
/// (footer pages, features, trust badges). Loaded once per request by
/// <see cref="SiteContextPageFilter"/>; the services behind it are memory cached, so
/// this costs no database round trip on most requests.
/// </summary>
public interface ISiteContext
{
    StoreSettingDto Settings { get; }
    IReadOnlyList<ContentPageLinkDto> FooterPages { get; }
    IReadOnlyList<SiteFeatureDto> Features { get; }
    IReadOnlyList<TrustBadgeDto> TrustBadges { get; }

    /// <summary>Active DB menus (header + footer columns); links of disabled features are hidden.</summary>
    IReadOnlyList<MenuDto> Menus { get; }

    bool IsLoaded { get; }

    /// <summary>Visible items of the first menu at <paramref name="location"/>.</summary>
    List<MenuItemDto> MenuItems(MenuLocation location);

    /// <summary>False for links to features turned off in the settings (club, order tracking, wallet).</summary>
    bool IsLinkEnabled(string? url);

    /// <summary>Safe logo URL (configured one, or the bundled logo).</summary>
    string LogoUrl { get; }

    /// <summary>Safe favicon URL (configured one, else the logo).</summary>
    string FaviconUrl { get; }

    /// <summary>True when the announcement bar is on and has a text.</summary>
    bool ShowAnnouncement { get; }

    /// <summary>Validated announcement background colour (hex).</summary>
    string AnnouncementColor { get; }

    /// <summary>Validated browser / PWA theme colour (hex).</summary>
    string ThemeColor { get; }

    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>"Page title | Site name" using the configured separator.</summary>
    string Title(string? pageTitle);
}

public class SiteContext(
    IStoreSettingService settingService,
    IContentPageService contentPages,
    ISiteContentService siteContent,
    IMenuService menuService) : ISiteContext
{
    private StoreSettingDto? _settings;

    public StoreSettingDto Settings => _settings ?? throw new InvalidOperationException("Site context was not loaded.");
    public IReadOnlyList<ContentPageLinkDto> FooterPages { get; private set; } = [];
    public IReadOnlyList<SiteFeatureDto> Features { get; private set; } = [];
    public IReadOnlyList<TrustBadgeDto> TrustBadges { get; private set; } = [];
    public IReadOnlyList<MenuDto> Menus { get; private set; } = [];
    public bool IsLoaded => _settings is not null;

    public string LogoUrl => Helpers.UrlSafety.Link(Settings.LogoUrl, DefaultLogo);

    public string FaviconUrl => Helpers.UrlSafety.Link(Settings.FaviconUrl, LogoUrl);

    public bool ShowAnnouncement => Settings.IsAnnouncementEnabled && !string.IsNullOrWhiteSpace(Settings.AnnouncementText);

    public string AnnouncementColor => SafeColor(Settings.AnnouncementColor, "#0369a1");

    public string ThemeColor => SafeColor(Settings.ThemeColor, "#0ea5e9");

    private static string SafeColor(string? value, string fallback) =>
        value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$") ? value : fallback;

    private const string DefaultLogo = "/assets/image/logo.png";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_settings is not null)
            return;

        // Sequential on purpose: the services share one scoped DbContext.
        var settings = await settingService.GetAsync(cancellationToken);
        var pages = await contentPages.GetActiveLinksAsync(cancellationToken);
        Features = await siteContent.GetActiveFeaturesAsync(cancellationToken);
        TrustBadges = await siteContent.GetActiveTrustBadgesAsync(cancellationToken);
        FooterPages = pages.Where(page => page.ShowInFooter).ToList();
        _settings = settings;

        var menus = await menuService.GetActiveMenusAsync(cancellationToken);
        foreach (var menu in menus)
            menu.Items = menu.Items.Where(item => IsLinkEnabled(item.Url)).ToList();
        Menus = menus;
    }

    public List<MenuItemDto> MenuItems(MenuLocation location) =>
        Menus.FirstOrDefault(menu => menu.Location == location)?.Items ?? [];

    public bool IsLinkEnabled(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || _settings is null)
            return true;

        var path = url.Split('?', '#')[0].TrimEnd('/').ToLowerInvariant();
        return path switch
        {
            "/club" => _settings.IsLoyaltyEnabled,
            "/track-order" => _settings.IsGuestOrderTrackingEnabled,
            "/profile/wallet" => _settings.IsWalletEnabled,
            _ => true,
        };
    }

    public string Title(string? pageTitle)
    {
        var siteName = Settings.SiteName;
        if (string.IsNullOrWhiteSpace(pageTitle) || pageTitle == siteName)
            return siteName;

        var separator = string.IsNullOrWhiteSpace(Settings.TitleSeparator) ? "|" : Settings.TitleSeparator.Trim();
        return $"{pageTitle} {separator} {siteName}";
    }
}

/// <summary>Loads <see cref="ISiteContext"/> before any page handler runs (also for re-executed error pages).</summary>
public class SiteContextPageFilter(ISiteContext site) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        await site.LoadAsync(context.HttpContext.RequestAborted);
        await next();
    }
}
