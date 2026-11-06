using Fratak_Shop.Core.Services.Searches;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>یک دستهٔ تازه که دسته‌بندی هوشمند ساخت (برای صف «تکمیل دسته» و اعلان کارکنان).</summary>
public sealed record CreatedCategory(Guid Id, string Name, string Slug, int Products, Guid? ParentId);

/// <summary>
/// گزارش یک دور دسته‌بندی هوشمند. مثال: Examined=40، Moved=31، Created=[«پاوربانک» ۶ کالا]، Waiting=9 (هنوز مشابه کافی ندارند).
/// </summary>
public sealed record CategorizeReport(int Examined, int Moved, IReadOnlyList<CreatedCategory> Created, int Waiting)
{
    public static readonly CategorizeReport Empty = new(0, 0, [], 0);

    /// <summary>متن کوتاه برای اعلان/پیام. مثال: «۳۱ کالا دسته گرفت؛ ۱ دستهٔ تازه: پاوربانک».</summary>
    public string Summary => Moved == 0 && Created.Count == 0
        ? (Waiting > 0 ? $"{PersianText.Digits(Waiting)} کالا هنوز مشابه کافی برای ساخت دسته ندارد." : "کالای بی‌دسته‌ای نبود.")
        : $"{PersianText.Digits(Moved)} کالا دسته گرفت{(Created.Count > 0 ? $"؛ {PersianText.Digits(Created.Count)} دستهٔ تازه: {string.Join("، ", Created.Select(c => c.Name))}" : "")}.";
}

/// <summary>
/// دسته‌بندی هوشمند و خودکار کالاها (دور ۲۱). درون‌ریزی از تأمین‌کننده/سایت دیگر نیازی به انتخاب دسته ندارد.
/// <list type="number">
/// <item>کالای بی‌دسته به دستهٔ سیستمی و پنهان «دسته‌بندی نشده» می‌رود (CategoryId در دیتابیس اجباری است).</item>
/// <item>هر کالای بی‌دسته یا کالای دسته‌های «سایر/متفرقه»: اول «نوع کالا» (گوشی، پاوربانک …) با دستهٔ هم‌نام موجود،
/// بعد دسته‌بند آماری <see cref="CategoryClassifier"/> با آستانهٔ مطمئن.</item>
/// <item>نوع مهم (گوشی، لپ تاپ …) یا نوعی که مشتری‌ها زیاد جست‌وجو می‌کنند: همان لحظه دستهٔ خودش ساخته می‌شود
/// (زیرمجموعهٔ دستهٔ والد مناسب، مثلاً «موبایل»، اگر وجود دارد).</item>
/// <item>بقیه: وقتی دست‌کم ۳ کالای مشابه جمع شد، دستهٔ تازه با نام فارسی و نامک انگلیسی ساخته می‌شود.
/// توضیح، پرسش‌های رایج و تصویر دسته را کار «تکمیل دسته» در صف هوشمند می‌سازد (فراخوان این سرویس صف می‌کند).</item>
/// </list>
/// <para>ایمنی: فقط کالاهای «دسته‌بندی نشده» و «سایر» جابه‌جا می‌شوند؛ کالایی که ادمین در دستهٔ واقعی گذاشته هرگز دست نمی‌خورد.
/// حدس مردد = بدون تغییر. دستهٔ تازه تا کالای فعال نداشته باشد در سایت نمایش داده نمی‌شود (منوی خالی ساخته نمی‌شود).
/// همه‌چیز چند پرس‌وجوی سبک است (بدون شبکه) و حداکثر <c>max</c> کالا در هر دور.</para>
/// <para>محل استفاده: درون‌ریزی سایت (همان لحظه برای کالاهای تازه)، برنامه‌ریز SmartWorker (هر ۶ ساعت) و دکمهٔ صفحهٔ دسته‌های هوشمند.</para>
/// </summary>
public interface ISmartCategoryService
{
    /// <summary>شناسهٔ دستهٔ سیستمی «دسته‌بندی نشده» (اگر نیست ساخته می‌شود؛ غیرفعال و پنهان از سایت).</summary>
    Task<Guid> EnsureUncategorizedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// یک دور دسته‌بندی. <paramref name="productIds"/> = فقط این کالاها (مثلاً تازه درون‌ریزی‌شده‌ها)؛ null = همهٔ بی‌دسته‌ها تا سقف.
    /// </summary>
    Task<CategorizeReport> RunAsync(IReadOnlyCollection<Guid>? productIds = null, int max = 300, CancellationToken cancellationToken = default);
}

