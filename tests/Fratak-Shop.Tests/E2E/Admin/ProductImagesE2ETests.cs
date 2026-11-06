using System.Net;
using AngleSharp.Dom;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.E2E.Admin;

/// <summary>
/// دور بیستم از دید مرورگر: حذف «همان تصویری که روی آن کلیک شد» در ویرایش محصول (برنامهٔ واقعی + SQL Server).
/// <para>فرم واقعی صفحه خوانده و همان‌طور که مرورگر می‌فرستد ارسال می‌شود (همهٔ فیلدهای پرشده + تغییر آزمون).</para>
/// <list type="bullet">
/// <item>حذف با نام (Input.RemovedImages) برای تصویر وسط ← فقط همان حذف می‌شود؛ تصویر آخر سالم می‌ماند (باگ قبلی: همیشه آخری پاک می‌شد).</item>
/// <item>روش قدیمی (تیک Images[n].Remove) هنوز کار می‌کند.</item>
/// <item>فرم با «جای خالی» در شماره‌ها (Images[1] نیامده) ← تصویرهای بعد از جای خالی بی‌صدا پاک نمی‌شوند.</item>
/// <item>نام دست‌کاری‌شده (مسیر ../ یا نام ناموجود) هیچ تصویری را پاک نمی‌کند.</item>
/// </list>
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Admin")]
public class ProductImagesE2ETests(ShopApp app)
{
    /// <summary>کالای آزمون با ۳ تصویر (a اصلی، b وسط، c آخر) و تأمین‌کننده/لینک منبع (برای ذخیرهٔ فرم لازم است).</summary>
    private async Task<(Guid Id, string A, string B, string C)> ProductWithImagesAsync()
    {
        var (id, _) = await app.CreateProductAsync();
        var key = Guid.NewGuid().ToString("N")[..8];
        var (a, b, c) = ($"e2e-{key}-a.webp", $"e2e-{key}-b.webp", $"e2e-{key}-c.webp");
        await app.WithDbAsync(async db =>
        {
            var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.IsActive);
            if (supplier is null)
            {
                supplier = new Supplier { Name = "تأمین‌کنندهٔ آزمون", Website = "https://supplier.example", IsActive = true };
                db.Suppliers.Add(supplier);
                await db.SaveChangesAsync();
            }
            var product = await db.Products.AsTracking().SingleAsync(p => p.Id == id);
            product.SupplierId = supplier.Id;
            product.SupplierUrl = "https://supplier.example/p/" + key;
            db.ProductImages.Add(new ProductImage { ProductId = id, ImageName = a, AltText = "تصویر یک", DisplayOrder = 1, IsPrimary = true });
            db.ProductImages.Add(new ProductImage { ProductId = id, ImageName = b, AltText = "تصویر دو", DisplayOrder = 2 });
            db.ProductImages.Add(new ProductImage { ProductId = id, ImageName = c, AltText = "تصویر سه", DisplayOrder = 3 });
            await db.SaveChangesAsync();
        });
        return (id, a, b, c);
    }

    private Task<List<string>> ImagesAsync(Guid id) =>
        app.WithDbAsync(db => db.ProductImages.Where(i => i.ProductId == id).OrderBy(i => i.DisplayOrder).Select(i => i.ImageName).ToListAsync());

    /// <summary>
    /// همهٔ فیلدهای فرم اصلی ویرایش همان‌طور که مرورگر می‌فرستد: input/textarea/select پرشده، چک‌باکس/رادیوی تیک‌خورده؛
    /// بدون فایل، دکمه و فیلد غیرفعال. توکن ضد جعل را PostFormAsync تازه اضافه می‌کند.
    /// </summary>
    private static List<(string Key, string Value)> FormFields(HtmlPage page)
    {
        var form = page.One("form[enctype='multipart/form-data']") ?? throw new Xunit.Sdk.XunitException("Edit form not found.\n" + ShopClient.Excerpt(page.Html));
        var fields = new List<(string, string)>();
        foreach (var element in form.QuerySelectorAll("input[name], textarea[name], select[name]"))
        {
            var name = element.GetAttribute("name")!;
            if (name == "__RequestVerificationToken" || element.HasAttribute("disabled"))
                continue;
            switch (element.LocalName)
            {
                case "textarea":
                    fields.Add((name, element.TextContent));
                    break;
                case "select":
                    var options = element.QuerySelectorAll("option").ToList();
                    var selected = options.Where(o => o.HasAttribute("selected")).ToList();
                    if (selected.Count == 0 && options.Count > 0 && !element.HasAttribute("multiple"))
                        selected.Add(options[0]);
                    fields.AddRange(selected.Select(o => (name, o.GetAttribute("value") ?? o.TextContent.Trim())));
                    break;
                default:
                    var type = (element.GetAttribute("type") ?? "text").ToLowerInvariant();
                    if (type is "file" or "submit" or "button" or "reset" or "image")
                        continue;
                    if (type is "checkbox" or "radio")
                    {
                        if (element.HasAttribute("checked"))
                            fields.Add((name, element.GetAttribute("value") ?? "true"));
                        continue;
                    }
                    fields.Add((name, element.GetAttribute("value") ?? ""));
                    break;
            }
        }
        return fields;
    }

    private static async Task SaveAsync(ShopClient client, Guid id, List<(string Key, string Value)> fields)
    {
        using var response = await client.PostFormAsync($"/admin/products/edit/{id}", fields.ToArray());
        var location = response.Headers.Location?.OriginalString ?? "";
        if (response.StatusCode != HttpStatusCode.Redirect || !location.EndsWith("/admin/products", StringComparison.Ordinal))
        {
            var html = await response.Content.ReadAsStringAsync();
            var message = response.StatusCode == HttpStatusCode.Redirect
                ? (await client.GetPageAsync(location)).One(".a-alert, [role='alert'], .alert")?.TextContent
                : ShopClient.Excerpt(html);
            throw new Xunit.Sdk.XunitException($"Save failed: {(int)response.StatusCode} → {location}\n{message}");
        }
    }

    [SqlFact]
    public async Task Deleting_the_middle_image_by_name_removes_only_that_image()
    {
        var (id, a, b, c) = await ProductWithImagesAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);

        var page = await client.GetPageAsync($"/admin/products/edit/{id}");
        // هر کارت نام خودش و دکمهٔ حذف خودش را دارد (حذف «همین تصویر»، نه بر اساس جایگاه).
        var cards = page.All("[data-gallery='remove'] [data-gallery-item]");
        Assert.Equal([a, b, c], cards.Select(card => card.GetAttribute("data-name")));
        Assert.All(cards, card => Assert.NotNull(card.QuerySelector("[data-gallery-remove]")));
        Assert.Equal("Input.RemovedImages", page.One("[data-gallery='remove']")!.GetAttribute("data-remove-name"));

        var fields = FormFields(page);
        fields.Add(("Input.RemovedImages", b));
        await SaveAsync(client, id, fields);

        Assert.Equal([a, c], await ImagesAsync(id));
    }

    [SqlFact]
    public async Task Legacy_remove_checkbox_still_works_and_tampered_names_delete_nothing()
    {
        var (id, a, b, c) = await ProductWithImagesAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);

        // نام‌های دست‌کاری‌شده (مسیر، نام ناموجود، نام تصویر کالای دیگر) ← هیچ تصویری پاک نمی‌شود.
        var page = await client.GetPageAsync($"/admin/products/edit/{id}");
        var fields = FormFields(page);
        fields.Add(("Input.RemovedImages", "../../" + b));
        fields.Add(("Input.RemovedImages", "not-there.webp"));
        await SaveAsync(client, id, fields);
        Assert.Equal([a, b, c], await ImagesAsync(id));

        // روش قدیمی: تیک «حذف» ردیف اول.
        page = await client.GetPageAsync($"/admin/products/edit/{id}");
        fields = FormFields(page);
        var index = fields.First(f => f.Key.EndsWith(".ImageName", StringComparison.Ordinal) && f.Value == a).Key.Split('.')[1]; // Images[n]
        fields.Add(($"Input.{index}.Remove", "true"));
        await SaveAsync(client, id, fields);
        Assert.Equal([b, c], await ImagesAsync(id));
    }

    [SqlFact]
    public async Task A_gap_in_image_indexes_never_silently_deletes_the_following_images()
    {
        var (id, a, b, c) = await ProductWithImagesAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);

        var page = await client.GetPageAsync($"/admin/products/edit/{id}");
        var fields = FormFields(page);
        // شبیه‌سازی اسکریپت خراب/قدیمی: ردیف b از فرم برداشته شده (بدون شماره‌گذاری دوباره) و فقط به‌صورت نام حذف آمده.
        var indexOfB = fields.First(f => f.Key.EndsWith(".ImageName", StringComparison.Ordinal) && f.Value == b).Key.Split('.')[1];
        fields.RemoveAll(f => f.Key.StartsWith($"Input.{indexOfB}.", StringComparison.Ordinal));
        fields.Add(("Input.RemovedImages", b));
        await SaveAsync(client, id, fields);

        // c بعد از «جای خالی» آمده بود و اتصال فهرست آن را نمی‌دید؛ ولی چون صریحاً حذف نشده، می‌ماند.
        Assert.Equal([a, c], await ImagesAsync(id));
        var alt = await app.WithDbAsync(db => db.ProductImages.Where(i => i.ProductId == id && i.ImageName == c).Select(i => i.AltText).SingleAsync());
        Assert.Equal("تصویر سه", alt);
    }
}
