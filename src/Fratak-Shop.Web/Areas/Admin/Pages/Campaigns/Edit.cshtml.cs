using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Design;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Design;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Campaigns;

/// <summary>
/// صفحه‌ساز کمپین (دور ۲۱). آدرس: /admin/campaigns/edit/{id}
/// <para>ویرایشگر GrapesJS (کشیدن و رها کردن بلوک‌ها، پیش‌نمایش موبایل/تبلت/دسکتاپ، ویرایش متن روی صفحه، رنگ و فاصله‌ها از «ظاهر»،
/// لایه‌ها، برگرداندن). رفتار: wwwroot/assets/js/page-builder.js.</para>
/// <para>ذخیره (XHR): پروژهٔ ویرایشگر (برای ادامهٔ ویرایش) + HTML و CSS خروجی؛ سرور HTML را پاک‌سازی (بدون اسکریپت/فرم/رویداد) و CSS را
/// محدود به «.fs-lp» می‌کند (LandingPageService). تنظیمات صفحه: عنوان، نامک انگلیسی، متن اشتراک‌گذاری، نمایش هدر/فوتر، زمان شروع و پایان، انتشار.</para>
/// </summary>
[AdminPermission(AdminPermissions.MarketingManage, AdminPermissions.ContentManage)]
[RequestFormLimits(MultipartBodyLengthLimit = 12 * 1024 * 1024, ValueLengthLimit = 4 * 1024 * 1024)]
public class EditModel(ILandingPageService landings, IUploadStore uploads, IStoreSettingService settings) : AdminPageModel
{
    public LandingPage Landing { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var page = await landings.GetAsync(id, HttpContext.RequestAborted);
        if (page is null)
            return NotFound();
        Landing = page;
        return Page();
    }

    /// <summary>دادهٔ ویرایشگر: پروژهٔ ذخیره‌شده، یا اگر نیست HTML/CSS فعلی (صفحهٔ تازه از قالب).</summary>
    public async Task<IActionResult> OnGetProjectAsync(Guid id)
    {
        var page = await landings.GetAsync(id, HttpContext.RequestAborted);
        if (page is null)
            return NotFound();
        return new JsonResult(new { project = page.ProjectJson, html = page.Html ?? "", css = page.Css ?? "" });
    }

    /// <summary>قالب آماده برای دکمهٔ «قالب‌ها» (جایگزین محتوای بوم).</summary>
    public async Task<IActionResult> OnGetTemplateAsync(Guid id, string? key)
    {
        var page = await landings.GetAsync(id, HttpContext.RequestAborted);
        if (page is null)
            return NotFound();
        var store = await settings.GetAsync(HttpContext.RequestAborted);
        var built = LandingTemplates.Build(key, store.SiteName, page.EndAt);
        return new JsonResult(new { html = built.Html, css = built.Css });
    }

    /// <summary>ذخیرهٔ صفحه از صفحه‌ساز (فرم XHR). پاسخ JSON با نامک نهایی (ممکن است برای یکتا شدن عدد بگیرد).</summary>
    public async Task<IActionResult> OnPostSaveAsync(Guid id, string? title, string? slug, string? metaTitle, string? metaDescription, string? imageUrl,
        string? project, string? html, string? css, bool showHeader, bool showFooter, bool isActive, string? startAt, string? endAt)
    {
        var page = await landings.GetAsync(id, HttpContext.RequestAborted);
        if (page is null)
            return NotFound();
        var start = PersianDateInput.ParseDateTime(startAt);
        var end = PersianDateInput.ParseDateTime(endAt, new TimeOnly(23, 59));
        if (!string.IsNullOrWhiteSpace(startAt) && start is null || !string.IsNullOrWhiteSpace(endAt) && end is null)
            return BadRequest(new { error = "تاریخ را مثل «1404/09/01 18:00» بنویسید." });
        var result = await landings.SaveAsync(new LandingSaveInput(id, title ?? "", slug, metaTitle, metaDescription, imageUrl, project, html, css,
            showHeader, showFooter, isActive, start, end, page.TemplateKey), ActorName, HttpContext.RequestAborted);
        if (!result.IsSuccess())
            return BadRequest(new { error = result.Message });
        var saved = await landings.GetAsync(id, HttpContext.RequestAborted);
        return new JsonResult(new { ok = true, message = result.Message, slug = saved?.Slug, url = $"/campaign/{saved?.Slug}" });
    }

    /// <summary>
    /// آپلود عکس از «مدیریت تصاویر» صفحه‌ساز (قالب پاسخ GrapesJS: <c>{ data: [{ src, width, height }] }</c>). WebP، حداکثر ۵ عکس × ۱۰ مگابایت.
    /// </summary>
    public async Task<IActionResult> OnPostUploadAsync(List<IFormFile>? files)
    {
        if (files is null || files.Count == 0)
            return BadRequest(new { error = "فایلی انتخاب نشده است." });
        var (stored, error) = await uploads.SaveAsync(UploadArea.Content, files, 5, 10_240, HttpContext.RequestAborted);
        if (error is not null)
            return BadRequest(new { error });
        return new JsonResult(new
        {
            data = stored.Select(f => new { src = $"/uploads/{UploadArea.Content.Name}/{f.FileName}", type = "image", width = f.Width ?? 0, height = f.Height ?? 0 }),
        });
    }
}
