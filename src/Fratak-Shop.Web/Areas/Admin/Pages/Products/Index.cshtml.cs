using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Brands;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Smart;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Products;

public sealed class AdminProductRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Slug { get; init; } = "";
    public string SKU { get; init; } = "";
    public string? ImageName { get; init; }
    public string CategoryName { get; init; } = "";
    public string? BrandName { get; init; }
    public string? SupplierName { get; init; }
    public string? SupplierUrl { get; init; }
    public decimal Price { get; init; }
    public decimal? DiscountPrice { get; init; }
    public decimal? PurchasePrice { get; init; }
    public int Stock { get; init; }
    public int SalesCount { get; init; }
    public int ViewCount { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>امتیاز کامل بودن (مرحلهٔ هفتم)؛ بعد از کوئری اصلی برای ردیف‌های همین صفحه پر می‌شود.</summary>
    public CompletenessResult? Completeness { get; set; }

    /// <summary>دور ۲۳: پیش‌نویس «آمادهٔ انتشار» (<see cref="ProductCompleteness.ReadyToPublish"/>)؛ برچسب سبز در ستون وضعیت.</summary>
    public bool Ready { get; set; }

    /// <summary>
    /// دور ۲۶: پیش‌نویسی که عکس تأییدشده ندارد ولی دستیار عکس پیشنهادی پیدا کرده و منتظر تأیید کارشناس است
    /// (کار هوشمند «منتظر تأیید»). برچسب «منتظر تأیید عکس» با لینک صفحهٔ تکمیل هوشمند همان کالا.
    /// </summary>
    public bool AwaitingImages { get; set; }
}

