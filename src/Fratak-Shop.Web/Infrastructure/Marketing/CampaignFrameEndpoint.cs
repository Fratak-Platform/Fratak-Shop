using System.Text;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Design;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.PageCache;

namespace Fratak_Shop.Web.Infrastructure.Marketing;

/// <summary>
/// دور ۲۹ (#۱۱): «حالت کد» صفحه‌های کمپین – سند HTML کامل از کد خام مدیر، داخل قاب ایزوله.
/// <para>چرا امن است؟</para>
/// <list type="bullet">
/// <item>صفحهٔ /campaign/{slug} این سند را در <c>&lt;iframe sandbox="allow-scripts …"&gt;</c> <b>بدون</b> allow-same-origin می‌گذارد؛
/// پس کد مبدأ جدا (null) دارد: به کوکی، توکن ورود، localStorage و صفحهٔ اصلی سایت دسترسی ندارد.</item>
/// <item>خود پاسخ هم سرآیند CSP با دستور <c>sandbox</c> دارد؛ اگر کسی آدرس قاب را مستقیم باز کند، باز هم مرورگر آن را ایزوله اجرا می‌کند
/// (کد هرگز با مبدأ سایت اجرا نمی‌شود).</item>
/// <item>فقط کارمند بازاریابی/محتوا کد را ذخیره می‌کند؛ قاب فقط داخل همین سایت جاسازی می‌شود (frame-ancestors 'self').</item>
/// </list>
/// <para>کدهای کوتاه کالا ([[deals:8]]، [[product:slug]] …) مثل صفحه‌ساز با قیمت و موجودی امروز پر می‌شوند و استایل‌های سایت
/// (Tailwind، فونت وزیرمتن و کارت کالا) از قبل لینک شده‌اند. ارتفاع قاب با یک پیام کوچک (postMessage) به صفحهٔ اصلی خبر داده می‌شود
/// (landing.js) تا اسکرول دوتایی نداشته باشیم.</para>
/// <para>سرور: با کش صفحات سرو می‌شود (هر تغییر صفحه/کالا کش را خالی می‌کند) ← هزینهٔ بازدید زیاد ناچیز است.</para>
/// </summary>
public static class CampaignFrameEndpoint
{
    /// <summary>آدرس قاب یک کمپین.</summary>
    public static string PathFor(string slug) => $"/campaign/{slug}/frame";

    /// <summary>
    /// اجازه‌های قاب: اسکریپت، پنجرهٔ تازه برای لینک‌های بیرونی، و رفتن صفحهٔ اصلی به لینک فقط با کلیک کاربر
    /// (کد نمی‌تواند خودسرانه بازدیدکننده را جای دیگری ببرد). فرم، دسترسی به مبدأ سایت و پنجرهٔ بازشوی خودکار ممنوع.
    /// </summary>
    public const string SandboxFlags = "allow-scripts allow-popups allow-popups-to-escape-sandbox allow-top-navigation-by-user-activation";

    public static void MapCampaignFrame(IEndpointRouteBuilder app) =>
        app.MapGet("/campaign/{slug}/frame", HandleAsync).CacheOutput(PageCachePolicy.Name).ExcludeFromDescription();

