using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.Smart;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SmartContent;

/// <summary>
/// تکمیل هوشمند دسته‌ها (دور ۱۹، B19). آدرس: /admin/smart/categories
/// <para>فهرست دسته‌ها با «توضیح دارد؟»، «تصویر دارد؟» و تعداد کالا؛ دکمهٔ «تکمیل» هر ردیف یک کار در صف می‌گذارد.</para>
/// <para>متن دسته از روی کالاهای واقعی همان دسته (برندها، بازهٔ قیمت، ویژگی‌های مهم) نوشته می‌شود و تصویر بنر بدون متن
/// از بهترین عکس کالاها ساخته می‌شود. متن یا تصویری که خودتان گذاشته‌اید هرگز بازنویسی نمی‌شود.</para>
/// </summary>
[AdminPermission(AdminPermissions.CatalogManage)]
public class CategoriesModel(ISmartCatalogService catalog, ISmartTaskQueue queue) : AdminPageModel
{
    public List<(Guid Id, string Name, bool HasDescription, bool HasImage, int Products)> Items { get; private set; } = [];

    /// <summary>آخرین کار هر دسته (برای ستون «وضعیت»).</summary>
    public Dictionary<Guid, SmartTaskRow> Latest { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        Items = await catalog.ListCategoriesAsync(ct);
        foreach (var row in await queue.ListAsync(null, 300, ct))
        {
            if (row.TargetKind == SmartTarget.Category && row.TargetId is { } id && !Latest.ContainsKey(id))
                Latest[id] = row;
        }
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
}
