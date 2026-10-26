using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Tools;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Seo;

/// <summary>
/// ویرایش گروهی سئو (مرحلهٔ هفتم – فاز ۸ب). آدرس: /admin/seo/bulk?kind=Product&amp;filter=no-meta
/// <para>عنوان سئو، توضیح متا و کلمهٔ کلیدی چند صفحه را در یک جدول ویرایش و یک‌جا ذخیره کنید، یا روی ردیف‌های انتخاب‌شده
/// «جست‌وجو و جایگزینی» (با پیش‌نمایش) انجام دهید. منطق: Core/Services/Seo/Tools/SeoBulkEditService.cs.</para>
/// </summary>
[AdminPermission(AdminPermissions.SeoManage)]
public class BulkModel(ISeoBulkEditService bulk) : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public SeoEntityKind Kind { get; set; } = SeoEntityKind.Product;
    [BindProperty(SupportsGet = true)] public string Filter { get; set; } = "all";
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "page")] public int PageNumber { get; set; } = 1;

    /// <summary>ردیف‌های فرم (نام فیلدها: Rows[i].Title و …).</summary>
    [BindProperty] public List<RowInput> Rows { get; set; } = [];

    // فرم جست‌وجو و جایگزینی
    [BindProperty] public List<Guid> Ids { get; set; } = [];
    [BindProperty] public SeoBulkField Field { get; set; } = SeoBulkField.Title;
    [BindProperty] public string? Find { get; set; }
    [BindProperty] public string? Replacement { get; set; }

    public PagedList<SeoBulkRow> List { get; private set; } = new([], 0, 1, 25);
    public IReadOnlyDictionary<string, int> Counts { get; private set; } = new Dictionary<string, int>();
    public PagerModel Pager { get; private set; } = new(1, 1, 0);
    public IReadOnlyList<SeoReplacePreview>? Preview { get; private set; }
    public IReadOnlyList<string> SaveErrors { get; private set; } = [];

    public static readonly (string Key, string Title)[] FilterTabs =
    [
        ("all", "همه"),
        ("no-title", "بدون عنوان سئو"),
        ("no-meta", "بدون توضیح متا"),
        ("no-keyword", "بدون کلمهٔ کلیدی"),
        ("duplicate", "عنوان تکراری"),
        ("low", "نمرهٔ زیر ۷۰"),
    ];

    public sealed class RowInput
    {
        public Guid Id { get; set; }
        public string? OriginalTitle { get; set; }
        public string? Title { get; set; }
        public string? OriginalMeta { get; set; }
        public string? Meta { get; set; }
        public string? OriginalFocus { get; set; }
        public string? Focus { get; set; }
    }

    public Task OnGetAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        if (!Enum.IsDefined(Kind))
            Kind = SeoEntityKind.Product;
        if (!SeoBulkEditService.Filters.Contains(Filter))
            Filter = "all";
        List = await bulk.ListAsync(new SeoBulkQuery(Kind, Filter, Search, PageNumber), HttpContext.RequestAborted);
        Counts = await bulk.CountsAsync(Kind, HttpContext.RequestAborted);
        Pager = new PagerModel(List.Page, List.PageCount, List.Total);
    }

    /// <summary>ذخیرهٔ ردیف‌های تغییرکرده.</summary>
    public async Task<IActionResult> OnPostSaveAsync()
    {
        var changes = Rows
            .Select(r => new SeoBulkChange(r.Id, r.OriginalTitle, r.Title, r.OriginalMeta, r.Meta, r.OriginalFocus, r.Focus))
            .ToList();
        var result = await bulk.SaveAsync(Kind, changes, ActorId, HttpContext.RequestAborted);
        ShowResult(result);
        if (result.Data is { Errors.Count: > 0 } data && data.Saved > 0)
            ErrorMessage = string.Join(" ", data.Errors.Take(3));
        return LocalRedirect(ListUrl());
    }

    /// <summary>پیش‌نمایش جست‌وجو و جایگزینی (چیزی ذخیره نمی‌شود).</summary>
    public async Task<IActionResult> OnPostPreviewAsync()
    {
        if (string.IsNullOrEmpty(Find) || Ids.Count == 0)
        {
            ErrorMessage = Ids.Count == 0 ? "اول ردیف‌ها را با تیک انتخاب کنید." : "عبارتی که باید پیدا شود را وارد کنید.";
            return LocalRedirect(ListUrl());
        }
        Preview = await bulk.PreviewReplaceAsync(Kind, Ids, Field, Find, Replacement ?? "", HttpContext.RequestAborted);
        ModelState.Clear(); // فرم جدول با داده‌های تازهٔ دیتابیس ساخته شود، نه مقدارهای ارسالی
        await LoadAsync();
        return Page();
    }

    /// <summary>اعمال جایگزینی بعد از پیش‌نمایش.</summary>
    public async Task<IActionResult> OnPostReplaceAsync()
    {
        ShowResult(await bulk.ApplyReplaceAsync(Kind, Ids, Field, Find ?? "", Replacement ?? "", ActorId, HttpContext.RequestAborted));
        return LocalRedirect(ListUrl());
    }

    /// <summary>آدرس فهرست با فیلترهای فعلی (بعد از ذخیره به همان صفحه برمی‌گردیم).</summary>
    public string ListUrl(int? page = null)
    {
        var query = new List<string> { "kind=" + Kind };
        if (Filter != "all") query.Add("filter=" + Uri.EscapeDataString(Filter));
        if (!string.IsNullOrWhiteSpace(Search)) query.Add("q=" + Uri.EscapeDataString(Search));
        var target = page ?? PageNumber;
        if (target > 1) query.Add("page=" + target);
        return "/admin/seo/bulk?" + string.Join("&", query);
    }

    public string KindUrl(SeoEntityKind kind) => "/admin/seo/bulk?kind=" + kind;

    public string FilterUrl(string filter) =>
        "/admin/seo/bulk?kind=" + Kind + (filter == "all" ? "" : "&filter=" + filter) + (string.IsNullOrWhiteSpace(Search) ? "" : "&q=" + Uri.EscapeDataString(Search));
}
