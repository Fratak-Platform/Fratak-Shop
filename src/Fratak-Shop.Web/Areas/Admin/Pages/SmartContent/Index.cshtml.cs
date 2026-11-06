using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.Smart;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SmartContent;

/// <summary>
/// داشبورد «محتوای هوشمند» (دور ۱۹، B20/B21). آدرس: /admin/smart
/// <para>چه نشان می‌دهد؟ شمارش صف (در صف، در حال انجام، منتظر تأیید، ناموفق، انجام‌شدهٔ امروز)، کالاهای ناقص و فهرست کارها.</para>
/// <para>چه می‌شود کرد؟ «تکمیل هوشمند همهٔ کالاهای ناقص» (تا ۵۰ کالا در صف)، «تکمیل همهٔ دسته‌ها»، لغو/تلاش دوباره/پاک‌سازی.
/// همه چیز در پس‌زمینه انجام می‌شود و صفحه منتظر نمی‌ماند.</para>
/// </summary>
[AdminPermission(AdminPermissions.ProductsManage, AdminPermissions.CatalogManage, AdminPermissions.BlogPublish)]
public class IndexModel(ISmartTaskQueue queue, ISmartCatalogService catalog, IProductInsightService insights) : AdminPageModel
{
    public SmartQueueStats Stats { get; private set; } = new(0, 0, 0, 0, 0);

    public List<SmartTaskRow> Tasks { get; private set; } = [];

    public List<IncompleteProduct> Incomplete { get; private set; } = [];

    /// <summary>
    /// «خلاصهٔ خرید»هایی که ساخته شده‌اند ولی چون نظر کافی نداشتند (کمتر از حد تأیید خودکار) منتظر تأیید شما هستند.
    /// مثال: کالایی با ۵ نظر ← پیش‌نویس اینجا دیده می‌شود؛ با کلیک، صفحهٔ کالای هوشمند باز می‌شود و تأیید/ویرایش/رد می‌کنید.
    /// </summary>
    public List<(Guid ProductId, string? ProductName, DateTimeOffset GeneratedAt, int ReviewCount)> PendingInsights { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public SmartTaskStatus? Status { get; set; }

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        Stats = await queue.StatsAsync(ct);
        Tasks = await queue.ListAsync(Status, 150, ct);
        Incomplete = await catalog.ListIncompleteAsync(30, ct);
        PendingInsights = await insights.PendingAsync(20, ct);
    }

    /// <summary>صف کردن تکمیل هوشمند برای کالاهای ناقص (حداکثر ۵۰ در هر بار؛ تکراری‌ها خودکار حذف می‌شوند).</summary>
    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostCompleteAllAsync()
    {
        var ct = HttpContext.RequestAborted;
        var items = await catalog.ListIncompleteAsync(50, ct);
        foreach (var item in items)
            await queue.EnqueueAsync(SmartTaskKind.ProductComplete, SmartTarget.Product, item.Id, item.Name, SmartPriority.Batch, null, ActorId, ActorName, ct);
        SuccessMessage = items.Count == 0
            ? "کالای ناقصی پیدا نشد."
            : $"{Format.Number(items.Count)} کالا در صف تکمیل هوشمند قرار گرفت. هر کالا حدود یک دقیقه طول می‌کشد؛ نتیجه‌ها همین‌جا دیده می‌شوند.";
        return BackToList();
    }

    /// <summary>صف کردن تکمیل توضیح/تصویر برای دسته‌هایی که ندارند.</summary>
    [AdminPermission(AdminPermissions.CatalogManage)]
    public async Task<IActionResult> OnPostCategoriesAsync()
    {
        var ct = HttpContext.RequestAborted;
        var categories = (await catalog.ListCategoriesAsync(ct)).Where(c => (!c.HasDescription || !c.HasImage) && c.Products >= 3).Take(50).ToList();
        foreach (var c in categories)
            await queue.EnqueueAsync(SmartTaskKind.CategoryComplete, SmartTarget.Category, c.Id, c.Name, SmartPriority.Batch, null, ActorId, ActorName, ct);
        SuccessMessage = categories.Count == 0 ? "همهٔ دسته‌های دارای کالا توضیح و تصویر دارند." : $"{Format.Number(categories.Count)} دسته در صف تکمیل قرار گرفت.";
        return BackToList();
    }

    [AdminPermission(AdminPermissions.ProductsManage, AdminPermissions.CatalogManage, AdminPermissions.BlogPublish)]
    public async Task<IActionResult> OnPostCancelAsync(Guid id)
    {
        var result = await queue.CancelAsync(id, HttpContext.RequestAborted);
        if (result.Status == Fratak_Shop.Core.Utilities.EnOperationResultStatus.Success)
            SuccessMessage = result.Message;
        else
            ErrorMessage = result.Message;
        return BackToList();
    }

    [AdminPermission(AdminPermissions.ProductsManage, AdminPermissions.CatalogManage, AdminPermissions.BlogPublish)]
    public async Task<IActionResult> OnPostRetryAsync(Guid id)
    {
        var result = await queue.RetryAsync(id, HttpContext.RequestAborted);
        if (result.Status == Fratak_Shop.Core.Utilities.EnOperationResultStatus.Success)
            SuccessMessage = result.Message;
        else
            ErrorMessage = result.Message;
        return BackToList();
    }

    /// <summary>پاک کردن کارهای تمام‌شدهٔ قدیمی (بیش از ۳۰ روز).</summary>
    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostPurgeAsync()
    {
        var removed = await queue.PurgeAsync(HttpContext.RequestAborted);
        SuccessMessage = $"{Format.Number(removed)} کار قدیمی پاک شد.";
        return BackToList();
    }
}
