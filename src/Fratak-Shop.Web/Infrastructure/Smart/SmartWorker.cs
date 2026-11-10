using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Smart;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Infrastructure.Smart;

/// <summary>
/// کارگر پس‌زمینهٔ محتوای هوشمند (B21، C19–C21).
/// <list type="bullet">
/// <item>هر ۲۰ ثانیه (صف خالی: هر ۶۰ ثانیه) یک کار از صف هوشمند برمی‌دارد و با <see cref="SmartOrchestrator"/> اجرا می‌کند؛
/// خطا ← تلاش دوباره با فاصلهٔ فزاینده (حداکثر ۳ بار، بعد «ناموفق» با پیام خطا در پنل).</item>
/// <item>دور ۲۲ – هر ۳ دقیقه: «ورود هوشمند کالا» (<see cref="IProductIntakeService"/>): کالای تازه دسته می‌گیرد (با کمک دستهٔ همان کالا
/// در دیجی‌کالا)، «تکمیل هوشمند» در صف می‌رود و کالای تازه‌منتشرشده سئو می‌گیرد.</item>
/// <item>هر ۳۰ دقیقه: یادآوری‌های مشتری (درخواست نظر و تمدید کالای مصرفی، با سکوت شبانه).</item>
/// <item>دور ۲۳ – هر ۱۵ دقیقه: «تکمیل پیوسته» کالاها و پیش‌نویس‌های ناقص (شبانه‌روزی، بدون دکمهٔ مدیر؛ سقف NightlyCompletePerDay
/// در ۲۴ ساعت، هر کالا حداکثر یک بار در ۷ روز).</item>
/// <item>دور ۲۳ – هر ساعت: دسته‌بندی هوشمند کالاهای بی‌دسته/کلی و «ممیز درخت» (<see cref="ICategoryTreeService"/>): یکی کردن
/// دسته‌های تکراری با ۳۰۱، جابه‌جایی دستهٔ جای‌غلط، شکستن دستهٔ شلوغ، پنهان کردن دستهٔ خالی.</item>
/// <item>هر ۶ ساعت: تقویم محتوای ۴ هفتهٔ آینده و مقاله‌های سررسیده؛ رشد خودکار؛ ترمیم متن‌های قدیمی («دسته‌بندی نشده»)؛
/// خلاصهٔ «آمادهٔ انتشار» برای مدیر؛ پاک‌سازی کش تحقیق و کارهای قدیمی.</item>
/// <item>بعد از هر نتیجه (یا شکست نهایی): اعلان به ادمین با لینک مستقیم به بخش بازبینی (<see cref="SmartNotifier"/>).</item>
/// </list>
/// <para>همه از مدیر منابع می‌گذرند (کار هوشمند «سنگین» است: یکی‌یکی و فقط وقتی سرور زیر فشار نیست – A1)
/// و از صفحهٔ «فنی ← کارهای پس‌زمینه» قابل توقف‌اند. هیچ استثنایی از ExecuteAsync بیرون نمی‌رود.</para>
/// </summary>
public sealed class SmartWorker(IServiceScopeFactory scopeFactory, ILogger<SmartWorker> logger) : BackgroundService
{
    public const string JobName = "محتوای هوشمند";
    public const string ReminderJobName = "یادآوری‌های مشتری";
    public const string PlannerJobName = "برنامه‌ریز محتوای هوشمند";
    public const string IntakeJobName = "ورود هوشمند کالا";
    public const string ContinuousJobName = "تکمیل پیوستهٔ کالاها";
    public const string CategorizeJobName = "دسته‌بندی و ممیزی درخت دسته‌ها";

    private static readonly TimeSpan BusyDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ReminderEvery = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PlannerEvery = TimeSpan.FromHours(6);
    private static readonly TimeSpan IntakeEvery = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan ContinuousEvery = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CategorizeEvery = TimeSpan.FromHours(1);

