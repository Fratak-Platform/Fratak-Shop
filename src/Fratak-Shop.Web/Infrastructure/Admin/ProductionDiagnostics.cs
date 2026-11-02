using Fratak_Shop.Web.Infrastructure.Middleware;
using Fratak_Shop.Web.Infrastructure.Ops;

namespace Fratak_Shop.Web.Infrastructure.Admin;

/// <summary>
/// یادداشت‌های آماده‌سازی مدیران اصلی در آخرین شروع برنامه (مرحلهٔ هفدهم)، برای صفحهٔ «سلامت سامانه».
/// <para>چرا: قبلاً اگر مثلاً دو مدیر Key یکسان داشتند، فقط یک خط در لاگ شروع برنامه می‌آمد و مدیر دوم بی‌صدا «دسترسی ندارید»
/// می‌گرفت. حالا مشکل‌ها (Key تکراری/نامعتبر، شمارهٔ نامعتبر، تداخل شماره، …) در صفحهٔ سلامت هم هشدار می‌شوند.</para>
/// <para>محل پر شدن: Program.cs بعد از IAdminAccessService.EnsureDefaultsAsync.</para>
/// </summary>
public static class DefaultAdminStartupReport
{
    private static IReadOnlyList<string> _notes = [];

    /// <summary>همهٔ یادداشت‌ها (بدون رمز، شماره‌ها پوشیده).</summary>
    public static IReadOnlyList<string> Notes => _notes;

    /// <summary>یادداشت‌هایی که مشکل‌اند (نه «ساخته شد/بدون تغییر/به‌روز شد»).</summary>
    public static IReadOnlyList<string> Problems => _notes.Where(IsProblem).ToList();

    public static void Set(IReadOnlyList<string> notes) => _notes = notes.ToList();

    /// <summary>
    /// آیا این یادداشت مشکل است؟ مثال: «هشدار 0913***6156: Key …» ← true؛ «ساخته شد 0991***1090 …» ← false.
    /// </summary>
    public static bool IsProblem(string note)
        => note.StartsWith("هشدار", StringComparison.Ordinal) || note.StartsWith("نادیده", StringComparison.Ordinal)
           || note.StartsWith("ساخته نشد", StringComparison.Ordinal) || note.StartsWith("بازگردانی نشد", StringComparison.Ordinal)
           || note.StartsWith("ذخیره نشد", StringComparison.Ordinal) || note.StartsWith("تداخل", StringComparison.Ordinal);
}

/// <summary>
/// بررسی بارگذاری کتابخانه‌های بومی (مرحلهٔ هفدهم): SkiaSharp (پردازش تصویر محصولات و لوگو) و OnnxRuntime (خواندن کد امنیتی
/// رهگیری پست). یک بار در هر اجرا انجام و نتیجه نگه داشته می‌شود.
/// <para>چرا: خروجی انتشار حالا فقط فایل‌های بومی سیستم‌عامل مقصد را دارد (کاهش حجم از ~۲۰۰ به ~۷۰ مگابایت). اگر کسی خروجی
/// ویندوز را روی لینوکس بگذارد (یا برعکس)، این بررسی فوراً با پیام روشن هشدار می‌دهد.</para>
/// </summary>
public static class NativeLibraries
{
    private static readonly Lazy<string?> SkiaProblem = new(() => Probe(() =>
    {
        using var bitmap = new global::SkiaSharp.SKBitmap(1, 1);
    }));

    private static readonly Lazy<string?> OnnxProblem = new(() => Probe(() =>
    {
        using var options = new global::Microsoft.ML.OnnxRuntime.SessionOptions();
    }));

    /// <summary>خطای بارگذاری SkiaSharp یا null (سالم).</summary>
    public static string? Skia => SkiaProblem.Value;

    /// <summary>خطای بارگذاری OnnxRuntime یا null (سالم).</summary>
    public static string? Onnx => OnnxProblem.Value;

    private static string? Probe(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            var root = exception is TypeInitializationException { InnerException: { } inner } ? inner : exception;
            return root.GetType().Name + ": " + (root.Message.Length <= 200 ? root.Message : root.Message[..200]);
        }
    }
}

