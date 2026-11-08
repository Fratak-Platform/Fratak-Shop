using System.Net;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Smart;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.E2E.Admin;

/// <summary>
/// دور ۲۲ از دید مرورگر: «تغییر گروهی تأمین‌کننده» و «تکمیل هوشمند گروهی» در فهرست محصولات پنل (برنامهٔ واقعی + SQL Server).
/// <list type="bullet">
/// <item>مالک چند کالا را تیک می‌زند و تأمین‌کننده را عوض می‌کند ← فقط همان کالاها عوض می‌شوند.</item>
/// <item>مقصد نامعتبر (متن دست‌کاری‌شده) ← هیچ چیز عوض نمی‌شود؛ «none» ← تأمین‌کننده برداشته می‌شود.</item>
/// <item>«همهٔ کالاهای این فهرست» فقط کالاهای مطابق فیلتر فعلی (جست‌وجو) را عوض می‌کند.</item>
/// <item>کارمند فروش (بدون مجوز تأمین‌کننده) ← صفحهٔ «دسترسی ندارید» و بدون تغییر.</item>
/// <item>تکمیل هوشمند گروهی ← برای هر کالا یک کار «تکمیل محصول» در صف با اولویت گروهی.</item>
/// </list>
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Admin")]
public class BulkSupplierE2ETests(ShopApp app)
{
    /// <summary>یک تأمین‌کنندهٔ تازهٔ آزمونی می‌سازد و شناسه‌اش را برمی‌گرداند.</summary>
    private Task<Guid> NewSupplierAsync() => app.WithDbAsync(async db =>
    {
        var supplier = new Supplier { Name = "تأمین‌کنندهٔ گروهی " + Guid.NewGuid().ToString("N")[..6], Website = "https://bulk.example", IsActive = true };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return supplier.Id;
    });

    private Task<Guid?> SupplierOfAsync(Guid productId) =>
        app.WithDbAsync(db => db.Products.Where(p => p.Id == productId).Select(p => p.SupplierId).SingleAsync());

    /// <summary>ارسال فرم گروهی همان‌طور که فرم فهرست می‌فرستد (ids تکراری برای هر تیک).</summary>
    private static Task<HttpResponseMessage> BulkAsync(ShopClient client, string action, string? target, params Guid[] ids)
    {
        var fields = ids.Select(id => ("ids", id.ToString())).ToList();
        fields.Add(("action", action));
        if (target is not null)
            fields.Add(("supplierTarget", target));
        return client.PostFormAsync("/admin/products?handler=Bulk", fields.ToArray());
    }

    [SqlFact]
    public async Task Owner_changes_the_supplier_of_selected_products_only()
    {
        var supplier = await NewSupplierAsync();
        var (a, _) = await app.CreateProductAsync();
        var (b, _) = await app.CreateProductAsync();
        var (other, _) = await app.CreateProductAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);

        using (var response = await BulkAsync(client, "supplier", supplier.ToString(), a, b))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        Assert.Equal(supplier, await SupplierOfAsync(a));
        Assert.Equal(supplier, await SupplierOfAsync(b));
        Assert.Null(await SupplierOfAsync(other));

