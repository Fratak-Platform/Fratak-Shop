using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.Smart;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SmartContent;

/// <summary>
/// تقویم بلاگ خودکار (دور ۱۹، B14–B18). آدرس: /admin/smart/blog
/// <para>ایده‌های ۴۵ روز آینده و گذشتهٔ نزدیک با نوع مقاله، وضعیت، نمرهٔ کیفیت و لینک مقالهٔ ساخته‌شده.</para>
/// <para>دکمه‌ها: «پر کردن تقویم» (ایده از دسته‌ها/پرفروش‌ها/جست‌وجوها)، «همین حالا بنویس»، «رد» و فرم «افزودن ایده».
/// مقاله فقط از کالاهای واقعی و موجود نوشته می‌شود و اگر کنترل کیفیت را رد کند، پیش‌نویس می‌ماند.</para>
/// </summary>
[AdminPermission(AdminPermissions.BlogPublish)]
public class BlogModel(IBlogPlanService plan, ISmartTaskQueue queue, ISmartConfigService config) : AdminPageModel
{
    public List<BlogPlanRow> Rows { get; private set; } = [];

    public List<(Guid Id, string Name)> Categories { get; private set; } = [];

    public SmartOptions Options { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        Rows = await plan.ListAsync(45, ct);
        Categories = await plan.CategoriesAsync(ct);
        Options = await config.GetAsync(ct);
    }

    public async Task<IActionResult> OnPostPlanAsync()
    {
        var ct = HttpContext.RequestAborted;
        var options = await config.GetAsync(ct);
        var added = await plan.PlanAsync(Math.Max(1, options.BlogPostsPerWeek), ct);
        SuccessMessage = added == 0 ? "تقویم ۴ هفتهٔ آینده پر است (یا موضوع تازهٔ کافی پیدا نشد)." : $"{Format.Number(added)} ایدهٔ تازه به تقویم اضافه شد.";
        return BackToList();
    }

    public async Task<IActionResult> OnPostWriteAsync(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var row = await plan.GetAsync(id, ct);
        if (row is null || row.Status is not (BlogPlanStatus.Planned or BlogPlanStatus.Failed or BlogPlanStatus.Skipped))
        {
            ErrorMessage = "این ایده قابل نوشتن نیست (یا قبلاً نوشته شده).";
            return BackToList();
        }
        if (row.Status != BlogPlanStatus.Planned)
            await plan.MarkAsync(id, BlogPlanStatus.Planned, cancellationToken: ct);
        await queue.EnqueueAsync(SmartTaskKind.BlogArticle, SmartTarget.BlogPlan, id, row.Topic, SmartPriority.Manual, null, ActorId, ActorName, ct);
        SuccessMessage = $"نوشتن «{row.Topic}» در صف قرار گرفت؛ چند دقیقهٔ دیگر نتیجه این‌جا دیده می‌شود.";
        return BackToList();
    }

    public async Task<IActionResult> OnPostSkipAsync(Guid id)
    {
        ShowResult(await plan.SkipAsync(id, HttpContext.RequestAborted));
        return BackToList();
    }

    /// <summary>افزودن ایدهٔ دستی. days: چند روز دیگر (۰ = امروز).</summary>
    public async Task<IActionResult> OnPostAddAsync(BlogArticleType type, string? topic, string? keyword, Guid? categoryId, int days)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            ErrorMessage = "موضوع را بنویسید.";
            return BackToList();
        }
        var when = DateTimeOffset.UtcNow.AddDays(Math.Clamp(days, 0, 120));
        ShowResult(await plan.AddAsync(type, topic, keyword, categoryId, null, when, HttpContext.RequestAborted));
        return BackToList();
    }
}
