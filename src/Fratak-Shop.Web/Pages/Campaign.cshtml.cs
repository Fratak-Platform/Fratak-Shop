using Fratak_Shop.Core.DTOs.Blogs.Posts.Query;
using Fratak_Shop.Core.Services.Design;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// صفحهٔ عمومی کمپین (دور ۲۱): /campaign/{slug} — خروجی صفحه‌ساز پنل.
/// <list type="bullet">
/// <item>HTML و CSS هنگام ذخیره پاک‌سازی شده‌اند (بدون اسکریپت/رویداد؛ CSS محدود به «.fs-lp»)؛ این‌جا فقط کدهای کوتاه کالا با قیمت و
/// موجودی امروز جایگزین می‌شوند (همان کارت مقاله‌ها).</item>
/// <item>هدر/فوتر سایت طبق گزینهٔ صفحه نمایش داده یا پنهان می‌شود (کلاس body).</item>
/// <item>کمپین تمام‌شده: صفحه ۴۰۴ نمی‌دهد (لینک‌های پخش‌شده در شبکه‌های اجتماعی نمی‌سوزند)، پیام «به پایان رسید» با لینک فروشگاه
/// نشان داده می‌شود و noindex می‌گیرد؛ صفحهٔ غیرفعال یا هنوز شروع‌نشده ← ۴۰۴.</item>
/// <item>با کش صفحات سرو می‌شود (هر تغییر صفحه یا کالا کش را تازه می‌کند) ← بار سرور در ۱۰ هزار بازدید روزانه ناچیز.</item>
/// </list>
/// </summary>
[Microsoft.AspNetCore.OutputCaching.OutputCache(PolicyName = Fratak_Shop.Web.Infrastructure.PageCache.PageCachePolicy.Name)]
public class CampaignModel(ILandingPageService landings, IBuyerGuideService buyerGuide, IPageMetaService pageMeta) : ShopPageModel
{
    public LandingView Landing { get; private set; } = null!;

    /// <summary>HTML نهایی (پاک‌شده هنگام ذخیره + کارت کالاها).</summary>
    public string BodyHtml { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        var ct = HttpContext.RequestAborted;
        var landing = await landings.GetPublicAsync(slug, DateTimeOffset.UtcNow, ct);
        if (landing is null)
            return NotFound();
        Landing = landing;

        if (!landing.Ended)
        {
            var html = await landings.ExpandListsAsync(landing.Html, ct);
            var slugs = BlogShortcodes.ProductSlugs(html);
            BodyHtml = slugs.Count == 0 ? html : BlogShortcodes.Render(html, await buyerGuide.CardsBySlugsAsync(slugs, ct), ImageHelper.Product);
        }

        var classes = new List<string>();
        if (!landing.ShowHeader)
            classes.Add("fs-no-header");
        if (!landing.ShowFooter)
            classes.Add("fs-no-footer");
        if (classes.Count > 0)
            ViewData["BodyClass"] = string.Join(' ', classes);

        var seo = new SeoMetaDataDto
        {
            Title = landing.MetaTitle,
            MetaDescription = landing.MetaDescription,
            NoIndex = landing.Ended ? true : null,
        };
        pageMeta.Apply(ViewData, seo, landing.Title, landing.MetaDescription, landing.ImageUrl);
        pageMeta.AddBreadcrumbs(ViewData, (landing.Title, null));
        return Page();
    }
}