        // «none» ← بدون تأمین‌کننده.
        using (var response = await BulkAsync(client, "supplier", "none", a))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await SupplierOfAsync(a));
        Assert.Equal(supplier, await SupplierOfAsync(b));
    }

    [SqlFact]
    public async Task Tampered_or_unknown_supplier_targets_change_nothing()
    {
        var supplier = await NewSupplierAsync();
        var (a, _) = await app.CreateProductAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);
        using (var response = await BulkAsync(client, "supplier", supplier.ToString(), a))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        // متن نامعتبر، شناسهٔ خالی، تأمین‌کنندهٔ ناموجود، بدون مقصد، تزریق ← همه بی‌اثر.
        foreach (var target in new[] { "garbage", Guid.Empty.ToString(), Guid.NewGuid().ToString(), "", "' OR 1=1 --" })
        {
            using var response = await BulkAsync(client, "supplier", target, a);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(supplier, await SupplierOfAsync(a));
        }
        using (var response = await BulkAsync(client, "supplier", null, a))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(supplier, await SupplierOfAsync(a));
    }

    [SqlFact]
    public async Task Supplier_for_all_products_respects_the_current_search_filter()
    {
        var supplier = await NewSupplierAsync();
        var (match, slug) = await app.CreateProductAsync();
        var (other, _) = await app.CreateProductAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);

        // فیلترهای فعلی فهرست (اینجا جست‌وجوی نامک) همراه فرم فرستاده می‌شوند؛ فقط همان کالا عوض می‌شود.
        using (var response = await client.PostFormAsync("/admin/products?handler=SupplierAll", ("q", slug), ("supplierTarget", supplier.ToString())))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(supplier, await SupplierOfAsync(match));
        Assert.Null(await SupplierOfAsync(other));

        // مقصد نامعتبر ← بی‌اثر.
        using (var response = await client.PostFormAsync("/admin/products?handler=SupplierAll", ("q", slug), ("supplierTarget", "garbage")))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(supplier, await SupplierOfAsync(match));

        // «none» با همان فیلتر ← برداشته می‌شود.
        using (var response = await client.PostFormAsync("/admin/products?handler=SupplierAll", ("q", slug), ("supplierTarget", "none")))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await SupplierOfAsync(match));
    }

    [SqlFact]
    public async Task Sales_staff_cannot_change_suppliers()
    {
        var supplier = await NewSupplierAsync();
        var (a, _) = await app.CreateProductAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.SalesManagerPhone);

        using (var response = await BulkAsync(client, "supplier", supplier.ToString(), a))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/admin/denied", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
        }
        using (var response = await client.PostFormAsync("/admin/products?handler=SupplierAll", ("supplierTarget", supplier.ToString())))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/admin/denied", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Null(await SupplierOfAsync(a));
    }

    [SqlFact]
    public async Task Bulk_smart_completion_queues_one_task_per_product()
    {
        var (a, _) = await app.CreateProductAsync();
        var (b, _) = await app.CreateProductAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);
        try
        {
            // دو بار پشت سر هم ← صف تکراری نمی‌سازد (هر کالا یک کار در انتظار).
            for (var i = 0; i < 2; i++)
            {
                using var response = await BulkAsync(client, "smart", null, a, b, a);
                Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            }
            var tasks = await app.WithDbAsync(db => db.SmartTasks
                .Where(t => t.TargetId == a || t.TargetId == b)
                .Where(t => t.Kind == SmartTaskKind.ProductComplete && t.Status == SmartTaskStatus.Queued)
                .Select(t => new { t.TargetId, t.Priority })
                .ToListAsync());
            Assert.Equal(2, tasks.Count);
            Assert.Single(tasks, t => t.TargetId == a);
            Assert.Single(tasks, t => t.TargetId == b);
            Assert.All(tasks, t => Assert.Equal(Fratak_Shop.Core.Services.Smart.SmartPriority.Batch, t.Priority));
        }
        finally
        {
            await app.WithDbAsync(db => db.SmartTasks.Where(t => t.TargetId == a || t.TargetId == b).ExecuteDeleteAsync());
        }
    }

    [SqlFact]
    public async Task Missing_category_filter_and_empty_selection_are_safe()
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);
        await client.GetPageAsync("/admin/products?missing=category");
        await client.GetPageAsync("/admin/products?missing=short");

        // بدون تیک یا با عملیات ناشناخته ← فقط پیام خطا و بازگشت؛ بدون خطای سرور.
        using (var response = await client.PostFormAsync("/admin/products?handler=Bulk", ("action", "supplier"), ("supplierTarget", "none")))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var (a, _) = await app.CreateProductAsync();
        using (var response = await BulkAsync(client, "drop-table", null, a))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(await app.WithDbAsync(db => db.Products.AnyAsync(p => p.Id == a)));
    }
}
