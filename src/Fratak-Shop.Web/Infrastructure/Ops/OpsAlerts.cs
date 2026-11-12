using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Data.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fratak_Shop.Web.Infrastructure.Ops;

/// <summary>نوع هشدار پیامکی فنی.</summary>
public enum OpsAlertKind
{
    /// <summary>خطاهای زیاد در زمان کوتاه (مثلاً ۳۰ خطا در ۱۰ دقیقه).</summary>
    ErrorSpike,

    /// <summary>پایگاه داده چند بار پشت سر هم در دسترس نبود.</summary>
    DatabaseDown,

    /// <summary>اجرای قبلی برنامه ناگهانی تمام شده بود (کرش / کمبود حافظه / خاموشی سرور) و برنامه دوباره روشن شد.</summary>
    UncleanRestart,
}

/// <summary>
/// دور ۲۳ (مورد ۷۸): قاعده‌های هشدار پیامکی «خطا / از کار افتادن» – بی‌حالت نسبت به بیرون، امن برای چند رشته.
/// <para>قانون‌ها (برای اینکه مدیر با پیامک اضافه آزار نبیند):</para>
/// <list type="bullet">
/// <item>جهش خطا: دست‌کم <see cref="SpikeErrors"/> خطا در <see cref="SpikeWindow"/>.</item>
/// <item>هر نوع هشدار حداکثر یک بار در <see cref="Cooldown"/>.</item>
/// <item>کل هشدارها حداکثر <see cref="DailyMax"/> پیامک در ۲۴ ساعت.</item>
/// </list>
/// <para>مثال: ۳۵ خطا در ۵ دقیقه ← یک پیامک؛ ۱۰۰ خطای دیگر در همان ساعت ← هیچ پیامک اضافه‌ای.</para>
/// <para>استفاده: <see cref="OpsAlerts"/> (سراسری) و آزمون‌ها (نمونهٔ جدا با زمان دلخواه).</para>
/// </summary>
public sealed class OpsAlertRules
{
    public const int SpikeErrors = 30;
    public static readonly TimeSpan SpikeWindow = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan Cooldown = TimeSpan.FromHours(2);
    public const int DailyMax = 6;

    private readonly object _gate = new();
    private readonly Queue<DateTimeOffset> _errors = new();
    private readonly Dictionary<OpsAlertKind, DateTimeOffset> _lastFired = [];
    private readonly Queue<DateTimeOffset> _fired = new();

    /// <summary>ثبت یک خطا؛ true یعنی همین الان باید هشدار «جهش خطا» فرستاده شود (آستانه، فاصله و سقف روزانه رعایت شده).</summary>
    public bool ObserveError(DateTimeOffset now)
    {
        lock (_gate)
        {
            _errors.Enqueue(now);
            while (_errors.Count > 0 && now - _errors.Peek() > SpikeWindow)
                _errors.Dequeue();
            // صف هرگز بیش از آستانه بزرگ نمی‌شود (حافظهٔ ثابت حتی با هزاران خطا).
            while (_errors.Count > SpikeErrors)
                _errors.Dequeue();
            return _errors.Count >= SpikeErrors && TryFireLocked(OpsAlertKind.ErrorSpike, now);
        }
    }

    /// <summary>آیا این هشدار الان مجاز است؟ اگر بله، به‌عنوان «فرستاده‌شده» ثبت می‌شود.</summary>
    public bool TryFire(OpsAlertKind kind, DateTimeOffset now)
    {
        lock (_gate)
            return TryFireLocked(kind, now);
    }

    private bool TryFireLocked(OpsAlertKind kind, DateTimeOffset now)
    {
        if (_lastFired.TryGetValue(kind, out var last) && now - last < Cooldown)
            return false;
        while (_fired.Count > 0 && now - _fired.Peek() > TimeSpan.FromHours(24))
            _fired.Dequeue();
        if (_fired.Count >= DailyMax)
            return false;
        _lastFired[kind] = now;
        _fired.Enqueue(now);
        return true;
    }

    /// <summary>
    /// متن پیامک (کوتاه، بدون عدد ۴ رقمی و بدون جزئیات فنی/شخصی). مثال: ErrorSpike ← «هشدار فنی فروشگاه: خطاهای زیاد در ۱۰ دقیقهٔ اخیر…».
    /// </summary>
    public static string Message(OpsAlertKind kind, string siteName) => kind switch
    {
        OpsAlertKind.DatabaseDown => $"هشدار فنی {siteName}: پایگاه داده چند بار پشت سر هم در دسترس نبود. لطفاً هاست را بررسی کنید.",
        OpsAlertKind.UncleanRestart => $"هشدار فنی {siteName}: سایت ناگهانی خاموش شده بود و دوباره روشن شد. جزئیات: پنل ← فنی ← سلامت سیستم.",
        _ => $"هشدار فنی {siteName}: خطاهای زیاد در ۱۰ دقیقهٔ اخیر. جزئیات: پنل ← فنی ← سلامت سیستم.",
    };
}

/// <summary>
/// دور ۲۳ (مورد ۷۸): نقطهٔ سراسری هشدارهای فنی. هر جای برنامه (ثبت‌کنندهٔ خطا، نویسندهٔ گزارش خطا) بدون وابستگی صدا می‌زند؛
/// پیامک را <see cref="OpsAlertService"/> در پس‌زمینه می‌فرستد. صدا زدن هیچ‌وقت مسدود نمی‌شود و استثنا نمی‌دهد.
/// <para>مثال: <c>OpsAlerts.ErrorLogged()</c> در DbErrorLogger؛ <c>OpsAlerts.Raise(OpsAlertKind.DatabaseDown)</c> در ErrorLogWriter.</para>
/// </summary>
public static class OpsAlerts
{
    private static readonly System.Threading.Channels.Channel<OpsAlertKind> Pending =
        System.Threading.Channels.Channel.CreateBounded<OpsAlertKind>(new System.Threading.Channels.BoundedChannelOptions(8)
        {
            FullMode = System.Threading.Channels.BoundedChannelFullMode.DropWrite,
        });

