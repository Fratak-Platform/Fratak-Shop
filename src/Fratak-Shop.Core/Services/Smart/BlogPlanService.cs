using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>یک دسته با تعداد کالای موجود و دو پرفروش‌ترین (ورودی برنامه‌ریز).</summary>
public sealed record PlannerCategory(Guid Id, string Name, int InStock, IReadOnlyList<(Guid Id, string Name)> TopProducts);

/// <summary>یک ایدهٔ پیشنهادی تقویم محتوا.</summary>
public sealed record PlanIdea(BlogArticleType Type, string Topic, string Keyword, Guid? CategoryId, IReadOnlyList<Guid> ProductIds, string Origin);

/// <summary>ردیف تقویم برای نمایش.</summary>
public sealed record BlogPlanRow(Guid Id, DateTimeOffset PlannedFor, BlogArticleType Type, string Topic, string? Keyword, BlogPlanStatus Status,
    Guid? PostId, int? QualityScore, string? Note, string? Origin, Guid? CategoryId, string? ProductIds);

/// <summary>
/// برنامه‌ریز تقویم محتوای بلاگ (B18) – بخش خالص و قطعی (بدون دیتابیس) برای آزمون‌پذیری.
/// <para>قاعده‌ها: فقط دسته‌هایی که دست‌کم ۳ کالای موجود دارند (مقاله بدون کالای واقعی ارزشی ندارد)؛ انواع مقاله
/// به نوبت (دور ۲۳: معرفی ویژهٔ یک کالای واقعی ← راهنمای خرید ← مقایسه ← معرفی کالای دوم؛ بدون فهرست کلی «بهترین‌ها»)؛ موضوع تکراری (در برنامه یا منتشرشده) پیشنهاد نمی‌شود؛ جست‌وجوهای پرتکرار
/// کاربران که به نام یک دسته می‌خورند اولویت دارند («تقاضای واقعی»).</para>
/// </summary>
public static class BlogPlanner
{
    public static List<PlanIdea> Propose(IReadOnlyList<PlannerCategory> categories, IReadOnlyCollection<string> takenTopics, IReadOnlyList<string> demandQueries, int count)
    {
        var taken = new HashSet<string>(takenTopics.Select(PersianText.Normalize), StringComparer.Ordinal);
        var ideas = new List<PlanIdea>();
        var eligible = categories.Where(c => c.InStock >= 3).ToList();

        // ۱) تقاضای واقعی: جست‌وجویی که نام یک دسته را دارد ← «پیشنهاد خرید …» (دور ۲۱: بدون ادعای «بهترین»).
        foreach (var query in demandQueries)
        {
            var normalized = PersianText.Normalize(query);
            var category = eligible.FirstOrDefault(c => normalized.Contains(PersianText.Normalize(c.Name), StringComparison.Ordinal));
            if (category is null)
                continue;
            // دور ۲۳: تقاضای واقعی ← راهنمای خرید (نه فهرست کلی «پیشنهاد خرید»).
            TryAdd(new PlanIdea(BlogArticleType.BuyingGuide, $"راهنمای خرید {PersianText.Clean(query)}", $"راهنمای خرید {PersianText.Clean(query)}", category.Id, [], "demand"));
            if (ideas.Count >= count)
                return ideas;
        }

        // ۲) چرخهٔ انواع برای هر دسته. دور ۲۳: به جای فهرست کلی «پیشنهاد خرید …»، «معرفی ویژهٔ» یک کالای واقعی پرفروش همان دسته
        // (نام، دسته و مشخصات برجستهٔ واقعی؛ BlogArticleType.Review). نوبت چهارم = معرفی دومین کالای پرفروش.
        var rounds = new (BlogArticleType Type, int Pick)[] { (BlogArticleType.Review, 0), (BlogArticleType.BuyingGuide, 0), (BlogArticleType.Comparison, 0), (BlogArticleType.Review, 1) };
        foreach (var (round, pick) in rounds)
        {
            foreach (var category in eligible.OrderByDescending(c => c.InStock))
            {
                var name = PersianText.Clean(category.Name);
                var idea = round switch
                {
                    BlogArticleType.Review when category.TopProducts.Count > pick => new PlanIdea(round,
                        $"معرفی و بررسی {Short(category.TopProducts[pick].Name)}", $"بررسی {Short(category.TopProducts[pick].Name)}",
                        category.Id, [category.TopProducts[pick].Id], "auto"),
                    BlogArticleType.Review => null,
                    BlogArticleType.BuyingGuide => new PlanIdea(round, $"راهنمای خرید {name}", $"راهنمای خرید {name}", category.Id, [], "auto"),
                    _ when category.TopProducts.Count >= 2 => new PlanIdea(round,
                        $"مقایسه {Short(category.TopProducts[0].Name)} و {Short(category.TopProducts[1].Name)}",
                        $"مقایسه {Short(category.TopProducts[0].Name)} و {Short(category.TopProducts[1].Name)}",
                        category.Id, [category.TopProducts[0].Id, category.TopProducts[1].Id], "auto"),
                    _ => null,
                };
                if (idea is not null)
                    TryAdd(idea);
                if (ideas.Count >= count)
                    return ideas;
            }
        }
        return ideas;

        void TryAdd(PlanIdea idea)
        {
            if (taken.Add(PersianText.Normalize(idea.Topic)))
                ideas.Add(idea);
        }
    }

