using System.Text.Json;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Smart;
using Fratak_Shop.Data.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>اطلاعات یک محصول برای تحقیق هوشمند (نام، برند، کدها، مشخصات فعلی، تعداد عکس).</summary>
public sealed record SmartProductSubject(
    Guid Id, string Name, string? EnglishName, string Slug, string Sku, string? Gtin, string? Mpn, string? BrandName, string? BrandEnglishName,
    Guid CategoryId, string? CategoryName, string? SupplierUrl, string? ShortDescription, int DescriptionLength,
    IReadOnlyList<SeoFeature> Features, IReadOnlyList<string> ImageNames, decimal FinalPrice, decimal? PurchasePrice, bool IsActive)
{
    /// <summary>
    /// عبارت جست‌وجو برای منابع: نام انگلیسی (اگر هست) دقیق‌تر است؛ وگرنه نام فارسی. مثال: «Xiaomi Redmi Buds 4».
    /// </summary>
    public string SearchQuery => !string.IsNullOrWhiteSpace(EnglishName)
        ? (string.IsNullOrWhiteSpace(BrandEnglishName) || EnglishName.Contains(BrandEnglishName, StringComparison.OrdinalIgnoreCase) ? EnglishName : BrandEnglishName + " " + EnglishName)
        : Name;
}

/// <summary>اطلاعات یک دسته برای «تکمیل هوشمند دسته» (B19).</summary>
public sealed record SmartCategorySubject(Guid Id, string Name, string Slug, string? Description, string? ImageName, string? FaqJson, int ProductCount,
    IReadOnlyList<BlogProduct> TopProducts, IReadOnlyList<string> ProductImageNames);

/// <summary>
/// وضعیت متن «توضیحات» محصول برای تصمیم دستیار دربارهٔ «نقد و بررسی» (دور بیستم).
/// <list type="bullet">
/// <item>WrittenBySmart: متن فعلی دقیقاً همان است که دستیار نوشته (هش برابر آخرین تغییر «نقد و بررسی هوشمند» و برنگشته) ← بازنویسی مجاز.</item>
/// <item>HasMarker: نشانهٔ data-fs="review" هست ولی متن عوض شده ← کارشناس ویرایش کرده؛ دست نزن.</item>
/// <item>RolledBack: کارشناس آخرین «نقد و بررسی» را از تاریخچه برگردانده ← تصمیم انسان؛ دوباره نمی‌نویسیم.</item>
/// </list>
/// </summary>
public sealed record ProductBodyState(string Html, int Words, bool WrittenBySmart, bool HasMarker, bool RolledBack);

/// <summary>یک تصمیم کارشناس روی یک عکس پیشنهادی (برای جدول بازخورد). Url=آدرس منبع، Hash=dHash (۰ = نامعلوم)، Source=کلید منبع.</summary>
public sealed record ImageFeedbackItem(string? Url, long Hash, string? Source, bool Approved);

/// <summary>یک ردیف «ناقص» برای دکمهٔ «تکمیل هوشمند همهٔ ناقص‌ها» (B20).</summary>
public sealed record IncompleteProduct(Guid Id, string Name, int Images, int Features, bool HasShortDescription, bool HasInsight, DateTimeOffset CreatedAt);

/// <summary>
/// خواندن/نوشتن دادهٔ کاتالوگ برای امکانات هوشمند (لایهٔ هسته؛ صفحه‌های پنل مستقیم به دیتابیس دست نمی‌زنند).
/// <para>هر نوشتن فقط «افزودنی» یا «پرکردن خالی» است مگر اینکه کارشناس صریحاً «جایگزینی» را تأیید کند؛ پس هیچ دادهٔ دستی
/// بی‌اجازه عوض نمی‌شود. پاک‌شدن کش صفحه‌ها خودکار است (قانون ذخیرهٔ DbContext/PageCachePolicy).</para>
/// <para>محل استفاده: SmartOrchestrator، استودیوی محصول/دسته و برنامه‌ریز بلاگ.</para>
/// </summary>
public interface ISmartCatalogService
{
    Task<SmartProductSubject?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>افزودن عکس‌های ذخیره‌شده به گالری محصول (بعد از عکس‌های فعلی؛ اولی اصلی می‌شود اگر محصول عکس اصلی ندارد).</summary>
    Task<int> AddProductImagesAsync(Guid productId, IReadOnlyList<(string FileName, string Alt)> images, CancellationToken cancellationToken = default);

