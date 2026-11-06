using Fratak_Shop.Core.DTOs.Contents;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Contents;
using Fratak_Shop.Data.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Contents;

public class SiteContentService(FratakShopDbContext context, IMemoryCache cache) : ISiteContentService
{
    // ── Home sections ──

    public async Task<List<HomeSectionDto>> GetActiveHomeSectionsAsync(CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(CacheKeys.HomeSections, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheKeys.DefaultLifetime;
            return await QueryHomeSections(context.HomeSections.Where(s => s.IsActive)).ToListAsync(cancellationToken);
        }) ?? [];
    }

    public async Task<List<HomeSectionDto>> GetAllHomeSectionsAsync()
    {
        return await QueryHomeSections(context.HomeSections).ToListAsync();
    }

    public async Task<OperationResult<Guid>> SaveHomeSectionAsync(HomeSectionDto command)
    {
        if (command.Type == HomeSectionType.None || !Enum.IsDefined(command.Type))
            return OperationResult<Guid>.Error("نوع بخش معتبر نیست.");

        if (command.ItemCount is < 1 or > 50)
            return OperationResult<Guid>.Error("تعداد آیتم‌ها باید بین ۱ تا ۵۰ باشد.");

        if (command.Type == HomeSectionType.CategoryProducts && command.CategoryId is null)
            return OperationResult<Guid>.Error("برای این بخش یک دسته‌بندی انتخاب کنید.");

        if (command.CategoryId is { } categoryId && !await context.Categories.AnyAsync(c => c.Id == categoryId))
            return OperationResult<Guid>.Error("دسته‌بندی انتخاب‌شده معتبر نیست.");

        var section = await FindOrCreate(context.HomeSections, command.Id);
        if (section is null)
            return OperationResult<Guid>.NotFound();

        section.Type = command.Type;
        section.Title = Clean(command.Title);
        section.Subtitle = Clean(command.Subtitle);
        section.LinkUrl = CleanUrl(command.LinkUrl);
        section.ItemCount = command.ItemCount;
        section.CategoryId = command.CategoryId;
        section.IsActive = command.IsActive;
        section.DisplayOrder = command.DisplayOrder;

        await context.SaveChangesAsync();
        cache.Remove(CacheKeys.HomeSections);
        return OperationResult<Guid>.Success(section.Id, "بخش صفحه اصلی ذخیره شد.");
    }

    public Task<OperationResult> DeleteHomeSectionAsync(Guid id) =>
        DeleteAsync(context.HomeSections, id, CacheKeys.HomeSections);

    private static IQueryable<HomeSectionDto> QueryHomeSections(IQueryable<HomeSection> query) =>
        query
            .OrderBy(s => s.DisplayOrder)
            .Select(s => new HomeSectionDto
            {
                Id = s.Id,
                Type = s.Type,
                Title = s.Title,
                Subtitle = s.Subtitle,
                LinkUrl = s.LinkUrl,
                ItemCount = s.ItemCount,
                CategoryId = s.CategoryId,
                CategorySlug = s.Category != null ? s.Category.Slug : null,
                CategoryTitle = s.Category != null ? s.Category.Name : null,
                IsActive = s.IsActive,
                DisplayOrder = s.DisplayOrder,
            });

    // ── Features ──

    public async Task<List<SiteFeatureDto>> GetActiveFeaturesAsync(CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(CacheKeys.SiteFeatures, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheKeys.DefaultLifetime;
            return await QueryFeatures(context.SiteFeatures.Where(f => f.IsActive)).ToListAsync(cancellationToken);
        }) ?? [];
    }

    public async Task<List<SiteFeatureDto>> GetAllFeaturesAsync()
    {
        return await QueryFeatures(context.SiteFeatures).ToListAsync();
    }

    public async Task<OperationResult<Guid>> SaveFeatureAsync(SiteFeatureDto command)
    {
        if (string.IsNullOrWhiteSpace(command.Title))
            return OperationResult<Guid>.Error("عنوان را وارد کنید.");

        var feature = await FindOrCreate(context.SiteFeatures, command.Id);
        if (feature is null)
            return OperationResult<Guid>.NotFound();

        feature.Title = Clean(command.Title)!;
        feature.Description = Clean(command.Description);
        feature.IconKey = Clean(command.IconKey);
        feature.LinkUrl = CleanUrl(command.LinkUrl);
        feature.IsActive = command.IsActive;
        feature.DisplayOrder = command.DisplayOrder;

        await context.SaveChangesAsync();
        cache.Remove(CacheKeys.SiteFeatures);
        return OperationResult<Guid>.Success(feature.Id, "ذخیره شد.");
    }

    public Task<OperationResult> DeleteFeatureAsync(Guid id) =>
        DeleteAsync(context.SiteFeatures, id, CacheKeys.SiteFeatures);

    private static IQueryable<SiteFeatureDto> QueryFeatures(IQueryable<SiteFeature> query) =>
        query
            .OrderBy(f => f.DisplayOrder)
            .Select(f => new SiteFeatureDto
            {
                Id = f.Id,
                Title = f.Title,
                Description = f.Description,
                IconKey = f.IconKey,
                LinkUrl = f.LinkUrl,
                IsActive = f.IsActive,
                DisplayOrder = f.DisplayOrder,
            });

    // ── Trust badges ──

    public async Task<List<TrustBadgeDto>> GetActiveTrustBadgesAsync(CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(CacheKeys.TrustBadges, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheKeys.DefaultLifetime;
            return await QueryBadges(context.TrustBadges.Where(b => b.IsActive)).ToListAsync(cancellationToken);
        }) ?? [];
    }

    public async Task<List<TrustBadgeDto>> GetAllTrustBadgesAsync()
    {
        return await QueryBadges(context.TrustBadges).ToListAsync();
    }

    public async Task<OperationResult<Guid>> SaveTrustBadgeAsync(TrustBadgeDto command)
    {
        if (string.IsNullOrWhiteSpace(command.Title) || string.IsNullOrWhiteSpace(command.ImageUrl))
            return OperationResult<Guid>.Error("عنوان و تصویر نماد الزامی است.");

        var badge = await FindOrCreate(context.TrustBadges, command.Id);
        if (badge is null)
            return OperationResult<Guid>.NotFound();

        badge.Title = Clean(command.Title)!;
        badge.ImageUrl = CleanUrl(command.ImageUrl)!;
        badge.LinkUrl = CleanUrl(command.LinkUrl);
        badge.IsActive = command.IsActive;
        badge.DisplayOrder = command.DisplayOrder;

        await context.SaveChangesAsync();
        cache.Remove(CacheKeys.TrustBadges);
        return OperationResult<Guid>.Success(badge.Id, "ذخیره شد.");
    }

    public Task<OperationResult> DeleteTrustBadgeAsync(Guid id) =>
        DeleteAsync(context.TrustBadges, id, CacheKeys.TrustBadges);

    private static IQueryable<TrustBadgeDto> QueryBadges(IQueryable<TrustBadge> query) =>
        query
            .OrderBy(b => b.DisplayOrder)
            .Select(b => new TrustBadgeDto
            {
                Id = b.Id,
                Title = b.Title,
                ImageUrl = b.ImageUrl,
                LinkUrl = b.LinkUrl,
                IsActive = b.IsActive,
                DisplayOrder = b.DisplayOrder,
            });

    // ── helpers ──

    private static async Task<T?> FindOrCreate<T>(DbSet<T> set, Guid id) where T : BaseEntity, new()
    {
        if (id != Guid.Empty)
            return await set.AsTracking().FirstOrDefaultAsync(x => x.Id == id);

        var entity = new T();
        set.Add(entity);
        return entity;
    }

    private async Task<OperationResult> DeleteAsync<T>(DbSet<T> set, Guid id, string cacheKey) where T : BaseEntity
    {
        var entity = await set.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null)
            return OperationResult.NotFound();

        set.Remove(entity);
        await context.SaveChangesAsync();
        cache.Remove(cacheKey);
        return OperationResult.Success("حذف شد.");
    }

    private static string? Clean(string? value) =>
        value.ToPlainText() is { Length: > 0 } text ? text : null;

    /// <summary>Only relative paths and http(s) links are stored (no javascript:/data: urls).</summary>
    private static string? CleanUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var url = value.Trim();
        if (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal))
            return url;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.ToString()
            : null;
    }
}
