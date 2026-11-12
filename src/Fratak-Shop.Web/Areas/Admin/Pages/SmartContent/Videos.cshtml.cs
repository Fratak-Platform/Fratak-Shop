using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.Smart;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Smart;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SmartContent;

/// <summary>
/// دور ۲۹ (#۱۸) – «ویدیوهای پیشنهادی کالا». آدرس: /admin/smart/videos (فیلتر: ?status=new|used|dismissed و ?product={id}).
/// <para>سیستم هر روز برای کالاهای بی‌ویدیو، ویدیوی معرفی همان کالا را در صفحهٔ تأمین‌کننده، آپارات و یوتیوب پیدا می‌کند و این‌جا نشان می‌دهد:
/// منبع، لینک صفحه (برای دیدن/دانلود)، تصویر و امتیاز تطبیق. مدیر بررسی می‌کند و یکی از این‌ها را می‌زند:</para>
/// <list type="bullet">
/// <item>«افزودن از آپارات»: ویدیوی آپارات مستقیم (جاسازی) به کالا اضافه می‌شود.</item>
/// <item>«استفاده شد»: مدیر خودش دانلود و بارگذاری کرده است.</item>
/// <item>«رد»: کالای دیگری است؛ این لینک دیگر پیشنهاد نمی‌شود.</item>
/// </list>
/// GET فقط خواندنی است.
/// </summary>
[AdminPermission(AdminPermissions.ProductsManage)]
public class VideosModel(IVideoSuggestionService suggestions, VideoSuggestionFinder finder) : AdminPageModel
{
    /// <summary>ردیف‌های فهرست فعلی.</summary>
    public List<VideoSuggestionRow> Items { get; private set; } = [];

    /// <summary>وضعیت انتخاب‌شده (پیش‌فرض: تازه).</summary>
    public VideoSuggestionStatus Status { get; private set; } = VideoSuggestionStatus.New;

    /// <summary>فیلتر یک کالا (از صفحهٔ ویرایش کالا).</summary>
    public Guid? ProductId { get; private set; }

    /// <summary>تعداد پیشنهادهای تازه (برای نشان زبانه).</summary>
    public int PendingCount { get; private set; }

    public async Task OnGetAsync(string? status, Guid? product)
    {
        var ct = HttpContext.RequestAborted;
        Status = Parse(status);
        ProductId = product;
        Items = await suggestions.ListAsync(Status, ProductId, ct);
        PendingCount = await suggestions.PendingCountAsync(ct);
    }

    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostUsedAsync(Guid id, Guid? product) =>
        await SetAsync(id, VideoSuggestionStatus.Used, false, product);

    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostDismissAsync(Guid id, Guid? product) =>
        await SetAsync(id, VideoSuggestionStatus.Dismissed, false, product);

    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostAparatAsync(Guid id, Guid? product) =>
        await SetAsync(id, VideoSuggestionStatus.Used, true, product);

    /// <summary>
    /// «جست‌وجوی الان»: با product ← همان کالا؛ بدون آن ← ۳ کالای بی‌ویدیوی بعدی (درخواست‌ها مؤدبانه و با فاصله‌اند؛ ممکن است چند ثانیه طول بکشد).
    /// </summary>
    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostScanAsync(Guid? product)
    {
        var ct = HttpContext.RequestAborted;
        if (product is { } id)
        {
            var found = await finder.ScanProductAsync(id, ct);
            if (found is null)
                return NotFound();
            if (found > 0)
                SuccessMessage = $"{PersianText.Digits(found.Value)} ویدیوی پیشنهادی تازه پیدا شد.";
            else
                ErrorMessage = "ویدیوی تازه‌ای که مطمئن باشیم همین کالاست پیدا نشد (یا قبلاً پیشنهاد شده است).";
        }
        else
        {
            var (saved, products) = await finder.RunAsync(3, ct);
            if (saved > 0)
                SuccessMessage = $"{PersianText.Digits(saved)} ویدیوی پیشنهادی برای {PersianText.Digits(products)} کالا پیدا شد.";
            else
                ErrorMessage = "در این نوبت ویدیوی مطمئنی پیدا نشد؛ جست‌وجوی خودکار روزانه ادامه دارد.";
        }
        return RedirectToPage(new { product });
    }

    private async Task<IActionResult> SetAsync(Guid id, VideoSuggestionStatus status, bool aparat, Guid? product)
    {
        var result = await suggestions.SetStatusAsync(id, status, aparat, HttpContext.RequestAborted);
        ShowResult(result);
        return RedirectToPage(new { product });
    }

    /// <summary>متن وضعیت در آدرس ← enum (نامعتبر ← تازه).</summary>
    public static VideoSuggestionStatus Parse(string? status) => status?.ToLowerInvariant() switch
    {
        "used" => VideoSuggestionStatus.Used,
        "dismissed" => VideoSuggestionStatus.Dismissed,
        _ => VideoSuggestionStatus.New,
    };

    /// <summary>نام نمایشی منبع.</summary>
    public static string SourceTitle(string source) => source switch
    {
        "supplier" => "صفحهٔ تأمین‌کننده",
        "aparat" => "آپارات",
        "youtube" => "یوتیوب",
        _ => source,
    };
}
