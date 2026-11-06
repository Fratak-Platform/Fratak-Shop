using System.Diagnostics;

namespace Fratak_Shop.Web.Infrastructure.Ops;

// ═══════════════════════════════════════════════════════════════════════════════════════════════
//  دور نوزدهم (A4–A12، A25): «مدیر منابع» برنامه روی هاست اشتراکی ضعیف.
//
//  مشکل: روی هاست اشتراکی (Plesk/IIS) چند سایت یک سرور را شریک‌اند و استخر برنامه سقف حافظه/CPU دارد.
//  اگر کارهای سنگین پس‌زمینه (بررسی تأمین‌کننده، قیمت رقبا، رهگیری پست با مدل ONNX، سئوی شبانه، پشتیبان‌گیری)
//  هم‌زمان با موج بازدید اجرا شوند، حافظه از سقف می‌گذرد ← IIS پروسه را می‌کشد ← چند بار پشت‌سرهم ← ۵۰۳.
//
//  راه‌حل این فایل (بدون هیچ بستهٔ جدید):
//   ۱) پروفایل منابع Low/Normal/High (تنظیم Shop:Resources:Profile) ← بودجهٔ هر چیز (کش صفحه، سقف درخواست هم‌زمان، …).
//   ۲) هر اجرای یک کار پس‌زمینه از JobMonitor.TrackAsync می‌گذرد و آنجا از این کلاس «نوبت» می‌گیرد:
//       • کار حیاتی (ارسال پیامک/ایمیل/اعلان، ثبت آمار) هرگز معطل نمی‌شود؛
//       • کار سبک فقط وقتی فشار «بحرانی» است کمی صبر می‌کند؛
//       • کار سنگین: ۱۰ دقیقهٔ اول بعد از روشن شدن اجرا نمی‌شود، هر بار فقط یکی، و تا فشار پایین نیاید صبر می‌کند.
//   ۳) هر چند ثانیه وضعیت حافظه/CPU/صف درخواست‌ها نمونه‌برداری می‌شود (ResourceMonitorService) و «سطح فشار» محاسبه می‌شود.
//   ۴) اگر حافظه مدت طولانی در خط قرمز بماند، در ساعت خلوت شب یک ری‌استارت کنترل‌شده انجام می‌شود (بهتر از کشته شدن ناگهانی).
//
//  کجا استفاده می‌شود؟ JobMonitor.TrackAsync، ResourceMonitorService، LoadSheddingMiddleware، صفحهٔ «فنی ← منابع سرور».
// ═══════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// پروفایل منابع میزبان. مقدار پیش‌فرض Low است (امن برای ضعیف‌ترین هاست اشتراکی).
/// مثال appsettings: <c>"Shop": { "Resources": { "Profile": "Normal" } }</c> یا متغیر محیطی <c>Shop__Resources__Profile=High</c>.
/// </summary>
public enum ResourceProfile
{
    /// <summary>هاست اشتراکی ضعیف (۵۱۲MB تا ۱GB حافظه، سهم کم از CPU). پیش‌فرض.</summary>
    Low = 0,

    /// <summary>هاست اشتراکی خوب یا VPS کوچک (۲GB).</summary>
    Normal = 1,

    /// <summary>سرور اختصاصی/VPS قوی.</summary>
    High = 2,
}

/// <summary>
/// وزن یک کار پس‌زمینه. محل تعیین: <see cref="JobWeights.Of"/>.
/// </summary>
public enum JobWeight
{
    /// <summary>حیاتی: هرگز معطل نمی‌شود (صف پیامک/ایمیل/پوش، ثبت آمار، لغو سفارش‌های پرداخت‌نشده).</summary>
    Critical = 0,

    /// <summary>سبک: فقط هنگام فشار بحرانی حداکثر چند دقیقه صبر می‌کند.</summary>
    Light = 1,

    /// <summary>سنگین: دورهٔ آرام شروع، یکی‌یکی، و فقط وقتی فشار پایین است.</summary>
    Heavy = 2,
}

/// <summary>سطح فشار منابع؛ هر چه بالاتر، کارهای بیشتری عقب می‌افتند. محاسبه: <see cref="ResourceGovernor.Evaluate"/>.</summary>
public enum PressureLevel
{
    Normal = 0,
    Elevated = 1,
    High = 2,
    Critical = 3,
}

/// <summary>
/// تنظیمات بخش <c>Shop:Resources</c>. همهٔ مقدارها اختیاری‌اند؛ صفر/منفی یعنی «از پروفایل بگیر».
/// <para>مثال (هاست ۱ گیگ با سقف حافظهٔ استخر ۸۰۰MB):
/// <c>"Resources": { "Profile": "Low", "MemoryLimitMb": 800 }</c></para>
/// </summary>
public sealed class ResourceOptions
{
    public const string Section = "Shop:Resources";

    /// <summary>پروفایل؛ پیش‌فرض Low.</summary>
    public ResourceProfile Profile { get; set; } = ResourceProfile.Low;

    /// <summary>سقف حافظهٔ استخر برنامه به مگابایت (Private Memory Limit در IIS). صفر = خودکار (کمینهٔ حافظهٔ در دسترس و سقف پروفایل).</summary>
    public int MemoryLimitMb { get; set; }

    /// <summary>سقف درخواست‌های هم‌زمان در حال پردازش. صفر = از پروفایل.</summary>
    public int MaxConcurrentRequests { get; set; }

    /// <summary>دقیقه‌های «شروع آرام» (کار سنگین اجرا نمی‌شود). منفی = از پروفایل؛ صفر = بدون مکث (آزمون‌ها).</summary>
    public int StartupQuietMinutes { get; set; } = -1;

