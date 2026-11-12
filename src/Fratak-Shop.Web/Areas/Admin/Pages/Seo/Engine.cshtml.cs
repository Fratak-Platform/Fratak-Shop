using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.SeoEngine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Seo;

/// <summary>
/// داشبورد «موتور هوشمند سئو» (مرحلهٔ هفتم – فاز ۸): میانگین نمره، توزیع نمره‌ها، ضعیف‌ترین صفحه‌ها، وضعیت منبع‌های
/// تحقیق رقبا، اجراهای اخیر و فهرست کامل نمرهٔ همهٔ صفحه‌ها (زبانهٔ «همهٔ صفحه‌ها» با جست‌وجو و فیلتر).
/// <para>منطق در Core/Services/Seo/Engine/SeoEngineService.cs؛ اجرای دستی به صف سرویس پس‌زمینه می‌رود
/// (Web/Infrastructure/SeoEngine/SeoEngineWorker) تا درخواست صفحه منتظر نماند.</para>
/// </summary>
[AdminPermission(AdminPermissions.SeoManage)]
public class EngineModel(ISeoEngineService engine, IStoreSettingService settings, IAdminDataService data, SeoEngineQueue queue) : AdminPageModel
{
    public const int PageSize = 50;

    /// <summary>زبانه‌ها: overview (نمای کلی) و pages (همهٔ صفحه‌ها).</summary>
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "overview";
    [BindProperty(SupportsGet = true)] public SeoEntityKind? Kind { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    /// <summary>مرتب‌سازی: score (کم‌نمره اول)، views (پربازدید اول)، best (پرنمره اول).</summary>
    [BindProperty(SupportsGet = true)] public string Sort { get; set; } = "score";
    /// <summary>بازهٔ نمره: weak (زیر ۵۰)، medium (۵۰ تا ۷۹)، strong (۸۰+).</summary>
    [BindProperty(SupportsGet = true)] public string? Band { get; set; }

    public SeoDashboard Dashboard { get; private set; } = new();
    public StoreSettingDto Settings { get; private set; } = null!;
    public DateTimeOffset NextRun { get; private set; }
    public bool IsRunning => SeoEngineService.IsRunning;
    public bool RunQueued => queue.HasPending;
    public List<SeoScore> Scores { get; private set; } = [];
    public PagerModel Pager { get; private set; } = new(1, 1, 0);

    public async Task OnGetAsync([FromQuery(Name = "page")] int pageNumber = 1)
    {
        SeoEngineHost.Current = Request.Host.Host;
        Settings = await settings.GetAsync(HttpContext.RequestAborted);
        NextRun = SeoEngineSchedule.NextRun(DateTimeOffset.UtcNow, Settings.SeoEngineRunsPerDay);
        Dashboard = await engine.GetDashboardAsync(HttpContext.RequestAborted);

        if (Tab != "pages")
        {
            Tab = "overview";
            return;
        }

        var query = data.Query<SeoScore>();
        if (Kind.HasValue)
            query = query.Where(s => s.Kind == Kind.Value);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            query = query.Where(s => s.Title.Contains(term) || (s.FocusKeyword != null && s.FocusKeyword.Contains(term)) || s.Path.Contains(term));
        }
        query = Band switch
        {
            "weak" => query.Where(s => s.Score < 50),
            "medium" => query.Where(s => s.Score >= 50 && s.Score < 80),
            "strong" => query.Where(s => s.Score >= 80),
            _ => query,
        };
        query = Sort switch
        {
            "views" => query.OrderByDescending(s => s.Views).ThenBy(s => s.Score),
            "best" => query.OrderByDescending(s => s.Score).ThenByDescending(s => s.Views),
            _ => query.OrderBy(s => s.Score).ThenByDescending(s => s.Views),
        };

        var total = await query.CountAsync(HttpContext.RequestAborted);
        var page = ClampPage(pageNumber, total, PageSize);
        Scores = await query.Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(HttpContext.RequestAborted);
        Pager = new PagerModel(page, Math.Max(1, (int)Math.Ceiling(total / (double)PageSize)), total);
    }

    /// <summary>«همین الان اجرا کن»: درخواست به صف سرویس پس‌زمینه.</summary>
    public IActionResult OnPostRun()
    {
        if (IsRunning)
            ErrorMessage = "یک اجرا همین الان در جریان است؛ پس از پایان، نتیجه در همین صفحه دیده می‌شود.";
        else if (queue.Request())
            SuccessMessage = "اجرا در پس‌زمینه شروع می‌شود (حداکثر تا چند ثانیهٔ دیگر). بعد از پایان، اگر پیشنهاد تازه‌ای باشد اعلان می‌گیرید.";
        else
            ErrorMessage = "درخواست اجرا قبلاً ثبت شده و منتظر شروع است.";
        return BackToList();
    }

    /// <summary>آدرس همین صفحه با تغییر یک پارامتر (فیلترها حفظ می‌شوند).</summary>
    public string UrlWith(params (string Key, string? Value)[] values) => AdminUi.With(Request, values);
}
