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
    IReadOnlyList<SeoFeature> Features, IReadOnlyList<string> ImageNames, decimal FinalPrice, decimal? PurchasePrice, bool IsActive,
    decimal Price = 0, DateTimeOffset? PublishedAt = null, string? WarrantyTitle = null, bool CategoryIsSystem = false, string? SourceCategory = null)
{
    /// <summary>
    /// دور ۲۲: کالا هنوز هیچ‌وقت منتشر نشده (پیش‌نویس تازه). فقط در این حالت نام/نامک خودکار عوض می‌شود و قیمت هوشمند خودکار است؛
    /// کالای منتشرشده آدرس و نامش در گوگل ثبت شده و فقط «پیشنهاد» می‌گیرد.
    /// </summary>
    public bool NeverPublished => !IsActive && PublishedAt is null;

    /// <summary>
    /// عبارت جست‌وجو برای منابع: نام انگلیسی (اگر هست) دقیق‌تر است؛ وگرنه نام فارسی. مثال: «Xiaomi Redmi Buds 4».
    /// </summary>
    public string SearchQuery => !string.IsNullOrWhiteSpace(EnglishName)
        ? (string.IsNullOrWhiteSpace(BrandEnglishName) || EnglishName.Contains(BrandEnglishName, StringComparison.OrdinalIgnoreCase) ? EnglishName : BrandEnglishName + " " + EnglishName)
        : Name;
}

/// <summary>اطلاعات یک دسته برای «تکمیل هوشمند دسته» (B19).</summary>
public sealed record SmartCategorySubject(Guid Id, string Name, string Slug, string? Description, string? ImageName, string? FaqJson, int ProductCount,
    IReadOnlyList<BlogProduct> TopProducts, IReadOnlyList<string> ProductImageNames, string? ParentName = null, string? ParentSlug = null);

/// <summary>
/// وضعیت متن «توضیحات» محصول برای تصمیم دستیار دربارهٔ «توضیحات محصول» (دور بیستم).
/// <list type="bullet">
/// <item>WrittenBySmart: متن فعلی دقیقاً همان است که دستیار نوشته (هش برابر آخرین تغییر «توضیحات محصول هوشمند» و برنگشته) ← بازنویسی مجاز.</item>
/// <item>HasMarker: نشانهٔ data-fs="review" هست ولی متن عوض شده ← کارشناس ویرایش کرده؛ دست نزن.</item>
/// <item>RolledBack: کارشناس آخرین «توضیحات محصول» را از تاریخچه برگردانده ← تصمیم انسان؛ دوباره نمی‌نویسیم.</item>
/// </list>
/// </summary>
public sealed record ProductBodyState(string Html, int Words, bool WrittenBySmart, bool HasMarker, bool RolledBack);

/// <summary>یک تصمیم کارشناس روی یک عکس پیشنهادی (برای جدول بازخورد). Url=آدرس منبع، Hash=dHash (۰ = نامعلوم)، Source=کلید منبع.</summary>
public sealed record ImageFeedbackItem(string? Url, long Hash, string? Source, bool Approved);

