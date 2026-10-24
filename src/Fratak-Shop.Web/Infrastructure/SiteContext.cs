using Fratak_Shop.Core.DTOs.Contents;
using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Services.Contents;
using Fratak_Shop.Core.Services.Settings;
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
    bool IsLoaded { get; }

    /// <summary>Safe logo URL (configured one, or the bundled logo).</summary>
    string LogoUrl { get; }

    /// <summary>Safe favicon URL (configured one, else the logo).</summary>
    string FaviconUrl { get; }

    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>"Page title | Site name" using the configured separator.</summary>
    string Title(string? pageTitle);
}

public class SiteContext(
    IStoreSettingService settingService,
    IContentPageService contentPages,
    ISiteContentService siteContent) : ISiteContext
{
    private StoreSettingDto? _settings;

    public StoreSettingDto Settings => _settings ?? throw new InvalidOperationException("Site context was not loaded.");
    public IReadOnlyList<ContentPageLinkDto> FooterPages { get; private set; } = [];
    public IReadOnlyList<SiteFeatureDto> Features { get; private set; } = [];
    public IReadOnlyList<TrustBadgeDto> TrustBadges { get; private set; } = [];
    public bool IsLoaded => _settings is not null;

    public string LogoUrl => Helpers.UrlSafety.Link(Settings.LogoUrl, DefaultLogo);

    public string FaviconUrl => Helpers.UrlSafety.Link(Settings.FaviconUrl, LogoUrl);

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
