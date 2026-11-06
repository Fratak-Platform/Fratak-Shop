using System.Security.Cryptography;
using Fratak_Shop.Core.DTOs.Comments.Command;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Web.Services;

/// <summary>
/// Stores review photos under wwwroot/uploads/comments. Every file is checked by its real
/// content and rewritten without metadata (<see cref="ImageSanitizer"/>), gets a random
/// name and one of the fixed extensions .jpg/.png/.webp – the client file name and type
/// are never used.
/// </summary>
public interface IReviewImageStore
{
    public const string WebPath = "/uploads/comments/";

    Task<(List<CommentImageInput> Images, string? Error)> SaveAsync(IReadOnlyList<IFormFile> files, int maxCount, int maxSizeKb,
        CancellationToken cancellationToken = default);

    void Delete(IEnumerable<CommentImageInput> images);
}

public class ReviewImageStore(IWebHostEnvironment environment, ILogger<ReviewImageStore> logger) : IReviewImageStore
{
    private string Folder => Path.Combine(environment.WebRootPath, "uploads", "comments");

    public async Task<(List<CommentImageInput> Images, string? Error)> SaveAsync(IReadOnlyList<IFormFile> files, int maxCount,
        int maxSizeKb, CancellationToken cancellationToken = default)
    {
        var selected = files.Where(f => f.Length > 0).ToList();
        if (selected.Count == 0)
            return ([], null);

        if (selected.Count > maxCount)
            return ([], $"حداکثر {maxCount} تصویر برای هر دیدگاه قابل ارسال است.");

        var maxBytes = (long)Math.Clamp(maxSizeKb, 50, 10_240) * 1024;
        if (selected.Any(f => f.Length > maxBytes))
            return ([], $"حجم هر تصویر حداکثر {maxBytes / 1024:N0} کیلوبایت است.");

        // Check everything first, write afterwards: nothing is left behind for a rejected form.
        var cleaned = new List<SanitizedImage>(selected.Count);
        foreach (var file in selected)
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream((int)file.Length);
            await stream.CopyToAsync(buffer, cancellationToken);

            // دور سئوی خودکار: عکس دیدگاه هم WebP ذخیره می‌شود (حجم کمتر، صفحهٔ محصول سریع‌تر).
            var image = Infrastructure.Media.WebPEncoder.FromUpload(buffer.ToArray(), out var error, logger);
            if (image is null)
                return ([], error ?? "فایل تصویر معتبر نیست.");

            cleaned.Add(image);
        }

        Directory.CreateDirectory(Folder);
        var saved = new List<CommentImageInput>(cleaned.Count);
        try
        {
            foreach (var image in cleaned)
            {
                var name = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant() + image.Extension;
                await File.WriteAllBytesAsync(Path.Combine(Folder, name), image.Data, cancellationToken);
                saved.Add(new CommentImageInput(name, image.ContentType, image.Data.Length));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Saving review images failed");
            Delete(saved);
            return ([], "ذخیره تصویر انجام نشد. لطفاً دوباره تلاش کنید.");
        }

        return (saved, null);
    }

    public void Delete(IEnumerable<CommentImageInput> images)
    {
        foreach (var image in images)
        {
            var name = Path.GetFileName(image.FileName);
            if (string.IsNullOrEmpty(name) || name != image.FileName)
                continue;

            try
            {
                File.Delete(Path.Combine(Folder, name));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Review image {File} could not be deleted", name);
            }
        }
    }
}
