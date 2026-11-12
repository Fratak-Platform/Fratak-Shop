using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Reports;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Reports;

/// <summary>
/// صفحهٔ «منبع ورودی و کمپین‌ها» (/admin/reports/campaigns، مرحلهٔ هفتم – فاز ۷):
/// بازدیدکننده، سفارش قطعی، مبلغ و نرخ تبدیل هر منبع (گوگل، اینستاگرام، ترب، …) یا هر کمپین UTM،
/// به‌همراه «سازندهٔ لینک کمپین» برای استوری، پیامک، بنر و همکاری در فروش.
/// سفارش به «آخرین منبع غیرمستقیم» ۳۰ روز اخیر بازدیدکننده نسبت داده می‌شود (کوکی fr_src).
/// </summary>
[AdminPermission(AdminPermissions.ReportsView, AdminPermissions.FinanceView, AdminPermissions.MarketingManage)]
public class CampaignsModel(IAnalyticsReportService reports) : ReportPageModel
{
    public override string BasePath => "/admin/reports/campaigns";

    /// <summary>by=campaign → جدا کردن هر کمپین؛ وگرنه فقط منبع و نوع.</summary>
    [BindProperty(SupportsGet = true, Name = "by")] public string? GroupBy { get; set; }

    // ── سازندهٔ لینک (فرم GET) ─────────────────────────────────────────
    [BindProperty(SupportsGet = true, Name = "url")] public string? LinkUrl { get; set; }
    [BindProperty(SupportsGet = true, Name = "src")] public string? LinkSource { get; set; }
    [BindProperty(SupportsGet = true, Name = "med")] public string? LinkMedium { get; set; }
    [BindProperty(SupportsGet = true, Name = "cmp")] public string? LinkCampaign { get; set; }
    [BindProperty(SupportsGet = true, Name = "cnt")] public string? LinkContent { get; set; }

    public bool ByCampaign => GroupBy == "campaign";
    public List<TrafficRow> Rows { get; private set; } = [];

    /// <summary>لینک ساخته‌شده یا null.</summary>
    public string? BuiltLink { get; private set; }

    /// <summary>پیام خطای سازندهٔ لینک (مثلاً آدرس نامعتبر).</summary>
    public string? LinkError { get; private set; }

    /// <summary>پیشنهادهای آماده برای «منبع» و «نوع» در فرم.</summary>
    public static readonly string[] SuggestedSources = ["instagram", "telegram", "whatsapp", "bale", "eitaa", "rubika", "sms", "email", "push", "torob", "aparat", "youtube"];
    public static readonly string[] SuggestedMediums = ["social", "story", "post", "sms", "email", "banner", "cpc", "affiliate", "push", "comparison"];

    public async Task OnGetAsync()
    {
        ResolvePeriod();
        Rows = await reports.GetCampaignsAsync(Period.Current, ByCampaign, HttpContext.RequestAborted);

        if (!string.IsNullOrWhiteSpace(LinkUrl) || !string.IsNullOrWhiteSpace(LinkSource))
        {
            // آدرس نسبی (مثلاً /product/x) به دامنهٔ همین سایت کامل می‌شود.
            var url = LinkUrl?.Trim();
            if (url is not null && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal))
                url = $"{Request.Scheme}://{Request.Host}{url}";
            else if (string.IsNullOrEmpty(url))
                url = $"{Request.Scheme}://{Request.Host}/";
            BuiltLink = TrafficRules.BuildCampaignUrl(url, LinkSource, LinkMedium, LinkCampaign, LinkContent);
            if (BuiltLink is null)
                LinkError = string.IsNullOrWhiteSpace(LinkSource) || TrafficRules.Clean(LinkSource, 60) is null
                    ? "«منبع» را وارد کنید (مثلاً instagram)."
                    : "آدرس صفحه نامعتبر است؛ با http یا https یا / شروع شود.";
        }
    }
}