    /// <summary>
    /// تاریخ‌های انتشار: <paramref name="perWeek"/> روز در هفته با فاصلهٔ یکنواخت، ساعت ۱۰ صبح به وقت ایران، از فردا.
    /// مثال: ۲ در هفته ← روزهای ۱ و ۴ هر هفته.
    /// </summary>
    public static List<DateTimeOffset> Schedule(DateTimeOffset now, int perWeek, int weeks, IReadOnlyCollection<DateOnly> busyDays)
    {
        var result = new List<DateTimeOffset>();
        if (perWeek <= 0)
            return result;
        var start = IranClock.Today(now).AddDays(1);
        var step = 7.0 / perWeek;
        var busy = new HashSet<DateOnly>(busyDays);
        for (var week = 0; week < weeks; week++)
        {
            for (var i = 0; i < perWeek; i++)
            {
                var day = start.AddDays(week * 7 + (int)Math.Round(i * step));
                while (busy.Contains(day))
                    day = day.AddDays(1);
                busy.Add(day);
                result.Add(IranClock.At(day, new TimeOnly(10, 0)));
            }
        }
        return result;
    }

    private static string Short(string name)
    {
        var words = PersianText.Clean(name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Take(5));
    }
}

/// <summary>
/// تقویم محتوای بلاگ روی جدول BlogPlanItems (B18).
/// <para>هر هفته کارگر هوشمند <see cref="PlanAsync"/> را صدا می‌زند تا ۴ هفتهٔ آینده پر باشد؛ هر روز ردیف سررسیده را به
/// «نوشتن مقاله» در صف هوشمند می‌سپارد. ادمین می‌تواند ایده اضافه/حذف/جابه‌جا کند یا همین حالا بنویسد.</para>
/// <para>محل استفاده: SmartWorker و صفحهٔ «پنل ← محتوای هوشمند ← تقویم بلاگ».</para>
/// </summary>
public interface IBlogPlanService
{
    /// <summary>پر کردن تقویم تا ۴ هفتهٔ آینده. خروجی: تعداد ایدهٔ افزوده.</summary>
    Task<int> PlanAsync(int perWeek, CancellationToken cancellationToken = default);

    Task<List<BlogPlanRow>> ListAsync(int days = 45, CancellationToken cancellationToken = default);

    Task<BlogPlanRow?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>ردیف‌های سررسیده (برنامه‌ریزی‌شده و زمانشان رسیده).</summary>
    Task<List<BlogPlanRow>> DueAsync(DateTimeOffset now, int take = 1, CancellationToken cancellationToken = default);

    Task<OperationResult<Guid>> AddAsync(BlogArticleType type, string topic, string? keyword, Guid? categoryId, IReadOnlyCollection<Guid>? productIds, DateTimeOffset plannedFor, CancellationToken cancellationToken = default);

    Task MarkAsync(Guid id, BlogPlanStatus status, Guid? postId = null, int? score = null, string? note = null, CancellationToken cancellationToken = default);

