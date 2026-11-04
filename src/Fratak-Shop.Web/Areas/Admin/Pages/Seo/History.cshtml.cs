using System.Text.Json;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Seo;

/// <summary>
/// «تاریخچهٔ سئو» (دور سئوی خودکار، موارد ۱۶۵ تا ۱۷۶: ضدجریمه و برگشت‌پذیری). آدرس: /admin/seo/history
/// <para>هر تغییری که سئوی خودکار (یا مدیر از مسیر سئو) روی یک صفحه داده با «قبل» و «بعد» ثبت است؛ «برگرداندن» مقدار قبلی را
/// دوباره می‌نشاند (خودِ برگرداندن هم یک ردیف تاریخچه است). پایین صفحه: آزمون‌های عنوان و نتیجهٔ هرکدام.</para>
/// </summary>
[AdminPermission(AdminPermissions.SeoView, AdminPermissions.SeoManage)]
public class HistoryModel(ISeoReportService reports, ISeoAutopilotService autopilot, ISeoLinkTargetProvider linkTargets) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }

    public PagedList<SeoRevision> Revisions { get; private set; } = new([], 0, 1, 30);
    public PagerModel Pager { get; private set; } = new(1, 1, 0);
    public IReadOnlyList<SeoTitleTest> Tests { get; private set; } = [];
    public bool CanManage => Can(AdminPermissions.SeoManage);

    public async Task OnGetAsync([FromQuery(Name = "page")] int pageNumber = 1)
    {
        var ct = HttpContext.RequestAborted;
        Revisions = await reports.RevisionsAsync(Search, ClampPage(pageNumber), ct);
        Pager = new PagerModel(Revisions.Page, Math.Max(1, (int)Math.Ceiling(Revisions.Total / (double)Revisions.Take)), Revisions.Total);
        Tests = await reports.TitleTestsAsync(20, ct);
    }

    /// <summary>برگرداندن یک تغییر (مقدارهای «قبل» دوباره ذخیره می‌شوند).</summary>
    [AdminPermission(AdminPermissions.SeoManage)]
    public async Task<IActionResult> OnPostRollbackAsync(Guid id)
    {
        var result = await autopilot.RollbackAsync(id, ActorName, HttpContext.RequestAborted);
        if (result.IsSuccess())
            linkTargets.Invalidate();
        ShowResult(result);
        return BackToList();
    }

    /// <summary>
    /// خلاصهٔ خوانای «قبل/بعد» یک تغییر. ورودی JSON ساده‌ای مثل <c>{"Title":"…","MetaDescription":"…"}</c> است.
    /// مثال خروجی: [("Title", "قیمت گوشی", "خرید گوشی سامسونگ")]. مقدار طولانی به ۲۰۰ نویسه کوتاه می‌شود.
    /// </summary>
    public static IReadOnlyList<(string Field, string? Before, string? After)> Diff(string beforeJson, string afterJson)
    {
        var before = Read(beforeJson);
        var after = Read(afterJson);
        return before.Keys.Union(after.Keys)
            .Select(key => (key, before.GetValueOrDefault(key), after.GetValueOrDefault(key)))
            .Where(x => !string.Equals(x.Item2, x.Item3, StringComparison.Ordinal))
            .Select(x => (x.key, Short(x.Item2), Short(x.Item3)))
            .ToList();

        static Dictionary<string, string?> Read(string json)
        {
            var map = new Dictionary<string, string?>(StringComparer.Ordinal);
            try
            {
                using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return map;
                foreach (var property in document.RootElement.EnumerateObject())
                    map[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.Null => null,
                        JsonValueKind.String => property.Value.GetString(),
                        _ => property.Value.GetRawText(),
                    };
            }
            catch (JsonException)
            {
                // دادهٔ خراب: فقط چیزی نمایش داده نمی‌شود.
            }

            return map;
        }

        static string? Short(string? value)
        {
            if (value is null)
                return null;
            var plain = Regex.Replace(value, "<[^>]+>", " ", RegexOptions.None, TimeSpan.FromMilliseconds(100)).Trim();
            return plain.Length > 200 ? plain[..200] + "…" : plain;
        }
    }

    public static string TestStatus(SeoTitleTestStatus status) => status switch
    {
        SeoTitleTestStatus.Running => "در جریان",
        SeoTitleTestStatus.Won => "موفق (ماند)",
        SeoTitleTestStatus.Lost => "ناموفق (برگشت)",
        SeoTitleTestStatus.Inconclusive => "بی‌نتیجه",
        SeoTitleTestStatus.Cancelled => "لغو",
        _ => "—",
    };

    public static string TestBadge(SeoTitleTestStatus status) => status switch
    {
        SeoTitleTestStatus.Won => "a-badge-green",
        SeoTitleTestStatus.Lost => "a-badge-red",
        SeoTitleTestStatus.Running => "a-badge-sky",
        _ => "a-badge-gray",
    };
}
