using Fratak_Shop.Core.DTOs.Navigation.Command;
using Fratak_Shop.Core.DTOs.Navigation.Query;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Navigation;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Navigation;

public class MenuService(FratakShopDbContext context, ISiteSettingService settingService) : IMenuService
{
    // -------------------------------------------------------------- storefront

    public async Task<List<MenuItemDto>> GetByLocationAsync(MenuLocation location, CancellationToken cancellationToken = default)
    {
        var items = await context.MenuItems
            .Where(item => item.Location == location && item.IsActive)
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Title)
            .ToListAsync(cancellationToken);

        return BuildTree(items);
    }

    public Task<List<MenuItemDto>> GetHeaderAsync(CancellationToken cancellationToken = default) =>
        GetByLocationAsync(MenuLocation.Header, cancellationToken);

    public Task<List<MenuItemDto>> GetFooterColumnsAsync(CancellationToken cancellationToken = default) =>
        GetByLocationAsync(MenuLocation.Footer, cancellationToken);

    public Task<List<MenuItemDto>> GetFooterBottomAsync(CancellationToken cancellationToken = default) =>
        GetByLocationAsync(MenuLocation.FooterBottom, cancellationToken);

    public Task<List<MenuItemDto>> GetProfileMenuAsync(CancellationToken cancellationToken = default) =>
        GetByLocationAsync(MenuLocation.ProfileSidebar, cancellationToken);

    public Task<List<MenuItemDto>> GetQuickAccessAsync(CancellationToken cancellationToken = default) =>
        GetByLocationAsync(MenuLocation.QuickAccess, cancellationToken);

    public async Task<List<SocialLinkDto>> GetSocialLinksAsync(bool? inHeader = null, bool? inFooter = null)
    {
        var query = context.SocialLinks.Where(link => link.IsActive);

        if (inHeader.HasValue)
            query = query.Where(link => link.ShowInHeader == inHeader.Value);

        if (inFooter.HasValue)
            query = query.Where(link => link.ShowInFooter == inFooter.Value);

        var links = await query
            .OrderBy(link => link.DisplayOrder)
            .ToListAsync();

        return links.Select(MapSocial).ToList();
    }

    // ------------------------------------------------------------------- admin

    public async Task<List<MenuItemDto>> GetAllAsync()
    {
        var items = await context.MenuItems
            .Include(item => item.Parent)
            .OrderBy(item => item.Location)
            .ThenBy(item => item.DisplayOrder)
            .ToListAsync();

        return items.Select(item =>
        {
            var dto = Map(item);
            dto.ParentTitle = item.Parent?.Title;
            return dto;
        }).ToList();
    }

    public async Task<MenuItemDto?> GetAsync(Guid id)
    {
        var item = await context.MenuItems
            .Include(menuItem => menuItem.Parent)
            .FirstOrDefaultAsync(menuItem => menuItem.Id == id);

        if (item is null)
            return null;

        var dto = Map(item);
        dto.ParentTitle = item.Parent?.Title;
        return dto;
    }

    public async Task<OperationResult> SaveAsync(SaveMenuItemCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Title))
            return OperationResult.Error("عنوان آیتم منو را وارد کنید.");

        if (command.Location == MenuLocation.None)
            return OperationResult.Error("محل نمایش منو را انتخاب کنید.");

        if (command.ParentId == command.Id)
            return OperationResult.Error("آیتم منو نمی‌تواند والد خودش باشد.");

        if (command.ParentId.HasValue)
        {
            var parent = await context.MenuItems.FirstOrDefaultAsync(item => item.Id == command.ParentId.Value);
            if (parent is null)
                return OperationResult.Error("آیتم والد انتخاب‌شده معتبر نیست.");

            if (parent.Location != command.Location)
                return OperationResult.Error("آیتم والد باید در همان محل نمایش باشد.");
        }

        if (command.Id.HasValue)
        {
            var item = await context.MenuItems.AsTracking().FirstOrDefaultAsync(menuItem => menuItem.Id == command.Id.Value);
            if (item is null)
                return OperationResult.NotFound("آیتم منو یافت نشد.");

            item.Location = command.Location;
            item.ParentId = command.ParentId;
            item.Title = command.Title.Trim();
            item.Url = command.Url?.Trim();
            item.Icon = command.Icon;
            item.BadgeText = command.BadgeText;
            item.BadgeColor = command.BadgeColor;
            item.OpenInNewTab = command.OpenInNewTab;
            item.ShowOnMobile = command.ShowOnMobile;
            item.IsHighlighted = command.IsHighlighted;
            item.DisplayOrder = command.DisplayOrder;
            item.IsActive = command.IsActive;

            await context.SaveChangesAsync();
            return OperationResult.Success("آیتم منو ویرایش شد.");
        }

        context.MenuItems.Add(new MenuItem
        {
            Location = command.Location,
            ParentId = command.ParentId,
            Title = command.Title.Trim(),
            Url = command.Url?.Trim(),
            Icon = command.Icon,
            BadgeText = command.BadgeText,
            BadgeColor = command.BadgeColor,
            OpenInNewTab = command.OpenInNewTab,
            ShowOnMobile = command.ShowOnMobile,
            IsHighlighted = command.IsHighlighted,
            DisplayOrder = command.DisplayOrder,
            IsActive = command.IsActive,
        });

        await context.SaveChangesAsync();
        return OperationResult.Success("آیتم منو ثبت شد.");
    }

    public async Task<OperationResult> DeleteAsync(Guid id)
    {
        var item = await context.MenuItems.AsTracking()
            .Include(menuItem => menuItem.Children)
            .FirstOrDefaultAsync(menuItem => menuItem.Id == id);

        if (item is null)
            return OperationResult.NotFound("آیتم منو یافت نشد.");

        if (item.Children.Count > 0)
            context.MenuItems.RemoveRange(item.Children);

        context.MenuItems.Remove(item);
        await context.SaveChangesAsync();

        return OperationResult.Success("آیتم منو (و زیرمنوی آن) حذف شد.");
    }

    public async Task<OperationResult> ToggleActiveAsync(Guid id)
    {
        var item = await context.MenuItems.AsTracking().FirstOrDefaultAsync(menuItem => menuItem.Id == id);
        if (item is null)
            return OperationResult.NotFound("آیتم منو یافت نشد.");

        item.IsActive = !item.IsActive;
        await context.SaveChangesAsync();

        return OperationResult.Success(item.IsActive ? "آیتم منو نمایش داده می‌شود." : "آیتم منو پنهان شد.");
    }

    public async Task<OperationResult> ReorderAsync(Guid id, int displayOrder)
    {
        var item = await context.MenuItems.AsTracking().FirstOrDefaultAsync(menuItem => menuItem.Id == id);
        if (item is null)
            return OperationResult.NotFound("آیتم منو یافت نشد.");

        item.DisplayOrder = displayOrder;
        await context.SaveChangesAsync();

        return OperationResult.Success("ترتیب منو ذخیره شد.");
    }

    public async Task<List<SocialLinkDto>> GetAllSocialLinksAsync()
    {
        var links = await context.SocialLinks
            .OrderBy(link => link.DisplayOrder)
            .ToListAsync();

        return links.Select(MapSocial).ToList();
    }

    public async Task<OperationResult> SaveSocialLinkAsync(SaveSocialLinkCommand command)
    {
        if (command.Platform == SocialPlatform.None)
            return OperationResult.Error("پلتفرم را انتخاب کنید.");

        if (string.IsNullOrWhiteSpace(command.Url))
            return OperationResult.Error("آدرس یا شناسه شبکه اجتماعی را وارد کنید.");

        if (command.Id.HasValue)
        {
            var link = await context.SocialLinks.AsTracking().FirstOrDefaultAsync(item => item.Id == command.Id.Value);
            if (link is null)
                return OperationResult.NotFound("شبکه اجتماعی یافت نشد.");

            link.Platform = command.Platform;
            link.Url = command.Url.Trim();
            link.Title = command.Title;
            link.ShowInHeader = command.ShowInHeader;
            link.ShowInFooter = command.ShowInFooter;
            link.DisplayOrder = command.DisplayOrder;
            link.IsActive = command.IsActive;

            await context.SaveChangesAsync();
            return OperationResult.Success("شبکه اجتماعی ویرایش شد.");
        }

        context.SocialLinks.Add(new SocialLink
        {
            Platform = command.Platform,
            Url = command.Url.Trim(),
            Title = command.Title,
            ShowInHeader = command.ShowInHeader,
            ShowInFooter = command.ShowInFooter,
            DisplayOrder = command.DisplayOrder,
            IsActive = command.IsActive,
        });

        await context.SaveChangesAsync();
        return OperationResult.Success("شبکه اجتماعی ثبت شد.");
    }

    public async Task<OperationResult> DeleteSocialLinkAsync(Guid id)
    {
        var link = await context.SocialLinks.AsTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (link is null)
            return OperationResult.NotFound("شبکه اجتماعی یافت نشد.");

        context.SocialLinks.Remove(link);
        await context.SaveChangesAsync();

        return OperationResult.Success("شبکه اجتماعی حذف شد.");
    }

    public async Task<OperationResult> ToggleSocialLinkAsync(Guid id)
    {
        var link = await context.SocialLinks.AsTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (link is null)
            return OperationResult.NotFound("شبکه اجتماعی یافت نشد.");

        link.IsActive = !link.IsActive;
        await context.SaveChangesAsync();

        return OperationResult.Success(link.IsActive ? "شبکه اجتماعی فعال شد." : "شبکه اجتماعی غیرفعال شد.");
    }

    public async Task EnsureDefaultsAsync(CancellationToken cancellationToken = default)
    {
        if (await context.MenuItems.AnyAsync(cancellationToken))
            return;

        var settings = await settingService.GetStorefrontSettingsAsync();

        context.MenuItems.AddRange(
            new MenuItem { Location = MenuLocation.Header, Title = "صفحه اصلی", Url = "/", DisplayOrder = 1 },
            new MenuItem { Location = MenuLocation.Header, Title = "فروشگاه", Url = "/products", DisplayOrder = 2 },
            new MenuItem { Location = MenuLocation.Header, Title = "دسته‌بندی‌ها", Url = "/categories", DisplayOrder = 3 },
            new MenuItem { Location = MenuLocation.Header, Title = "مقالات", Url = "/blog", DisplayOrder = 4 },
            new MenuItem { Location = MenuLocation.Header, Title = "تماس با ما", Url = "/contact-us", DisplayOrder = 5 },
            new MenuItem { Location = MenuLocation.Header, Title = "تخفیف‌های ویژه", Url = "/products?onlyDiscounted=true", BadgeText = "داغ", BadgeColor = "rose", IsHighlighted = true, DisplayOrder = 6 });

        var quickAccess = new MenuItem
        {
            Location = MenuLocation.Footer,
            Title = "دسترسی سریع",
            DisplayOrder = 1,
        };

        var customerService = new MenuItem
        {
            Location = MenuLocation.Footer,
            Title = "خدمات مشتریان",
            DisplayOrder = 2,
        };

        context.MenuItems.AddRange(quickAccess, customerService);

        context.MenuItems.AddRange(
            new MenuItem { Location = MenuLocation.Footer, Parent = quickAccess, Title = "فروشگاه", Url = "/products", DisplayOrder = 1 },
            new MenuItem { Location = MenuLocation.Footer, Parent = quickAccess, Title = "مقالات", Url = "/blog", DisplayOrder = 2 },
            new MenuItem { Location = MenuLocation.Footer, Parent = quickAccess, Title = "درباره ما", Url = "/about-us", DisplayOrder = 3 });

        context.MenuItems.AddRange(
            new MenuItem { Location = MenuLocation.Footer, Parent = customerService, Title = "پرسش‌های متداول", Url = "/faq", DisplayOrder = 1 },
            new MenuItem { Location = MenuLocation.Footer, Parent = customerService, Title = "قوانین و مقررات", Url = "/terms", DisplayOrder = 2 },
            new MenuItem { Location = MenuLocation.Footer, Parent = customerService, Title = "حریم خصوصی", Url = "/privacy", DisplayOrder = 3 },
            new MenuItem { Location = MenuLocation.Footer, Parent = customerService, Title = "گارانتی و خدمات پس از فروش", Url = "/warranty", DisplayOrder = 4 });

        context.MenuItems.AddRange(
            new MenuItem { Location = MenuLocation.FooterBottom, Title = "قوانین و مقررات", Url = "/terms", DisplayOrder = 1 },
            new MenuItem { Location = MenuLocation.FooterBottom, Title = "حریم خصوصی", Url = "/privacy", DisplayOrder = 2 });

        context.MenuItems.AddRange(
            new MenuItem { Location = MenuLocation.ProfileSidebar, Title = "پیشخوان", Url = "/profile", Icon = "home", DisplayOrder = 1 },
            new MenuItem { Location = MenuLocation.ProfileSidebar, Title = "سفارش‌های من", Url = "/profile/orders", Icon = "bag", DisplayOrder = 2 },
            new MenuItem { Location = MenuLocation.ProfileSidebar, Title = "علاقه‌مندی‌ها", Url = "/profile/favorites", Icon = "heart", DisplayOrder = 3 },
            new MenuItem { Location = MenuLocation.ProfileSidebar, Title = "آدرس‌های من", Url = "/profile/addresses", Icon = "pin", DisplayOrder = 4 },
            new MenuItem { Location = MenuLocation.ProfileSidebar, Title = "پیام‌ها و اعلان‌ها", Url = "/profile/notifications", Icon = "bell", DisplayOrder = 5 },
            new MenuItem { Location = MenuLocation.ProfileSidebar, Title = "اطلاعات شخصی", Url = "/profile/personal-info", Icon = "user", DisplayOrder = 6 });

        if (!string.IsNullOrWhiteSpace(settings.InstagramUrl))
            context.SocialLinks.Add(new SocialLink { Platform = SocialPlatform.Instagram, Url = settings.InstagramUrl, ShowInHeader = true, ShowInFooter = true, DisplayOrder = 1 });

        if (!string.IsNullOrWhiteSpace(settings.TelegramUrl))
            context.SocialLinks.Add(new SocialLink { Platform = SocialPlatform.Telegram, Url = settings.TelegramUrl, ShowInHeader = true, ShowInFooter = true, DisplayOrder = 2 });

        if (!string.IsNullOrWhiteSpace(settings.WhatsappUrl))
            context.SocialLinks.Add(new SocialLink { Platform = SocialPlatform.WhatsApp, Url = settings.WhatsappUrl, ShowInFooter = true, DisplayOrder = 3 });

        await context.SaveChangesAsync(cancellationToken);
    }

    // ---------------------------------------------------------------- internals

    private static MenuItemDto Map(MenuItem item) => new()
    {
        Id = item.Id,
        CreatedAt = item.CreatedAt,
        UpdatedAt = item.UpdatedAt,
        Location = item.Location,
        ParentId = item.ParentId,
        Title = item.Title,
        Url = item.Url,
        Icon = item.Icon,
        BadgeText = item.BadgeText,
        BadgeColor = item.BadgeColor,
        OpenInNewTab = item.OpenInNewTab,
        ShowOnMobile = item.ShowOnMobile,
        IsHighlighted = item.IsHighlighted,
        DisplayOrder = item.DisplayOrder,
        IsActive = item.IsActive,
    };

    private static SocialLinkDto MapSocial(SocialLink link) => new()
    {
        Id = link.Id,
        CreatedAt = link.CreatedAt,
        UpdatedAt = link.UpdatedAt,
        Platform = link.Platform,
        Url = link.Url,
        Title = link.Title,
        ShowInHeader = link.ShowInHeader,
        ShowInFooter = link.ShowInFooter,
        DisplayOrder = link.DisplayOrder,
        IsActive = link.IsActive,
    };

    /// <summary>ساخت درخت دو سطحی (والد و زیرمنو) از فهرست تخت.</summary>
    private static List<MenuItemDto> BuildTree(List<MenuItem> items)
    {
        var roots = items
            .Where(item => item.ParentId is null)
            .Select(Map)
            .ToList();

        foreach (var root in roots)
        {
            root.Children = items
                .Where(item => item.ParentId == root.Id)
                .Select(Map)
                .ToList();
        }

        return roots;
    }
}
