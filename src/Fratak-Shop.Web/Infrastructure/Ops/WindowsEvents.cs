using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Fratak_Shop.Web.Infrastructure.Ops;

/// <summary>یک رویداد ویندوز مربوط به همین سایت، با توضیح ساده.</summary>
/// <param name="At">زمان رویداد.</param>
/// <param name="Source">منبع (مثلاً «Microsoft-Windows-WAS» یا «Application Error»).</param>
/// <param name="Id">شمارهٔ رویداد (مثلاً ۵۰۰۲ = خاموش شدن استخر با Rapid-Fail).</param>
/// <param name="Title">عنوان فارسی کوتاه.</param>
/// <param name="Explanation">توضیح ساده و کاری که باید کرد.</param>
/// <param name="Message">متن اصلی رویداد (کوتاه‌شده) برای جزئیات.</param>
/// <param name="Severe">باعث بسته شدن برنامه یا خاموش شدن استخر شده است؟</param>
public sealed record WindowsEvent(DateTimeOffset At, string Source, int Id, string Title, string Explanation, string Message, bool Severe);

/// <summary>
/// تشخیص این رویداد مال همین سایت است (روی سرور اشتراکی رویداد سایت‌های دیگر هم در گزارش ویندوز هست).
/// </summary>
/// <param name="ContentRoot">پوشهٔ برنامه (مثلاً C:\Inetpub\vhosts\fratak.ir\shop.fratak.ir).</param>
/// <param name="AppPool">نام استخر IIS (متغیر محیطی APP_POOL_ID که IIS برای پروسهٔ برنامه می‌گذارد).</param>
/// <param name="ProcessIds">شمارهٔ پروسه‌های اجراهای ثبت‌شدهٔ همین برنامه (lifecycle.json).</param>
public sealed record WindowsEventFilter(string ContentRoot, string? AppPool, IReadOnlySet<int> ProcessIds)
{
    private static readonly Regex FaultingPid = new(@"(?i)faulting process id:\s*0x(?<pid>[0-9a-f]+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// آیا این رویداد مال همین سایت است؟ مثال: پیام WAS «application pool 'shop.fratak.ir(domain)(4.0)(pool)'» با همان نام
    /// استخر ← true؛ «Faulting process id: 0x1a2b» وقتی 0x1a2b شمارهٔ یکی از اجراهای ثبت‌شده است ← true؛ سایت دیگر ← false.
    /// </summary>
    public bool IsOurs(string? source, int? processId, string message)
    {
        if (string.IsNullOrEmpty(message))
            return false;
        // رویدادهایی که خود پروسهٔ برنامه ثبت کرده (.NET Runtime و ماژول ASP.NET Core در IIS).
        if (processId is { } pid && ProcessIds.Contains(pid) && source is not null
            && (source.Equals(".NET Runtime", StringComparison.OrdinalIgnoreCase) || source.StartsWith("IIS AspNetCore Module", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (!string.IsNullOrWhiteSpace(AppPool) && message.Contains("'" + AppPool + "'", StringComparison.OrdinalIgnoreCase))
            return true;
        var root = ContentRoot.TrimEnd('\\', '/');
        if (root.Length > 3 && message.Contains(root, StringComparison.OrdinalIgnoreCase))
            return true;
        if (message.Contains("Fratak_Shop", StringComparison.Ordinal) || message.Contains("Fratak-Shop.Web", StringComparison.OrdinalIgnoreCase))
            return true;
        try
        {
            var match = FaultingPid.Match(message);
            return match.Success && int.TryParse(match.Groups["pid"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var faulting)
                   && ProcessIds.Contains(faulting);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}

/// <summary>
/// خواندن «علت واقعی» توقف‌های برنامه از گزارش رویدادهای ویندوز (مرحلهٔ هفدهم).
/// <para>چرا: وقتی IIS استخر برنامه را خاموش می‌کند (صفحهٔ انگلیسی «HTTP Error 503. The service is unavailable» و پیام Plesk
/// «applications pool is not running, but it was not stopped through Plesk»)، خود برنامه دیگر فرصت ثبت چیزی ندارد. علت را
/// ویندوز ثبت می‌کند: WAS (رویداد ۵۰۰۲ خاموش شدن با Rapid-Fail، ۵۰۱۰ جواب ندادن به Ping، ۵۰۱۱/۵۰۰۹ بسته شدن ناگهانی، سقف حافظه)،
/// «Application Error» (کدام کتابخانه و چه خطایی پروسه را بست، مثلاً StackOverflow یا خطای کتابخانهٔ تصویر) و «.NET Runtime»
/// (خطای مدیریت‌نشده با Stack trace). این کلاس همان‌ها را برای همین سایت پیدا و به فارسی توضیح می‌دهد.</para>
/// <para>محل نمایش: «فنی ← درخواست‌های ردشده و پایداری» و بررسی «علت توقف‌ها» در صفحهٔ سلامت. فقط روی ویندوز؛ اگر هاست اجازهٔ
/// خواندن گزارش را نداده باشد، پیام روشن نشان داده می‌شود. نتیجه ۲ دقیقه نگه داشته می‌شود و خواندن حداکثر ۸ ثانیه طول می‌کشد.</para>
/// </summary>
public static class WindowsEvents
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(2);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Snapshot? _cache;

    private sealed record Snapshot(DateTimeOffset At, IReadOnlyList<WindowsEvent> Events, string? Problem);

    private static readonly Regex Module = new(@"(?i)faulting module name:\s*(?<m>[^,\r\n]+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Code = new(@"(?i)exception code:\s*0x(?<c>[0-9a-f]{8})", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex ExceptionInfo = new(@"(?i)exception info:\s*(?<e>[^\r\n]+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// رویدادهای ۳ روز اخیر همین سایت (تازه‌ترین اول، حداکثر ۴۰) و اگر خواندن ممکن نبود، دلیلش.
    /// روی لینوکس همیشه فهرست خالی و بدون مشکل برمی‌گرداند.
    /// </summary>
    public static async Task<(IReadOnlyList<WindowsEvent> Events, string? Problem)> RecentAsync(string contentRoot, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            return (Array.Empty<WindowsEvent>(), null);
        if (_cache is { } fresh && DateTimeOffset.UtcNow - fresh.At < CacheFor)
            return (fresh.Events, fresh.Problem);

        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache is { } again && DateTimeOffset.UtcNow - again.At < CacheFor)
                return (again.Events, again.Problem);

            var pids = AppLifecycle.Runs().Select(r => r.ProcessId).Append(Environment.ProcessId).ToHashSet();
            var filter = new WindowsEventFilter(contentRoot, Environment.GetEnvironmentVariable("APP_POOL_ID"), pids);
            Snapshot snapshot;
            try
            {
                var (events, problem) = await Task.Run(() => ReadIfWindows(filter), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
                snapshot = new Snapshot(DateTimeOffset.UtcNow, events, problem);
            }
            catch (TimeoutException)
            {
                snapshot = new Snapshot(DateTimeOffset.UtcNow, [], "خواندن گزارش رویدادهای ویندوز بیش از ۸ ثانیه طول کشید.");
            }
            _cache = snapshot;
            return (snapshot.Events, snapshot.Problem);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (Array.Empty<WindowsEvent>(), "گزارش رویدادهای ویندوز خوانده نشد: " + exception.Message);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static (IReadOnlyList<WindowsEvent>, string?) ReadIfWindows(WindowsEventFilter filter) =>
        OperatingSystem.IsWindows() ? Read(filter) : (Array.Empty<WindowsEvent>(), null);

    [SupportedOSPlatform("windows")]
    private static (IReadOnlyList<WindowsEvent>, string?) Read(WindowsEventFilter filter)
    {
        const string Window = "TimeCreated[timediff(@SystemTime) <= 259200000]"; // ۳ روز
        var events = new List<WindowsEvent>();
        var problems = new List<string>();
        ReadLog("Application",
            $"*[System[(Provider[@Name='Application Error'] or Provider[@Name='.NET Runtime'] or Provider[@Name='IIS AspNetCore Module V2'] or Provider[@Name='IIS AspNetCore Module']) and {Window}]]",
            filter, events, problems);
        ReadLog("System", $"*[System[Provider[@Name='Microsoft-Windows-WAS'] and {Window}]]", filter, events, problems);
        return (events.OrderByDescending(e => e.At).Take(40).ToList(), problems.Count == 0 ? null : string.Join(" ", problems));
    }

    [SupportedOSPlatform("windows")]
    private static void ReadLog(string log, string xpath, WindowsEventFilter filter, List<WindowsEvent> events, List<string> problems)
    {
        try
        {
            using var reader = new EventLogReader(new EventLogQuery(log, PathType.LogName, xpath) { ReverseDirection = true });
            var scanned = 0;
            for (var record = reader.ReadEvent(); record is not null && scanned < 500; record = reader.ReadEvent(), scanned++)
            {
                using (record)
                {
                    var source = record.ProviderName ?? "";
                    var message = Describe(record);
                    if (!filter.IsOurs(source, record.ProcessId, message))
                        continue;
                    var (title, explanation, severe) = Explain(source, record.Id, message);
                    var at = record.TimeCreated is { } created ? new DateTimeOffset(created.ToUniversalTime(), TimeSpan.Zero) : DateTimeOffset.UtcNow;
                    events.Add(new WindowsEvent(at, source, record.Id, title, explanation, Clip(message, 1500), severe));
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            problems.Add($"هاست اجازهٔ خواندن گزارش «{log}» ویندوز را به برنامه نداده است (علت توقف را از پشتیبانی هاست بپرسید).");
        }
        catch (EventLogException exception)
        {
            problems.Add($"گزارش «{log}» ویندوز خوانده نشد: {exception.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static string Describe(EventRecord record)
    {
        try
        {
            var text = record.FormatDescription();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }
        catch (EventLogException)
        {
            // متن قالب رویداد روی این سرور نصب نیست؛ مقادیر خام استفاده می‌شوند.
        }
        return string.Join(" | ", record.Properties.Select(p => p.Value?.ToString()).Where(v => !string.IsNullOrWhiteSpace(v)));
    }

    /// <summary>
    /// توضیح فارسی یک رویداد. مثال: («Microsoft-Windows-WAS»، ۵۰۰۲، …) ← «IIS استخر برنامه را خاموش کرد (Rapid-Fail)»؛
    /// («Application Error»، ۱۰۰۰، «Faulting module name: libSkiaSharp.dll … Exception code: 0xc0000005») ← خطای کتابخانهٔ تصویر.
    /// </summary>
    public static (string Title, string Explanation, bool Severe) Explain(string source, int id, string message)
    {
        var text = message;
        if (source.Equals("Microsoft-Windows-WAS", StringComparison.OrdinalIgnoreCase))
            return ExplainWas(id, text);
        if (source.Equals("Application Error", StringComparison.OrdinalIgnoreCase))
            return ExplainFault(text);
        if (source.Equals(".NET Runtime", StringComparison.OrdinalIgnoreCase))
        {
            var info = Match(ExceptionInfo, text, "e");
            if (text.Contains("unhandled exception", StringComparison.OrdinalIgnoreCase) || info is not null)
                return ("خطای مدیریت‌نشده برنامه را بست", "برنامه با یک خطای پیش‌بینی‌نشده بسته شد" + (info is null ? "." : ": " + Clip(info, 200) + ".")
                    + " متن کامل (Stack trace) در جزئیات همین رویداد است؛ آن را برای پشتیبانی فنی بفرستید.", true);
            if (text.Contains("FailFast", StringComparison.OrdinalIgnoreCase))
                return ("توقف اضطراری .NET", "برنامه به‌دلیل یک خطای جدی داخلی خودش را بست؛ جزئیات را برای پشتیبانی فنی بفرستید.", true);
            return (".NET Runtime", FirstSentence(text), false);
        }
        if (source.StartsWith("IIS AspNetCore Module", StringComparison.OrdinalIgnoreCase))
        {
            if (text.Contains("failed to start", StringComparison.OrdinalIgnoreCase)
                || (text.Contains("startup", StringComparison.OrdinalIgnoreCase) && text.Contains("fail", StringComparison.OrdinalIgnoreCase)))
                return ("برنامه روشن نشد", "ماژول ASP.NET Core در IIS نتوانست برنامه را روشن کند (فایل ناقص، نسخهٔ نادرست Hosting Bundle یا خطای شروع). جزئیات همین رویداد را ببینید.", true);
            if (text.Contains("exited", StringComparison.OrdinalIgnoreCase) || text.Contains("crash", StringComparison.OrdinalIgnoreCase))
                return ("برنامه ناگهانی بسته شد", "پروسهٔ برنامه بیرون از روال عادی بسته شد؛ رویدادهای «Application Error» و «.NET Runtime» هم‌زمان علت را می‌گویند.", true);
            return ("ماژول ASP.NET Core", FirstSentence(text), false);
        }
        return (source, FirstSentence(text), false);
    }

    private static (string, string, bool) ExplainWas(int id, string text)
    {
        if (id == 5002 || text.Contains("series of failures", StringComparison.OrdinalIgnoreCase) || text.Contains("automatically disabled", StringComparison.OrdinalIgnoreCase))
            return ("IIS استخر برنامه را خاموش کرد (Rapid-Fail)",
                "برنامه در چند دقیقه چند بار پشت سر هم بسته شد و IIS استخر را متوقف کرد؛ تا Start دستی در Plesk سایت «HTTP Error 503» می‌دهد. "
                + "علت اصلی در رویدادهای کمی قبل از همین است (ردیف‌های پایین‌تر).", true);
        if (id == 5010 || text.Contains("ping", StringComparison.OrdinalIgnoreCase))
            return ("برنامه به بررسی سلامت IIS جواب نداد",
                "IIS برنامه را بست چون چند ثانیه به Ping جواب نداد؛ معمولاً یعنی پردازنده کاملاً مشغول یا محدود شده است (سقف CPU استخر در Plesk). "
                + "در Plesk ← Dedicated IIS Application Pool سقف CPU را بردارید یا بیشتر کنید.", true);
        if (text.Contains("memory", StringComparison.OrdinalIgnoreCase) || text.Contains("private bytes", StringComparison.OrdinalIgnoreCase))
            return ("بازیافت به‌خاطر سقف حافظهٔ استخر",
                "حافظهٔ برنامه به سقف تعیین‌شده برای استخر رسید و IIS آن را بازیافت کرد (کاربران ممکن است یک لحظه ۵۰۳ ببینند). "
                + "در Plesk ← Dedicated IIS Application Pool سقف حافظه را حداقل ۱۰۲۴ مگابایت کنید.", false);
        if (text.Contains("CPU", StringComparison.OrdinalIgnoreCase))
            return ("محدودیت پردازندهٔ استخر",
                "استخر برنامه به سقف CPU تعیین‌شده رسید و IIS بر اساس تنظیم آن (Kill/Throttle) عمل کرد. سقف را بردارید یا روی Throttle بگذارید، نه Kill.", true);
        if (id is 5009 or 5011 || text.Contains("terminated unexpectedly", StringComparison.OrdinalIgnoreCase) || text.Contains("fatal communication", StringComparison.OrdinalIgnoreCase))
            return ("پروسهٔ برنامه ناگهانی بسته شد",
                "پروسهٔ برنامه بی‌خبر بسته شد (خطای مرگبار، کمبود حافظه یا کشته شدن پروسه). رویداد «Application Error» یا «.NET Runtime» هم‌زمان، علت دقیق را می‌گوید.", true);
        if (text.Contains("idle", StringComparison.OrdinalIgnoreCase) || text.Contains("inactivity", StringComparison.OrdinalIgnoreCase))
            return ("خاموشی به‌خاطر بیکاری", "استخر چون مدتی درخواستی نداشت خاموش شد (عادی است ولی اولین بازدید بعدی کند می‌شود). Idle Time-out را ۰ بگذارید.", false);
        if (text.Contains("recycle", StringComparison.OrdinalIgnoreCase))
            return ("بازیافت استخر", "IIS استخر برنامه را بازیافت کرد (زمان‌بندی‌شده یا تغییر تنظیمات/فایل‌ها). اگر زیاد تکرار می‌شود، بازیافت دوره‌ای را در Plesk خاموش کنید.", false);
        return ("رویداد IIS (" + id.ToString(CultureInfo.InvariantCulture) + ")", FirstSentence(text), false);
    }

    private static (string, string, bool) ExplainFault(string text)
    {
        var module = Match(Module, text, "m")?.Trim() ?? "";
        var code = Match(Code, text, "c")?.ToLowerInvariant();
        var what = code switch
        {
            "c00000fd" => "سرریز پشته (StackOverflow)",
            "c0000005" => "دسترسی غیرمجاز به حافظه در کد بومی",
            "c0000374" => "خرابی حافظهٔ Heap در کد بومی",
            "c0000409" => "توقف اضطراری (FailFast)",
            "e0434352" => "خطای مدیریت‌نشدهٔ .NET",
            "c0000017" or "e06d7363" => "کمبود حافظه یا خطای کد بومی",
            null => "خطای مرگبار",
            _ => "خطای مرگبار (کد 0x" + code + ")",
        };
        var library = module.ToLowerInvariant() switch
        {
            var m when m.Contains("skia") => "کتابخانهٔ پردازش تصویر (SkiaSharp)؛ معمولاً هنگام تغییر اندازهٔ عکس‌های خیلی بزرگ یا کمبود حافظه",
            var m when m.Contains("onnx") => "کتابخانهٔ خواندن کد امنیتی رهگیری پست (OnnxRuntime)",
            var m when m.Contains("coreclr") || m.Contains("clrjit") => "خود .NET (معمولاً کمبود حافظه یا سرریز پشته)",
            var m when m.Contains("ntdll") || m.Contains("kernelbase") => "ویندوز (معمولاً کمبود حافظه یا خطای مدیریت‌نشده)",
            "" => "نامشخص",
            _ => module,
        };
        return ($"{what} پروسهٔ برنامه را بست",
            $"محل خطا: {library}. اگر بعد از چند بار تکرار IIS استخر را خاموش کرده، رویداد «Rapid-Fail» هم دیده می‌شود. "
            + "این رویداد را (با جزئیات) برای پشتیبانی فنی بفرستید.", true);
    }

    private static string? Match(Regex regex, string text, string group)
    {
        try
        {
            var match = regex.Match(text);
            return match.Success ? match.Groups[group].Value : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static string FirstSentence(string text)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        return Clip(line, 300);
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}