    private readonly string _workerId = Environment.MachineName + ":" + Environment.ProcessId;
    private DateTimeOffset _lastReminders = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPlanner = DateTimeOffset.MinValue;
    private DateTimeOffset _lastIntake = DateTimeOffset.MinValue;
    private DateTimeOffset _lastContinuous = DateTimeOffset.MinValue;
    private DateTimeOffset _lastCategorize = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        JobMonitor.Register(JobName, TimeSpan.FromHours(12));
        JobMonitor.Register(ReminderJobName, TimeSpan.FromHours(2));
        JobMonitor.Register(PlannerJobName, TimeSpan.FromHours(12));
        JobMonitor.Register(IntakeJobName, TimeSpan.FromHours(1));
        JobMonitor.Register(ContinuousJobName, TimeSpan.FromHours(1));
        JobMonitor.Register(CategorizeJobName, TimeSpan.FromHours(3));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(90), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = IdleDelay;
            try
            {
                var now = DateTimeOffset.UtcNow;
                if (now - _lastReminders >= ReminderEvery)
                {
                    _lastReminders = now;
                    await RunScopedAsync(ReminderJobName, async sp =>
                    {
                        var result = await sp.GetRequiredService<ICustomerReminderService>().RunAsync(DateTimeOffset.UtcNow, stoppingToken);
                        return result.Sent + result.Scheduled;
                    });
                }
                if (now - _lastIntake >= IntakeEvery)
                {
                    _lastIntake = now;
                    await RunScopedAsync(IntakeJobName, sp => IntakeAsync(sp, DateTimeOffset.UtcNow, stoppingToken));
                }
                if (now - _lastCategorize >= CategorizeEvery)
                {
                    _lastCategorize = now;
                    await RunScopedAsync(CategorizeJobName, sp => CategorizeHourlyAsync(sp, DateTimeOffset.UtcNow, stoppingToken));
                }
                if (now - _lastContinuous >= ContinuousEvery)
                {
                    _lastContinuous = now;
                    await RunScopedAsync(ContinuousJobName, sp => ContinuousAsync(sp, DateTimeOffset.UtcNow, stoppingToken));
                }
                if (now - _lastPlanner >= PlannerEvery)
                {
                    _lastPlanner = now;
                    await RunScopedAsync(PlannerJobName, sp => PlanAsync(sp, stoppingToken));
                }
                var processed = await RunScopedAsync(JobName, sp => ProcessOneAsync(sp, stoppingToken));
                if (processed > 0)
                    delay = BusyDelay;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Smart worker iteration failed");
                delay = TimeSpan.FromMinutes(2);
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<int> RunScopedAsync(string job, Func<IServiceProvider, Task<int>> run)
    {
        using var scope = scopeFactory.CreateScope();
        return await JobMonitor.TrackAsync(job, () => run(scope.ServiceProvider));
    }

    /// <summary>
    /// دور ۲۲: یک دور «ورود هوشمند کالا». دستهٔ کالا در سایت دیگر با <see cref="SmartSources.SourceCategoryAsync"/> پرسیده می‌شود
    /// (مؤدبانه و کش‌شده). کالای بی‌دسته‌مانده ← اعلان به ادمین با لینک فهرست «بدون دسته». خطا فقط ثبت می‌شود.
    /// </summary>
    public static async Task<int> IntakeAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var sources = services.GetRequiredService<SmartSources>();
            var report = await services.GetRequiredService<IProductIntakeService>()
                .RunAsync(now, sources.SourceCategoryAsync, null, ProductIntakeService.DefaultTake, cancellationToken);
            await services.GetRequiredService<SmartNotifier>().NotifyIntakeAsync(report);
            return report.Examined + report.Seo;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            services.GetRequiredService<ILogger<SmartWorker>>().LogWarning(exception, "Smart product intake failed");
            return 0;
        }
    }

    /// <summary>برنامه‌ریزی تقویم، صف کردن مقاله‌های سررسیده، تکمیل شبانهٔ کالاها و پاک‌سازی.</summary>
    private static async Task<int> PlanAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var options = await services.GetRequiredService<ISmartConfigService>().GetAsync(cancellationToken);
        var queue = services.GetRequiredService<ISmartTaskQueue>();
        var count = 0;
        if (options.Enabled && options.BlogPlanner && options.BlogPostsPerWeek > 0)
        {
            var plan = services.GetRequiredService<IBlogPlanService>();
            count += await plan.PlanAsync(options.BlogPostsPerWeek, cancellationToken);
            foreach (var row in await plan.DueAsync(DateTimeOffset.UtcNow, 2, cancellationToken))
            {
                await queue.EnqueueAsync(SmartTaskKind.BlogArticle, SmartTarget.BlogPlan, row.Id, row.Topic, SmartPriority.Nightly,
                    userName: SmartOrchestrator.Actor, cancellationToken: cancellationToken);
                count++;
            }
        }
        if (options.Enabled)
            count += await RepairContentAsync(services, queue, cancellationToken);
        if (options.Enabled)
            await NotifyReadyAsync(services, DateTimeOffset.UtcNow, cancellationToken);
        if (options.Enabled && options.GrowthAutopilot)
            count += await GrowAsync(services, options.BannerIdeasPerDay, DateTimeOffset.UtcNow, cancellationToken);
        await services.GetRequiredService<ISmartResearchStore>().PruneAsync(cancellationToken);
        await queue.PurgeAsync(cancellationToken);
        return count;
    }

    /// <summary>
    /// دور ۲۳ – «تکمیل پیوسته» (هر ۱۵ دقیقه): سهمی از سقف روزانه را صف می‌کند تا کالاها و پیش‌نویس‌ها شبانه‌روز، بدون دکمهٔ مدیر،
    /// کامل شوند. سهم هر دور = بیشترِ ۳ و (سقف روزانه ÷ ۲۴). مثال: سقف ۱۲۰ ← هر دور حداکثر ۵ کالا (و هرگز بیش از ۱۲۰ در ۲۴ ساعت).
    /// </summary>
    public static async Task<int> ContinuousAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var options = await services.GetRequiredService<ISmartConfigService>().GetAsync(cancellationToken);
        if (!options.Enabled || options.NightlyCompletePerDay <= 0)
            return 0;
        var perRun = Math.Max(3, options.NightlyCompletePerDay / 24);
        return await PlanNightlyCompleteAsync(services, services.GetRequiredService<ISmartTaskQueue>(), options.NightlyCompletePerDay, now, cancellationToken, perRun);
    }

    /// <summary>
    /// دور ۲۳ – هر ساعت: دسته‌بندی هوشمند (کالای بی‌دسته/کلی) و بعد ممیز درخت دسته‌ها. هر دو جدا محافظت می‌شوند تا خطای یکی دیگری را
    /// متوقف نکند. نتیجهٔ مهم (یکی شدن، جابه‌جایی، پنهان شدن) به مدیر اعلان می‌شود.
    /// </summary>
    public static async Task<int> CategorizeHourlyAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var options = await services.GetRequiredService<ISmartConfigService>().GetAsync(cancellationToken);
        if (!options.Enabled)
            return 0;
        var count = await CategorizeAsync(services, services.GetRequiredService<ISmartTaskQueue>(), cancellationToken);
        try
        {
            var report = await services.GetRequiredService<ICategoryTreeService>().AuditAsync(now, cancellationToken);
            await services.GetRequiredService<SmartNotifier>().NotifyTreeAsync(report);
            count += report.Merged + report.Reparented + report.Split + report.Hidden;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            services.GetRequiredService<ILogger<SmartWorker>>().LogWarning(exception, "Category tree audit failed");
        }
        count += await SyncSpecFacetsAsync(services, now, cancellationToken);
        try
        {
            // دور ۲۳ (مورد ۱۲): اثر انگشت عکس‌ها و کنار گذاشتن عکس تکراری همان کالا (کم‌کم، ۱۵۰ عکس در ساعت).
            var (_, removed) = await ImageHashBackfill.RunAsync(services, cancellationToken);
            count += removed;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            services.GetRequiredService<ILogger<SmartWorker>>().LogWarning(exception, "Image hash backfill failed");
        }
        count += await PlanOccasionCouponsAsync(services, now, cancellationToken);
        return count;
    }

    private static long _lastCouponTicks;

    /// <summary>
    /// دور ۲۳ (مورد ۸۲): کوپن مناسبتی غیرفعال + اعلان «آمادهٔ تأیید» (هر ۱۲ ساعت یک بار بررسی؛ تکراری ساخته نمی‌شود).
    /// منطق: <see cref="Fratak_Shop.Core.Services.Marketing.OccasionCoupons"/>. خطا فقط ثبت می‌شود.
    /// </summary>
    public static async Task<int> PlanOccasionCouponsAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var last = Interlocked.Read(ref _lastCouponTicks);
        if (last != 0 && now.UtcTicks - last < TimeSpan.FromHours(12).Ticks)
            return 0;
        Interlocked.Exchange(ref _lastCouponTicks, now.UtcTicks);
        try
        {
            return await services.GetRequiredService<Fratak_Shop.Core.Services.Marketing.IOccasionCouponService>().PlanAsync(now, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            services.GetRequiredService<ILogger<SmartWorker>>().LogWarning(exception, "Occasion coupon planning failed");
            return 0;
        }
    }

    private static long _lastSpecFacetsTicks;

    /// <summary>
    /// دور ۲۳ – فیلتر مشخصات خودکار (<see cref="Fratak_Shop.Core.Services.Products.ISpecFacetService"/>): حداکثر هر ۶ ساعت یک بار
    /// (خواندن مشخصات همهٔ کالاها سبک نیست). فیلتر موجود هرگز تغییر نمی‌کند؛ فقط فیلترهای مفید تازه ساخته می‌شوند. خطا فقط ثبت می‌شود.
    /// </summary>
    public static async Task<int> SyncSpecFacetsAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var last = Interlocked.Read(ref _lastSpecFacetsTicks);
        if (last != 0 && now.UtcTicks - last < TimeSpan.FromHours(6).Ticks)
            return 0;
        Interlocked.Exchange(ref _lastSpecFacetsTicks, now.UtcTicks);
        try
        {
            var report = await services.GetRequiredService<Fratak_Shop.Core.Services.Products.ISpecFacetService>().SyncAsync(cancellationToken);
            return report.Created;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            services.GetRequiredService<ILogger<SmartWorker>>().LogWarning(exception, "Spec facet sync failed");
            return 0;
        }
    }

    /// <summary>
    /// دور ۲۳: ترمیم متن‌های قدیمی که «دسته‌بندی نشده» یا لینک به دستهٔ سیستمی دارند (<see cref="ISmartCatalogService.RepairForbiddenTextAsync"/>)
    /// و صف «تکمیل هوشمند» برای همان کالاها تا متن کامل و درست دوباره نوشته شود. خطا فقط ثبت می‌شود.
    /// </summary>
    public static async Task<int> RepairContentAsync(IServiceProvider services, ISmartTaskQueue queue, CancellationToken cancellationToken)
    {
        try
        {
            var result = await services.GetRequiredService<ISmartCatalogService>().RepairForbiddenTextAsync(100, cancellationToken);
            foreach (var id in result.Products)
            {
                await queue.EnqueueAsync(SmartTaskKind.ProductComplete, SmartTarget.Product, id, "ترمیم متن", SmartPriority.Batch,
                    userName: SmartOrchestrator.Actor, cancellationToken: cancellationToken);
            }
            return result.Total;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            services.GetRequiredService<ILogger<SmartWorker>>().LogWarning(exception, "Smart content repair failed");
            return 0;
        }
    }

    /// <summary>
    /// دور ۲۳: روزی یک بار (اولین دور برنامه‌ریز بعد از ۹ صبح ایران) اگر پیش‌نویس «آمادهٔ انتشار» هست، اعلان با لینک فیلتر همان فهرست.
    /// سیستم هرگز خودش منتشر نمی‌کند.
    /// </summary>
    private static async Task NotifyReadyAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var local = IranClock.ToLocal(now);
            if (local.Hour < 9 || local.Hour >= 15)
                return;
            var count = await services.GetRequiredService<Fratak_Shop.Data.Contexts.FratakShopDbContext>().Products
                .CountAsync(Fratak_Shop.Core.Services.Products.ProductCompleteness.ReadyToPublish, cancellationToken);
            await services.GetRequiredService<SmartNotifier>().NotifyReadyAsync(count);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            services.GetRequiredService<ILogger<SmartWorker>>().LogWarning(exception, "Ready-to-publish digest failed");
        }
    }

    /// <summary>
    /// تکمیل خودکار (بخش F، دور ۲۳ پیوسته): تا سقف ۲۴ ساعت کالای ناقص (پیش‌نویس‌ها اول، بعد پربازدیدها) با اولویت «شبانه»
    /// (پایین‌ترین؛ کار دستی همیشه جلوتر) در صف می‌رود. کالایی که در ۷ روز گذشته تکمیل (یا تلاش) شده دوباره نمی‌آید تا منابع بیرونی و
    /// هاست بی‌دلیل درگیر نشوند. <paramref name="perRun"/> = سقف همین دور (null = همهٔ جای خالی روز).
    /// مثال: سقف ۲۰ و ۱۲ کار در ۲۴ ساعت گذشته ← ۸ کالای تازه (با perRun = ۵ ← فقط ۵).
    /// </summary>
    public static async Task<int> PlanNightlyCompleteAsync(IServiceProvider services, ISmartTaskQueue queue, int perDay, DateTimeOffset now,
        CancellationToken cancellationToken, int? perRun = null)
    {
        var done = (await queue.RecentTargetsAsync(SmartTaskKind.ProductComplete, now.AddHours(-24), SmartPriority.Nightly, cancellationToken)).Count;
        var room = Math.Clamp(perDay, 0, 500) - done;
        if (perRun is { } cap)
            room = Math.Min(room, Math.Max(1, cap));
        if (room <= 0)
            return 0;
        var recent = (await queue.RecentTargetsAsync(SmartTaskKind.ProductComplete, now.AddDays(-7), cancellationToken: cancellationToken)).ToHashSet();
        var candidates = await services.GetRequiredService<ISmartCatalogService>().ListIncompleteAsync(Math.Min(1000, recent.Count + room * 3), cancellationToken);
        var count = 0;
        foreach (var product in candidates.Where(p => !recent.Contains(p.Id)).Take(room))
        {
            await queue.EnqueueAsync(SmartTaskKind.ProductComplete, SmartTarget.Product, product.Id, product.Name, SmartPriority.Nightly,
                userName: SmartOrchestrator.Actor, cancellationToken: cancellationToken);
            count++;
        }
        return count;
    }

    /// <summary>
    /// بازبینی خودکار دسته‌ها (دور ۲۳: هر ساعت): کالاهای «دسته‌بندی نشده»، «سایر» و «دستهٔ کلی» دستهٔ دقیق می‌گیرند (حتی تک‌کالا) و برای
    /// هر دستهٔ تازه «تکمیل دسته» (توضیح، پرسش‌های رایج، تصویر) با اولویت شبانه صف می‌شود. خطا فقط ثبت می‌شود.
    /// </summary>
    private static async Task<int> CategorizeAsync(IServiceProvider services, ISmartTaskQueue queue, CancellationToken cancellationToken)
    {
        try
        {
            var report = await services.GetRequiredService<ISmartCategoryService>().RunAsync(null, 300, cancellationToken);
            foreach (var category in report.Created)
            {
                await queue.EnqueueAsync(SmartTaskKind.CategoryComplete, SmartTarget.Category, category.Id, category.Name, SmartPriority.Nightly,
                    userName: SmartOrchestrator.Actor, cancellationToken: cancellationToken);
            }
            await services.GetRequiredService<SmartNotifier>().NotifyCategoriesAsync(report);
            return report.Moved;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            services.GetRequiredService<ILogger<SmartWorker>>().LogWarning(exception, "Smart categorization failed");
            return 0;
        }
    }

    /// <summary>
    /// دور ۲۱: «رشد خودکار» روزانه (هر ۶ ساعت بررسی می‌شود؛ در هر روز ایران فقط یک بار واقعاً اجرا می‌شود).
    /// <list type="bullet">
    /// <item>بازدید دیروز با میانهٔ روزهای قبل مقایسه می‌شود؛ سئوی ضعیف‌ترین صفحه‌ها بالا می‌رود و کالاهای پربازدیدِ ناقص به صف تکمیل می‌روند.</item>
    /// <item>اگر بازدید کم بود: کار بیشتر (صفحهٔ بیشتر، مقالهٔ بعدی جلوتر) و ایدهٔ بنر دو برابر – ولی بنر هرگز بدون تأیید مدیر منتشر نمی‌شود.</item>
    /// <item>ایده‌های بنر از دادهٔ واقعی (تخفیف‌ها، پرفروش‌ها، تازه‌ها، مناسبت‌ها) ساخته و در «طراحی ← پیشنهادها» منتظر تأیید می‌مانند.</item>
    /// <item>نتیجه در جدول GrowthActions ثبت و به ادمین اعلان می‌شود (پنل ← رشد خودکار). خطا فقط ثبت می‌شود و بقیهٔ برنامه‌ریزی ادامه دارد.</item>
    /// </list>
    /// <para>force = دکمهٔ «اجرای دوباره الان» در صفحهٔ رشد خودکار (حتی اگر امروز اجرا شده باشد).</para>
    /// مثال: دیروز ۴۰ بازدید و میانهٔ معمول ۱۲۰ ← «بازدید کم» ← ۲۰ صفحه بهبود سئو + ۶ کالا در صف + ۶ ایدهٔ بنر.
    /// </summary>
    public static async Task<int> GrowAsync(IServiceProvider services, int bannerIdeasPerDay, DateTimeOffset now, CancellationToken cancellationToken,
        bool force = false)
    {
        try
        {
            var growth = services.GetRequiredService<IGrowthAutopilot>();
            var report = await growth.RunDailyAsync(now, force, cancellationToken);
            if (!report.Ran)
                return 0;
            var ideas = 0;
            var wanted = report.BannerIdeas(bannerIdeasPerDay);
            if (wanted > 0)
            {
                try
                {
                    ideas = await services.GetRequiredService<Fratak_Shop.Web.Infrastructure.Design.DesignStudio>().SuggestIdeasAsync(wanted, cancellationToken);
                    if (ideas > 0)
                        await growth.LogAsync(IranClock.Today(now), GrowthActionKind.BannerIdeas, ideas, null,
                            $"{PersianText.Digits(ideas)} ایدهٔ بنر تازه منتظر تأیید است.", cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    services.GetRequiredService<ILogger<SmartWorker>>().LogWarning(exception, "Banner ideas failed");
                }
            }
            await services.GetRequiredService<SmartNotifier>().NotifyGrowthAsync(report, ideas);
            return report.SeoLifted + report.ProductsQueued + ideas;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            services.GetRequiredService<ILogger<SmartWorker>>().LogWarning(exception, "Growth autopilot failed");
            return 0;
        }
    }

    private async Task<int> ProcessOneAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var options = await services.GetRequiredService<ISmartConfigService>().GetAsync(cancellationToken);
        if (!options.Enabled)
            return 0;
        var queue = services.GetRequiredService<ISmartTaskQueue>();
        var task = await queue.ClaimNextAsync(_workerId, Lease, cancellationToken);
        if (task is null)
            return 0;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Lease - TimeSpan.FromMinutes(1));
            var outcome = await services.GetRequiredService<SmartOrchestrator>().RunAsync(task, timeout.Token);
            await queue.CompleteAsync(task.Id, outcome.Status, outcome.Summary, outcome.ResultJson, cancellationToken: cancellationToken);
            if (options.NotifyResults)
                await services.GetRequiredService<SmartNotifier>().NotifyAsync(task, outcome.Status, outcome.Summary, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // خاموش شدن برنامه: کار با پایان اجاره دوباره در صف قرار می‌گیرد.
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Smart task {Task} ({Kind}) failed", task.Id, task.Kind);
            var message = exception is OperationCanceledException ? "زمان کار تمام شد (منابع بیرونی کند بودند)." : "خطا: " + exception.Message;
            await queue.RetryLaterAsync(task.Id, TimeSpan.FromMinutes(15 * Math.Max(1, task.Attempts)), message, CancellationToken.None);
            // شکست نهایی (بعد از ۳ تلاش) هم نتیجه است و باید به ادمین برسد؛ تلاش‌های میانی بی‌صدا.
            if (options.NotifyResults && task.Attempts >= SmartTaskQueue.MaxAttempts)
                await services.GetRequiredService<SmartNotifier>().NotifyAsync(task, SmartTaskStatus.Failed, message, CancellationToken.None);
        }
        return 1;
    }
}
