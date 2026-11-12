using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Smart;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Smart;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SmartContent;

/// <summary>
/// گزارش «رشد خودکار» (دور ۲۱). آدرس: /admin/smart/growth
/// <para>بازدید یکتای ۱۵ روز اخیر (نمودار میله‌ای ساده)، قضاوت امروز (کم/عادی نسبت به میانهٔ روزهای قبل) و کارهایی که سایت در ۳۰ روز
/// گذشته خودش انجام داده (بهبود سئو، تکمیل کالا، جلو انداختن مقاله، ایدهٔ بنر). اجرای خودکار: SmartWorker.GrowAsync روزی یک بار.</para>
/// <para>دکمهٔ «اجرای دوباره الان» (فقط بازاریابی/سئو): همان کار روزانه را فوراً اجرا می‌کند؛ کارها کوچک‌اند (حداکثر ۲۰ صفحه سئو و ۶ کالا در صف)
/// تا درخواست سنگین نشود.</para>
/// </summary>
[AdminPermission(AdminPermissions.ReportsView, AdminPermissions.MarketingManage, AdminPermissions.SeoManage)]
public class GrowthModel(IGrowthAutopilot growth, ISmartConfigService config) : AdminPageModel
{
    public List<GrowthAction> Actions { get; private set; } = [];

    public List<(DateOnly Day, int Visitors)> Visitors { get; private set; } = [];

    public TrafficVerdict Verdict { get; private set; } = new(false, 0, 0, "");

    public bool Enabled { get; private set; }

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        var today = IranClock.Today(DateTimeOffset.UtcNow);
        var options = await config.GetAsync(ct);
        Enabled = options.Enabled && options.GrowthAutopilot;
        Actions = await growth.RecentAsync(30, ct);
        Visitors = await growth.VisitorsAsync(today, 15, ct);
        var yesterday = Visitors.FirstOrDefault(v => v.Day == today.AddDays(-1)).Visitors;
        Verdict = GrowthAutopilot.Judge(yesterday, Visitors.Where(v => v.Day < today.AddDays(-1)).Select(v => v.Visitors).ToList());
    }

    /// <summary>اجرای فوری کار روزانه (حتی اگر امروز اجرا شده). نتیجه: پیام خلاصه بالای صفحه.</summary>
    [AdminPermission(AdminPermissions.MarketingManage, AdminPermissions.SeoManage)]
    public async Task<IActionResult> OnPostRunAsync()
    {
        var ct = HttpContext.RequestAborted;
        var options = await config.GetAsync(ct);
        if (!options.Enabled || !options.GrowthAutopilot)
        {
            ErrorMessage = "رشد خودکار در تنظیمات محتوای هوشمند خاموش است.";
            return RedirectToPage();
        }
        var done = await SmartWorker.GrowAsync(HttpContext.RequestServices, options.BannerIdeasPerDay, DateTimeOffset.UtcNow, ct, force: true);
        SuccessMessage = done > 0 ? $"اجرا شد؛ {PersianText.Digits(done)} کار انجام یا در صف گذاشته شد." : "اجرا شد؛ کار تازه‌ای لازم نبود.";
        return RedirectToPage();
    }

    /// <summary>عنوان فارسی هر نوع کار برای جدول.</summary>
    public static string KindTitle(GrowthActionKind kind) => kind switch
    {
        GrowthActionKind.DailyRun => "اجرای روزانه",
        GrowthActionKind.SeoLift => "بهبود سئو",
        GrowthActionKind.TrafficDrop => "افت بازدید",
        GrowthActionKind.BlogBoost => "جلو انداختن مقاله",
        GrowthActionKind.BannerIdeas => "ایدهٔ بنر",
        GrowthActionKind.ProductRefresh => "تکمیل کالا",
        _ => kind.ToString(),
    };

    public static string KindColor(GrowthActionKind kind) => kind switch
    {
        GrowthActionKind.TrafficDrop => "red",
        GrowthActionKind.SeoLift => "green",
        GrowthActionKind.BannerIdeas => "purple",
        GrowthActionKind.BlogBoost => "sky",
        GrowthActionKind.ProductRefresh => "yellow",
        _ => "gray",
    };
}