    /// <summary>
    /// اعمال ردیف‌های مشخصات. ردیف تازه افزوده می‌شود؛ ردیف موجود فقط اگر <paramref name="replaceKeys"/> شامل کلیدش باشد عوض می‌شود.
    /// خروجی: (افزوده، جایگزین).
    /// </summary>
    Task<(int Added, int Replaced)> ApplySpecsAsync(Guid productId, IReadOnlyList<(string Key, string Value)> rows, IReadOnlySet<string> replaceKeys, CancellationToken cancellationToken = default);

    /// <summary>توضیح کوتاه را فقط اگر خالی است پر می‌کند.</summary>
    Task<bool> FillShortDescriptionAsync(Guid productId, string text, CancellationToken cancellationToken = default);

    /// <summary>نظرهای تأییدشدهٔ خود سایت برای تحلیل (حداکثر ۳۰۰ نظر اخیر).</summary>
    Task<List<SourceReview>> GetOwnReviewsAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>کلیدهای فیلتر تعریف‌شده برای دستهٔ محصول (برای هم‌نام‌کردن کلید مشخصات).</summary>
    Task<List<string>> FilterKeysAsync(Guid categoryId, CancellationToken cancellationToken = default);

    Task<List<IncompleteProduct>> ListIncompleteAsync(int take = 200, CancellationToken cancellationToken = default);

    Task<SmartCategorySubject?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);

    /// <summary>پر کردن توضیح/تصویر/پرسش‌های دسته. هر بخش فقط اگر خالی است، مگر overwrite=true (تأیید کارشناس).</summary>
    Task<int> ApplyCategoryAsync(Guid categoryId, string? descriptionHtml, string? imageName, string? faqJson, bool overwrite, CancellationToken cancellationToken = default);

    Task<List<(Guid Id, string Name, bool HasDescription, bool HasImage, int Products)>> ListCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>محصولات واقعی و موجود برای مقاله (با نکته‌های خلاصهٔ خرید تأییدشده).</summary>
    Task<List<BlogProduct>> GetBlogProductsAsync(Guid? categoryId, IReadOnlyCollection<Guid>? productIds, int take, CancellationToken cancellationToken = default);

    /// <summary>عنوان‌ها و هش متن مقاله‌های موجود (برای دروازهٔ کیفیت: عنوان تکراری و متن تقریباً یکسان).</summary>
    Task<(List<string> Titles, List<long> Hashes)> ExistingPostsAsync(CancellationToken cancellationToken = default);

    /// <summary>ساخت مقاله در دستهٔ «راهنمای خرید» بلاگ (ساخته می‌شود اگر نیست). خروجی: شناسهٔ مقاله.</summary>
    Task<Guid> CreatePostAsync(BlogArticle article, string? imageName, string? imageAlt, bool publish, Guid? authorId, CancellationToken cancellationToken = default);

    Task<bool> SetPostImageAsync(Guid postId, string imageName, string alt, CancellationToken cancellationToken = default);

    Task<(string Title, string? ImageName, string? Excerpt)?> GetPostAsync(Guid postId, CancellationToken cancellationToken = default);

    /// <summary>دور بیستم: فهرست سیاه عکس‌های ردشدهٔ یک محصول (آدرس + اثر انگشت). محل استفاده: SmartImageFinder.</summary>
    Task<ImageBlacklist> GetImageBlacklistAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// دور بیستم: ثبت تصمیم کارشناس روی عکس‌ها. ردیف تکراری (همان محصول و همان آدرس) فقط به‌روز می‌شود. خروجی: تعداد ردیف ثبت/به‌روزشده.
    /// مثال: کارشناس ۳ عکس از ۱۲ را انتخاب کرد ← ۳ ردیف Approved=true و ۹ ردیف Approved=false.
    /// </summary>
    Task<int> RecordImageFeedbackAsync(Guid productId, IReadOnlyList<ImageFeedbackItem> items, CancellationToken cancellationToken = default);

    /// <summary>دور بیستم: امتیاز هر منبع عکس در همهٔ محصولات (لاپلاس). مثال: {"digikala":0.83، "bing:x.ir":0.2}.</summary>
    Task<Dictionary<string, double>> ImageSourceScoresAsync(CancellationToken cancellationToken = default);

    /// <summary>دور بیستم: وضعیت متن توضیحات محصول برای تصمیم «نقد و بررسی» (null = محصول پیدا نشد).</summary>
    Task<ProductBodyState?> GetBodyStateAsync(Guid productId, CancellationToken cancellationToken = default);
}

