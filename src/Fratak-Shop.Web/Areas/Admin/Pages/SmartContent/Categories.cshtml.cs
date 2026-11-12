using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.Smart;
using System.Text.Json;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Smart;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SmartContent;

/// <summary>
/// تکمیل هوشمند دسته‌ها (دور ۱۹، B19). آدرس: /admin/smart/categories
/// <para>فهرست دسته‌ها با «توضیح دارد؟»، «تصویر دارد؟» و تعداد کالا؛ دکمهٔ «تکمیل» هر ردیف یک کار در صف می‌گذارد.</para>
/// <para>متن دسته از روی کالاهای واقعی همان دسته (برندها، بازهٔ قیمت، ویژگی‌های مهم) نوشته می‌شود و (دور ۲۶) عکس فضاسازی
/// واقعی از اینترنت پیدا و محلی ذخیره می‌شود (نه از عکس کالاهای خود دسته). متن یا تصویری که خودتان گذاشته‌اید هرگز بازنویسی نمی‌شود.</para>
/// <para>دور ۲۲ – «۴ تصویر پیشنهادی»: ستون تصویرها همهٔ پیشنهادها را نشان می‌دهد (فعال با قاب آبی). «انتخاب» = این تصویر روی سایت برود؛
/// «حذف» یک تصویر یا «حذف همه» (فایل هم پاک می‌شود و دیگر خودکار ساخته نمی‌شود)؛ «پیشنهاد دوباره» = تصویر انتخاب‌شده می‌ماند و بقیه
/// با عکس‌های فضاسازی تازه از اینترنت عوض می‌شوند.</para>
/// </summary>
[AdminPermission(AdminPermissions.CatalogManage)]
public class CategoriesModel(ISmartCatalogService catalog, ISmartTaskQueue queue, ISmartCategoryService categorizer, SmartImageFinder images,
    ISupplierCategoryService supplierCategories, SmartSources sources) : AdminPageModel
{
    /// <summary>دور ۲۲: تصویر فعلی و پیشنهادهای هر دسته.</summary>
    public Dictionary<Guid, CategoryImageState> ImageStates { get; private set; } = [];

    public List<(Guid Id, string Name, bool HasDescription, bool HasImage, int Products)> Items { get; private set; } = [];

    /// <summary>آخرین کار هر دسته (برای ستون «وضعیت»).</summary>
    public Dictionary<Guid, SmartTaskRow> Latest { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        Items = await catalog.ListCategoriesAsync(ct);
        ImageStates = await catalog.ListCategoryImagesAsync(ct);
        foreach (var row in await queue.ListAsync(null, 300, ct))
        {
            if (row.TargetKind == SmartTarget.Category && row.TargetId is { } id && !Latest.ContainsKey(id))
                Latest[id] = row;
        }
    }

    /// <summary>
    /// دور ۲۱: دکمهٔ «دسته‌بندی هوشمند الان» – همان کاری که هر ۶ ساعت خودکار انجام می‌شود (کالاهای «دسته‌بندی نشده» و «سایر» دسته
    /// می‌گیرند و برای کالاهای مهم یا هر ۳ کالای مشابه دستهٔ تازه ساخته می‌شود). دستهٔ تازه بلافاصله «تکمیل» (متن و تصویر) را در صف می‌گیرد.
    /// </summary>
    public async Task<IActionResult> OnPostCategorizeAsync()
    {
        var ct = HttpContext.RequestAborted;
        var report = await categorizer.RunAsync(null, 500, ct);
        foreach (var category in report.Created)
            await queue.EnqueueAsync(SmartTaskKind.CategoryComplete, SmartTarget.Category, category.Id, category.Name, SmartPriority.Batch, null, ActorId, ActorName, ct);
        // دور ۳۱ (#۱): کالاهایی که هنوز بی‌دسته ماندند ← دسته از صفحهٔ تأمین‌کننده (۶ کالا در همین درخواست؛ بقیه را کار ساعتی انجام می‌دهد).
        var supplier = report.Waiting > 0
            ? await supplierCategories.RunAsync(DateTimeOffset.UtcNow, sources.SupplierCategoryAsync, null, 6, false, ct)
            : SupplierCategoryReport.Empty;
        SuccessMessage = "دسته‌بندی هوشمند: " + report.Summary + (supplier.Examined > 0 ? " " + supplier.Summary : "");
        return BackToList();
    }

    public async Task<IActionResult> OnPostRunAsync(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var item = (await catalog.ListCategoriesAsync(ct)).FirstOrDefault(c => c.Id == id);
        if (item.Id == Guid.Empty)
            return NotFound();
        await queue.EnqueueAsync(SmartTaskKind.CategoryComplete, SmartTarget.Category, id, item.Name, SmartPriority.Manual, null, ActorId, ActorName, ct);
        SuccessMessage = $"تکمیل دستهٔ «{item.Name}» در صف قرار گرفت.";
        return BackToList();
    }

    /// <summary>دور ۲۲: انتخاب یکی از تصویرهای پیشنهادی به‌عنوان تصویر دسته (فقط نام موجود در فهرست همان دسته).</summary>
    public async Task<IActionResult> OnPostChooseImageAsync(Guid id, string? option)
    {
        var ok = !string.IsNullOrWhiteSpace(option) && await catalog.ChooseCategoryImageAsync(id, option, HttpContext.RequestAborted);
        if (ok)
            SuccessMessage = "تصویر دسته عوض شد.";
        else
            ErrorMessage = "این تصویر در پیشنهادهای این دسته نیست.";
        return BackToList();
    }

    /// <summary>
    /// دور ۲۲: حذف یک تصویر پیشنهادی (option) یا همه (all=true). فایل‌ها از دیسک هم پاک می‌شوند؛ اگر تصویر فعال حذف شد، پیشنهاد بعدی
    /// جایش می‌نشیند. بعد از «حذف همه» تصویر خودکار دوباره ساخته نمی‌شود (مگر با «پیشنهاد دوباره»).
    /// </summary>
    public async Task<IActionResult> OnPostDeleteImageAsync(Guid id, string? option, bool all = false)
    {
        if (!all && string.IsNullOrWhiteSpace(option))
            return BackToList();
        var removed = await catalog.RemoveCategoryImagesAsync(id, all ? null : option, HttpContext.RequestAborted);
        if (removed.Count > 0)
        {
            images.DeleteCategoryFiles(removed);
            SuccessMessage = removed.Count == 1 ? "تصویر حذف شد." : $"{Format.Number(removed.Count)} تصویر حذف شد.";
        }
        else
        {
            ErrorMessage = "تصویری برای حذف پیدا نشد.";
        }
        return BackToList();
    }

    /// <summary>دور ۲۲: «پیشنهاد دوباره» تصویرها (کار CategoryComplete با بار {"images":1}).</summary>
    public async Task<IActionResult> OnPostResuggestAsync(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var state = await catalog.GetCategoryImagesAsync(id, ct);
        if (state is null)
            return NotFound();
        await queue.EnqueueAsync(SmartTaskKind.CategoryComplete, SmartTarget.Category, id, state.Name, SmartPriority.Manual,
            JsonSerializer.Serialize(new { images = 1 }), ActorId, ActorName, ct);
        SuccessMessage = $"پیشنهاد تصویرهای تازه برای «{state.Name}» در صف قرار گرفت؛ نتیجه اعلان می‌شود.";
        return BackToList();
    }
}