    /// <summary>ری‌استارت کنترل‌شده هنگام ماندن طولانی حافظه در خط قرمز (A25). پیش‌فرض روشن.</summary>
    public bool SelfRestart { get; set; } = true;

    /// <summary>شروع ساعت خلوت (به وقت ایران). پیش‌فرض ۳ بامداد.</summary>
    public int QuietHourStart { get; set; } = 3;

    /// <summary>پایان ساعت خلوت (به وقت ایران، انحصاری). پیش‌فرض ۶ صبح.</summary>
    public int QuietHourEnd { get; set; } = 6;

    /// <summary>
    /// شروع «ساعت‌های شلوغ» سایت (به وقت ایران)؛ کارهای سنگین غیرفوری (سئو، گزارش‌ها) در این ساعت‌ها فقط وقتی اجرا می‌شوند که
    /// فشار منابع «عادی» باشد (A11). پیش‌فرض ۹ صبح.
    /// </summary>
    public int BusyHourStart { get; set; } = 9;

    /// <summary>پایان ساعت‌های شلوغ (انحصاری). پیش‌فرض ۲۴ (نیمه‌شب).</summary>
    public int BusyHourEnd { get; set; } = 24;

    /// <summary>ذخیرهٔ «آخرین نسخهٔ سالم» صفحه‌های عمومی روی دیسک برای روز مبادا (A13). پیش‌فرض روشن.</summary>
    public bool PageSnapshots { get; set; } = true;

    /// <summary>استخر DbContext (A16). پیش‌فرض روشن؛ فقط برای عیب‌یابی خاموش کنید.</summary>
    public bool DbContextPooling { get; set; } = true;

    /// <summary>
    /// سقف هیپ GC = ۵۵٪ سقف حافظهٔ برنامه (A4، <see cref="MemoryLimits"/>). پیش‌فرض روشن: GC پیش از رسیدن به سقف استخر/کانتینر
    /// جمع‌آوری می‌کند و جا برای حافظهٔ غیرمدیریت‌شده می‌ماند. پروفایل High بدون سقف دستی اثری ندارد.
    /// </summary>
    public bool LimitGcHeap { get; set; } = true;

    /// <summary>
    /// سهم هیپ GC از سقف حافظهٔ برنامه به درصد (۳۰ تا ۷۵). صفر = پیش‌فرض (۵۵٪). مثال: <c>Shop__Resources__HeapPercent=45</c> روی هاست
    /// ۵۱۲ مگ ← سقف هیپ ۲۳۰ مگ (جای بیشتر برای حافظهٔ غیرمدیریت‌شده). استفاده: <see cref="MemoryLimits.Apply"/>.
    /// </summary>
    public int HeapPercent { get; set; }
}

/// <summary>
/// بودجهٔ منابع یک پروفایل. هر عدد اینجا یک «سقف» است تا برنامه هرگز از سهم خود بیشتر نخواهد.
/// <para>مثال: <c>ResourceBudget.For(ResourceProfile.Low).OutputCacheMb</c> → 48.</para>
/// </summary>
/// <param name="Profile">پروفایل.</param>
/// <param name="MemoryLimitMb">سقف فرضی حافظه وقتی تنظیم نشده (مگابایت).</param>
/// <param name="OutputCacheMb">سقف کش صفحه‌های عمومی در حافظه.</param>
/// <param name="MaxConcurrentRequests">درخواست هم‌زمان در حال پردازش؛ بیشتر از این در صف کوتاه می‌ماند.</param>
/// <param name="QueueLength">حداکثر درخواست منتظر در صف؛ بیشتر از این ← نسخهٔ ذخیره‌شده یا پاسخ سریع «کمی بعد».</param>
/// <param name="QueueWait">حداکثر انتظار یک درخواست در صف.</param>
/// <param name="HeavySlots">چند کار سنگین هم‌زمان.</param>
/// <param name="StartupQuiet">دورهٔ آرام بعد از روشن شدن.</param>
/// <param name="DbContextPoolSize">اندازهٔ استخر DbContext.</param>
/// <param name="SqlMaxPoolSize">سقف اتصال‌های SQL (Max Pool Size) وقتی در رشتهٔ اتصال نیامده.</param>
/// <param name="CustomerRealtime">اتصال زندهٔ (SignalR) مشتریان؛ در Low فقط کارکنان (A21).</param>
/// <param name="SnapshotFiles">حداکثر فایل‌های «آخرین نسخهٔ سالم» صفحه‌ها.</param>
public sealed record ResourceBudget(
    ResourceProfile Profile,
    int MemoryLimitMb,
    int OutputCacheMb,
    int MaxConcurrentRequests,
    int QueueLength,
    TimeSpan QueueWait,
    int HeavySlots,
    TimeSpan StartupQuiet,
    int DbContextPoolSize,
    int SqlMaxPoolSize,
    bool CustomerRealtime,
    int SnapshotFiles)
{
    /// <summary>بودجهٔ پیش‌فرض هر پروفایل. مثال: <c>ResourceBudget.For(ResourceProfile.High).HeavySlots</c> → 2.</summary>
    public static ResourceBudget For(ResourceProfile profile) => profile switch
    {
        ResourceProfile.High => new(profile, 0, 256, 256, 512, TimeSpan.FromSeconds(5), 2, TimeSpan.FromMinutes(2), 128, 200, true, 5000),
        ResourceProfile.Normal => new(profile, 2048, 128, 96, 192, TimeSpan.FromSeconds(5), 1, TimeSpan.FromMinutes(5), 64, 100, true, 3000),
        // Low: ۸ صفحهٔ هم‌زمان کافی است (روی هاست ضعیف بیشتر فقط حافظه می‌خورد و سریع‌تر نمی‌شود؛ صفحه‌های کش‌شده چند
        // میلی‌ثانیه‌ای جا را آزاد می‌کنند) + صف ۴۸ تایی.
        _ => new(ResourceProfile.Low, 1024, 48, 8, 48, TimeSpan.FromSeconds(4), 1, TimeSpan.FromMinutes(10), 32, 40, false, 1500),
    };

    /// <summary>
    /// بودجهٔ نهایی با اعمال مقدارهای دستی تنظیمات. مثال:
    /// <c>ResourceBudget.From(new ResourceOptions { Profile = ResourceProfile.Low, MaxConcurrentRequests = 50 })</c> → سقف ۵۰.
    /// </summary>
    public static ResourceBudget From(ResourceOptions options)
    {
        var budget = For(options.Profile);
        if (options.MemoryLimitMb > 0)
            budget = budget with { MemoryLimitMb = Math.Clamp(options.MemoryLimitMb, 128, 1024 * 1024) };
        if (options.MaxConcurrentRequests > 0)
            budget = budget with { MaxConcurrentRequests = Math.Clamp(options.MaxConcurrentRequests, 4, 10000), QueueLength = Math.Clamp(options.MaxConcurrentRequests * 2, 8, 20000) };
        if (options.StartupQuietMinutes >= 0)
            budget = budget with { StartupQuiet = TimeSpan.FromMinutes(Math.Min(options.StartupQuietMinutes, 120)) };
        return budget;
    }
}

