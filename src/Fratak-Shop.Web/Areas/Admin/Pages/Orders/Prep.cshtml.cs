using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Orders;

/// <summary>یک ردیف فهرست برداشت: یک کالا با جمع تعداد در همهٔ سفارش‌های در جریان.</summary>
public sealed record PickRow(Guid? ProductId, string Name, string Sku, string? Supplier, int Quantity, int Orders, int? Stock, List<(Guid Id, string Number)> OrderRefs)
{
    /// <summary>موجودی انبار کمتر از نیاز است (باید از تأمین‌کننده خرید شود).</summary>
    public bool Short => Stock is { } stock && stock < 0;
}

/// <summary>
/// دور ۲۴ (پیشنهاد ۶۸): داشبورد آماده‌سازی (<c>/admin/orders/prep</c>).
/// <para>
/// به‌جای باز کردن تک‌تک سفارش‌ها، کارمند یک «فهرست برداشت» تجمیعی می‌بیند: هر کالا یک ردیف با جمع تعداد در همهٔ سفارش‌های
/// «تأییدشده» (باید از تأمین‌کننده خریده شود) یا «در حال آماده‌سازی» (باید بسته‌بندی شود)، به تفکیک تأمین‌کننده،
/// همراه شمارهٔ سفارش‌ها. خروجی اکسل/CSV و چاپ دارد. چیزی تغییر نمی‌کند (فقط خواندن).
/// </para>
/// <para>مثال: ۳ سفارش هر کدام ۲ «کابل تایپ‌سی» ← یک ردیف «کابل تایپ‌سی × ۶ (۳ سفارش)».</para>
/// </summary>
[AdminPermission(AdminPermissions.OrdersView)]
public class PrepModel(IAdminDataService data) : AdminPageModel
{
    /// <summary>حداکثر سفارش در جریان که خوانده می‌شود (هاست ضعیف).</summary>
    public const int MaxOrders = 500;

    /// <summary>مرحله: confirmed (خرید از تأمین‌کننده)، processing (بسته‌بندی) یا all.</summary>
    [BindProperty(SupportsGet = true)] public string Stage { get; set; } = "all";

    public List<PickRow> Rows { get; private set; } = [];
    public int Confirmed { get; private set; }
    public int Processing { get; private set; }
    public int AwaitingTracking { get; private set; }
    public DateTimeOffset? Oldest { get; private set; }
    public bool ShowSupplier => Can(AdminPermissions.ProductsSupplier);

    public async Task OnGetAsync()
    {
        Normalize();
        var open = data.Query<Order>();
        Confirmed = await open.CountAsync(o => o.Status == OrderStatus.Confirmed);
        Processing = await open.CountAsync(o => o.Status == OrderStatus.Processing);
        AwaitingTracking = await open.CountAsync(o => o.Status == OrderStatus.Shipped && (o.TrackingCode == null || o.TrackingCode == ""));
        Oldest = await open.Where(o => o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Processing)
            .OrderBy(o => o.CreatedAt).Select(o => (DateTimeOffset?)o.CreatedAt).FirstOrDefaultAsync();
        Rows = await LoadAsync();
    }

    /// <summary>خروجی اکسل/CSV فهرست برداشت. مثال: /admin/orders/prep?handler=Export&amp;format=csv&amp;stage=confirmed</summary>
    public async Task<IActionResult> OnGetExportAsync(string? format)
    {
        Normalize();
        var rows = await LoadAsync();
        var table = ShowSupplier
            ? new ExportTable("فهرست برداشت", "کالا", "کد", "تأمین‌کننده", "تعداد", "سفارش‌ها", "موجودی", "شمارهٔ سفارش‌ها")
            : new ExportTable("فهرست برداشت", "کالا", "کد", "تعداد", "سفارش‌ها", "موجودی", "شمارهٔ سفارش‌ها");
        foreach (var row in rows)
        {
            var numbers = string.Join(" ", row.OrderRefs.Select(o => o.Number));
            if (ShowSupplier)
                table.Add(row.Name, row.Sku, row.Supplier, row.Quantity, row.Orders, row.Stock, numbers);
            else
                table.Add(row.Name, row.Sku, row.Quantity, row.Orders, row.Stock, numbers);
        }
        return Exporter.File(table, format ?? "xlsx", "pick-list");
    }

    private void Normalize()
    {
        if (Stage is not ("confirmed" or "processing" or "all"))
            Stage = "all";
    }

    private async Task<List<PickRow>> LoadAsync()
    {
        var statuses = Stage switch
        {
            "confirmed" => new[] { OrderStatus.Confirmed },
            "processing" => new[] { OrderStatus.Processing },
            _ => new[] { OrderStatus.Confirmed, OrderStatus.Processing },
        };
        var orderIds = await data.Query<Order>().Where(o => statuses.Contains(o.Status))
            .OrderBy(o => o.CreatedAt).Take(MaxOrders).Select(o => o.Id).ToListAsync();
        var items = await data.Query<OrderItem>()
            .Where(i => orderIds.Contains(i.OrderId))
            .Select(i => new
            {
                i.ProductId,
                i.ProductName,
                i.SKU,
                i.Quantity,
                i.OrderId,
                i.Order.OrderNumber,
                Supplier = i.Product != null && i.Product.Supplier != null ? i.Product.Supplier.Name : null,
                Stock = i.Product != null ? (int?)i.Product.StockQuantity : null,
            })
            .ToListAsync();
        return items
            .GroupBy(i => i.ProductId is { } id ? id.ToString() : "n:" + i.ProductName)
            .Select(g => new PickRow(
                g.First().ProductId,
                g.First().ProductName,
                g.First().SKU,
                g.First().Supplier,
                g.Sum(i => i.Quantity),
                g.Select(i => i.OrderId).Distinct().Count(),
                g.First().Stock,
                g.GroupBy(i => i.OrderId).Select(o => (o.Key, o.First().OrderNumber)).OrderBy(o => o.Item2, StringComparer.Ordinal).ToList()))
            .OrderBy(r => r.Supplier ?? "\uffff", StringComparer.Ordinal)
            .ThenByDescending(r => r.Quantity)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
    }
}
