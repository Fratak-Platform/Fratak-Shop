using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.SeoEngine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Seo;

/// <summary>یک پیشنهاد آمادهٔ نمایش (JSONها یک بار اینجا خوانده می‌شوند).</summary>
public sealed record SeoSuggestionView(
    SeoSuggestion Entity,
    SeoProposal Proposal,
    SeoProposal Current,
    IReadOnlyList<string> Reasons,
    SeoResearchSummary? Research);

/// <summary>
/// صف بررسی پیشنهادهای موتور سئو (مرحلهٔ هفتم – فاز ۸). کار اصلی کارشناس سئو همین صفحه است:
/// <list type="bullet">
/// <item>هر پیشنهاد یک کارت: نمرهٔ قبل → بعد، مقایسهٔ «فعلی / پیشنهادی» هر بخش، پیش‌نمایش نتیجهٔ گوگل و دلیل‌ها.</item>
/// <item>تأیید: فقط بخش‌های تیک‌خورده روی صفحه نوشته می‌شوند (نامک همیشه با ریدایرکت ۳۰۱).</item>
/// <item>رد: بخش‌های بد را علامت بزنید و دلیل بنویسید؛ نسخهٔ تازه بلافاصله ساخته و در همین صف گذاشته می‌شود.</item>
/// <item>تأیید گروهی برای وقتی که چند پیشنهاد را دیده‌اید و همه درست‌اند.</item>
/// </list>
/// منطق در Core/Services/Seo/Engine/SeoEngineService.cs (ApproveAsync / RejectAsync).
/// </summary>
[AdminPermission(AdminPermissions.SeoManage)]
public class ReviewModel(ISeoEngineService engine, IAdminDataService data, IStoreSettingService settings, AdminBadgeService badges) : AdminPageModel
{
    public const int PageSize = 10;

    /// <summary>دلیل‌های آماده برای رد (با یک کلیک در کادر دلیل نوشته می‌شوند؛ موتور سبک نسخهٔ بعد را از آن می‌فهمد).</summary>
    public static readonly string[] QuickReasons =
    [
        "خیلی طولانی است",
        "خیلی کوتاه است",
        "زیادی تبلیغاتی است",
        "اطلاعات نادرست دارد",
        "لحن مناسب نیست",
        "کلمهٔ کلیدی مناسب نیست",
    ];

    /// <summary>وضعیت: pending (منتظر)، applied (اعمال‌شده)، rejected (ردشده)، other (کهنه/جایگزین‌شده).</summary>
    [BindProperty(SupportsGet = true)] public string Status { get; set; } = "pending";
    [BindProperty(SupportsGet = true)] public SeoEntityKind? Kind { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    /// <summary>شناسهٔ پیشنهادی که باید بالای صفحه بیاید (بعد از «ساخت پیشنهاد» یا رد).</summary>
    [BindProperty(SupportsGet = true)] public Guid? Focus { get; set; }

    public List<SeoSuggestionView> Items { get; private set; } = [];
    public PagerModel Pager { get; private set; } = new(1, 1, 0);
    public Dictionary<string, int> Counts { get; private set; } = [];
    public string SiteName { get; private set; } = "";

    public async Task OnGetAsync([FromQuery(Name = "page")] int pageNumber = 1)
    {
        SeoEngineHost.Current = Request.Host.Host;
        SiteName = (await settings.GetAsync(HttpContext.RequestAborted)).SiteName ?? "";

        var all = data.Query<SeoSuggestion>();
        Counts = new Dictionary<string, int>
        {
            ["pending"] = await all.CountAsync(s => s.Status == SeoSuggestionStatus.Pending),
            ["applied"] = await all.CountAsync(s => s.Status == SeoSuggestionStatus.Applied),
            ["rejected"] = await all.CountAsync(s => s.Status == SeoSuggestionStatus.Rejected),
            ["other"] = await all.CountAsync(s => s.Status == SeoSuggestionStatus.Stale || s.Status == SeoSuggestionStatus.Superseded),
        };

        var query = Status switch
        {
            "applied" => all.Where(s => s.Status == SeoSuggestionStatus.Applied),
            "rejected" => all.Where(s => s.Status == SeoSuggestionStatus.Rejected),
            "other" => all.Where(s => s.Status == SeoSuggestionStatus.Stale || s.Status == SeoSuggestionStatus.Superseded),
            _ => all.Where(s => s.Status == SeoSuggestionStatus.Pending),
        };
        if (Status is not ("applied" or "rejected" or "other"))
            Status = "pending";
        if (Kind.HasValue)
            query = query.Where(s => s.Kind == Kind.Value);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            query = query.Where(s => s.EntityTitle.Contains(term) || s.Path.Contains(term));
        }

        // منتظرها: بیشترین افزایش نمره اول (کار مؤثرتر اول)؛ بقیه: تازه‌ترین اول.
        query = Status == "pending"
            ? query.OrderByDescending(s => Focus.HasValue && s.Id == Focus.Value).ThenByDescending(s => s.ScoreAfter - s.ScoreBefore).ThenByDescending(s => s.CreatedAt)
            : query.OrderByDescending(s => s.ReviewedAt ?? s.UpdatedAt);

        var total = await query.CountAsync(HttpContext.RequestAborted);
        var page = ClampPage(pageNumber, total, PageSize);
        var rows = await query.Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(HttpContext.RequestAborted);
        Items = rows.Select(r => new SeoSuggestionView(
            r,
            SeoProposal.FromJson(r.ProposalJson),
            SeoProposal.FromJson(r.CurrentJson),
            SeoJson.Deserialize<List<string>>(r.ReasonsJson) ?? [],
            SeoJson.Deserialize<SeoResearchSummary>(r.ResearchJson))).ToList();
        Pager = new PagerModel(page, Math.Max(1, (int)Math.Ceiling(total / (double)PageSize)), total);
    }

