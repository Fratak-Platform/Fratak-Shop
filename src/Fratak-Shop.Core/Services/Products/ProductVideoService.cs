using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Products;

/// <summary>Product videos: uploaded files, Aparat videos or direct mp4/webm links (validated on read and write).</summary>
public interface IProductVideoService
{
    Task<List<ProductVideoDto>> GetForProductAsync(Guid productId);

    /// <summary>Admin panel: adds a video after validating the source.</summary>
    Task<OperationResult> AddAsync(Guid productId, ProductVideoProvider provider, string source, string? title, string? posterImage, int? durationSeconds);

    Task<OperationResult> DeleteAsync(Guid videoId);
}

public sealed class ProductVideoDto
{
    public Guid Id { get; init; }
    public string? Title { get; init; }
    public ProductVideoProvider Provider { get; init; }
    public string? EmbedUrl { get; init; }
    public string? FileUrl { get; init; }
    public string? MimeType { get; init; }
    public string PageUrl { get; init; } = null!;
    public string? PosterImage { get; init; }
    public int? DurationSeconds { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public class ProductVideoService(FratakShopDbContext context, IStoreSettingService settingService) : IProductVideoService
{
    public async Task<List<ProductVideoDto>> GetForProductAsync(Guid productId)
    {
        if (!(await settingService.GetAsync()).IsProductVideoEnabled)
            return [];

        var rows = await context.ProductVideos
            .Where(v => v.ProductId == productId && v.IsActive)
            .OrderBy(v => v.DisplayOrder).ThenBy(v => v.CreatedAt)
            .Take(10)
            .ToListAsync();

        return rows
            .Select(v => (Video: v, Resolved: VideoSource.Resolve(v.Provider, v.Source)))
            .Where(x => x.Resolved is not null)
            .Select(x => new ProductVideoDto
            {
                Id = x.Video.Id,
                Title = x.Video.Title,
                Provider = x.Video.Provider,
                EmbedUrl = x.Resolved!.EmbedUrl,
                FileUrl = x.Resolved.FileUrl,
                MimeType = x.Resolved.MimeType,
                PageUrl = x.Resolved.PageUrl,
                PosterImage = x.Video.PosterImage,
                DurationSeconds = x.Video.DurationSeconds,
                CreatedAt = x.Video.CreatedAt,
            })
            .ToList();
    }

    public async Task<OperationResult> AddAsync(Guid productId, ProductVideoProvider provider, string source, string? title, string? posterImage, int? durationSeconds)
    {
        var normalized = VideoSource.Normalize(provider, source);
        if (normalized is null)
            return OperationResult.Error("آدرس ویدیو معتبر نیست (فایل mp4/webm، لینک https مستقیم یا شناسه آپارات).");

        if (!await context.Products.AnyAsync(p => p.Id == productId))
            return OperationResult.NotFound("محصول یافت نشد.");

        var order = await context.ProductVideos.Where(v => v.ProductId == productId).Select(v => (int?)v.DisplayOrder).MaxAsync() ?? 0;
        context.ProductVideos.Add(new ProductVideo
        {
            ProductId = productId,
            Provider = provider,
            Source = normalized,
            Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
            PosterImage = string.IsNullOrWhiteSpace(posterImage) ? null : posterImage.Trim(),
            DurationSeconds = durationSeconds is > 0 and < 86400 ? durationSeconds : null,
            DisplayOrder = order + 1,
        });
        await context.SaveChangesAsync();
        return OperationResult.Success("ویدیو اضافه شد.");
    }

    public async Task<OperationResult> DeleteAsync(Guid videoId)
    {
        var video = await context.ProductVideos.AsTracking().FirstOrDefaultAsync(v => v.Id == videoId);
        if (video is null)
            return OperationResult.NotFound("ویدیو یافت نشد.");

        context.ProductVideos.Remove(video);
        await context.SaveChangesAsync();
        return OperationResult.Success("ویدیو حذف شد.");
    }
}
