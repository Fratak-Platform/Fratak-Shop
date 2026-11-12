using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Http;
using Fratak_Shop.Web.Infrastructure.Ops;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SystemPages;

/// <summary>
/// دور نوزدهم (A1/A3): صفحهٔ «فنی ← منابع سرور» – /admin/system/resources.
/// <para>چه نشان می‌دهد؟ پروفایل منابع، سطح فشار فعلی، حافظه نسبت به سقف استخر، CPU، درخواست‌های هم‌زمان و صف، کار سنگینی که
/// الان اجرا می‌شود و کارهای منتظر نوبت، نمودار دقیقه‌ای ۳ ساعت آخر، رویدادهای مهم (فشرده‌سازی کش، ری‌استارت کنترل‌شده)،
/// قطع‌کن‌های مدار سرویس‌های بیرونی، سنگین‌ترین صفحه‌ها از نظر تعداد پرس‌وجو، و تاریخچهٔ روشن/خاموش شدن با دلیل و مدت آماده شدن.</para>
/// <para>فقط خواندنی است (هیچ تغییری نمی‌دهد). مثال کاربرد: «چرا سایت دیروز ساعت ۹ شب کند بود؟» ← نمودار و رویدادها.</para>
/// </summary>
[AdminPermission(AdminPermissions.MonitoringView, AdminPermissions.SystemManage)]
public class ResourcesModel(ResourceHistory history, PageSnapshotStore snapshots) : AdminPageModel
{
    public ResourceGovernor Governor { get; private set; } = ResourceGovernor.Current;
    public ResourceSample Sample { get; private set; }
    public IReadOnlyList<ResourcePoint> Points { get; private set; } = [];
    public IReadOnlyList<ResourceEvent> Events { get; private set; } = [];
    public IReadOnlyList<JobState> Waiting { get; private set; } = [];
    public IReadOnlyList<CircuitBreakers.State> Circuits { get; private set; } = [];
    public IReadOnlyList<PageQueryStats.Row> HeavyPages { get; private set; } = [];
    /// <summary>دور ۲۳: پرس‌وجوهای کند اخیر (بدون مقدار پارامترها).</summary>
    public IReadOnlyList<Fratak_Shop.Data.Diagnostics.SlowQueryLog.Entry> SlowQueries { get; private set; } = [];
    public IReadOnlyList<LifecycleRun> Runs { get; private set; } = [];
    public double PeakMemoryMb { get; private set; }
    public int PeakRequests { get; private set; }
    public int SnapshotCount { get; private set; }
    public long SnapshotServed { get; private set; }

    public void OnGet()
    {
        Governor = ResourceGovernor.Current;
        Sample = Governor.LastSample;
        Points = history.Points().TakeLast(60).Reverse().ToList();
        Events = history.Events();
        Waiting = JobMonitor.All.Where(j => j.WaitReason is not null).ToList();
        Circuits = CircuitBreakers.All().Take(30).ToList();
        HeavyPages = PageQueryStats.Top(15);
        SlowQueries = Fratak_Shop.Data.Diagnostics.SlowQueryLog.Recent();
        Runs = AppLifecycle.Runs().Take(10).ToList();
        PeakMemoryMb = history.PeakMemoryMb;
        PeakRequests = history.PeakRequests;
        SnapshotCount = snapshots.Count();
        SnapshotServed = snapshots.Served;
    }

    /// <summary>کلاس رنگ نشان سطح فشار. مثال: Critical → a-badge-red.</summary>
    public static string LevelCss(PressureLevel level) => level switch
    {
        PressureLevel.Critical => "a-badge-red",
        PressureLevel.High => "a-badge-yellow",
        PressureLevel.Elevated => "a-badge-blue",
        _ => "a-badge-green",
    };

    /// <summary>نام فارسی پروفایل. مثال: Low → «ضعیف (هاست اشتراکی)».</summary>
    public static string ProfileTitle(ResourceProfile profile) => profile switch
    {
        ResourceProfile.High => "قوی (سرور اختصاصی)",
        ResourceProfile.Normal => "متوسط (هاست خوب / VPS کوچک)",
        _ => "ضعیف (هاست اشتراکی)",
    };

    /// <summary>نام فارسی وزن کار.</summary>
    public static string WeightTitle(JobWeight weight) => weight switch
    {
        JobWeight.Critical => "حیاتی",
        JobWeight.Heavy => "سنگین",
        _ => "سبک",
    };
}
