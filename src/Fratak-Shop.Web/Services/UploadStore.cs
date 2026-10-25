using System.Security.Cryptography;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Web.Services;

/// <summary>فایل ذخیره‌شده؛ برای تصویرها ابعاد هم پر می‌شود (کتابخانهٔ رسانه).</summary>
public sealed record StoredFile(string FileName, string ContentType, int SizeBytes, int? Width = null, int? Height = null);

/// <summary>Where an upload area lives: public (wwwroot/uploads/…) or private (App_Data/…, served by an authorized handler).</summary>
public sealed record UploadArea(string Name, bool IsPublic, bool AllowPdf)
{
    /// <summary>Return-request photos: random names under /uploads/returns (not indexed, see robots.txt).</summary>
    public static readonly UploadArea Returns = new("returns", IsPublic: true, AllowPdf: false);

    /// <summary>Ticket attachments: private, only the ticket owner (and staff later) can download them.</summary>
    public static readonly UploadArea Tickets = new("tickets", IsPublic: false, AllowPdf: true);

    /// <summary>Supplier purchase invoices of orders: private (App_Data), staff with the permission only – never customers.</summary>
    public static readonly UploadArea PurchaseInvoices = new("purchase-invoices", IsPublic: false, AllowPdf: true);

    // Admin panel images (public, re-encoded without metadata, random names).
    public static readonly UploadArea Products = new("products", IsPublic: true, AllowPdf: false);
    public static readonly UploadArea Categories = new("categories", IsPublic: true, AllowPdf: false);
    public static readonly UploadArea Brands = new("brands", IsPublic: true, AllowPdf: false);
    public static readonly UploadArea Blogs = new("blogs", IsPublic: true, AllowPdf: false);
    public static readonly UploadArea Banners = new("banners", IsPublic: true, AllowPdf: false);
    public static readonly UploadArea Content = new("content", IsPublic: true, AllowPdf: false);

    /// <summary>کتابخانهٔ رسانهٔ پنل (/uploads/media): تصویرهای داخل متن محصول، مقاله و صفحات.</summary>
    public static readonly UploadArea Media = new("media", IsPublic: true, AllowPdf: false);
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

    /// <summary>
    /// کپی فایل‌ها با نام تصادفی تازه (مرحلهٔ هفتم – «کپی محصول»). خروجی: نام قدیم → نام تازه؛
    /// فایلی که نامش نامعتبر است یا روی دیسک نیست در خروجی نمی‌آید.
    /// <para>چرا کپی؟ چون حذف تصویر از یک محصول فایل را از دیسک پاک می‌کند و نباید عکس محصول دیگر خراب شود.</para>
    /// مثال: <c>Duplicate(UploadArea.Products, ["a1.webp"])</c> ← <c>{ "a1.webp": "9f3c….webp" }</c>.
    /// </summary>
    Dictionary<string, string> Duplicate(UploadArea area, IEnumerable<string> fileNames);
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

        var prepared = new List<(byte[] Data, string Extension, string ContentType, int? Width, int? Height)>(selected.Count);
        foreach (var file in selected)
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream((int)file.Length);
            await stream.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();

            if (area.AllowPdf && bytes.AsSpan().StartsWith(PdfSignature))
            {
                prepared.Add((bytes, ".pdf", "application/pdf", null, null));
                continue;
            }

            var image = ImageSanitizer.Sanitize(bytes, out var error);
            if (image is null)
                return ([], area.AllowPdf ? "فقط تصویر (JPG، PNG، WebP) یا فایل PDF قابل ارسال است." : error ?? "فایل تصویر معتبر نیست.");

            prepared.Add((image.Data, image.Extension, image.ContentType, image.Width, image.Height));
        }

        var folder = Folder(area);
        Directory.CreateDirectory(folder);
        var saved = new List<StoredFile>(prepared.Count);
        try
        {
            foreach (var (data, extension, contentType, width, height) in prepared)
            {
                var name = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant() + extension;
                await File.WriteAllBytesAsync(Path.Combine(folder, name), data, cancellationToken);
                saved.Add(new StoredFile(name, contentType, data.Length, width, height));
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

    public Dictionary<string, string> Duplicate(UploadArea area, IEnumerable<string> fileNames)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var folder = Folder(area);
        foreach (var fileName in fileNames.Distinct(StringComparer.Ordinal))
        {
            // همان بررسی GetPath: فقط نام سادهٔ فایل (بدون مسیر) پذیرفته می‌شود.
            var source = GetPath(area, fileName);
            if (source is null)
                continue;

            var name = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant() + Path.GetExtension(fileName).ToLowerInvariant();
            try
            {
                File.Copy(source, Path.Combine(folder, name), overwrite: false);
                map[fileName] = name;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Upload {File} could not be duplicated", fileName);
            }
        }

        return map;
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
