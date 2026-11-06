using Fratak_Shop.Core.DTOs.Contents;
using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Services.Branding;
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

    /// <summary>
    /// برند قابل‌تنظیم (مرحلهٔ دهم): آیکون‌های روشن/تیره، متن «فراتک» + «شاپ» و رنگ‌هایشان، همه از تنظیمات.
    /// محل استفاده: partial «_BrandLockup» (هدر، ورود، ثبت‌نام، پنل، فاکتور).
    /// </summary>
    BrandLockup Brand { get; }

    /// <summary>آیا متن برند در این محل کنار آیکون نوشته شود؟ مثال: Header → پیش‌فرض false.</summary>
    bool ShowBrandText(BrandPlace place);

    /// <summary>
    /// مرحلهٔ دوازدهم: تنظیمات «استودیوی لوگو» (JSON ستون BrandStyleJson، خوانده‌شده و بی‌خطا).
    /// </summary>
    BrandStyleConfig BrandStyle { get; }

    /// <summary>
    /// لوگوی نهایی یک محل (اندازه‌ها، فاصله‌ها، رنگ‌ها، متن‌ها و آیکون). در هر درخواست یک بار برای هر محل ساخته می‌شود.
    /// محل استفاده: partial «_BrandLockup»، قالب ایمیل و صفحهٔ تعمیرات.
    /// </summary>
    BrandView BrandFor(BrandPlace place);

    /// <summary>آیکون‌های اپ (PWA) و آیفون (اختصاصی مدیر یا آیکون‌های مشکی برند).</summary>
    PwaIconSet Pwa { get; }

    /// <summary>آدرس مانیفست با نسخه‌ای که از آیکون‌های اپ ساخته می‌شود؛ با هر تغییر آیکون، مرورگر مانیفست تازه می‌خواند.</summary>
    string ManifestUrl { get; }

    /// <summary>آیکون اپ (PWA): آیکون آپلودشدهٔ مدیر یا آیکون مشکی برند <see cref="BrandIdentity.AppIcon512"/>.</summary>
    string PwaIconUrl { get; }

    /// <summary>True وقتی مدیر آیکون اپ اختصاصی نگذاشته و آیکون‌های برند (any + maskable) استفاده می‌شوند.</summary>
    bool UsesBrandPwaIcon { get; }

    /// <summary>آیکون لوگو برای زمینهٔ روشن (همان <see cref="BrandLockup.IconUrl"/>؛ برای سازگاری با کد قبلی).</summary>
    string LogoUrl { get; }

    /// <summary>آیکون لوگو برای زمینهٔ تیره (همان <see cref="BrandLockup.IconDarkUrl"/>).</summary>
    string LogoDarkUrl { get; }

    /// <summary>نسخهٔ PNG لوگو (schema.org، فاکتور PDF): لوگوی مدیر یا <see cref="BrandIdentity.LogoPng"/>.</summary>
    string LogoRasterUrl { get; }

    /// <summary>True وقتی لوگوی پیش‌فرض برند استفاده می‌شود (مدیر لوگوی دیگری نگذاشته).</summary>
    bool UsesBrandLogo { get; }

    /// <summary>Safe favicon URL (configured one, else the brand favicon <see cref="BrandIdentity.FaviconSvg"/>).</summary>
    string FaviconUrl { get; }

    /// <summary>True وقتی مدیر فاوآیکن اختصاصی نگذاشته و فاوآیکن‌های برند (svg + ico + apple) لینک می‌شوند.</summary>
    bool UsesBrandFavicon { get; }

    /// <summary>True when the announcement bar is on and has a text.</summary>
    bool ShowAnnouncement { get; }

    /// <summary>Validated announcement background colour (hex).</summary>
    string AnnouncementColor { get; }

    /// <summary>دور ۲۱: ظاهر کامل و امن نوار اطلاعیه (سبک، جلوه، اندازه، پیام‌های چرخشی).</summary>
    AnnouncementLook Announcement { get; }

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

    private BrandLockup? _brand;

    public BrandLockup Brand => _brand ??= BrandLockup.From(Settings);

    public bool ShowBrandText(BrandPlace place) => BrandFor(place).ShowText;

    private BrandStyleConfig? _brandStyle;
    private readonly Dictionary<BrandPlace, BrandView> _brandViews = [];
    private PwaIconSet? _pwa;

    public BrandStyleConfig BrandStyle => _brandStyle ??= BrandStyleRules.Parse(Settings.BrandStyleJson);

    public BrandView BrandFor(BrandPlace place)
    {
        if (!_brandViews.TryGetValue(place, out var view))
            _brandViews[place] = view = BrandResolver.Resolve(place, Settings, BrandStyle, BrandIdentity.Defaults);
        return view;
    }

    public PwaIconSet Pwa => _pwa ??= PwaIconSet.From(Settings, BrandStyle);

    // نسخهٔ ثابت + ۸ نویسه از هش آیکون‌ها. مثال: /manifest.webmanifest?v=brand4-1a2b3c4d
    public string ManifestUrl => BrandIdentity.ManifestUrl + "-" + Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Pwa.Fingerprint)))[..8].ToLowerInvariant();

    public bool UsesBrandPwaIcon => Pwa.IsDefault;

    public string PwaIconUrl => Pwa.Icon512;

    public string LogoUrl => Brand.IconUrl;

    public bool UsesBrandLogo => Brand.IsDefaultIcon;

    public string LogoDarkUrl => Brand.IconDarkUrl;

    // schema.org (Organization.logo): لوگوی افقی PNG برند، یا آیکون آپلودشدهٔ مدیر.
    public string LogoRasterUrl => UsesBrandLogo ? BrandIdentity.LogoPng : LogoUrl;

    public bool UsesBrandFavicon => !Helpers.UrlSafety.IsSafeLink(Settings.FaviconUrl);

    public string FaviconUrl => Helpers.UrlSafety.Link(Settings.FaviconUrl, BrandIdentity.FaviconSvg);

    public bool ShowAnnouncement => Settings.IsAnnouncementEnabled && !string.IsNullOrWhiteSpace(Settings.AnnouncementText);

    public string AnnouncementColor => SafeColor(Settings.AnnouncementColor, BrandIdentity.DefaultAnnouncementColor);

    private AnnouncementLook? _announcement;

    public AnnouncementLook Announcement => _announcement ??= AnnouncementLook.Build(
        Settings.AnnouncementColor, Settings.AnnouncementColor2, Settings.AnnouncementTextColor,
        Settings.AnnouncementStyle, Settings.AnnouncementEffect, Settings.AnnouncementFontSize, Settings.AnnouncementFontWeight,
        Settings.AnnouncementText, Settings.AnnouncementMessages, Settings.IsAnnouncementCloseable);

    public string ThemeColor => SafeColor(Settings.ThemeColor, BrandIdentity.DefaultThemeColor);

    private static string SafeColor(string? value, string fallback) =>
        value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$") ? value : fallback;

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
            "/club" or "/profile/club" => _settings.IsLoyaltyEnabled,
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
