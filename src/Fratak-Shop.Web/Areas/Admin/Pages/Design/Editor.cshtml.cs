using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Blogs;
using Fratak_Shop.Core.Services.Design;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Design;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Design;
using Fratak_Shop.Web.Infrastructure.Smart;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Design;

/// <summary>
/// ویرایشگر گرافیکی طرح (دور ۲۱). آدرس: /admin/design/editor/{id}
/// <para>بوم Fabric.js در مرورگر (wwwroot/assets/js/design-studio.js): کشیدن، اندازه، چرخش، رنگ، قلم، لایه‌ها، برگرداندن/تکرار (۳۰ قدم)،
/// متن و شکل و نمونه‌های آماده (نشان تخفیف، دکمه، برچسب قیمت)، قالب‌ها، و «افزودن عکس» از سه جا: عکس کالاهای فروشگاه، آپلود، جست‌وجوی اینترنت.</para>
/// <para>ذخیره: مرورگر PNG همان بوم را می‌سازد ← سرور WebP ذخیره می‌کند (Handler «Save»). اسلایدر اصلی دو زبانه دارد: دسکتاپ و موبایل.</para>
/// <para>امنیت: همهٔ عکس‌های بوم آدرس محلی‌اند (عکس اینترنت اول با «Import» دانلود و پاک‌سازی می‌شود؛ بوم آلوده (tainted) نمی‌شود)،
/// حجم JSON و PNG سقف دارد، همهٔ POSTها توکن ضدجعل دارند و GETها هیچ تغییری نمی‌دهند.</para>
/// </summary>
[AdminPermission(AdminPermissions.MarketingManage, AdminPermissions.ContentManage, AdminPermissions.BlogPublish)]
[RequestFormLimits(MultipartBodyLengthLimit = 25 * 1024 * 1024)]
public class EditorModel(IDesignService designs, DesignStudio studio, IPostService posts, SmartImageFinder images, IUploadStore uploads) : AdminPageModel
{
    public DesignDoc Doc { get; private set; } = null!;

    public DesignPreset? Preset { get; private set; }

    /// <summary>اندازهٔ نسخهٔ موبایل (فقط اسلایدر اصلی).</summary>
    public DesignPreset? Mobile { get; private set; }

    public Guid? PostId { get; private set; }

    public string? PostTitle { get; private set; }

    public string? ImageUrl => DesignStudio.Url(Doc.Kind, Doc.ImageName);

    public bool CanPublishBanner => CanAny(AdminPermissions.MarketingManage, AdminPermissions.ContentManage);

    public bool CanCover => CanAny(AdminPermissions.BlogPublish, AdminPermissions.ContentManage);

    public async Task<IActionResult> OnGetAsync(Guid id, Guid? post)
    {
        var doc = await designs.GetAsync(id, HttpContext.RequestAborted);
        if (doc is null)
            return NotFound();
        Doc = doc;
        Preset = DesignPresets.Find(doc.PresetKey);
        Mobile = Preset?.MobileKey is { } mk ? DesignPresets.Find(mk) : null;
        var target = post ?? doc.BlogPostId;
        if (doc.Kind == DesignKind.BlogCover && target is { } postId && await posts.GetPostByAsync(postId) is { } found)
        {
            PostId = postId;
            PostTitle = found.Title;
        }
        return Page();
    }

    /// <summary>دادهٔ بوم (JSON طرح) برای بار اول ویرایشگر – جدا از HTML تا صفحه سبک بماند.</summary>
    public async Task<IActionResult> OnGetDocAsync(Guid id)
    {
        var doc = await designs.GetAsync(id, HttpContext.RequestAborted);
        if (doc is null)
            return NotFound();
        var mobile = DesignPresets.Find(doc.PresetKey)?.MobileKey is { } mk ? DesignPresets.Find(mk) : null;
        return new JsonResult(new
        {
            title = doc.Title,
            width = doc.Width,
            height = doc.Height,
            json = doc.Json,
            mobile = mobile is null ? null : new { width = mobile.Width, height = mobile.Height, json = doc.MobileJson ?? DesignTemplates.Blank(mobile.Width, mobile.Height) },
        });
    }

