using System.Globalization;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Seo;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Seo;

/// <summary>
/// داشبورد «سئوی خودکار» (دور سئوی خودکار، موارد ۱۵۰ تا ۱۶۴ و ۲۰۶ تا ۲۱۰). آدرس: /admin/seo/autopilot
/// <para>چه نشان می‌دهد؟ نمرهٔ سلامت آخرین خزش، پوشش فیلدهای سئو، کارهای باز، تغییرهای این هفته، آزمون‌های عنوان، روند گوگل،
/// صفحه‌های نزدیک صفحهٔ اول، هم‌نوع‌خواری کلمه‌ها، مناسبت‌های پیش رو و خلاصهٔ آخرین اجرای شبانه.</para>
/// <para>چه می‌شود کرد؟ (فقط با مجوز «مدیریت سئو») روشن/خاموش کردن هر بخش، انتخاب حالت «خودکار / فقط پیشنهاد / خاموش»
/// و «همین الان اجرا کن» (کار شبانه در پس‌زمینه صف می‌شود؛ صفحه منتظر نمی‌ماند).</para>
/// <para>مجوز «مشاهدهٔ گزارش‌های سئو» (نقش مشاور سئو) همه‌چیز را می‌بیند ولی دکمه‌های تغییر را نمی‌بیند و POST او رد می‌شود.</para>
/// </summary>
[AdminPermission(AdminPermissions.SeoView, AdminPermissions.SeoManage)]
public class AutopilotModel(
    ISeoAutopilotService autopilot,
    ISeoReportService reports,
    ISeoLinkTargetProvider linkTargets,
    SeoAutopilotQueue queue) : AdminPageModel
{
    /// <summary>تنظیمات فعلی (در POST «ذخیره» از فرم پر می‌شود).</summary>
    [BindProperty] public SeoAutopilotOptions Options { get; set; } = new();

    public SeoAutopilotReport Report { get; private set; } = new();

    /// <summary>کلید IndexNow این سایت (فایل /indexnow/{key}.txt برای اثبات مالکیت به بینگ/یاندکس).</summary>
    public string? IndexNowKey { get; private set; }

    /// <summary>تعداد صفحه‌های تغییرکرده که منتظر پردازش خودکارند.</summary>
    public int Pending => queue.Pending;

    public bool CanManage => Can(AdminPermissions.SeoManage);

    /// <summary>دامنهٔ عمومی برای نمایش آدرس فیدها. مثال: https://shop.fratak.ir</summary>
    public string SiteRoot => $"{Request.Scheme}://{Request.Host.Value}";

    /// <summary>حالت‌های هر بخش با برچسب فارسی (برای فهرست کشویی).</summary>
    public static readonly (SeoAutoMode Mode, string Title)[] Modes =
    [
        (SeoAutoMode.Auto, "خودکار (با تاریخچه و برگشت)"),
        (SeoAutoMode.Suggest, "فقط پیشنهاد در «کارهای سئو»"),
        (SeoAutoMode.Off, "خاموش"),
    ];

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        var config = await autopilot.GetConfigAsync(ct);
        IndexNowKey = config.IndexNowKey;
        Options = SeoAutopilotOptions.Parse(config.OptionsJson);
        Report = await reports.BuildAsync(ct);
    }

    /// <summary>ذخیرهٔ تنظیمات. مقدارهای عددی خارج از بازه در <see cref="SeoAutopilotOptions.Normalized"/> اصلاح می‌شوند.</summary>
    [AdminPermission(AdminPermissions.SeoManage)]
    public async Task<IActionResult> OnPostSaveAsync()
    {
        await autopilot.SaveOptionsAsync(Options.Normalized(), HttpContext.RequestAborted);
        // کش تنظیمات و مقصدهای لینک داخلی (صفحه‌های فروشگاه از آن می‌خوانند) فوراً تازه شود.
        linkTargets.Invalidate();
        SuccessMessage = "تنظیمات سئوی خودکار ذخیره شد و از همین حالا اعمال می‌شود.";
        return BackToList();
    }

    /// <summary>«همین الان اجرا کن»: کار شبانه (پر کردن فیلدها، تصویرها، خزش، فیدها…) در پس‌زمینه صف می‌شود.</summary>
    [AdminPermission(AdminPermissions.SeoManage)]
    public IActionResult OnPostRun()
    {
        if (queue.RequestNightly())
            SuccessMessage = "اجرای کامل سئوی خودکار در پس‌زمینه شروع شد. بسته به تعداد صفحه‌ها چند دقیقه طول می‌کشد؛ نتیجه در همین صفحه و تاریخچه دیده می‌شود.";
        else
            ErrorMessage = "یک اجرا همین الان در صف است؛ چند دقیقهٔ دیگر صفحه را تازه کنید.";
        return BackToList();
    }

    /// <summary>رنگ نشان نمرهٔ سلامت. مثال: ۸۵ ← سبز، ۶۰ ← زرد، ۳۰ ← قرمز.</summary>
    public static string HealthBadge(int? score) => score switch
    {
        null => "a-badge-gray",
        >= 80 => "a-badge-green",
        >= 50 => "a-badge-yellow",
        _ => "a-badge-red",
    };

    /// <summary>عنوان فارسی منبع تغییر. مثال: AutoFill ← «پر کردن خودکار».</summary>
    public static string SourceTitle(Fratak_Shop.Data.Entities.SeoEngine.SeoRevisionSource source) => source switch
    {
        Fratak_Shop.Data.Entities.SeoEngine.SeoRevisionSource.AutoFill => "پر کردن خودکار",
        Fratak_Shop.Data.Entities.SeoEngine.SeoRevisionSource.HtmlFix => "مرتب‌سازی HTML",
        Fratak_Shop.Data.Entities.SeoEngine.SeoRevisionSource.Rewrite => "بازنویسی داده‌محور",
        Fratak_Shop.Data.Entities.SeoEngine.SeoRevisionSource.TitleTest => "آزمون عنوان",
        Fratak_Shop.Data.Entities.SeoEngine.SeoRevisionSource.Rollback => "برگرداندن",
        Fratak_Shop.Data.Entities.SeoEngine.SeoRevisionSource.Slug => "نامک انگلیسی",
        Fratak_Shop.Data.Entities.SeoEngine.SeoRevisionSource.Image => "تصویر",
        Fratak_Shop.Data.Entities.SeoEngine.SeoRevisionSource.Manual => "دستی",
        Fratak_Shop.Data.Entities.SeoEngine.SeoRevisionSource.SmartReview => "توضیحات محصول هوشمند",
        _ => "نامشخص",
    };

    /// <summary>عنوان فارسی شدت کار سئو.</summary>
    public static string SeverityTitle(Fratak_Shop.Data.Entities.SeoEngine.SeoTaskSeverity severity) => severity switch
    {
        Fratak_Shop.Data.Entities.SeoEngine.SeoTaskSeverity.Critical => "خطا",
        Fratak_Shop.Data.Entities.SeoEngine.SeoTaskSeverity.Warning => "هشدار",
        Fratak_Shop.Data.Entities.SeoEngine.SeoTaskSeverity.Info => "نکته",
        _ => "—",
    };

    public static string SeverityBadge(Fratak_Shop.Data.Entities.SeoEngine.SeoTaskSeverity severity) => severity switch
    {
        Fratak_Shop.Data.Entities.SeoEngine.SeoTaskSeverity.Critical => "a-badge-red",
        Fratak_Shop.Data.Entities.SeoEngine.SeoTaskSeverity.Warning => "a-badge-yellow",
        _ => "a-badge-blue",
    };

    /// <summary>برچسب فارسی وضعیت سرعت (خروجی SeoVitals.Rate). ۱ خوب، ۰ نیاز به بهبود، ۱- ضعیف.</summary>
    public static (string Title, string Badge) VitalRating(int rating) => rating switch
    {
        > 0 => ("خوب", "a-badge-green"),
        < 0 => ("ضعیف", "a-badge-red"),
        _ => ("نیاز به بهبود", "a-badge-yellow"),
    };

    /// <summary>نمایش مقدار معیار سرعت. مثال: ("LCP", 2140) ← «۲٫۱ ثانیه»؛ ("CLS", 0.05) ← «0.05».</summary>
    public static string VitalValue(string metric, double value) => metric switch
    {
        "CLS" => value.ToString("0.000", CultureInfo.InvariantCulture),
        "LCP" or "FCP" when value >= 1000 => (value / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " ثانیه",
        _ => Fratak_Shop.Web.Helpers.Format.Number((long)Math.Round(value)) + " میلی‌ثانیه",
    };
}
