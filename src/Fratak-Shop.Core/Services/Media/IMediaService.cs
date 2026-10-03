using Fratak_Shop.Core.Utilities;
using Microsoft.AspNetCore.Http;

namespace Fratak_Shop.Core.Services.Media;

/// <summary>
/// Central file storage of the shop. The web layer never writes a file itself:
/// it hands the uploaded form file to this service, which validates it, stores it
/// under a safe random name and returns a reference that is saved in the database.
/// </summary>
public interface IMediaService
{
    /// <summary>ذخیره تصویر با اعتبارسنجی نوع و ابعاد.</summary>
    Task<OperationResult<string>> SaveImageAsync(IFormFile? file, string folder, CancellationToken cancellationToken = default);

    /// <summary>ذخیره فایل عمومی (تصویر یا PDF)؛ برای فیش‌های کارت به کارت.</summary>
    Task<OperationResult<string>> SaveFileAsync(IFormFile? file, string folder, CancellationToken cancellationToken = default);

    /// <summary>ذخیره چند فایل پشت‌سرهم (چند فیش برای یک واریز).</summary>
    Task<OperationResult<List<StoredFileDto>>> SaveFilesAsync(IEnumerable<IFormFile>? files, string folder,
        CancellationToken cancellationToken = default);

    Task<OperationResult> DeleteAsync(string folder, string? fileName);

    /// <summary>تبدیل نام فایل ذخیره‌شده به آدرس عمومی.</summary>
    string GetUrl(string folder, string? fileName);

    /// <summary>مسیر فیزیکی فایل روی سرور.</summary>
    string GetPhysicalPath(string folder, string fileName);
}

/// <summary>مشخصات فایل ذخیره‌شده که در دیتابیس ثبت می‌شود.</summary>
public class StoredFileDto
{
    public string FileName { get; set; } = null!;
    public string? OriginalName { get; set; }
    public string? ContentType { get; set; }
    public long SizeBytes { get; set; }
}
