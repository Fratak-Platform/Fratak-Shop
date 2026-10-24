using Fratak_Shop.Core.DTOs.Contents;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Contents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Contents;

public class ContentPageService(FratakShopDbContext context, IMemoryCache cache) : IContentPageService
{
    private static string PageCacheKey(string slug) => $"core:content-page:{slug}";

    public async Task<ContentPageDto?> GetActiveBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var normalized = slug.ToSlug();
        if (normalized.Length == 0)
            return null;

        var key = PageCacheKey(normalized);
        if (cache.TryGetValue<ContentPageDto>(key, out var cached) && cached is not null)
            return cached;

        var page = await context.ContentPages
            .Where(p => p.IsActive && p.Slug == normalized)
            .FirstOrDefaultAsync(cancellationToken);

        if (page is null)
            return null;

        var dto = Map(page);
        cache.Set(key, dto, CacheKeys.DefaultLifetime);
        return dto;
    }

    public async Task<List<ContentPageLinkDto>> GetActiveLinksAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue<List<ContentPageLinkDto>>(CacheKeys.FooterPages, out var cached) && cached is not null)
            return cached;

        var links = await context.ContentPages
            .Where(p => p.IsActive)
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.Title)
            .Select(p => new ContentPageLinkDto
            {
                Slug = p.Slug,
                Title = p.Title,
                ShowInFooter = p.ShowInFooter,
                UpdatedAt = p.UpdatedAt,
                NoIndex = p.Seo != null && p.Seo.NoIndex == true,
            })
            .ToListAsync(cancellationToken);

        cache.Set(CacheKeys.FooterPages, links, CacheKeys.DefaultLifetime);
        return links;
    }

    public async Task<List<ContentPageDto>> GetAllAsync()
    {
        var pages = await context.ContentPages.OrderBy(p => p.DisplayOrder).ToListAsync();
        return pages.Select(Map).ToList();
    }

    public async Task<ContentPageDto?> GetByIdAsync(Guid id)
    {
        var page = await context.ContentPages.FirstOrDefaultAsync(p => p.Id == id);
        return page is null ? null : Map(page);
    }

    public async Task<OperationResult<Guid>> SaveAsync(ContentPageCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Title))
            return OperationResult<Guid>.Error("عنوان صفحه را وارد کنید.");

        var slug = (string.IsNullOrWhiteSpace(command.Slug) ? command.Title : command.Slug).ToSlug();
        if (slug.Length == 0)
            return OperationResult<Guid>.Error("نامک صفحه معتبر نیست.");

        if (await context.ContentPages.AnyAsync(p => p.Slug == slug && p.Id != command.Id))
            return OperationResult<Guid>.Error("نامک صفحه تکراری است.");

        ContentPage? page;
        string? oldSlug = null;
        if (command.Id == Guid.Empty)
        {
            page = new ContentPage();
            context.ContentPages.Add(page);
        }
        else
        {
            page = await context.ContentPages.AsTracking().FirstOrDefaultAsync(p => p.Id == command.Id);
            if (page is null)
                return OperationResult<Guid>.NotFound();

            oldSlug = page.Slug;
        }

        page.Slug = slug;
        page.Title = command.Title.Trim().ToPlainText();
        page.Summary = command.Summary.ToPlainText(1000) is { Length: > 0 } summary ? summary : null;
        page.Content = command.Content.SanitizeOrNull();
        page.ImageUrl = string.IsNullOrWhiteSpace(command.ImageUrl) ? null : command.ImageUrl.Trim();
        page.PageKey = command.PageKey;
        page.IsActive = command.IsActive;
        page.ShowInFooter = command.ShowInFooter;
        page.DisplayOrder = command.DisplayOrder;
        page.Seo = command.Seo;

        await context.SaveChangesAsync();
        Invalidate(slug, oldSlug);

        return OperationResult<Guid>.Success(page.Id, "صفحه ذخیره شد.");
    }

    public async Task<OperationResult> DeleteAsync(Guid id)
    {
        var page = await context.ContentPages.AsTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (page is null)
            return OperationResult.NotFound();

        context.ContentPages.Remove(page);
        await context.SaveChangesAsync();
        Invalidate(page.Slug, null);

        return OperationResult.Success("صفحه حذف شد.");
    }

    private void Invalidate(string slug, string? oldSlug)
    {
        cache.Remove(CacheKeys.FooterPages);
        cache.Remove(PageCacheKey(slug));
        if (oldSlug is not null)
            cache.Remove(PageCacheKey(oldSlug));
    }

    private static ContentPageDto Map(ContentPage page) => new()
    {
        Id = page.Id,
        CreatedAt = page.CreatedAt,
        UpdatedAt = page.UpdatedAt,
        Slug = page.Slug,
        Title = page.Title,
        Summary = page.Summary,
        Content = page.Content,
        ImageUrl = page.ImageUrl,
        PageKey = page.PageKey,
        IsActive = page.IsActive,
        ShowInFooter = page.ShowInFooter,
        DisplayOrder = page.DisplayOrder,
        Seo = page.Seo,
    };
}