/// <summary>
/// یک نمونه از وضعیت منابع (هر ۵ ثانیه). مثال: WorkingSetMb=420، LimitMb=1024 → MemoryRatio≈0.41.
/// </summary>
/// <param name="At">زمان نمونه.</param>
/// <param name="MemoryMb">حافظهٔ خصوصی پروسه (مگابایت) – همان عددی که IIS با سقف مقایسه می‌کند.</param>
/// <param name="LimitMb">سقف مؤثر حافظه (مگابایت).</param>
/// <param name="GcHeapMb">حجم هیپ مدیریت‌شده.</param>
/// <param name="CpuRatio">سهم CPU پروسه از کل هسته‌ها در بازهٔ اخیر (۰ تا ۱).</param>
/// <param name="PendingWorkItems">کارهای منتظر در ThreadPool (نشانهٔ گیر افتادن).</param>
/// <param name="ActiveRequests">درخواست‌های در حال پردازش.</param>
/// <param name="QueuedRequests">درخواست‌های منتظر در صف.</param>
/// <param name="Threads">تعداد نخ‌های ThreadPool.</param>
public readonly record struct ResourceSample(
    DateTimeOffset At,
    double MemoryMb,
    double LimitMb,
    double GcHeapMb,
    double CpuRatio,
    long PendingWorkItems,
    int ActiveRequests,
    int QueuedRequests,
    int Threads)
{
    /// <summary>نسبت حافظه به سقف (۰ اگر سقف معلوم نیست).</summary>
    public double MemoryRatio => LimitMb > 0 ? MemoryMb / LimitMb : 0;
}

/// <summary>
/// وزن هر کار پس‌زمینه بر اساس نامش (همان نام JobMonitor). کار ناشناخته «سبک» است.
/// <para>مثال: <c>JobWeights.Of(SupplierSyncWorker.JobName)</c> → Heavy.</para>
/// </summary>
public static class JobWeights
{
    private static readonly Dictionary<string, JobWeight> Map = new(StringComparer.Ordinal)
    {
        // سنگین: شبکهٔ بیرونی زیاد، پردازش تصویر/مدل ONNX یا خواندن کل جدول‌ها.
        [Suppliers.SupplierSyncWorker.JobName] = JobWeight.Heavy,
        [Suppliers.MarketPricingWorker.JobName] = JobWeight.Heavy,
        [Shipping.PostTrackingWorker.JobName] = JobWeight.Heavy,
        [SeoEngine.SeoEngineWorker.JobName] = JobWeight.Heavy,
        [SeoEngine.SeoToolsWorker.LinkJobName] = JobWeight.Heavy,
        [SeoEngine.SeoToolsWorker.GscJobName] = JobWeight.Heavy,
        [Seo.SeoAutopilotWorker.JobName] = JobWeight.Heavy,
        [BackupWorker.JobName] = JobWeight.Heavy,
        [Insights.InsightsWorker.JobName] = JobWeight.Heavy,
        // دور ۱۹: محتوای هوشمند (دانلود عکس، تحلیل تصویر، چند درخواست بیرونی برای هر کالا).
        [Smart.SmartWorker.JobName] = JobWeight.Heavy,

        // حیاتی: صف‌های حافظه‌ای که معطل شدنشان داده از دست می‌دهد یا مشتری را منتظر می‌گذارد.
        [Analytics.AnalyticsFlushWorker.JobName] = JobWeight.Critical,
        [Email.EmailDispatcher.JobName] = JobWeight.Critical,
        [Engagement.WebPushDispatcher.JobName] = JobWeight.Critical,
        [Realtime.StaffAlertDispatcher.JobName] = JobWeight.Critical,
        [OpsWorker.FirewallJobName] = JobWeight.Critical,
        [OpsWorker.AlertsJobName] = JobWeight.Critical,
        ["لغو سفارش‌های پرداخت‌نشده"] = JobWeight.Critical,
    };

    /// <summary>وزن یک کار. مثال: <c>JobWeights.Of("پاک‌سازی لاگ‌های قدیمی")</c> → Light.</summary>
    public static JobWeight Of(string name) => Map.TryGetValue(name, out var weight) ? weight : JobWeight.Light;

