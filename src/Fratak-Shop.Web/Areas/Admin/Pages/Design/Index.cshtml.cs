using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Blogs;
using Fratak_Shop.Core.Services.Design;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Design;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Design;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Design;

/// <summary>
/// استودیوی طراحی (دور ۲۱). آدرس: /admin/design
/// <list type="bullet">
/// <item><b>زبانه‌ها:</b> پیشنهادی (ایده‌های هوشمند منتظر تأیید) / پیش‌نویس / منتشرشده / ردشده.</item>
/// <item><b>کارت ایده:</b> پیش‌نمایش، دلیل ایده («۵ کالای پرتخفیف این هفته …»)، «تأیید و انتشار» (بنر فعال در جایگاه درست)، «ویرایش»، «رد».</item>
/// <item><b>طرح تازه:</b> اندازهٔ هر جایگاه از پیش تنظیم است (اسلایدر ۱۹۲۰×۶۴۰ + موبایل، بنر عریض، کارت تبلیغ، کاور مقاله، پست، استوری)؛
/// قالب (چیدمان + پالت) یا صفحهٔ خالی؛ «اندازهٔ دلخواه» با عرض و ارتفاع آزاد.</item>
/// <item><b>کاور مقاله:</b> از ویرایش مقاله با <c>?preset=blog-cover&amp;post={id}</c> می‌آید؛ فرم از پیش انتخاب‌شده است و بعد از ساخت،
/// ویرایشگر دکمهٔ «گذاشتن روی مقاله» دارد.</item>
/// <item><b>«ساخت ایده الان»</b> همان کار روزانهٔ خودکار است (۳ ایده؛ ایدهٔ تکراری یا ردشده نمی‌آید).</item>
/// </list>
/// <para>مجوز: بازاریابی، محتوا یا انتشار مقاله. انتشار بنر فقط با «مدیریت بازاریابی/محتوا»؛ GET هیچ تغییری نمی‌دهد.</para>
/// </summary>
[AdminPermission(AdminPermissions.MarketingManage, AdminPermissions.ContentManage, AdminPermissions.BlogPublish)]
public class IndexModel(IDesignService designs, DesignStudio studio, IPostService posts,
    Fratak_Shop.Core.Services.Orders.IStaffOrderService staffOrders, Fratak_Shop.Core.Services.Marketing.ICampaignOfferService campaigns) : AdminPageModel
{
    public List<DesignListItem> Items { get; private set; } = [];

    /// <summary>دور ۲۴: قاعده‌های کمپین اخیر (پیش‌نویس/فعال/تمام‌شده) با امکان لغو.</summary>
    public List<Fratak_Shop.Core.Services.Marketing.CampaignOfferItem> Offers { get; private set; } = [];

    /// <summary>زبانهٔ فعال (null = همه به‌جز ردشده‌ها).</summary>
    public DesignStatus? Status { get; private set; }

    public int Pending { get; private set; }

    /// <summary>پیش‌تنظیم انتخاب‌شده در فرم «طرح تازه» (از آدرس).</summary>
    public string SelectedPreset { get; private set; } = DesignPresets.Hero;

    /// <summary>مقالهٔ مقصد کاور (از ویرایش مقاله).</summary>
    public Guid? PostId { get; private set; }

    public string? PostTitle { get; private set; }

    public bool CanPublishBanner => CanAny(AdminPermissions.MarketingManage, AdminPermissions.ContentManage);

    public async Task OnGetAsync(string? status, string? preset, Guid? post)
    {
        var ct = HttpContext.RequestAborted;
        Pending = await designs.PendingIdeasAsync(ct);
        Status = Enum.TryParse<DesignStatus>(status, true, out var s) && Enum.IsDefined(s) ? s : Pending > 0 && status is null ? DesignStatus.Suggested : null;
        var list = await designs.ListAsync(Status, null, 90, ct);
        Items = Status is null ? list.Where(d => d.Status != DesignStatus.Rejected).ToList() : list;
        if (CanPublishBanner)
            Offers = (await campaigns.ListAsync(12, ct))
                .Where(o => o.Status is CampaignOfferStatus.Draft or CampaignOfferStatus.Active || o.CreatedAt > DateTimeOffset.UtcNow.AddDays(-30))
                .ToList();
        if (DesignPresets.Find(preset) is { } p)
            SelectedPreset = p.Key;
        if (post is { } postId && await posts.GetPostByAsync(postId) is { } found)
        {
            PostId = postId;
            PostTitle = found.Title;
            SelectedPreset = DesignPresets.BlogCover;
        }
    }

    /// <summary>
    /// طرح تازه ← ویرایشگر. «layout» خالی یا «blank» = صفحهٔ خالی. اندازهٔ دلخواه فقط برای «free» (۲۰۰ تا ۴۰۰۰ پیکسل).
    /// کاور مقاله: عنوان مقاله تیتر نمونهٔ طرح می‌شود.
    /// </summary>
    public async Task<IActionResult> OnPostCreateAsync(string preset, string? layout, string? palette, int? width, int? height, string? title, Guid? post)
    {
        var ct = HttpContext.RequestAborted;
        string? postTitle = null;
        if (post is { } postId)
            postTitle = (await posts.GetPostByAsync(postId))?.Title;
        var result = await studio.CreateAsync(preset, layout, palette, width, height, string.IsNullOrWhiteSpace(title) ? postTitle : title, postTitle, ActorName, ct);
        if (!result.IsSuccess())
        {
            ErrorMessage = result.Message;
            return BackToList();
        }
        var url = $"/admin/design/editor/{result.Data}";
        if (postTitle is not null)
            url += $"?post={post}";
        return LocalRedirect(url);
    }

    /// <summary>تأیید ایده: بنر فعال در جایگاه پیش‌فرض طرح (اسلایدر اصلی/بنر عریض/کارت تبلیغ) با لینک و متن ایده.</summary>
    public async Task<IActionResult> OnPostApproveAsync(Guid id)
    {
        if (!CanPublishBanner)
            return Forbid();
        ShowResult(await designs.PublishBannerAsync(id, BannerPosition.None, null, null, ActorName, HttpContext.RequestAborted));
        return BackToList();
    }

    public async Task<IActionResult> OnPostRejectAsync(Guid id)
    {
        ShowResult(await designs.RejectAsync(id, ActorName, HttpContext.RequestAborted));
        return BackToList();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        ShowResult(await studio.DeleteAsync(id, HttpContext.RequestAborted));
        return BackToList();
    }

    /// <summary>
    /// دور ۲۳ – «ایده ← طرح»: یک جملهٔ کوتاه (مثل «حراج هدفون یلدا» یا «کارت هدیه») ← ۳ طرح پیشنهادی کامل در اندازهٔ جایگاه انتخابی.
    /// هشدارها (مثلاً ادعای تخفیف بیشتر از واقعیت) به مدیر نشان داده می‌شود. چیزی منتشر نمی‌شود.
    /// </summary>
    /// <remarks>دور ۲۴: <paramref name="productId"/> = کالای انتخاب‌شده از فهرست؛ درصد/«امتیاز n برابر» در متن ← قاعدهٔ واقعی پیش‌نویس.</remarks>
    public async Task<IActionResult> OnPostBriefAsync(string? brief, string? place, Guid? productId)
    {
        if (!CanPublishBanner)
            return Forbid();
        var result = await studio.BuildFromBriefAsync(brief, place, ActorName, HttpContext.RequestAborted, productId);
        if (!result.IsSuccess())
        {
            ErrorMessage = result.Message;
            return LocalRedirect("/admin/design#brief");
        }
        var warnings = result.Data.Warnings.Count == 0 ? "" : " " + string.Join(" ", result.Data.Warnings);
        SuccessMessage = result.Message + " بهترین را منتشر کنید (اگر چند نسخه منتشر شود، خودکار آزمون A/B می‌شوند)." + warnings;
        return LocalRedirect("/admin/design?status=Suggested");
    }

    /// <summary>
    /// دور ۲۴: جست‌وجوی زندهٔ کالا برای «انتخاب کالا» در فرم ایده (فقط کالای فعال و موجود؛ JSON).
    /// نمونه: <c>[{"id":"…","name":"هدفون X","price":450000,"finalPrice":420000,"stock":12,"image":"/uploads/…"}]</c>
    /// </summary>
    public async Task<IActionResult> OnGetProductsAsync(string? q)
    {
        if (!CanPublishBanner)
            return Forbid();
        var items = await staffOrders.SearchProductsAsync(q);
        return new JsonResult(items.Select(p => new
        {
            id = p.Id,
            name = p.Name,
            price = p.Price,
            finalPrice = p.FinalPrice,
            stock = p.Stock,
            image = ImageHelper.Sized(ImageHelper.Product(p.Image), 160),
        }));
    }

    /// <summary>دور ۲۴: لغو پیش‌نویس کمپین (کوپنش حذف) یا توقف فوری کمپین فعال.</summary>
    public async Task<IActionResult> OnPostCancelOfferAsync(Guid id)
    {
        if (!CanPublishBanner)
            return Forbid();
        ShowResult(await campaigns.CancelAsync(id, ActorName, HttpContext.RequestAborted));
        return LocalRedirect("/admin/design#campaigns");
    }

    /// <summary>ساخت ۳ ایدهٔ تازه همین حالا (همان کار روزانه).</summary>
    public async Task<IActionResult> OnPostSuggestAsync()
    {
        var count = await studio.SuggestIdeasAsync(3, HttpContext.RequestAborted);
        if (count > 0)
            SuccessMessage = $"{Format.Number(count)} ایدهٔ تازه ساخته شد.";
        else
            ErrorMessage = "ایدهٔ تازه‌ای پیدا نشد (ایده‌های این هفته قبلاً ساخته یا رد شده‌اند، یا کالای موجود با عکس کم است).";
        return LocalRedirect("/admin/design?status=Suggested");
    }
}