/// <summary>
/// بررسی‌های «آمادگی سرور واقعی» که مرحلهٔ هفدهم به صفحهٔ سلامت اضافه کرد؛ هر کدام دقیقاً یکی از علت‌های ۴۰۳/۵۰۳/خروج ناگهانی
/// را هدف می‌گیرد و راه‌حل مشخص می‌دهد. خالص و بدون پایگاه داده (ورودی‌ها از وضعیت حافظه‌ای برنامه).
/// <para>استفاده: HealthService.RunAsync (صفحهٔ /admin/system و /healthz با توکن).</para>
/// </summary>
public static class ProductionDiagnostics
{
    /// <summary>کلید امضای توکن‌های ورود. مثال: منبع «ephemeral» ← بحرانی (هر بازیافت برنامه همه را بیرون می‌اندازد).</summary>
    public static HealthCheck SigningKey(string source, string? problem) => source switch
    {
        "configured" => new HealthCheck("کلید نشست‌های ورود", HealthLevel.Healthy, "از تنظیمات سرور (Jwt:SigningKey)؛ با ری‌استارت و بازیافت برنامه کسی بیرون نمی‌افتد."),
        "persisted" => new HealthCheck("کلید نشست‌های ورود", HealthLevel.Healthy, "کلید پایدار ذخیره‌شده در App_Data/keys؛ با ری‌استارت و بازیافت برنامه کسی بیرون نمی‌افتد."),
        _ => new HealthCheck("کلید نشست‌های ورود", HealthLevel.Critical,
            "کلید موقت حافظه‌ای: با هر ری‌استارت یا بازیافت استخر برنامه همهٔ کاربران و مدیران بیرون می‌افتند" + (problem is null ? "." : " (" + problem + ")."),
            "به کاربر استخر برنامه اجازهٔ نوشتن در App_Data بدهید یا متغیر محیطی Jwt__SigningKey (حداقل ۳۲ نویسهٔ تصادفی) را تنظیم کنید."),
    };

    /// <summary>
    /// دامنهٔ اصلی در برابر دامنه‌ای که واقعاً با آن به سایت می‌آیند.
    /// مثال: CanonicalHost = «faratek.ir» و همهٔ درخواست‌ها با «shop.fratak.ir» ← بحرانی با راه‌حل «shop.fratak.ir بگذارید».
    /// </summary>
    public static HealthCheck? CanonicalHost(string? configured, IReadOnlyDictionary<string, long> seen, IReadOnlyCollection<string> aliases, bool production)
    {
        const string Name = "دامنهٔ اصلی";
        var canonical = CanonicalHostMiddleware.Normalize(configured);
        var publicHosts = seen.Where(h => CanonicalHostMiddleware.Normalize(h.Key) is not null).ToList();
        var top = publicHosts.OrderByDescending(h => h.Value).Select(h => h.Key).FirstOrDefault();
        if (canonical is null)
            return production
                ? new HealthCheck(Name, HealthLevel.Warning, "دامنهٔ اصلی سایت (Shop:CanonicalHost) تنظیم نشده است؛ نسخهٔ www و بدون www جدا دیده می‌شوند.",
                    $"در appsettings.Production.json مقدار Shop:CanonicalHost را دامنهٔ سایت ({top ?? "shop.fratak.ir"}) بگذارید.")
                : null;

        var onCanonical = publicHosts.Where(h => string.Equals(h.Key, canonical, StringComparison.OrdinalIgnoreCase) || CanonicalHostMiddleware.IsWwwTwin(canonical, h.Key)).Sum(h => h.Value);
        var foreign = publicHosts.Where(h => CanonicalHostMiddleware.IsForeign(canonical, h.Key, aliases)).OrderByDescending(h => h.Value).ToList();
        if (onCanonical == 0 && foreign.Count > 0 && foreign[0].Value >= 5)
            return new HealthCheck(Name, HealthLevel.Critical,
                $"دامنهٔ اصلی تنظیمات «{canonical}» است ولی سایت با «{foreign[0].Key}» باز می‌شود ({foreign[0].Value} درخواست).",
                $"اگر دامنهٔ واقعی «{foreign[0].Key}» است، Shop:CanonicalHost را همین بگذارید؛ نشانی‌های کانونیکال، نقشهٔ سایت و پیامک‌ها از آن ساخته می‌شوند.");
        return new HealthCheck(Name, HealthLevel.Healthy,
            $"{canonical} ({onCanonical} درخواست؛ {RequestMonitor.CanonicalRedirects} ریدایرکت از www/دامنهٔ قدیمی)" + (foreign.Count > 0 ? $"؛ دامنهٔ ناشناختهٔ بدون ریدایرکت: {string.Join("، ", foreign.Take(3).Select(f => f.Key))}" : "") + ".");
    }