/// <summary>یک ردیف «ناقص» برای دکمهٔ «تکمیل هوشمند همهٔ ناقص‌ها» (B20).</summary>
/// <summary>
/// کالای ناقص برای صف «تکمیل همیشگی». دور ۲۳: کالای منتشرنشده هم می‌آید (<paramref name="IsActive"/> = false) و
/// <paramref name="Missing"/> تعداد کمبودهاست (برای اولویت: هرچه بیشتر، زودتر).
/// </summary>
public sealed record IncompleteProduct(Guid Id, string Name, int Images, int Features, bool HasShortDescription, bool HasInsight, DateTimeOffset CreatedAt,
    bool IsActive = true, int Missing = 0);

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

    /// <summary>
    /// دور ۲۳ – ترمیم متن‌های قدیمی: کالا/مقاله/دسته‌ای که در متنش «دسته‌بندی نشده»، «uncategorized» یا لینک به دستهٔ سیستمی هست
    /// با <see cref="ContentQualityGate.Clean"/> تمیز و ذخیره می‌شود. خروجی: شناسهٔ کالاهای ترمیم‌شده (برای «تکمیل هوشمند» دوباره) و
    /// تعداد مقاله/دستهٔ ترمیم‌شده. استفاده: برنامه‌ریز کارگر هوشمند (هر ۶ ساعت). مثال: توضیح «… در دستهٔ دسته‌بندی نشده …» ← آن بند حذف.
    /// </summary>
    Task<ContentRepairResult> RepairForbiddenTextAsync(int max = 100, CancellationToken cancellationToken = default);

    Task<SmartCategorySubject?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// دور ۲۶: آدرس صفحهٔ ویژهٔ (فرود) فعالی که عنوانش همان عنوان کمپین مقاله است ← «/campaign/{slug}»؛ نبود ← null.
    /// مثال: «جشنوارهٔ پاییزه» ← «/campaign/autumn-sale».
    /// </summary>
    Task<string?> CampaignUrlAsync(string title, CancellationToken cancellationToken = default);

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

    /// <summary>دور بیستم: وضعیت متن توضیحات محصول برای تصمیم «توضیحات محصول» (null = محصول پیدا نشد).</summary>
    Task<ProductBodyState?> GetBodyStateAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// دور ۲۲: اعمال «هویت» پیشنهادی کالا (نام بهینه، نام انگلیسی، نامک). هر بخش جداگانه و فقط با شرط ایمن:
    /// نام فقط اگر هنوز همان <paramref name="expectedName"/> است (کسی در این فاصله عوضش نکرده)؛ نام انگلیسی فقط اگر خالی است
    /// (مگر <paramref name="overwriteEnglish"/>)؛ نامک فقط برای کالای هرگز منتشرنشده و یکتا. خروجی: چه چیزهایی عوض شد.
    /// </summary>
    Task<IdentityApplied> ApplyIdentityAsync(Guid productId, string? name, string? englishName, string? slug, string expectedName, bool overwriteEnglish = false,
        CancellationToken cancellationToken = default);

    /// <summary>دور ۲۲: این کالا قبلاً «قیمت‌گذاری بازار» داشته (در صف یا انجام‌شده)؟ تا قیمت دوباره خودکار عوض نشود.</summary>
    Task<bool> HasMarketPriceCheckAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>دور ۲۲: تصویر فعلی و تصویرهای پیشنهادی یک دسته (null = دسته پیدا نشد).</summary>
    Task<CategoryImageState?> GetCategoryImagesAsync(Guid categoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// دور ۲۶: دسته‌های فعالی که هنوز «عکس فضاسازی» از اینترنت ندارند (فهرست پیشنهاد خالی یا فقط بنرهای قدیمیِ ساخته‌شده از عکس کالا).
    /// دسته‌ای که ادمین همهٔ پیشنهادهایش را حذف کرده («[]») یا دستهٔ سیستمی هرگز برنمی‌گردد. پرکالاترها اول.
    /// </summary>
    Task<List<(Guid Id, string Name)>> ListCategoriesNeedingPhotosAsync(int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// دور ۲۶ – «حلقهٔ آماده‌سازی»: پیش‌نویس‌های هرگز منتشرنشده که هنوز «آمادهٔ انتشار» نیستند، حداکثر <paramref name="take"/> کالا.
    /// دور ۲۸: سقف تلاش ندارد و کالای بدون «ورود هوشمند» هم برداشته می‌شود؛ ۵ تلاش اول با فاصلهٔ ۶ ساعت، بعد روزی یک بار. کالاهای برگزیده همین‌جا «ادعا» می‌شوند
    /// (شمارنده +۱ و زمان بررسی = now) تا دو کارگر هم‌زمان یک کالا را دوباره صف نکنند. قدیمی‌ترین بررسی اول.
    /// </summary>
    /// <param name="now">زمان فعلی.</param>
    /// <param name="take">حداکثر تعداد (۱ تا ۵۰).</param>
    /// <param name="cancellationToken">لغو.</param>
    /// <param name="onlyIds">دور ۲۸: فقط از میان این کالاها (برای «آماده‌سازی دوبارهٔ انتخاب‌شده‌ها» و آزمون ایزوله). null = همه.</param>
    Task<List<(Guid Id, string Name)>> ClaimReadinessCandidatesAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default,
        IReadOnlyCollection<Guid>? onlyIds = null);

    /// <summary>
    /// دور ۲۸ – دکمهٔ «آماده‌سازی دوباره همه»: شمارش تلاش و زمان آخرین تلاش همهٔ پیش‌نویس‌های هنوز ناآماده صفر می‌شود تا
    /// حلقهٔ آماده‌سازی (هر ۱۰ دقیقه) از اول سراغشان برود. خروجی: تعداد کالاها.
    /// </summary>
    Task<int> ResetReadinessAsync(CancellationToken cancellationToken = default);

    /// <summary>دور ۲۲: تصویرهای پیشنهادی همهٔ دسته‌ها (برای جدول صفحهٔ دسته‌های هوشمند).</summary>
    Task<Dictionary<Guid, CategoryImageState>> ListCategoryImagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// دور ۲۲: ذخیرهٔ فهرست تصویرهای پیشنهادی (حداکثر ۴). اگر دسته تصویر ندارد اولی خودکار تصویر دسته می‌شود («یکی پیش‌فرض تأیید»).
    /// خروجی true اگر ذخیره شد.
    /// </summary>
    Task<bool> SaveCategoryImageOptionsAsync(Guid categoryId, IReadOnlyList<string> options, CancellationToken cancellationToken = default);

    /// <summary>دور ۲۲: انتخاب یکی از تصویرهای پیشنهادی به‌عنوان تصویر دسته (فقط نام موجود در فهرست پذیرفته می‌شود).</summary>
    Task<bool> ChooseCategoryImageAsync(Guid categoryId, string option, CancellationToken cancellationToken = default);

    /// <summary>
    /// دور ۲۲: حذف یک تصویر پیشنهادی (<paramref name="option"/>) یا همه (null). اگر تصویر فعلی دسته حذف شد، پیشنهاد بعدی جایش می‌نشیند
    /// (یا بدون تصویر). خروجی: نام فایل‌هایی که باید از دیسک پاک شوند (فقط فایل‌های همین فهرست).
    /// </summary>
    Task<IReadOnlyList<string>> RemoveCategoryImagesAsync(Guid categoryId, string? option, CancellationToken cancellationToken = default);

    /// <summary>
    /// دور ۲۹ (#۸): کنار گذاشتن خودکار تصویرهای ردشده (مثل کلاژ قدیمی عکس کالاها). فرقش با حذف ادمین: اگر چیزی نماند فهرست «null» می‌شود
    /// (نه «[]») تا جست‌وجوی عکس درست دوباره انجام شود؛ تا آن موقع صفحهٔ اصلی آیکن دسته را نشان می‌دهد. خروجی: نام‌های حذف‌شده.
    /// </summary>
    Task<IReadOnlyList<string>> DropCategoryImagesAsync(Guid categoryId, IReadOnlyCollection<string> names, CancellationToken cancellationToken = default);
}

