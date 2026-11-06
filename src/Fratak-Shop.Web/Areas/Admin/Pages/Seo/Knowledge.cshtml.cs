using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Knowledge;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Seo;

/// <summary>
/// جدول دانش سئو (مرحلهٔ شانزدهم): آمار، جست‌وجو و فیلتر ردیف‌ها، غیرفعال/فعال کردن و افزودن ردیف دستی.
/// موتور سئو و «پیشنهاد هوشمند» از این جدول استفاده می‌کنند و از تحقیق اینترنتی و تأیید/رد کارشناس‌ها مدام بزرگ‌تر می‌شود.
/// </summary>
[AdminPermission(AdminPermissions.SeoManage)]
public class KnowledgeModel(ISeoKnowledgeService knowledge) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "kind")] public SeoKnowledgeKind? Kind { get; set; }
    [BindProperty(SupportsGet = true, Name = "source")] public SeoKnowledgeSource? Source { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "active")] public bool? Active { get; set; }

    public SeoKnowledgeStats Stats { get; private set; } = null!;
    public SeoKnowledgeList List { get; private set; } = null!;
    public PagerModel Pager { get; private set; } = new(1, 1, 0);

    /// <summary>نوع‌هایی که کارشناس می‌تواند دستی اضافه کند (بقیه از دادهٔ اولیه یا یادگیری می‌آیند).</summary>
    public static readonly SeoKnowledgeKind[] ManualKinds =
    [
        SeoKnowledgeKind.Correction, SeoKnowledgeKind.Style, SeoKnowledgeKind.Spam, SeoKnowledgeKind.Synonym,
        SeoKnowledgeKind.PowerWord, SeoKnowledgeKind.Modifier, SeoKnowledgeKind.CallToAction, SeoKnowledgeKind.Benefit,
        SeoKnowledgeKind.Heading, SeoKnowledgeKind.Question, SeoKnowledgeKind.Phrase, SeoKnowledgeKind.Keyword,
        SeoKnowledgeKind.Word, SeoKnowledgeKind.Compound,
    ];

    /// <summary>تعداد واژه‌های واژه‌نامهٔ داخلی (فقط در حافظه، نه در جدول). مثال: حدود ۱۶۶ هزار.</summary>
    public static int LexiconCount => Lexicon.Value;

    private static readonly Lazy<int> Lexicon = new(() => SeoKnowledgeSeed.Rows.Count(r => r.Known));

    public async Task OnGetAsync([FromQuery(Name = "page")] int pageNumber = 1)
    {
        var ct = HttpContext.RequestAborted;
        if (Kind is { } kind && (!Enum.IsDefined(kind) || kind == SeoKnowledgeKind.Meta)) Kind = null;
        if (Source is { } source && !Enum.IsDefined(source)) Source = null;
        Stats = await knowledge.GetStatsAsync(ct);
        List = await knowledge.ListAsync(new SeoKnowledgeQuery(Kind, Source, Search, Active, ClampPage(pageNumber)), ct);
        Pager = new PagerModel(List.Page, Math.Max(1, (int)Math.Ceiling(List.Total / (double)List.Take)), List.Total);
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id, bool active)
    {
        if (await knowledge.SetActiveAsync(id, active, HttpContext.RequestAborted))
            SuccessMessage = active ? "ردیف دوباره فعال شد." : "ردیف غیرفعال شد و دیگر در پیشنهادها استفاده نمی‌شود.";
        else
            ErrorMessage = "ردیف پیدا نشد.";
        return BackToList();
    }

    public async Task<IActionResult> OnPostAddAsync(SeoKnowledgeKind kind, string? term, string? value, string? topic, double weight = 1)
    {
        if (!ManualKinds.Contains(kind))
        {
            ErrorMessage = "این نوع ردیف دستی قابل افزودن نیست.";
            return BackToList();
        }
        var error = await knowledge.SaveManualAsync(kind, term ?? "", value, topic, weight, HttpContext.RequestAborted);
        if (error is null)
            SuccessMessage = $"«{term}» در جدول دانش ذخیره شد و از همین حالا در پیشنهادها استفاده می‌شود.";
        else
            ErrorMessage = error;
        return BackToList();
    }

    /// <summary>نام فارسی نوع ردیف. مثال: Correction → «اصلاح املایی».</summary>
    public static string KindTitle(SeoKnowledgeKind kind) => kind switch
    {
        SeoKnowledgeKind.Word => "واژه",
        SeoKnowledgeKind.Compound => "واژهٔ مرکب (نیم‌فاصله)",
        SeoKnowledgeKind.Verb => "فعل",
        SeoKnowledgeKind.StopWord => "واژهٔ پرتکرار بی‌معنا",
        SeoKnowledgeKind.Informal => "محاوره → رسمی",
        SeoKnowledgeKind.Abbreviation => "کوته‌نوشت",
        SeoKnowledgeKind.Correction => "اصلاح املایی",
        SeoKnowledgeKind.Style => "اصلاح سبک / کلیشه",
        SeoKnowledgeKind.Unit => "واحد",
        SeoKnowledgeKind.Number => "عدد",
        SeoKnowledgeKind.Modifier => "پیشوند/پسوند جست‌وجو",
        SeoKnowledgeKind.PowerWord => "واژهٔ ترغیبی",
        SeoKnowledgeKind.Spam => "عبارت اسپم",
        SeoKnowledgeKind.Transition => "واژهٔ ربط",
        SeoKnowledgeKind.CallToAction => "دعوت به اقدام",
        SeoKnowledgeKind.Benefit => "مزیت فروشگاه",
        SeoKnowledgeKind.Heading => "الگوی تیتر",
        SeoKnowledgeKind.Question => "پرسش خریدار",
        SeoKnowledgeKind.Synonym => "هم‌معنی",
        SeoKnowledgeKind.Phrase => "عبارت جست‌وجوی واقعی",
        SeoKnowledgeKind.Keyword => "کلمهٔ کلیدی آموخته",
        SeoKnowledgeKind.Pattern => "الگوی عنوان آموخته",
        _ => kind.ToString(),
    };

    public static string SourceTitle(SeoKnowledgeSource source) => source switch
    {
        SeoKnowledgeSource.Seed => "دادهٔ اولیه",
        SeoKnowledgeSource.Research => "تحقیق اینترنتی",
        SeoKnowledgeSource.Learned => "یادگیری",
        SeoKnowledgeSource.Manual => "دستی",
        _ => "—",
    };

    public string UrlWith(params (string Key, string? Value)[] values) => AdminUi.With(Request, values);
}
