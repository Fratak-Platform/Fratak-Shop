using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Seo;

/// <summary>
/// «ربات‌ها و سرعت» (دور سئوی خودکار، موارد ۷۶، ۱۳۸ تا ۱۴۹). آدرس: /admin/seo/signals?days=7
/// <para>بالا: ربات‌های موتورهای جست‌وجو (گوگل، بینگ، یاندکس…) – چند بار آمدند، چند بار واقعی (DNS معکوس تأییدشده) و چند بار جعلی،
/// چند خطا دیدند و سرعت پاسخ. وسط: پربازدیدترین و خطادارترین مسیرها برای ربات‌ها (بودجهٔ خزش کجا خرج می‌شود).
/// پایین: سرعت واقعی کاربران (LCP، INP، CLS، FCP، TTFB) به تفکیک نوع صفحه و موبایل/دسکتاپ با معیار رسمی گوگل.</para>
/// </summary>
[AdminPermission(AdminPermissions.SeoView, AdminPermissions.SeoManage)]
public class SignalsModel(ISeoReportService reports) : AdminPageModel
{
    /// <summary>بازهٔ گزارش: ۷ یا ۲۸ روز (عدد دیگر به نزدیک‌ترین مقدار مجاز گرد می‌شود).</summary>
    [BindProperty(SupportsGet = true)] public int Days { get; set; } = 7;

    public SeoSignalsView View { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Days = Days >= 28 ? 28 : 7;
        View = await reports.SignalsAsync(Days, HttpContext.RequestAborted);
    }

    /// <summary>نام فارسی نوع صفحه (همان مقدار rum.js). مثال: product ← «محصول».</summary>
    public static string PageTypeTitle(string type) => type switch
    {
        "home" => "صفحهٔ اصلی",
        "product" => "محصول",
        "category" => "دسته/برند",
        "post" => "مقاله",
        "blog" => "وبلاگ",
        "search" => "جست‌وجو/فهرست",
        "page" => "صفحهٔ محتوا",
        _ => "سایر",
    };
}