/// <summary>نتیجهٔ <see cref="ISmartCatalogService.ApplyIdentityAsync"/>: کدام بخش‌ها عوض شدند. Slug = نامک تازه (یا null).</summary>
public sealed record IdentityApplied(bool Name, bool EnglishName, string? Slug)
{
    public bool Any => Name || EnglishName || Slug is not null;
}

/// <summary>تصویر فعلی دسته و تصویرهای پیشنهادی. Options = null یعنی هنوز پیشنهادی ساخته نشده؛ [] یعنی ادمین همه را حذف کرده.</summary>
public sealed record CategoryImageState(Guid Id, string Name, string? ImageName, IReadOnlyList<string>? Options);

/// <summary>نتیجهٔ ترمیم متن‌ها: کالاهای تمیزشده (برای تکمیل دوباره) + تعداد مقاله و دستهٔ تمیزشده.</summary>
public sealed record ContentRepairResult(IReadOnlyList<Guid> Products, int Posts, int Categories)
{
    public int Total => Products.Count + Posts + Categories;
}

public sealed class SmartCatalogService(FratakShopDbContext context) : ISmartCatalogService
{
    // عبارت‌های جست‌وجو در پایگاه داده (LIKE)؛ نسخهٔ دقیق‌تر با یکسان‌سازی فارسی در ContentQualityGate.HasForbidden دوباره سنجیده می‌شود.
    private static readonly string[] RepairNeedles = ["دسته بندی نشده", "دسته‌بندی نشده", "دسته‌بندی‌نشده", "بدون دسته", "uncategorized"];