    private static async Task<IResult> HandleAsync(string slug, HttpContext http, ILandingPageService landings, IBuyerGuideService buyerGuide)
    {
        var ct = http.RequestAborted;
        var now = DateTimeOffset.UtcNow;
        var code = await landings.GetCodeAsync(slug, now, ct);
        if (code is null && await StaffPreview.AllowedAsync(http, AdminPermissions.MarketingManage, AdminPermissions.ContentManage))
        {
            // پیش‌نمایش کارمند (کمپین هنوز منتشرنشده): هرگز کش نمی‌شود.
            code = await landings.GetCodeAsync(slug, now, ct, includeHidden: true);
            http.Response.Headers.CacheControl = "no-store, private";
        }
        if (code is null)
            return Results.NotFound();

        var html = await landings.ExpandListsAsync(code.Html, ct);
        var slugs = BlogShortcodes.ProductSlugs(html);
        if (slugs.Count > 0)
            html = BlogShortcodes.Render(html, await buyerGuide.CardsBySlugsAsync(slugs, ct), ImageHelper.Product);

        var self = $"{http.Request.Scheme}://{http.Request.Host}";
        http.Response.Headers["Content-Security-Policy"] = Policy(self);
        http.Response.Headers["X-Robots-Tag"] = "noindex";
        return Results.Content(Document(code.Title, html, code.Css, code.Js), "text/html; charset=utf-8");
    }

    /// <summary>
    /// CSP قاب: اسکریپت و استایل درون‌خطی مجاز (کد خام مدیر)، منابع https مجاز، فرم ممنوع، فقط داخل همین سایت، و دستور sandbox.
    /// «self» صریح (مبدأ سایت) نوشته می‌شود چون سند ایزوله مبدأ null دارد.
    /// </summary>
    public static string Policy(string self) =>
        $"default-src 'none'; img-src {self} data: blob: https:; media-src {self} blob: https:; style-src {self} 'unsafe-inline' https:; " +
        $"font-src {self} data: https:; script-src {self} 'unsafe-inline' https:; connect-src {self} https:; frame-src https:; " +
        $"form-action 'none'; base-uri {self}; frame-ancestors 'self'; sandbox {SandboxFlags}";

    /// <summary>
    /// سند کامل قاب. «&lt;/style» و «&lt;/script» داخل کد شکسته می‌شوند تا کد مدیر از تگ خودش بیرون نزند
    /// (در CSS بی‌معنی است و در JS «&lt;\/script» همان رشته است).
    /// </summary>
    public static string Document(string title, string html, string css, string js)
    {
        var builder = new StringBuilder(html.Length + css.Length + js.Length + 1200);
        builder.Append("<!doctype html><html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            // لینک‌ها به‌طور پیش‌فرض کل صفحه را عوض می‌کنند (نه فقط داخل قاب).
            .Append("<base target=\"_top\">")
            .Append("<title>").Append(System.Net.WebUtility.HtmlEncode(title)).Append("</title>")
            .Append("<link rel=\"stylesheet\" href=\"/assets/css/output.css\">")
            .Append("<link rel=\"stylesheet\" href=\"/assets/css/font.css\">")
            .Append("<link rel=\"stylesheet\" href=\"/assets/css/commerce.css\">")
            .Append("<style>html,body{margin:0;background:transparent}body{font-family:Vazirmatn,Tahoma,sans-serif}img{max-width:100%;height:auto}</style>");
        if (!string.IsNullOrWhiteSpace(css))
            builder.Append("<style>").Append(Escape(css, "</style")).Append("</style>");
        builder.Append("</head><body>").Append(html);
        // خبر دادن ارتفاع به صفحهٔ اصلی (بار اول، بعد از بارگذاری عکس‌ها و هر تغییر اندازه).
        builder.Append("<script>(function(){var last=0;function send(){var h=Math.ceil(document.documentElement.scrollHeight);")
            .Append("if(Math.abs(h-last)>2){last=h;parent.postMessage({fsLpHeight:h},'*');}}")
            .Append("window.addEventListener('load',send);window.addEventListener('resize',send);")
            .Append("if(window.ResizeObserver){new ResizeObserver(send).observe(document.body);}send();})();</script>");
        if (!string.IsNullOrWhiteSpace(js))
            builder.Append("<script>").Append(Escape(js, "</script")).Append("</script>");
        builder.Append("</body></html>");
        return builder.ToString();
    }

    private static string Escape(string code, string closing) =>
        code.Replace(closing, "<\\" + closing[1..], StringComparison.OrdinalIgnoreCase);
}
