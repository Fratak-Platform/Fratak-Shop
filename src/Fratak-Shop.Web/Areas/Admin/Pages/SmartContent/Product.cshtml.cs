using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Data.Entities.Smart;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Smart;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SmartContent;

/// <summary>
/// محتوای هوشمند یک کالا (دور ۱۹، B1–B13، B20). آدرس: /admin/smart/product/{id}
/// <para>بالا: دکمه‌های «تکمیل هوشمند» (همه)، «فقط عکس»، «فقط مشخصات»، «فقط خلاصهٔ خرید».</para>
/// <para>وسط: موارد منتظر تأیید – مشخصات پیشنهادی (با منبع و میزان توافق، قبل/بعد) و عکس‌های پیشنهادی؛ کارشناس تیک می‌زند و «اعمال».
/// عکس‌های ردشده از دیسک هم پاک می‌شوند (فایل یتیم نمی‌ماند).</para>
/// <para>پایین: خلاصهٔ خرید (پیش‌نویس/تأییدشده) با ویرایش، تأیید و رد؛ و تاریخچهٔ کارهای این کالا.</para>
/// </summary>
[AdminPermission(AdminPermissions.ProductsManage)]
public class ProductModel(
    ISmartTaskQueue queue,
    ISmartCatalogService catalog,
    IProductInsightService insights,
    ISeoAutopilotService autopilot,
    SmartImageFinder images) : AdminPageModel
{
    public SmartProductSubject? Subject { get; private set; }

    public ProductInsightView? Insight { get; private set; }

    public List<SmartTaskRow> History { get; private set; } = [];

    /// <summary>کارهای «منتظر تأیید» همراه نتیجه‌شان (مشخصات و عکس‌های پیشنهادی).</summary>
    public List<(SmartTaskRow Row, SmartTaskResult Result)> Pending { get; private set; } = [];

    [BindProperty] public ProductInsightEdit Edit { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        Subject = await catalog.GetProductAsync(id, ct);
        if (Subject is null)
            return NotFound();
        Insight = await insights.GetAsync(id, ct);
        if (Insight is not null)
        {
            Edit = new ProductInsightEdit
            {
                Summary = Insight.Summary,
                ForWhom = string.Join('\n', Insight.ForWhom),
                NotFor = string.Join('\n', Insight.NotFor),
                Pros = string.Join('\n', Insight.Pros.Select(p => p.Text)),
                Cons = string.Join('\n', Insight.Cons.Select(p => p.Text)),
                Verdict = Insight.Verdict,
            };
        }
        History = await queue.ForTargetAsync(SmartTarget.Product, id, 20, ct);
        foreach (var row in History.Where(r => r.Status == SmartTaskStatus.NeedsReview))
        {
            var result = SmartUi.Result(await queue.GetResultJsonAsync(row.Id, ct));
            if (result is not null && (PendingSpecs(result).Any() || result.PendingImages.Count > 0))
                Pending.Add((row, result));
        }
        return Page();
    }

    /// <summary>ردیف‌های مشخصاتی که هنوز تأیید لازم دارند (تکراری‌ها و خودکارها حذف).</summary>
    public static IEnumerable<SpecSuggestion> PendingSpecs(SmartTaskResult result) =>
        result.Specs.Where(s => s.Diff != SpecDiffKind.Same && !s.AutoApplicable);

    /// <summary>دکمه‌های اجرا. kind: complete | images | specs | insight.</summary>
    public async Task<IActionResult> OnPostRunAsync(Guid id, string? kind)
    {
        var ct = HttpContext.RequestAborted;
        var subject = await catalog.GetProductAsync(id, ct);
        if (subject is null)
            return NotFound();
        var taskKind = kind switch
        {
            "images" => SmartTaskKind.ProductImages,
            "specs" => SmartTaskKind.ProductSpecs,
            "insight" => SmartTaskKind.ProductInsight,
            _ => SmartTaskKind.ProductComplete,
        };
        await queue.EnqueueAsync(taskKind, SmartTarget.Product, id, subject.Name, SmartPriority.Manual, null, ActorId, ActorName, ct);
        SuccessMessage = $"«{SmartUi.KindTitle(taskKind)}» در صف قرار گرفت و معمولاً کمتر از یک دقیقه طول می‌کشد. صفحه را چند لحظه بعد تازه کنید.";
        return RedirectToPage(new { id });
    }

    /// <summary>اعمال ردیف‌های تیک‌خوردهٔ مشخصات و عکس‌های انتخاب‌شده؛ بقیهٔ عکس‌های پیشنهادی پاک می‌شوند.</summary>
    public async Task<IActionResult> OnPostApplyAsync(Guid id, Guid taskId, string[]? specs, string[]? imageNames)
    {
        var ct = HttpContext.RequestAborted;
        var (row, result) = await LoadPendingAsync(id, taskId, ct);
        if (row is null || result is null)
        {
            ErrorMessage = "این مورد دیگر منتظر تأیید نیست.";
            return RedirectToPage(new { id });
        }

        var chosenKeys = new HashSet<string>(specs ?? [], StringComparer.Ordinal);
        var chosen = PendingSpecs(result).Where(s => chosenKeys.Contains(s.Key)).ToList();
        var specsAdded = 0;
        if (chosen.Count > 0)
        {
            var replace = chosen.Where(s => s.Diff == SpecDiffKind.Different).Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
            var (added, replaced) = await catalog.ApplySpecsAsync(id, chosen.Select(s => (s.Key, s.Value)).ToList(), replace, ct);
            specsAdded = added + replaced;
        }

        // فقط نام‌هایی که واقعاً در نتیجهٔ همین کار بوده‌اند پذیرفته می‌شوند (جلوگیری از دست‌کاری فرم).
        var allowed = result.PendingImages.Select(i => i.FileName).ToHashSet(StringComparer.Ordinal);
        var approved = (imageNames ?? []).Where(allowed.Contains).Distinct(StringComparer.Ordinal).ToList();
        var imagesAdded = 0;
        if (approved.Count > 0)
            imagesAdded = await catalog.AddProductImagesAsync(id, approved.Select(n => (n, row.TargetTitle ?? "")).ToList(), ct);
        images.DeleteProductFiles(allowed.Except(approved, StringComparer.Ordinal));

        if (specsAdded + imagesAdded > 0)
            await autopilot.ProcessAsync(SeoEntityKind.Product, id, ct);
        await queue.MarkReviewedAsync(taskId, $"بازبینی {ActorName}: {Format.Number(specsAdded)} مشخصه و {Format.Number(imagesAdded)} عکس اعمال شد.", ct);
        SuccessMessage = $"{Format.Number(specsAdded)} مشخصه و {Format.Number(imagesAdded)} عکس اعمال شد.";
        return RedirectToPage(new { id });
    }

    /// <summary>رد همهٔ پیشنهادهای یک کار (عکس‌های موقت پاک می‌شوند).</summary>
    public async Task<IActionResult> OnPostRejectAsync(Guid id, Guid taskId)
    {
        var ct = HttpContext.RequestAborted;
        var (row, result) = await LoadPendingAsync(id, taskId, ct);
        if (row is not null && result is not null)
        {
            images.DeleteProductFiles(result.PendingImages.Select(i => i.FileName));
            await queue.MarkReviewedAsync(taskId, $"پیشنهادها توسط {ActorName} رد شد.", ct);
            SuccessMessage = "پیشنهادها رد شد.";
        }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostInsightAsync(Guid id, string? op)
    {
        var ct = HttpContext.RequestAborted;
        var result = op switch
        {
            "approve" => await insights.ApproveAsync(id, ActorName, ct),
            "reject" => await insights.RejectAsync(id, ActorName, ct),
            _ => await insights.UpdateAsync(id, Edit, ActorName, ct),
        };
        if (result.Status == Fratak_Shop.Core.Utilities.EnOperationResultStatus.Success)
        {
            SuccessMessage = result.Message;
            // صفحهٔ محصول از کش خروجی بیرون بیاید (خلاصه روی صفحهٔ کالا نمایش داده می‌شود).
            await autopilot.ProcessAsync(SeoEntityKind.Product, id, ct);
        }
        else
        {
            ErrorMessage = result.Message;
        }
        return RedirectToPage(new { id });
    }

    private async Task<(SmartTaskRow? Row, SmartTaskResult? Result)> LoadPendingAsync(Guid productId, Guid taskId, CancellationToken ct)
    {
        var row = await queue.GetAsync(taskId, ct);
        if (row is null || row.Status != SmartTaskStatus.NeedsReview || row.TargetKind != SmartTarget.Product || row.TargetId != productId)
            return (null, null);
        return (row, SmartUi.Result(await queue.GetResultJsonAsync(taskId, ct)));
    }
}
