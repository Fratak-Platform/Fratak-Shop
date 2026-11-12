using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Contents;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Content;

/// <summary>
/// دور ۲۹ (#۲۷) – «منوی بالای سایت»: ویرایش ساده و سریع لینک‌های نوار منوی هدر (عنوان، آدرس، برچسب، ترتیب، روشن/خاموش)،
/// افزودن لینک تازه و «افزودن سریع» لینک‌های پرکاربرد (مثل «تخفیف‌دارها»). همان دادهٔ جدول منوها (Menu/MenuItem) است؛
/// پس هدر سایت بلافاصله بعد از ذخیره عوض می‌شود (کش منو با ذخیره پاک می‌شود). فقط سطح اول منو این‌جا نمایش داده می‌شود؛
/// زیرمنوها و زمان‌بندی در «همهٔ جدول‌ها ← آیتم‌های منو» هستند.
/// </summary>
[AdminPermission(AdminPermissions.ContentManage)]
public class HeaderMenuModel(IAdminDataService data) : AdminPageModel
{
    /// <summary>منوی جایگاه «نوار منوی هدر» (اگر هنوز ساخته نشده null).</summary>
    public Menu? Current { get; private set; }

    /// <summary>آیتم‌های سطح اول منو به ترتیب نمایش.</summary>
    public List<MenuItem> Items { get; private set; } = [];

