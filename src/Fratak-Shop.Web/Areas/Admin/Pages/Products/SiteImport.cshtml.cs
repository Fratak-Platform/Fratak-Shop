using System.Security.Cryptography;
using Fratak_Shop.Core.DTOs.Products.Command;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Importing;
using Fratak_Shop.Core.Services.Pricing;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Brands;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Suppliers;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Products;

/// <summary>
/// صفحهٔ «درون‌ریزی از سایت» (دور ۱۰): نشانی یک سایت فروشگاهی را می‌دهید، همهٔ محصولاتش (عنوان، توضیح، مشخصات، قیمت و تصاویر)
/// خوانده و پیش‌نمایش می‌شود، بعد محصولات انتخاب‌شده به‌صورت <b>پیش‌نویس غیرفعال</b> ساخته می‌شوند.
/// <list type="bullet">
/// <item>تصاویر دانلود و روی سرور خودمان ذخیره می‌شوند (همان مسیر و پاک‌سازی آپلود دستی: بازسازی تصویر و حذف متادیتا)؛
/// هیچ تصویری از سایت مبدأ مستقیم نمایش داده نمی‌شود. تصویری که دانلود نشد کنار می‌رود و محصول بدون آن ساخته می‌شود.</item>
/// <item>تصاویر داخل متن توضیح و پیوندهای سایت مبدأ حذف می‌شوند (SiteCatalogParsers.CleanDescription) و متن از پاک‌ساز XSS می‌گذرد.</item>
/// <item>نشانی صفحهٔ مبدأ به‌عنوان «لینک منبع خرید» ذخیره می‌شود؛ پس بررسی خودکار تأمین‌کننده (قیمت/موجودی) هم برایش کار می‌کند.</item>
/// <item>محصولی که قبلاً با همین لینک منبع ساخته شده دوباره ساخته نمی‌شود.</item>
/// </list>
/// روند: «خواندن سایت» ← پیش‌نمایش (تا ۶۰ دقیقه در حافظهٔ سرور، فقط برای همین کاربر) ← «ساخت محصولات انتخاب‌شده» (هر بار حداکثر ۳۰).
/// <para>منطق خواندن سایت: Core/Services/Importing/SiteCatalogImport.cs.</para>
/// </summary>
[AdminPermission(AdminPermissions.ProductsManage)]
public class SiteImportModel(ISiteCatalogService catalog, IProductService products, IAdminDataService data, IUploadStore uploads,
    IStoreSettingService storeSettings, IHttpClientFactory httpFactory, IMemoryCache cache, ILogger<SiteImportModel> logger,
    IMarketPricingService marketPricing, Fratak_Shop.Core.Services.Smart.ISmartCategoryService smartCategories, Fratak_Shop.Core.Services.Smart.ISmartTaskQueue smartQueue) : AdminPageModel
{
    /// <summary>روش‌های قیمت‌گذاری: ۱ = سود هدف روی قیمت مبدأ، ۲ = همان قیمت مبدأ، ۳ = بر اساس قیمت بازار (پیش‌فرض، مرحلهٔ سیزدهم).</summary>
    public const int PriceModeTarget = 1, PriceModeSource = 2, PriceModeMarket = 3;

    /// <summary>وضعیت صف قیمت‌گذاری بازار برای نمایش در صفحه.</summary>
    public MarketPricingSummary? Market { get; private set; }

    /// <summary>بیشترین تعداد محصول در هر بار «ساخت» (هر محصول چند تصویر دانلود می‌کند).</summary>
    public const int MaxPerRun = 30;

    /// <summary>بیشترین تعداد تصویر دانلودی برای هر محصول.</summary>
    public const int MaxImagesPerProduct = 8;

    /// <summary>سقف حجم هر تصویر دانلودی (۵ مگابایت؛ همان سقف آپلود دستی).</summary>
    private const int MaxImageBytes = 5 * 1024 * 1024;

    /// <summary>داده‌ای که بین پیش‌نمایش و ساخت در حافظه می‌ماند. Imported: ردیف‌هایی که در همین جلسه ساخته شدند.</summary>
    private sealed record PendingSiteImport(Guid UserId, SiteCatalog Catalog, HashSet<int> Imported, HashSet<int> Existing);

    public SiteCatalog? Catalog { get; private set; }
    public string? Token { get; private set; }
    public HashSet<int> Imported { get; private set; } = [];
    public HashSet<int> Existing { get; private set; } = [];
    public List<string> Report { get; } = [];

    public List<(Guid Id, string Name)> Categories { get; private set; } = [];
    public List<(Guid Id, string Name)> Suppliers { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? SiteUrl { get; set; }

    /// <summary>ثبت محصول جدید با لینک منبع خرید و قیمت خرید نیاز به دسترسی «اطلاعات تأمین‌کننده» هم دارد (مثل فرم محصول).</summary>
    public bool CanSupplier => Can(AdminPermissions.ProductsSupplier);

    public async Task OnGetAsync(string? token)
    {
        await LoadListsAsync();
        if (token is not null && Pending(token) is { } pending)
            Show(token, pending);
    }

    /// <summary>خواندن سایت و نمایش پیش‌نمایش.</summary>
    public async Task<IActionResult> OnPostDiscoverAsync(int max = 200)
    {
        await LoadListsAsync();
        if (!CanSupplier)
            return Fail("درون‌ریزی محصول نیاز به دسترسی «اطلاعات تأمین‌کننده و قیمت خرید» دارد.");
        if (string.IsNullOrWhiteSpace(SiteUrl) || SiteUrl.Length > 500)
            return Fail("نشانی سایت را وارد کنید. مثال: https://shop.example.com");

        var result = await catalog.DiscoverAsync(SiteUrl, Math.Clamp(max, 1, 500), HttpContext.RequestAborted);
        if (result.Products.Count == 0)
        {
            Catalog = result;
            return Fail(result.Problems.FirstOrDefault() ?? "محصولی پیدا نشد.");
        }

        // محصولاتی که قبلاً با همین لینک منبع ساخته شده‌اند.
        var urls = result.Products.Select(p => p.SourceUrl).ToList();
        var known = await data.Query<Product>().Where(p => p.SupplierUrl != null && urls.Contains(p.SupplierUrl)).Select(p => p.SupplierUrl!).ToListAsync();
        var knownSet = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
        var existing = Enumerable.Range(0, result.Products.Count).Where(i => knownSet.Contains(result.Products[i].SourceUrl)).ToHashSet();

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        var pending = new PendingSiteImport(ActorId, result, [], existing);
        cache.Set(CacheKey(token), pending, TimeSpan.FromMinutes(60));
        Show(token, pending);
        return Page();
    }

    /// <summary>
    /// ساخت محصولات انتخاب‌شده به‌صورت پیش‌نویس غیرفعال.
    /// </summary>
    /// <param name="token">توکن پیش‌نمایش.</param>
    /// <param name="selected">شمارهٔ ردیف‌های انتخاب‌شده.</param>
    /// <param name="categoryId">دستهٔ پیش‌فرض (اختیاری). دور ۲۱: خالی = «خودکار»؛ کالا اول در «دسته‌بندی نشده» ساخته و بلافاصله با
    /// دسته‌بندی هوشمند (SmartCategoryService) به دستهٔ درست برده می‌شود (یا دستهٔ تازه ساخته می‌شود). اگر نام دستهٔ مبدأ با دسته‌ای
    /// در فروشگاه یکی بود، همان انتخاب می‌شود.</param>
    /// <param name="supplierId">تأمین‌کننده (اختیاری).</param>
    /// <param name="priceMode">۱ = قیمت مبدأ قیمت خرید است و قیمت فروش با سود هدف تنظیمات محاسبه می‌شود؛ ۲ = قیمت مبدأ همان قیمت فروش است؛
    /// ۳ (پیش‌فرض) = قیمت مبدأ قیمت خرید است، محصول با قیمت سود هدف ساخته می‌شود و بلافاصله در صف «قیمت‌گذاری بازار» می‌رود تا
    /// قیمت فروش از روی قیمت رقبا (همان «پیشنهاد قیمت فروش») تنظیم شود.</param>
    /// <param name="withImages">تصاویر دانلود شوند.</param>
    public async Task<IActionResult> OnPostImportAsync(string? token, List<int> selected, Guid? categoryId, Guid? supplierId, int priceMode = PriceModeMarket, bool withImages = true)
    {
        await LoadListsAsync();
        if (!CanSupplier)
            return Fail("درون‌ریزی محصول نیاز به دسترسی «اطلاعات تأمین‌کننده و قیمت خرید» دارد.");
        if (token is null || Pending(token) is not { } pending)
        {
            ErrorMessage = "مهلت پیش‌نمایش تمام شده است؛ سایت را دوباره بخوانید.";
            return Redirect("/admin/products/site-import");
        }
        Show(token, pending);

        if (categoryId is not null && Categories.All(c => c.Id != categoryId))
            return Fail("دستهٔ انتخاب‌شده یافت نشد.");
        var automatic = categoryId is null;
        var fallbackCategory = categoryId ?? await smartCategories.EnsureUncategorizedAsync(HttpContext.RequestAborted);
        var createdIds = new List<Guid>();
        if (supplierId is { } sid && Suppliers.All(s => s.Id != sid))
            return Fail("تأمین‌کنندهٔ انتخاب‌شده یافت نشد.");

        var rows = selected.Distinct().Where(i => i >= 0 && i < pending.Catalog.Products.Count && !pending.Imported.Contains(i) && !pending.Existing.Contains(i))
            .OrderBy(i => i).ToList();
        if (rows.Count == 0)
            return Fail("محصولی برای ساخت انتخاب نشده است (محصولات ساخته‌شده یا موجود قابل انتخاب نیستند).");
        var skipped = 0;
        if (rows.Count > MaxPerRun)
        {
            skipped = rows.Count - MaxPerRun;
            rows = rows.Take(MaxPerRun).ToList();
        }

        var settings = await storeSettings.GetAsync(HttpContext.RequestAborted);
        var costs = PricingCosts.From(settings);
        var categoryNames = await data.Query<Category>().Select(c => new { c.Id, c.Name, c.ParentId, c.MinMarginPercent }).ToListAsync();
        // دور ۲۳ (مورد ۶۹): کف سود هر دسته در قیمت پیشنهادی درون‌ریزی.
        var marginNodes = categoryNames.ToDictionary(c => c.Id, c => new CategoryMarginNode(c.ParentId, c.MinMarginPercent));
        var brandNames = await data.Query<Brand>().Select(b => new { b.Id, b.Name }).ToListAsync();

        if (priceMode is not (PriceModeTarget or PriceModeSource or PriceModeMarket))
            priceMode = PriceModeMarket;
        int created = 0, failed = 0, imagesSaved = 0, imagesFailed = 0, queued = 0;
        foreach (var index in rows)
        {
            HttpContext.RequestAborted.ThrowIfCancellationRequested();
            var item = pending.Catalog.Products[index];

            // دوباره بررسی تکراری (ممکن است همکار دیگری همین الان ساخته باشد).
            if (await data.Query<Product>().AnyAsync(p => p.SupplierUrl == item.SourceUrl))
            {
                pending.Existing.Add(index);
                continue;
            }

            var savedNames = new List<string>();
            if (withImages)
            {
                var (names, failedCount) = await DownloadImagesAsync(item.Images.Take(MaxImagesPerProduct).ToList(), HttpContext.RequestAborted);
                savedNames = names;
                imagesSaved += names.Count;
                imagesFailed += failedCount;
            }

            var rowCategory = categoryNames.FirstOrDefault(c => Same(c.Name, item.Category))?.Id ?? fallbackCategory;
            var rowCosts = costs with { MinMarginPercent = CategoryMargins.Effective(costs.MinMarginPercent, rowCategory, marginNodes) };
            var command = BuildCommand(item, priceMode, rowCosts, supplierId, rowCategory,
                brandNames.FirstOrDefault(b => Same(b.Name, item.Brand))?.Id);
            command.Slug = await UniqueSlugAsync(EditModel.Slugify(item.Name));
            command.Images = savedNames.Select((name, i) => new ProductImageCommand { ImageName = name, AltText = command.Name, DisplayOrder = i + 1, IsPrimary = i == 0 }).ToList();

            OperationResult result;
            try
            {
                result = await products.CreateProductAsync(command);
            }
            catch (DbUpdateException exception)
            {
                logger.LogWarning(exception, "Site import of {Url} failed", item.SourceUrl);
                result = OperationResult.Error("ذخیره انجام نشد (احتمالاً نام یا کد تکراری).");
            }

            if (Succeeded(result))
            {
                created++;
                pending.Imported.Add(index);
                if (await data.Query<Product>().Where(p => p.Slug == command.Slug).Select(p => (Guid?)p.Id).FirstOrDefaultAsync() is { } newId)
                    createdIds.Add(newId);
                if (priceMode == PriceModeMarket && command.PurchasePrice is > 0)
                    queued += await QueueMarketPriceAsync(item.SourceUrl, command.Name, command.Price, command.PurchasePrice);
            }
            else
            {
                failed++;
                uploads.Delete(UploadArea.Products, savedNames);
                if (Report.Count < 10)
                    Report.Add($"«{item.Name}»: {result.Message}");
            }
        }

        Show(token, pending);
        var categorizeNote = createdIds.Count > 0 ? await CategorizeAsync(createdIds, automatic) : null;
        var message = $"{created} محصول به‌صورت پیش‌نویس غیرفعال ساخته شد";
        if (withImages)
            message += $" ({imagesSaved} تصویر روی سرور ذخیره شد{(imagesFailed > 0 ? $"، {imagesFailed} تصویر دانلود/خوانده نشد" : "")})";
        message += ".";
        if (queued > 0)
            message += $" قیمت فروش {queued} محصول در پس‌زمینه از روی قیمت بازار (ترب، دیجی‌کالا، باسلام) تنظیم می‌شود؛ نتیجه در جدول «قیمت‌گذاری بازار» همین صفحه.";
        if (categorizeNote is not null)
            message += " " + categorizeNote;
        if (failed > 0)
            message += $" {failed} محصول ساخته نشد.";
        if (skipped > 0)
            message += $" {skipped} محصول انتخاب‌شدهٔ دیگر مانده است؛ دوباره «ساخت» را بزنید (هر بار حداکثر {MaxPerRun}).";
        if (created > 0)
            SuccessMessage = message;
        else
            ErrorMessage = message;
        return Page();
    }

    /// <summary>
    /// ساخت دستور محصول از دادهٔ مبدأ. مثال: قیمت مبدأ ۱۰۰ هزار با «قیمت خرید» ← قیمت فروش = قیمت با سود هدف تنظیمات (مثلاً ۱۳۲ هزار).
    /// </summary>
    private static CreateProductCommand BuildCommand(ImportedProduct item, int priceMode, PricingCosts costs, Guid? supplierId, Guid categoryId, Guid? brandId)
    {
        decimal price = 0;
        decimal? purchase = null;
        if (item.Price is { } sourcePrice && sourcePrice > 0)
        {
            if (priceMode == PriceModeSource)
                price = sourcePrice;
            else
            {
                purchase = sourcePrice;
                price = PriceAdvisor.Suggest(sourcePrice, 0, costs, []).TargetPrice;
            }
        }

        string? description;
        try
        {
            description = SiteCatalogParsers.CleanDescription(item.Description).SanitizeOrNull();
        }
        catch (global::System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            description = null;
        }

        var features = item.Attributes
            .Select((a, i) => new ProductFeatureCommand { Key = a.Key, Value = a.Value, ValueType = FeatureValueType.Text, DisplayOrder = i + 1 })
            .ToList();
        return new CreateProductCommand
        {
            CategoryId = categoryId,
            BrandId = brandId,
            Name = item.Name,
            SKU = "IMP-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)),
            ShortDescription = item.ShortDescription,
            Description = description,
            Price = price,
            PurchasePrice = purchase,
            StockQuantity = 0,
            IsActive = false,
            SupplierId = supplierId,
            SupplierUrl = item.SourceUrl,
            SupplierSku = item.Sku,
            SupplierNote = item.InStock ? "درون‌ریزی از سایت" : "درون‌ریزی از سایت – در مبدأ ناموجود بود",
            Features = features,
            // دور ۲۲: مسیر دستهٔ سایت مبدأ (مثلاً «لوازم شخصی > ماساژور») – بهترین سرنخ دسته‌بندی هوشمند.
            SourceCategory = item.Category,
        };
    }

    /// <summary>
    /// گذاشتن محصول تازه‌ساخته در صف قیمت‌گذاری بازار. شناسهٔ محصول از روی لینک منبع پیدا می‌شود (CreateProductAsync شناسه برنمی‌گرداند).
    /// خطای صف، ساخت محصول را خراب نمی‌کند (محصول با قیمت سود هدف می‌ماند). خروجی: ۱ اگر در صف رفت.
    /// </summary>
    private async Task<int> QueueMarketPriceAsync(string sourceUrl, string name, decimal price, decimal? purchase)
    {
        try
        {
            var productId = await data.Query<Product>().Where(p => p.SupplierUrl == sourceUrl).Select(p => (Guid?)p.Id).FirstOrDefaultAsync();
            if (productId is null)
                return 0;
            await marketPricing.EnqueueAsync(productId.Value, name, price, purchase, HttpContext.RequestAborted);
            return 1;
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(exception, "Queueing market price of {Url} failed", sourceUrl);
            return 0;
        }
    }

    /// <summary>
    /// دانلود تصاویر از مسیر امن تأمین‌کننده (محافظ SSRF) و ذخیره با همان پاک‌سازی آپلود دستی (بازسازی تصویر، حذف متادیتا، نام تصادفی).
    /// هر تصویر جدا ذخیره می‌شود تا یک تصویر خراب بقیه را از بین نبرد.
    /// </summary>
    private async Task<(List<string> Names, int Failed)> DownloadImagesAsync(List<string> urls, CancellationToken cancellationToken)
    {
        var downloads = await Task.WhenAll(urls.Select(url => DownloadAsync(url, cancellationToken)));
        var names = new List<string>();
        var failed = 0;
        foreach (var bytes in downloads)
        {
            if (bytes is null)
            {
                failed++;
                continue;
            }
            using var stream = new MemoryStream(bytes);
            var file = new FormFile(stream, 0, bytes.Length, "image", "image");
            var (files, error) = await uploads.SaveAsync(UploadArea.Products, [file], 1, MaxImageBytes / 1024, cancellationToken);
            if (error is null && files.Count == 1)
                names.Add(files[0].FileName);
            else
                failed++;
        }
        return (names, failed);
    }

    private async Task<byte[]?> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("image/avif,image/webp,image/png,image/jpeg,image/*;q=0.8");
            using var response = await httpFactory.CreateClient(SupplierHttp.ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxImageBytes)
                return null;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxImageBytes)
                    return null;
                buffer.Write(chunk, 0, read);
            }
            return buffer.Length > 100 ? buffer.ToArray() : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or SupplierHttp.BlockedAddressException
                                              || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>اسلاگ یکتا: haylou-t15، اگر بود haylou-t15-2، …</summary>
    private async Task<string> UniqueSlugAsync(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            slug = "product";
        if (slug.Length > 190)
            slug = slug[..190].Trim('-');
        var taken = await data.Query<Product>().Where(p => p.Slug == slug || p.Slug.StartsWith(slug + "-")).Select(p => p.Slug).ToListAsync();
        if (!taken.Contains(slug))
            return slug;
        for (var i = 2; i < 1000; i++)
            if (!taken.Contains($"{slug}-{i}"))
                return $"{slug}-{i}";
        return $"{slug}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant()}";
    }

    /// <summary>
    /// دور ۲۱: دسته‌بندی هوشمند کالاهای تازه (فقط آن‌هایی که در «دسته‌بندی نشده» یا «سایر» افتاده‌اند؛ انتخاب دستی ادمین دست نمی‌خورد).
    /// برای هر دستهٔ تازه کار «تکمیل دسته» (توضیح، پرسش‌های رایج و تصویر) در صف هوشمند گذاشته می‌شود.
    /// خطا هرگز درون‌ریزی را خراب نمی‌کند (کالاها ساخته شده‌اند و بازبینی ۶ ساعته دوباره تلاش می‌کند).
    /// مثال خروجی: «دسته‌بندی هوشمند: ۱۸ کالا دسته گرفت؛ ۲ دستهٔ تازه: پاوربانک، لیوان سرامیکی.»
    /// </summary>
    private async Task<string?> CategorizeAsync(List<Guid> ids, bool automatic)
    {
        try
        {
            var report = await smartCategories.RunAsync(ids, ids.Count, HttpContext.RequestAborted);
            foreach (var category in report.Created)
            {
                await smartQueue.EnqueueAsync(Fratak_Shop.Data.Entities.Smart.SmartTaskKind.CategoryComplete, Fratak_Shop.Data.Entities.Smart.SmartTarget.Category, category.Id,
                    category.Name, Fratak_Shop.Core.Services.Smart.SmartPriority.Batch, userId: ActorId, userName: ActorName, cancellationToken: HttpContext.RequestAborted);
            }
            if (report.Examined == 0)
                return null;
            var note = "دسته‌بندی هوشمند: " + report.Summary;
            if (report.Waiting > 0 && automatic)
                note += $" {report.Waiting} کالا فعلاً در «دسته‌بندی نشده» ماند تا با رسیدن کالاهای مشابه دسته بگیرد (یا خودتان انتخاب کنید).";
            return note;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Smart categorization after site import failed");
            return "دسته‌بندی هوشمند این بار انجام نشد؛ در بازبینی خودکار بعدی انجام می‌شود.";
        }
    }

    private async Task LoadListsAsync()
    {
        var categories = await data.Query<Category>().Where(c => !c.IsSystem).Select(c => new { c.Id, c.Name, Parent = c.ParentCategory != null ? c.ParentCategory.Name : null }).ToListAsync();
        Categories = categories.Select(c => (c.Id, c.Parent is null ? c.Name : $"{c.Parent} › {c.Name}")).OrderBy(c => c.Item2).ToList();
        var suppliers = await data.Query<Supplier>().Where(s => s.IsActive).OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name).Select(s => new { s.Id, s.Name }).ToListAsync();
        Suppliers = suppliers.Select(s => (s.Id, s.Name)).ToList();
        Market = await marketPricing.SummaryAsync(15, HttpContext.RequestAborted);
    }

    private PendingSiteImport? Pending(string token)
        => token.Length <= 40 && cache.TryGetValue(CacheKey(token), out PendingSiteImport? pending) && pending is not null && pending.UserId == ActorId ? pending : null;

    private void Show(string token, PendingSiteImport pending)
    {
        Token = token;
        Catalog = pending.Catalog;
        Imported = pending.Imported;
        Existing = pending.Existing;
        SiteUrl ??= pending.Catalog.BaseUrl;
    }

    private IActionResult Fail(string message)
    {
        // Page() برمی‌گردد؛ پیام مستقیم نمایش داده می‌شود.
        ErrorMessage = message;
        return Page();
    }

    private static bool Same(string a, string? b)
        => b is not null && string.Equals(global::Fratak_Shop.Core.Services.Seo.Engine.PersianText.Normalize(a),
            global::Fratak_Shop.Core.Services.Seo.Engine.PersianText.Normalize(b), StringComparison.Ordinal);

    /// <summary>کلید حافظه به کاربر گره خورده تا توکن کاربر دیگری قابل استفاده نباشد.</summary>
    private string CacheKey(string token) => $"admin-site-import:{ActorId}:{token}";
}
