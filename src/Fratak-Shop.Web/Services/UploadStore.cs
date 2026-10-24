using System.Security.Cryptography;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Web.Services;

public sealed record StoredFile(string FileName, string ContentType, int SizeBytes);

/// <summary>Where an upload area lives: public (wwwroot/uploads/…) or private (App_Data/…, served by an authorized handler).</summary>
public sealed record UploadArea(string Name, bool IsPublic, bool AllowPdf)
{
    /// <summary>Return-request photos: random names under /uploads/returns (not indexed, see robots.txt).</summary>
    public static readonly UploadArea Returns = new("returns", IsPublic: true, AllowPdf: false);

    /// <summary>Ticket attachments: private, only the ticket owner (and staff later) can download them.</summary>
    public static readonly UploadArea Tickets = new("tickets", IsPublic: false, AllowPdf: true);
}

/// <summary>
/// Customer uploads outside reviews (returns, tickets). Images are checked by their real content
/// and re-encoded without metadata (<see cref="ImageSanitizer"/>); PDFs (tickets only) must start
/// with the PDF signature. Files get random names and fixed extensions – client names and types are
/// never trusted. Everything is validated before anything is written.
/// </summary>
public interface IUploadStore
{
    Task<(List<StoredFile> Files, string? Error)> SaveAsync(UploadArea area, IReadOnlyList<IFormFile> files, int maxCount, int maxSizeKb,
        CancellationToken cancellationToken = default);

    /// <summary>Physical path of a stored private file (null when the name is invalid or the file is missing).</summary>
    string? GetPath(UploadArea area, string fileName);

    void Delete(UploadArea area, IEnumerable<string> fileNames);
}

public sealed class UploadStore(IWebHostEnvironment environment, ILogger<UploadStore> logger) : IUploadStore
{
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    private string Folder(UploadArea area) => area.IsPublic
        ? Path.Combine(environment.WebRootPath, "uploads", area.Name)
        : Path.Combine(environment.ContentRootPath, "App_Data", area.Name);

    public async Task<(List<StoredFile> Files, string? Error)> SaveAsync(UploadArea area, IReadOnlyList<IFormFile> files, int maxCount, int maxSizeKb,
        CancellationToken cancellationToken = default)
    {
        var selected = files.Where(f => f.Length > 0).ToList();
        if (selected.Count == 0)
            return ([], null);

        if (selected.Count > maxCount)
            return ([], $"حداکثر {maxCount} فایل قابل ارسال است.");

        var maxBytes = (long)Math.Clamp(maxSizeKb, 50, 10_240) * 1024;
        if (selected.Any(f => f.Length > maxBytes))
            return ([], $"حجم هر فایل حداکثر {maxBytes / 1024:N0} کیلوبایت است.");

        var prepared = new List<(byte[] Data, string Extension, string ContentType)>(selected.Count);
        foreach (var file in selected)
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream((int)file.Length);
            await stream.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();

            if (area.AllowPdf && bytes.AsSpan().StartsWith(PdfSignature))
            {
                prepared.Add((bytes, ".pdf", "application/pdf"));
                continue;
            }

            var image = ImageSanitizer.Sanitize(bytes, out var error);
            if (image is null)
                return ([], area.AllowPdf ? "فقط تصویر (JPG، PNG، WebP) یا فایل PDF قابل ارسال است." : error ?? "فایل تصویر معتبر نیست.");

            prepared.Add((image.Data, image.Extension, image.ContentType));
        }

        var folder = Folder(area);
        Directory.CreateDirectory(folder);
        var saved = new List<StoredFile>(prepared.Count);
        try
        {
            foreach (var (data, extension, contentType) in prepared)
            {
                var name = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant() + extension;
                await File.WriteAllBytesAsync(Path.Combine(folder, name), data, cancellationToken);
                saved.Add(new StoredFile(name, contentType, data.Length));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Saving uploads of {Area} failed", area.Name);
            Delete(area, saved.Select(f => f.FileName));
            return ([], "ذخیره فایل انجام نشد. لطفاً دوباره تلاش کنید.");
        }

        return (saved, null);
    }

    public string? GetPath(UploadArea area, string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrEmpty(name) || name != fileName || name.Length > 100)
            return null;

        var path = Path.Combine(Folder(area), name);
        return File.Exists(path) ? path : null;
    }

    public void Delete(UploadArea area, IEnumerable<string> fileNames)
    {
        foreach (var fileName in fileNames)
        {
            var name = Path.GetFileName(fileName);
            if (string.IsNullOrEmpty(name) || name != fileName)
                continue;

            try
            {
                File.Delete(Path.Combine(Folder(area), name));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Upload {File} could not be deleted", name);
            }
        }
    }
}