    /// <summary>تعداد زیرمنوی هر آیتم (برای راهنمایی؛ زیرمنوها این‌جا ویرایش نمی‌شوند).</summary>
    public Dictionary<Guid, int> ChildCounts { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Current = await data.Query<Menu>().Where(m => m.Location == MenuLocation.HeaderMain)
            .OrderBy(m => m.DisplayOrder).ThenBy(m => m.CreatedAt).FirstOrDefaultAsync(HttpContext.RequestAborted);
        if (Current is null)
            return;
        var menuId = Current.Id;
        var all = await data.Query<MenuItem>().Where(i => i.MenuId == menuId)
            .OrderBy(i => i.DisplayOrder).ThenBy(i => i.CreatedAt).ToListAsync(HttpContext.RequestAborted);
        Items = all.Where(i => i.ParentId == null).ToList();
        ChildCounts = all.Where(i => i.ParentId != null).GroupBy(i => i.ParentId!.Value).ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>ساخت منوی هدر (فقط اگر نباشد؛ مثلاً دیتابیس خالی).</summary>
    [AdminPermission(AdminPermissions.ContentManage)]
    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!await data.Query<Menu>().AnyAsync(m => m.Location == MenuLocation.HeaderMain, HttpContext.RequestAborted))
        {
            var result = await data.AddAsync(new Menu { Key = "header-main", Title = "منوی اصلی", Location = MenuLocation.HeaderMain, DisplayOrder = 1 });
            ShowResult(Succeeded(result) ? OperationResult.Success("منوی بالای سایت ساخته شد.") : OperationResult.Error(result.Message ?? "ساخت منو ممکن نشد."));
        }
        return LocalRedirect("/admin/content/header-menu");
    }

    /// <summary>ذخیرهٔ یک آیتم.</summary>
    [AdminPermission(AdminPermissions.ContentManage)]
    public async Task<IActionResult> OnPostSaveAsync(Guid id, string? title, string? url, string? badge, int order, bool active, bool newTab)
    {
        var item = await data.FindAsync<MenuItem>(id);
        if (item is null || !await IsHeaderItemAsync(item.MenuId))
        {
            ShowResult(OperationResult.NotFound("آیتم منو پیدا نشد."));
            return LocalRedirect("/admin/content/header-menu");
        }
        var clean = HeaderMenuRules.Clean(title, url, badge, item.Type == MenuItemType.CategoriesMegaMenu);
        if (clean.Error is not null)
        {
            ShowResult(OperationResult.Error(clean.Error));
            return LocalRedirect("/admin/content/header-menu");
        }
        item.Title = clean.Title!;
        item.Url = clean.Url;
        item.BadgeText = clean.Badge;
        item.DisplayOrder = Math.Clamp(order, 0, 999);
        item.IsActive = active;
        item.OpenInNewTab = newTab && UrlSafety.IsExternal(clean.Url);
        var result = await data.UpdateAsync(item);
        ShowResult(Succeeded(result) ? OperationResult.Success($"«{item.Title}» ذخیره شد.") : OperationResult.Error(result.Message ?? "ذخیره ممکن نشد."));
        return LocalRedirect("/admin/content/header-menu");
    }

    /// <summary>افزودن لینک تازه (ته منو، مگر ترتیب داده شود).</summary>
    [AdminPermission(AdminPermissions.ContentManage)]
    public async Task<IActionResult> OnPostAddAsync(string? title, string? url, string? badge, int? order)
    {
        var menu = await data.Query<Menu>().Where(m => m.Location == MenuLocation.HeaderMain)
            .OrderBy(m => m.DisplayOrder).ThenBy(m => m.CreatedAt).FirstOrDefaultAsync(HttpContext.RequestAborted);
        if (menu is null)
        {
            ShowResult(OperationResult.Error("اول منوی بالای سایت را بسازید."));
            return LocalRedirect("/admin/content/header-menu");
        }
        var clean = HeaderMenuRules.Clean(title, url, badge, false);
        if (clean.Error is not null)
        {
            ShowResult(OperationResult.Error(clean.Error));
            return LocalRedirect("/admin/content/header-menu");
        }
        var menuId = menu.Id;
        var count = await data.Query<MenuItem>().CountAsync(i => i.MenuId == menuId && i.ParentId == null, HttpContext.RequestAborted);
        if (count >= HeaderMenuRules.MaxItems)
        {
            ShowResult(OperationResult.Error($"منوی بالا حداکثر {Format.Number(HeaderMenuRules.MaxItems)} لینک دارد (بیشتر از آن در هدر جا نمی‌شود)."));
            return LocalRedirect("/admin/content/header-menu");
        }
        var last = await data.Query<MenuItem>().Where(i => i.MenuId == menuId && i.ParentId == null)
            .Select(i => (int?)i.DisplayOrder).MaxAsync(HttpContext.RequestAborted) ?? 0;
        var item = new MenuItem
        {
            MenuId = menuId,
            Title = clean.Title!,
            Url = clean.Url,
            BadgeText = clean.Badge,
            Type = MenuItemType.Link,
            DisplayOrder = Math.Clamp(order ?? last + 1, 0, 999),
            IsActive = true,
        };
        var result = await data.AddAsync(item);
        ShowResult(Succeeded(result) ? OperationResult.Success($"«{item.Title}» به منوی بالا اضافه شد.") : OperationResult.Error(result.Message ?? "افزودن ممکن نشد."));
        return LocalRedirect("/admin/content/header-menu");
    }

    /// <summary>افزودن سریع یکی از لینک‌های آماده (<see cref="HeaderMenuRules.Presets"/>).</summary>
    [AdminPermission(AdminPermissions.ContentManage)]
    public Task<IActionResult> OnPostPresetAsync(string? key)
    {
        var preset = HeaderMenuRules.Presets.FirstOrDefault(p => p.Key == key);
        if (preset is null)
        {
            ShowResult(OperationResult.Error("لینک آماده پیدا نشد."));
            return Task.FromResult<IActionResult>(LocalRedirect("/admin/content/header-menu"));
        }
        return OnPostAddAsync(preset.Title, preset.Url, preset.Badge, null);
    }

    /// <summary>جابه‌جایی یک پله به بالا/پایین (ترتیب همهٔ آیتم‌ها از نو شماره می‌خورد: ۱، ۲، ۳، …).</summary>
    [AdminPermission(AdminPermissions.ContentManage)]
    public async Task<IActionResult> OnPostMoveAsync(Guid id, int step)
    {
        var item = await data.Query<MenuItem>().FirstOrDefaultAsync(i => i.Id == id, HttpContext.RequestAborted);
        if (item is null || item.ParentId is not null || !await IsHeaderItemAsync(item.MenuId))
            return LocalRedirect("/admin/content/header-menu");
        var menuId = item.MenuId;
        var ids = await data.Query<MenuItem>().Where(i => i.MenuId == menuId && i.ParentId == null)
            .OrderBy(i => i.DisplayOrder).ThenBy(i => i.CreatedAt).Select(i => i.Id).ToListAsync(HttpContext.RequestAborted);
        var order = HeaderMenuRules.Move(ids, id, step);
        for (var i = 0; i < order.Count; i++)
        {
            var row = await data.FindAsync<MenuItem>(order[i]);
            if (row is null || row.DisplayOrder == i + 1)
                continue;
            row.DisplayOrder = i + 1;
            await data.UpdateAsync(row);
        }
        return LocalRedirect("/admin/content/header-menu");
    }

    /// <summary>حذف (نرم؛ از سطل بازیافت برمی‌گردد). «فروشگاه» (مگامنو) حذف نمی‌شود؛ فقط خاموش.</summary>
    [AdminPermission(AdminPermissions.ContentManage)]
    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var item = await data.Query<MenuItem>().FirstOrDefaultAsync(i => i.Id == id, HttpContext.RequestAborted);
        if (item is null || !await IsHeaderItemAsync(item.MenuId))
        {
            ShowResult(OperationResult.NotFound("آیتم منو پیدا نشد."));
            return LocalRedirect("/admin/content/header-menu");
        }
        if (item.Type == MenuItemType.CategoriesMegaMenu)
        {
            ShowResult(OperationResult.Error("«مگامنوی دسته‌بندی‌ها» حذف نمی‌شود؛ اگر نمی‌خواهید دیده شود، خاموشش کنید."));
            return LocalRedirect("/admin/content/header-menu");
        }
        ShowResult(await data.DeleteAsync<MenuItem>(id));
        return LocalRedirect("/admin/content/header-menu");
    }

    private Task<bool> IsHeaderItemAsync(Guid menuId) =>
        data.Query<Menu>().AnyAsync(m => m.Id == menuId && m.Location == MenuLocation.HeaderMain, HttpContext.RequestAborted);
}

