using Fratak_Shop.Core.DTOs.Contents;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Contents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Contents;

public class MenuDto
{
    public Guid Id { get; set; }
    public string Key { get; set; } = null!;
    public string Title { get; set; } = null!;
    public MenuLocation Location { get; set; }
    public MenuSource Source { get; set; }
    public int DisplayOrder { get; set; }
    public List<MenuItemDto> Items { get; set; } = [];
}

public class MenuItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = null!;
    public string? Url { get; set; }
    public MenuItemType Type { get; set; }
    public string? Icon { get; set; }
    public string? BadgeText { get; set; }
    public bool OpenInNewTab { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public int DisplayOrder { get; set; }
    public List<MenuItemDto> Children { get; set; } = [];

    public bool IsVisible(DateTimeOffset now) => (StartsAt is null || StartsAt <= now) && (EndsAt is null || EndsAt > now);
}

/// <summary>
/// Header / footer navigation from the database (cached). Menus with the
/// <see cref="MenuSource.ContentPages"/> source list the content pages marked
/// "show in footer" automatically, followed by their manual items.
/// </summary>
public interface IMenuService
{
    /// <summary>Active menus with their currently visible items.</summary>
    Task<List<MenuDto>> GetActiveMenusAsync(CancellationToken cancellationToken = default);

    /// <summary>Call after changing menus or menu items in the admin panel.</summary>
    void InvalidateCache();
}

public class MenuService(FratakShopDbContext context, IContentPageService contentPages, IMemoryCache cache) : IMenuService
{
    public async Task<List<MenuDto>> GetActiveMenusAsync(CancellationToken cancellationToken = default)
    {
        var menus = await cache.GetOrCreateAsync(CacheKeys.Menus, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheKeys.DefaultLifetime;
            return await LoadAsync(cancellationToken);
        }) ?? [];

        // Content pages come from their own (invalidated) cache, so page edits show up at once.
        List<ContentPageLinkDto> pages = menus.Any(m => m.Source == MenuSource.ContentPages)
            ? await contentPages.GetActiveLinksAsync(cancellationToken)
            : [];

        // Scheduled items are filtered per request (the cache may live across the boundary).
        var now = DateTimeOffset.UtcNow;
        return menus.Select(menu => new MenuDto
        {
            Id = menu.Id,
            Key = menu.Key,
            Title = menu.Title,
            Location = menu.Location,
            Source = menu.Source,
            DisplayOrder = menu.DisplayOrder,
            Items = (menu.Source == MenuSource.ContentPages ? PageItems(pages) : []).Concat(menu.Items.Where(i => i.IsVisible(now)).Select(i => new MenuItemDto
            {
                Id = i.Id,
                Title = i.Title,
                Url = i.Url,
                Type = i.Type,
                Icon = i.Icon,
                BadgeText = i.BadgeText,
                OpenInNewTab = i.OpenInNewTab,
                DisplayOrder = i.DisplayOrder,
                Children = i.Children.Where(c => c.IsVisible(now)).ToList(),
            })).ToList(),
        }).ToList();
    }

    public void InvalidateCache() => cache.Remove(CacheKeys.Menus);

    private async Task<List<MenuDto>> LoadAsync(CancellationToken cancellationToken)
    {
        var menus = await context.Menus
            .Where(m => m.IsActive)
            .OrderBy(m => m.Location).ThenBy(m => m.DisplayOrder)
            .Select(m => new MenuDto
            {
                Id = m.Id,
                Key = m.Key,
                Title = m.Title,
                Location = m.Location,
                Source = m.Source,
                DisplayOrder = m.DisplayOrder,
            })
            .ToListAsync(cancellationToken);

        if (menus.Count == 0)
            return menus;

        var menuIds = menus.Select(m => m.Id).ToList();
        var items = await context.MenuItems
            .Where(i => menuIds.Contains(i.MenuId) && i.IsActive)
            .OrderBy(i => i.DisplayOrder)
            .Select(i => new
            {
                i.MenuId,
                i.ParentId,
                Dto = new MenuItemDto
                {
                    Id = i.Id,
                    Title = i.Title,
                    Url = i.Url,
                    Type = i.Type,
                    Icon = i.Icon,
                    BadgeText = i.BadgeText,
                    OpenInNewTab = i.OpenInNewTab,
                    StartsAt = i.StartsAt,
                    EndsAt = i.EndsAt,
                    DisplayOrder = i.DisplayOrder,
                },
            })
            .ToListAsync(cancellationToken);

        var children = items.Where(i => i.ParentId.HasValue).ToLookup(i => i.ParentId!.Value, i => i.Dto);

        foreach (var menu in menus)
        {
            var roots = items.Where(i => i.MenuId == menu.Id && i.ParentId == null).Select(i => i.Dto).ToList();
            foreach (var root in roots)
                root.Children = children[root.Id].ToList();

            menu.Items = roots;
        }

        return menus;
    }

    private static List<MenuItemDto> PageItems(List<ContentPageLinkDto> pages) =>
        pages.Where(p => p.ShowInFooter).Select(p => new MenuItemDto
        {
            Title = p.Title,
            Url = $"/page/{p.Slug}",
            Type = MenuItemType.Link,
        }).ToList();
}