    /// <summary>همهٔ نگاشت‌ها (صفحهٔ منابع و آزمون‌ها).</summary>
    public static IReadOnlyDictionary<string, JobWeight> All => Map;

    // کارهای سنگینی که عقب افتادنشان چند ساعت ضرری ندارد (برخلاف قیمت/موجودی تأمین‌کننده یا رهگیری مرسوله که مشتری منتظرش است).
    private static readonly HashSet<string> QuietHourJobs = new(StringComparer.Ordinal)
    {
        SeoEngine.SeoEngineWorker.JobName,
        SeoEngine.SeoToolsWorker.LinkJobName,
        SeoEngine.SeoToolsWorker.GscJobName,
        Seo.SeoAutopilotWorker.JobName,
        Insights.InsightsWorker.JobName,
    };

    /// <summary>
    /// آیا این کار «غیرفوری» است و بهتر است در ساعت خلوت اجرا شود؟ (A11)
    /// مثال: <c>PrefersQuietHours(SeoAutopilotWorker.JobName)</c> → true؛ <c>PrefersQuietHours(SupplierSyncWorker.JobName)</c> → false.
    /// </summary>
    public static bool PrefersQuietHours(string name) => QuietHourJobs.Contains(name);
}

/// <summary>
/// مدیر منابع؛ یک نمونهٔ سراسری (<see cref="Current"/>) که Program.cs با <see cref="Configure"/> می‌سازد.
/// <para>مثال استفاده (درون JobMonitor.TrackAsync):
/// <code>using var lease = await ResourceGovernor.Current.EnterAsync(name, JobWeights.Of(name));</code></para>
/// <para>نمونهٔ پیش‌فرض (قبل از Configure، مثلاً آزمون‌های واحد) هیچ مکثی ندارد.</para>
/// </summary>
public sealed class ResourceGovernor
{
    /// <summary>فاصلهٔ بررسی دوباره هنگام انتظار.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>کار سنگین حداکثر این‌قدر برای پایین آمدن فشار صبر می‌کند؛ بعد «ترمز است نه دیوار» و اجرا می‌شود.</summary>
    public static readonly TimeSpan MaxHeavyPressureWait = TimeSpan.FromMinutes(15);

    /// <summary>کار سبک حداکثر این‌قدر هنگام فشار بحرانی صبر می‌کند.</summary>
    public static readonly TimeSpan MaxLightPressureWait = TimeSpan.FromMinutes(2);

    /// <summary>حداکثر انتظار کار غیرفوری در ساعت شلوغ برای «عادی» شدن فشار؛ بعد از آن قانون معمول کار سنگین (A11).</summary>
    public static readonly TimeSpan MaxBusyHourWait = TimeSpan.FromMinutes(30);

    private static volatile ResourceGovernor _current = new(new ResourceOptions { StartupQuietMinutes = 0, Profile = ResourceProfile.High }, DateTimeOffset.UtcNow, CancellationToken.None, unlimited: true);

    /// <summary>نمونهٔ سراسری فعلی.</summary>
    public static ResourceGovernor Current => _current;

    private readonly SemaphoreSlim _heavySlots;
    private readonly CancellationToken _stopping;
    private readonly bool _unlimited;
    private readonly TimeSpan _poll;
    private ResourceSample _last;
    private PressureLevel _pressure;
    private string? _heavyRunning;
    private long _deferrals;

    /// <summary>
    /// ساخت یک مدیر مستقل (آزمون‌ها). در برنامه از <see cref="Configure"/> استفاده کنید.
    /// </summary>
    /// <param name="options">تنظیمات.</param>
    /// <param name="startedAt">زمان روشن شدن برنامه (برای دورهٔ آرام).</param>
    /// <param name="stopping">توکن خاموش شدن برنامه؛ انتظارها با آن لغو می‌شوند.</param>
    /// <param name="unlimited">true = بدون هیچ محدودیتی (نمونهٔ پیش‌فرض).</param>
    /// <param name="pollInterval">فاصلهٔ بررسی دوباره هنگام انتظار (پیش‌فرض ۵ ثانیه؛ آزمون‌ها کوتاه‌تر).</param>
    public ResourceGovernor(ResourceOptions options, DateTimeOffset startedAt, CancellationToken stopping, bool unlimited = false, TimeSpan? pollInterval = null)
    {
        _poll = pollInterval ?? PollInterval;
        Options = options;
        Budget = ResourceBudget.From(options);
        StartedAt = startedAt;
        _stopping = stopping;
        _unlimited = unlimited;
        _heavySlots = new SemaphoreSlim(Math.Max(1, Budget.HeavySlots));
        RequestSlots = new SemaphoreSlim(Math.Max(1, Budget.MaxConcurrentRequests));
    }

    /// <summary>نمونهٔ «بدون محدودیت» (پیش‌فرض پیش از Configure)؟ آن‌وقت هیچ صف/ترمزی اعمال نمی‌شود.</summary>
    public bool IsUnlimited => _unlimited;

    /// <summary>جای خالی درخواست‌های مهمانِ قابل صف (LoadSheddingMiddleware). ظرفیت = MaxConcurrentRequests.</summary>
    public SemaphoreSlim RequestSlots { get; }

    /// <summary>
    /// ساخت و نصب نمونهٔ سراسری. فقط Program.cs (یک بار در شروع).
    /// مثال: <c>ResourceGovernor.Configure(options, app.Lifetime.ApplicationStopping)</c>.
    /// </summary>
    public static ResourceGovernor Configure(ResourceOptions options, CancellationToken stopping)
    {
        var governor = new ResourceGovernor(options, DateTimeOffset.UtcNow, stopping);
        _current = governor;
        return governor;
    }