    /// <summary>
    /// سایت پشت CDN/پراکسی است ولی IP واقعی کاربران خوانده نمی‌شود؟ (همه با IP پراکسی دیده می‌شوند ← محدودیت تعداد درخواست و
    /// دیوار آتش برای همه با هم اعمال می‌شود: ۴۲۹/۴۰۳ برای کاربران بی‌گناه).
    /// </summary>
    public static HealthCheck? Proxy(long untrustedForwarded, IReadOnlyList<(string Ip, long Count)> proxies)
    {
        if (untrustedForwarded < 20)
            return null;
        return new HealthCheck("پراکسی / CDN", HealthLevel.Warning,
            $"{untrustedForwarded} درخواست با سرآیند X-Forwarded-For از فرستندهٔ تأییدنشده آمد ({string.Join("، ", proxies.Take(5).Select(p => p.Ip))})؛ "
            + "یعنی سایت پشت پراکسی/CDN است و همهٔ کاربران با IP آن دیده می‌شوند.",
            "در appsettings.Production.json بخش Security مقدار KnownProxies (IPهای پراکسی) یا KnownNetworks (بازهٔ IP شبکهٔ CDN، مثل بازه‌های ابرآروان) را بگذارید؛ راهنما: docs/DEPLOYMENT.md.");
    }

    /// <summary>مشکل‌های فهرست مدیران اصلی (Shop:DefaultAdmins) در آخرین شروع برنامه.</summary>
    public static HealthCheck DefaultAdmins(IReadOnlyList<string> notes, IReadOnlyList<string> problems)
        => problems.Count > 0
            ? new HealthCheck("مدیران اصلی (تنظیمات سرور)", HealthLevel.Warning, string.Join(" | ", problems.Take(5)),
                "فهرست Shop:DefaultAdmins در appsettings.json را اصلاح کنید (هر مدیر یک Key لاتین جدا، مثل owner و partner، و شمارهٔ ۱۱ رقمی) و برنامه را ری‌استارت کنید.")
            : new HealthCheck("مدیران اصلی (تنظیمات سرور)", HealthLevel.Healthy, notes.Count == 0 ? "مدیر اصلی در تنظیمات تعریف نشده است." : $"{notes.Count} مورد بررسی شد؛ بدون مشکل.");

    /// <summary>
    /// تاریخچهٔ روشن/خاموش شدن ۲۴ ساعت اخیر. مثال: ۳ توقف ناگهانی ← هشدار؛ خطای مرگبار ← بحرانی؛ ۱۲ بار روشن شدن ← هشدار بازیافت مکرر.
    /// </summary>
    public static HealthCheck Lifecycle(int starts, IReadOnlyList<LifecycleRun> unclean)
    {
        const string Name = "روشن/خاموش شدن برنامه";
        if (unclean.Count > 0)
        {
            var last = unclean.OrderByDescending(r => r.StartedAt).First();
            var crashed = unclean.FirstOrDefault(r => r.Crash is not null);
            return new HealthCheck(Name, crashed is not null ? HealthLevel.Critical : HealthLevel.Warning,
                $"در ۲۴ ساعت اخیر {unclean.Count} بار ناگهانی متوقف شد (آخرین اجرای ناگهانی: {Helpers.Format.DateTime(last.StartedAt)})" + (crashed is not null ? "؛ خطای مرگبار: " + crashed.Crash : "") + ".",
                "صفحهٔ «گزارش‌های سرور» و «خطاها» را ببینید؛ در Plesk/IIS حافظهٔ مجاز استخر برنامه (Private Memory Limit) را بررسی و stdout لاگ ANCM را روشن کنید (docs/DEPLOYMENT.md).");
        }

        if (starts > 8)
            return new HealthCheck(Name, HealthLevel.Warning, $"برنامه در ۲۴ ساعت اخیر {starts} بار روشن شده است (بازیافت مکرر استخر برنامه).",
                "در IIS/Plesk برای استخر برنامه Idle Time-out را 0، Start Mode را AlwaysRunning و بازیافت دوره‌ای را خاموش (یا یک بار در شب) بگذارید.");
        return new HealthCheck(Name, HealthLevel.Healthy, $"{starts} بار روشن شدن در ۲۴ ساعت اخیر؛ بدون توقف ناگهانی.");
    }

