using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// یک دسته با تعداد کالای موجود و دو پرفروش‌ترین (ورودی برنامه‌ریز).
/// دور ۲۶: <paramref name="NewProducts"/> = کالاهای موجودِ تازه‌واردِ ۳۰ روز اخیر (برای مقالهٔ «خبر»).
/// </summary>
public sealed record PlannerCategory(Guid Id, string Name, int InStock, IReadOnlyList<(Guid Id, string Name)> TopProducts,
    IReadOnlyList<(Guid Id, string Name)>? NewProducts = null);

/// <summary>دور ۲۶: یک کمپین/صفحهٔ ویژهٔ فعال (صفحهٔ فرود فعال یا پیشنهاد کمپین) برای مقالهٔ کمپین. مثال: («جشنوارهٔ پاییزه»، دستهٔ خوراکی).</summary>
public sealed record PlannerCampaign(string Title, Guid? CategoryId, string? Url);

/// <summary>یک ایدهٔ پیشنهادی تقویم محتوا.</summary>
public sealed record PlanIdea(BlogArticleType Type, string Topic, string Keyword, Guid? CategoryId, IReadOnlyList<Guid> ProductIds, string Origin);

/// <summary>ردیف تقویم برای نمایش.</summary>
public sealed record BlogPlanRow(Guid Id, DateTimeOffset PlannedFor, BlogArticleType Type, string Topic, string? Keyword, BlogPlanStatus Status,
    Guid? PostId, int? QualityScore, string? Note, string? Origin, Guid? CategoryId, string? ProductIds);

/// <summary>
/// دور ۲۶: نتیجهٔ «پر کردن تقویم» با دلیل روشن. Needed = چند جای خالی بود؛ Added = چند ایده اضافه شد؛ Reason = چرا کمتر شد.
/// پیام «تقویم پر است» فقط وقتی درست است که Needed ≤ ۰ باشد (باگ قبلی: بعد از حذف همهٔ ایده‌ها هم همین پیام می‌آمد).
/// </summary>
public sealed record BlogPlanResult(int Added, int Needed, string? Reason)
{
    /// <summary>متن پیام برای مدیر. مثال: (0، 0) ← «تقویم ۴ هفتهٔ آینده پر است»؛ (3، 8) ← «۳ ایدهٔ تازه … ؛ برای ۵ جای خالی دیگر …».</summary>
    public string Message => Needed <= 0
        ? "تقویم ۴ هفتهٔ آینده پر است؛ برای ایدهٔ تازه، یکی از ایده‌ها را حذف کنید یا تعداد مقاله در هفته را در تنظیمات بیشتر کنید."
        : Added == 0
            ? Reason ?? "موضوع تازه‌ای پیدا نشد."
            : $"{PersianText.Digits(Added)} ایدهٔ تازه به تقویم اضافه شد." + (Added < Needed && Reason is not null ? $" برای {PersianText.Digits(Needed - Added)} جای خالی دیگر: {Reason}" : "");
}

/// <summary>
/// برنامه‌ریز تقویم محتوای بلاگ (B18) – بخش خالص و قطعی (بدون دیتابیس) برای آزمون‌پذیری.
/// <para>دور ۲۶ (خواستهٔ کاربر): بیشتر مقاله‌ها «دسته‌ای» باشند، بعد کمپین، خبر و معرفی کالا. ترتیب:</para>
/// <list type="number">
/// <item>تقاضای واقعی (جست‌وجوی پرتکرار مشتری‌ها که نام دسته دارد) ← راهنمای خرید.</item>
/// <item>کمپین/صفحهٔ ویژهٔ فعال ← مقالهٔ کمپین؛ مناسبت پیش رو (≥ ۱۰ روز مانده) ← مقالهٔ کمپین مناسبتی برای یک دسته.</item>
/// <item>خبر: دسته‌ای که در ۳۰ روز اخیر کالای تازه گرفته ← «تازه‌های {دسته} در {ماه}».</item>
/// <item>چرخهٔ دسته‌ها: معرفی دسته (انواع و کاربرد) ← معرفی کالای پرفروش ← راهنمای خرید ← نکات نگهداری ← مقایسه ← معرفی کالای دوم.</item>
/// </list>
/// <para>هر دسته با دست‌کم ۱ کالای موجود (مقاله بدون کالای واقعی ارزشی ندارد)؛ راهنمای خرید ≥ ۲ و مقایسه ۲ کالای پرفروش لازم دارد.
/// موضوع تکراری (در برنامه یا منتشرشده) پیشنهاد نمی‌شود.</para>
/// </summary>
public static class BlogPlanner
{
    /// <summary>کمترین فاصله تا مناسبت (روز) برای پیشنهاد مقالهٔ مناسبتی؛ کمتر از این، مقاله دیر منتشر و دیده می‌شود.</summary>
    public const int OccasionMinDaysLeft = 10;