    /// <summary>
    /// قالب آماده در اندازهٔ همین طرح (منوی «قالب‌ها»). متن نمونه است و مدیر روی بوم عوضش می‌کند.
    /// مثال: ?handler=Template&amp;id=…&amp;layout=sale&amp;palette=sunset&amp;mobile=false
    /// </summary>
    public async Task<IActionResult> OnGetTemplateAsync(Guid id, string? layout, string? palette, bool mobile)
    {
        var doc = await designs.GetAsync(id, HttpContext.RequestAborted);
        if (doc is null)
            return NotFound();
        var preset = DesignPresets.Find(doc.PresetKey);
        var size = mobile && preset?.MobileKey is { } mk && DesignPresets.Find(mk) is { } m ? m : null;
        var (w, h, transparent) = size is null ? (doc.Width, doc.Height, preset?.Transparent == true) : (size.Width, size.Height, size.Transparent);
        if (string.IsNullOrWhiteSpace(layout) || layout == "blank")
            return Content(DesignTemplates.Blank(w, h, transparent), "application/json");
        var spec = DesignStudio.SampleSpec(layout, palette, doc.Kind);
        if (!string.IsNullOrWhiteSpace(doc.Headline))
            spec = spec with { Headline = doc.Headline };
        return Content(DesignTemplates.Build(spec, w, h, transparent), "application/json");
    }

    /// <summary>عکس کالاهای خود فروشگاه (جست‌وجو در نام؛ خالی = پربازدیدها).</summary>
    public async Task<IActionResult> OnGetImagesAsync(string? q)
    {
        var list = await designs.ProductImagesAsync(q, 24, HttpContext.RequestAborted);
        return new JsonResult(list.Select(i => new { url = i.Url, title = i.Title }));
    }

    /// <summary>جست‌وجوی عکس در اینترنت (فقط فهرست؛ چیزی دانلود نمی‌شود). round = صفحهٔ بعدی نتایج.</summary>
    public async Task<IActionResult> OnGetWebAsync(string? q, int round)
    {
        var hits = await images.SearchWebAsync(q ?? "", Math.Clamp(round, 0, 20), HttpContext.RequestAborted);
        return new JsonResult(hits.Select(h => new { url = h.Url, title = h.Title, page = h.PageUrl }));
    }

    /// <summary>وارد کردن عکس انتخاب‌شده از اینترنت به پوشهٔ محلی (حداقل ۳۰۰ پیکسل).</summary>
    public async Task<IActionResult> OnPostImportAsync(string url)
    {
        var found = await images.ImportAsync(url ?? "", UploadArea.Content, 300, HttpContext.RequestAborted);
        if (found is null)
            return BadRequest(new { error = "این عکس دریافت نشد (کوچک‌تر از ۳۰۰ پیکسل، خراب یا در دسترس نیست). عکس دیگری انتخاب کنید." });
        return new JsonResult(new { url = $"/uploads/{UploadArea.Content.Name}/{found.FileName}", width = found.Width, height = found.Height });
    }

    /// <summary>
    /// دور ۲۷ – «حذف زمینهٔ عکس»: عکس محلی انتخاب‌شده روی بوم ← نسخهٔ شفاف (زمینهٔ سفید/خیلی روشن برداشته و حاشیه بریده می‌شود).
    /// پاسخ: {url, width, height} یا 400 با {error} فارسی. فقط عکس‌های خود سایت (/uploads/…) پذیرفته می‌شوند.
    /// </summary>
    public async Task<IActionResult> OnPostCutoutAsync(string? url)
    {
        var (image, error) = await studio.RemoveBackgroundAsync(url, HttpContext.RequestAborted);
        if (image is null)
            return BadRequest(new { error = error ?? "حذف زمینه انجام نشد." });
        return new JsonResult(new { url = image.Url, width = image.Width, height = image.Height });
    }

