using Fratak_Shop.Core.Services.Learning;
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
/// <param name="Unplaced">دور ۲۲: شناسهٔ کالاهایی که هنوز «دسته‌بندی نشده» مانده‌اند (برای اعلان به ادمین؛ این کالاها منتشر نمی‌شوند).</param>
/// <param name="General">دور ۲۲: چند کالا موقتاً در دستهٔ کلی‌تر رفتند (کالای مشابه کافی برای دستهٔ اختصاصی نبود).</param>
public sealed record CategorizeReport(int Examined, int Moved, IReadOnlyList<CreatedCategory> Created, int Waiting,
    IReadOnlyList<Guid>? Unplaced = null, int General = 0)
{
    public static readonly CategorizeReport Empty = new(0, 0, [], 0);

    /// <summary>متن کوتاه برای اعلان/پیام. مثال: «۳۱ کالا دسته گرفت (۲ کالا در دستهٔ کلی‌تر)؛ ۱ دستهٔ تازه: پاوربانک».</summary>
    public string Summary => Moved == 0 && Created.Count == 0
        ? (Waiting > 0 ? $"{PersianText.Digits(Waiting)} کالا هنوز مشابه کافی برای ساخت دسته ندارد." : "کالای بی‌دسته‌ای نبود.")
        : $"{PersianText.Digits(Moved)} کالا دسته گرفت{(General > 0 ? $" ({PersianText.Digits(General)} کالا در دستهٔ کلی‌تر)" : "")}{(Created.Count > 0 ? $"؛ {PersianText.Digits(Created.Count)} دستهٔ تازه: {string.Join("، ", Created.Select(c => c.Name))}" : "")}.";
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

    /// <summary>
    /// دور ۲۹ (#۱۰): بازبینی کالاهایی که در دستهٔ «واقعی» اشتباه گذاشته شده‌اند (<see cref="CategoryAudit.IsMisplaced"/>) و انتقال به دستهٔ درست
    /// (رأی فروشگاه‌های دیگر ← نوع کالا ← دسته‌بند آماری مطمئن/مسیر دستهٔ سایت تأمین‌کننده). حداکثر <paramref name="maxMoves"/> جابه‌جایی در هر بار.
    /// <para><paramref name="research"/>: اگر دستهٔ مطمئنی پیدا نشد، دستهٔ همین کالا در سایت‌های دیگر پرسیده می‌شود (حداکثر ۶ کالا در هر بار؛ null = بدون شبکه).</para>
    /// </summary>
    Task<CategoryAuditReport> AuditAsync(int maxMoves = 25, CancellationToken cancellationToken = default,
        Func<IntakeProduct, CancellationToken, Task<MarketLookup?>>? research = null);
}