    public async Task<ContentRepairResult> RepairForbiddenTextAsync(int max = 100, CancellationToken cancellationToken = default)
    {
        max = Math.Clamp(max, 1, 500);
        var (n0, n1, n2, n3, n4) = (RepairNeedles[0], RepairNeedles[1], RepairNeedles[2], RepairNeedles[3], RepairNeedles[4]);

        var products = await context.Products.AsTracking()
            .Where(p => (p.Description != null && (p.Description.Contains(n0) || p.Description.Contains(n1) || p.Description.Contains(n2) || p.Description.Contains(n3) || p.Description.Contains(n4)))
                        || (p.ShortDescription != null && (p.ShortDescription.Contains(n0) || p.ShortDescription.Contains(n1) || p.ShortDescription.Contains(n2) || p.ShortDescription.Contains(n3) || p.ShortDescription.Contains(n4)))
                        || (p.Seo != null && p.Seo.MetaDescription != null && (p.Seo.MetaDescription.Contains(n0) || p.Seo.MetaDescription.Contains(n1) || p.Seo.MetaDescription.Contains(n2) || p.Seo.MetaDescription.Contains(n4)))
                        || (p.Seo != null && p.Seo.Title != null && (p.Seo.Title.Contains(n0) || p.Seo.Title.Contains(n1) || p.Seo.Title.Contains(n2) || p.Seo.Title.Contains(n4))))
            .OrderByDescending(p => p.IsActive).ThenByDescending(p => p.ViewCount).Take(max).ToListAsync(cancellationToken);
        var repaired = new List<Guid>();
        foreach (var product in products)
        {
            var changed = false;
            product.Description = CleanField(product.Description, ref changed);
            product.ShortDescription = CleanField(product.ShortDescription, ref changed);
            if (product.Seo is not null)
            {
                product.Seo.MetaDescription = CleanField(product.Seo.MetaDescription, ref changed);
                product.Seo.Title = CleanField(product.Seo.Title, ref changed);
            }
            if (changed)
                repaired.Add(product.Id);
        }

        var posts = await context.BlogPosts.AsTracking()
            .Where(b => b.Content.Contains(n0) || b.Content.Contains(n1) || b.Content.Contains(n2) || b.Content.Contains(n4) || b.Content.Contains("/category/uncategorized")
                        || (b.Excerpt != null && (b.Excerpt.Contains(n0) || b.Excerpt.Contains(n1) || b.Excerpt.Contains(n2) || b.Excerpt.Contains(n4))))
            .Take(max).ToListAsync(cancellationToken);
        var postCount = 0;
        foreach (var post in posts)
        {
            var changed = false;
            post.Content = CleanField(post.Content, ref changed) ?? "";
            post.Excerpt = CleanField(post.Excerpt, ref changed);
            if (changed)
                postCount++;
        }

        var categories = await context.Categories.AsTracking()
            .Where(c => !c.IsSystem && c.Description != null && (c.Description.Contains(n0) || c.Description.Contains(n1) || c.Description.Contains(n2) || c.Description.Contains(n4)))
            .Take(max).ToListAsync(cancellationToken);
        var categoryCount = 0;
        foreach (var category in categories)
        {
            var changed = false;
            category.Description = CleanField(category.Description, ref changed);
            if (changed)
                categoryCount++;
        }

        if (repaired.Count + postCount + categoryCount > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
            DataChangeNotifier.Publish(typeof(Product), typeof(BlogPost), typeof(Data.Entities.Categories.Category));
        }
        context.ChangeTracker.Clear();
        return new ContentRepairResult(repaired, postCount, categoryCount);
    }