public sealed class SmartCatalogService(FratakShopDbContext context) : ISmartCatalogService
{
    public const string GuideCategorySlug = "buying-guide";

    public async Task<SmartProductSubject?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var p = await context.Products.Where(x => x.Id == productId)
            .Select(x => new
            {
                x.Id, x.Name, x.EnglishName, x.Slug, x.SKU, x.Gtin, x.Mpn,
                BrandName = x.Brand != null ? x.Brand.Name : null,
                BrandEnglish = x.Brand != null ? x.Brand.EnglishName : null,
                x.CategoryId, CategoryName = x.Category.Name, x.SupplierUrl, x.ShortDescription,
                DescriptionLength = x.Description == null ? 0 : x.Description.Length,
                x.Price, x.DiscountPrice, x.DiscountStartAt, x.DiscountEndAt, x.PurchasePrice, x.IsActive,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (p is null)
            return null;
        var features = await context.ProductFeatures.Where(f => f.ProductId == productId).OrderBy(f => f.DisplayOrder)
            .Select(f => new SeoFeature(f.Key, f.Value)).ToListAsync(cancellationToken);
        var images = await context.ProductImages.Where(i => i.ProductId == productId).OrderBy(i => i.DisplayOrder)
            .Select(i => i.ImageName).ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var discounted = p.DiscountPrice is > 0 && p.DiscountPrice < p.Price
                         && (p.DiscountStartAt == null || p.DiscountStartAt <= now) && (p.DiscountEndAt == null || p.DiscountEndAt >= now);
        return new SmartProductSubject(p.Id, p.Name, p.EnglishName, p.Slug, p.SKU, p.Gtin, p.Mpn, p.BrandName, p.BrandEnglish, p.CategoryId, p.CategoryName,
            p.SupplierUrl, p.ShortDescription, p.DescriptionLength, features, images, discounted ? p.DiscountPrice!.Value : p.Price, p.PurchasePrice, p.IsActive);
    }

    public async Task<int> AddProductImagesAsync(Guid productId, IReadOnlyList<(string FileName, string Alt)> images, CancellationToken cancellationToken = default)
    {
        if (images.Count == 0)
            return 0;
        var existing = await context.ProductImages.Where(i => i.ProductId == productId)
            .Select(i => new { i.DisplayOrder, i.IsPrimary }).ToListAsync(cancellationToken);
        var order = existing.Count == 0 ? 0 : existing.Max(i => i.DisplayOrder) + 1;
        var needsPrimary = !existing.Any(i => i.IsPrimary);
        foreach (var (fileName, alt) in images.Take(12))
        {
            context.ProductImages.Add(new ProductImage
            {
                ProductId = productId,
                ImageName = fileName,
                AltText = PersianText.Truncate(PersianText.Clean(alt), 250),
                DisplayOrder = order++,
                IsPrimary = needsPrimary,
            });
            needsPrimary = false;
        }
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return Math.Min(images.Count, 12);
    }

    public async Task<(int Added, int Replaced)> ApplySpecsAsync(Guid productId, IReadOnlyList<(string Key, string Value)> rows, IReadOnlySet<string> replaceKeys, CancellationToken cancellationToken = default)
    {
        var current = await context.ProductFeatures.AsTracking().Where(f => f.ProductId == productId).ToListAsync(cancellationToken);
        var order = current.Count == 0 ? 0 : current.Max(f => f.DisplayOrder) + 1;
        int added = 0, replaced = 0;
        foreach (var (rawKey, rawValue) in rows.Take(80))
        {
            var key = PersianText.Truncate(PersianText.Clean(rawKey), 250);
            var value = SpecMerge.NormalizeValue(rawValue);
            if (key.Length == 0 || value.Length == 0 || !SpecMerge.IsUseful(key, value))
                continue;
            var match = current.FirstOrDefault(f => SpecMerge.NormalizeKey(f.Key) == SpecMerge.NormalizeKey(key));
            if (match is null)
            {
                var feature = new ProductFeature { ProductId = productId, Key = key, Value = value, ValueType = FeatureValueType.Text, DisplayOrder = order++ };
                context.ProductFeatures.Add(feature);
                current.Add(feature);
                added++;
            }
            else if (replaceKeys.Contains(match.Key) || replaceKeys.Contains(key))
            {
                if (SpecMerge.ValueKey(match.Value) != SpecMerge.ValueKey(value))
                {
                    match.Value = value;
                    replaced++;
                }
            }
        }
        if (added + replaced > 0)
            await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return (added, replaced);
    }

    public async Task<bool> FillShortDescriptionAsync(Guid productId, string text, CancellationToken cancellationToken = default)
    {
        var clean = PersianText.Truncate(PersianText.Clean(text), 1000);
        if (clean.Length < 20)
            return false;
        var now = DateTimeOffset.UtcNow;
        var changed = await context.Products
            .Where(p => p.Id == productId && (p.ShortDescription == null || p.ShortDescription == ""))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ShortDescription, clean).SetProperty(p => p.UpdatedAt, now), cancellationToken);
        return changed == 1;
    }

    public async Task<List<SourceReview>> GetOwnReviewsAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var rows = await context.Comments
            .Where(c => c.ProductId == productId && c.Type == CommentType.Review && c.Status == CommentStatus.Approved && c.ParentId == null)
            .OrderByDescending(c => c.CreatedAt).Take(300)
            .Select(c => new { c.Title, c.Content, c.Rating, c.IsRecommended })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new SourceReview("site", r.Title, r.Content, r.Rating, [], [], r.IsRecommended)).ToList();
    }

    public Task<List<string>> FilterKeysAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        context.ProductFilterAttributes.Where(a => a.IsActive && (a.CategoryId == categoryId || a.CategoryId == null))
            .Select(a => a.FeatureKey).Distinct().ToListAsync(cancellationToken);

    public async Task<List<IncompleteProduct>> ListIncompleteAsync(int take = 200, CancellationToken cancellationToken = default)
    {
        var rows = await context.Products
            .Where(p => p.IsActive)
            .Select(p => new
            {
                p.Id, p.Name, p.CreatedAt,
                Images = context.ProductImages.Count(i => i.ProductId == p.Id),
                Features = context.ProductFeatures.Count(f => f.ProductId == p.Id),
                HasShort = p.ShortDescription != null && p.ShortDescription != "",
                HasInsight = context.ProductInsights.Any(i => i.ProductId == p.Id),
            })
            .Where(p => p.Images < 2 || p.Features < 4 || !p.HasShort || !p.HasInsight)
            .OrderByDescending(p => p.CreatedAt)
            .Take(Math.Clamp(take, 1, 1000))
            .ToListAsync(cancellationToken);
        return rows.Select(r => new IncompleteProduct(r.Id, r.Name, r.Images, r.Features, r.HasShort, r.HasInsight, r.CreatedAt)).ToList();
    }

    public async Task<SmartCategorySubject?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        var c = await context.Categories.Where(x => x.Id == categoryId)
            .Select(x => new { x.Id, x.Name, x.Slug, x.Description, x.ImageName, Faq = x.Seo != null ? x.Seo.FaqJson : null })
            .FirstOrDefaultAsync(cancellationToken);
        if (c is null)
            return null;
        var count = await context.Products.CountAsync(p => p.CategoryId == categoryId && p.IsActive, cancellationToken);
        var products = await GetBlogProductsAsync(categoryId, null, 8, cancellationToken);
        var images = await context.Products
            .Where(p => p.CategoryId == categoryId && p.IsActive && p.StockQuantity > 0)
            .OrderByDescending(p => p.SalesCount)
            .Select(p => p.Images.Where(i => i.IsPrimary).Select(i => i.ImageName).FirstOrDefault())
            .Where(name => name != null)
            .Take(8).ToListAsync(cancellationToken);
        return new SmartCategorySubject(c.Id, c.Name, c.Slug, c.Description, c.ImageName, c.Faq, count, products, images.Select(i => i!).ToList());
    }

    public async Task<int> ApplyCategoryAsync(Guid categoryId, string? descriptionHtml, string? imageName, string? faqJson, bool overwrite, CancellationToken cancellationToken = default)
    {
        var category = await context.Categories.AsTracking().FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken);
        if (category is null)
            return 0;
        var changes = 0;
        if (!string.IsNullOrWhiteSpace(descriptionHtml) && (overwrite || string.IsNullOrWhiteSpace(category.Description)))
        {
            category.Description = Fratak_Shop.Core.Utilities.XssSecurity.SanitizeText(descriptionHtml);
            changes++;
        }
        if (!string.IsNullOrWhiteSpace(imageName) && (overwrite || string.IsNullOrWhiteSpace(category.ImageName)))
        {
            category.ImageName = imageName;
            changes++;
        }
        if (!string.IsNullOrWhiteSpace(faqJson))
        {
            category.Seo ??= new Fratak_Shop.Data.Utilities.SeoManagement.SeoMetaData();
            if (overwrite || string.IsNullOrWhiteSpace(category.Seo.FaqJson))
            {
                category.Seo.FaqJson = faqJson;
                changes++;
            }
        }
        if (changes > 0)
            await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return changes;
    }

    public async Task<List<(Guid Id, string Name, bool HasDescription, bool HasImage, int Products)>> ListCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.Categories.OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new
            {
                c.Id, c.Name,
                HasDescription = c.Description != null && c.Description != "",
                HasImage = c.ImageName != null && c.ImageName != "",
                Products = context.Products.Count(p => p.CategoryId == c.Id && p.IsActive),
            })
            .Take(500).ToListAsync(cancellationToken);
        return rows.Select(r => (r.Id, r.Name, r.HasDescription, r.HasImage, r.Products)).ToList();
    }

    public async Task<List<BlogProduct>> GetBlogProductsAsync(Guid? categoryId, IReadOnlyCollection<Guid>? productIds, int take, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var query = context.Products.Where(p => p.IsActive && p.StockQuantity > 0);
        if (productIds is { Count: > 0 })
            query = query.Where(p => productIds.Contains(p.Id));
        else if (categoryId is { } cid)
            query = query.Where(p => p.CategoryId == cid);
        var rows = await query
            .OrderByDescending(p => p.SalesCount).ThenByDescending(p => p.ViewCount)
            .Take(Math.Clamp(take, 1, 20))
            .Select(p => new
            {
                p.Id, p.Name, p.Slug, Brand = p.Brand != null ? p.Brand.Name : null,
                p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt, p.StockQuantity, p.SalesCount,
                Rating = p.Comments.Where(c => c.Type == CommentType.Review && c.Status == CommentStatus.Approved && c.Rating != null).Average(c => (double?)c.Rating) ?? 0,
                Reviews = p.Comments.Count(c => c.Type == CommentType.Review && c.Status == CommentStatus.Approved),
            })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return [];
        var ids = rows.Select(r => r.Id).ToList();
        var features = await context.ProductFeatures.Where(f => ids.Contains(f.ProductId)).OrderBy(f => f.DisplayOrder)
            .Select(f => new { f.ProductId, f.Key, f.Value }).ToListAsync(cancellationToken);
        var insights = await context.ProductInsights.Where(i => ids.Contains(i.ProductId) && i.Status == ProductInsightStatus.Approved)
            .ToListAsync(cancellationToken);

        return rows.Select(r =>
        {
            var insight = insights.FirstOrDefault(i => i.ProductId == r.Id) is { } row ? ProductInsightService.ToView(row) : null;
            var discounted = r.DiscountPrice is > 0 && r.DiscountPrice < r.Price
                             && (r.DiscountStartAt == null || r.DiscountStartAt <= now) && (r.DiscountEndAt == null || r.DiscountEndAt >= now);
            return new BlogProduct(r.Id, r.Name, r.Slug, r.Brand, discounted ? r.DiscountPrice!.Value : r.Price, r.StockQuantity > 0,
                r.Rating, r.Reviews, r.SalesCount,
                features.Where(f => f.ProductId == r.Id).Select(f => new SeoFeature(f.Key, f.Value)).ToList(),
                insight?.Pros.Select(p => p.Text).ToList() ?? [],
                insight?.Cons.Select(p => p.Text).ToList() ?? [],
                insight?.ForWhom ?? []);
        }).ToList();
    }

    public async Task<(List<string> Titles, List<long> Hashes)> ExistingPostsAsync(CancellationToken cancellationToken = default)
    {
        var titles = await context.BlogPosts.Select(p => p.Title).ToListAsync(cancellationToken);
        // فقط ۶۰ مقالهٔ اخیر برای هش متن (حافظهٔ کم روی هاست ضعیف).
        var contents = await context.BlogPosts.OrderByDescending(p => p.CreatedAt).Take(60).Select(p => p.Content).ToListAsync(cancellationToken);
        var hashes = contents.Select(c => Seo.Autopilot.SeoPenaltyGuard.SimHash(PersianText.HtmlToText(c))).Where(h => h != 0).ToList();
        return (titles, hashes);
    }

    public async Task<Guid> CreatePostAsync(BlogArticle article, string? imageName, string? imageAlt, bool publish, Guid? authorId, CancellationToken cancellationToken = default)
    {
        var categoryId = await context.BlogCategories.Where(c => c.Slug == GuideCategorySlug).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(cancellationToken);
        if (categoryId is null)
        {
            var category = new BlogCategory { Name = "راهنمای خرید", Slug = GuideCategorySlug, Description = "راهنماهای خرید، مقایسه‌ها و بهترین‌ها بر اساس دادهٔ واقعی فروشگاه.", IsActive = true };
            context.BlogCategories.Add(category);
            await context.SaveChangesAsync(cancellationToken);
            categoryId = category.Id;
        }

        var baseSlug = EnglishSlug.From(article.Title, 150);
        if (string.IsNullOrWhiteSpace(baseSlug))
            baseSlug = "guide";
        var taken = await context.BlogPosts.IgnoreQueryFilters().Where(p => p.Slug.StartsWith(baseSlug)).Select(p => p.Slug).ToListAsync(cancellationToken);
        var slug = EnglishSlug.Unique(baseSlug, s => taken.Contains(s), 200);

        var authorExists = authorId is { } aid && await context.Users.AnyAsync(u => u.Id == aid, cancellationToken);
        var post = new BlogPost
        {
            BlogCategoryId = categoryId.Value,
            AuthorId = authorExists ? authorId : null,
            Title = PersianText.Truncate(article.Title, 250),
            Slug = slug,
            Excerpt = PersianText.Truncate(article.Excerpt, 500),
            Content = article.Html,
            ImageName = imageName,
            ImageAlt = imageAlt is null ? null : PersianText.Truncate(imageAlt, 250),
            IsPublished = publish,
            PublishedAt = publish ? DateTimeOffset.UtcNow : null,
            Seo = new Fratak_Shop.Data.Utilities.SeoManagement.SeoMetaData { FocusKeyword = PersianText.Truncate(article.Keyword, 150) },
        };
        context.BlogPosts.Add(post);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return post.Id;
    }

    public async Task<bool> SetPostImageAsync(Guid postId, string imageName, string alt, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cleanAlt = PersianText.Truncate(alt, 250);
        return await context.BlogPosts.Where(p => p.Id == postId && (p.ImageName == null || p.ImageName == ""))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ImageName, imageName).SetProperty(p => p.ImageAlt, cleanAlt).SetProperty(p => p.UpdatedAt, now), cancellationToken) == 1;
    }

    public async Task<(string Title, string? ImageName, string? Excerpt)?> GetPostAsync(Guid postId, CancellationToken cancellationToken = default)
    {
        var row = await context.BlogPosts.Where(p => p.Id == postId).Select(p => new { p.Title, p.ImageName, p.Excerpt }).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : (row.Title, row.ImageName, row.Excerpt);
    }

    // ── دور بیستم: بازخورد عکس و وضعیت متن ─────────────────────────────

    public async Task<ImageBlacklist> GetImageBlacklistAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var rows = await context.SmartImageFeedbacks.Where(f => f.ProductId == productId && !f.Approved)
            .OrderByDescending(f => f.CreatedAt).Take(2000)
            .Select(f => new { f.UrlKey, f.Hash })
            .ToListAsync(cancellationToken);
        return rows.Count == 0 ? ImageBlacklist.Empty : new ImageBlacklist(rows.Select(r => r.UrlKey), rows.Select(r => r.Hash));
    }

    public async Task<int> RecordImageFeedbackAsync(Guid productId, IReadOnlyList<ImageFeedbackItem> items, CancellationToken cancellationToken = default)
    {
        // هر آیتم یک کلید یکتا دارد: هش آدرس؛ اگر آدرس نداشت (عکس قدیمی بدون منبع)، هش «اثر انگشت» به‌جایش.
        var clean = items
            .Where(i => !string.IsNullOrWhiteSpace(i.Url) || i.Hash != 0)
            .Select(i => (Key: SmartImageRules.UrlKey(string.IsNullOrWhiteSpace(i.Url) ? "dhash:" + i.Hash : i.Url!), i.Hash,
                Source: SmartTaskQueue.Limit(i.Source, 100), i.Approved))
            .GroupBy(i => i.Key, StringComparer.Ordinal)
            .Select(g => g.Last())
            .Take(100)
            .ToList();
        if (clean.Count == 0)
            return 0;
        var keys = clean.Select(c => c.Key).ToList();
        var existing = await context.SmartImageFeedbacks.AsTracking()
            .Where(f => f.ProductId == productId && keys.Contains(f.UrlKey))
            .ToListAsync(cancellationToken);
        foreach (var item in clean)
        {
            var row = existing.FirstOrDefault(f => f.UrlKey == item.Key);
            if (row is null)
            {
                context.SmartImageFeedbacks.Add(new SmartImageFeedback
                {
                    ProductId = productId, UrlKey = item.Key, Hash = item.Hash, Source = item.Source, Approved = item.Approved,
                });
                continue;
            }
            row.Approved = item.Approved;
            if (item.Hash != 0)
                row.Hash = item.Hash;
            row.Source ??= item.Source;
        }
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return clean.Count;
    }

    public async Task<Dictionary<string, double>> ImageSourceScoresAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.SmartImageFeedbacks.Where(f => f.Source != null)
            .GroupBy(f => f.Source!)
            .Select(g => new { Source = g.Key, Total = g.Count(), Approved = g.Count(f => f.Approved) })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Source, r => SmartImageRules.SourceScore(r.Approved, r.Total), StringComparer.Ordinal);
    }

    public async Task<ProductBodyState?> GetBodyStateAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var product = await context.Products.Where(p => p.Id == productId).Select(p => new { p.Description }).FirstOrDefaultAsync(cancellationToken);
        if (product is null)
            return null;
        var html = product.Description ?? "";
        var words = PersianText.AnalyzeHtml(html).Words;
        var hasMarker = html.Contains(ContentWriter.ReviewMarkerAttribute, StringComparison.Ordinal);
        var revisions = await context.SeoRevisions
            .Where(r => r.Kind == Fratak_Shop.Data.Entities.SeoEngine.SeoEntityKind.Product && r.EntityId == productId
                        && r.Source == Fratak_Shop.Data.Entities.SeoEngine.SeoRevisionSource.SmartReview)
            .OrderByDescending(r => r.CreatedAt)
            .Take(5)
            .Select(r => new { r.AfterJson, r.RevertedAt })
            .ToListAsync(cancellationToken);
        var hash = PersianText.Hash(html);
        var written = hasMarker && revisions.Any(r => r.RevertedAt == null && BodyHash(r.AfterJson) == hash);
        var rolledBack = revisions.Count > 0 && revisions[0].RevertedAt != null;
        return new ProductBodyState(html, words, written, hasMarker, rolledBack);

        static string? BodyHash(string? json)
        {
            var map = Fratak_Shop.Core.Services.Seo.Autopilot.SeoAutopilotService.ReadStamps(json);
            return map.TryGetValue(Fratak_Shop.Core.Services.Seo.Autopilot.SeoFieldMap.BodyField, out var body) ? PersianText.Hash(body) : null;
        }
    }
}