    public ResourceOptions Options { get; }

    public ResourceBudget Budget { get; }

    public DateTimeOffset StartedAt { get; }

    /// <summary>سطح فشار فعلی (از آخرین نمونه).</summary>
    public PressureLevel Pressure => _pressure;

    /// <summary>آخرین نمونه (پیش از اولین نمونه‌برداری مقدار پیش‌فرض است).</summary>
    public ResourceSample LastSample => _last;

    /// <summary>نام کار سنگینی که الان در حال اجراست (یا null).</summary>
    public string? HeavyRunning => _heavyRunning;

    /// <summary>چند بار کاری به خاطر فشار/دورهٔ آرام عقب افتاد.</summary>
    public long Deferrals => Interlocked.Read(ref _deferrals);

    /// <summary>
    /// آیا الان (به وقت ایران) جزو ساعت‌های شلوغ سایت است؟ مثال: ساعت ۲۱ با پیش‌فرض ۹ تا ۲۴ → true؛ ساعت ۴ → false.
    /// </summary>
    public bool IsBusyHour(DateTimeOffset now) =>
        SelfRestartPolicy.HourInRange(Options.BusyHourStart, Options.BusyHourEnd, Fratak_Shop.Core.Utilities.IranClock.ToLocal(now).Hour);

    /// <summary>آیا دورهٔ آرام شروع هنوز تمام نشده؟</summary>
    public bool InStartupQuiet(DateTimeOffset now) => !_unlimited && now - StartedAt < Budget.StartupQuiet;

    /// <summary>
    /// ثبت نمونهٔ جدید و محاسبهٔ سطح فشار. فقط ResourceMonitorService (و آزمون‌ها).
    /// </summary>
    public PressureLevel Update(ResourceSample sample)
    {
        _last = sample;
        _pressure = _unlimited ? PressureLevel.Normal : Evaluate(sample, Budget);
        return _pressure;
    }

    /// <summary>
    /// قانون محاسبهٔ سطح فشار (تابع خالص و آزمون‌پذیر).
    /// <list type="bullet">
    /// <item>بحرانی: حافظه ≥ ۹۲٪ سقف، یا بیش از ۱۰۰۰ کار منتظر در ThreadPool، یا صف درخواست پر.</item>
    /// <item>بالا: حافظه ≥ ۸۲٪، CPU ≥ ۹۰٪، درخواست هم‌زمان ≥ ۸۰٪ سقف، یا ≥ ۲۰۰ کار منتظر.</item>
    /// <item>افزایش‌یافته: حافظه ≥ ۷۰٪، CPU ≥ ۷۰٪، درخواست هم‌زمان ≥ ۵۰٪ سقف، یا ≥ ۵۰ کار منتظر.</item>
    /// </list>
    /// مثال: حافظهٔ ۹۵۰ از ۱۰۲۴ مگ → Critical.
    /// </summary>
    public static PressureLevel Evaluate(ResourceSample sample, ResourceBudget budget)
    {
        var memory = sample.MemoryRatio;
        var max = Math.Max(1, budget.MaxConcurrentRequests);
        if (memory >= 0.92 || sample.PendingWorkItems >= 1000 || sample.QueuedRequests >= budget.QueueLength)
            return PressureLevel.Critical;
        if (memory >= 0.82 || sample.CpuRatio >= 0.90 || sample.ActiveRequests >= max * 0.8 || sample.PendingWorkItems >= 200)
            return PressureLevel.High;
        if (memory >= 0.70 || sample.CpuRatio >= 0.70 || sample.ActiveRequests >= max * 0.5 || sample.PendingWorkItems >= 50)
            return PressureLevel.Elevated;
        return PressureLevel.Normal;
    }

    /// <summary>
    /// گرفتن نوبت برای اجرای یک کار. خروجی را حتماً با using آزاد کنید.
    /// <para>کار حیاتی بلافاصله؛ سبک فقط هنگام فشار بحرانی (حداکثر ۲ دقیقه)؛ سنگین: پایان دورهٔ آرام، فشار زیر «بالا»
    /// (حداکثر ۱۵ دقیقه انتظار) و یک جای خالی از <see cref="ResourceBudget.HeavySlots"/>.</para>
    /// <para>هنگام خاموش شدن برنامه OperationCanceledException می‌دهد (کارها همین را انتظار دارند).</para>
    /// </summary>
    /// <param name="job">نام کار (برای نمایش «در انتظار …»).</param>
    /// <param name="weight">وزن کار.</param>
    /// <param name="onWait">اختیاری: هر بار که به انتظار می‌افتد با دلیل صدا زده می‌شود (JobMonitor وضعیت «در انتظار» را ثبت می‌کند).</param>
    public async Task<IDisposable> EnterAsync(string job, JobWeight weight, Action<string?>? onWait = null)
    {
        if (_unlimited || weight == JobWeight.Critical)
            return NoopLease.Instance;

        if (weight == JobWeight.Light)
        {
            var waitedLight = TimeSpan.Zero;
            while (_pressure == PressureLevel.Critical && waitedLight < MaxLightPressureWait)
            {
                onWait?.Invoke("فشار بحرانی منابع");
                Interlocked.Increment(ref _deferrals);
                await Task.Delay(_poll, _stopping);
                waitedLight += _poll;
            }
            onWait?.Invoke(null);
            return NoopLease.Instance;
        }

        // ── سنگین ──
        while (InStartupQuiet(DateTimeOffset.UtcNow))
        {
            onWait?.Invoke("دورهٔ آرام بعد از روشن شدن");
            Interlocked.Increment(ref _deferrals);
            var left = Budget.StartupQuiet - (DateTimeOffset.UtcNow - StartedAt);
            await Task.Delay(left > _poll * 6 ? _poll * 6 : left > TimeSpan.Zero ? left : _poll, _stopping);
        }

        // A11: کار غیرفوری (سئو، گزارش‌ها) در ساعت‌های شلوغ سایت فقط با فشار «عادی» شروع می‌شود (حداکثر ۳۰ دقیقه انتظار).
        if (JobWeights.PrefersQuietHours(job) && IsBusyHour(DateTimeOffset.UtcNow))
        {
            var waitedBusy = TimeSpan.Zero;
            while (_pressure >= PressureLevel.Elevated && waitedBusy < MaxBusyHourWait)
            {
                onWait?.Invoke("ساعت شلوغ سایت؛ کار غیرفوری منتظر کم شدن بار است");
                Interlocked.Increment(ref _deferrals);
                await Task.Delay(_poll, _stopping);
                waitedBusy += _poll;
            }
        }

        var waited = TimeSpan.Zero;
        while (_pressure >= PressureLevel.High && waited < MaxHeavyPressureWait)
        {
            onWait?.Invoke("فشار بالای منابع");
            Interlocked.Increment(ref _deferrals);
            await Task.Delay(_poll, _stopping);
            waited += _poll;
        }

        if (!await _heavySlots.WaitAsync(0, CancellationToken.None))
        {
            onWait?.Invoke("کار سنگین دیگری در حال اجراست: " + (_heavyRunning ?? "-"));
            Interlocked.Increment(ref _deferrals);
            await _heavySlots.WaitAsync(_stopping);
        }

        onWait?.Invoke(null);
        _heavyRunning = job;
        return new HeavyLease(this);
    }

