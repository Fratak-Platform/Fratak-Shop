using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Entities.Brands;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Contents;
using Fratak_Shop.Data.Entities.Faqs;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Data.Entities.SeoRedirects;
using Fratak_Shop.Data.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Fratak_Shop.Data.Contexts;

// ═══════════════════════════════════════════════════════════════════════════
//  قانون‌های سئوی هنگام ذخیره (دور سئوی خودکار)
//  چرا اینجا؟ چون هر مسیری که داده را عوض کند (پنل، درون‌ریزی سایت، همگام‌سازی تأمین‌کننده، کپی محصول، موتور سئو،
//  جدول‌های مدیریتی) در نهایت از SaveChanges می‌گذرد؛ قانون در یک جا نوشته می‌شود و هیچ مسیری جا نمی‌ماند.
//   ۱) نامک انگلیسی: نامک فارسی/ناتمیز پیش از ذخیره انگلیسی و یکتا می‌شود (EnglishSlug).
//   ۲) ریدایرکت ۳۰۱ خودکار: تغییر نامک ← آدرس قدیم به جدید (بدون زنجیره و حلقه).
//   ۳) حذف محصول/مقاله/دسته/برند ← ریدایرکت به نزدیک‌ترین صفحهٔ مرتبط (دسته، وبلاگ، فهرست محصولات) تا لینک‌ها و
//      اعتبار گوگل آن صفحه هدر نرود (مورد ۱۷۴ و ۱۹۳ پیشنهاد سئو).
//   ۴) صفحهٔ تازه روی آدرسی که قبلاً ریدایرکت داشت ← آن ریدایرکت برداشته می‌شود تا صفحهٔ تازه دیده شود.
//   ۵) اعلام صفحه‌های ذخیره‌شده به SeoChangeFeed (پر کردن خودکار فیلدهای سئو، IndexNow، مرتب‌سازی HTML).
// ═══════════════════════════════════════════════════════════════════════════
public partial class FratakShopDbContext
{
    // مرحلهٔ سئوی خودکار: جدول‌های تازه
    public DbSet<SeoAutopilotConfig> SeoAutopilotConfigs { get; set; } = null!;
    public DbSet<SeoAutoState> SeoAutoStates { get; set; } = null!;
    public DbSet<SeoRevision> SeoRevisions { get; set; } = null!;
    public DbSet<SeoCrawlRun> SeoCrawlRuns { get; set; } = null!;
    public DbSet<SeoCrawlPage> SeoCrawlPages { get; set; } = null!;
    public DbSet<SeoTask> SeoTasks { get; set; } = null!;
    public DbSet<SeoBotVisit> SeoBotVisits { get; set; } = null!;
    public DbSet<SeoVitalStat> SeoVitalStats { get; set; } = null!;
    public DbSet<SeoTitleTest> SeoTitleTests { get; set; } = null!;

    /// <summary>
    /// پیشوند آدرس عمومی هر نوع صفحهٔ نامک‌دار (برای ساخت ریدایرکت). نوعی که صفحهٔ مستقل ندارد ← null.
    /// مثال: <c>SeoPathPrefix(typeof(Product)) == "/product/"</c>.
    /// </summary>
    public static string? SeoPathPrefix(Type type) =>
        type == typeof(Product) ? "/product/"
        : type == typeof(BlogPost) ? "/blog/"
        : type == typeof(Category) ? "/category/"
        : type == typeof(Brand) ? "/brand/"
        : type == typeof(BlogCategory) ? "/blog/category/"
        : type == typeof(ContentPage) ? "/page/"
        : null;

