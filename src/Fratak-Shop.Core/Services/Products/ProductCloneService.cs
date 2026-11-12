using System.Security.Cryptography;
using System.Text.Json;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Utilities.SeoManagement;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Products;

/// <summary>
/// «کپی محصول» (مرحلهٔ هفتم – فاز ۵): یک محصول تازه با همهٔ اطلاعات محصول مبدأ می‌سازد تا برای کالای مشابه
/// (مثلاً رنگ یا مدل دیگر) لازم نباشد همه چیز از اول وارد شود.
/// <para>
/// قانون‌ها: کپی «غیرفعال» و «منتشرنشده» ساخته می‌شود؛ موجودی صفر، آمار فروش/بازدید صفر؛ نامک و کد کالا یکتا می‌شوند
/// («-copy» / «-C1»)؛ بارکد (GTIN) و نشانی Canonical کپی نمی‌شوند چون باید یکتا باشند؛ ویدیوها کپی نمی‌شوند.
/// عکس‌ها: فایل‌ها در لایهٔ وب کپی می‌شوند (نام تازه) و نقشهٔ «نام قدیم → نام جدید» به این سرویس داده می‌شود تا
/// حذف عکس از یکی، عکس دیگری را خراب نکند.
/// </para>
/// استفاده: دکمهٔ «کپی» در صفحهٔ ویرایش محصول.
/// </summary>
public interface IProductCloneService
{
    /// <summary>نام فایل عکس‌های محصول (برای کپی فایل‌ها در لایهٔ وب پیش از ساخت).</summary>
    Task<List<string>> ImageNamesAsync(Guid productId);

    /// <summary>
    /// ساخت کپی. <paramref name="imageMap"/>: نام فایل قدیم → نام فایل کپی‌شده (عکس‌های بدون نقشه با همان نام
    /// می‌مانند فقط اگر نشانی بیرونی/ثابت باشند، وگرنه کپی نمی‌شوند).
    /// </summary>
    Task<OperationResult<Guid>> CloneAsync(Guid productId, IReadOnlyDictionary<string, string> imageMap);
}

public sealed class ProductCloneService(FratakShopDbContext context) : IProductCloneService
{
    public Task<List<string>> ImageNamesAsync(Guid productId) =>
        context.ProductImages.Where(i => i.ProductId == productId).Select(i => i.ImageName).ToListAsync();

    public async Task<OperationResult<Guid>> CloneAsync(Guid productId, IReadOnlyDictionary<string, string> imageMap)
    {
        var source = await context.Products
            .Include(p => p.Images)
            .Include(p => p.Features)
            .FirstOrDefaultAsync(p => p.Id == productId);
        if (source is null)
            return OperationResult<Guid>.NotFound("محصول پیدا نشد.");

        var slug = await UniqueSlugAsync(source.Slug);
        var sku = await UniqueSkuAsync(source.SKU);

        var copy = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = source.CategoryId,
            BrandId = source.BrandId,
            Name = Cut(source.Name, 242) + " (کپی)",
            EnglishName = source.EnglishName,
            Slug = slug,
            SKU = sku,
            ShortDescription = source.ShortDescription,
            Description = source.Description,
            Price = source.Price,
            DiscountPrice = source.DiscountPrice,
            DiscountStartAt = source.DiscountStartAt,
            DiscountEndAt = source.DiscountEndAt,
            PurchasePrice = source.PurchasePrice,
            StockQuantity = 0,
            WarrantyTitle = source.WarrantyTitle,
            WarrantyMonths = source.WarrantyMonths,
            Seo = CopySeo(source.Seo),
            Gtin = null,
            Mpn = source.Mpn,
            SupplierId = source.SupplierId,
            SupplierUrl = source.SupplierUrl,
            SupplierSku = source.SupplierSku,
            SupplierNote = source.SupplierNote,
            ExcludeFromSupplierSync = source.ExcludeFromSupplierSync,
            IsActive = false,
            PublishedAt = null,
            // دور ۲۲: کپی، الگوی ادمین برای کالای مشابه است؛ ورود هوشمند (اصلاح نام و تکمیل) رویش اجرا نمی‌شود تا کار ادمین را به هم نزند.
            SmartIntakeAt = DateTimeOffset.UtcNow,
            CategorySource = source.CategorySource,
            SourceCategory = source.SourceCategory,
        };

        foreach (var image in source.Images.OrderBy(i => i.DisplayOrder))
        {
            var external = image.ImageName.StartsWith('/') || image.ImageName.StartsWith("http", StringComparison.OrdinalIgnoreCase);
            string? name = imageMap.TryGetValue(image.ImageName, out var mapped) ? mapped : external ? image.ImageName : null;
            if (name is null)
                continue;
            copy.Images.Add(new ProductImage { ImageName = name, AltText = image.AltText, DisplayOrder = image.DisplayOrder, IsPrimary = image.IsPrimary });
        }
        if (copy.Images.Count > 0 && !copy.Images.Any(i => i.IsPrimary))
            copy.Images[0].IsPrimary = true;

        foreach (var feature in source.Features.OrderBy(f => f.DisplayOrder))
            copy.Features.Add(new ProductFeature { Key = feature.Key, Value = feature.Value, ValueType = feature.ValueType, DisplayOrder = feature.DisplayOrder });

        context.Products.Add(copy);
        await context.SaveChangesAsync();
        return OperationResult<Guid>.Success(copy.Id, $"کپی «{source.Name}» ساخته شد (غیرفعال). نام، نامک و کد کالا را بررسی و سپس فعال کنید.");
    }

    /// <summary>نامک یکتا؛ مثال: «hedphone-x» → «hedphone-x-copy» → «hedphone-x-copy-2».</summary>
    private async Task<string> UniqueSlugAsync(string slug)
    {
        var baseSlug = Cut(slug, 180) + "-copy";
        var candidate = baseSlug;
        for (var i = 2; i < 50 && await context.Products.IgnoreQueryFilters().AnyAsync(p => p.Slug == candidate); i++)
            candidate = $"{baseSlug}-{i}";
        if (await context.Products.IgnoreQueryFilters().AnyAsync(p => p.Slug == candidate))
            candidate = $"{baseSlug}-{RandomSuffix()}";
        return candidate;
    }

    /// <summary>کد کالای یکتا؛ مثال: «HP-100» → «HP-100-C1» → «HP-100-C2».</summary>
    private async Task<string> UniqueSkuAsync(string sku)
    {
        var baseSku = Cut(sku, 85);
        for (var i = 1; i < 50; i++)
        {
            var candidate = $"{baseSku}-C{i}";
            if (!await context.Products.IgnoreQueryFilters().AnyAsync(p => p.SKU == candidate))
                return candidate;
        }
        return $"{baseSku}-{RandomSuffix()}";
    }

    /// <summary>کپی اطلاعات سئو به‌جز Canonical (که به محصول مبدأ اشاره می‌کرد)، پرسش‌وپاسخ و وضعیت بازبینی.</summary>
    private static SeoMetaData? CopySeo(SeoMetaData? seo)
    {
        if (seo is null)
            return null;
        var copy = JsonSerializer.Deserialize<SeoMetaData>(JsonSerializer.Serialize(seo)) ?? new SeoMetaData();
        copy.CanonicalUrl = null;
        // پرسش‌وپاسخ دربارهٔ محصول مبدأ نوشته شده (نام و مشخصات آن)؛ برای کپی موتور سئو از نو می‌سازد.
        copy.FaqJson = null;
        copy.IsReviewed = false;
        copy.ReviewedAt = null;
        copy.ReviewedByUserId = null;
        return copy;
    }

    private static string RandomSuffix() => Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}
