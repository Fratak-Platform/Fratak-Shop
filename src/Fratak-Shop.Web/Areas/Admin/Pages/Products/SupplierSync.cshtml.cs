using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Suppliers;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Suppliers;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Products;

/// <summary>
/// صفحهٔ «بررسی تأمین‌کننده» (مرحلهٔ هفتم – فاز ۵): داشبورد اجراها، پیشنهادهای منتظر تأیید (موجود شدن، افزایش موجودی،
/// تغییر قیمت)، تغییرهای خودکار (ناموجودی) و مشکل‌ها (لینک خراب/خطای خواندن).
/// <para>دسترسی: دیدن = «اطلاعات تأمین‌کننده و قیمت خرید»؛ تأیید/رد = «مدیریت محصولات».</para>
/// منطق اصلی در Core/Services/Suppliers/SupplierSyncService.cs است؛ این صفحه فقط نمایش و فرم است.
/// </summary>
[AdminPermission(AdminPermissions.ProductsSupplier)]
public class SupplierSyncModel(ISupplierSyncService sync, SupplierSyncQueue queue, AdminBadgeService badges) : AdminPageModel
{
    /// <summary>زبانه‌ها؛ مثال آدرس: /admin/products/supplier-sync?tab=auto</summary>
    public static readonly (string Key, string Title)[] Tabs =
    [
        ("pending", "منتظر تأیید"),
        ("problems", "مشکل‌ها"),
        ("auto", "تغییرهای خودکار"),
        ("history", "رسیدگی‌شده"),
        ("runs", "اجراها"),
    ];

    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "pending";
    [BindProperty(SupportsGet = true)] public SupplierChangeKind? Kind { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }

    public SupplierSyncDashboard Dashboard { get; private set; } = null!;
    public PagedList<SupplierChangeRow> Changes { get; private set; } = new([], 0, 1, 50);
    public bool RunQueued => queue.HasPending;

    public async Task OnGetAsync([FromQuery(Name = "page")] int pageNumber = 1)
    {
        if (!Tabs.Any(t => t.Key == Tab))
            Tab = "pending";
        Dashboard = await sync.GetDashboardAsync(HttpContext.RequestAborted);
        if (Tab != "runs")
            Changes = await sync.ListChangesAsync(new SupplierChangeQuery(Tab, Kind, Search, ClampPage(pageNumber)), HttpContext.RequestAborted);
    }

    /// <summary>«همین الان بررسی کن»: درخواست به صف سرویس پس‌زمینه (حداکثر یک دقیقه بعد شروع می‌شود).</summary>
    public IActionResult OnPostRun()
    {
        if (sync.IsRunning)
            ErrorMessage = "یک بررسی همین الان در جریان است؛ پس از پایان، نتیجه در همین صفحه دیده می‌شود.";
        else if (queue.Request(ActorName))
            SuccessMessage = "بررسی در پس‌زمینه شروع می‌شود (حداکثر تا یک دقیقهٔ دیگر). بعد از پایان اعلان می‌گیرید.";
        else
            ErrorMessage = "درخواست بررسی قبلاً ثبت شده و منتظر شروع است.";
        return BackToList();
    }

    /// <summary>
    /// تأیید یا رد گروهی/تکی. فیلدها: ids (انتخاب‌شده‌ها)، decision (approve/reject)،
    /// single («approve:شناسه» برای دکمهٔ هر ردیف)، qty-{شناسه} (موجودی دلخواه)، adjustSale (تغییر قیمت فروش هم‌زمان).
    /// </summary>
    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostDecideAsync(List<Guid> ids, string? decision, string? single, bool adjustSale)
    {
        if (!string.IsNullOrEmpty(single))
        {
            var parts = single.Split(':', 2);
            if (parts.Length == 2 && Guid.TryParse(parts[1], out var one))
            {
                decision = parts[0];
                ids = [one];
            }
        }

        ids = ids.Distinct().ToList();
        if (decision == "reject")
        {
            ShowResult(await sync.RejectAsync(ids, ActorId, ActorName));
        }
        else if (decision == "approve")
        {
            var approvals = new List<SupplierApproval>();
            foreach (var id in ids)
            {
                int? quantity = null;
                var raw = Request.Form[$"qty-{id}"].ToString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    if (!int.TryParse(SearchText.NormalizeDigits(raw).Trim(), out var parsed) || parsed < 0)
                    {
                        ErrorMessage = "موجودی واردشده معتبر نیست.";
                        return BackToList();
                    }
                    quantity = parsed;
                }
                approvals.Add(new SupplierApproval(id, quantity));
            }
            ShowResult(await sync.ApproveAsync(approvals, adjustSale, ActorId, ActorName));
        }
        else
        {
            ErrorMessage = "نوع عملیات مشخص نیست.";
        }

        badges.Invalidate();
        return BackToList();
    }

    /// <summary>آدرس همین صفحه با زبانهٔ دیگر (فیلترها پاک می‌شوند).</summary>
    public static string TabUrl(string tab) => tab == "pending" ? "/admin/products/supplier-sync" : $"/admin/products/supplier-sync?tab={tab}";

    /// <summary>رنگ نشان هر نوع تغییر.</summary>
    public static string KindCss(SupplierChangeKind kind) => kind switch
    {
        SupplierChangeKind.OutOfStock => "a-badge-red",
        SupplierChangeKind.BackInStock => "a-badge-green",
        SupplierChangeKind.StockIncrease => "a-badge-sky",
        SupplierChangeKind.StockDecrease => "a-badge-yellow",
        SupplierChangeKind.PriceChange => "a-badge-purple",
        _ => "a-badge-gray",
    };

    /// <summary>عدد برای data-* (نقطهٔ اعشار انگلیسی، بدون جداکننده) تا JS بخواند.</summary>
    public static string Invariant(decimal? value) =>
        value?.ToString(global::System.Globalization.CultureInfo.InvariantCulture) ?? "";

    /// <summary>درصد تغییر با علامت؛ مثال: 3.75 → «(+3.8٪)».</summary>
    public static string PercentText(decimal? percent) =>
        percent is { } p ? $"({(p > 0 ? "+" : "")}{p.ToString("0.#", global::System.Globalization.CultureInfo.InvariantCulture)}٪)" : "";

    /// <summary>حاشیهٔ سود فروش با قیمت خرید داده‌شده؛ مثال: فروش ۵۰۰ و خرید ۴۱۵ هزار → «17٪».</summary>
    public static string MarginText(decimal salePrice, decimal cost) =>
        salePrice <= 0 ? "-" : $"{Math.Round((salePrice - cost) * 100m / salePrice, 1).ToString("0.#", global::System.Globalization.CultureInfo.InvariantCulture)}٪";

    /// <summary>عنوان وضعیت رسیدگی.</summary>
    public static string StatusTitle(SupplierChangeStatus status) => status switch
    {
        SupplierChangeStatus.Pending => "منتظر تأیید",
        SupplierChangeStatus.Approved => "تأیید شد",
        SupplierChangeStatus.Rejected => "رد شد",
        SupplierChangeStatus.AutoApplied => "خودکار اعمال شد",
        SupplierChangeStatus.Superseded => "جایگزین شد",
        _ => status.ToString(),
    };
}