    /// <summary>
    /// قانون‌های ۱ تا ۴. پیش از ثبت تاریخ‌ها اجرا می‌شود تا ریدایرکت‌های تازه هم شناسه و تاریخ بگیرند.
    /// خطای این بخش هرگز ذخیرهٔ اصلی را خراب نمی‌کند (فقط از قانون رد می‌شود).
    /// </summary>
    private void ApplySeoRules()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => e.Metadata.FindProperty("Slug") is not null && e.Entity is BaseEntity)
            .ToList();
        if (entries.Count == 0)
            return;

        var newPaths = new List<string>();
        foreach (var entry in entries)
        {
            try
            {
                if (IsBeingDeleted(entry))
                {
                    AddDeleteRedirect(entry);
                    continue;
                }

                EnforceEnglishSlug(entry);

                var prefix = SeoPathPrefix(entry.Metadata.ClrType);
                if (prefix is null)
                    continue;

                var slugProperty = entry.Property("Slug");
                var slug = slugProperty.CurrentValue as string;
                if (string.IsNullOrWhiteSpace(slug))
                    continue;

                if (entry.State == EntityState.Added)
                {
                    newPaths.Add(Path(prefix, slug));
                }
                else if (slugProperty.IsModified && slugProperty.OriginalValue is string original
                         && !string.IsNullOrWhiteSpace(original) && !string.Equals(original, slug, StringComparison.Ordinal))
                {
                    AddSlugRedirect(prefix, original, slug);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // قانون سئو نباید ذخیرهٔ اصلی را متوقف کند؛ نامک ناتمیز در کار شبانه دوباره بررسی می‌شود.
            }
        }

        if (newPaths.Count > 0)
            RemoveRedirectsFrom(newPaths);
    }

    private static bool IsBeingDeleted(EntityEntry entry)
    {
        if (entry.State == EntityState.Deleted)
            return true;
        if (entry.State != EntityState.Modified || entry.Entity is not BaseEntity)
            return false;
        var deleted = entry.Property(nameof(BaseEntity.IsDeleted));
        return deleted.IsModified && deleted.CurrentValue is true && deleted.OriginalValue is false;
    }

    private static string Path(string prefix, string slug) => (prefix + slug).TrimEnd('/').ToLowerInvariant();

    private static string Target(string prefix, string slug) => prefix + Uri.EscapeDataString(slug);

    // ── ۱) نامک انگلیسی ──────────────────────────────────────────────────

    /// <summary>
    /// اگر نامک انگلیسیِ تمیز نیست، از خود نامک (یا نام انگلیسی/نام صفحه) نامک انگلیسی یکتا ساخته می‌شود.
    /// مثال: محصول با نامک «گوشی-سامسونگ-a55» و نام انگلیسی «Samsung Galaxy A55» ← «samsung-galaxy-a55».
    /// </summary>
    private void EnforceEnglishSlug(EntityEntry entry)
    {
        var property = entry.Property("Slug");
        var current = property.CurrentValue as string;
        if (EnglishSlug.IsEnglish(current))
            return;

        var max = Math.Min(entry.Metadata.FindProperty("Slug")?.GetMaxLength() ?? 200, EnglishSlug.DefaultMaxLength);
        var (latin, name) = entry.Entity switch
        {
            Product p => (p.EnglishName, p.Name),
            Brand b => (b.EnglishName, b.Name),
            Category c => ((string?)null, c.Name),
            BlogPost p => (null, p.Title),
            BlogCategory c => (null, c.Name),
            ContentPage p => (null, p.Title),
            FaqCategory c => (null, c.Title),
            ProductFilterAttribute a => (null, a.Title),
            _ => (null, null),
        };

        var slug = EnglishSlug.From(EnglishSlug.Prefer(current, latin), max);
        if (slug.Length < 2)
            slug = EnglishSlug.From(EnglishSlug.Prefer(name, latin), max);
        if (slug.Length < 2)
        {
            var id = ((BaseEntity)entry.Entity).Id;
            slug = entry.Metadata.ClrType.Name.ToLowerInvariant() + "-" + (id == Guid.Empty ? Guid.NewGuid() : id).ToString("N")[..8];
        }

        property.CurrentValue = EnglishSlug.Unique(slug, candidate => SlugTaken(entry, candidate), max);
    }

    /// <summary>آیا این نامک را صفحهٔ دیگری از همین نوع (در همین ذخیره یا در پایگاه داده) گرفته است؟</summary>
    private bool SlugTaken(EntityEntry entry, string slug)
    {
        var entity = (BaseEntity)entry.Entity;
        var type = entry.Metadata.ClrType;
        Guid? filterCategory = entity is ProductFilterAttribute attribute ? attribute.CategoryId : null;

        foreach (var other in ChangeTracker.Entries())
        {
            if (ReferenceEquals(other.Entity, entity) || other.Metadata.ClrType != type || other.State == EntityState.Deleted)
                continue;
            if (other.Entity is ProductFilterAttribute otherAttribute && otherAttribute.CategoryId != filterCategory)
                continue;
            if (string.Equals(other.Property("Slug").CurrentValue as string, slug, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var id = entity.Id;
        return entity switch
        {
            Product => Products.AsNoTracking().Any(e => e.Slug == slug && e.Id != id),
            Brand => Brands.AsNoTracking().Any(e => e.Slug == slug && e.Id != id),
            Category => Categories.AsNoTracking().Any(e => e.Slug == slug && e.Id != id),
            BlogPost => BlogPosts.AsNoTracking().Any(e => e.Slug == slug && e.Id != id),
            BlogCategory => BlogCategories.AsNoTracking().Any(e => e.Slug == slug && e.Id != id),
            ContentPage => ContentPages.AsNoTracking().Any(e => e.Slug == slug && e.Id != id),
            FaqCategory => FaqCategories.AsNoTracking().Any(e => e.Slug == slug && e.Id != id),
            ProductFilterAttribute => ProductFilterAttributes.AsNoTracking().Any(e => e.Slug == slug && e.Id != id && e.CategoryId == filterCategory),
            _ => false,
        };
    }

    // ── ۲) ریدایرکت تغییر نامک ───────────────────────────────────────────

    /// <summary>
    /// ریدایرکت ۳۰۱ آدرس قدیم ← جدید، بدون زنجیره و حلقه (همان قانون موتور سئو):
    /// ریدایرکت‌هایی که به آدرس قدیم می‌رفتند مستقیم به آدرس جدید می‌روند؛ ریدایرکتِ «از» آدرس جدید حذف می‌شود.
    /// اگر همین ذخیره قبلاً ریدایرکت آدرس قدیم را ساخته باشد (موتور سئو)، کاری انجام نمی‌شود.
    /// </summary>
    private void AddSlugRedirect(string prefix, string oldSlug, string newSlug)
    {
        var oldPath = Path(prefix, oldSlug);
        var newPath = Path(prefix, newSlug);
        var target = Target(prefix, newSlug);
        if (TrackedRedirectFrom(oldPath))
            return;

        var oldTarget = Target(prefix, oldSlug);
        var rawOld = prefix + oldSlug;
        var related = SeoRedirects.AsTracking()
            .Where(r => r.FromPath == oldPath || r.FromPath == newPath || r.ToUrl == oldPath || r.ToUrl == oldTarget || r.ToUrl == rawOld)
            .ToList();
        foreach (var redirect in related)
        {
            if (string.Equals(redirect.FromPath, newPath, StringComparison.OrdinalIgnoreCase))
                SeoRedirects.Remove(redirect);
            else
            {
                redirect.ToUrl = target;
                redirect.IsActive = true;
            }
        }

        if (!related.Any(r => string.Equals(r.FromPath, oldPath, StringComparison.OrdinalIgnoreCase)))
        {
            SeoRedirects.Add(new SeoRedirect
            {
                FromPath = oldPath,
                ToUrl = target,
                Type = RedirectType.MovedPermanently,
                Notes = "خودکار: تغییر نامک",
                IsActive = true,
            });
        }
    }

    private bool TrackedRedirectFrom(string path) => ChangeTracker.Entries<SeoRedirect>()
        .Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Unchanged
                  && string.Equals(e.Entity.FromPath, path, StringComparison.OrdinalIgnoreCase) && !e.Entity.IsDeleted);

    // ── ۳) ریدایرکت حذف ─────────────────────────────────────────────────

    /// <summary>
    /// صفحهٔ حذف‌شده ← نزدیک‌ترین صفحهٔ مرتبط با ۳۰۱: محصول ← دسته‌اش (یا /products)، مقاله ← دستهٔ وبلاگش (یا /blog)،
    /// دسته ← دستهٔ والد (یا /products)، برند ← /products، دستهٔ وبلاگ ← /blog. اگر از قبل ریدایرکتی از آن آدرس باشد، دست نمی‌خورد.
    /// </summary>
    private void AddDeleteRedirect(EntityEntry entry)
    {
        var prefix = SeoPathPrefix(entry.Metadata.ClrType);
        var slug = (entry.State == EntityState.Deleted ? entry.Property("Slug").OriginalValue : entry.Property("Slug").CurrentValue) as string;
        if (prefix is null || string.IsNullOrWhiteSpace(slug) || entry.Entity is ContentPage)
            return;

        var from = Path(prefix, slug);
        if (TrackedRedirectFrom(from) || SeoRedirects.AsNoTracking().Any(r => r.FromPath == from))
            return;

        string? target = entry.Entity switch
        {
            Product product => CategoryTarget(product.CategoryId) ?? "/products",
            BlogPost post => BlogCategories.AsNoTracking().Where(c => c.Id == post.BlogCategoryId && c.IsActive)
                .Select(c => "/blog/category/" + c.Slug).FirstOrDefault() ?? "/blog",
            Category category => (category.ParentId is { } parent ? CategoryTarget(parent) : null) ?? "/products",
            Brand => "/products",
            BlogCategory => "/blog",
            _ => null,
        };
        if (target is null || string.Equals(target, from, StringComparison.OrdinalIgnoreCase))
            return;

        SeoRedirects.Add(new SeoRedirect
        {
            FromPath = from,
            ToUrl = target,
            Type = RedirectType.MovedPermanently,
            Notes = "خودکار: حذف صفحه",
            IsActive = true,
        });
    }

    private string? CategoryTarget(Guid categoryId)
    {
        var deleting = ChangeTracker.Entries<Category>().Any(e => e.Entity.Id == categoryId && IsBeingDeleted(e));
        if (deleting)
            return null;
        var slug = Categories.AsNoTracking().Where(c => c.Id == categoryId && c.IsActive).Select(c => c.Slug).FirstOrDefault();
        return slug is null ? null : "/category/" + Uri.EscapeDataString(slug);
    }

    // ── ۴) آدرس تازه ← برداشتن ریدایرکت قدیمی همان آدرس ──────────────────

    private void RemoveRedirectsFrom(List<string> paths)
    {
        var stale = SeoRedirects.AsTracking().Where(r => paths.Contains(r.FromPath)).ToList();
        foreach (var redirect in stale)
        {
            // ریدایرکتی که همین ذخیره ساخته (مثلاً تغییر نامک صفحهٔ دیگر) نگه داشته می‌شود.
            if (Entry(redirect).State == EntityState.Unchanged)
                SeoRedirects.Remove(redirect);
        }
    }

    // ── ۵) اعلام صفحه‌های ذخیره‌شده ─────────────────────────────────────

    /// <summary>
    /// صفحه‌های سئوپذیر این ذخیره (بعد از ثبت شناسه‌ها). تصویر یا مشخصهٔ محصول هم یعنی «محصول عوض شد».
    /// اگر شنونده‌ای نباشد یا ذخیره از داخل خود سئوی خودکار باشد، فهرست ساخته نمی‌شود.
    /// </summary>
    private List<SeoChange> CollectSeoChanges()
    {
        if (!SeoChangeFeed.HasListeners || SeoChangeFeed.IsSuppressed)
            return [];

        var changes = new Dictionary<(SeoEntityKind, Guid), SeoChange>();
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified) || entry.Entity is not BaseEntity { IsDeleted: false } entity)
                continue;
            var added = entry.State == EntityState.Added;
            (SeoEntityKind Kind, Guid Id)? key = entity switch
            {
                Product => (SeoEntityKind.Product, entity.Id),
                Category => (SeoEntityKind.Category, entity.Id),
                Brand => (SeoEntityKind.Brand, entity.Id),
                BlogPost => (SeoEntityKind.BlogPost, entity.Id),
                ContentPage => (SeoEntityKind.ContentPage, entity.Id),
                ProductImage image => (SeoEntityKind.Product, image.ProductId),
                ProductFeature feature => (SeoEntityKind.Product, feature.ProductId),
                _ => null,
            };
            if (key is not { } k || k.Id == Guid.Empty)
                continue;
            if (!changes.TryGetValue(k, out var existing) || (added && !existing.Added && entity is not ProductImage and not ProductFeature))
                changes[k] = new SeoChange(k.Kind, k.Id, added && entity is not ProductImage and not ProductFeature);
        }
        return changes.Values.ToList();
    }
}
