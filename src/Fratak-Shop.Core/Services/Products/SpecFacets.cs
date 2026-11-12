using System.Security.Cryptography;
using System.Text;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Products;

/// <summary>یک مشخصهٔ کالا برای برنامه‌ریز فیلتر. مثال: (کالا، «حافظه داخلی»، «128 گیگابایت»).</summary>
public sealed record SpecFeatureRow(Guid ProductId, string Key, string Value);

/// <summary>پیشنهاد یک فیلتر مشخصه: کلید، تعداد کالای دارای آن و تعداد مقدار متفاوت.</summary>
public sealed record SpecFacetPlan(string Key, int Coverage, int DistinctValues);

/// <summary>نتیجهٔ یک دور ساخت فیلترهای خودکار.</summary>
public sealed record SpecFacetReport(int Created, IReadOnlyList<string> Notes)
{
    public static readonly SpecFacetReport Empty = new(0, []);
}

/// <summary>
/// برنامه‌ریز «فیلتر مشخصات خودکار» (دور ۲۳ – مورد ۱۷). قانون خالص بدون پایگاه داده تا آزمون واحد بگیرد.
/// <para>
/// یک مشخصه وقتی فیلتر صفحهٔ دسته می‌شود که واقعاً به خریدار کمک کند:
/// <list type="bullet">
/// <item>دسته دست‌کم <see cref="MinProducts"/> کالای فعال دارد (با ۳ کالا فیلتر بی‌معنی است).</item>
/// <item>دست‌کم نیمی از کالاهای دسته این مشخصه را دارند.</item>
/// <item>۲ تا ۱۵ مقدار متفاوت و کوتاه دارد («۶۴ گیگ / ۱۲۸ گیگ» بله؛ متن بلند یا ۵۰ مقدار پراکنده نه).</item>
/// <item>یک مقدار بیش از ۹۰٪ کالاها را نگرفته (فیلتری که همه را برمی‌گرداند بی‌فایده است).</item>
/// <item>کلید شناسه/مدل/ابعاد/وزن/توضیح نیست و از قبل (دستی یا خودکار، فعال یا غیرفعال) فیلتر نشده است.</item>
/// </list>
/// </para>
/// مثال: ۱۰ گوشی که ۸ تا «حافظه داخلی» با مقدارهای ۶۴/۱۲۸/۲۵۶ دارند ← فیلتر «حافظه داخلی».
/// </summary>
public static class SpecFacetPlanner
{
    /// <summary>کمترین تعداد کالای فعال دسته برای ساخت فیلتر.</summary>
    public const int MinProducts = 6;

    /// <summary>بیشترین تعداد مقدار متفاوت.</summary>
    public const int MaxDistinct = 15;

    /// <summary>بیشترین طول یک مقدار.</summary>
    public const int MaxValueLength = 40;

    /// <summary>پیشوند نامک فیلترهای خودکار (برای تشخیص در پنل). مثال: «spec-1a2b3c4d9f0e».</summary>
    public const string SlugPrefix = "spec-";

    // کلیدهایی که فیلتر خوبی نمی‌شوند (شناسه، متن آزاد، عدد پیوسته).
    private static readonly string[] StopWords =
    [
        "مدل", "کد", "شناسه", "سریال", "توضیح", "ابعاد", "وزن", "سایر", "محتویات", "اقلام", "لینک", "آدرس", "تاریخ",
        "گارانتی", "بارکد", "قیمت", "model", "sku", "code", "weight", "dimension", "size (mm)",
    ];