    Task<OperationResult> SkipAsync(Guid id, CancellationToken cancellationToken = default);

    Task<List<(Guid Id, string Name)>> CategoriesAsync(CancellationToken cancellationToken = default);
}

public sealed class BlogPlanService(FratakShopDbContext context) : IBlogPlanService
{
    public async Task<int> PlanAsync(int perWeek, CancellationToken cancellationToken = default)
    {
        if (perWeek <= 0)
            return 0;
        var now = DateTimeOffset.UtcNow;
        var horizon = now.AddDays(28);
        var upcoming = await context.BlogPlanItems
            .Where(i => i.PlannedFor >= now && i.PlannedFor <= horizon && (i.Status == BlogPlanStatus.Planned || i.Status == BlogPlanStatus.Writing))
            .Select(i => i.PlannedFor).ToListAsync(cancellationToken);
        var needed = perWeek * 4 - upcoming.Count;
        if (needed <= 0)
            return 0;

        var categories = await context.Categories.Where(c => c.IsActive)
            .Select(c => new
            {
                c.Id, c.Name,
                InStock = context.Products.Count(p => p.CategoryId == c.Id && p.IsActive && p.StockQuantity > 0),
            })
            .Where(c => c.InStock >= 3)
            .OrderByDescending(c => c.InStock).Take(40)
            .ToListAsync(cancellationToken);
        var ids = categories.Select(c => c.Id).ToList();
        var tops = await context.Products
            .Where(p => ids.Contains(p.CategoryId) && p.IsActive && p.StockQuantity > 0)
            .OrderByDescending(p => p.SalesCount)
            .Select(p => new { p.CategoryId, p.Id, p.Name })
            .Take(400).ToListAsync(cancellationToken);
        var planner = categories.Select(c => new PlannerCategory(c.Id, c.Name, c.InStock,
            tops.Where(t => t.CategoryId == c.Id).Take(2).Select(t => (t.Id, t.Name)).ToList())).ToList();

        var taken = await context.BlogPlanItems.IgnoreQueryFilters().Select(i => i.Topic).ToListAsync(cancellationToken);
        taken.AddRange(await context.BlogPosts.Select(p => p.Title).ToListAsync(cancellationToken));
        var since = now.AddDays(-30);
        var demand = await context.SearchQueryLogs
            .Where(q => !q.IsHidden && q.LastSearchedAt >= since && q.SearchCount >= 5)
            .OrderByDescending(q => q.SearchCount).Select(q => q.Query).Take(30).ToListAsync(cancellationToken);

        // موضوع منتشرشده با عنوان کامل مقایسه نمی‌شود؛ «۵ پیشنهاد خرید X» در عنوان «۵ پیشنهاد خرید X ۱۴۰۵؛ …» هم تکراری حساب شود.
        var takenTopics = taken.Concat(taken.Select(t => t.Split('؛')[0].Trim())).ToList();
        var stripped = takenTopics.Select(t => System.Text.RegularExpressions.Regex.Replace(t, @"\s*[۰-۹0-9]{4}\s*$", "", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100))).ToList();
        var ideas = BlogPlanner.Propose(planner, stripped, demand, needed);
        if (ideas.Count == 0)
            return 0;