    /// <summary>
    /// «علت توقف‌ها (گزارش ویندوز)»: اگر در ۲۴ ساعت اخیر رویداد جدی (بسته شدن پروسه، Rapid-Fail، Ping) برای همین سایت ثبت شده،
    /// هشدار با عنوان آخرین رویداد. null = روی ویندوز نیستیم (چیزی برای گفتن نیست). عمداً «هشدار» است نه «بحرانی»: اگر این
    /// بررسی اجرا می‌شود یعنی برنامه دوباره بالاست و /healthz نباید به‌خاطر رویداد گذشته ۵۰۳ بدهد.
    /// مثال: رویداد ۵۰۰۲ دو ساعت پیش ← هشدار «IIS استخر برنامه را خاموش کرد (Rapid-Fail)…».
    /// </summary>
    public static HealthCheck? WindowsCrashes(bool isWindows, IReadOnlyList<WindowsEvent> events, string? problem, DateTimeOffset now)
    {
        const string Name = "علت توقف‌ها (گزارش ویندوز)";
        if (!isWindows)
            return null;
        var severe = events.Where(e => e.Severe && now - e.At <= TimeSpan.FromHours(24)).OrderByDescending(e => e.At).ToList();
        if (severe.Count > 0)
            return new HealthCheck(Name, HealthLevel.Warning,
                $"در ۲۴ ساعت اخیر {severe.Count} رویداد جدی برای این سایت ثبت شده؛ آخرین: «{severe[0].Title}» ({Helpers.Format.DateTime(severe[0].At)}).",
                "«فنی ← درخواست‌های ردشده و پایداری» بخش «علت توقف‌ها از گزارش ویندوز» را ببینید؛ توضیح و راه‌حل هر رویداد همان‌جاست.");
        if (problem is not null)
            return new HealthCheck(Name, HealthLevel.Healthy, problem,
                "بدون این گزارش هم تاریخچهٔ روشن/خاموش شدن و حافظهٔ هر اجرا ثبت می‌شود؛ علت دقیق توقف را می‌توان از پشتیبانی هاست پرسید.");
        return new HealthCheck(Name, HealthLevel.Healthy, "در ۲۴ ساعت اخیر بسته شدن ناگهانی یا خاموش شدن استخر برای این سایت ثبت نشده است.");
    }

    /// <summary>خلاصهٔ درخواست‌های ردشده یک ساعت اخیر با هشدار برای «گم شدن نشست» و «۴۲۹ زیاد».</summary>
    public static HealthCheck Rejections(IReadOnlyDictionary<RequestMonitor.Kind, int> lastHour, int lostSessions)
    {
        const string Name = "درخواست‌های ردشده";
        var summary = string.Join("، ", lastHour.Where(k => k.Value > 0).Select(k => $"{RequestMonitor.Title(k.Key)}: {k.Value}"));
        if (lostSessions >= 5)
            return new HealthCheck(Name, HealthLevel.Warning,
                $"{lostSessions} بار در یک ساعت اخیر کاربرِ دارای نشست به صفحهٔ ورود فرستاده شد (نشانهٔ گم شدن نشست).",
                "«فنی ← درخواست‌های ردشده» را ببینید؛ معمولاً یعنی برنامه پشت سر هم بازیافت می‌شود یا کلید نشست پایدار نیست (بررسی‌های بالا).");
        if (lastHour.TryGetValue(RequestMonitor.Kind.RateLimited, out var limited) && limited >= 50)
            return new HealthCheck(Name, HealthLevel.Warning, $"{limited} درخواست در یک ساعت اخیر به سقف تعداد خورد (۴۲۹).",
                "اگر کاربران واقعی‌اند (مثلاً همه پشت یک IP/پراکسی)، بخش «پراکسی / CDN» و Security:TrustedIps را ببینید.");
        return new HealthCheck(Name, HealthLevel.Healthy, string.IsNullOrEmpty(summary) ? "در یک ساعت اخیر درخواستی رد نشده است." : summary + " (یک ساعت اخیر).");
    }

    /// <summary>کتابخانه‌های بومی تصویر و کپچا. SkiaSharp بحرانی (تصویر محصول)، OnnxRuntime هشدار (فقط رهگیری پست).</summary>
    public static IEnumerable<HealthCheck> Natives(string? skia, string? onnx)
    {
        const string Hint = "خروجی انتشار برای سیستم‌عامل دیگری ساخته شده است؛ دوباره با tools/deploy/publish.ps1 (ویندوز) یا -p:FratakPublishTarget=linux بسازید (docs/DEPLOYMENT.md).";
        yield return skia is null
            ? new HealthCheck("پردازش تصویر (SkiaSharp)", HealthLevel.Healthy, "کتابخانهٔ بومی بارگذاری شد.")
            : new HealthCheck("پردازش تصویر (SkiaSharp)", HealthLevel.Critical, "کتابخانهٔ بومی بارگذاری نشد: " + skia, Hint);
        if (onnx is not null)
            yield return new HealthCheck("خواندن کد امنیتی پست (OnnxRuntime)", HealthLevel.Warning, "کتابخانهٔ بومی بارگذاری نشد: " + onnx, Hint);
    }
}