public class SmartCategoryService(FratakShopDbContext context, ISearchInsightsService insights) : ISmartCategoryService
{
    /// <summary>نامک ثابت دستهٔ سیستمی.</summary>
    public const string UncategorizedSlug = "uncategorized";

    /// <summary>نام دستهٔ سیستمی.</summary>
    public const string UncategorizedName = "دسته‌بندی نشده";

    public async Task<Guid> EnsureUncategorizedAsync(CancellationToken cancellationToken = default)
    {
        var id = await context.Categories.IgnoreQueryFilters()
            .Where(c => c.IsSystem && c.Slug == UncategorizedSlug).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(cancellationToken);
        if (id is { } existing)
            return existing;
        // شاید نامک قبلاً دستی ساخته شده باشد: همان سیستمی می‌شود (تکراری ساخته نمی‌شود).
        var bySlug = await context.Categories.AsTracking().IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Slug == UncategorizedSlug, cancellationToken);
        if (bySlug is not null)
        {
            bySlug.IsSystem = true;
            bySlug.IsDeleted = false;
            await context.SaveChangesAsync(cancellationToken);
            return bySlug.Id;
        }
        var category = new Category
        {
            Name = UncategorizedName,
            Slug = UncategorizedSlug,
            IsActive = false,
            IsSystem = true,
            DisplayOrder = 9999,
            Description = null,
        };
        context.Categories.Add(category);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return category.Id;
        }
        catch (DbUpdateException)
        {
            // درخواست هم‌زمان دیگری همین الان ساخت (نامک یکتا) ← همان را برمی‌گردانیم.
            context.Entry(category).State = EntityState.Detached;
            return await context.Categories.IgnoreQueryFilters().Where(c => c.Slug == UncategorizedSlug).Select(c => c.Id).FirstAsync(cancellationToken);
        }
    }

    public async Task<CategorizeReport> RunAsync(IReadOnlyCollection<Guid>? productIds = null, int max = 300, CancellationToken cancellationToken = default)
    {
        var uncategorized = await EnsureUncategorizedAsync(cancellationToken);
        await ActivateFilledAsync(cancellationToken);

        var categories = await context.Categories
            .Select(c => new CategoryRow(c.Id, c.Name, c.Slug, c.ParentId, c.IsActive, c.IsSystem))
            .ToListAsync(cancellationToken);
        var catchAll = categories.Where(c => c.Id == uncategorized || c.IsSystem || CategoryClassifier.IsCatchAll(c.Name, c.Slug))
            .Select(c => c.Id).ToHashSet();
        var targets = categories.Where(c => !catchAll.Contains(c.Id)).ToList();

        var query = context.Products.Where(p => catchAll.Contains(p.CategoryId));
        if (productIds is not null)
            query = query.Where(p => productIds.Contains(p.Id));
        var candidates = await query.OrderByDescending(p => p.CreatedAt).Take(Math.Clamp(max, 1, 2000))
            .Select(p => new { p.Id, p.Name, p.CategoryId, p.IsActive }).ToListAsync(cancellationToken);
        if (candidates.Count == 0)
            return CategorizeReport.Empty;

        // آموزش: حداکثر ۲۰۰ نام پرفروش از هر دستهٔ واقعی (کل حداکثر ۸۰۰۰ ردیف؛ برای هاست ضعیف سبک).
        var targetIds = targets.Select(t => t.Id).ToList();
        var names = await context.Products.Where(p => targetIds.Contains(p.CategoryId))
            .OrderByDescending(p => p.SalesCount).ThenByDescending(p => p.CreatedAt)
            .Take(8000).Select(p => new { p.CategoryId, p.Name }).ToListAsync(cancellationToken);
        var byCategory = names.GroupBy(n => n.CategoryId).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Name).Take(200).ToList());
        var classifier = CategoryClassifier.Train(targets.Select(t => new CategorySample(t.Id, t.Name, byCategory.GetValueOrDefault(t.Id) ?? [])));
        var demanded = await DemandedTypesAsync();

        var moves = new Dictionary<Guid, Guid>();
        var created = new Dictionary<string, (Category Entity, int Count)>(StringComparer.Ordinal);
        var leftovers = new List<(Guid Id, string Name)>();
        foreach (var product in candidates)
        {
            var type = ProductTypes.Detect(product.Name);
            var existing = type is null ? null : targets.FirstOrDefault(t => ProductTypes.NameMatches(type, t.Name, t.Slug));
            if (existing is not null)
            {
                moves[product.Id] = existing.Id;
                continue;
            }
            // نوع مهم یا پرجست‌وجو: دستهٔ اختصاصی خودش (زیرمجموعهٔ والد مناسب) – در دستهٔ کلی‌تری مثل «لوازم جانبی» گم نمی‌شود.
            if (type is not null && (type.Important || demanded.Contains(type.Key)))
            {
                var entity = await NewCategoryAsync(created, type.Name, type.Key, ParentFor(type, product.CategoryId, categories, catchAll), product.IsActive, cancellationToken);
                moves[product.Id] = entity.Id;
                continue;
            }
            var guess = classifier.Classify(product.Name);
            if (CategoryClassifier.IsConfident(guess))
            {
                moves[product.Id] = guess!.CategoryId;
                continue;
            }
            leftovers.Add((product.Id, product.Name));
        }

        // خوشه‌بندی باقی‌مانده‌ها: فقط این دور (کالاهایی که قبلاً هم بی‌دسته مانده‌اند همین‌جا دوباره بررسی می‌شوند).
        var waiting = leftovers.Count;
        if (leftovers.Count >= CategoryClassifier.MinClusterSize)
        {
            var brands = await context.Brands.Select(b => b.Name).Take(2000).ToListAsync(cancellationToken);
            foreach (var cluster in CategoryClassifier.Cluster(leftovers, brands))
            {
                var firstSource = candidates.First(c => c.Id == cluster.ProductIds[0]).CategoryId;
                var anyActive = candidates.Any(c => cluster.ProductIds.Contains(c.Id) && c.IsActive);
                var parent = cluster.Type is null ? ParentFromSource(firstSource, categories, catchAll) : ParentFor(cluster.Type, firstSource, categories, catchAll);
                var entity = await NewCategoryAsync(created, cluster.Name, cluster.Type?.Key, parent, anyActive, cancellationToken);
                foreach (var id in cluster.ProductIds)
                    moves[id] = entity.Id;
                waiting -= cluster.ProductIds.Count;
            }
        }

        if (created.Count > 0)
            await context.SaveChangesAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var moved = 0;
        foreach (var group in moves.GroupBy(m => m.Value))
        {
            var ids = group.Select(g => g.Key).ToList();
            moved += await context.Products.Where(p => ids.Contains(p.Id) && catchAll.Contains(p.CategoryId))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.CategoryId, group.Key).SetProperty(p => p.UpdatedAt, now), cancellationToken);
        }
        if (moved > 0)
            DataChangeNotifier.Publish(typeof(Product), typeof(Category));

        var result = created.Values.Select(c => new CreatedCategory(c.Entity.Id, c.Entity.Name, c.Entity.Slug,
            moves.Count(m => m.Value == c.Entity.Id), c.Entity.ParentId)).ToList();
        return new CategorizeReport(candidates.Count, moved, result, Math.Max(0, waiting));
    }

    private sealed record CategoryRow(Guid Id, string Name, string Slug, Guid? ParentId, bool IsActive, bool IsSystem);

    /// <summary>
    /// دستهٔ ساخته‌شدهٔ خودکار که حالا کالای فعال دارد ← فعال (نمایش در سایت). مثال: پاوربانک‌های درون‌ریزی‌شده بعد از
    /// بازبینی ادمین فعال شدند ← دستهٔ «پاوربانک» هم در منو می‌آید.
    /// </summary>
    private async Task ActivateFilledAsync(CancellationToken cancellationToken)
    {
        var count = await context.Categories
            .Where(c => c.IsAutoCreated && !c.IsSystem && !c.IsActive && c.Products.Any(p => p.IsActive))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, true).SetProperty(c => c.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
        if (count > 0)
            DataChangeNotifier.Publish(typeof(Category));
    }

    /// <summary>
    /// نوع‌هایی که مشتری‌ها زیاد جست‌وجو کرده‌اند (از جست‌وجوهای پرتکرار فروشگاه) ← «نیاز کاربر»؛ مثل نوع مهم فوراً دسته می‌گیرند.
    /// مثال: «پاوربانک» در جست‌وجوهای پرتکرار ← اولین پاوربانک درون‌ریزی‌شده دستهٔ «پاوربانک» را می‌سازد.
    /// </summary>
    private async Task<HashSet<string>> DemandedTypesAsync()
    {
        try
        {
            return (await insights.GetPopularAsync()).Select(ProductTypes.Detect).Where(t => t is not null).Select(t => t!.Key).ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return [];
        }
    }

    /// <summary>
    /// والد دستهٔ تازهٔ یک نوع: اول دستهٔ والد مبدأ اگر کالا از «سایر X» آمده، بعد اولین دستهٔ سطح‌بالای فعال که نامش یکی از
    /// <see cref="ProductType.ParentHints"/> را دارد. مثال: گوشی + دستهٔ «موبایل و تبلت» موجود ← زیرمجموعهٔ «موبایل و تبلت».
    /// </summary>
    private static Guid? ParentFor(ProductType type, Guid sourceCategory, List<CategoryRow> categories, HashSet<Guid> catchAll)
    {
        if (ParentFromSource(sourceCategory, categories, catchAll) is { } fromSource)
            return fromSource;
        foreach (var hint in type.ParentHints)
        {
            var normalizedHint = PersianText.Normalize(hint);
            var parent = categories.FirstOrDefault(c => c.ParentId == null && c.IsActive && !catchAll.Contains(c.Id)
                                                        && PersianText.Normalize(c.Name).Contains(normalizedHint, StringComparison.Ordinal));
            if (parent is not null)
                return parent.Id;
        }
        return null;
    }

    /// <summary>کالایی که از «سایر لوازم جانبی» (زیر «لوازم جانبی») آمده ← دستهٔ تازه هم زیر «لوازم جانبی». از «دسته‌بندی نشده» ← سطح بالا.</summary>
    private static Guid? ParentFromSource(Guid sourceCategory, List<CategoryRow> categories, HashSet<Guid> catchAll)
    {
        var source = categories.FirstOrDefault(c => c.Id == sourceCategory);
        return source is { IsSystem: false, ParentId: { } parentId } && !catchAll.Contains(parentId) ? parentId : null;
    }

    /// <summary>
    /// ساخت (یا استفادهٔ دوباره در همین دور) دستهٔ تازه با نامک انگلیسی یکتا. فعال فقط اگر کالای فعال دارد.
    /// مثال: («پاوربانک»، «power-bank») ← نامک power-bank (یا power-bank-2 اگر گرفته شده).
    /// </summary>
    private async Task<Category> NewCategoryAsync(Dictionary<string, (Category Entity, int Count)> created, string name, string? key, Guid? parentId,
        bool active, CancellationToken cancellationToken)
    {
        var display = Display(name);
        if (created.TryGetValue(display, out var existing))
        {
            if (active && !existing.Entity.IsActive)
                existing.Entity.IsActive = true;
            created[display] = (existing.Entity, existing.Count + 1);
            return existing.Entity;
        }
        var baseSlug = key ?? EnglishSlug.From(display, 60);
        if (string.IsNullOrWhiteSpace(baseSlug) || !EnglishSlug.IsEnglish(baseSlug))
            baseSlug = "category";
        var taken = await context.Categories.IgnoreQueryFilters().Where(c => c.Slug.StartsWith(baseSlug)).Select(c => c.Slug).ToListAsync(cancellationToken);
        var takenSet = taken.Concat(created.Values.Select(c => c.Entity.Slug)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entity = new Category
        {
            Name = display,
            Slug = EnglishSlug.Unique(baseSlug, takenSet.Contains, 200),
            ParentId = parentId,
            IsActive = active,
            IsAutoCreated = true,
            DisplayOrder = 500,
        };
        context.Categories.Add(entity);
        created[display] = (entity, 1);
        return entity;
    }

    /// <summary>نام نمایشی تمیز. مثال: «لیوان سرامیکی» همان؛ «power bank» ← «Power Bank».</summary>
    public static string Display(string name)
    {
        var clean = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0)
            return "دستهٔ تازه";
        return string.Join(' ', clean.Split(' ').Select(w => w.All(c => c is >= 'a' and <= 'z') ? char.ToUpperInvariant(w[0]) + w[1..] : w));
    }
}
