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
    public const string ReadinessJobName = "حلقهٔ آماده‌سازی کالاهای واردشده";

    private static readonly TimeSpan BusyDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ReminderEvery = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PlannerEvery = TimeSpan.FromHours(6);
    private static readonly TimeSpan IntakeEvery = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan ContinuousEvery = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CategorizeEvery = TimeSpan.FromHours(1);
    private static readonly TimeSpan ReadinessEvery = TimeSpan.FromMinutes(10);

    private readonly string _workerId = Environment.MachineName + ":" + Environment.ProcessId;
    private DateTimeOffset _lastReminders = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPlanner = DateTimeOffset.MinValue;
    private DateTimeOffset _lastIntake = DateTimeOffset.MinValue;
    private DateTimeOffset _lastContinuous = DateTimeOffset.MinValue;
    private DateTimeOffset _lastReadiness = DateTimeOffset.MinValue;
    private DateTimeOffset _lastCategorize = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        JobMonitor.Register(JobName, TimeSpan.FromHours(12));
        JobMonitor.Register(ReminderJobName, TimeSpan.FromHours(2));
        JobMonitor.Register(PlannerJobName, TimeSpan.FromHours(12));
        JobMonitor.Register(IntakeJobName, TimeSpan.FromHours(1));
        JobMonitor.Register(ContinuousJobName, TimeSpan.FromHours(1));
        JobMonitor.Register(CategorizeJobName, TimeSpan.FromHours(3));
        JobMonitor.Register(ReadinessJobName, TimeSpan.FromHours(1));
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
                if (now - _lastReadiness >= ReadinessEvery)
                {
                    _lastReadiness = now;
                    await RunScopedAsync(ReadinessJobName, sp => ReadinessAsync(sp, DateTimeOffset.UtcNow, stoppingToken));
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
    /// دور ۲۲: یک دور «ورود هوشمند کالا». دستهٔ کالا در سایت‌های دیگر با <see cref="SmartSources.MarketCategoryAsync"/> پرسیده می‌شود (دور ۲۶: رأی اکثریت دیجی‌کالا، ترب و باسلام)
    /// (مؤدبانه و کش‌شده). کالای بی‌دسته‌مانده ← اعلان به ادمین با لینک فهرست «بدون دسته». خطا فقط ثبت می‌شود.
    /// </summary>
    public static async Task<int> IntakeAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var sources = services.GetRequiredService<SmartSources>();
            var report = await services.GetRequiredService<IProductIntakeService>()
                .RunAsync(now, sources.MarketCategoryAsync, null, ProductIntakeService.DefaultTake, cancellationToken);
            await services.GetRequiredService<SmartNotifier>().NotifyIntakeAsync(report);
            return report.Examined + report.Seo;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            services.GetRequiredService<ILogger<SmartWorker>>().LogWarning(exception, "Smart product intake failed");
            return 0;
        }
    }

    /// <summary>
    /// دور ۲۶ – «حلقهٔ آماده‌سازی» (هر ۱۰ دقیقه): خواستهٔ کاربر این است که هیچ کالای واردشده‌ای به دست انسان نیاز نداشته باشد؛
    /// شنوندهٔ هوشمند آن‌قدر کامل می‌کند (دسته، مشخصات، توضیحات، عکس، نام انگلیسی…) تا «آمادهٔ انتشار» شود و مدیر فقط بازبینی و منتشر کند.
    /// <para>
    /// هر دور حداکثر ۱۰ پیش‌نویس ناقص (هرگز منتشرنشده – دور ۲۸: چه با «ورود هوشمند» آمده باشد چه دستی ساخته شده باشد)؛
    /// ۵ تلاش اول با فاصلهٔ ۶ ساعت و بعد از آن بدون سقف، روزی یک بار (<see cref="ISmartCatalogService.ClaimReadinessCandidatesAsync"/>).
    /// دکمهٔ «آماده‌سازی دوباره همه» در فهرست کالاها شمارنده را صفر می‌کند. «تکمیل هوشمند» با نشانهٔ manual صف می‌شود تا دستیار هرگز
    /// «نیاز نیست» نگوید و هر بخش ناقص را واقعاً انجام دهد. فقط وقتی دستیار در تنظیمات روشن است. خطا فقط ثبت می‌شود.
    /// </para>
    /// </summary>
    public static async Task<int> ReadinessAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var options = await services.GetRequiredService<ISmartConfigService>().GetAsync(cancellationToken);
            if (!options.Enabled)
                return 0;
            var queue = services.GetRequiredService<ISmartTaskQueue>();
            var count = 0;
            foreach (var (id, name) in await services.GetRequiredService<ISmartCatalogService>().ClaimReadinessCandidatesAsync(now, 10, cancellationToken))
            {
                await queue.EnqueueAsync(SmartTaskKind.ProductComplete, SmartTarget.Product, id, name, SmartPriority.Batch,
                    "{\"manual\":1}", null, SmartOrchestrator.Actor, cancellationToken);
                count++;
            }
            return count;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            services.GetRequiredService<ILogger<SmartWorker>>().LogWarning(exception, "Smart readiness loop failed");
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
        try
        {
            // دور ۲۹ (#۵ و #۶): پاک کردن لوگو/واترمارک عکس‌ها و یکدست کردن عکس اصلی (کم‌کم، هر ساعت).
            var care = await ProductImageCare.RunAsync(services, cancellationToken);
            count += care.Cleaned + care.Removed + care.Cutouts;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            services.GetRequiredService<ILogger<SmartWorker>>().LogWarning(exception, "Product image care failed");
        }
        count += await TopUpProductImagesAsync(services, now, cancellationToken);
        count += await SuggestVideosAsync(services, now, cancellationToken);
        count += await RecategorizeAsync(services, now, cancellationToken);
        count += await PlanOccasionCouponsAsync(services, now, cancellationToken);
        count += await RefreshOldPostsAsync(services, now, cancellationToken);
        count += await PurgeCategoryCollagesAsync(services, now, cancellationToken);
        count += await BackfillCategoryPhotosAsync(services, now, cancellationToken);
        count += await BackfillPostCoversAsync(services, now, cancellationToken);
        return count;
    }

    private static long _lastPostCoverTicks;

    /// <summary>
    /// دور ۳۰ (#۶): «کاور هوشمند» برای مقاله‌های بدون تصویر (نوشتهٔ دستی یا مقاله‌ای که موقع نوشتن کاورش ساخته نشد).
    /// هر ۶ ساعت حداکثر ۳ مقاله؛ مقاله‌ای که در ۳ روز گذشته تلاش شده دوباره نمی‌آید. خطا فقط ثبت می‌شود.
    /// </summary>
    public static async Task<int> BackfillPostCoversAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken, bool force = false)
    {
        var last = Interlocked.Read(ref _lastPostCoverTicks);
        if (!force && last != 0 && now.UtcTicks - last < TimeSpan.FromHours(6).Ticks)
            return 0;
        Interlocked.Exchange(ref _lastPostCoverTicks, now.UtcTicks);
        try
        {
            var queue = services.GetRequiredService<ISmartTaskQueue>();
            var recent = (await queue.RecentTargetsAsync(SmartTaskKind.BlogImage, now.AddDays(-3), cancellationToken: cancellationToken)).ToHashSet();
            var count = 0;
            foreach (var (id, title) in await services.GetRequiredService<ISmartCatalogService>().ListPostsWithoutImageAsync(20, cancellationToken))
            {
                if (recent.Contains(id))
                    continue;
                await queue.EnqueueAsync(SmartTaskKind.BlogImage, SmartTarget.BlogPost, id, title, SmartPriority.Nightly,
                    userName: SmartOrchestrator.Actor, cancellationToken: cancellationToken);
                if (++count >= 3)
                    break;
            }
            return count;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            services.GetRequiredService<ILogger<SmartWorker>>().LogWarning(exception, "Post cover backfill failed");
            return 0;
        }
    }

    private static long _lastVideoTicks;

    /// <summary>
    /// دور ۲۹ (#۱۸): «ویدیوی پیشنهادی» – هر ۶ ساعت ۸ کالای بی‌ویدیو (روزی ~۳۲؛ هر کالا هر ۳۰ روز یک بار) در صفحهٔ تأمین‌کننده، آپارات و یوتیوب
    /// جست‌وجو می‌شوند و ویدیوهای «همین کالا» با یک اعلان قابل کلیک به مدیر پیشنهاد می‌شوند. هیچ ویدیویی خودکار اضافه نمی‌شود.
    /// </summary>
    public static async Task<int> SuggestVideosAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken, bool force = false)
    {
        var last = Interlocked.Read(ref _lastVideoTicks);
        if (!force && last != 0 && now.UtcTicks - last < TimeSpan.FromHours(6).Ticks)
            return 0;
        Interlocked.Exchange(ref _lastVideoTicks, now.UtcTicks);
        try
        {
            var options = await services.GetRequiredService<ISmartConfigService>().GetAsync(cancellationToken);
            if (!options.Enabled || !options.VideoSuggestions)
                return 0;
            var (saved, products) = await services.GetRequiredService<VideoSuggestionFinder>().RunAsync(8, cancellationToken);
            await services.GetRequiredService<SmartNotifier>().NotifyVideoSuggestionsAsync(saved, products);
            return saved;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            services.GetRequiredService<ILogger<SmartWorker>>().LogWarning(exception, "Video suggestions failed");
            return 0;
        }
    }

    private static long _lastRecategorizeTicks;

    /// <summary>
    /// دور ۲۹ (#۱۰): روزی یک بار «بازبینی دستهٔ کالاها» (تنظیم Recategorize): کالای ناجور (مثل «سرویس ظرف» در «چندراهی برق») به دستهٔ
    /// درست می‌رود (حداکثر ۲۵ کالا در روز) و نتیجه با یک اعلان قابل کلیک به کارکنان گزارش می‌شود. سبک و بدون شبکه.
    /// </summary>
    public static async Task<int> RecategorizeAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken, bool force = false)
    {
        var last = Interlocked.Read(ref _lastRecategorizeTicks);
        if (!force && last != 0 && now.UtcTicks - last < TimeSpan.FromHours(24).Ticks)
            return 0;
        Interlocked.Exchange(ref _lastRecategorizeTicks, now.UtcTicks);
        try
        {
            var options = await services.GetRequiredService<ISmartConfigService>().GetAsync(cancellationToken);
            if (!options.Enabled || !options.Recategorize)
                return 0;
            var report = await services.GetRequiredService<ISmartCategoryService>().AuditAsync(25, cancellationToken,
                services.GetRequiredService<SmartSources>().MarketCategoryAsync);
            await services.GetRequiredService<SmartNotifier>().NotifyRecategorizedAsync(report);
            return report.Moved.Count;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            services.GetRequiredService<ILogger<SmartWorker>>().LogWarning(exception, "Recategorize audit failed");
            return 0;
        }
    }

    private static long _lastImageTopUpTicks;

    /// <summary>
    /// دور ۲۹ (#۹): «حداقل عکس هر کالا» – روزی یک بار کالاهایی که کمتر از MinProductImages (پیش‌فرض ۵) عکس دارند در صف
    /// «عکس هوشمند» می‌روند (حالت topup: فقط عکس‌های «همان کالا»؛ مشابه و نامرتبط کنار گذاشته می‌شوند).
    /// هر روز حداکثر ۱۲ کالا (مؤدب با منابع بیرونی)؛ کالایی که در ۳ روز گذشته تلاش شده نمی‌آید. خطا فقط ثبت می‌شود.
    /// </summary>
    public static async Task<int> TopUpProductImagesAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken, bool force = false)
    {
        var last = Interlocked.Read(ref _lastImageTopUpTicks);
        if (!force && last != 0 && now.UtcTicks - last < TimeSpan.FromHours(24).Ticks)
            return 0;
        Interlocked.Exchange(ref _lastImageTopUpTicks, now.UtcTicks);
        try
        {
            var options = await services.GetRequiredService<ISmartConfigService>().GetAsync(cancellationToken);
            if (!options.Enabled)
                return 0;
            var queue = services.GetRequiredService<ISmartTaskQueue>();
            var recent = (await queue.RecentTargetsAsync(SmartTaskKind.ProductImages, now.AddDays(-3), cancellationToken: cancellationToken)).ToHashSet();
            var context = services.GetRequiredService<Fratak_Shop.Data.Contexts.FratakShopDbContext>();
            var min = options.MinProductImages;
            var rows = await context.Products
                .Where(p => !p.Category.IsSystem && p.Images.Count() < min)
                .OrderByDescending(p => p.IsActive).ThenBy(p => p.Images.Count()).ThenByDescending(p => p.CreatedAt)
                .Select(p => new { p.Id, p.Name })
                .Take(60)
                .ToListAsync(cancellationToken);
            var count = 0;
            foreach (var row in rows)
            {
                if (recent.Contains(row.Id))
                    continue;
                await queue.EnqueueAsync(SmartTaskKind.ProductImages, SmartTarget.Product, row.Id, row.Name, SmartPriority.Nightly,
                    SmartOrchestrator.TopUpPayload, null, SmartOrchestrator.Actor, cancellationToken);
                if (++count >= 12)
                    break;
            }
            return count;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            services.GetRequiredService<ILogger<SmartWorker>>().LogWarning(exception, "Product image top-up failed");
            return 0;
        }
    }

    private static long _lastCollageSweepTicks;

    /// <summary>
    /// دور ۲۹ (#۸): «حلقهٔ بازبینی تصویر دسته‌ها» – روزی یک بار همهٔ تصویرهای پیشنهادی دسته‌ها بررسی می‌شوند و کلاژ عکس کالاها
    /// (<see cref="CategoryComposite.IsCollage(byte[])"/>) کنار گذاشته می‌شود؛ دسته دوباره در صف جست‌وجوی عکس درست می‌رود
    /// و تا آن موقع صفحهٔ اصلی آیکن دسته را نشان می‌دهد. تصویری که ادمین خودش آپلود کرده (خارج از فهرست پیشنهادها) دست نمی‌خورد.
    /// سبک: تعداد دسته‌ها کم است و هر فایل فقط یک بار باز می‌شود. خروجی: تعداد تصویر حذف‌شده.
    /// </summary>
    public static async Task<int> PurgeCategoryCollagesAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken, bool force = false)
    {
        var last = Interlocked.Read(ref _lastCollageSweepTicks);
        if (!force && last != 0 && now.UtcTicks - last < TimeSpan.FromHours(24).Ticks)
            return 0;
        Interlocked.Exchange(ref _lastCollageSweepTicks, now.UtcTicks);
        try
        {
            var catalog = services.GetRequiredService<ISmartCatalogService>();
            var uploads = services.GetRequiredService<Fratak_Shop.Web.Services.IUploadStore>();
            var removed = 0;
            foreach (var (id, state) in await catalog.ListCategoryImagesAsync(cancellationToken))
            {
                var bad = new List<string>();
                foreach (var name in state.Options ?? [])
                {
                    var path = uploads.GetPath(Fratak_Shop.Web.Services.UploadArea.Categories, Path.GetFileName(name));
                    // فایل گم‌شده کاری ندارد (ممکن است در حال انتقال/بازگردانی باشد)؛ فقط کلاژ کنار گذاشته می‌شود.
                    if (path is null || !File.Exists(path))
                        continue;
                    if (CategoryComposite.IsCollage(await File.ReadAllBytesAsync(path, cancellationToken)))
                        bad.Add(name);
                }
                if (bad.Count == 0)
                    continue;
                var dropped = await catalog.DropCategoryImagesAsync(id, bad, cancellationToken);
                if (dropped.Count > 0)
                {
                    services.GetRequiredService<SmartImageFinder>().DeleteCategoryFiles(dropped);
                    removed += dropped.Count;
                }
            }
            if (removed > 0)
                services.GetRequiredService<ILogger<SmartWorker>>().LogInformation("Category image sweep removed {Count} collage/missing images", removed);
            return removed;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            services.GetRequiredService<ILogger<SmartWorker>>().LogWarning(exception, "Category collage sweep failed");
            return 0;
        }
    }

    private static long _lastCategoryPhotoTicks;

    /// <summary>
    /// دور ۲۶: «عکس فضاسازی دسته از اینترنت» برای دسته‌های قدیمی که هنوز فقط بنرِ ساخته‌شده از عکس کالا دارند یا اصلاً تصویر ندارند.
    /// هر ۶ ساعت حداکثر ۳ دسته (مؤدب با منابع بیرونی)؛ دسته‌ای که در ۳ روز گذشته تلاش شده دوباره نمی‌آید. خطا فقط ثبت می‌شود.
    /// </summary>
    public static async Task<int> BackfillCategoryPhotosAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken, bool force = false)
    {
        var last = Interlocked.Read(ref _lastCategoryPhotoTicks);
        if (!force && last != 0 && now.UtcTicks - last < TimeSpan.FromHours(6).Ticks)
            return 0;
        Interlocked.Exchange(ref _lastCategoryPhotoTicks, now.UtcTicks);
        try
        {
            var queue = services.GetRequiredService<ISmartTaskQueue>();
            var recent = (await queue.RecentTargetsAsync(SmartTaskKind.CategoryComplete, now.AddDays(-3), cancellationToken: cancellationToken)).ToHashSet();
            var count = 0;
            foreach (var (id, name) in await services.GetRequiredService<ISmartCatalogService>().ListCategoriesNeedingPhotosAsync(20, cancellationToken))
            {
                if (recent.Contains(id))
                    continue;
                await queue.EnqueueAsync(SmartTaskKind.CategoryComplete, SmartTarget.Category, id, name, SmartPriority.Nightly,
                    userName: SmartOrchestrator.Actor, cancellationToken: cancellationToken);
                if (++count >= 3)
                    break;
            }
            return count;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            services.GetRequiredService<ILogger<SmartWorker>>().LogWarning(exception, "Category photo backfill failed");
            return 0;
        }
    }

    private static long _lastBlogRefreshTicks;

    /// <summary>
    /// دور ۲۳ (مورد ۳۶): به‌روزرسانی مقاله‌های قدیمی که کالاهایشان ناموجود شده (روزی یک بار، حداکثر ۳ مقاله).
    /// منطق: <see cref="Fratak_Shop.Core.Services.Blogs.BlogRefresh"/>. خطا فقط ثبت می‌شود.
    /// </summary>
    public static async Task<int> RefreshOldPostsAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var last = Interlocked.Read(ref _lastBlogRefreshTicks);
        if (last != 0 && now.UtcTicks - last < TimeSpan.FromHours(24).Ticks)
            return 0;
        Interlocked.Exchange(ref _lastBlogRefreshTicks, now.UtcTicks);
        try
        {
            return await services.GetRequiredService<Fratak_Shop.Core.Services.Blogs.IBlogRefreshService>().RefreshAsync(now, 3, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            services.GetRequiredService<ILogger<SmartWorker>>().LogWarning(exception, "Old blog post refresh failed");
            return 0;
        }
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
            // دور ۲۹ (#۱۷): سربرگ برای همهٔ دسته‌های اصلی (روزی حداکثر ۴ پیشنهاد تازه تا همه پوشش داده شوند؛ منتظر تأیید مدیر).
            try
            {
                var headers = await services.GetRequiredService<Fratak_Shop.Web.Infrastructure.Design.DesignStudio>().SuggestCategoryHeadersAsync(4, cancellationToken);
                if (headers > 0)
                {
                    ideas += headers;
                    await growth.LogAsync(IranClock.Today(now), GrowthActionKind.BannerIdeas, headers, null,
                        $"{PersianText.Digits(headers)} بنر سربرگ دسته منتظر تأیید است.", cancellationToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                services.GetRequiredService<ILogger<SmartWorker>>().LogWarning(exception, "Category header ideas failed");
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