public class SmartCategoryService(FratakShopDbContext context) : ISmartCategoryService
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

        // نامزدها: کالاهای «دسته‌بندی نشده/سایر» + (دور ۲۲) کالاهایی که سیستم موقتاً در دستهٔ کلی گذاشته بود (شاید حالا دستهٔ دقیق داشته باشند).
        var query = context.Products.Where(p => catchAll.Contains(p.CategoryId) || p.CategorySource == ProductCategorySource.General);
        if (productIds is not null)
            query = query.Where(p => productIds.Contains(p.Id));
        var candidates = await query.OrderByDescending(p => p.CreatedAt).Take(Math.Clamp(max, 1, 2000))
            .Select(p => new Candidate(p.Id, p.Name, p.CategoryId, p.IsActive, p.SourceCategory, p.CategorySource, p.MarketCategory))
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
            return CategorizeReport.Empty;

        // آموزش: حداکثر ۲۰۰ نام پرفروش از هر دستهٔ واقعی (کل حداکثر ۸۰۰۰ ردیف؛ برای هاست ضعیف سبک).
        var targetIds = targets.Select(t => t.Id).ToList();
        var names = await context.Products.Where(p => targetIds.Contains(p.CategoryId))
            .OrderByDescending(p => p.SalesCount).ThenByDescending(p => p.CreatedAt)
            .Take(8000).Select(p => new { p.CategoryId, p.Name }).ToListAsync(cancellationToken);
        var byCategory = names.GroupBy(n => n.CategoryId).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Name).Take(200).ToList());
        var classifier = CategoryClassifier.Train(targets.Select(t => new CategorySample(t.Id, t.Name, byCategory.GetValueOrDefault(t.Id) ?? [])));
        // دور ۲۴ (پیشنهاد ۵۳): مدل دوم (بیز ساده روی واژه و حروف) برای «نظر دوم»؛ با همان داده، بدون سرویس بیرونی.
        var secondOpinion = CategoryConsensus.TrainSecondOpinion(names.Select(n => (n.Name, n.CategoryId))
            .Concat(targets.Select(t => (t.Name, t.Id))));
        var brands = await context.Brands.Select(b => b.Name).Take(2000).ToListAsync(cancellationToken);

        var moves = new Dictionary<Guid, Move>();
        var created = new Dictionary<string, (Category Entity, int Count)>(StringComparer.Ordinal);
        var leftovers = new List<Candidate>();
        foreach (var product in candidates.Where(c => catchAll.Contains(c.CategoryId)))
        {
            // دور ۲۶ – «رأی رقبا» مقدم بر همه: دسته‌ای که اکثریت سایت‌های دیگر (≥ ۲) برای همین کالا گذاشته‌اند.
            // دستهٔ هم‌نام موجود ← همان؛ نبود ← ساخته می‌شود (زیر سطح کلی مسیر سایت دیگر، اگر هست).
            if (CategoryNames.Proper(product.MarketCategory, brands) is { } market)
            {
                if (targets.FirstOrDefault(t => CategoryNames.Same(market, t.Name, t.Slug)) is { } same)
                {
                    moves[product.Id] = new Move(same.Id, ProductCategorySource.Smart);
                    continue;
                }
                Guid? marketParent = null;
                if (GeneralLevel(product.SourceCategory, brands) is { } level && !CategoryNames.Same(level.Name, market))
                {
                    var grand = level.Parent is null ? null : targets.FirstOrDefault(t => CategoryNames.Same(level.Parent, t.Name, t.Slug))?.Id;
                    marketParent = targets.FirstOrDefault(t => CategoryNames.Same(level.Name, t.Name, t.Slug))?.Id
                                   ?? (await NewCategoryAsync(created, level.Name, null, grand, product.IsActive, cancellationToken)).Id;
                }
                else if (ProductTypes.Detect(product.Name) is { } marketType)
                {
                    marketParent = ParentFor(marketType, product.CategoryId, categories, catchAll);
                }
                var made = await NewCategoryAsync(created, market, null, marketParent, product.IsActive, cancellationToken);
                moves[product.Id] = new Move(made.Id, ProductCategorySource.Smart);
                continue;
            }
            var type = ProductTypes.Detect(product.Name);
            if (type is not null && targets.FirstOrDefault(t => ProductTypes.NameMatches(type, t.Name, t.Slug)) is { } typed)
            {
                moves[product.Id] = new Move(typed.Id, ProductCategorySource.Smart);
                continue;
            }
            // دور ۲۳: هر نوع شناخته‌شده (حتی تک‌کالا) دستهٔ دقیق خودش را زیر خانوادهٔ درست می‌گیرد («جزئی‌تر شدن» دسته‌ها).
            // دستهٔ کم‌کالا (کمتر از ۳ کالای فعال) تا پر شدن از نقشهٔ سایت و ایندکس گوگل بیرون می‌ماند (CategoryVisibility) ← بی‌خطر برای سئو.
            if (type is not null)
            {
                var parentId = ParentFor(type, product.CategoryId, categories, catchAll)
                               ?? await FamilyParentAsync(type, categories, created, product.IsActive, cancellationToken);
                var entity = await NewCategoryAsync(created, type.Name, type.Key, parentId, product.IsActive, cancellationToken);
                moves[product.Id] = new Move(entity.Id, ProductCategorySource.Smart);
                continue;
            }
            if (Decide(product, type, targets, categories, catchAll, classifier, secondOpinion) is { } found)
            {
                moves[product.Id] = found;
                continue;
            }
            leftovers.Add(product);
        }

        // خوشه‌بندی باقی‌مانده‌ها (≥ ۳ کالای مشابه ← دستهٔ اختصاصی با نام درست؛ نام نصفه/تبلیغاتی ← خوشه رد می‌شود).
        if (leftovers.Count >= CategoryClassifier.MinClusterSize)
        {
            foreach (var cluster in CategoryClassifier.Cluster(leftovers.Select(l => (l.Id, l.Name)), brands))
            {
                var members = leftovers.Where(l => cluster.ProductIds.Contains(l.Id)).ToList();
                var name = cluster.Type?.Name ?? CategoryNames.Proper(SharedLeaf(members) ?? cluster.Name, brands);
                if (name is null)
                    continue;
                var firstSource = members[0].CategoryId;
                var parent = PathParent(members, targets)
                             ?? (cluster.Type is null ? ParentFromSource(firstSource, categories, catchAll) : ParentFor(cluster.Type, firstSource, categories, catchAll));
                var entity = await NewCategoryAsync(created, name, cluster.Type?.Key, parent, members.Any(m => m.IsActive), cancellationToken);
                foreach (var member in members)
                    moves[member.Id] = new Move(entity.Id, ProductCategorySource.Smart);
            }
            leftovers.RemoveAll(l => moves.ContainsKey(l.Id));
        }

        // کالای تک با مسیر دستهٔ سایت دیگر (دور ۲۳): برگ مسیر اگر نام درست و کاملی است ← دستهٔ دقیق زیر دستهٔ «کلی» همان مسیر
        // (مثلاً «ماساژور» زیر «لوازم شخصی برقی»)؛ برگ ناجور ← فقط دستهٔ کلی (بعداً با کالاهای بیشتر دقیق می‌شود).
        // دور ۳۱ (#۱): اگر مسیر سایت دیگر/تأمین‌کننده «کلی» است (مثلاً فقط «لوازم آشپزخانه»)، دستهٔ دقیق‌تر از نام خود کالا ساخته می‌شود
        // (CategoryRefiner: «کتری برقی پارس خزر مدل KT-2000» ← «کتری برقی» زیر «لوازم آشپزخانه»).
        foreach (var group in leftovers.Select(l =>
                     {
                         var general = GeneralLevel(l.SourceCategory, brands);
                         var leaf = LeafName(l.SourceCategory, brands);
                         var finer = (general is not null || leaf is not null) && CategoryRefiner.IsCoarse(CategoryNames.Path(l.SourceCategory))
                             ? CategoryRefiner.FinerName(l.Name, leaf ?? general?.Name, brands)
                             : null;
                         return (Product: l, General: general, Leaf: leaf, Finer: finer);
                     })
                     .Where(x => x.General is not null || x.Leaf is not null)
                     .GroupBy(x => (x.General?.Name ?? "") + "|" + (x.Leaf ?? "") + "|" + (x.Finer ?? ""), StringComparer.Ordinal))
        {
            var first = group.First();
            Guid? generalId = null;
            if (first.General is { } level)
            {
                var parent = level.Parent is null ? null : targets.FirstOrDefault(t => CategoryNames.Same(level.Parent, t.Name, t.Slug))?.Id;
                generalId = targets.FirstOrDefault(t => CategoryNames.Same(level.Name, t.Name, t.Slug))?.Id
                            ?? (await NewCategoryAsync(created, level.Name, null, parent, group.Any(x => x.Product.IsActive), cancellationToken)).Id;
            }
            var exactLeaf = first.Leaf is not null && (first.General is null || !CategoryNames.Same(first.Leaf, first.General.Value.Name));
            Guid targetId;
            if (exactLeaf)
            {
                targetId = targets.FirstOrDefault(t => CategoryNames.Same(first.Leaf, t.Name, t.Slug))?.Id
                           ?? (await NewCategoryAsync(created, first.Leaf!, null, generalId, group.Any(x => x.Product.IsActive), cancellationToken)).Id;
            }
            else if (generalId is { } onlyGeneral)
                targetId = onlyGeneral;
            else
                continue;
            // دستهٔ دقیق‌تر زیر همان دستهٔ کلی (دستهٔ هم‌نام موجود ← همان؛ نبود ← ساخته می‌شود).
            if (first.Finer is { } finer && !CategoryNames.Same(finer, first.Leaf) && !CategoryNames.Same(finer, first.General?.Name))
            {
                targetId = targets.FirstOrDefault(t => CategoryNames.Same(finer, t.Name, t.Slug))?.Id
                           ?? (await NewCategoryAsync(created, finer, null, targetId, group.Any(x => x.Product.IsActive), cancellationToken)).Id;
                exactLeaf = true;
            }
            foreach (var item in group)
                moves[item.Product.Id] = new Move(targetId, exactLeaf ? ProductCategorySource.Smart : ProductCategorySource.General);
        }

        // بازبینی کالاهای «دستهٔ کلی»: دستهٔ دقیق پیدا شد یا حالا ≥ ۳ کالای هم‌نوع جمع شده ← زیر‌دستهٔ اختصاصی.
        await SplitGeneralsAsync(candidates.Where(c => !catchAll.Contains(c.CategoryId) && c.Source == ProductCategorySource.General).ToList(),
            targets, moves, created, brands, cancellationToken);

        if (created.Count > 0)
            await context.SaveChangesAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var moved = 0;
        foreach (var group in moves.GroupBy(m => m.Value))
        {
            var ids = group.Select(g => g.Key).ToList();
            var (target, source) = group.Key;
            // فقط کالاهایی که هنوز در «سایر/بی‌دسته» یا «دستهٔ کلی سیستم» هستند (ادمین در همین فاصله دسته را عوض کرده باشد ← دست نمی‌خورد).
            moved += await context.Products
                .Where(p => ids.Contains(p.Id) && (catchAll.Contains(p.CategoryId) || p.CategorySource == ProductCategorySource.General))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.CategoryId, target).SetProperty(p => p.CategorySource, source).SetProperty(p => p.UpdatedAt, now), cancellationToken);
        }
        if (moved > 0)
            DataChangeNotifier.Publish(typeof(Product), typeof(Category));

        var result = created.Values.Select(c => new CreatedCategory(c.Entity.Id, c.Entity.Name, c.Entity.Slug,
            moves.Count(m => m.Value.Target == c.Entity.Id), c.Entity.ParentId)).ToList();
        var unplaced = candidates.Where(c => catchAll.Contains(c.CategoryId) && !moves.ContainsKey(c.Id)).Select(c => c.Id).ToList();
        var general = moves.Count(m => m.Value.Source == ProductCategorySource.General);
        return new CategorizeReport(candidates.Count, moved, result, unplaced.Count, unplaced, general);
    }

    public async Task<CategoryAuditReport> AuditAsync(int maxMoves = 25, CancellationToken cancellationToken = default,
        Func<IntakeProduct, CancellationToken, Task<MarketLookup?>>? research = null)
    {
        var uncategorized = await EnsureUncategorizedAsync(cancellationToken);
        var categories = await context.Categories
            .Select(c => new CategoryRow(c.Id, c.Name, c.Slug, c.ParentId, c.IsActive, c.IsSystem))
            .ToListAsync(cancellationToken);
        var catchAll = categories.Where(c => c.Id == uncategorized || c.IsSystem || CategoryClassifier.IsCatchAll(c.Name, c.Slug))
            .Select(c => c.Id).ToHashSet();
        var targets = categories.Where(c => !catchAll.Contains(c.Id)).ToList();
        var targetIds = targets.Select(t => t.Id).ToList();
        // همهٔ کالاهای دسته‌های واقعی (سقف ۸۰۰۰ برای هاست ضعیف؛ فقط چند ستون سبک).
        var products = await context.Products.Where(p => targetIds.Contains(p.CategoryId))
            .OrderByDescending(p => p.IsActive).ThenByDescending(p => p.SalesCount).ThenByDescending(p => p.CreatedAt)
            .Take(8000)
            .Select(p => new Candidate(p.Id, p.Name, p.CategoryId, p.IsActive, p.SourceCategory, p.CategorySource, p.MarketCategory))
            .ToListAsync(cancellationToken);
        if (products.Count == 0)
            return CategoryAuditReport.Empty;
        var byId = categories.ToDictionary(c => c.Id);
        var members = products.GroupBy(p => p.CategoryId).ToDictionary(g => g.Key, g => g.ToList());
        var brands = await context.Brands.Select(b => b.Name).Take(2000).ToListAsync(cancellationToken);
        var brandTerms = CategoryAudit.BrandTerms(brands);

        // ۱) کالاهای ناجور
        var misplaced = new List<Candidate>();
        foreach (var product in products)
        {
            var category = byId[product.CategoryId];
            var siblings = members[product.CategoryId].Where(m => m.Id != product.Id).Select(m => m.Name).Take(300).ToList();
            if (CategoryAudit.IsMisplaced(product.Name, category.Name, category.Slug, siblings, brandTerms))
                misplaced.Add(product);
        }
        if (misplaced.Count == 0)
            return new CategoryAuditReport(products.Count, 0, [], []);

        // ۲) آموزش دسته‌بند بدون خود کالاهای ناجور (تا دستهٔ اشتباه فعلی دوباره «درست» حدس زده نشود).
        var wrong = misplaced.Select(m => m.Id).ToHashSet();
        var training = products.Where(p => !wrong.Contains(p.Id)).ToList();
        var byCategory = training.GroupBy(n => n.CategoryId).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Name).Take(200).ToList());
        var classifier = CategoryClassifier.Train(targets.Select(t => new CategorySample(t.Id, t.Name, byCategory.GetValueOrDefault(t.Id) ?? [])));
        var secondOpinion = CategoryConsensus.TrainSecondOpinion(training.Select(n => (n.Name, n.CategoryId)).Concat(targets.Select(t => (t.Name, t.Id))));

        // ۳) دستهٔ درست: رأی فروشگاه‌های دیگر ← نوع کالا ← دسته‌بند آماری/مسیر سایت تأمین‌کننده. مقصد همان دستهٔ فعلی یا «سایر» ← بی‌تغییر.
        var fixes = new List<(Candidate Product, Guid Target)>();
        var suspects = new List<string>();
        var lookups = 0;
        foreach (var candidate in misplaced)
        {
            var product = candidate;
            // «دستهٔ نامعلوم ← دستهٔ سایت‌های دیگر»: کالایی که هنوز رأی رقبا ندارد یک بار پرسیده می‌شود (مؤدب و کش‌شده).
            if (research is not null && product.MarketCategory is null && lookups < 6)
            {
                lookups++;
                try
                {
                    var lookup = await research(new IntakeProduct(product.Id, product.Name, null, null, product.SourceCategory), cancellationToken);
                    var vote = CategoryNames.Truncate(CategoryNames.Proper(lookup?.MarketCategory), 150);
                    var route = CategoryNames.Path(lookup?.Path);
                    var joined = route.Count >= 2 ? CategoryNames.Truncate(CategoryNames.Join(route), 300) : null;
                    if (vote is not null || joined is not null)
                    {
                        await context.Products.Where(p => p.Id == product.Id)
                            .ExecuteUpdateAsync(s => s.SetProperty(p => p.MarketCategory, p => vote ?? p.MarketCategory)
                                .SetProperty(p => p.SourceCategory, p => joined ?? p.SourceCategory), cancellationToken);
                        product = product with { MarketCategory = vote ?? product.MarketCategory, SourceCategory = joined ?? product.SourceCategory };
                    }
                }
                catch (Exception lookupError) when (lookupError is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
                {
                    // شبکه/منبع بیرونی در دسترس نبود؛ با دادهٔ داخلی ادامه می‌دهیم.
                    _ = lookupError;
                }
            }
            Guid? target = null;
            if (CategoryNames.Proper(product.MarketCategory, brands) is { } market
                && targets.FirstOrDefault(t => CategoryNames.Same(market, t.Name, t.Slug)) is { } same)
                target = same.Id;
            var type = ProductTypes.Detect(product.Name);
            if (target is null && type is not null && targets.FirstOrDefault(t => ProductTypes.NameMatches(type, t.Name, t.Slug)) is { } typed)
                target = typed.Id;
            if (target is null && Decide(product, type, targets, categories, catchAll, classifier, secondOpinion) is { } found)
                target = found.Target;
            if (target is { } chosen && chosen != product.CategoryId && !catchAll.Contains(chosen)
                && !CategoryAudit.IsMisplaced(product.Name, byId[chosen].Name, byId[chosen].Slug,
                    (members.GetValueOrDefault(chosen) ?? []).Where(m => !wrong.Contains(m.Id)).Select(m => m.Name).Take(300).ToList(), brandTerms))
                fixes.Add((product, chosen));
            else
                suspects.Add(product.Name);
        }

        var now = DateTimeOffset.UtcNow;
        var moved = new List<CategoryFix>();
        foreach (var (product, target) in fixes.Take(Math.Clamp(maxMoves, 1, 200)))
        {
            // فقط اگر در همین فاصله کسی دسته را عوض نکرده باشد.
            var changed = await context.Products
                .Where(p => p.Id == product.Id && p.CategoryId == product.CategoryId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.CategoryId, target).SetProperty(p => p.CategorySource, ProductCategorySource.Smart)
                    .SetProperty(p => p.UpdatedAt, now), cancellationToken);
            if (changed > 0)
                moved.Add(new CategoryFix(product.Id, product.Name, byId[product.CategoryId].Name, byId[target].Name));
        }
        if (moved.Count > 0)
            DataChangeNotifier.Publish(typeof(Product), typeof(Category));
        return new CategoryAuditReport(products.Count, suspects.Count, moved, suspects.Take(5).ToList());
    }

    private sealed record CategoryRow(Guid Id, string Name, string Slug, Guid? ParentId, bool IsActive, bool IsSystem);

    /// <summary>کالای نامزد دسته‌بندی. Source = منبع دستهٔ فعلی (Manual/Smart/General).</summary>
    private sealed record Candidate(Guid Id, string Name, Guid CategoryId, bool IsActive, string? SourceCategory, ProductCategorySource Source,
        string? MarketCategory = null);

    /// <summary>تصمیم جابه‌جایی: دستهٔ مقصد و اینکه دقیق است (Smart) یا کلی (General).</summary>
    private readonly record struct Move(Guid Target, ProductCategorySource Source);

    /// <summary>
    /// تصمیم برای یک کالای بی‌دسته با دسته‌های موجود (بدون ساخت دستهٔ تازه). پیش از این، نوع کالا با دستهٔ هم‌نوع موجود
    /// (پاوربانک ← «پاوربانک») و نوع مهم (دستهٔ اختصاصی) بررسی شده‌اند. ترتیب:
    /// <list type="number">
    /// <item>دسته‌بند آماری مطمئن ← دقیق.</item>
    /// <item>مسیر دستهٔ سایت دیگر: آخرین بخش (برگ) با دستهٔ موجود یکی بود ← دقیق؛ بخش بالاتر ← کلی.
    /// مثال: «زیبایی و سلامت > لوازم شخصی برقی > ماساژور» و دستهٔ «لوازم شخصی برقی» موجود ← کلی.</item>
    /// <item>نوع شناخته‌شده و دستهٔ والد مناسب موجود (ماساژور ← «لوازم شخصی») ← کلی.</item>
    /// </list>
    /// </summary>
    private static Move? Decide(Candidate product, ProductType? type, List<CategoryRow> targets, List<CategoryRow> categories, HashSet<Guid> catchAll,
        CategoryClassifier classifier, NaiveBayesText? secondOpinion = null)
    {
        var guess = classifier.Classify(product.Name);
        var verdict = CategoryConsensus.Decide(guess, CategoryConsensus.Ask(secondOpinion, product.Name));
        if (verdict is { } accepted)
            return new Move(accepted, ProductCategorySource.Smart);

        var path = CategoryNames.Path(product.SourceCategory);
        for (var i = path.Count - 1; i >= 0; i--)
        {
            if (targets.FirstOrDefault(t => CategoryNames.Same(path[i], t.Name, t.Slug)) is { } match)
                return new Move(match.Id, i == path.Count - 1 ? ProductCategorySource.Smart : ProductCategorySource.General);
        }

        if (type is not null && ParentFor(type, product.CategoryId, categories, catchAll) is { } parent)
            return new Move(parent, ProductCategorySource.General);
        return null;
    }

    /// <summary>
    /// دستهٔ «کلی» از مسیر سایت دیگر برای کالای تک: بخش دوم مسیرهای ۳+ بخشی، بخش اول مسیرهای ۲ بخشی (برگ هرگز؛ دستهٔ یک‌کالایی نمی‌سازیم).
    /// والد = بخش بالاتر (اگر در فروشگاه هست). نام باید از دروازهٔ کیفیت <see cref="CategoryNames.Proper"/> بگذرد.
    /// مثال: «زیبایی و سلامت > لوازم شخصی برقی > ماساژور» ← («لوازم شخصی برقی»، والد «زیبایی و سلامت»)؛ «ماساژور» تنها ← null.
    /// </summary>
    public static (string Name, string? Parent)? GeneralLevel(string? sourceCategory, IEnumerable<string>? brands = null)
    {
        var path = CategoryNames.Path(sourceCategory);
        if (path.Count < 2)
            return null;
        var index = path.Count >= 3 ? 1 : 0;
        // از سطح انتخاب‌شده به بالا: اولین نامی که درست و کامل است.
        for (var i = index; i >= 0; i--)
        {
            if (CategoryNames.Proper(path[i], brands) is { } name)
                return (name, i > 0 ? path[i - 1] : null);
        }
        return null;
    }

    /// <summary>برگ مشترک مسیر همهٔ اعضای یک خوشه (برای نام درست‌تر دسته). مثال: سه کالا با برگ «ماساژور» ← «ماساژور».</summary>
    private static string? SharedLeaf(List<Candidate> members)
    {
        var leaves = members.Select(m => CategoryNames.Path(m.SourceCategory)).Select(p => p.Count > 0 ? p[^1] : null).ToList();
        if (leaves.Any(l => l is null))
            return null;
        var first = leaves[0]!;
        return leaves.All(l => CategoryNames.Same(l, first)) ? first : null;
    }

    /// <summary>والد دستهٔ تازهٔ خوشه از مسیر مشترک: بخش پیش از برگ در اولین عضو، اگر در فروشگاه هست.</summary>
    private static Guid? PathParent(List<Candidate> members, List<CategoryRow> targets)
    {
        var path = CategoryNames.Path(members[0].SourceCategory);
        for (var i = path.Count - 2; i >= 0; i--)
        {
            if (targets.FirstOrDefault(t => CategoryNames.Same(path[i], t.Name, t.Slug)) is { } match)
                return match.Id;
        }
        return null;
    }

    /// <summary>
    /// بازبینی کالاهای «دستهٔ کلی» (هر بار دسته‌بندی، از جمله بازبینی روزانه). گروه‌بندی بر اساس (دستهٔ فعلی، نوع کالا یا برگ مسیر):
    /// <list type="bullet">
    /// <item>دستهٔ دقیق همان نوع حالا در فروشگاه هست (مثلاً ادمین «ماساژور» را ساخت) ← همه به آن می‌روند (دقیق).</item>
    /// <item>≥ ۳ کالای هم‌نوع در یک دستهٔ کلی ← زیر‌دستهٔ اختصاصی زیر همان دستهٔ کلی ساخته و کالاها منتقل می‌شوند.</item>
    /// <item>کمتر از ۳ ← همان‌جا می‌مانند (دستهٔ یک‌کالایی ساخته نمی‌شود).</item>
    /// </list>
    /// مثال: ۳ ماساژور در «لوازم شخصی برقی» ← دستهٔ «ماساژور» زیر «لوازم شخصی برقی».
    /// </summary>
    private async Task SplitGeneralsAsync(List<Candidate> generals, List<CategoryRow> targets, Dictionary<Guid, Move> moves,
        Dictionary<string, (Category Entity, int Count)> created, IReadOnlyList<string> brands, CancellationToken cancellationToken)
    {
        if (generals.Count == 0)
            return;
        var keyed = generals.Select(g =>
        {
            var type = ProductTypes.Detect(g.Name);
            var path = CategoryNames.Path(g.SourceCategory);
            var leaf = path.Count > 0 ? CategoryNames.Proper(path[^1], brands) : null;
            var name = type?.Name ?? leaf;
            return (Product: g, Type: type, Name: name);
        }).Where(x => x.Name is not null).ToList();

        foreach (var group in keyed.GroupBy(x => (x.Product.CategoryId, Key: x.Type?.Key ?? PersianText.Normalize(x.Name))))
        {
            var current = targets.FirstOrDefault(t => t.Id == group.Key.CategoryId);
            var first = group.First();
            // دستهٔ فعلی خودش همین نوع است (مثلاً دستهٔ کلی «ماساژور» نام گرفته) ← کاری نیست.
            if (current is not null && (CategoryNames.Same(first.Name, current.Name, current.Slug)
                                        || first.Type is not null && ProductTypes.NameMatches(first.Type, current.Name, current.Slug)))
                continue;

            var exact = targets.FirstOrDefault(t => t.Id != group.Key.CategoryId
                                                    && (first.Type is not null ? ProductTypes.NameMatches(first.Type, t.Name, t.Slug) : CategoryNames.Same(first.Name, t.Name, t.Slug)));
            Guid? target = exact?.Id;
            // دور ۲۳: حتی یک کالا ← زیردستهٔ دقیق (دستهٔ کم‌کالا تا پر شدن noindex و بیرون از نقشهٔ سایت می‌ماند).
            if (target is null)
            {
                var entity = await NewCategoryAsync(created, first.Name!, first.Type?.Key, group.Key.CategoryId, group.Any(x => x.Product.IsActive), cancellationToken);
                target = entity.Id;
            }
            if (target is not { } id)
                continue;
            foreach (var item in group)
                moves[item.Product.Id] = new Move(id, ProductCategorySource.Smart);
        }
    }

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
    /// والد دستهٔ تازهٔ یک نوع: اول دستهٔ والد مبدأ اگر کالا از «سایر X» آمده، بعد اولین دستهٔ سطح‌بالای فعال که نامش یکی از
    /// <see cref="ProductType.ParentHints"/> را دارد. مثال: گوشی + دستهٔ «موبایل و تبلت» موجود ← زیرمجموعهٔ «موبایل و تبلت».
    /// </summary>
    private static Guid? ParentFor(ProductType type, Guid sourceCategory, List<CategoryRow> categories, HashSet<Guid> catchAll)
    {
        if (ParentFromSource(sourceCategory, categories, catchAll) is { } fromSource)
            return fromSource;
        // دور ۲۳: اول خانوادهٔ دقیق (نام کامل برابر؛ «لوازم جانبی خودرو» با «لوازم جانبی» یکی گرفته نمی‌شود).
        if (CategoryFamilies.For(type) is { } family
            && categories.FirstOrDefault(c => !catchAll.Contains(c.Id) && CategoryFamilies.Match(c.Name, c.Slug) == family) is { } exact)
            return exact.Id;
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
    /// دور ۲۳: خانوادهٔ (دستهٔ اصلی) یک نوع کالا؛ اگر در فروشگاه نیست ساخته می‌شود. مثال: اولین ماساژور ← «لوازم شخصی برقی»
    /// (سطح بالا) ساخته و «ماساژور» زیر آن می‌رود. نوع بدون خانواده ← null (دسته سطح بالا می‌شود).
    /// </summary>
    private async Task<Guid?> FamilyParentAsync(ProductType type, List<CategoryRow> categories, Dictionary<string, (Category Entity, int Count)> created,
        bool active, CancellationToken cancellationToken)
    {
        if (CategoryFamilies.For(type) is not { } family)
            return null;
        if (categories.FirstOrDefault(c => !c.IsSystem && CategoryFamilies.Match(c.Name, c.Slug) == family) is { } existing)
            return existing.Id;
        return (await NewCategoryAsync(created, family.Name, family.Key, null, active, cancellationToken)).Id;
    }

    /// <summary>
    /// دور ۲۳: نام «برگ» مسیر دستهٔ سایت دیگر اگر درست و کامل است (برای دستهٔ دقیق تک‌کالا). مثال: «زیبایی > لوازم شخصی برقی > ماساژور»
    /// ← «ماساژور»؛ برگ نصفه/تبلیغاتی («حراج ویژه») ← null.
    /// </summary>
    public static string? LeafName(string? sourceCategory, IEnumerable<string>? brands = null)
    {
        var path = CategoryNames.Path(sourceCategory);
        return path.Count == 0 ? null : CategoryNames.Proper(path[^1], brands);
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
            DisplayOrder = parentId is null ? 100 : 500,
            IconKey = CategoryIcons.For(display, key),
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