    /// <summary>
    /// پیشنهاد فیلترهای تازهٔ یک دسته. <paramref name="existingKeys"/> = کلیدهایی که از قبل فیلترند (همه‌جا یا همین دسته و والدهایش).
    /// خروجی مرتب: پوشش بیشتر اول؛ حداکثر <paramref name="max"/> مورد.
    /// </summary>
    public static List<SpecFacetPlan> Plan(int productCount, IEnumerable<SpecFeatureRow> rows, IReadOnlySet<string> existingKeys, int max = 4)
    {
        var result = new List<SpecFacetPlan>();
        if (productCount < MinProducts || max <= 0)
            return result;

        foreach (var group in rows.Where(r => !string.IsNullOrWhiteSpace(r.Key) && !string.IsNullOrWhiteSpace(r.Value))
                     .GroupBy(r => r.Key.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var key = group.Key;
            if (key.Length is < 2 or > 60 || existingKeys.Contains(key) || IsStopKey(key))
                continue;
            // هر کالا یک بار (اولین مقدار).
            var perProduct = group.GroupBy(r => r.ProductId).Select(g => g.First().Value.Trim()).ToList();
            var coverage = perProduct.Count;
            if (coverage * 2 < productCount)
                continue;
            if (perProduct.Any(v => v.Length > MaxValueLength))
                continue;
            var counts = perProduct.GroupBy(v => v, StringComparer.Ordinal).Select(g => g.Count()).ToList();
            if (counts.Count is < 2 or > MaxDistinct)
                continue;
            if (counts.Max() * 10 > coverage * 9)
                continue;
            result.Add(new SpecFacetPlan(key, coverage, counts.Count));
        }

        return result.OrderByDescending(p => p.Coverage).ThenBy(p => p.DistinctValues).ThenBy(p => p.Key, StringComparer.Ordinal)
            .Take(max).ToList();
    }

    /// <summary>کلید نامناسب برای فیلتر؟ مثال: «مدل» ← true؛ «ابعاد بسته» ← true؛ «حافظه داخلی» ← false.</summary>
    public static bool IsStopKey(string key)
    {
        var k = key.Trim().ToLowerInvariant();
        return StopWords.Any(w => k.Contains(w, StringComparison.Ordinal));
    }

    /// <summary>
    /// نامک لاتین پایدار و یکتا برای فیلتر یک کلید در یک دسته (حداکثر ۵۰ نویسه). مثال: «spec-1a2b3c4d9f0e».
    /// همان کلید و دسته همیشه همان نامک را می‌گیرد (آدرس فیلتر عوض نمی‌شود).
    /// </summary>
    public static string Slug(string key, Guid categoryId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim().ToLowerInvariant() + "|" + categoryId.ToString("N")));
        return SlugPrefix + Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
    }
}

/// <summary>سرویس ساخت فیلترهای مشخصهٔ خودکار (از کارگر هوشمند، حداکثر هر ۶ ساعت).</summary>
public interface ISpecFacetService
{
    /// <summary>
    /// برای هر دستهٔ فعال، فیلترهای مفید تازه را می‌سازد (فیلتر موجود را هرگز تغییر یا حذف نمی‌کند؛ فیلتری که مدیر غیرفعال کرده دوباره ساخته نمی‌شود).
    /// </summary>
    Task<SpecFacetReport> SyncAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// پیاده‌سازی <see cref="ISpecFacetService"/>. فقط ستون‌های لازم خوانده می‌شود و سقف ردیف دارد تا روی هاست ضعیف سنگین نشود.
/// محل استفاده: <c>SmartWorker.CategorizeHourlyAsync</c>.
/// </summary>
public sealed class SpecFacetService(FratakShopDbContext context) : ISpecFacetService
{
    /// <summary>بیشترین فیلتر تازه در یک دور (کل فروشگاه).</summary>
    public const int MaxPerRun = 60;

    /// <summary>بیشترین ردیف مشخصه که در یک دور خوانده می‌شود.</summary>
    public const int MaxRows = 200_000;