    /// <summary>آپلود عکس از کامپیوتر/گوشی (WebP، حداکثر ۱۰ مگابایت).</summary>
    public async Task<IActionResult> OnPostUploadAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "فایلی انتخاب نشده است." });
        var (files, error) = await uploads.SaveAsync(UploadArea.Content, [file], 1, DesignStudio.MaxUploadKb, HttpContext.RequestAborted);
        if (error is not null || files.Count != 1)
            return BadRequest(new { error = error ?? "عکس ذخیره نشد." });
        var stored = files[0];
        return new JsonResult(new { url = $"/uploads/{UploadArea.Content.Name}/{stored.FileName}", width = stored.Width ?? 0, height = stored.Height ?? 0 });
    }

    /// <summary>
    /// ذخیره (فرم چندبخشی از ویرایشگر): JSON بوم + PNG دسکتاپ و (اختیاری) موبایل. پاسخ JSON با آدرس تصویر تازه.
    /// </summary>
    /// <remarks>دور ۲۶: <paramref name="motion"/> = حرکت بنر زنده (None/Soft/Loop؛ نامعتبر ← بدون تغییر).</remarks>
    public async Task<IActionResult> OnPostSaveAsync(Guid id, string? title, string? json, string? mobileJson, IFormFile? image, IFormFile? mobileImage, string? headline,
        string? motion)
    {
        if (string.IsNullOrWhiteSpace(json))
            return BadRequest(new { error = "طرح خالی است." });
        BannerMotion? chosen = Enum.TryParse<BannerMotion>(motion, true, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(motion, out _) ? parsed : null;
        var result = await studio.SaveFromEditorAsync(id, title ?? "", json, string.IsNullOrWhiteSpace(mobileJson) ? null : mobileJson,
            image, mobileImage, headline, ActorName, HttpContext.RequestAborted, chosen);
        if (!result.IsSuccess())
            return BadRequest(new { error = result.Message });
        var doc = await designs.GetAsync(id, HttpContext.RequestAborted);
        return new JsonResult(new { ok = true, message = "طرح ذخیره شد.", image = doc is null ? null : DesignStudio.Url(doc.Kind, doc.ImageName) });
    }

    /// <summary>انتشار طرح بنر در جایگاه انتخابی (اسلایدر اصلی/بنر عریض/کارت تبلیغ) با لینک و متن دکمه.</summary>
    public async Task<IActionResult> OnPostPublishAsync(Guid id, BannerPosition position, string? link, string? button)
    {
        if (!CanPublishBanner)
            return Forbid();
        if (!Enum.IsDefined(position))
            position = BannerPosition.None;
        if (!string.IsNullOrWhiteSpace(link) && DesignService.SafeLink(link) is null)
            return BadRequest(new { error = "لینک باید مسیر داخلی سایت (مثل /products) یا آدرس https باشد." });
        var result = await designs.PublishBannerAsync(id, position, link, button, ActorName, HttpContext.RequestAborted);
        return result.IsSuccess() ? new JsonResult(new { ok = true, message = result.Message }) : BadRequest(new { error = result.Message });
    }

    /// <summary>گذاشتن تصویر طرح روی مقاله به‌عنوان کاور (کاور قبلی مقاله پاک نمی‌شود؛ شاید جای دیگری استفاده شده باشد).</summary>
    public async Task<IActionResult> OnPostCoverAsync(Guid id, Guid postId)
    {
        if (!CanCover)
            return Forbid();
        var result = await designs.AttachCoverAsync(id, postId, ActorName, HttpContext.RequestAborted);
        return result.IsSuccess() ? new JsonResult(new { ok = true, message = result.Message, edit = $"/admin/blog/edit/{postId}" }) : BadRequest(new { error = result.Message });
    }
}
