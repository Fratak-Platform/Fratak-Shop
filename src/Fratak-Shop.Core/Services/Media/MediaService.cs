using Fratak_Shop.Core.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Fratak_Shop.Core.Services.Media;

public class MediaService(IOptions<MediaOptions> options) : IMediaService
{
    private readonly MediaOptions _options = options.Value;
    private const string PdfExtension = ".pdf";

    public async Task<OperationResult<string>> SaveImageAsync(IFormFile? file, string folder,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            return OperationResult<string>.Error("فایلی انتخاب نشده است.");

        if (!file.IsValidImageFile())
            return OperationResult<string>.Error("فرمت تصویر مجاز نیست؛ تنها JPG، PNG و BMP پذیرفته می‌شود.");

        return await StoreAsync(file, folder, cancellationToken);
    }

    public async Task<OperationResult<string>> SaveFileAsync(IFormFile? file, string folder,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            return OperationResult<string>.Error("فایلی انتخاب نشده است.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var isPdf = extension == PdfExtension && file.IsValidFile();

        if (!isPdf && !file.IsValidImageFile())
            return OperationResult<string>.Error("فقط تصویر (JPG/PNG) یا فایل PDF مجاز است.");

        return await StoreAsync(file, folder, cancellationToken);
    }

    public async Task<OperationResult<List<StoredFileDto>>> SaveFilesAsync(IEnumerable<IFormFile>? files, string folder,
        CancellationToken cancellationToken = default)
    {
        var uploaded = files?.Where(file => file.Length > 0).ToList() ?? [];
        if (uploaded.Count == 0)
            return OperationResult<List<StoredFileDto>>.Error("حداقل یک فایل باید بارگذاری شود.");

        var saved = new List<StoredFileDto>();
        foreach (var file in uploaded)
        {
            var result = await SaveFileAsync(file, folder, cancellationToken);
            if (result.Status != EnOperationResultStatus.Success)
            {
                foreach (var item in saved)
                    await DeleteAsync(folder, item.FileName);

                return OperationResult<List<StoredFileDto>>.Error(result.Message);
            }

            saved.Add(new StoredFileDto
            {
                FileName = result.Data,
                OriginalName = file.FileName,
                ContentType = file.ContentType,
                SizeBytes = file.Length,
            });
        }

        return OperationResult<List<StoredFileDto>>.Success(saved, "فایل‌ها با موفقیت بارگذاری شدند.");
    }

    public Task<OperationResult> DeleteAsync(string folder, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return Task.FromResult(OperationResult.Success());

        var safeName = Path.GetFileName(fileName);
        var path = GetPhysicalPath(folder, safeName);

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            return Task.FromResult(OperationResult.Error("حذف فایل با خطا مواجه شد."));
        }

        return Task.FromResult(OperationResult.Success("فایل حذف شد."));
    }

    public string GetUrl(string folder, string? fileName) =>
        string.IsNullOrWhiteSpace(fileName)
            ? string.Empty
            : $"{_options.PublicBasePath.TrimEnd('/')}/{folder.Trim('/')}/{fileName}";

    public string GetPhysicalPath(string folder, string fileName) =>
        Path.Combine(GetRootPath(), _options.PublicBasePath.TrimStart('/'), folder, Path.GetFileName(fileName));

    private async Task<OperationResult<string>> StoreAsync(IFormFile file, string folder, CancellationToken cancellationToken)
    {
        if (file.Length > (long)_options.MaxFileSizeMegabytes * 1024 * 1024)
            return OperationResult<string>.Error($"حجم فایل نباید بیشتر از {_options.MaxFileSizeMegabytes} مگابایت باشد.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..30] + extension;

        var directory = Path.Combine(GetRootPath(), _options.PublicBasePath.TrimStart('/'), folder);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, fileName);
        await using var stream = new FileStream(path, FileMode.Create);
        await file.CopyToAsync(stream, cancellationToken);

        return OperationResult<string>.Success(fileName, "فایل با موفقیت ذخیره شد.");
    }

    /// <summary>ریشه ذخیره‌سازی: مقدار تنظیمات یا پوشه wwwroot برنامه.</summary>
    private string GetRootPath() =>
        string.IsNullOrWhiteSpace(_options.RootPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
            : _options.RootPath!;
}