    public async Task<SpecFacetReport> SyncAsync(CancellationToken cancellationToken = default)
    {
        var categories = await context.Categories.Where(c => c.IsActive && !c.IsSystem)
            .Select(c => new { c.Id, c.ParentId, c.Name }).ToListAsync(cancellationToken);
        if (categories.Count == 0)
            return SpecFacetReport.Empty;

        var products = await context.Products.Where(p => p.IsActive)
            .Select(p => new { p.Id, p.CategoryId }).ToListAsync(cancellationToken);
        if (products.Count < SpecFacetPlanner.MinProducts)
            return SpecFacetReport.Empty;

        var features = await context.ProductFeatures
            .Where(f => f.Product.IsActive && f.ValueType != FeatureValueType.Object && f.Value.Length <= SpecFacetPlanner.MaxValueLength)
            .Select(f => new SpecFeatureRow(f.ProductId, f.Key, f.Value))
            .Take(MaxRows)
            .ToListAsync(cancellationToken);
        if (features.Count == 0)
            return SpecFacetReport.Empty;
        var featuresByProduct = features.GroupBy(f => f.ProductId).ToDictionary(g => g.Key, g => g.ToList());

        var existing = await context.ProductFilterAttributes.IgnoreQueryFilters()
            .Select(f => new { f.FeatureKey, f.CategoryId, f.Slug }).ToListAsync(cancellationToken);
        var slugs = existing.Select(e => e.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var parentOf = categories.ToDictionary(c => c.Id, c => c.ParentId);
        var productsByCategory = products.GroupBy(p => p.CategoryId).ToDictionary(g => g.Key, g => g.Select(p => p.Id).ToList());

        var notes = new List<string>();
        var created = 0;
        foreach (var category in categories)
        {
            if (created >= MaxPerRun)
                break;
            // کالاهای خود دسته و زیردسته‌های مستقیمش (همان چیزی که صفحهٔ دسته نشان می‌دهد).
            var ids = new List<Guid>(productsByCategory.GetValueOrDefault(category.Id) ?? []);
            foreach (var child in categories.Where(c => c.ParentId == category.Id))
                ids.AddRange(productsByCategory.GetValueOrDefault(child.Id) ?? []);
            if (ids.Count < SpecFacetPlanner.MinProducts)
                continue;

            var scope = new HashSet<Guid>();
            Guid? current = category.Id;
            for (var depth = 0; current is { } id && depth < 8 && scope.Add(id); depth++)
                current = parentOf.GetValueOrDefault(id);
            var existingKeys = existing.Where(e => e.CategoryId is null || scope.Contains(e.CategoryId.Value))
                .Select(e => e.FeatureKey.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var rows = ids.SelectMany(id => featuresByProduct.GetValueOrDefault(id) ?? []);
            var plans = SpecFacetPlanner.Plan(ids.Count, rows, existingKeys, Math.Min(4, MaxPerRun - created));
            var order = 100;
            foreach (var plan in plans)
            {
                var slug = SpecFacetPlanner.Slug(plan.Key, category.Id);
                if (!slugs.Add(slug))
                    continue;
                // کلید دقیقاً همان شکلی که بیشتر کالاها دارند (فیلتر با برابری دقیق کلید کار می‌کند).
                var featureKey = ids.SelectMany(id => featuresByProduct.GetValueOrDefault(id) ?? [])
                    .Where(f => string.Equals(f.Key.Trim(), plan.Key, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(f => f.Key).OrderByDescending(g => g.Count()).First().Key;
                context.ProductFilterAttributes.Add(new ProductFilterAttribute
                {
                    Title = plan.Key.Length > 150 ? plan.Key[..150] : plan.Key,
                    FeatureKey = featureKey,
                    Slug = slug,
                    CategoryId = category.Id,
                    ShowWithoutCategory = false,
                    MaxValues = 20,
                    DisplayOrder = order++,
                    IsActive = true,
                });
                existing.Add(new { FeatureKey = featureKey, CategoryId = (Guid?)category.Id, Slug = slug });
                notes.Add($"فیلتر «{plan.Key}» برای «{category.Name}»");
                created++;
            }
        }

        if (created > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
            DataChangeNotifier.Publish(typeof(ProductFilterAttribute));
        }
        context.ChangeTracker.Clear();
        return new SpecFacetReport(created, notes);
    }
}