    private void ReleaseHeavy()
    {
        _heavyRunning = null;
        _heavySlots.Release();
    }

    private sealed class HeavyLease(ResourceGovernor owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.ReleaseHeavy();
        }
    }

    private sealed class NoopLease : IDisposable
    {
        public static readonly NoopLease Instance = new();

        public void Dispose()
        {
        }
    }

    // ── شمارش درخواست‌های در حال پردازش (LoadSheddingMiddleware) ──

    private int _active;
    private int _queued;

    /// <summary>درخواست‌های در حال پردازش.</summary>
    public int ActiveRequests => Volatile.Read(ref _active);

    /// <summary>درخواست‌های منتظر در صف.</summary>
    public int QueuedRequests => Volatile.Read(ref _queued);

    internal int IncrementActive() => Interlocked.Increment(ref _active);

    internal void DecrementActive() => Interlocked.Decrement(ref _active);

    internal int IncrementQueued() => Interlocked.Increment(ref _queued);

    internal void DecrementQueued() => Interlocked.Decrement(ref _queued);

    // ── سقف حافظه ──

    /// <summary>
    /// سقف مؤثر حافظه (مگابایت): مقدار تنظیمات اگر داده شده؛ وگرنه کمینهٔ «حافظهٔ در دسترس GC» (که سقف Job Object
    /// استخر IIS یا کانتینر را رعایت می‌کند) و سقف فرضی پروفایل. مثال: هاست ۸ گیگ + پروفایل Low → ۱۰۲۴.
    /// </summary>
    public double EffectiveMemoryLimitMb(long gcAvailableBytes)
    {
        if (Options.MemoryLimitMb > 0)
            return Budget.MemoryLimitMb;
        var available = gcAvailableBytes > 0 ? gcAvailableBytes / 1024d / 1024d : 0;
        if (Budget.MemoryLimitMb <= 0)
            return available;
        return available > 0 ? Math.Min(available, Budget.MemoryLimitMb) : Budget.MemoryLimitMb;
    }
}

/// <summary>
/// سیاست ری‌استارت کنترل‌شده (A25) – تابع خالص و آزمون‌پذیر.
/// <para>فقط وقتی true است که همهٔ این‌ها برقرار باشد: گزینه روشن؛ حافظه دست‌کم ۳ دقیقه پیوسته ≥ ۹۲٪؛ برنامه دست‌کم ۳۰ دقیقه
/// روشن بوده؛ ۶ ساعت از ری‌استارت قبلی گذشته؛ و یا ساعت خلوت شب است یا حافظه ≥ ۹۷٪ (کشته شدن قریب‌الوقوع).</para>
/// <para>مثال: ساعت ۴ بامداد، حافظه ۹۴٪ از ۵ دقیقه پیش، آخرین ری‌استارت دیروز → true.</para>
/// </summary>
public static class SelfRestartPolicy
{
    public static readonly TimeSpan Sustain = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan MinUptime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan MinGap = TimeSpan.FromHours(6);

    public static bool ShouldRestart(ResourceOptions options, DateTimeOffset now, DateTimeOffset startedAt, DateTimeOffset? redSince,
        DateTimeOffset? lastRestart, double memoryRatio, int iranHour)
    {
        if (!options.SelfRestart || redSince is null || memoryRatio < 0.92)
            return false;
        if (now - redSince.Value < Sustain || now - startedAt < MinUptime)
            return false;
        if (lastRestart is { } last && now - last < MinGap)
            return false;
        return IsQuietHour(options, iranHour) || memoryRatio >= 0.97;
    }

    /// <summary>آیا ساعت (به وقت ایران) در بازهٔ خلوت است؟ بازهٔ عبوری از نیمه‌شب (مثلاً ۲۳ تا ۵) هم پشتیبانی می‌شود.</summary>
    public static bool IsQuietHour(ResourceOptions options, int hour) => HourInRange(options.QuietHourStart, options.QuietHourEnd, hour);

