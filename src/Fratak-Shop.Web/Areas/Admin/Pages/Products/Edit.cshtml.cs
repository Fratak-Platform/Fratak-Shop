using Fratak_Shop.Core.DTOs.Products.Command;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Suppliers;
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

    /// <summary>روش قدیمی (تیک «حذف» کنار هر ردیف)؛ برای سازگاری با فرم‌های بدون جاوااسکریپت هنوز پذیرفته می‌شود.</summary>
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

    /// <summary>«ناموجود دستی»: این محصول در بررسی خودکار تأمین‌کننده خوانده نشود.</summary>
    public bool ExcludeFromSupplierSync { get; set; }
    public string? PrimaryImage { get; set; }
    public List<ProductImageInput> Images { get; set; } = [];

    /// <summary>
    /// نام فایل تصویرهایی که کارمند «حذف» زده است (دور بیستم؛ admin-gallery.js). حذف بر اساس نام است، نه شمارهٔ ردیف،
    /// تا هرگز تصویر دیگری (مثلاً آخری) به‌جای تصویر کلیک‌شده حذف نشود. مثال: ["a1b2.webp"].
    /// </summary>
    public List<string> RemovedImages { get; set; } = [];
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
public class EditModel(IProductService products, IAdminDataService data, IUploadStore uploads,
    IAuditHistoryService history, IEditDraftService drafts, EditPresenceNotifier presence, ISupplierSyncService supplierSync,
    IInventoryService inventory, IProductCloneService cloner,
    Fratak_Shop.Core.Services.Pricing.ICompetitorPriceService competitorPrices,
    Fratak_Shop.Core.Services.Settings.IStoreSettingService storeSettings,
    Fratak_Shop.Core.Services.Pricing.IProductCompetitorService productCompetitors,
    Microsoft.Extensions.Options.IOptions<global::Fratak_Shop.Web.Infrastructure.ShopOptions> shopOptions) : AdminPageModel
{
    private const int MaxImages = 12;

    /// <summary>برچسب فارسی فیلدهای محصول برای پنل «تاریخچهٔ تغییرات».</summary>
    public static readonly IReadOnlyDictionary<string, string> FieldLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Name"] = "نام", ["EnglishName"] = "نام انگلیسی", ["Slug"] = "نامک", ["SKU"] = "کد کالا", ["CategoryId"] = "دسته‌بندی", ["BrandId"] = "برند",
        ["ShortDescription"] = "توضیح کوتاه", ["Description"] = "توضیحات", ["Price"] = "قیمت", ["DiscountPrice"] = "قیمت با تخفیف",
        ["DiscountStartAt"] = "شروع تخفیف", ["DiscountEndAt"] = "پایان تخفیف", ["PurchasePrice"] = "قیمت خرید", ["StockQuantity"] = "موجودی",
        ["WarrantyTitle"] = "گارانتی", ["WarrantyMonths"] = "ماه‌های گارانتی", ["IsActive"] = "فعال", ["PublishedAt"] = "تاریخ انتشار",
        ["Gtin"] = "GTIN", ["Mpn"] = "MPN", ["SupplierId"] = "تأمین‌کننده", ["SupplierUrl"] = "لینک منبع", ["SupplierSku"] = "کد نزد تأمین‌کننده",
        ["SupplierNote"] = "یادداشت تأمین", ["MainImage"] = "تصویر اصلی", ["ExcludeFromSupplierSync"] = "خارج از بررسی تأمین‌کننده",
    };

    // ── ابزارهای ویرایش هم‌زمان (مرحلهٔ هفتم – فاز ۳) ──
    /// <summary>کلید حضور زنده و پیش‌نویس خودکار: <c>product:{id}</c> یا <c>product:new</c>.</summary>
    public string EditKey => ProductId is { } id ? $"product:{id}" : "product:new";
    public string Stamp { get; private set; } = "0";
    public string? ConflictMessage { get; private set; }
    public EditHistoryModel? History { get; private set; }

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

    /// <summary>آخرین نتیجهٔ بررسی خودکار تأمین‌کننده برای کارت «منبع خرید» (null = هنوز بررسی نشده).</summary>
    public SupplierProductStatusDto? SupplierStatus { get; private set; }

    // ── ابزارهای کاتالوگ (مرحلهٔ هفتم – فاز ۵) ──
    /// <summary>امتیاز کامل بودن محصول ذخیره‌شده (کارت «کامل بودن»).</summary>
    public CompletenessResult? Completeness { get; private set; }

    /// <summary>پیش‌بینی اتمام موجودی بر اساس فروش ۳۰ روز اخیر.</summary>
    public StockForecast? Forecast { get; private set; }

    /// <summary>آخرین گردش‌های موجودی همین محصول.</summary>
    public List<StockMovementRow> Movements { get; private set; } = [];

    /// <summary>مرحلهٔ پانزدهم: آخرین بررسی قیمت رقبای همین محصول (کارت «قیمت رقبا»)؛ null = هنوز بررسی نشده.</summary>
    public Fratak_Shop.Core.Services.Pricing.ProductCompetitorDetail? Competitors { get; private set; }

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
            Stamp = EditStamp.Format(await history.StampAsync(nameof(Product), product.Id));
            await LoadHistoryAsync();
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

            // کارمند دیگری در این فاصله همین محصول را ذخیره کرده؟ (تغییر موجودی با خرید مشتری حساب نمی‌شود)
            if (EditStamp.IsStale(Request.Form, await history.StampAsync(nameof(Product), existing.Id)))
            {
                ConflictMessage = await EditStamp.ConflictMessageAsync(history, nameof(Product), existing.Id);
                return await FailAsync("ذخیره نشد: این محصول هم‌زمان توسط شخص دیگری تغییر کرده است.");
            }
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
            Input.ExcludeFromSupplierSync = existing.ExcludeFromSupplierSync;
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

        // حراج زمان‌دار (مرحلهٔ هفتم): تاریخ با ساعت اختیاری؛ «1404/08/10 18:00». بدون ساعت: شروع از ابتدای روز و پایان تا آخر روز.
        var discountStartAt = PersianDateInput.ParseDateTime(Input.DiscountStart);
        var discountEndAt = PersianDateInput.ParseDateTime(Input.DiscountEnd, EndOfDay);
        if ((!string.IsNullOrWhiteSpace(Input.DiscountStart) && discountStartAt is null) || (!string.IsNullOrWhiteSpace(Input.DiscountEnd) && discountEndAt is null))
            return await FailAsync("تاریخ شروع/پایان تخفیف را به‌صورت ۱۴۰۴/۰۸/۰۱ یا با ساعت «۱۴۰۴/۰۸/۰۱ ۱۸:۰۰» وارد کنید.");
        // «23:59» که فرم نشان می‌دهد همان آخر روز ذخیره‌شده است (بدون این، هر ذخیره ۵۹ ثانیه پایان را جابه‌جا می‌کرد).
        if (discountEndAt is { } endAt && IranClock.ToLocal(endAt).TimeOfDay == new TimeSpan(23, 59, 0))
            discountEndAt = endAt.AddSeconds(59);
        if (discountStartAt is { } startAt && discountEndAt is { } finishAt && finishAt <= startAt)
            return await FailAsync("پایان تخفیف باید بعد از شروع آن باشد.");

        // Images: kept ones (+ alt / order / primary) and the new uploads.
        // حذف بر اساس «نام فایل» (Input.RemovedImages)؛ هر نام فقط یک بار (ردیف تکراری فرم هرگز ردیف تکراری نمی‌سازد).
        var removedNames = RemovedNames(Input);
        var kept = Input.Images.Where(i => !i.Remove && !string.IsNullOrWhiteSpace(i.ImageName) && !removedNames.Contains(i.ImageName))
            .Where(i => existing is null || existing.Images.Any(e => e.ImageName == i.ImageName))
            .DistinctBy(i => i.ImageName, StringComparer.Ordinal)
            .ToList();
        if (existing is not null)
            kept.AddRange(Unposted(existing.Images, Input.Images, removedNames));
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

        var slug = string.IsNullOrWhiteSpace(Input.Slug) ? Fratak_Shop.Data.Utilities.EnglishSlug.Prefer(Input.Name, Input.EnglishName) : Slugify(Input.Slug);
        var sku = string.IsNullOrWhiteSpace(Input.SKU) ? "FR-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() : Input.SKU.Trim();
        var description = Input.Description.SanitizeOrNull();

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
                ExcludeFromSupplierSync = Input.ExcludeFromSupplierSync,
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
                ExcludeFromSupplierSync = Input.ExcludeFromSupplierSync,
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

        // پیش‌نویس خودکار دیگر لازم نیست؛ بقیهٔ کسانی که این محصول را باز کرده‌اند باخبر شوند.
        await drafts.DeleteAsync(ActorId, EditKey);
        if (existing is not null)
            await presence.SavedAsync(EditKey, ActorName, ActorId);

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

    /// <summary>
    /// دکمهٔ «پیشنهاد قیمت فروش» (دور ۱۰، بازنویسی مرحلهٔ پانزدهم): قیمت خرید + همهٔ هزینه‌های تنظیمات + هزینهٔ اضافهٔ همین کالا،
    /// و قیمت همان کالا در ترب (همهٔ فروشندگان)، دیجی‌کالا، غرفه‌های باسلام و «سایت‌های دیگر» (فروشگاه‌هایی که در جست‌وجوی وب پیدا شدند)
    /// ← قیمت کمی ارزان‌تر از ارزان‌ترین فروشندهٔ ترب (یا اگر ترب نبود، ارزان‌ترین رقیب معتبر) با سود مناسب.
    /// <para>هر پیشنهاد داوری می‌شود (عنوان نامشابه، لوازم جانبی، مدل دیگر، تکراری، قیمت پرت، فروشگاه خودمان) و کارمند می‌تواند
    /// هر کدام را «حذف» کند (exclude) یا کنارگذاشته‌ای را «حساب کند» (include). این انتخاب‌ها <b>فقط برای همین محاسبه</b>اند و ذخیره نمی‌شوند:
    /// دفعهٔ بعد همه دوباره داوری می‌شوند.</para>
    /// <para>قیمت فروش محصول عوض نمی‌شود؛ کارمند با «استفاده از این قیمت» عدد را در فرم می‌گذارد و خودش ذخیره می‌کند.
    /// فقط «آخرین قیمت رقبا»ی این محصول برای کارت «قیمت رقبا» و صفحهٔ «رصد قیمت رقبا» ذخیره می‌شود.
    /// POST است چون به اینترنت درخواست می‌فرستد و باید توکن ضد جعل داشته باشد.</para>
    /// <para>مثال پاسخ: { ok, suggested: 264000, reference: 270000, torob: { sellers: 3, lowest: 270000, rank: 1 },
    /// offers: [{ id: "a1b2c3d4e5f6", shop: "فروشگاه Y", price: 270000, source: "ترب", accepted: true, code: "ok" }, …] }.</para>
    /// </summary>
    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostSuggestPriceAsync(Guid? id, decimal purchase, decimal extra, string? query, bool lookup = true,
        string[]? exclude = null, string[]? include = null, Guid? categoryId = null)
    {
        if (purchase <= 0 || purchase > 100_000_000_000m)
            return new JsonResult(new { ok = false, message = "قیمت خرید را وارد کنید (عددی بزرگ‌تر از صفر)." });
        if (extra is < 0 or > 100_000_000_000m)
            return new JsonResult(new { ok = false, message = "هزینهٔ اضافه نامعتبر است." });

        var cancellationToken = HttpContext.RequestAborted;
        var settings = await storeSettings.GetAsync(cancellationToken);
        var problems = new List<string>();
        string? matched = null;
        DateTimeOffset? cachedAt = null;
        Fratak_Shop.Core.Services.Pricing.CompetitorLookup? result = null;
        IReadOnlyList<Fratak_Shop.Core.Services.Pricing.CompetitorOffer> found = [];
        query = query?.Trim();
        if (query is { Length: > 200 })
            query = query[..200];
        if (!settings.IsCompetitorPriceLookupEnabled)
            problems.Add("جست‌وجوی قیمت رقبا در تنظیمات خاموش است؛ فقط از روی هزینه‌ها محاسبه شد.");
        else if (lookup && !string.IsNullOrEmpty(query))
        {
            result = await competitorPrices.SearchAsync(query, cancellationToken);
            found = result.Offers;
            matched = result.MatchedTitle;
            cachedAt = result.CachedAt;
            problems.AddRange(result.Problems);
            // مرحلهٔ سیزدهم: قیمت‌های کش ماندگار (حداکثر ۶ ساعت) تا درخواست تکراری باعث محدود شدن نشود.
            if (result.CachedAt is { } at && result.Problems.Count == 0)
                problems.Add($"قیمت‌ها از جست‌وجوی حدود {Math.Max(1, (int)Math.Round((DateTimeOffset.UtcNow - at).TotalHours))} ساعت پیش است (هر کالا حداکثر هر ۶ ساعت یک بار دوباره جست‌وجو می‌شود تا منابع ما را محدود نکنند).");
        }

        // حذف/حساب‌کردن‌های کارمند فقط برای همین محاسبه (شناسه‌های کوتاه هگز؛ حداکثر ۲۰۰ تا).
        static string[] Ids(string[]? values) => (values ?? Array.Empty<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v) && v.Length <= 24).Select(v => v.Trim()).Distinct().Take(200).ToArray();
        var excluded = Ids(exclude);
        var included = Ids(include);
        var ownShops = OwnShopNames.For(settings, shopOptions.Value, Request);
        var review = new Fratak_Shop.Core.Services.Pricing.OfferReviewOptions(query, excluded, included, ownShops);

        var costs = Fratak_Shop.Core.Services.Pricing.PricingCosts.From(settings);
        // دور ۲۳ (مورد ۶۹): کف سود دسته (دستهٔ فرم، یا دستهٔ ذخیره‌شدهٔ کالا) – فقط بالاتر از کف سراسری.
        categoryId ??= id is { } owner ? await data.Query<Product>().Where(p => p.Id == owner).Select(p => (Guid?)p.CategoryId).FirstOrDefaultAsync(cancellationToken) : null;
        if (categoryId is not null)
        {
            var nodes = await data.Query<Category>().Select(c => new { c.Id, c.ParentId, c.MinMarginPercent }).ToListAsync(cancellationToken);
            costs = costs with
            {
                MinMarginPercent = Fratak_Shop.Core.Services.Pricing.CategoryMargins.Effective(costs.MinMarginPercent, categoryId,
                    nodes.ToDictionary(c => c.Id, c => new Fratak_Shop.Core.Services.Pricing.CategoryMarginNode(c.ParentId, c.MinMarginPercent))),
            };
        }
        var s = Fratak_Shop.Core.Services.Pricing.PriceAdvisor.Suggest(purchase, extra, costs, found, review);

        // اولین جست‌وجو (بدون حذف/حساب‌کردن کارمند) برای کارت «قیمت رقبا» ذخیره می‌شود.
        if (id is { } productId && result is not null && excluded.Length == 0 && included.Length == 0)
        {
            var current = await data.Query<Product>().Where(p => p.Id == productId)
                .Select(p => new { p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt }).FirstOrDefaultAsync(cancellationToken);
            if (current is not null)
                await productCompetitors.SaveAsync(productId, query!, result,
                    Fratak_Shop.Core.Services.Pricing.ProductCompetitorService.Effective(current.Price, current.DiscountPrice, current.DiscountStartAt, current.DiscountEndAt, DateTimeOffset.UtcNow),
                    ownShops, cancellationToken);
        }

        var reviewed = s.Reviewed ?? Array.Empty<Fratak_Shop.Core.Services.Pricing.ReviewedOffer>();
        return new JsonResult(new
        {
            ok = true,
            totalCost = s.TotalCost,
            breakEven = s.BreakEven,
            minPrice = s.MinPrice,
            targetPrice = s.TargetPrice,
            suggested = s.Suggested,
            profit = s.Profit,
            profitPercent = s.ProfitPercent,
            strategy = s.Strategy,
            reason = s.Reason,
            lowest = s.Lowest,
            median = s.Median,
            highest = s.Offers.Count > 0 ? s.Offers.Max(o => o.Price) : (decimal?)null,
            reference = s.Reference,
            matched,
            cachedAt,
            torob = s.Torob is { } t ? new { sellers = t.Sellers, lowest = t.Lowest, rank = t.Rank, cheaperElsewhere = t.CheaperElsewhere } : null,
            offers = reviewed.Select(r => new
            {
                id = r.Id,
                shop = r.Offer.Shop,
                price = r.Offer.Price,
                source = r.Offer.Source,
                url = global::Fratak_Shop.Web.Helpers.UrlSafety.IsHttpUrl(r.Offer.Url) ? r.Offer.Url : null,
                title = r.Offer.Title,
                accepted = r.Accepted,
                code = r.Code,
                reason = r.Reason,
                match = r.Match,
                // فاصله با قیمت پیشنهادی به درصد (مثبت = رقیب گران‌تر است). مثال: ۳٫۴.
                diff = s.Suggested > 0 ? Math.Round((r.Offer.Price - s.Suggested) * 100 / s.Suggested, 1) : 0,
            }),
            problems,
            costs = new
            {
                packaging = costs.PackagingCost,
                shipping = costs.ShippingCost,
                other = costs.OtherCostAmount,
                percent = costs.PercentOfSale,
                target = costs.TargetMarginPercent,
                min = costs.MinMarginPercent,
            },
        });
    }

    /// <summary>
    /// دکمهٔ «بررسی دوباره» در کارت «قیمت رقبا» (مرحلهٔ پانزدهم): قیمت رقبای همین محصول را با نام آن (یا آخرین عبارت جست‌وجو) دوباره می‌گیرد و ذخیره می‌کند.
    /// قیمت محصول عوض نمی‌شود.
    /// </summary>
    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostRefreshCompetitorsAsync(Guid id)
    {
        var settings = await storeSettings.GetAsync(HttpContext.RequestAborted);
        ShowResult(await productCompetitors.RefreshAsync(id, OwnShopNames.For(settings, shopOptions.Value, Request), HttpContext.RequestAborted));
        return Redirect($"/admin/products/edit/{id}#competitors");
    }

    /// <summary>بازگردانی یک ویرایش محصول از تاریخچه (AuditHistoryService).</summary>
    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostRevertAsync(Guid id, Guid auditId)
    {
        var result = await history.RevertAsync(auditId, nameof(Product), id);
        if (Succeeded(result))
            await presence.SavedAsync($"product:{id}", ActorName, ActorId);
        ShowResult(result);
        return Redirect($"/admin/products/edit/{id}#history");
    }

    /// <summary>
    /// «کپی محصول»: نسخهٔ غیرفعال با همان مشخصات، عکس‌ها (فایل‌ها هم کپی می‌شوند)، ویژگی‌ها و سئو؛
    /// بعد از ساخت به صفحهٔ ویرایش کپی می‌رود. منطق در Core/Services/Products/ProductCloneService.cs است.
    /// </summary>
    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostCloneAsync(Guid id)
    {
        // فایل‌های عکس کپی می‌شوند تا حذف عکس از یکی، عکس دیگری را خراب نکند (Delete فایل را از دیسک پاک می‌کند).
        var names = await cloner.ImageNamesAsync(id);
        var map = uploads.Duplicate(UploadArea.Products, names);
        var result = await cloner.CloneAsync(id, map);
        if (!Succeeded(result))
        {
            uploads.Delete(UploadArea.Products, map.Values);
            ShowResult(result);
            return Redirect($"/admin/products/edit/{id}");
        }

        ShowResult(result);
        return Redirect($"/admin/products/edit/{result.Data}");
    }

    [AdminPermission(AdminPermissions.ProductsDelete)]
    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await products.DeleteProductAsync(id);
        ShowResult(result);
        return Succeeded(result) ? Redirect("/admin/products") : Redirect($"/admin/products/edit/{id}");
    }

    /// <summary>
    /// نام تصویرهای حذف‌شده: Input.RemovedImages + ردیف‌هایی که تیک قدیمی «حذف» دارند (فقط نام فایل، بدون مسیر).
    /// مثال: RemovedImages=["b.webp"] و Images[0]={a.webp, Remove=true} ← {a.webp, b.webp}.
    /// </summary>
    public static HashSet<string> RemovedNames(ProductInput input) =>
        input.RemovedImages.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim())
            .Concat(input.Images.Where(i => i.Remove && !string.IsNullOrWhiteSpace(i.ImageName)).Select(i => i.ImageName))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// تصویرهای فعلی محصول که اصلاً در فرم نیامده‌اند و کسی هم حذفشان نکرده است (خالص؛ آزمون‌پذیر).
    /// <para>چرا؟ اتصال فهرست در ASP.NET با اولین «جای خالی» در شماره‌ها (Images[0]، Images[2] بدون Images[1]) متوقف می‌شود؛
    /// بدون این محافظ، تصویرهای بعد از جای خالی بی‌صدا پاک می‌شدند. قانون: تصویر فقط با «حذف» صریح پاک می‌شود، نه با نیامدن در فرم.</para>
    /// <para>مثال: در پایگاه [a، b، c]؛ فرم فقط Images[0]=a و RemovedImages=b ← c با alt و ترتیب قبلی خودش می‌ماند.</para>
    /// </summary>
    public static IEnumerable<ProductImageInput> Unposted(IEnumerable<ProductImage> current, IEnumerable<ProductImageInput> posted, IReadOnlySet<string> removed)
    {
        var seen = posted.Select(i => i.ImageName).Where(n => !string.IsNullOrWhiteSpace(n)).ToHashSet(StringComparer.Ordinal);
        return current
            .Where(e => !seen.Contains(e.ImageName) && !removed.Contains(e.ImageName))
            .DistinctBy(e => e.ImageName, StringComparer.Ordinal)
            .Select(e => new ProductImageInput { ImageName = e.ImageName, AltText = e.AltText, DisplayOrder = e.DisplayOrder })
            .ToList();
    }

    private async Task<IActionResult> FailAsync(string message)
    {
        ErrorMessage = message;
        // فرم دوباره با همان نسخه‌ای نمایش داده می‌شود که کارمند بر اساسش نوشته است.
        Stamp = Request.Form[EditStamp.FieldName].FirstOrDefault() ?? "0";
        await LoadHistoryAsync();
        if (ProductId is { } id)
        {
            // Keep the typed values; images come back from the database (new uploads must be chosen again).
            // تصویرهایی که کارمند «حذف» زده بود حذف‌شده می‌مانند (نام‌ها دوباره در فرم می‌آیند) و alt/ترتیب تایپ‌شده حفظ می‌شود.
            var removedNames = RemovedNames(Input);
            var product = await data.Query<Product>().Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == id);
            if (product is not null)
            {
                var typed = Input.Images.Where(x => !string.IsNullOrWhiteSpace(x.ImageName))
                    .DistinctBy(x => x.ImageName, StringComparer.Ordinal).ToDictionary(x => x.ImageName, StringComparer.Ordinal);
                Input.RemovedImages = product.Images.Select(i => i.ImageName).Where(removedNames.Contains).ToList();
                Input.Images = product.Images.Where(i => !removedNames.Contains(i.ImageName))
                    .Select(i => new ProductImageInput
                    {
                        ImageName = i.ImageName,
                        AltText = typed.TryGetValue(i.ImageName, out var t) ? t.AltText : i.AltText,
                        DisplayOrder = typed.TryGetValue(i.ImageName, out var o) ? o.DisplayOrder : i.DisplayOrder,
                    })
                    .OrderBy(i => i.DisplayOrder)
                    .ToList();
            }
        }
        else
        {
            Input.Images = [];
            Input.RemovedImages = [];
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

    private async Task LoadHistoryAsync()
    {
        if (ProductId is not { } id)
            return;
        var items = await history.ListAsync(nameof(Product), id);
        History = new EditHistoryModel(nameof(Product), id, items, $"/admin/products/edit/{id}?handler=Revert", FieldLabels, CanEditProduct);
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
        {
            VideoCount = await data.Query<ProductVideo>().CountAsync(v => v.ProductId == id);
            var input = await data.Query<Product>().Where(p => p.Id == id).Select(ProductCompleteness.Projection).FirstOrDefaultAsync();
            Completeness = input is null ? null : ProductCompleteness.Evaluate(input);
            if (Can(AdminPermissions.ProductsView))
            {
                Forecast = await inventory.ForecastProductAsync(id);
                Movements = (await inventory.ListMovementsAsync(new StockMovementQuery(ProductId: id, Take: 10))).Items;
            }
            if (CanSupplier)
                SupplierStatus = await supplierSync.GetProductStatusAsync(id);
            if (Can(AdminPermissions.ProductsView))
                Competitors = await productCompetitors.GetAsync(id, HttpContext.RequestAborted);
        }
    }

    private static ProductInput ToInput(Product p) => new()
    {
        Name = p.Name, EnglishName = p.EnglishName, Slug = p.Slug, SKU = p.SKU, CategoryId = p.CategoryId, BrandId = p.BrandId,
        ShortDescription = p.ShortDescription, Description = p.Description, Price = p.Price, DiscountPrice = p.DiscountPrice,
        DiscountStart = DiscountText(p.DiscountStartAt, TimeOnly.MinValue),
        DiscountEnd = DiscountText(p.DiscountEndAt, EndOfDay),
        PurchasePrice = p.PurchasePrice, StockQuantity = p.StockQuantity, WarrantyTitle = p.WarrantyTitle, WarrantyMonths = p.WarrantyMonths,
        IsActive = p.IsActive, Gtin = p.Gtin, Mpn = p.Mpn, SupplierId = p.SupplierId, SupplierUrl = p.SupplierUrl, SupplierSku = p.SupplierSku,
        SupplierNote = p.SupplierNote, ExcludeFromSupplierSync = p.ExcludeFromSupplierSync,
        PrimaryImage = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).FirstOrDefault(),
        Images = p.Images.OrderBy(i => i.DisplayOrder).Select(i => new ProductImageInput { ImageName = i.ImageName, AltText = i.AltText, DisplayOrder = i.DisplayOrder }).ToList(),
        Features = p.Features.Count == 0 ? [new ProductFeatureInput()] : p.Features.OrderBy(f => f.DisplayOrder).Select(f => new ProductFeatureInput { Key = f.Key, Value = f.Value }).ToList(),
        Seo = SeoInput.From(p.Seo),
    };

    /// <summary>آخر روز برای پایان حراج بدون ساعت.</summary>
    private static readonly TimeOnly EndOfDay = new(23, 59, 59);

    /// <summary>
    /// متن فرم برای زمان حراج: اگر ساعت همان پیش‌فرض باشد (ابتدا/انتهای روز) فقط تاریخ، وگرنه تاریخ و ساعت.
    /// مثال: شروع 1404/08/10 00:00 ← «1404/08/10»؛ شروع 18:30 ← «1404/08/10 18:30».
    /// </summary>
    private static string? DiscountText(DateTimeOffset? moment, TimeOnly defaultTime)
    {
        if (moment is not { } value)
            return null;
        var local = IranClock.ToLocal(value);
        return TimeOnly.FromTimeSpan(local.TimeOfDay) == defaultTime
            ? PersianDateInput.Format(IranClock.Today(value))
            : PersianDateInput.FormatDateTime(value);
    }

    /// <summary>URL slug: lower-case, Persian letters and digits kept, everything else → '-'.</summary>
    /// <summary>
    /// نامک همیشه انگلیسی (دور سئوی خودکار): متن فارسی ترجمه/آوانویسی می‌شود.
    /// مثال: «هدفون بی‌سیم سونی» ← «wireless-headphones-sony». منطق: Data/Utilities/EnglishSlug.cs.
    /// </summary>
    public static string Slugify(string value) => Fratak_Shop.Data.Utilities.EnglishSlug.From(value, 200);
}