    /// <summary>تمیز کردن یک فیلد فقط اگر واقعاً ایراد قطعی دارد؛ نتیجهٔ خالی ← null (تا نویسندهٔ هوشمند دوباره بنویسد).</summary>
    private static string? CleanField(string? value, ref bool changed)
    {
        if (string.IsNullOrWhiteSpace(value) || ContentQualityGate.Issues(value, style: false).Count == 0)
            return value;
        var cleaned = ContentQualityGate.Clean(value);
        if (string.Equals(cleaned, value, StringComparison.Ordinal))
            return value;
        changed = true;
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

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
                x.PublishedAt, x.WarrantyTitle, CategoryIsSystem = x.Category.IsSystem, x.SourceCategory,
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
        return new SmartProductSubject(p.Id, p.Name, p.EnglishName, p.Slug, p.SKU, p.Gtin, p.Mpn, p.BrandName, p.BrandEnglish, p.CategoryId, p.CategoryIsSystem ? null : p.CategoryName,
            p.SupplierUrl, p.ShortDescription, p.DescriptionLength, features, images, discounted ? p.DiscountPrice!.Value : p.Price, p.PurchasePrice, p.IsActive,
            p.Price, p.PublishedAt, p.WarrantyTitle, p.CategoryIsSystem, p.SourceCategory);
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

    /// <summary>
    /// دور ۲۳ – «تکمیل همیشگی»: همهٔ کالاهای ناقص، چه منتشرشده چه منتشرنشده.
    /// <list type="bullet">
    /// <item>ناقص یعنی: بی‌دسته (دستهٔ سیستمی)، بدون نام انگلیسی، کمتر از ۲ عکس، کمتر از ۴ مشخصه، بدون توضیح کوتاه،
    /// توضیحات کمتر از ۶۰۰ نویسه، یا (فقط وقتی دست‌کم ۳ نظر تأییدشده دارد) بدون «خلاصهٔ خرید».</item>
    /// <item>ترتیب: کالای منتشرنشده اول (باید آمادهٔ انتشار شود)، بعد پربازدیدترها، بعد تازه‌ترها.</item>
    /// </list>
    /// مثال: پیش‌نویس تازه با یک عکس و بدون توضیحات ← در صدر فهرست.
    /// </summary>
    public async Task<List<IncompleteProduct>> ListIncompleteAsync(int take = 200, CancellationToken cancellationToken = default)
    {
        var rows = await context.Products
            .Select(p => new
            {
                p.Id, p.Name, p.CreatedAt, p.IsActive, p.ViewCount,
                Images = context.ProductImages.Count(i => i.ProductId == p.Id),
                Features = context.ProductFeatures.Count(f => f.ProductId == p.Id),
                HasShort = p.ShortDescription != null && p.ShortDescription != "",
                HasInsight = context.ProductInsights.Any(i => i.ProductId == p.Id),
                Reviews = context.Comments.Count(c => c.ProductId == p.Id && c.Type == CommentType.Review && c.Status == CommentStatus.Approved),
                NoCategory = p.Category.IsSystem,
                NoEnglish = p.EnglishName == null || p.EnglishName == "",
                ShortBody = p.Description == null || p.Description.Length < 600,
            })
            .Where(p => p.Images < 2 || p.Features < 4 || !p.HasShort || p.NoCategory || p.NoEnglish || p.ShortBody || (!p.HasInsight && p.Reviews >= 3))
            .OrderBy(p => p.IsActive)
            .ThenByDescending(p => p.ViewCount)
            .ThenByDescending(p => p.CreatedAt)
            .Take(Math.Clamp(take, 1, 1000))
            .ToListAsync(cancellationToken);
        return rows.Select(r => new IncompleteProduct(r.Id, r.Name, r.Images, r.Features, r.HasShort, r.HasInsight, r.CreatedAt, r.IsActive,
            (r.Images < 2 ? 1 : 0) + (r.Features < 4 ? 1 : 0) + (r.HasShort ? 0 : 1) + (r.NoCategory ? 1 : 0) + (r.NoEnglish ? 1 : 0) + (r.ShortBody ? 1 : 0)
            + (!r.HasInsight && r.Reviews >= 3 ? 1 : 0))).ToList();
    }

    public async Task<string?> CampaignUrlAsync(string title, CancellationToken cancellationToken = default)
    {
        title = (title ?? "").Trim();
        if (title.Length == 0)
            return null;
        var slug = await context.LandingPages.Where(l => l.IsActive && l.Title == title)
            .OrderByDescending(l => l.CreatedAt).Select(l => l.Slug).FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(slug) ? null : "/campaign/" + slug;
    }

    public async Task<SmartCategorySubject?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        var c = await context.Categories.Where(x => x.Id == categoryId)
            .Select(x => new
            {
                x.Id, x.Name, x.Slug, x.Description, x.ImageName, Faq = x.Seo != null ? x.Seo.FaqJson : null,
                // دور ۲۶: نام و نامک والد برای گسترش کلمه‌های جست‌وجوی عکس فضاسازی.
                ParentName = x.ParentCategory != null ? x.ParentCategory.Name : null,
                ParentSlug = x.ParentCategory != null ? x.ParentCategory.Slug : null,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (c is null)
            return null;
        var count = await context.Products.CountAsync(p => p.CategoryId == categoryId && p.IsActive, cancellationToken);
        var products = await GetBlogProductsAsync(categoryId, null, 8, cancellationToken);
        // دور ۲۲: تصویر کالاهای پیش‌نویس هم حساب می‌شود (دستهٔ تازه‌ساخته هنوز کالای فعال ندارد ولی باید تصویر پیشنهادی بگیرد)؛
        // اولویت با کالای فعال و موجود و پرفروش. هر کالا فقط عکس اصلی (یا اولین عکس) خودش ← تصویرها از کالاهای متفاوت‌اند.
        var images = await context.Products
            .Where(p => p.CategoryId == categoryId)
            .OrderByDescending(p => p.IsActive && p.StockQuantity > 0).ThenByDescending(p => p.SalesCount).ThenByDescending(p => p.CreatedAt)
            .Select(p => p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).FirstOrDefault())
            .Where(name => name != null && !name.StartsWith("http") && !name.StartsWith("/"))
            .Take(12).ToListAsync(cancellationToken);
        return new SmartCategorySubject(c.Id, c.Name, c.Slug, c.Description, c.ImageName, c.Faq, count, products, images.Select(i => i!).ToList(),
            c.ParentName, c.ParentSlug);
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

    public async Task<IdentityApplied> ApplyIdentityAsync(Guid productId, string? name, string? englishName, string? slug, string expectedName, bool overwriteEnglish = false,
        CancellationToken cancellationToken = default)
    {
        var product = await context.Products.AsTracking().FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
            return new IdentityApplied(false, false, null);
        bool nameChanged = false, englishChanged = false;
        string? newSlug = null;

        var cleanName = name?.Trim();
        if (!string.IsNullOrWhiteSpace(cleanName) && cleanName.Length is >= 3 and <= 250 && cleanName != product.Name
            && string.Equals(product.Name, expectedName, StringComparison.Ordinal))
        {
            product.Name = cleanName;
            nameChanged = true;
        }

        var cleanEnglish = englishName?.Trim();
        if (!string.IsNullOrWhiteSpace(cleanEnglish) && cleanEnglish.Length <= 250 && cleanEnglish != product.EnglishName
            && (overwriteEnglish || string.IsNullOrWhiteSpace(product.EnglishName)))
        {
            product.EnglishName = cleanEnglish;
            englishChanged = true;
        }

        // نامک فقط برای پیش‌نویسی که هرگز منتشر نشده (آدرسش هیچ‌جا ثبت نشده) و فقط انگلیسی تمیز.
        if (!string.IsNullOrWhiteSpace(slug) && EnglishSlug.IsEnglish(slug) && slug.Length <= 200 && !product.IsActive && product.PublishedAt is null
            && !string.Equals(slug, product.Slug, StringComparison.Ordinal))
        {
            var taken = await context.Products.IgnoreQueryFilters().Where(p => p.Id != productId && p.Slug.StartsWith(slug)).Select(p => p.Slug)
                .ToListAsync(cancellationToken);
            var set = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unique = EnglishSlug.Unique(slug, set.Contains, 200);
            if (unique != product.Slug)
            {
                product.Slug = unique;
                newSlug = unique;
            }
        }

        if (nameChanged || englishChanged || newSlug is not null)
        {
            // نام عوض شد ← متن جایگزین عکس اصلی هم اگر همان نام قبلی بود، به‌روز می‌شود (alt درست برای سئو).
            if (nameChanged)
            {
                await context.ProductImages.Where(i => i.ProductId == productId && i.AltText == expectedName)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.AltText, cleanName), cancellationToken);
            }
            await context.SaveChangesAsync(cancellationToken);
        }
        context.ChangeTracker.Clear();
        return new IdentityApplied(nameChanged, englishChanged, newSlug);
    }

