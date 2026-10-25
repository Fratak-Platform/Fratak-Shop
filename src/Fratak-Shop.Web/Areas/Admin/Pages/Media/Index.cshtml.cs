using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Media;

/// <summary>
/// کتابخانهٔ رسانه (<c>/admin/media</c>): بارگذاری، جست‌وجو، کپی آدرس، ویرایش متن جایگزین و حذف تصویرها.
/// <para>
/// همین صفحه برای انتخابگر تصویر ویرایشگر متن هم JSON برمی‌گرداند:
/// <c>GET /admin/media?handler=List&amp;q=&amp;page=</c> و <c>POST ?handler=Upload</c> (با هدر <c>X-Requested-With: fetch</c>).
/// استفاده‌کننده: <c>wwwroot/assets/js/editor.js</c>.
/// </para>
/// فایل‌ها با <see cref="UploadArea.Media"/> در <c>wwwroot/uploads/media</c> ذخیره می‌شوند؛ تصویر بازسازی و متادیتای آن
/// (مکان GPS و ...) حذف می‌شود و نام تصادفی می‌گیرد، پس فایل مخرب یا نام خطرناک ذخیره نمی‌شود.
/// </summary>
[AdminPermission(AdminPermissions.ContentManage, AdminPermissions.BlogWrite, AdminPermissions.ProductsManage, AdminPermissions.MarketingManage)]
[RequestFormLimits(MultipartBodyLengthLimit = 41 * 1024 * 1024)]
[RequestSizeLimit(42 * 1024 * 1024)]
public class IndexModel(IMediaLibraryService media, IUploadStore uploads, IMonitoringService monitoring) : AdminPageModel
{
    /// <summary>حداکثر تعداد فایل در هر بارگذاری و حجم هر فایل (کیلوبایت).</summary>
    public const int MaxFiles = 20;
    public const int MaxSizeKb = 4096;

    [FromQuery(Name = "q")] public string? Query { get; set; }
    [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;

    public PagedList<MediaFileDto> Items { get; private set; } = new([], 0, 1, 36);
    public int TotalCount { get; private set; }
    public long TotalBytes { get; private set; }

    private bool IsFetch => Request.Headers.XRequestedWith == "fetch";

    public async Task OnGetAsync()
    {
        Items = await media.ListAsync(Query, ClampPage(PageNumber), 36);
        (TotalCount, TotalBytes) = await media.StatsAsync();
    }

    /// <summary>فهرست JSON برای انتخابگر ویرایشگر (۲۴ مورد در هر صفحه).</summary>
    public async Task<IActionResult> OnGetListAsync(string? q, int page = 1)
    {
        var list = await media.ListAsync(q, ClampPage(page), 24);
        Response.Headers.CacheControl = "no-store";
        return new JsonResult(new { items = list.Items, page = list.Page, pageCount = list.PageCount, total = list.Total });
    }

    public async Task<IActionResult> OnPostUploadAsync(List<IFormFile> files)
    {
        var (saved, error) = await uploads.SaveAsync(UploadArea.Media, files, MaxFiles, MaxSizeKb);
        if (error is not null || saved.Count == 0)
            return Reply(false, error ?? "فایلی انتخاب نشده است.");

        // نام اصلی فایل‌ها به همان ترتیب ذخیره (فقط برای جست‌وجو و متن جایگزین پیش‌فرض).
        var originals = files.Where(f => f.Length > 0).Select(f => f.FileName).ToList();
        var added = await media.AddAsync(saved.Select((f, i) => new AddMediaCommand(
            f.FileName, $"/uploads/{UploadArea.Media.Name}/{f.FileName}", i < originals.Count ? originals[i] : null,
            f.ContentType, f.SizeBytes, f.Width, f.Height, ActorId)));
        await monitoring.LogActionAsync("media.upload", "MediaFile", added.FirstOrDefault()?.Id, $"{added.Count} فایل بارگذاری شد");
        return Reply(true, $"{added.Count} تصویر بارگذاری شد.", added);
    }

    public async Task<IActionResult> OnPostAltAsync(Guid id, string? alt)
    {
        var result = await media.UpdateAltAsync(id, alt);
        return Reply(result.IsSuccess(), result.Message);
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var fileName = await media.DeleteAsync(id);
        if (fileName is null)
            return Reply(false, "فایل پیدا نشد.");

        uploads.Delete(UploadArea.Media, [fileName]);
        await monitoring.LogActionAsync("media.delete", "MediaFile", id, fileName);
        return Reply(true, "تصویر حذف شد.");
    }

    private IActionResult Reply(bool ok, string message, object? data = null)
    {
        if (IsFetch)
            return new JsonResult(new { ok, message, data }) { StatusCode = ok ? 200 : 400 };

        TempData[ok ? "SuccessMessage" : "ErrorMessage"] = message;
        return BackToList();
    }
}
