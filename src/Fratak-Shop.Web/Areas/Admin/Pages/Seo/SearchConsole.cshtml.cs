using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Seo.Tools;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.SeoEngine;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Seo;

/// <summary>
/// Google Search Console (مرحلهٔ هفتم – فاز ۸ب). آدرس: /admin/seo/search-console
/// <para>زبانهٔ «گزارش»: کلیک، نمایش، نرخ کلیک و جایگاه ۲۸ روز اخیر در مقایسه با ۲۸ روز قبل، نمودار روزانه،
/// «فرصت‌ها» (صفحه‌هایی که با کمی کار کلیک بیشتری می‌گیرند)، عبارت‌ها و صفحه‌های برتر.
/// زبانهٔ «اتصال»: راهنمای قدم‌به‌قدم ساخت حساب سرویس رایگان گوگل، بارگذاری کلید، پروکسی و آزمایش اتصال.</para>
/// منطق: Core/Services/Seo/Tools/SearchConsoleService.cs (همگام‌سازی در SeoToolsWorker).
/// </summary>
[AdminPermission(AdminPermissions.SeoManage)]
public class SearchConsoleModel(ISearchConsoleService gsc, SeoToolsQueue queue) : AdminPageModel
{
    /// <summary>حداکثر حجم فایل کلید JSON (کلیدهای گوگل حدود ۲ کیلوبایت‌اند).</summary>
    private const long MaxKeyBytes = 32 * 1024;

    /// <summary>زبانه‌ها: report و setup.</summary>
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "report";

    public SearchConsoleStatus Status { get; private set; } = null!;
    public SearchConsoleReport Report { get; private set; } = new();
    public bool IsRunning => SearchConsoleService.IsRunning;
    public bool SyncQueued => queue.IsPending(SeoToolsJob.SearchConsoleSync);
    public string? SuggestedSite { get; private set; }

    public async Task OnGetAsync()
    {
        SeoEngineHost.Current = Request.Host.Host;
        Status = await gsc.GetStatusAsync(HttpContext.RequestAborted);
        SuggestedSite = SearchConsoleSites.Normalize(SeoPaths.BareHost(Request.Host.Host));
        if (Tab != "setup" && !Status.HasKey && Status.QueryRows == 0)
            Tab = "setup";
        if (Tab != "setup")
        {
            Tab = "report";
            Report = await gsc.GetReportAsync(HttpContext.RequestAborted);
        }
    }

    public async Task<IActionResult> OnPostSaveAsync(bool enabled, string? site, string? proxy, string? keyJson, IFormFile? keyFile, bool clearKey)
    {
        var json = keyJson;
        if (keyFile is { Length: > 0 })
        {
            if (keyFile.Length > MaxKeyBytes)
            {
                ErrorMessage = "فایل کلید بیش از حد بزرگ است؛ فایل JSON حساب سرویس گوگل را انتخاب کنید.";
                return LocalRedirect("/admin/seo/search-console?tab=setup");
            }
            using var reader = new StreamReader(keyFile.OpenReadStream());
            json = await reader.ReadToEndAsync(HttpContext.RequestAborted);
        }
        ShowResult(await gsc.SaveAsync(new SearchConsoleSettingsInput(enabled, site, json, clearKey, proxy), HttpContext.RequestAborted));
        return LocalRedirect("/admin/seo/search-console?tab=setup");
    }

    public async Task<IActionResult> OnPostTestAsync()
    {
        ShowResult(await gsc.TestAsync(HttpContext.RequestAborted));
        return LocalRedirect("/admin/seo/search-console?tab=setup");
    }

    /// <summary>همگام‌سازی در پس‌زمینه (دریافت ۵۰ هزار ردیف ممکن است یکی دو دقیقه طول بکشد).</summary>
    public IActionResult OnPostSync()
    {
        if (IsRunning)
            ErrorMessage = "یک همگام‌سازی همین الان در جریان است.";
        else if (queue.Request(SeoToolsJob.SearchConsoleSync))
            SuccessMessage = "دریافت آمار از گوگل در پس‌زمینه شروع شد؛ معمولاً کمتر از یک دقیقه. بعد صفحه را تازه کنید.";
        else
            ErrorMessage = "درخواست همگام‌سازی قبلاً ثبت شده است.";
        return BackToList();
    }

    public async Task<IActionResult> OnPostSitemapAsync()
    {
        ShowResult(await gsc.SubmitSitemapAsync(HttpContext.RequestAborted));
        return BackToList();
    }

    /// <summary>درصد با یک رقم اعشار. مثال: 0.034 → «۳٫۴٪».</summary>
    public static string Percent(double ratio) => SearchConsoleService.Percent(ratio);

    /// <summary>جایگاه با یک رقم اعشار. مثال: 7.46 → «۷٫۵».</summary>
    public static string Position(double position) => position <= 0 ? "—" : PersianText.Decimal1(position);

    /// <summary>
    /// تغییر نسبت به دورهٔ قبل. lowerIsBetter برای جایگاه (عدد کمتر = بهتر).
    /// خروجی: (متن، خوب است؟) یا null اگر دورهٔ قبل داده‌ای نداشت.
    /// </summary>
    public static (string Text, bool Good)? Change(double current, double previous, bool lowerIsBetter = false)
    {
        if (previous <= 0)
            return null;
        var ratio = (current - previous) / previous;
        var good = lowerIsBetter ? ratio < 0 : ratio > 0;
        var sign = ratio > 0 ? "+" : ratio < 0 ? "−" : "";
        return (sign + PersianText.Decimal1(Math.Abs(ratio) * 100) + "٪", good);
    }

    public static string OpportunityTitle(GscOpportunityKind kind) => kind switch
    {
        GscOpportunityKind.StrikingDistance => "نزدیک صفحهٔ اول",
        GscOpportunityKind.LowCtr => "نرخ کلیک کم",
        GscOpportunityKind.KeywordMismatch => "کلمهٔ کلیدی ناهمخوان",
        _ => kind.ToString(),
    };

    public static string OpportunityBadge(GscOpportunityKind kind) => kind switch
    {
        GscOpportunityKind.StrikingDistance => "a-badge-green",
        GscOpportunityKind.LowCtr => "a-badge-yellow",
        _ => "a-badge-blue",
    };
}
