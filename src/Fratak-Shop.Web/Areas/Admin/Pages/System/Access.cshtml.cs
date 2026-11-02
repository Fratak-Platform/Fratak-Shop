using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Ops;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SystemPages;

/// <summary>
/// «فنی ← درخواست‌های ردشده و پایداری» (/admin/system/access) – مرحلهٔ هفدهم.
/// <para>یک جا برای پاسخ به «چرا ۴۰۳/۵۰۳/دسترسی ندارید گرفتم؟»: هر ردی که خود برنامه انجام داده (دیوار آتش، محدودیت تعداد،
/// تعمیرات، راه‌اندازی، دسترسی پنل، ارجاع به ورود، توکن فرم) با زمان، مسیر، IP و دلیل؛ IPهای پرتکرار؛ پراکسی‌های تنظیم‌نشده؛
/// تاریخچهٔ روشن/خاموش شدن برنامه و وضعیت کلید نشست‌ها. اگر خطایی که کاربر دیده اینجا نیست، از بیرون برنامه آمده
/// (ModSecurity/فایروال Plesk یا توقف استخر IIS) و راهنمای پایین صفحه می‌گوید کجا را ببینید.</para>
/// <para>داده‌ها در حافظه‌اند (RequestMonitor) و با ری‌استارت پاک می‌شوند؛ تاریخچهٔ اجراها در App_Data/logs/lifecycle.json می‌ماند.</para>
/// </summary>
[AdminPermission(AdminPermissions.MonitoringView)]
public class AccessModel(Fratak_Shop.Web.Infrastructure.Auth.JwtSigningKeyProvider jwtKeys, IWebHostEnvironment environment) : AdminPageModel
{
    /// <summary>فیلتر نوع (نام enum، مثلاً «Firewall»)؛ خالی = همه.</summary>
    [BindProperty(SupportsGet = true, Name = "kind")]
    public string? KindFilter { get; set; }

    public RequestMonitor.Kind? SelectedKind { get; private set; }
    public IReadOnlyDictionary<RequestMonitor.Kind, long> Totals { get; private set; } = new Dictionary<RequestMonitor.Kind, long>();
    public IReadOnlyDictionary<RequestMonitor.Kind, int> LastHour { get; private set; } = new Dictionary<RequestMonitor.Kind, int>();
    public IReadOnlyList<RequestMonitor.Event> Events { get; private set; } = [];
    public IReadOnlyList<(string Ip, int Count, RequestMonitor.Kind Top)> TopIps { get; private set; } = [];
    public IReadOnlyList<(string Ip, long Count)> Proxies { get; private set; } = [];
    public IReadOnlyList<LifecycleRun> Runs { get; private set; } = [];
    public int LostSessions { get; private set; }
    public string KeySource => jwtKeys.Source;
    public string? KeyProblem => jwtKeys.Problem;
    public string MyIp { get; private set; } = "-";

    /// <summary>رویدادهای ویندوز ۳ روز اخیر همین سایت (علت واقعی توقف‌ها و خاموش شدن استخر IIS).</summary>
    public IReadOnlyList<WindowsEvent> WinEvents { get; private set; } = [];

    /// <summary>اگر گزارش ویندوز خوانده نشد، دلیلش (مثلاً نداشتن اجازه روی هاست اشتراکی)؛ وگرنه null.</summary>
    public string? WinProblem { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        (WinEvents, WinProblem) = await WindowsEvents.RecentAsync(environment.ContentRootPath, cancellationToken);

        SelectedKind = Enum.TryParse<RequestMonitor.Kind>(KindFilter, true, out var kind) && Enum.IsDefined(kind) ? kind : null;
        Totals = RequestMonitor.TotalsByKind();
        LastHour = RequestMonitor.LastMinutes(60);
        Events = RequestMonitor.Recent(SelectedKind, 200);
        TopIps = RequestMonitor.TopIps(10);
        Proxies = RequestMonitor.UntrustedProxies();
        Runs = AppLifecycle.Runs().Take(20).ToList();
        LostSessions = RequestMonitor.LostSessionsLastHour();
        var ip = HttpContext.Connection.RemoteIpAddress;
        MyIp = ip is null ? "-" : (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
    }

    /// <summary>رنگ نشان هر نوع.</summary>
    public static string Css(RequestMonitor.Kind kind) => kind switch
    {
        RequestMonitor.Kind.Firewall or RequestMonitor.Kind.Forbidden => "a-badge-red",
        RequestMonitor.Kind.RateLimited or RequestMonitor.Kind.Antiforgery => "a-badge-yellow",
        RequestMonitor.Kind.Maintenance or RequestMonitor.Kind.Starting => "a-badge-purple",
        RequestMonitor.Kind.AdminDenied or RequestMonitor.Kind.NotStaff => "a-badge-blue",
        _ => "a-badge-gray",
    };

    /// <summary>حافظهٔ یک اجرا. مثال: «۴۱۲ / بیشینه ۸۹۰ MB»؛ بدون نبض ثبت‌شده ← «-».</summary>
    public static string Memory(LifecycleRun run) => run.MemoryMb is { } memory
        ? $"{Format.Number(memory)} / بیشینه {Format.Number(run.PeakMemoryMb ?? memory)} MB"
        : "-";

    /// <summary>مدت یک اجرا به زبان ساده. مثال: ۳ ساعت و ۵ دقیقه.</summary>
    public static string Duration(LifecycleRun run)
    {
        var span = (run.StoppedAt ?? DateTimeOffset.UtcNow) - run.StartedAt;
        if (span.TotalMinutes < 1)
            return "کمتر از یک دقیقه";
        if (span.TotalHours < 1)
            return $"{Format.Number((int)span.TotalMinutes)} دقیقه";
        if (span.TotalDays < 1)
            return $"{Format.Number((int)span.TotalHours)} ساعت و {Format.Number(span.Minutes)} دقیقه";
        return $"{Format.Number((int)span.TotalDays)} روز و {Format.Number(span.Hours)} ساعت";
    }
}