    /// <param name="occasions">مناسبت‌های پیش رو (از <see cref="Seo.Autopilot.SeoCalendar"/>). مثال: شب یلدا، ۲۰ روز مانده، دستهٔ «هدفون» ←
    /// «راهنمای خرید هدفون برای شب یلدا» (نوع کمپین).</param>
    /// <param name="campaigns">دور ۲۶: کمپین‌ها و صفحه‌های ویژهٔ فعال.</param>
    /// <param name="now">دور ۲۶: زمان فعلی (برای نام ماه در مقالهٔ خبر)؛ null = بدون مقالهٔ خبر.</param>
    public static List<PlanIdea> Propose(IReadOnlyList<PlannerCategory> categories, IReadOnlyCollection<string> takenTopics, IReadOnlyList<string> demandQueries, int count,
        IReadOnlyList<Seo.Autopilot.SeoOccasion>? occasions = null, IReadOnlyList<PlannerCampaign>? campaigns = null, DateTimeOffset? now = null)
    {
        var taken = new HashSet<string>(takenTopics.Select(PersianText.Normalize), StringComparer.Ordinal);
        var ideas = new List<PlanIdea>();
        var eligible = categories.Where(c => c.InStock >= 1).OrderByDescending(c => c.InStock).ToList();
        if (count <= 0)
            return ideas;

        // ۱) تقاضای واقعی: جست‌وجویی که نام یک دسته را دارد ← راهنمای خرید.
        foreach (var query in demandQueries)
        {
            var normalized = PersianText.Normalize(query);
            var category = eligible.FirstOrDefault(c => c.InStock >= 2 && normalized.Contains(PersianText.Normalize(c.Name), StringComparison.Ordinal));
            if (category is null)
                continue;
            TryAdd(new PlanIdea(BlogArticleType.BuyingGuide, $"راهنمای خرید {PersianText.Clean(query)}", $"راهنمای خرید {PersianText.Clean(query)}", category.Id, [], "demand"));
            if (ideas.Count >= count)
                return ideas;
        }

        // ۲) کمپین‌های فعال (صفحهٔ ویژه/پیشنهاد کمپین) ← مقالهٔ کمپین با کالاهای واقعی همان دسته (یا پرفروش‌ترین دسته).
        foreach (var campaign in campaigns ?? Array.Empty<PlannerCampaign>())
        {
            var title = PersianText.Clean(campaign.Title);
            if (title.Length < 4 || eligible.Count == 0)
                continue;
            var category = eligible.FirstOrDefault(c => c.Id == campaign.CategoryId) ?? eligible[0];
            TryAdd(new PlanIdea(BlogArticleType.Campaign, title, title, category.Id, [], "campaign"));
            if (ideas.Count >= count)
                return ideas;
        }

        // ۲.۵) مناسبت‌های پیش رو ← مقالهٔ کمپین مناسبتی برای دسته‌ها (به نوبت).
        if (eligible.Count > 0 && occasions is not null)
        {
            var turn = 0;
            foreach (var occasion in occasions.Where(o => o.DaysLeft >= OccasionMinDaysLeft).OrderBy(o => o.DaysLeft))
            {
                var label = PersianText.Clean(occasion.Name.Split('(')[0]);
                if (label.Length == 0)
                    continue;
                var category = eligible[turn++ % eligible.Count];
                var topic = $"راهنمای خرید {PersianText.Clean(category.Name)} برای {label}";
                TryAdd(new PlanIdea(BlogArticleType.Campaign, topic, topic, category.Id, [], "occasion"));
                if (ideas.Count >= count)
                    return ideas;
            }
        }

        // ۳) خبر واقعی: کالاهای تازهٔ یک دسته در ماه جاری.
        if (now is { } today)
        {
            var month = PersianMonth(today);
            foreach (var category in eligible.Where(c => c.NewProducts is { Count: > 0 }).OrderByDescending(c => c.NewProducts!.Count))
            {
                var name = PersianText.Clean(category.Name);
                var topic = $"تازه‌های {name} در {month}";
                TryAdd(new PlanIdea(BlogArticleType.News, topic, $"تازه‌های {name}", category.Id, category.NewProducts!.Take(6).Select(p => p.Id).ToList(), "news"));
                if (ideas.Count >= count)
                    return ideas;
            }
        }

        // ۴) چرخهٔ دسته‌ها (بیشتر مقاله‌ها دسته‌ای‌اند).
        var rounds = new (BlogArticleType Type, int Pick)[]
        {
            (BlogArticleType.CategoryGuide, 0), (BlogArticleType.Review, 0), (BlogArticleType.BuyingGuide, 0),
            (BlogArticleType.HowTo, 0), (BlogArticleType.Comparison, 0), (BlogArticleType.Review, 1),
        };
        foreach (var (round, pick) in rounds)
        {
            foreach (var category in eligible)
            {
                var name = PersianText.Clean(category.Name);
                var idea = round switch
                {
                    BlogArticleType.CategoryGuide => new PlanIdea(round, $"انواع {name} و کاربرد هر کدام", $"انواع {name}", category.Id, [], "auto"),
                    BlogArticleType.HowTo => new PlanIdea(round, $"نکات نگهداری و استفادهٔ درست از {name}", $"نگهداری {name}", category.Id, [], "auto"),
                    BlogArticleType.Review when category.TopProducts.Count > pick => new PlanIdea(round,
                        $"معرفی و بررسی {Short(category.TopProducts[pick].Name)}", $"بررسی {Short(category.TopProducts[pick].Name)}",
                        category.Id, [category.TopProducts[pick].Id], "auto"),
                    BlogArticleType.Review => null,
                    BlogArticleType.BuyingGuide when category.InStock >= 2 => new PlanIdea(round, $"راهنمای خرید {name}", $"راهنمای خرید {name}", category.Id, [], "auto"),
                    BlogArticleType.BuyingGuide => null,
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

    /// <summary>نام ماه شمسی و سال. مثال: ۱ آبان ۱۴۰۵ ← «آبان ۱۴۰۵».</summary>
    public static string PersianMonth(DateTimeOffset now)
    {
        var local = IranClock.ToLocal(now).DateTime;
        var calendar = new System.Globalization.PersianCalendar();
        string[] months = ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];
        return $"{months[calendar.GetMonth(local) - 1]} {PersianText.Digits(calendar.GetYear(local))}";
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

    /// <summary>دور ۲۶: همان پر کردن تقویم با دلیل روشن (برای پیام صفحهٔ تقویم).</summary>
    Task<BlogPlanResult> PlanDetailedAsync(int perWeek, CancellationToken cancellationToken = default);

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
    public async Task<int> PlanAsync(int perWeek, CancellationToken cancellationToken = default) =>
        (await PlanDetailedAsync(perWeek, cancellationToken)).Added;

    public async Task<BlogPlanResult> PlanDetailedAsync(int perWeek, CancellationToken cancellationToken = default)
    {
        if (perWeek <= 0)
            return new BlogPlanResult(0, 0, "تعداد مقاله در هفته در تنظیمات صفر است.");
        var now = DateTimeOffset.UtcNow;
        var horizon = now.AddDays(28);
        var upcoming = await context.BlogPlanItems
            .Where(i => i.PlannedFor >= now && i.PlannedFor <= horizon && (i.Status == BlogPlanStatus.Planned || i.Status == BlogPlanStatus.Writing))
            .Select(i => i.PlannedFor).ToListAsync(cancellationToken);
        var needed = perWeek * 4 - upcoming.Count;
        if (needed <= 0)
            return new BlogPlanResult(0, needed, null);

        // دور ۲۶: هر دستهٔ فعال با دست‌کم ۱ کالای موجود (قبلاً ۳) ← فروشگاه کوچک هم ایدهٔ واقعی می‌گیرد.
        var categories = await context.Categories.Where(c => c.IsActive && !c.IsSystem)
            .Select(c => new
            {
                c.Id, c.Name,
                InStock = context.Products.Count(p => p.CategoryId == c.Id && p.IsActive && p.StockQuantity > 0),
            })
            .Where(c => c.InStock >= 1)
            .OrderByDescending(c => c.InStock).Take(60)
            .ToListAsync(cancellationToken);
        if (categories.Count == 0)
            return new BlogPlanResult(0, needed, "هیچ دستهٔ فعالی کالای منتشرشدهٔ موجود ندارد؛ مقاله بدون کالای واقعی نوشته نمی‌شود.");
        var ids = categories.Select(c => c.Id).ToList();
        var tops = await context.Products
            .Where(p => ids.Contains(p.CategoryId) && p.IsActive && p.StockQuantity > 0)
            .OrderByDescending(p => p.SalesCount)
            .Select(p => new { p.CategoryId, p.Id, p.Name })
            .Take(600).ToListAsync(cancellationToken);
        var newSince = now.AddDays(-30);
        var fresh = await context.Products
            .Where(p => ids.Contains(p.CategoryId) && p.IsActive && p.StockQuantity > 0 && (p.PublishedAt ?? p.CreatedAt) >= newSince)
            .OrderByDescending(p => p.PublishedAt ?? p.CreatedAt)
            .Select(p => new { p.CategoryId, p.Id, p.Name })
            .Take(300).ToListAsync(cancellationToken);
        var planner = categories.Select(c => new PlannerCategory(c.Id, c.Name, c.InStock,
            tops.Where(t => t.CategoryId == c.Id).Take(2).Select(t => (t.Id, t.Name)).ToList(),
            fresh.Where(t => t.CategoryId == c.Id).Take(6).Select(t => (t.Id, t.Name)).ToList())).ToList();

        // کمپین‌های فعال: صفحه‌های فرود فعال (در بازهٔ نمایش).
        var landings = await context.LandingPages
            .Where(l => l.IsActive && (l.StartAt == null || l.StartAt <= now) && (l.EndAt == null || l.EndAt > now))
            .OrderByDescending(l => l.CreatedAt).Select(l => new { l.Title, l.Slug }).Take(5).ToListAsync(cancellationToken);
        var campaigns = landings.Select(l => new PlannerCampaign(l.Title, null, "/campaign/" + l.Slug)).ToList();

        // دور ۲۶ (باگ «تقویم پر است»): ایدهٔ حذف‌شده (Skipped) و ردیف پاک‌شده دیگر «گرفته» حساب نمی‌شود مگر در دور اول؛
        // اول ایدهٔ کاملاً تازه، اگر کم بود موضوع‌های حذف‌شدهٔ قبلی هم دوباره مجازند.
        var active = await context.BlogPlanItems.Where(i => i.Status != BlogPlanStatus.Skipped).Select(i => i.Topic).ToListAsync(cancellationToken);
        var skipped = await context.BlogPlanItems.IgnoreQueryFilters().Where(i => i.Status == BlogPlanStatus.Skipped || i.IsDeleted)
            .Select(i => i.Topic).ToListAsync(cancellationToken);
        var posts = await context.BlogPosts.Select(p => p.Title).ToListAsync(cancellationToken);
        var since = now.AddDays(-30);
        var demand = await context.SearchQueryLogs
            .Where(q => !q.IsHidden && q.LastSearchedAt >= since && q.SearchCount >= 5)
            .OrderByDescending(q => q.SearchCount).Select(q => q.Query).Take(30).ToListAsync(cancellationToken);

        var occasions = Seo.Autopilot.SeoCalendar.Upcoming(IranClock.Today(now), Array.Empty<string>(), 45);
        var firstPass = Strip(active.Concat(posts).Concat(skipped));
        var ideas = BlogPlanner.Propose(planner, firstPass, demand, needed, occasions, campaigns, now);
        if (ideas.Count < needed)
        {
            var secondPass = Strip(active.Concat(posts).Concat(ideas.Select(i => i.Topic)));
            ideas.AddRange(BlogPlanner.Propose(planner, secondPass, demand, needed - ideas.Count, occasions, campaigns, now));
        }
        if (ideas.Count == 0)
        {
            return new BlogPlanResult(0, needed,
                "همهٔ موضوع‌های ممکن با دسته‌ها و کالاهای فعلی قبلاً نوشته یا برنامه‌ریزی شده‌اند؛ با کالا یا دستهٔ تازه، ایدهٔ تازه ساخته می‌شود. می‌توانید ایدهٔ دستی هم اضافه کنید.");
        }

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
        return new BlogPlanResult(count, needed, count < needed ? "موضوع تازهٔ بیشتری با کالاهای فعلی پیدا نشد." : null);
    }

    /// <summary>
    /// عنوان‌های گرفته‌شده + نسخهٔ بدون پسوند (عنوان منتشرشدهٔ «… ۱۴۰۵؛ …» با موضوع «…» یکی حساب شود).
    /// </summary>
    private static List<string> Strip(IEnumerable<string> titles)
    {
        var list = titles.ToList();
        var all = list.Concat(list.Select(t => t.Split('؛')[0].Trim())).ToList();
        return all.Select(t => System.Text.RegularExpressions.Regex.Replace(t, @"\s*[۰-۹0-9]{4}\s*$", "", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100))).ToList();
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
        // دور ۲۶: کمپین و خبر با دسته یا دست‌کم یک محصول؛ معرفی دسته/راهنما/نگهداری/فهرست حتماً با دسته.
        var hasProducts = (productIds?.Count ?? 0) > 0;
        if (type is BlogArticleType.Campaign or BlogArticleType.News && categoryId is null && !hasProducts)
            return OperationResult<Guid>.Error("برای مقالهٔ کمپین یا خبر یک دسته یا دست‌کم یک محصول انتخاب کنید.");
        if (type is BlogArticleType.CategoryGuide or BlogArticleType.BuyingGuide or BlogArticleType.HowTo or BlogArticleType.BestPicks && categoryId is null)
            return OperationResult<Guid>.Error("برای این نوع مقاله یک دسته انتخاب کنید.");
        if (type == BlogArticleType.Review && categoryId is null && !hasProducts)
            return OperationResult<Guid>.Error("برای معرفی کالا یک محصول (یا دسته) انتخاب کنید.");
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
