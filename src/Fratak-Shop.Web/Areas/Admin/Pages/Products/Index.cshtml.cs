using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Brands;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
}

[AdminPermission(AdminPermissions.ProductsView, AdminPermissions.SeoManage)]
public class IndexModel(IAdminDataService data, IProductService products, IStoreSettingService settings) : AdminPageModel
{
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
    public List<(Guid Id, string Name)> Brands { get; private set; } = [];
    public List<(Guid Id, string Name)> Suppliers { get; private set; } = [];
    public int LowStockThreshold { get; private set; }
    public Dictionary<string, int> Counts { get; private set; } = new();

    public async Task OnGetAsync([FromQuery(Name = "page")] int pageNumber = 1)
    {
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
        foreach (var row in rows)
            row.Completeness = scores.GetValueOrDefault(row.Id);

        var all = data.Query<Product>();
        Counts = new Dictionary<string, int>
        {
            ["all"] = await all.CountAsync(),
            ["inactive"] = await all.CountAsync(p => !p.IsActive),
            ["out"] = await all.CountAsync(p => p.IsActive && p.StockQuantity <= 0),
            ["low"] = await all.CountAsync(p => p.IsActive && p.StockQuantity > 0 && p.StockQuantity <= LowStockThreshold),
            ["source"] = await all.CountAsync(p => p.SupplierUrl == null || p.SupplierUrl == ""),
        };

        var categories = await data.Query<Category>().OrderBy(c => c.Name).Select(c => new { c.Id, c.Name, Parent = c.ParentCategory != null ? c.ParentCategory.Name : null }).ToListAsync();
        Categories = categories.Select(c => (c.Id, c.Parent is null ? c.Name : $"{c.Parent} › {c.Name}")).OrderBy(c => c.Item2).ToList();
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
    public async Task<IActionResult> OnPostBulkAsync(List<Guid> ids, string action)
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
                foreach (var id in ids)
                    if (Succeeded(await products.ChangeProductActivationAsync(id, action == "activate")))
                        done++;
                SuccessMessage = $"{done} محصول {(action == "activate" ? "فعال" : "غیرفعال")} شد.";
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
