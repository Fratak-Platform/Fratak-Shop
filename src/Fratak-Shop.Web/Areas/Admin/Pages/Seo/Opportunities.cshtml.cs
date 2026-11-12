using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Seo;

/// <summary>
/// دور ۲۴ (پیشنهادهای ۴۸ تا ۵۰): «فرصت‌های سئو». آدرس: /admin/seo/opportunities?tab=searches
/// <para>سه زبانه: «کم‌بازدید» (کالا/مقالهٔ قدیمی بی‌بازدید با دلیل‌های قابل اصلاح)، «جست‌وجوهای بی‌نتیجه» (با دکمهٔ یک‌کلیکی
/// «افزودن مترادف» وقتی نزدیک‌ترین کالا/دسته پیدا شده) و «هم‌نوع‌خواری کلمه» (چند صفحه برای یک کلمه).
/// مشاور سئو (SeoView) فقط می‌بیند؛ افزودن مترادف نیاز به SeoManage دارد. منطق: Core/Services/Seo/Autopilot/SeoOpportunityService.cs.</para>
/// </summary>
[AdminPermission(AdminPermissions.SeoView, AdminPermissions.SeoManage)]
public class OpportunitiesModel(ISeoOpportunityService opportunities) : AdminPageModel
{
    /// <summary>زبانه‌ها: low (پیش‌فرض)، searches، cannibal.</summary>
    public static readonly (string Key, string Title)[] Tabs =
    [
        ("low", "صفحه‌های کم‌بازدید"),
        ("searches", "جست‌وجوهای بی‌نتیجه"),
        ("cannibal", "هم‌نوع‌خواری کلمه"),
    ];

    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "low";

    public IReadOnlyList<LowTrafficRow> LowTraffic { get; private set; } = [];
    public IReadOnlyList<SearchGapRow> SearchGaps { get; private set; } = [];
    public IReadOnlyList<KeywordClash> Cannibal { get; private set; } = [];

    public async Task OnGetAsync()
    {
        if (!Tabs.Any(t => t.Key == Tab))
            Tab = "low";
        var ct = HttpContext.RequestAborted;
        switch (Tab)
        {
            case "searches":
                SearchGaps = await opportunities.SearchGapsAsync(cancellationToken: ct);
                break;
            case "cannibal":
                Cannibal = await opportunities.CannibalizationAsync(cancellationToken: ct);
                break;
            default:
                LowTraffic = await opportunities.LowTrafficAsync(cancellationToken: ct);
                break;
        }
    }

    /// <summary>افزودن مجموعهٔ مترادف پیشنهادی (مثلاً «ایرپاد, هدفون بی سیم»)؛ ورودی دوباره در سرویس اعتبارسنجی می‌شود.</summary>
    [AdminPermission(AdminPermissions.SeoManage)]
    public async Task<IActionResult> OnPostSynonymAsync(string? terms)
    {
        if (SeoOpportunityService.CleanSynonym(terms) is null)
            ErrorMessage = "مترادف نامعتبر است: دست‌کم دو واژهٔ متفاوت، بدون نشانه‌های خاص.";
        else if (await opportunities.AddSynonymAsync(terms!, HttpContext.RequestAborted))
            SuccessMessage = "مترادف ثبت شد؛ از همین حالا جست‌وجوی این عبارت نتیجه می‌دهد.";
        else
            ErrorMessage = "این مترادف قبلاً ثبت شده است.";
        return RedirectToPage(new { tab = "searches" });
    }
}
