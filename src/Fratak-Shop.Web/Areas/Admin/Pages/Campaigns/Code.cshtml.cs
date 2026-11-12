using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Design;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Design;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Campaigns;

/// <summary>
/// دور ۲۹ (#۱۱): «حالت کد» صفحهٔ کمپین – آدرس: /admin/campaigns/code/{id}
/// <para>جایگزین صفحه‌ساز برای کسی که HTML/CSS/JS بلد است: سه ویرایشگر کد (شمارهٔ خط، Tab، تورفتگی خودکار، Ctrl+S = ذخیره؛ رفتار:
/// code-editor.js) و پیش‌نمایش زنده بعد از هر ذخیره.</para>
/// <para>با روشن کردن «حالت کد»، صفحهٔ عمومی همین کد را داخل قاب ایزوله نشان می‌دهد (CampaignFrameEndpoint)؛ خروجی صفحه‌ساز پاک نمی‌شود
/// و با خاموش کردن برمی‌گردد. عنوان، نامک، زمان‌بندی و انتشار همچنان از «تنظیمات صفحه» در صفحه‌ساز است.</para>
/// </summary>
[AdminPermission(AdminPermissions.MarketingManage, AdminPermissions.ContentManage)]
[RequestFormLimits(ValueLengthLimit = 2 * 1024 * 1024)]
public class CodeModel(ILandingPageService landings) : AdminPageModel
{
    public LandingPage Landing { get; private set; } = null!;

    /// <summary>نمونهٔ شروع برای صفحهٔ بدون کد (دکمهٔ «نمونهٔ آماده»).</summary>
    public const string SampleHtml =
        "<section class=\"hero\">\n  <h1>حراج ویژهٔ این هفته</h1>\n  <p>بهترین قیمت‌ها، ارسال سریع</p>\n  <a class=\"cta\" href=\"/search?discounted=true\">دیدن تخفیف‌ها</a>\n</section>\n\n" +
        "<section class=\"products\">\n  <h2>پرتخفیف‌ترین‌ها</h2>\n  [[deals:8]]\n</section>\n";

    public const string SampleCss =
        ".hero {\n  padding: 64px 16px;\n  text-align: center;\n  color: #fff;\n  background: linear-gradient(135deg, #0ea5e9, #6366f1);\n}\n" +
        ".hero h1 { margin: 0 0 8px; font-size: clamp(28px, 5vw, 48px); }\n" +
        ".cta {\n  display: inline-block;\n  margin-top: 16px;\n  padding: 12px 28px;\n  border-radius: 999px;\n  background: #fff;\n  color: #0f172a;\n  font-weight: 700;\n  text-decoration: none;\n}\n" +
        ".products { max-width: 1200px; margin: 0 auto; padding: 32px 16px; }\n";

    public const string SampleJs = "// کد جاوااسکریپت این‌جا (داخل قاب ایزوله اجرا می‌شود؛ به حساب و کوکی کاربر دسترسی ندارد).\n";

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var page = await landings.GetAsync(id, HttpContext.RequestAborted);
        if (page is null)
            return NotFound();
        Landing = page;
        return Page();
    }

    /// <summary>ذخیرهٔ کد و روشن/خاموش کردن حالت کد؛ برگشت به همین صفحه (پیش‌نمایش تازه می‌شود).</summary>
    [AdminPermission(AdminPermissions.MarketingManage, AdminPermissions.ContentManage)]
    public async Task<IActionResult> OnPostAsync(Guid id, bool codeMode, string? html, string? css, string? js)
    {
        var result = await landings.SaveCodeAsync(new LandingCodeInput(id, codeMode, html, css, js), ActorName, HttpContext.RequestAborted);
        ShowResult(result);
        if (result.IsSuccess())
            return LocalRedirect($"/admin/campaigns/code/{id}");

        // خطا (مثلاً کد خیلی بزرگ): همان صفحه با کدی که نوشته شده دوباره نشان داده می‌شود تا کار مدیر از دست نرود.
        var page = await landings.GetAsync(id, HttpContext.RequestAborted);
        if (page is null)
            return NotFound();
        page.CodeMode = codeMode;
        page.CodeHtml = html;
        page.CodeCss = css;
        page.CodeJs = js;
        Landing = page;
        return Page();
    }
}
