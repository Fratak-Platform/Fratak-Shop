using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Media;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Admin;

/// <summary>یک فایل کتابخانهٔ رسانه برای نمایش در شبکهٔ تصاویر یا انتخابگر ویرایشگر.</summary>
public sealed record MediaFileDto(Guid Id, string Url, string FileName, string? OriginalName, string? AltText, int SizeBytes, int? Width, int? Height, DateTimeOffset CreatedAt);

/// <summary>اطلاعات فایلی که لایهٔ وب روی دیسک ذخیره کرده و باید در کتابخانه ثبت شود.</summary>
public sealed record AddMediaCommand(string FileName, string Url, string? OriginalName, string ContentType, int SizeBytes, int? Width, int? Height, Guid? UploadedByUserId);

/// <summary>
/// ردیف‌های «کتابخانهٔ رسانه» (فقط دیتابیس؛ ذخیره و حذف فایل روی دیسک کار <c>IUploadStore</c> در لایهٔ وب است).
/// <para>
/// جریان بارگذاری: صفحهٔ <c>/admin/media</c> فایل را با <c>UploadArea.Media</c> ذخیره می‌کند (تصویر بازسازی و متادیتا حذف می‌شود)،
/// سپس <see cref="AddAsync"/> ردیفش را می‌سازد. هنگام حذف، <see cref="DeleteAsync"/> نام فایل را برمی‌گرداند تا از دیسک هم پاک شود.
/// </para>
/// </summary>
public interface IMediaLibraryService
{
    /// <summary>فهرست صفحه‌بندی‌شده با جست‌وجو در نام اصلی و متن جایگزین.</summary>
    Task<PagedList<MediaFileDto>> ListAsync(string? query, int page, int take);

    Task<List<MediaFileDto>> AddAsync(IEnumerable<AddMediaCommand> files);

    Task<OperationResult> UpdateAltAsync(Guid id, string? altText);

    /// <summary>حذف ردیف؛ خروجی نام فایل روی دیسک (یا null اگر پیدا نشد).</summary>
    Task<string?> DeleteAsync(Guid id);

    /// <summary>جمع حجم و تعداد فایل‌ها (برای نمایش در بالای صفحه).</summary>
    Task<(int Count, long TotalBytes)> StatsAsync();
}

public sealed class MediaLibraryService(FratakShopDbContext context) : IMediaLibraryService
{
    private static MediaFileDto ToDto(MediaFile m) =>
        new(m.Id, m.Url, m.FileName, m.OriginalName, m.AltText, m.SizeBytes, m.Width, m.Height, m.CreatedAt);

    public async Task<PagedList<MediaFileDto>> ListAsync(string? query, int page, int take)
    {
        take = Math.Clamp(take, 6, 120);
        var source = context.MediaFiles.AsQueryable();
        foreach (var term in SearchText.Terms(query))
            source = source.Where(m => (m.OriginalName != null && m.OriginalName.Contains(term)) || (m.AltText != null && m.AltText.Contains(term)));

        var total = await source.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)take)));
        var rows = await source.OrderByDescending(m => m.CreatedAt).ThenBy(m => m.Id)
            .Skip((page - 1) * take).Take(take).ToListAsync();
        return new PagedList<MediaFileDto>(rows.Select(ToDto).ToList(), total, page, take);
    }

    public async Task<List<MediaFileDto>> AddAsync(IEnumerable<AddMediaCommand> files)
    {
        var rows = files.Select(f => new MediaFile
        {
            FileName = f.FileName,
            Url = f.Url,
            OriginalName = Trim(Path.GetFileNameWithoutExtension(f.OriginalName ?? ""), 200),
            // متن جایگزین پیش‌فرض: نام اصلی فایل بدون خط تیره (مثلاً «iphone-15-blue» → «iphone 15 blue»).
            AltText = Trim(Path.GetFileNameWithoutExtension(f.OriginalName ?? "").Replace('-', ' ').Replace('_', ' '), 200),
            ContentType = f.ContentType,
            SizeBytes = f.SizeBytes,
            Width = f.Width,
            Height = f.Height,
            UploadedByUserId = f.UploadedByUserId,
        }).ToList();
        if (rows.Count == 0)
            return [];

        context.MediaFiles.AddRange(rows);
        await context.SaveChangesAsync();
        return rows.Select(ToDto).ToList();
    }

    public async Task<OperationResult> UpdateAltAsync(Guid id, string? altText)
    {
        var row = await context.MediaFiles.AsTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (row is null)
            return OperationResult.NotFound("فایل پیدا نشد.");

        row.AltText = Trim(XssSecurity.ToPlainText(altText), 200);
        await context.SaveChangesAsync();
        return OperationResult.Success("متن جایگزین ذخیره شد.");
    }

    public async Task<string?> DeleteAsync(Guid id)
    {
        var row = await context.MediaFiles.AsTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (row is null)
            return null;

        context.MediaFiles.Remove(row);
        await context.SaveChangesAsync();
        return row.FileName;
    }

    public async Task<(int Count, long TotalBytes)> StatsAsync()
    {
        var count = await context.MediaFiles.CountAsync();
        var total = count == 0 ? 0 : await context.MediaFiles.SumAsync(m => (long)m.SizeBytes);
        return (count, total);
    }

    private static string? Trim(string? text, int max)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value))
            return null;
        return value.Length > max ? value[..max] : value;
    }
}