    /// <summary>تأیید بخش‌های تیک‌خورده. fields = مقدار عددی هر بخش (SeoFields).</summary>
    public async Task<IActionResult> OnPostApproveAsync(Guid id, List<SeoFields> fields)
    {
        var selected = fields.Aggregate(SeoFields.None, (all, f) => all | f);
        ShowResult(await engine.ApproveAsync(id, selected, ActorId, ActorName, HttpContext.RequestAborted));
        badges.Invalidate();
        return BackToList();
    }

    /// <summary>رد بخش‌های علامت‌خورده با دلیل؛ نسخهٔ تازه بلافاصله ساخته می‌شود و بالای صف می‌آید.</summary>
    public async Task<IActionResult> OnPostRejectAsync(Guid id, List<SeoFields> rejected, string? reason)
    {
        var selected = rejected.Aggregate(SeoFields.None, (all, f) => all | f);
        var result = await engine.RejectAsync(id, selected, reason, ActorId, ActorName, HttpContext.RequestAborted);
        ShowResult(result);
        badges.Invalidate();
        if (Succeeded(result) && result.Data is { } next)
            return LocalRedirect($"/admin/seo/review?focus={next}");
        return BackToList();
    }

    /// <summary>تأیید گروهی: همهٔ بخش‌های پیشنهادهای انتخاب‌شده.</summary>
    public async Task<IActionResult> OnPostBulkApproveAsync(List<Guid> ids)
    {
        // هر پیشنهاد جدا اعمال می‌شود؛ خطای یکی (متن بلند، نامک تکراری، صفحهٔ ویرایش‌شده) بقیه را متوقف نمی‌کند
        // و صفحه هرگز با خطای ۵۰۰ از کار نمی‌افتد. اولین علت خطا به کارشناس نشان داده می‌شود.
        int ok = 0, failed = 0;
        string? firstError = null;
        foreach (var id in ids.Distinct().Take(50))
        {
            try
            {
                var result = await engine.ApproveAsync(id, SeoFields.All, ActorId, ActorName, HttpContext.RequestAborted);
                if (Succeeded(result)) ok++;
                else { failed++; firstError ??= result.Message; }
            }
            catch (global::System.Exception ex) when (ex is not global::System.OperationCanceledException)
            {
                failed++;
                firstError ??= "خطای پیش‌بینی‌نشده هنگام اعمال؛ جزئیات در گزارش خطاها ثبت شد.";
                HttpContext.RequestServices.GetRequiredService<ILogger<ReviewModel>>().LogError(ex, "Bulk SEO approve failed for {Id}", id);
            }
        }
        badges.Invalidate();
        if (ok > 0)
            SuccessMessage = $"{ok} پیشنهاد اعمال شد." + (failed > 0 ? $" {failed} مورد اعمال نشد؛ نمونهٔ علت: {firstError}" : "");
        else if (failed > 0)
            ErrorMessage = $"هیچ پیشنهادی اعمال نشد. علت: {firstError}";
        else
            ErrorMessage = "هیچ پیشنهادی اعمال نشد. ابتدا پیشنهادها را انتخاب کنید.";
        return BackToList();
    }

    /// <summary>
    /// دور ۳۱ (#۱۳): «حذف همهٔ پیشنهادهای منتظر» (kind = فقط همان نوع صفحه). فقط وضعیت «منتظر تأیید» حذف می‌شود تا
    /// موتور پیشنهادهای تازه و غیرتکراری بسازد؛ داده‌های اعمال‌شده و ذخیره‌شده هرگز پاک نمی‌شوند.
    /// </summary>
    public async Task<IActionResult> OnPostDeletePendingAsync(SeoEntityKind? kind)
    {
        ShowResult(await engine.DeletePendingAsync(kind, ActorId, ActorName, HttpContext.RequestAborted));
        badges.Invalidate();
        return LocalRedirect(kind is { } k ? $"/admin/seo/review?kind={k}" : "/admin/seo/review");
    }

    public string UrlWith(params (string Key, string? Value)[] values) => AdminUi.With(Request, values);
}
