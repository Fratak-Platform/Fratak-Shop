using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Ux;

/// <summary>
/// صفحهٔ «میانبرهای من» (<c>/admin/ux</c>) و نقطهٔ پایانی JSON ابزارهای رابط کاربری پنل.
/// <para>
/// برای همهٔ کارکنان باز است (مثل صندوق اعلان‌ها) چون فقط دادهٔ شخصی خود کارمند را تغییر می‌دهد؛
/// تنها هندلر <see cref="OnGetSearchAsync"/> داده‌های فروشگاه را برمی‌گرداند که آن هم بر اساس مجوزهای کارمند محدود می‌شود.
/// </para>
/// هندلرها (صدا زده‌شده از <c>wwwroot/assets/js/admin-ux.js</c>):
/// <list type="bullet">
/// <item><c>GET  ?handler=Search&amp;q=…</c> → جست‌وجوی سریع (Ctrl+K)</item>
/// <item><c>POST ?handler=Pin</c> (url, title, icon) → سنجاق/برداشتن سنجاق</item>
/// <item><c>POST ?handler=Filter</c> (path, name, query) → ذخیرهٔ فیلتر</item>
/// <item><c>POST ?handler=DeleteFilter</c> (path, name) → حذف فیلتر</item>
/// <item><c>GET  ?handler=Draft&amp;key=…</c> → پیش‌نویس خودکار یک فرم (admin-edit.js)</item>
/// <item><c>POST ?handler=Draft</c> (key, stamp, title, url, data) → ذخیرهٔ پیش‌نویس</item>
/// <item><c>POST ?handler=DeleteDraft</c> (key) → دور انداختن پیش‌نویس</item>
/// </list>
/// </summary>
public class IndexModel(IAdminShortcutService shortcuts, IAdminSearchService search, IEditDraftService drafts) : AdminPageModel
{
    public IReadOnlyList<EditDraftSummary> Drafts { get; private set; } = [];
    public List<PinItem> Pins { get; private set; } = [];
    public List<RecentItem> Recents { get; private set; } = [];
    public Dictionary<string, List<SavedFilter>> Filters { get; private set; } = [];

    /// <summary>درخواست از fetch آمده؟ (پاسخ JSON) یا از فرم معمولی صفحه؟ (بازگشت به همین صفحه)</summary>
    private bool IsFetch => Request.Headers.XRequestedWith == "fetch" || Request.Headers.Accept.ToString().Contains("application/json");

    public async Task OnGetAsync()
    {
        Pins = await shortcuts.GetPinsAsync(ActorId);
        Recents = await shortcuts.GetRecentsAsync(ActorId);
        Filters = await shortcuts.GetAllFiltersAsync(ActorId);
        Drafts = await drafts.ListAsync(ActorId);
    }

    /// <summary>
    /// جست‌وجوی سریع. فقط بخش‌هایی جست‌وجو می‌شوند که کارمند مجوز دیدنشان را دارد؛
    /// مثلاً نویسندهٔ وبلاگ هیچ سفارش یا مشتری‌ای در نتایج نمی‌بیند.
    /// </summary>
    public async Task<IActionResult> OnGetSearchAsync(string? q, CancellationToken cancellationToken)
    {
        var scopes = AdminSearchScope.None;
        if (Can(AdminPermissions.OrdersView)) scopes |= AdminSearchScope.Orders;
        if (Can(AdminPermissions.CustomersView)) scopes |= AdminSearchScope.Customers;
        if (CanAny(AdminPermissions.ProductsView, AdminPermissions.ProductsManage)) scopes |= AdminSearchScope.Products;
        if (Can(AdminPermissions.TicketsManage)) scopes |= AdminSearchScope.Tickets;

        var hits = await search.SearchAsync(q, scopes, 5, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return new JsonResult(hits);
    }

    public async Task<IActionResult> OnPostPinAsync(string? url, string? title, string? icon)
    {
        var pinned = await shortcuts.TogglePinAsync(ActorId, url ?? "", title ?? "", icon);
        if (pinned is null)
            return Reply(false, $"این صفحه قابل سنجاق نیست یا به سقف {AdminShortcutService.MaxPins} سنجاق رسیده‌اید.");
        return Reply(true, pinned.Value ? "به سنجاق‌شده‌ها اضافه شد." : "از سنجاق‌شده‌ها برداشته شد.", new { pinned = pinned.Value });
    }

    public async Task<IActionResult> OnPostFilterAsync(string? path, string? name, string? query)
    {
        var error = await shortcuts.SaveFilterAsync(ActorId, path ?? "", name ?? "", query ?? "");
        return error is null ? Reply(true, "فیلتر ذخیره شد.") : Reply(false, error);
    }

    public async Task<IActionResult> OnPostDeleteFilterAsync(string? path, string? name)
    {
        await shortcuts.DeleteFilterAsync(ActorId, path ?? "", name ?? "");
        return Reply(true, "فیلتر حذف شد.");
    }

    /// <summary>
    /// پیش‌نویس ذخیره‌شدهٔ کارمند برای یک فرم. پاسخ: <c>{ok:true, data:{stamp, title, savedAt, data}}</c> یا <c>{ok:false}</c>.
    /// </summary>
    public async Task<IActionResult> OnGetDraftAsync(string? key, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var draft = await drafts.GetAsync(ActorId, key ?? "", cancellationToken);
        if (draft is null)
            return new JsonResult(new { ok = false });
        return new JsonResult(new
        {
            ok = true,
            data = new { stamp = draft.BaseStamp.ToString(global::System.Globalization.CultureInfo.InvariantCulture), title = draft.Title, savedAt = draft.SavedAt, data = draft.Data },
        });
    }

    /// <summary>ذخیرهٔ خودکار فرم (هر چند ثانیه هنگام تایپ). stamp به‌صورت متن می‌آید چون عدد ۱۸ رقمی در JavaScript دقیق نمی‌ماند.</summary>
    public async Task<IActionResult> OnPostDraftAsync(string? key, string? stamp, string? title, string? url, string? data, CancellationToken cancellationToken)
    {
        _ = long.TryParse(stamp, global::System.Globalization.NumberStyles.None, global::System.Globalization.CultureInfo.InvariantCulture, out var baseStamp);
        var error = await drafts.SaveAsync(ActorId, key ?? "", baseStamp, title, url, data ?? "", cancellationToken);
        return error is null ? Reply(true, "پیش‌نویس ذخیره شد.") : Reply(false, error);
    }

    public async Task<IActionResult> OnPostDeleteDraftAsync(string? key, CancellationToken cancellationToken)
    {
        await drafts.DeleteAsync(ActorId, key ?? "", cancellationToken);
        return Reply(true, "پیش‌نویس حذف شد.");
    }

    /// <summary>پاسخ مشترک: JSON برای fetch، یا پیام موقت و بازگشت برای فرم‌های همین صفحه.</summary>
    private IActionResult Reply(bool ok, string message, object? extra = null)
    {
        if (IsFetch)
            return new JsonResult(new { ok, message, data = extra });

        TempData[ok ? "SuccessMessage" : "ErrorMessage"] = message;
        return RedirectToPage();
    }
}