    /// <summary>قاعده‌های سراسری.</summary>
    public static OpsAlertRules Rules { get; } = new();

    internal static System.Threading.Channels.ChannelReader<OpsAlertKind> Reader => Pending.Reader;

    /// <summary>یک خطا ثبت شد (سطح Error/Critical).</summary>
    public static void ErrorLogged()
    {
        if (Rules.ObserveError(DateTimeOffset.UtcNow))
            Pending.Writer.TryWrite(OpsAlertKind.ErrorSpike);
    }

    /// <summary>درخواست هشدار (فاصله و سقف روزانه رعایت می‌شود).</summary>
    public static void Raise(OpsAlertKind kind)
    {
        if (Rules.TryFire(kind, DateTimeOffset.UtcNow))
            Pending.Writer.TryWrite(kind);
    }
}

/// <summary>
/// دور ۲۳ (مورد ۷۸): فرستندهٔ پیامک هشدار فنی به مدیران اصلی.
/// <para>گیرنده‌ها: کاربران «مدیر اصلی» (ستون SystemAdminKey، مسدودنشده) با شمارهٔ فعلی‌شان؛ هر ساعت تازه می‌شود. اگر پایگاه داده
/// در دسترس نباشد (همان وقتی که هشدار «پایگاه داده» لازم است) آخرین فهرست حافظه یا شماره‌های Shop:DefaultAdmins به کار می‌رود.</para>
/// <para>در شروع (پس از ۲ دقیقه) بررسی می‌کند اجرای قبلی ناگهانی تمام شده یا نه (<see cref="AppLifecycle.IsUnclean"/>).</para>
/// <para>خاموش کردن: <c>Shop:OpsAlertSms=false</c> (آزمون‌های E2E خاموشش می‌کنند).</para>
/// </summary>
public sealed class OpsAlertService(ISmsSender sms, IOptions<ShopOptions> options, IServiceScopeFactory scopes,
    ILogger<OpsAlertService> logger) : BackgroundService
{
    private List<string> _recipients = [];
    private DateTimeOffset _recipientsAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.OpsAlertSms)
            return;
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            CheckPreviousRun();
            await foreach (var kind in OpsAlerts.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await SendAsync(kind, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // هشدار نباید خودش خطای تازه بسازد (حلقهٔ هشدار) ← فقط هشدار سطح Warning.
                    logger.LogWarning("Ops alert {Kind} could not be sent: {Message}", kind, exception.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>اجرای قبلی (اگر در ۷ روز اخیر بوده) ناگهانی تمام شده؟ ← هشدار.</summary>
    private static void CheckPreviousRun()
    {
        var current = AppLifecycle.Current;
        var previous = AppLifecycle.Runs()
            .FirstOrDefault(r => current is null || r.ProcessId != current.ProcessId || r.StartedAt != current.StartedAt);
        if (previous is not null && previous.StartedAt > DateTimeOffset.UtcNow.AddDays(-7) && AppLifecycle.IsUnclean(previous))
            OpsAlerts.Raise(OpsAlertKind.UncleanRestart);
    }

    private async Task SendAsync(OpsAlertKind kind, CancellationToken cancellationToken)
    {
        var phones = await RecipientsAsync(cancellationToken);
        if (phones.Count == 0)
            return;
        var text = OpsAlertRules.Message(kind, SiteName());
        foreach (var phone in phones)
            await sms.SendAsync(phone, text);
        logger.LogInformation("Ops alert {Kind} sent to {Count} admin(s).", kind, phones.Count);
    }

    /// <summary>نام فروشگاه از تنظیمات (کش‌شده)؛ اگر پایگاه داده در دسترس نباشد «فروشگاه».</summary>
    private string SiteName()
    {
        try
        {
            using var scope = scopes.CreateScope();
            var name = scope.ServiceProvider.GetRequiredService<ISiteContext>().Settings.SiteName;
            return string.IsNullOrWhiteSpace(name) ? "فروشگاه" : name.Trim();
        }
        catch
        {
            return "فروشگاه";
        }
    }

    private async Task<List<string>> RecipientsAsync(CancellationToken cancellationToken)
    {
        if (_recipients.Count > 0 && DateTimeOffset.UtcNow - _recipientsAt < TimeSpan.FromHours(1))
            return _recipients;
        try
        {
            using var scope = scopes.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var phones = await context.Users.AsNoTracking()
                .Where(u => u.SystemAdminKey != null && !u.IsBlocked)
                .Select(u => u.PhoneNumber)
                .Take(10)
                .ToListAsync(timeout.Token);
            var list = phones.Select(AdminAccessService.NormalizeOwnerPhone).OfType<string>().Distinct().ToList();
            if (list.Count > 0)
            {
                _recipients = list;
                _recipientsAt = DateTimeOffset.UtcNow;
                return list;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // پایگاه داده در دسترس نیست ← فهرست قبلی یا تنظیمات.
        }
        if (_recipients.Count > 0)
            return _recipients;
        return options.Value.DefaultAdminAccounts()
            .Select(a => AdminAccessService.NormalizeOwnerPhone(a.PhoneNumber)).OfType<string>().Distinct().Take(10).ToList();
    }
}
