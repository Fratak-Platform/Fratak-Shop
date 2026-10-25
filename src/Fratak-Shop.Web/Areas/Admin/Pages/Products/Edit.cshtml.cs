using Fratak_Shop.Core.DTOs.Products.Command;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Brands;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Products;

public sealed class ProductImageInput
{
    public string ImageName { get; set; } = "";
    public string? AltText { get; set; }
    public int DisplayOrder { get; set; }
    public bool Remove { get; set; }
}

public sealed class ProductFeatureInput
{
    public string? Key { get; set; }
    public string? Value { get; set; }
}

public sealed class ProductInput
{
    public string? Name { get; set; }
    public string? EnglishName { get; set; }
    public string? Slug { get; set; }
    public string? SKU { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? BrandId { get; set; }
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public decimal? Price { get; set; }
    public decimal? DiscountPrice { get; set; }
    public string? DiscountStart { get; set; }
    public string? DiscountEnd { get; set; }
    public decimal? PurchasePrice { get; set; }
    public int StockQuantity { get; set; }
    public string? WarrantyTitle { get; set; }
    public int? WarrantyMonths { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Gtin { get; set; }
    public string? Mpn { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierUrl { get; set; }
    public string? SupplierSku { get; set; }
    public string? SupplierNote { get; set; }
    public string? PrimaryImage { get; set; }
    public List<ProductImageInput> Images { get; set; } = [];
    public List<ProductFeatureInput> Features { get; set; } = [];
    public SeoInput Seo { get; set; } = new();
}

/// <summary>
/// Create / edit a product: catalog data, prices, stock, images, specifications, SEO – and the private
/// supplier data (source link is required: whoever prepares an order must know where to buy the item).
/// SEO specialists without the product permission can only edit the SEO section.
/// </summary>
[AdminPermission(AdminPermissions.ProductsView, AdminPermissions.SeoManage)]
[RequestFormLimits(MultipartBodyLengthLimit = 41 * 1024 * 1024, ValueCountLimit = 4000)]
[RequestSizeLimit(42 * 1024 * 1024)]
public class EditModel(IProductService products, IAdminDataService data, IUploadStore uploads) : AdminPageModel
{
    private const int MaxImages = 12;

    [BindProperty] public ProductInput Input { get; set; } = new();

    public Guid? ProductId { get; private set; }
    public string? CurrentSlug { get; private set; }
    public int SalesCount { get; private set; }
    public int ViewCount { get; private set; }
    public int VideoCount { get; private set; }
    public DateTimeOffset? CreatedAt { get; private set; }
    public bool IsNew => ProductId is null;

    public List<(Guid Id, string Name)> Categories { get; private set; } = [];
    public List<(Guid Id, string Name)> Brands { get; private set; } = [];
    public List<(Guid Id, string Name)> Suppliers { get; private set; } = [];

    public bool CanEditProduct => Can(AdminPermissions.ProductsManage);
    public bool CanSupplier => Can(AdminPermissions.ProductsSupplier);

    public async Task<IActionResult> OnGetAsync(Guid? id)
    {
        if (id is null)
        {
            if (!CanEditProduct)
                return Redirect("/admin/denied?need=" + AdminPermissions.ProductsManage);
            Input.Features = [new ProductFeatureInput()];
        }
        else
        {
            var product = await data.Query<Product>().Include(p => p.Images).Include(p => p.Features).FirstOrDefaultAsync(p => p.Id == id);
            if (product is null)
                return NotFound();
            Input = ToInput(product);
            SetInfo(product);
        }

        await LoadListsAsync();
        return Page();
    }

    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostAsync(Guid? id, List<IFormFile> newImages)
    {
        Product? existing = null;
        if (id is { } productId)
        {
            existing = await data.Query<Product>().Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == productId);
            if (existing is null)
                return NotFound();
            SetInfo(existing);
        }

        // Without the supplier permission the private fields are kept as they are.
        if (!CanSupplier)
        {
            if (existing is null)
                return await FailAsync("ثبت محصول جدید نیاز به دسترسی «اطلاعات تأمین‌کننده» دارد (لینک منبع خرید الزامی است).");
            Input.SupplierId = existing.SupplierId;
            Input.SupplierUrl = existing.SupplierUrl;
            Input.SupplierSku = existing.SupplierSku;
            Input.SupplierNote = existing.SupplierNote;
            Input.PurchasePrice = existing.PurchasePrice;
        }
        else if (Input.SupplierId is null)
        {
            return await FailAsync("تأمین‌کننده (سایت منبع خرید) را انتخاب کنید.");
        }

        if (string.IsNullOrWhiteSpace(Input.Name) || Input.CategoryId is null || Input.Price is null)
            return await FailAsync("نام، دسته‌بندی و قیمت محصول الزامی است.");

        var seoError = Input.Seo.Validate();
        if (seoError is not null)
            return await FailAsync(seoError);

        if (Input.ShortDescription?.Length > 1000)
            return await FailAsync("توضیح کوتاه حداکثر ۱۰۰۰ نویسه است.");

        var discountStart = PersianDateInput.Parse(Input.DiscountStart);
        var discountEnd = PersianDateInput.Parse(Input.DiscountEnd);
        if ((!string.IsNullOrWhiteSpace(Input.DiscountStart) && discountStart is null) || (!string.IsNullOrWhiteSpace(Input.DiscountEnd) && discountEnd is null))
            return await FailAsync("تاریخ شروع/پایان تخفیف را به‌صورت ۱۴۰۴/۰۸/۰۱ وارد کنید.");

        // Images: kept ones (+ alt / order / primary) and the new uploads.
        var kept = Input.Images.Where(i => !i.Remove && !string.IsNullOrWhiteSpace(i.ImageName))
            .Where(i => existing is null || existing.Images.Any(e => e.ImageName == i.ImageName))
            .ToList();
        var newFiles = newImages.Where(f => f.Length > 0).ToList();
        if (kept.Count + newFiles.Count > MaxImages)
            return await FailAsync($"حداکثر {MaxImages} تصویر برای هر محصول مجاز است.");

        List<string> savedNames = [];
        if (newFiles.Count > 0)
        {
            var (files, error) = await uploads.SaveAsync(UploadArea.Products, newFiles, MaxImages, 5_120, HttpContext.RequestAborted);
            if (error is not null)
                return await FailAsync(error);
            savedNames = files.Select(f => f.FileName).ToList();
        }

        var images = kept.Select(i => new ProductImageCommand
            {
                ImageName = i.ImageName,
                AltText = string.IsNullOrWhiteSpace(i.AltText) ? null : i.AltText.Trim(),
                DisplayOrder = i.DisplayOrder,
                IsPrimary = i.ImageName == Input.PrimaryImage,
            })
            .OrderBy(i => i.DisplayOrder)
            .ToList();
        var order = images.Count == 0 ? 0 : images.Max(i => i.DisplayOrder);
        images.AddRange(savedNames.Select(name => new ProductImageCommand { ImageName = name, AltText = Input.Name.Trim(), DisplayOrder = ++order }));

        var features = Input.Features
            .Where(f => !string.IsNullOrWhiteSpace(f.Key) && !string.IsNullOrWhiteSpace(f.Value))
            .Select((f, index) => new ProductFeatureCommand { Key = f.Key!.Trim(), Value = f.Value!.Trim(), ValueType = FeatureValueType.Text, DisplayOrder = index + 1 })
            .ToList();
        if (features.GroupBy(f => f.Key, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
        {
            uploads.Delete(UploadArea.Products, savedNames);
            return await FailAsync("عنوان هر مشخصه باید یکتا باشد.");
        }

        var slug = string.IsNullOrWhiteSpace(Input.Slug) ? Slugify(Input.EnglishName ?? Input.Name) : Slugify(Input.Slug);
        var sku = string.IsNullOrWhiteSpace(Input.SKU) ? "FR-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() : Input.SKU.Trim();
        var description = Input.Description.SanitizeOrNull();
        var discountStartAt = discountStart is { } ds ? IranClock.StartOfDay(ds) : (DateTimeOffset?)null;
        var discountEndAt = discountEnd is { } de ? IranClock.StartOfDay(de).AddDays(1).AddSeconds(-1) : (DateTimeOffset?)null;

        OperationResult result;
        if (existing is null)
        {
            result = await products.CreateProductAsync(new CreateProductCommand
            {
                CategoryId = Input.CategoryId.Value, BrandId = Input.BrandId, Name = Input.Name, EnglishName = Input.EnglishName,
                Slug = slug, SKU = sku, ShortDescription = Input.ShortDescription?.Trim(), Description = description,
                Price = Input.Price.Value, DiscountPrice = Input.DiscountPrice, PurchasePrice = Input.PurchasePrice,
                DiscountStartAt = discountStartAt, DiscountEndAt = discountEndAt, StockQuantity = Input.StockQuantity,
                WarrantyTitle = Input.WarrantyTitle?.Trim(), WarrantyMonths = Input.WarrantyMonths, IsActive = Input.IsActive,
                Seo = Input.Seo.ApplyTo(null), Gtin = Input.Gtin, Mpn = Input.Mpn,
                SupplierId = Input.SupplierId, SupplierUrl = Input.SupplierUrl, SupplierSku = Input.SupplierSku, SupplierNote = Input.SupplierNote,
                Images = images, Features = features,
            });
        }
        else
        {
            result = await products.EditProductAsync(new EditProductCommand
            {
                Id = existing.Id, CategoryId = Input.CategoryId.Value, BrandId = Input.BrandId, Name = Input.Name, EnglishName = Input.EnglishName,
                Slug = slug, SKU = sku, ShortDescription = Input.ShortDescription?.Trim(), Description = description,
                Price = Input.Price.Value, DiscountPrice = Input.DiscountPrice, PurchasePrice = Input.PurchasePrice,
                DiscountStartAt = discountStartAt, DiscountEndAt = discountEndAt, StockQuantity = Input.StockQuantity,
                WarrantyTitle = Input.WarrantyTitle?.Trim(), WarrantyMonths = Input.WarrantyMonths, IsActive = Input.IsActive,
                PublishedAt = existing.PublishedAt,
                Seo = Input.Seo.ApplyToCopy(existing.Seo), Gtin = Input.Gtin, Mpn = Input.Mpn,
                SupplierId = Input.SupplierId, SupplierUrl = Input.SupplierUrl, SupplierSku = Input.SupplierSku, SupplierNote = Input.SupplierNote,
                Images = images, Features = features,
            });
        }

        if (!Succeeded(result))
        {
            uploads.Delete(UploadArea.Products, savedNames);
            return await FailAsync(result.Message ?? "ذخیره انجام نشد.");
        }

        // Files of removed images are deleted (only names that belong to this product and were uploaded).
        if (existing is not null)
        {
            var removed = existing.Images.Select(i => i.ImageName)
                .Where(name => !images.Any(i => i.ImageName == name) && !name.StartsWith('/') && !name.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (removed.Count > 0)
                uploads.Delete(UploadArea.Products, removed);
        }

        SuccessMessage = result.Message;
        var savedId = existing?.Id ?? await data.Query<Product>().Where(p => p.Slug == slug).Select(p => p.Id).FirstOrDefaultAsync();
        return Redirect(savedId == Guid.Empty ? "/admin/products" : $"/admin/products/edit/{savedId}");
    }

    /// <summary>SEO-only save (SEO specialists).</summary>
    [AdminPermission(AdminPermissions.SeoManage, AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostSeoAsync(Guid id)
    {
        var error = Input.Seo.Validate();
        if (error is not null)
        {
            ErrorMessage = error;
            return Redirect($"/admin/products/edit/{id}#seo");
        }

        var product = await data.FindAsync<Product>(id);
        if (product is null)
            return NotFound();
        product.Seo = Input.Seo.ApplyToCopy(product.Seo);
        ShowResult(await data.UpdateAsync(product));
        return Redirect($"/admin/products/edit/{id}#seo");
    }

    [AdminPermission(AdminPermissions.ProductsDelete)]
    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await products.DeleteProductAsync(id);
        ShowResult(result);
        return Succeeded(result) ? Redirect("/admin/products") : Redirect($"/admin/products/edit/{id}");
    }

    private async Task<IActionResult> FailAsync(string message)
    {
        ErrorMessage = message;
        if (ProductId is { } id)
        {
            // Keep the typed values; images come back from the database (new uploads must be chosen again).
            var product = await data.Query<Product>().Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == id);
            if (product is not null)
                Input.Images = product.Images.OrderBy(i => i.DisplayOrder)
                    .Select(i => new ProductImageInput { ImageName = i.ImageName, AltText = Input.Images.FirstOrDefault(x => x.ImageName == i.ImageName)?.AltText ?? i.AltText, DisplayOrder = i.DisplayOrder })
                    .ToList();
        }
        else
        {
            Input.Images = [];
        }
        if (Input.Features.Count == 0)
            Input.Features = [new ProductFeatureInput()];
        await LoadListsAsync();
        return Page();
    }

    private void SetInfo(Product product)
    {
        ProductId = product.Id;
        CurrentSlug = product.Slug;
        SalesCount = product.SalesCount;
        ViewCount = product.ViewCount;
        CreatedAt = product.CreatedAt;
    }

    private async Task LoadListsAsync()
    {
        var categories = await data.Query<Category>().Select(c => new { c.Id, c.Name, Parent = c.ParentCategory != null ? c.ParentCategory.Name : null }).ToListAsync();
        Categories = categories.Select(c => (c.Id, c.Parent is null ? c.Name : $"{c.Parent} › {c.Name}")).OrderBy(c => c.Item2).ToList();
        var brands = await data.Query<Brand>().OrderBy(b => b.Name).Select(b => new { b.Id, b.Name }).ToListAsync();
        Brands = brands.Select(b => (b.Id, b.Name)).ToList();
        if (CanSupplier)
        {
            var suppliers = await data.Query<Supplier>().Where(s => s.IsActive || s.Id == Input.SupplierId).OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
                .Select(s => new { s.Id, s.Name }).ToListAsync();
            Suppliers = suppliers.Select(s => (s.Id, s.Name)).ToList();
        }
        if (ProductId is { } id)
            VideoCount = await data.Query<ProductVideo>().CountAsync(v => v.ProductId == id);
    }

    private static ProductInput ToInput(Product p) => new()
    {
        Name = p.Name, EnglishName = p.EnglishName, Slug = p.Slug, SKU = p.SKU, CategoryId = p.CategoryId, BrandId = p.BrandId,
        ShortDescription = p.ShortDescription, Description = p.Description, Price = p.Price, DiscountPrice = p.DiscountPrice,
        DiscountStart = p.DiscountStartAt is { } s ? PersianDateInput.Format(IranClock.Today(s)) : null,
        DiscountEnd = p.DiscountEndAt is { } e ? PersianDateInput.Format(IranClock.Today(e)) : null,
        PurchasePrice = p.PurchasePrice, StockQuantity = p.StockQuantity, WarrantyTitle = p.WarrantyTitle, WarrantyMonths = p.WarrantyMonths,
        IsActive = p.IsActive, Gtin = p.Gtin, Mpn = p.Mpn, SupplierId = p.SupplierId, SupplierUrl = p.SupplierUrl, SupplierSku = p.SupplierSku,
        SupplierNote = p.SupplierNote,
        PrimaryImage = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).FirstOrDefault(),
        Images = p.Images.OrderBy(i => i.DisplayOrder).Select(i => new ProductImageInput { ImageName = i.ImageName, AltText = i.AltText, DisplayOrder = i.DisplayOrder }).ToList(),
        Features = p.Features.Count == 0 ? [new ProductFeatureInput()] : p.Features.OrderBy(f => f.DisplayOrder).Select(f => new ProductFeatureInput { Key = f.Key, Value = f.Value }).ToList(),
        Seo = SeoInput.From(p.Seo),
    };

    /// <summary>URL slug: lower-case, Persian letters and digits kept, everything else → '-'.</summary>
    public static string Slugify(string value)
    {
        var text = SearchText.NormalizeDigits(value.Trim().ToLowerInvariant());
        var chars = text.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length > 200 ? slug[..200].Trim('-') : slug;
    }
}