/// <summary>یک لینک آمادهٔ «افزودن سریع».</summary>
public sealed record HeaderMenuPreset(string Key, string Title, string Url, string? Badge, string Hint);

/// <summary>نتیجهٔ پاک‌سازی ورودی فرم منو (Error پر ← ورودی نامعتبر).</summary>
public sealed record HeaderMenuInput(string? Title, string? Url, string? Badge, string? Error);

/// <summary>
/// دور ۲۹ (#۲۷) – قواعد خالص منوی بالای سایت (آزمون‌پذیر): اعتبارسنجی عنوان/آدرس/برچسب، جابه‌جایی و لینک‌های آماده.
/// </summary>
public static class HeaderMenuRules
{
    /// <summary>بیشترین لینک سطح اول (بیشتر از این در نوار هدر دسکتاپ جا نمی‌شود).</summary>
    public const int MaxItems = 12;

    /// <summary>لینک‌های پرکاربرد برای «افزودن سریع». مثال خواستهٔ کاربر: «تخفیف‌دارها».</summary>
    public static readonly IReadOnlyList<HeaderMenuPreset> Presets =
    [
        new("discounted", "تخفیف‌دارها", "/search?discounted=true", "٪", "همهٔ کالاهای دارای تخفیف"),
        new("best-selling", "پرفروش‌ترین‌ها", "/search?sort=BestSelling", null, "کالاهایی که بیشتر خریده شده‌اند"),
        new("newest", "جدیدترین‌ها", "/search?sort=Newest", "جدید", "تازه‌ترین کالاهای فروشگاه"),
        new("most-discounted", "بیشترین تخفیف", "/search?sort=MostDiscounted", null, "به ترتیب درصد تخفیف"),
        new("available", "فقط موجودها", "/search?available=true", null, "کالاهای آمادهٔ ارسال"),
        new("blog", "مجلهٔ خرید", "/blog", null, "راهنمای خرید و مقاله‌ها"),
        new("track", "پیگیری سفارش", "/track-order", null, "پیگیری بدون ورود"),
        new("contact", "تماس با ما", "/contact-us", null, "راه‌های ارتباط"),
    ];

    /// <summary>
    /// پاک‌سازی ورودی. عنوان ۲ تا ۴۰ نویسه؛ آدرس فقط «/مسیر» داخلی یا http(s)؛ برچسب حداکثر ۱۲ نویسه.
    /// مگامنو (فروشگاه) می‌تواند آدرس خالی داشته باشد (پیش‌فرض /products).
    /// مثال: («  تخفیف‌دارها »، «/search?discounted=true»، «٪») ← معتبر؛ («x»، «javascript:alert(1)»، null) ← خطا.
    /// </summary>
    public static HeaderMenuInput Clean(string? title, string? url, string? badge, bool megaMenu)
    {
        var t = (title ?? "").Trim();
        if (t.Length < 2 || t.Length > 40)
            return new HeaderMenuInput(null, null, null, "عنوان لینک باید ۲ تا ۴۰ نویسه باشد.");
        var u = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        if (u is null && !megaMenu)
            return new HeaderMenuInput(null, null, null, "آدرس لینک را بنویسید (مثلاً /search?discounted=true).");
        if (u is not null && (u.Length > 500 || !UrlSafety.IsSafeLink(u) || u.Any(char.IsWhiteSpace)))
            return new HeaderMenuInput(null, null, null, "آدرس باید با «/» شروع شود (لینک داخلی) یا یک آدرس کامل http(s) باشد.");
        var b = string.IsNullOrWhiteSpace(badge) ? null : badge.Trim();
        if (b is not null && b.Length > 12)
            return new HeaderMenuInput(null, null, null, "برچسب کنار لینک حداکثر ۱۲ نویسه است (مثلاً «جدید» یا «٪»).");
        return new HeaderMenuInput(t, u, b, null);
    }

    /// <summary>
    /// ترتیب تازه بعد از جابه‌جایی یک پله. مثال: ([a، b، c]، b، -1) ← [b، a، c]؛ جابه‌جایی بیرون از لبه ← بدون تغییر.
    /// </summary>
    public static List<Guid> Move(IReadOnlyList<Guid> order, Guid id, int step)
    {
        var list = order.ToList();
        var index = list.IndexOf(id);
        var target = index + Math.Sign(step);
        if (index < 0 || step == 0 || target < 0 || target >= list.Count)
            return list;
        (list[index], list[target]) = (list[target], list[index]);
        return list;
    }
}
