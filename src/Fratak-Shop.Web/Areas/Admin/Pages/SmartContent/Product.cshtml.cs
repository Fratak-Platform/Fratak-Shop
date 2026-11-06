using Fratak_Shop.Core.Services.Admin;
using System.Text.Json;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
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
/// <para>دور بیستم: ۱۰ تا ۱۵ عکس نامزد با بزرگ‌نمایی، «انتخاب همه» و «رد همه و جست‌وجوی دوباره» (عکس‌های ردشده دیگر هرگز
/// برای این کالا نمی‌آیند و منابعشان در رتبه‌بندی بعدی پایین می‌روند)؛ پیش‌نمایش «توضیحات محصول» با جایگزین/افزودن/رد؛
/// هر کار لنگر id="task-{id}" دارد تا لینک اعلان مستقیم به همان بخش بیاید.</para>
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
            if (result is not null && (PendingSpecs(result).Any() || result.PendingImages.Count > 0 || HasPendingReview(result)))
                Pending.Add((row, result));
        }
        return Page();
    }

    /// <summary>ردیف‌های مشخصاتی که هنوز تأیید لازم دارند (تکراری‌ها و خودکارها حذف).</summary>
    public static IEnumerable<SpecSuggestion> PendingSpecs(SmartTaskResult result) =>
        result.Specs.Where(s => s.Diff != SpecDiffKind.Same && !s.AutoApplicable);

    /// <summary>آیا این نتیجه پیش‌نمایش «توضیحات محصول» اعمال‌نشده دارد؟</summary>
    public static bool HasPendingReview(SmartTaskResult result) => result.Review is { Applied: false } r && !string.IsNullOrWhiteSpace(r.Html);

    /// <summary>HTML امن پیش‌نمایش (دوباره پاک‌سازی می‌شود؛ ستون نتیجه هم نباید راه XSS باشد).</summary>
    public static string SafePreview(ReviewPreview review) => review.Html.SanitizeText();

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
            "review" => SmartTaskKind.ProductReview,
            _ => SmartTaskKind.ProductComplete,
        };
        await queue.EnqueueAsync(taskKind, SmartTarget.Product, id, subject.Name, SmartPriority.Manual, null, ActorId, ActorName, ct);
        SuccessMessage = $"«{SmartUi.KindTitle(taskKind)}» در صف قرار گرفت و معمولاً کمتر از یک دقیقه طول می‌کشد. صفحه را چند لحظه بعد تازه کنید.";
        return RedirectToPage(new { id });
    }

    /// <summary>
    /// اعمال بازبینی یک کار: ردیف‌های تیک‌خوردهٔ مشخصات، عکس‌های انتخاب‌شده و تصمیم «توضیحات محصول».
    /// <para>عکس: انتخاب‌شده‌ها به کالا اضافه و «تأییدشده» ثبت می‌شوند؛ بقیه «ردشده» ثبت (هرگز دوباره برای این کالا نمی‌آیند) و فایلشان پاک می‌شود.
    /// refetch="true" (دکمهٔ «رد همهٔ عکس‌ها و جست‌وجوی دوباره»): همهٔ عکس‌ها رد و دور بعدی جست‌وجو در صف گذاشته می‌شود.</para>
    /// <para>reviewMode: replace = جایگزین متن فعلی؛ append = افزودن زیر متن فعلی؛ هر چیز دیگر = رد پیش‌نمایش.</para>
    /// </summary>
    public async Task<IActionResult> OnPostApplyAsync(Guid id, Guid taskId, string[]? specs, string[]? imageNames, string? reviewMode, string? refetch)
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
        var again = string.Equals(refetch, "true", StringComparison.OrdinalIgnoreCase);
        var (approved, rejected) = SplitImages(result.PendingImages, again ? null : imageNames);
        var imagesAdded = 0;
        if (approved.Count > 0)
        {
            var alts = approved.Select((img, i) => (img.FileName, Alt: i == 0 ? row.TargetTitle ?? "" : $"{row.TargetTitle} – نمای {PersianText.Digits(i + 1)}")).ToList();
            imagesAdded = await catalog.AddProductImagesAsync(id, alts, ct);
            if (imagesAdded < approved.Count)
                images.DeleteProductFiles(approved.Skip(imagesAdded).Select(f => f.FileName));
        }
        await catalog.RecordImageFeedbackAsync(id, Feedback(approved, rejected), ct);
        images.DeleteProductFiles(rejected.Select(f => f.FileName));

        var reviewNote = await ApplyReviewAsync(id, result.Review, reviewMode, ct);
        if (reviewNote is { Ok: false })
            ErrorMessage = reviewNote.Value.Message;

        if (specsAdded + imagesAdded > 0)
            await autopilot.ProcessAsync(SeoEntityKind.Product, id, ct);
        var parts = $"{Format.Number(specsAdded)} مشخصه و {Format.Number(imagesAdded)} عکس اعمال شد" + (reviewNote is { Ok: true } ok ? "؛ " + ok.Message : "");
        await queue.MarkReviewedAsync(taskId, $"بازبینی {ActorName}: {parts}.", ct);

        if (again)
        {
            var round = result.ImageRound + 1;
            await queue.EnqueueAsync(SmartTaskKind.ProductImages, SmartTarget.Product, id, row.TargetTitle, SmartPriority.Manual,
                JsonSerializer.Serialize(new { round }), ActorId, ActorName, ct);
            SuccessMessage = $"{parts}. {Format.Number(rejected.Count)} عکس رد شد و دیگر هرگز برای این کالا پیشنهاد نمی‌شود؛ جست‌وجوی تازه در صف است و نتیجه‌اش اعلان می‌شود.";
        }
        else
        {
            SuccessMessage = parts + ".";
        }
        return RedirectToPage(new { id });
    }

    /// <summary>رد همهٔ پیشنهادهای یک کار: عکس‌ها «ردشده» ثبت (دیگر نمی‌آیند) و فایل‌های موقت پاک می‌شوند.</summary>
    public async Task<IActionResult> OnPostRejectAsync(Guid id, Guid taskId)
    {
        var ct = HttpContext.RequestAborted;
        var (row, result) = await LoadPendingAsync(id, taskId, ct);
        if (row is not null && result is not null)
        {
            await catalog.RecordImageFeedbackAsync(id, Feedback([], result.PendingImages), ct);
            images.DeleteProductFiles(result.PendingImages.Select(i => i.FileName));
            await queue.MarkReviewedAsync(taskId, $"پیشنهادها توسط {ActorName} رد شد.", ct);
            SuccessMessage = "پیشنهادها رد شد؛ عکس‌های ردشده دیگر برای این کالا پیشنهاد نمی‌شوند.";
        }
        return RedirectToPage(new { id });
    }

    /// <summary>
    /// جدا کردن عکس‌های انتخاب‌شده و ردشده (خالص؛ آزمون‌پذیر). نام‌های ناشناخته (دست‌کاری فرم) نادیده گرفته می‌شوند؛
    /// chosen=null یعنی همه رد. مثال: [a,b,c] و ["b","x"] ← ([b], [a,c]).
    /// </summary>
    public static (List<FoundImage> Approved, List<FoundImage> Rejected) SplitImages(IReadOnlyList<FoundImage> pending, IEnumerable<string>? chosen)
    {
        var picked = new HashSet<string>(chosen ?? [], StringComparer.Ordinal);
        var unique = pending.DistinctBy(i => i.FileName, StringComparer.Ordinal).ToList();
        return (unique.Where(i => picked.Contains(i.FileName)).ToList(), unique.Where(i => !picked.Contains(i.FileName)).ToList());
    }

    /// <summary>بازخورد عکس‌ها برای یادگیری (فقط آن‌هایی که آدرس یا اثر انگشت دارند؛ نتیجه‌های قدیمی صف این‌ها را نداشتند).</summary>
    public static List<ImageFeedbackItem> Feedback(IEnumerable<FoundImage> approved, IEnumerable<FoundImage> rejected) =>
        approved.Select(i => (Image: i, Ok: true)).Concat(rejected.Select(i => (Image: i, Ok: false)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Image.Url) || x.Image.Hash != 0)
            .Select(x => new ImageFeedbackItem(x.Image.Url, x.Image.Hash, x.Image.Source, x.Ok))
            .ToList();

    /// <summary>
    /// اعمال تصمیم «توضیحات محصول». null = این کار پیش‌نمایشی نداشت یا کارشناس رد کرد.
    /// اگر متن صفحه بعد از ساخت پیش‌نمایش عوض شده باشد، چیزی اعمال نمی‌شود (کار کسی پاک نشود).
    /// </summary>
    private async Task<(bool Ok, string Message)?> ApplyReviewAsync(Guid id, ReviewPreview? review, string? mode, CancellationToken ct)
    {
        if (review is not { Applied: false } || string.IsNullOrWhiteSpace(review.Html) || mode is not ("replace" or "append"))
            return null;
        var state = await catalog.GetBodyStateAsync(id, ct);
        if (state is null)
            return (false, "محصول پیدا نشد.");
        if (review.BaseHash is not null && review.BaseHash != PersianText.Hash(state.Html))
            return (false, "متن توضیحات کالا بعد از ساخت این پیش‌نویس عوض شده؛ «توضیحات محصول» را دوباره بزنید تا با متن تازه نوشته شود.");
        var html = mode == "append" && !string.IsNullOrWhiteSpace(state.Html) ? state.Html.TrimEnd() + "\n" + review.Html : review.Html;
        var applied = await autopilot.ApplyBodyAsync(id, html,
            mode == "append" ? "توضیحات محصول هوشمند زیر متن فعلی (تأیید کارشناس)" : "توضیحات محصول هوشمند جایگزین متن قبلی (تأیید کارشناس)", ActorName, ct);
        return applied.IsSuccess()
            ? (true, mode == "append" ? "توضیحات محصول زیر متن فعلی اضافه شد" : "توضیحات محصول جایگزین شد")
            : (false, applied.Message ?? "توضیحات محصول ذخیره نشد.");
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