    /// <summary>
    /// آیا ساعت در بازهٔ [start, end) است؟ بازهٔ عبوری از نیمه‌شب هم درست است. مثال: (۲۳، ۵، ۲) → true؛ (۹، ۲۴، ۲۳) → true؛ (۴، ۴، ۴) → false.
    /// </summary>
    public static bool HourInRange(int startHour, int endHour, int hour)
    {
        var start = Math.Clamp(startHour, 0, 23);
        var end = Math.Clamp(endHour, 0, 24);
        if (start == end)
            return false;
        return start < end ? hour >= start && hour < end : hour >= start || hour < end;
    }
}

/// <summary>
/// اندازه‌گیری مصرف پروسه (حافظهٔ خصوصی، CPU). یک نمونه برای کل برنامه؛ فقط ResourceMonitorService.
/// </summary>
public sealed class ProcessMeter
{
    private readonly Process _process = Process.GetCurrentProcess();
    private TimeSpan _lastCpu;
    private DateTimeOffset _lastAt;

    public ProcessMeter()
    {
        _lastCpu = _process.TotalProcessorTime;
        _lastAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// حافظهٔ پس‌نگرفتنی پروسه در لینوکس = RssAnon + RssShmem از /proc/self/status (بایت).
    /// مثال: «RssAnon: 204800 kB» و «RssShmem: 102400 kB» → ۳۰۰ مگ. اگر خوانده نشد null (آنگاه WorkingSet به کار می‌رود).
    /// </summary>
    public static long? LinuxAnonymousBytes()
    {
        try
        {
            return SumAnonymous(File.ReadLines("/proc/self/status"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    /// <summary>جمع سطرهای RssAnon و RssShmem؛ اگر RssAnon نبود null. مثال: ["RssAnon: 2 kB","RssShmem: 1 kB"] → 3072.</summary>
    public static long? SumAnonymous(IEnumerable<string> statusLines)
    {
        long? anon = null;
        long shmem = 0;
        foreach (var line in statusLines)
        {
            if (line.StartsWith("RssAnon:", StringComparison.Ordinal))
                anon = ParseKb(line);
            else if (line.StartsWith("RssShmem:", StringComparison.Ordinal))
                shmem = ParseKb(line) ?? 0;
        }
        return anon is { } value ? value + shmem : null;
    }

    /// <summary>مثال: <c>ParseKb("RssAnon:\t  2048 kB")</c> → 2097152؛ متن نامعتبر → null.</summary>
    public static long? ParseKb(string line)
    {
        var colon = line.IndexOf(':');
        if (colon < 0)
            return null;
        var digits = new string(line[(colon + 1)..].Where(char.IsAsciiDigit).ToArray());
        return long.TryParse(digits, out var kb) ? kb * 1024 : null;
    }

    /// <summary>نمونهٔ تازه از وضعیت پروسه و ThreadPool.</summary>
    public ResourceSample Measure(ResourceGovernor governor)
    {
        _process.Refresh();
        var now = DateTimeOffset.UtcNow;
        var cpu = _process.TotalProcessorTime;
        var elapsed = (now - _lastAt).TotalMilliseconds;
        var cpuRatio = elapsed > 0 ? (cpu - _lastCpu).TotalMilliseconds / (elapsed * Math.Max(1, Environment.ProcessorCount)) : 0;
        _lastCpu = cpu;
        _lastAt = now;

        // ویندوز/IIS سقف را با Private Bytes می‌سنجد؛ لینوکس با حافظهٔ ناشناس + اشتراکی (RssAnon + RssShmem؛ کد JIT‌شده با
        // W^X در حافظهٔ اشتراکی است و پس گرفته نمی‌شود). RSS کامل صفحه‌های نگاشت‌شدهٔ DLLها را هم می‌شمارد که سیستم هر وقت لازم
        // شد پس می‌گیرد و جزو سقف واقعی نیست.
        var memoryBytes = OperatingSystem.IsWindows() ? _process.PrivateMemorySize64 : LinuxAnonymousBytes() ?? _process.WorkingSet64;
        var gc = GC.GetGCMemoryInfo();
        return new ResourceSample(
            now,
            memoryBytes / 1024d / 1024d,
            governor.EffectiveMemoryLimitMb(MemoryLimits.SystemAvailableBytes ?? gc.TotalAvailableMemoryBytes),
            GC.GetTotalMemory(false) / 1024d / 1024d,
            Math.Clamp(cpuRatio, 0, 1),
            ThreadPool.PendingWorkItemCount,
            governor.ActiveRequests,
            governor.QueuedRequests,
            ThreadPool.ThreadCount);
    }
}

/// <summary>
/// سقف واقعی حافظه و تنظیم سقف هیپ GC (دور نوزدهم، A4).
/// <para>
/// مشکل: GC به‌طور پیش‌فرض تا ۷۵٪ حافظهٔ کانتینر/سیستم را برای هیپ خودش مجاز می‌داند. ولی برنامه حافظهٔ دیگری هم دارد (کد نگاشت‌شدهٔ
/// DLLها، ICU، بافرهای شبکه، SqlClient)؛ روی هاست ۵۱۲ مگی این جمع از سقف می‌گذرد و سیستم‌عامل پروسه را می‌کشد (۵۰۳).
/// راه‌حل: بعد از شروع، سقف هیپ = ۵۵٪ سقف برنامه (کمینهٔ سقف کانتینر/سیستم، سقف پروفایل یا مقدار دستی) با GC.RefreshMemoryLimit.
/// </para>
/// <para>مثال: کانتینر ۵۱۲ مگ + Low → سقف برنامه ۵۱۲ → هیپ ۲۸۲ مگ. IIS روی سرور ۸ گیگ + Low → سقف ۱۰۲۴ → هیپ ۵۶۳ مگ.</para>
/// <para>
/// استفاده: Program.cs بلافاصله بعد از ResourceGovernor.Configure فقط <see cref="Detect"/> (خواندن سقف واقعی)؛
/// ResourceMonitorService یک دقیقه بعد از آماده شدن <see cref="Apply"/> (شروع برنامه – مهاجرت و داده‌های اولیه – حافظهٔ بیشتری
/// می‌خواهد و نباید به سقف هیپ بخورد). ProcessMeter سقف مؤثر را از SystemAvailableBytes می‌خواند.
/// </para>
/// </summary>
public static class MemoryLimits
{
    /// <summary>سهم هیپ GC از سقف برنامه.</summary>
    public const double HeapShare = 0.55;

    /// <summary>کمترین سقف هیپ (کمتر از این برنامه حتی بالا نمی‌آید).</summary>
    public const long MinHeapBytes = 160L * 1024 * 1024;

    /// <summary>حافظهٔ در دسترس سیستم/کانتینر پیش از تنظیم سقف هیپ (بایت)؛ null = هنوز خوانده نشده.</summary>
    public static long? SystemAvailableBytes { get; private set; }

    /// <summary>سقف هیپی که اعمال شد (بایت) یا null (صفحهٔ منابع).</summary>
    public static long? AppliedHeapLimitBytes { get; private set; }

    /// <summary>
    /// خواندن سقف واقعی حافظهٔ سیستم/کانتینر (یک بار، پیش از هر تغییر سقف هیپ). مثال: کانتینر ۵۱۲ مگ → ۵۳۶۸۷۰۹۱۲.
    /// </summary>
    public static void Detect()
    {
        if (SystemAvailableBytes is not null)
            return;
        var gcAvailable = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        SystemAvailableBytes = ContainerLimitBytes() ?? (gcAvailable > 0 ? gcAvailable : null);
    }

    /// <summary>
    /// سقف حافظهٔ cgroup لینوکس (کانتینر/هاست با محدودیت)، یا null. مثال: محتوای memory.max = «536870912» → 536870912؛ «max» → null.
    /// </summary>
    public static long? ContainerLimitBytes()
    {
        if (!OperatingSystem.IsLinux())
            return null;
        foreach (var file in new[] { "/sys/fs/cgroup/memory.max", "/sys/fs/cgroup/memory/memory.limit_in_bytes" })
        {
            try
            {
                if (File.Exists(file) && ParseLimit(File.ReadAllText(file)) is { } limit)
                    return limit;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
        return null;
    }

    /// <summary>متن فایل سقف cgroup → بایت. مثال: «536870912\n» → 536870912؛ «max» یا عدد نجومی (بدون سقف) → null.</summary>
    public static long? ParseLimit(string? text) =>
        long.TryParse(text?.Trim(), out var value) && value > 0 && value < (1L << 50) ? value : null;

    /// <summary>
    /// سقف هیپ پیشنهادی (بایت) یا null. مثال: <c>HeapLimitFor(512)</c> → ۲۸۲ مگ؛ <c>HeapLimitFor(0)</c> → null (بدون سقف).
    /// </summary>
    public static long? HeapLimitFor(double processLimitMb, double share = HeapShare) =>
        processLimitMb > 0 ? Math.Max(MinHeapBytes, (long)(processLimitMb * share * 1024 * 1024)) : null;

    /// <summary>سهم هیپ از تنظیمات (HeapPercent) یا پیش‌فرض. مثال: HeapPercent=45 ← ۰٫۴۵؛ ۰ ← ۰٫۵۵؛ ۹۰ ← ۰٫۷۵ (محدود).</summary>
    public static double ShareFor(ResourceOptions options) =>
        options.HeapPercent > 0 ? Math.Clamp(options.HeapPercent, 30, 75) / 100.0 : HeapShare;

    /// <summary>
    /// خواندن سقف واقعی و (اگر روشن باشد) پایین آوردن سقف هیپ GC. هرگز سقف را بالا نمی‌برد و هرگز استثنا بیرون نمی‌دهد.
    /// خروجی: متن کوتاه برای گزارش شروع. مثال: «heap limit 282 MB (process 512 MB)».
    /// </summary>
    public static string Apply(ResourceGovernor governor)
    {
        var gcAvailable = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        Detect();
        var processLimitMb = governor.EffectiveMemoryLimitMb(SystemAvailableBytes ?? 0);
        if (!governor.Options.LimitGcHeap || governor.IsUnlimited || HeapLimitFor(processLimitMb, ShareFor(governor.Options)) is not { } heapLimit)
            return $"GC heap not limited (process limit {processLimitMb:0} MB)";
        if (gcAvailable > 0 && gcAvailable <= heapLimit)
            return $"GC heap already within {gcAvailable / 1048576:0} MB (process limit {processLimitMb:0} MB)";
        try
        {
            AppContext.SetData("GCHeapHardLimit", (ulong)heapLimit);
            GC.RefreshMemoryLimit();
            AppliedHeapLimitBytes = heapLimit;
            return $"GC heap limit {heapLimit / 1048576:0} MB (process limit {processLimitMb:0} MB)";
        }
        catch (Exception exception)
        {
            // مثلاً حافظهٔ فعلی از سقف تازه بیشتر است: برنامه با تنظیم پیش‌فرض ادامه می‌دهد.
            return $"GC heap limit not applied: {exception.GetType().Name}";
        }
    }
}