        var busy = upcoming.Select(d => IranClock.Today(d)).ToList();
        var dates = BlogPlanner.Schedule(now, perWeek, 4, busy);
        var count = 0;
        foreach (var (idea, date) in ideas.Zip(dates))
        {
            context.BlogPlanItems.Add(new BlogPlanItem
            {
                PlannedFor = date,
                ArticleType = idea.Type,
                Topic = PersianText.Truncate(idea.Topic, 250),
                Keyword = PersianText.Truncate(idea.Keyword, 250),
                CategoryId = idea.CategoryId,
                ProductIds = idea.ProductIds.Count == 0 ? null : string.Join(',', idea.ProductIds),
                Status = BlogPlanStatus.Planned,
                Origin = idea.Origin,
            });
            count++;
        }
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return count;
    }

    public Task<List<BlogPlanRow>> ListAsync(int days = 45, CancellationToken cancellationToken = default)
    {
        var from = DateTimeOffset.UtcNow.AddDays(-days);
        return Rows(context.BlogPlanItems.Where(i => i.PlannedFor >= from).OrderBy(i => i.PlannedFor).Take(300)).ToListAsync(cancellationToken);
    }

    public Task<BlogPlanRow?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Rows(context.BlogPlanItems.Where(i => i.Id == id)).FirstOrDefaultAsync(cancellationToken);

    public Task<List<BlogPlanRow>> DueAsync(DateTimeOffset now, int take = 1, CancellationToken cancellationToken = default) =>
        Rows(context.BlogPlanItems.Where(i => i.Status == BlogPlanStatus.Planned && i.PlannedFor <= now)
            .OrderBy(i => i.PlannedFor).Take(Math.Clamp(take, 1, 10))).ToListAsync(cancellationToken);

    public async Task<OperationResult<Guid>> AddAsync(BlogArticleType type, string topic, string? keyword, Guid? categoryId, IReadOnlyCollection<Guid>? productIds, DateTimeOffset plannedFor, CancellationToken cancellationToken = default)
    {
        var clean = PersianText.Clean(topic);
        if (clean.Length < 4)
            return OperationResult<Guid>.Error("موضوع مقاله را بنویسید (حداقل ۴ حرف).");
        if (type == BlogArticleType.Comparison && (productIds?.Count ?? 0) < 2)
            return OperationResult<Guid>.Error("برای مقالهٔ مقایسه دو محصول انتخاب کنید.");
        if (type != BlogArticleType.Comparison && type != BlogArticleType.Review && categoryId is null)
            return OperationResult<Guid>.Error("برای این نوع مقاله یک دسته انتخاب کنید.");
        var item = new BlogPlanItem
        {
            PlannedFor = plannedFor,
            ArticleType = type,
            Topic = PersianText.Truncate(clean, 250),
            Keyword = string.IsNullOrWhiteSpace(keyword) ? null : PersianText.Truncate(PersianText.Clean(keyword), 250),
            CategoryId = categoryId,
            ProductIds = productIds is { Count: > 0 } ? string.Join(',', productIds.Take(10)) : null,
            Status = BlogPlanStatus.Planned,
            Origin = "manual",
        };
        context.BlogPlanItems.Add(item);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return OperationResult<Guid>.Success(item.Id, "ایده به تقویم محتوا اضافه شد.");
    }

    public async Task MarkAsync(Guid id, BlogPlanStatus status, Guid? postId = null, int? score = null, string? note = null, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cleanNote = SmartTaskQueue.Limit(note, 1000);
        await context.BlogPlanItems.Where(i => i.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, status)
                .SetProperty(i => i.PostId, i => postId ?? i.PostId)
                .SetProperty(i => i.QualityScore, i => score ?? i.QualityScore)
                .SetProperty(i => i.Note, i => cleanNote ?? i.Note)
                .SetProperty(i => i.UpdatedAt, now), cancellationToken);
    }

    public async Task<OperationResult> SkipAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var changed = await context.BlogPlanItems.Where(i => i.Id == id && i.Status == BlogPlanStatus.Planned)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, BlogPlanStatus.Skipped).SetProperty(i => i.UpdatedAt, now), cancellationToken);
        return changed == 1 ? OperationResult.Success("ایده از تقویم کنار گذاشته شد.") : OperationResult.Error("فقط ایده‌های «برنامه‌ریزی‌شده» قابل حذف‌اند.");
    }

    public async Task<List<(Guid Id, string Name)>> CategoriesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).Select(c => new { c.Id, c.Name }).Take(500).ToListAsync(cancellationToken);
        return rows.Select(r => (r.Id, r.Name)).ToList();
    }

    private static IQueryable<BlogPlanRow> Rows(IQueryable<BlogPlanItem> query) =>
        query.Select(i => new BlogPlanRow(i.Id, i.PlannedFor, i.ArticleType, i.Topic, i.Keyword, i.Status, i.PostId, i.QualityScore, i.Note, i.Origin, i.CategoryId, i.ProductIds));

    /// <summary>«id1,id2» ← فهرست شناسه‌ها (مقدار خراب نادیده).</summary>
    public static List<Guid> ParseIds(string? csv) =>
        (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Guid.TryParse(s, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).Distinct().ToList();
}