[AdminPermission(AdminPermissions.ProductsView, AdminPermissions.SeoManage)]
public class IndexModel(IAdminDataService data, IProductService products, IStoreSettingService settings, ISmartTaskQueue smartQueue,
    ISmartCatalogService catalog, Microsoft.Extensions.Caching.Memory.IMemoryCache cache) : AdminPageModel
{
    /// <summary>
    /// دور ۲۸ (#۹۸ – «بازگردانی تغییر»): آخرین تغییر گروهی هر کارمند ۳۰ دقیقه در حافظه می‌ماند تا با دکمهٔ «بازگردانی» برگردد.
    /// Kind: «category» (دستهٔ قبلی هر کالا) یا «activation» (فعال/غیرفعال قبلی). Target: دستهٔ مقصد (برای بررسی اینکه کسی بعداً عوضش نکرده).
    /// </summary>
    public sealed record BulkUndo(string Kind, Guid? Target, IReadOnlyList<(Guid ProductId, Guid OldCategoryId)> Categories, IReadOnlyList<(Guid ProductId, bool WasActive)> Activations, Guid? ActorId);

    /// <summary>رمز تغییر قابل بازگردانی (از TempData)؛ null = چیزی برای بازگردانی نیست.</summary>
    public string? UndoToken { get; private set; }

    private static string UndoKey(string token) => "admin:undo:" + token;

    private void RememberUndo(BulkUndo undo)
    {
        var token = Convert.ToHexString(global::System.Security.Cryptography.RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        cache.Set(UndoKey(token), undo, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30), Size = 1 });
        TempData["UndoToken"] = token;
    }

    /// <summary>دور ۲۲: سقف «تغییر تأمین‌کنندهٔ همهٔ نتایج فیلتر» در یک بار (برای فروشگاه‌های بزرگ‌تر فیلتر را محدودتر کنید).</summary>
    public const int MaxSupplierAll = 50_000;

    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Category { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Brand { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Supplier { get; set; }
    [BindProperty(SupportsGet = true)] public string? State { get; set; }
    [BindProperty(SupportsGet = true)] public string? Stock { get; set; }
    [BindProperty(SupportsGet = true)] public string? Missing { get; set; }
    [BindProperty(SupportsGet = true)] public string Sort { get; set; } = "newest";

    public PagedList<AdminProductRow> Products { get; private set; } = new([], 0, 1, 30);
    public List<(Guid Id, string Name)> Categories { get; private set; } = [];

    /// <summary>دور ۲۸ (#۹۵): دسته‌های مجاز برای «تغییر دسته» گروهی (بدون دستهٔ سیستمی).</summary>
    public List<(Guid Id, string Name)> MoveTargets { get; private set; } = [];
    public List<(Guid Id, string Name)> Brands { get; private set; } = [];
    public List<(Guid Id, string Name)> Suppliers { get; private set; } = [];
    public int LowStockThreshold { get; private set; }
    public Dictionary<string, int> Counts { get; private set; } = new();

    public async Task OnGetAsync([FromQuery(Name = "page")] int pageNumber = 1)
    {
        UndoToken = TempData["UndoToken"] as string;
        LowStockThreshold = (await settings.GetAsync()).LowStockThreshold;
        var query = Filter();
        var page = ClampPage(pageNumber);
        var total = await query.CountAsync();
        var rows = await Project(SortQuery(query)).Skip((page - 1) * 30).Take(30).ToListAsync();
        Products = new PagedList<AdminProductRow>(rows, total, page, 30);

        // امتیاز کامل بودن: یک کوئری سبک (فقط طول‌ها و شمارش‌ها) برای ۳۰ ردیف همین صفحه.
        var ids = rows.Select(r => r.Id).ToList();
        var inputs = await data.Query<Product>().Where(p => ids.Contains(p.Id)).Select(ProductCompleteness.Projection).ToListAsync();
        var scores = inputs.ToDictionary(i => i.ProductId, ProductCompleteness.Evaluate);
        var ready = (await data.Query<Product>().Where(p => ids.Contains(p.Id)).Where(ProductCompleteness.ReadyToPublish).Select(p => p.Id).ToListAsync()).ToHashSet();
        var noImage = inputs.Where(i => i.ImageCount == 0).Select(i => i.ProductId).ToList();
        var awaiting = noImage.Count == 0
            ? new HashSet<Guid>()
            : (await data.Query<Fratak_Shop.Data.Entities.Smart.SmartTask>()
                .Where(t => t.TargetId != null && noImage.Contains(t.TargetId.Value) && t.Status == Fratak_Shop.Data.Entities.Smart.SmartTaskStatus.NeedsReview)
                .Select(t => t.TargetId!.Value).Distinct().ToListAsync()).ToHashSet();
        foreach (var row in rows)
        {
            row.Completeness = scores.GetValueOrDefault(row.Id);
            row.Ready = ready.Contains(row.Id);
            row.AwaitingImages = !row.IsActive && awaiting.Contains(row.Id);
        }

        var all = data.Query<Product>();
        Counts = new Dictionary<string, int>
        {
            ["all"] = await all.CountAsync(),
            ["inactive"] = await all.CountAsync(p => !p.IsActive),
            ["out"] = await all.CountAsync(p => p.IsActive && p.StockQuantity <= 0),
            ["low"] = await all.CountAsync(p => p.IsActive && p.StockQuantity > 0 && p.StockQuantity <= LowStockThreshold),
            ["source"] = await all.CountAsync(p => p.SupplierUrl == null || p.SupplierUrl == ""),
            // دور ۲۲: کالای «دسته‌بندی نشده» قابل انتشار نیست؛ این عدد باید صفر بماند (دسته‌بندی هوشمند خودکار کمک می‌کند).
            ["category"] = await all.CountAsync(p => p.Category.IsSystem),
            // دور ۲۳: پیش‌نویس‌های کامل که فقط منتظر انتشار مدیرند (سیستم خودش منتشر نمی‌کند).
            ["ready"] = await all.CountAsync(ProductCompleteness.ReadyToPublish),
        };

        var categories = await data.Query<Category>().OrderBy(c => c.Name).Select(c => new { c.Id, c.Name, c.IsSystem, Parent = c.ParentCategory != null ? c.ParentCategory.Name : null }).ToListAsync();
        Categories = categories.Select(c => (c.Id, c.Parent is null ? c.Name : $"{c.Parent} › {c.Name}")).OrderBy(c => c.Item2).ToList();
        // دور ۲۸ (#۹۵): مقصد «تغییر دسته» هرگز دستهٔ سیستمی نیست.
        var systemIds = categories.Where(c => c.IsSystem).Select(c => c.Id).ToHashSet();
        MoveTargets = Categories.Where(c => !systemIds.Contains(c.Id)).ToList();
        var brands = await data.Query<Brand>().OrderBy(b => b.Name).Select(b => new { b.Id, b.Name }).ToListAsync();
        Brands = brands.Select(b => (b.Id, b.Name)).ToList();
        if (Can(AdminPermissions.ProductsSupplier))
        {
            var suppliers = await data.Query<Data.Entities.Products.Supplier>().OrderBy(s => s.Name).Select(s => new { s.Id, s.Name }).ToListAsync();
            Suppliers = suppliers.Select(s => (s.Id, s.Name)).ToList();
        }
    }

    public async Task<IActionResult> OnGetExportAsync(string format = "xlsx")
    {
        LowStockThreshold = (await settings.GetAsync()).LowStockThreshold;
        var rows = await Project(SortQuery(Filter())).Take(20_000).ToListAsync();
        var withSupplier = Can(AdminPermissions.ProductsSupplier);
        var columns = new List<string> { "نام", "کد کالا", "دسته", "برند", "قیمت", "قیمت با تخفیف", "موجودی", "فروش", "بازدید", "وضعیت", "تاریخ ثبت" };
        if (withSupplier)
            columns.AddRange(["قیمت خرید", "تأمین‌کننده", "لینک منبع"]);

        var table = new ExportTable("محصولات", [.. columns]);
        foreach (var p in rows)
        {
            var cells = new List<object?> { p.Name, p.SKU, p.CategoryName, p.BrandName, p.Price, p.DiscountPrice, p.Stock, p.SalesCount, p.ViewCount, p.IsActive ? "فعال" : "غیرفعال", p.CreatedAt };
            if (withSupplier)
                cells.AddRange([p.PurchasePrice, p.SupplierName, p.SupplierUrl]);
            table.Add([.. cells]);
        }
        return Exporter.File(table, format, "products");
    }

    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostStockAsync(Guid id, int stock)
    {
        ShowResult(await products.UpdateStockAsync(id, stock));
        return BackToList();
    }

    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostBulkAsync(List<Guid> ids, string action, string? supplierTarget = null, Guid? categoryTarget = null)
    {
        ids = ids.Distinct().Take(200).ToList();
        if (ids.Count == 0)
        {
            ErrorMessage = "هیچ محصولی انتخاب نشده است.";
            return BackToList();
        }

        var done = 0;
        switch (action)
        {
            case "activate":
            case "deactivate":
            {
                // دور ۲۸ (#۹۸): وضعیت قبلی برای «بازگردانی» نگه داشته می‌شود.
                var before = await data.Query<Product>().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.IsActive }).ToListAsync();
                var changed = new List<(Guid, bool)>();
                foreach (var row in before)
                {
                    if (row.IsActive == (action == "activate"))
                        continue;
                    if (Succeeded(await products.ChangeProductActivationAsync(row.Id, action == "activate")))
                    {
                        done++;
                        changed.Add((row.Id, row.IsActive));
                    }
                }
                SuccessMessage = $"{done} محصول {(action == "activate" ? "فعال" : "غیرفعال")} شد.";
                if (changed.Count > 0)
                    RememberUndo(new BulkUndo("activation", null, [], changed, ActorId));
                break;
            }

            case "category":
            {
                // دور ۲۸ (#۹۵): تغییر گروهی دسته (با امکان بازگردانی).
                if (categoryTarget is not { } destination)
                {
                    ErrorMessage = "دستهٔ مقصد را انتخاب کنید.";
                    break;
                }
                var moved = await products.ChangeCategoryAsync(ids, destination);
                if (moved.IsSuccess() && moved.Data is { Count: > 0 } undo)
                    RememberUndo(new BulkUndo("category", destination, undo, [], ActorId));
                if (moved.IsSuccess())
                    SuccessMessage = moved.Message;
                else
                    ErrorMessage = moved.Message;
                break;
            }

            case "reprepare":
            {
                // دور ۲۸ (#۶۶): «آماده‌سازی دوباره» انتخاب‌شده‌ها – همین حالا تکمیل هوشمند کامل (بدون «لازم نیست») در صف می‌رود.
                var drafts = await data.Query<Product>().Where(p => ids.Contains(p.Id) && !p.IsActive).Select(p => new { p.Id, p.Name }).ToListAsync();
                foreach (var item in drafts)
                {
                    await smartQueue.EnqueueAsync(SmartTaskKind.ProductComplete, SmartTarget.Product, item.Id, item.Name, SmartPriority.Manual,
                        "{\"manual\":1}", ActorId, ActorName, HttpContext.RequestAborted);
                    done++;
                }
                SuccessMessage = done == 0
                    ? "فقط پیش‌نویس‌ها (کالای منتشرنشده) آماده‌سازی می‌شوند."
                    : $"آماده‌سازی دوبارهٔ {Format.Number(done)} پیش‌نویس در صف رفت؛ نتیجهٔ هر کدام اعلان می‌شود.";
                break;
            }

            case "smart":
                // دور ۲۲: «تکمیل هوشمند» گروهی (همان دکمهٔ «پیشنهاد هوشمند» هر کالا، برای چند کالا با هم).
                var names = await data.Query<Product>().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.Name }).ToListAsync();
                foreach (var item in names)
                {
                    await smartQueue.EnqueueAsync(SmartTaskKind.ProductComplete, SmartTarget.Product, item.Id, item.Name, SmartPriority.Batch, null, ActorId, ActorName,
                        HttpContext.RequestAborted);
                    done++;
                }
                SuccessMessage = $"تکمیل هوشمند {Format.Number(done)} محصول در صف رفت؛ نتیجهٔ هر کدام اعلان می‌شود.";
                break;

            case "supplier":
                if (!Can(AdminPermissions.ProductsSupplier))
                    return Redirect("/admin/denied?need=" + AdminPermissions.ProductsSupplier);
                if (!TryParseSupplierTarget(supplierTarget, out var target))
                {
                    ErrorMessage = "تأمین‌کنندهٔ مقصد را انتخاب کنید.";
                    break;
                }
                ShowResult(await products.ChangeSupplierAsync(ids, target));
                break;

            case "delete":
                if (!Can(AdminPermissions.ProductsDelete))
                    return Redirect("/admin/denied?need=" + AdminPermissions.ProductsDelete);
                foreach (var id in ids)
                    if (Succeeded(await products.DeleteProductAsync(id)))
                        done++;
                SuccessMessage = $"{done} محصول حذف شد (قابل بازیابی از پایگاه داده).";
                break;

            default:
                ErrorMessage = "عملیات نامعتبر است.";
                break;
        }

        return BackToList();
    }

    /// <summary>
    /// دور ۲۳: «انتشار همهٔ آماده‌ها» – همهٔ پیش‌نویس‌های «آمادهٔ انتشار» (حداکثر ۲۰۰ در هر بار) با همان مسیر انتشار عادی
    /// (<see cref="IProductService.ChangeProductActivationAsync"/>؛ محافظ دسته و ثبت تاریخچه سر جایش است) منتشر می‌شوند.
    /// فقط با کلیک و تأیید مدیر؛ هیچ انتشار خودکاری در کار نیست.
    /// </summary>
    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostPublishReadyAsync()
    {
        var ids = await data.Query<Product>().Where(ProductCompleteness.ReadyToPublish).OrderByDescending(p => p.CreatedAt).Select(p => p.Id).Take(200).ToListAsync();
        var done = 0;
        foreach (var id in ids)
            if (Succeeded(await products.ChangeProductActivationAsync(id, true)))
                done++;
        if (ids.Count == 0)
            ErrorMessage = "کالای آمادهٔ انتشاری نیست.";
        else
            SuccessMessage = $"{Format.Number(done)} کالای آماده منتشر شد.";
        return BackToList();
    }

    /// <summary>
    /// دور ۲۸ (#۶۶) – «آماده‌سازی دوباره همه»: شمارش تلاش همهٔ پیش‌نویس‌های ناآماده صفر می‌شود و حلقهٔ آماده‌سازی (هر ۱۰ دقیقه،
    /// هر بار ۱۰ کالا) دوباره از اول سراغشان می‌رود. هیچ کالایی منتشر نمی‌شود.
    /// </summary>
    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostReprepareAllAsync()
    {
        var count = await catalog.ResetReadinessAsync(HttpContext.RequestAborted);
        SuccessMessage = count == 0
            ? "همهٔ پیش‌نویس‌ها آمادهٔ انتشارند؛ چیزی برای آماده‌سازی نماند."
            : $"آماده‌سازی دوبارهٔ {Format.Number(count)} پیش‌نویس شروع شد (هر ۱۰ دقیقه ۱۰ کالا؛ نتیجه اعلان می‌شود).";
        return BackToList();
    }

    /// <summary>
    /// دور ۲۸ (#۹۸) – «بازگردانی آخرین تغییر گروهی» (فقط همان کارمند و تا ۳۰ دقیقه). کالایی که در این فاصله کس دیگری
    /// تغییرش داده دست نمی‌خورد.
    /// </summary>
    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostUndoAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 40 || !cache.TryGetValue<BulkUndo>(UndoKey(token), out var undo) || undo is null || undo.ActorId != ActorId)
        {
            ErrorMessage = "مهلت بازگردانی تمام شده یا این تغییر مال شما نیست.";
            return BackToList();
        }
        cache.Remove(UndoKey(token));
        if (undo.Kind == "category" && undo.Target is { } target)
        {
            ShowResult(await products.RestoreCategoriesAsync(undo.Categories, target));
        }
        else
        {
            var done = 0;
            var undoIds = undo.Activations.Select(a => a.ProductId).ToList();
            var current = (await data.Query<Product>().Where(p => undoIds.Contains(p.Id))
                .Select(p => new { p.Id, p.IsActive }).ToListAsync()).ToDictionary(p => p.Id, p => p.IsActive);
            foreach (var (id, wasActive) in undo.Activations)
            {
                // فقط اگر هنوز همان حالتی است که ما گذاشتیم.
                if (current.TryGetValue(id, out var now) && now != wasActive && Succeeded(await products.ChangeProductActivationAsync(id, wasActive)))
                    done++;
            }
            SuccessMessage = $"وضعیت {Format.Number(done)} محصول به حالت قبل برگشت.";
        }
        return BackToList();
    }

    /// <summary>
    /// دور ۲۲: تغییر تأمین‌کنندهٔ «همهٔ محصولات همین فهرست» (همهٔ صفحه‌ها، با همان فیلترهای فعلی که در فرم پنهان فرستاده می‌شوند).
    /// supplierTarget: شناسهٔ تأمین‌کننده یا «none» = بدون تأمین‌کننده. حداکثر <see cref="MaxSupplierAll"/> کالا در یک بار.
    /// </summary>
    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostSupplierAllAsync(string? supplierTarget)
    {
        if (!Can(AdminPermissions.ProductsSupplier))
            return Redirect("/admin/denied?need=" + AdminPermissions.ProductsSupplier);
        if (!TryParseSupplierTarget(supplierTarget, out var target))
        {
            ErrorMessage = "تأمین‌کنندهٔ مقصد را انتخاب کنید.";
            return BackToList();
        }
        LowStockThreshold = (await settings.GetAsync()).LowStockThreshold;
        var ids = await Filter().OrderBy(p => p.Id).Select(p => p.Id).Take(MaxSupplierAll + 1).ToListAsync();
        if (ids.Count == 0)
        {
            ErrorMessage = "محصولی در این فهرست نیست.";
            return BackToList();
        }
        if (ids.Count > MaxSupplierAll)
        {
            ErrorMessage = $"این فهرست بیش از {Format.Number(MaxSupplierAll)} محصول دارد؛ با فیلتر (دسته، برند …) محدودترش کنید.";
            return BackToList();
        }
        var changed = 0;
        foreach (var chunk in ids.Chunk(ProductService.MaxSupplierBatch))
        {
            var result = await products.ChangeSupplierAsync(chunk, target);
            if (!result.IsSuccess())
            {
                ShowResult(result);
                return BackToList();
            }
            changed += result.Data;
        }
        SuccessMessage = target is null
            ? $"تأمین‌کنندهٔ {Format.Number(changed)} محصول برداشته شد."
            : $"تأمین‌کنندهٔ {Format.Number(changed)} محصول تغییر کرد (از {Format.Number(ids.Count)} محصول فهرست؛ بقیه از قبل همین تأمین‌کننده را داشتند).";
        return BackToList();
    }

    /// <summary>«none» ← null (بدون تأمین‌کننده)؛ شناسهٔ معتبر ← همان؛ خالی/نامعتبر ← false. مثال: «none» ← (true، null).</summary>
    public static bool TryParseSupplierTarget(string? value, out Guid? supplierId)
    {
        supplierId = null;
        if (string.Equals(value, "none", StringComparison.Ordinal))
            return true;
        if (Guid.TryParse(value, out var id) && id != Guid.Empty)
        {
            supplierId = id;
            return true;
        }
        return false;
    }

    private IQueryable<Product> Filter()
    {
        var query = data.Query<Product>();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = SearchText.NormalizeDigits(Search.Trim());
            query = query.Where(p => p.Name.Contains(term) || p.SKU.Contains(term) || p.Slug.Contains(term)
                                     || (p.EnglishName != null && p.EnglishName.Contains(term))
                                     || (p.SupplierSku != null && p.SupplierSku == term));
        }
        if (Category is { } categoryId)
            query = query.Where(p => p.CategoryId == categoryId || p.Category.ParentId == categoryId);
        if (Brand is { } brandId)
            query = query.Where(p => p.BrandId == brandId);
        if (Supplier is { } supplierId && Can(AdminPermissions.ProductsSupplier))
            query = query.Where(p => p.SupplierId == supplierId);
        query = State switch
        {
            "active" => query.Where(p => p.IsActive),
            "inactive" => query.Where(p => !p.IsActive),
            _ => query,
        };
        var low = LowStockThreshold;
        query = Stock switch
        {
            "out" => query.Where(p => p.StockQuantity <= 0),
            "low" => query.Where(p => p.StockQuantity > 0 && p.StockQuantity <= low),
            "in" => query.Where(p => p.StockQuantity > 0),
            _ => query,
        };
        query = Missing switch
        {
            "source" => query.Where(p => p.SupplierUrl == null || p.SupplierUrl == ""),
            "image" => query.Where(p => !p.Images.Any()),
            "seo" => query.Where(p => p.Seo == null || p.Seo.MetaDescription == null || p.Seo.MetaDescription == ""),
            "cost" => query.Where(p => p.PurchasePrice == null),
            "category" => query.Where(p => p.Category.IsSystem),
            "short" => query.Where(p => p.ShortDescription == null || p.ShortDescription == ""),
            "ready" => query.Where(ProductCompleteness.ReadyToPublish),
            _ => query,
        };
        return query;
    }

    private IQueryable<Product> SortQuery(IQueryable<Product> query) => Sort switch
    {
        "name" => query.OrderBy(p => p.Name),
        "price" => query.OrderByDescending(p => p.Price),
        "price-asc" => query.OrderBy(p => p.Price),
        "stock" => query.OrderBy(p => p.StockQuantity).ThenBy(p => p.Name),
        "sales" => query.OrderByDescending(p => p.SalesCount),
        "views" => query.OrderByDescending(p => p.ViewCount),
        "oldest" => query.OrderBy(p => p.CreatedAt),
        _ => query.OrderByDescending(p => p.CreatedAt),
    };

    private static IQueryable<AdminProductRow> Project(IQueryable<Product> query) => query.Select(p => new AdminProductRow
    {
        Id = p.Id,
        Name = p.Name,
        Slug = p.Slug,
        SKU = p.SKU,
        ImageName = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).FirstOrDefault(),
        CategoryName = p.Category.Name,
        BrandName = p.Brand != null ? p.Brand.Name : null,
        SupplierName = p.Supplier != null ? p.Supplier.Name : null,
        SupplierUrl = p.SupplierUrl,
        Price = p.Price,
        DiscountPrice = p.DiscountPrice,
        PurchasePrice = p.PurchasePrice,
        Stock = p.StockQuantity,
        SalesCount = p.SalesCount,
        ViewCount = p.ViewCount,
        IsActive = p.IsActive,
        CreatedAt = p.CreatedAt,
    });
}