    public Task<bool> HasMarketPriceCheckAsync(Guid productId, CancellationToken cancellationToken = default) =>
        context.MarketPriceChecks.AnyAsync(c => c.ProductId == productId, cancellationToken);

    public async Task<List<(Guid Id, string Name)>> ListCategoriesNeedingPhotosAsync(int take, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 50);
        var rows = await context.Categories
            .Where(c => c.IsActive && !c.IsSystem
                        // دور ۲۸: دسته‌ای که هنوز عکس «سوژهٔ تأییدشده» (-photo-w-) ندارد دوباره بازبینی می‌شود.
                        && (c.ImageOptionsJson == null || (c.ImageOptionsJson != "[]" && !c.ImageOptionsJson.Contains(CategorySubjectImages.Marker))))
            .OrderByDescending(c => c.Products.Count(p => p.IsActive)).ThenBy(c => c.DisplayOrder)
            .Select(c => new { c.Id, c.Name })
            .Take(take).ToListAsync(cancellationToken);
        return rows.Select(r => (r.Id, r.Name)).ToList();
    }

    /// <summary>
    /// دور ۲۸: «سقف ۵ تلاش» حذف شد (خواستهٔ کاربر). از این تعداد تلاش به بعد کالا رها نمی‌شود، فقط فاصلهٔ تلاش‌ها
    /// به <see cref="ReadySlowGap"/> می‌رسد تا منابع بیرونی و هاست درگیر نشوند.
    /// </summary>
    public const int MaxReadyAttempts = 5;

    /// <summary>کمترین فاصلهٔ دو تلاش «حلقهٔ آماده‌سازی» برای یک کالا (۵ تلاش اول).</summary>
    public static readonly TimeSpan ReadyRetryGap = TimeSpan.FromHours(6);

    /// <summary>دور ۲۸: فاصلهٔ تلاش‌ها بعد از <see cref="MaxReadyAttempts"/> تلاش (روزی یک بار، بی‌پایان تا آماده شود).</summary>
    public static readonly TimeSpan ReadySlowGap = TimeSpan.FromHours(24);

    // نقیض «آمادهٔ انتشار» به‌صورت عبارت قابل ترجمه به SQL (همان شرط‌ها؛ بدون تکرار منطق).
    private static readonly System.Linq.Expressions.Expression<Func<Product, bool>> NotReady =
        System.Linq.Expressions.Expression.Lambda<Func<Product, bool>>(
            System.Linq.Expressions.Expression.Not(Fratak_Shop.Core.Services.Products.ProductCompleteness.ReadyToPublish.Body),
            Fratak_Shop.Core.Services.Products.ProductCompleteness.ReadyToPublish.Parameters);

    public async Task<List<(Guid Id, string Name)>> ClaimReadinessCandidatesAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default,
        IReadOnlyCollection<Guid>? onlyIds = null)
    {
        take = Math.Clamp(take, 1, 50);
        var due = now - ReadyRetryGap;
        var slowDue = now - ReadySlowGap;
        // دور ۲۸: کالایی که «ورود هوشمند» نگرفته (SmartIntakeAt خالی، مثلاً دستی ساخته شده یا پیش از دور ۲۳ وارد شده) هم برداشته می‌شود
        // و سقف تلاش نداریم؛ بعد از ۵ تلاش فقط روزی یک بار.
        var scope = onlyIds is null ? context.Products : context.Products.Where(p => onlyIds.Contains(p.Id));
        var rows = await scope
            .Where(p => !p.IsActive && p.PublishedAt == null
                        && (p.SmartReadyCheckedAt == null
                            || (p.SmartReadyAttempts < MaxReadyAttempts && p.SmartReadyCheckedAt <= due) || (p.SmartReadyAttempts >= MaxReadyAttempts && p.SmartReadyCheckedAt <= slowDue)))
            .Where(NotReady)
            .OrderBy(p => p.SmartReadyCheckedAt ?? p.CreatedAt)
            .Select(p => new { p.Id, p.Name })
            .Take(take).ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return [];
        var ids = rows.Select(r => r.Id).ToList();
        // ادعای اتمی: فقط ردیف‌هایی که هنوز سررسیدند به‌روز می‌شوند (کارگر دیگر در همین لحظه همان را برنداشته باشد).
        var claimed = await context.Products
            .Where(p => ids.Contains(p.Id) && (p.SmartReadyCheckedAt == null
                                               || (p.SmartReadyAttempts < MaxReadyAttempts && p.SmartReadyCheckedAt <= due) || (p.SmartReadyAttempts >= MaxReadyAttempts && p.SmartReadyCheckedAt <= slowDue)))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.SmartReadyAttempts, p => p.SmartReadyAttempts + 1)
                .SetProperty(p => p.SmartReadyCheckedAt, now), cancellationToken);
        return claimed == 0 ? [] : rows.Select(r => (r.Id, r.Name)).ToList();
    }

    public async Task<int> ResetReadinessAsync(CancellationToken cancellationToken = default) =>
        await context.Products
            .Where(p => !p.IsActive && p.PublishedAt == null)
            .Where(NotReady)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.SmartReadyAttempts, 0).SetProperty(p => p.SmartReadyCheckedAt, (DateTimeOffset?)null), cancellationToken);

    public async Task<CategoryImageState?> GetCategoryImagesAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        var row = await context.Categories.Where(c => c.Id == categoryId).Select(c => new { c.Id, c.Name, c.ImageName, c.ImageOptionsJson })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new CategoryImageState(row.Id, row.Name, row.ImageName, CategoryImages.Parse(row.ImageOptionsJson));
    }

    public async Task<Dictionary<Guid, CategoryImageState>> ListCategoryImagesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.Categories.Select(c => new { c.Id, c.Name, c.ImageName, c.ImageOptionsJson }).Take(500).ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Id, r => new CategoryImageState(r.Id, r.Name, r.ImageName, CategoryImages.Parse(r.ImageOptionsJson)));
    }

    public async Task<bool> SaveCategoryImageOptionsAsync(Guid categoryId, IReadOnlyList<string> options, CancellationToken cancellationToken = default)
    {
        var category = await context.Categories.AsTracking().FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken);
        if (category is null)
            return false;
        var clean = options.Where(CategoryImages.IsSafeName).Distinct(StringComparer.Ordinal).Take(CategoryImages.Max).ToList();
        var old = CategoryImages.Parse(category.ImageOptionsJson) ?? [];
        // دور ۲۶: تصویر فعلی «بنر خودکار از عکس کالا»ی قدیمی (از فهرست پیشنهادها و نه عکس فضاسازی) با اولین عکس فضاسازی تازه عوض می‌شود؛
        // تصویری که ادمین خودش آپلود کرده (خارج از فهرست) یا عکس فضاسازی انتخاب‌شده هرگز عوض نمی‌شود.
        var autoBanner = category.ImageName is { } current && old.Contains(current, StringComparer.Ordinal) && !CategoryImages.IsPhoto(current)
                         && !clean.Contains(current, StringComparer.Ordinal);
        // دور ۲۸ (بازبینی عکس‌های موجود): عکس فضاسازی قدیمی (بدون نشان «-photo-w-»، یعنی بررسی سوژه را نگذرانده) هم با اولین
        // عکس سوژهٔ تازه عوض می‌شود. عکسی که ادمین خودش آپلود کرده (خارج از فهرست پیشنهادها) هرگز دست نمی‌خورد.
        var oldPhoto = category.ImageName is { } existing && old.Contains(existing, StringComparer.Ordinal) && CategoryImages.IsPhoto(existing)
                       && !CategorySubjectImages.IsSubject(existing) && clean.Any(CategorySubjectImages.IsSubject)
                       && !clean.Contains(existing, StringComparer.Ordinal);
        category.ImageOptionsJson = CategoryImages.Serialize(clean);
        if ((string.IsNullOrWhiteSpace(category.ImageName) || autoBanner || oldPhoto) && clean.Count > 0)
            category.ImageName = clean.FirstOrDefault(CategorySubjectImages.IsSubject) ?? clean.FirstOrDefault(CategoryImages.IsPhoto) ?? clean[0];
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return true;
    }

    public async Task<bool> ChooseCategoryImageAsync(Guid categoryId, string option, CancellationToken cancellationToken = default)
    {
        var category = await context.Categories.AsTracking().FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken);
        var options = CategoryImages.Parse(category?.ImageOptionsJson);
        if (category is null || options is null || !options.Contains(option, StringComparer.Ordinal))
            return false;
        if (category.ImageName != option)
        {
            category.ImageName = option;
            await context.SaveChangesAsync(cancellationToken);
        }
        context.ChangeTracker.Clear();
        return true;
    }

    public async Task<IReadOnlyList<string>> RemoveCategoryImagesAsync(Guid categoryId, string? option, CancellationToken cancellationToken = default)
    {
        var category = await context.Categories.AsTracking().FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken);
        var options = CategoryImages.Parse(category?.ImageOptionsJson);
        if (category is null || options is null || options.Count == 0)
            return [];
        var removed = option is null ? options.ToList() : options.Where(o => string.Equals(o, option, StringComparison.Ordinal)).ToList();
        if (removed.Count == 0)
            return [];
        var remaining = options.Except(removed, StringComparer.Ordinal).ToList();
        category.ImageOptionsJson = CategoryImages.Serialize(remaining);
        if (category.ImageName is { } current && removed.Contains(current, StringComparer.Ordinal))
            category.ImageName = remaining.FirstOrDefault();
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return removed;
    }

    public async Task<IReadOnlyList<string>> DropCategoryImagesAsync(Guid categoryId, IReadOnlyCollection<string> names, CancellationToken cancellationToken = default)
    {
        if (names.Count == 0)
            return [];
        var category = await context.Categories.AsTracking().FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken);
        var options = CategoryImages.Parse(category?.ImageOptionsJson);
        if (category is null || options is null)
            return [];
        var removed = options.Where(o => names.Contains(o, StringComparer.Ordinal)).ToList();
        if (removed.Count == 0)
            return [];
        var remaining = options.Except(removed, StringComparer.Ordinal).ToList();
        // چیزی نماند ← null یعنی «هنوز پیشنهادی ساخته نشده» تا BackfillCategoryPhotos دوباره دنبال عکس درست بگردد.
        category.ImageOptionsJson = remaining.Count == 0 ? null : CategoryImages.Serialize(remaining);
        if (category.ImageName is { } current && removed.Contains(current, StringComparer.Ordinal))
            category.ImageName = remaining.FirstOrDefault(CategorySubjectImages.IsSubject);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return removed;
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
