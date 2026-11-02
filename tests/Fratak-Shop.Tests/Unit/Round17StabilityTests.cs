using System.Text.Json;
using System.Xml.Linq;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Media;
using Fratak_Shop.Web.Infrastructure.Ops;
using SkiaSharp;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// آزمون‌های پایداری مرحلهٔ هفدهم (توقف استخر IIS / HTTP Error 503 وسط کار): تغییر اندازهٔ کم‌حافظهٔ عکس، خطا ندادن تغییر اندازه
/// برای فایل خراب، تشخیص رویدادهای ویندوزِ همین سایت و توضیح فارسی آن‌ها، و تنظیم جمع‌آوری حافظه.
/// <para>همه ایزوله‌اند (بدون پایگاه داده و بدون تغییر وضعیت سراسری).</para>
/// </summary>
public class Round17StabilityTests
{
    // ═══ تغییر اندازهٔ عکس: رمزگشایی کوچک‌شده و بدون خطا ═══

    private static string WriteJpeg(int width, int height)
    {
        var path = Path.Combine(Path.GetTempPath(), "fratak-media-" + Guid.NewGuid().ToString("N") + ".jpg");
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(30, 120, 200));
            using var paint = new SKPaint { Color = SKColors.OrangeRed };
            canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 3f, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    [Fact]
    public void Large_jpeg_is_decoded_at_reduced_size_but_never_below_the_requested_width()
    {
        var path = WriteJpeg(3200, 2000);
        try
        {
            using var codec = SKCodec.Create(path);
            var size = MediaEndpoints.DecodeSize(codec, 480);
            Assert.NotNull(size);
            Assert.InRange(size.Value.Width, 480, 3199);
            Assert.True(size.Value.Width * size.Value.Height < 3200 * 2000 / 4, "رمزگشایی باید دست‌کم ۴ برابر حافظهٔ کمتری بگیرد.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Small_image_is_not_scaled_at_decode()
    {
        var path = WriteJpeg(400, 300);
        try
        {
            using var codec = SKCodec.Create(path);
            Assert.Null(MediaEndpoints.DecodeSize(codec, 480));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(480)]
    [InlineData(960)]
    public void Resize_produces_exact_width_and_aspect_ratio(int width)
    {
        var path = WriteJpeg(3200, 2000);
        try
        {
            var bytes = MediaEndpoints.Resize(path, width, SKEncodedImageFormat.Webp, 80);
            Assert.NotNull(bytes);
            using var result = SKBitmap.Decode(bytes);
            Assert.Equal(width, result.Width);
            Assert.InRange(result.Height, (int)(width * 0.625) - 1, (int)(width * 0.625) + 1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Resize_of_a_broken_file_returns_null_instead_of_throwing()
    {
        var path = Path.Combine(Path.GetTempPath(), "fratak-broken-" + Guid.NewGuid().ToString("N") + ".jpg");
        File.WriteAllBytes(path, [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5]);
        try
        {
            Assert.Null(MediaEndpoints.Resize(path, 480, SKEncodedImageFormat.Jpeg, 80));
            Assert.Null(MediaEndpoints.Resize(path + ".missing", 480, SKEncodedImageFormat.Jpeg, 80));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Pixel_limit_is_lower_in_32_bit_processes()
    {
        Assert.Equal(Environment.Is64BitProcess ? 40_000_000 : 16_000_000, MediaEndpoints.PixelLimit);
        _ = MediaEndpoints.MemoryIsTight(); // هرگز خطا نمی‌دهد
    }

    // ═══ رویدادهای ویندوز: فقط همین سایت ═══

    private static WindowsEventFilter Filter() => new(
        @"C:\Inetpub\vhosts\fratak.ir\shop.fratak.ir", "shop.fratak.ir(domain)(4.0)(pool)", new HashSet<int> { 0x1a2b, 4242 });

    [Fact]
    public void Was_event_with_our_pool_name_is_ours()
    {
        Assert.True(Filter().IsOurs("Microsoft-Windows-WAS", null,
            "Application pool 'shop.fratak.ir(domain)(4.0)(pool)' is being automatically disabled due to a series of failures in the process(es) serving that application pool."));
        Assert.False(Filter().IsOurs("Microsoft-Windows-WAS", null,
            "Application pool 'other-site.ir(domain)(4.0)(pool)' is being automatically disabled due to a series of failures in the process(es) serving that application pool."));
    }

    [Fact]
    public void Application_error_is_matched_by_faulting_process_id()
    {
        const string Ours = "Faulting application name: w3wp.exe, version: 10.0\r\nFaulting module name: libSkiaSharp.dll, version: 3.116.1\r\nException code: 0xc0000005\r\nFaulting process id: 0x1a2b\r\n";
        Assert.True(Filter().IsOurs("Application Error", null, Ours));
        Assert.False(Filter().IsOurs("Application Error", null, Ours.Replace("0x1a2b", "0x9999")));
    }

    [Fact]
    public void Runtime_and_ancm_events_are_matched_by_process_id_or_content_root()
    {
        Assert.True(Filter().IsOurs(".NET Runtime", 4242, "Application: w3wp.exe\nDescription: The process was terminated due to an unhandled exception."));
        Assert.False(Filter().IsOurs(".NET Runtime", 777, "Application: w3wp.exe\nDescription: The process was terminated due to an unhandled exception."));
        Assert.True(Filter().IsOurs("IIS AspNetCore Module V2", null,
            @"Application '/LM/W3SVC/12/ROOT' with physical root 'C:\Inetpub\vhosts\fratak.ir\shop.fratak.ir\' failed to start process."));
        Assert.True(Filter().IsOurs(".NET Runtime", 777, "Exception Info: System.InvalidOperationException\n   at Fratak_Shop.Web.Program.Main()"));
        Assert.False(Filter().IsOurs("Application Error", 4242, ""));
    }

    // ═══ توضیح فارسی رویدادها ═══

    [Fact]
    public void Rapid_fail_is_explained_and_severe()
    {
        var (title, explanation, severe) = WindowsEvents.Explain("Microsoft-Windows-WAS", 5002, "Application pool 'x' is being automatically disabled due to a series of failures");
        Assert.Contains("Rapid-Fail", title);
        Assert.Contains("503", explanation);
        Assert.True(severe);
    }

    [Theory]
    [InlineData(5010, "A process serving application pool 'x' failed to respond to a ping.", "Ping", true)]
    [InlineData(5117, "A worker process serving application pool 'x' has requested a recycle because it reached its private bytes memory limit.", "حافظه", false)]
    [InlineData(5011, "A process serving application pool 'x' suffered a fatal communication error with the Windows Process Activation Service.", "ناگهانی", true)]
    [InlineData(5186, "A worker process serving application pool 'x' was shutdown due to inactivity.", "بیکاری", false)]
    [InlineData(5076, "A worker process serving application pool 'x' has requested a recycle because the worker process reached its allowed processing time limit.", "بازیافت", false)]
    public void Was_events_are_classified(int id, string message, string expected, bool severe)
    {
        var result = WindowsEvents.Explain("Microsoft-Windows-WAS", id, message);
        Assert.Contains(expected, result.Title + result.Explanation);
        Assert.Equal(severe, result.Severe);
    }

    [Theory]
    [InlineData("libSkiaSharp.dll", "c0000005", "SkiaSharp")]
    [InlineData("coreclr.dll", "c00000fd", "StackOverflow")]
    [InlineData("onnxruntime.dll", "c0000374", "OnnxRuntime")]
    [InlineData("KERNELBASE.dll", "e0434352", ".NET")]
    public void Application_errors_name_the_library_and_the_failure(string module, string code, string expected)
    {
        var message = $"Faulting application name: w3wp.exe\nFaulting module name: {module}, version: 1.0\nException code: 0x{code}\nFaulting process id: 0x1a2b";
        var (title, explanation, severe) = WindowsEvents.Explain("Application Error", 1000, message);
        Assert.Contains(expected, title + explanation);
        Assert.True(severe);
    }

    [Fact]
    public void Unhandled_dotnet_exception_shows_the_exception_type()
    {
        var (title, explanation, severe) = WindowsEvents.Explain(".NET Runtime", 1026,
            "Application: w3wp.exe\nDescription: The process was terminated due to an unhandled exception.\nException Info: System.OutOfMemoryException\n   at X");
        Assert.Contains("مدیریت‌نشده", title);
        Assert.Contains("System.OutOfMemoryException", explanation);
        Assert.True(severe);
    }

    [Fact]
    public void Ancm_start_failure_is_severe()
    {
        var result = WindowsEvents.Explain("IIS AspNetCore Module V2", 1000, "Application '/LM/W3SVC/12/ROOT' with physical root 'C:\\x\\' failed to start process with commandline 'dotnet .\\Fratak-Shop.Web.dll'");
        Assert.Equal("برنامه روشن نشد", result.Title);
        Assert.True(result.Severe);
    }

    [Fact]
    public async Task Reading_windows_events_never_throws()
    {
        var (events, problem) = await WindowsEvents.RecentAsync(AppContext.BaseDirectory, CancellationToken.None);
        Assert.NotNull(events);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Empty(events);
            Assert.Null(problem);
        }
    }

    // ═══ بررسی سلامت «علت توقف‌ها» ═══

    [Fact]
    public void Windows_crash_check_warns_only_for_recent_severe_events()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Null(ProductionDiagnostics.WindowsCrashes(false, [], null, now));

        var old = new WindowsEvent(now.AddDays(-2), "Microsoft-Windows-WAS", 5002, "Rapid-Fail", "-", "-", true);
        var mild = new WindowsEvent(now.AddHours(-1), "Microsoft-Windows-WAS", 5186, "بیکاری", "-", "-", false);
        Assert.Equal(HealthLevel.Healthy, ProductionDiagnostics.WindowsCrashes(true, [old, mild], null, now)!.Level);

        var recent = old with { At = now.AddHours(-2) };
        var check = ProductionDiagnostics.WindowsCrashes(true, [mild, recent], null, now)!;
        Assert.Equal(HealthLevel.Warning, check.Level);
        Assert.Contains("Rapid-Fail", check.Summary);

        Assert.Equal(HealthLevel.Healthy, ProductionDiagnostics.WindowsCrashes(true, [], "اجازه ندارد", now)!.Level);
    }

    // ═══ نبض و حافظهٔ اجراها ═══

    [Fact]
    public void Lifecycle_run_keeps_backward_compatible_json()
    {
        var oldJson = """[{"StartedAt":"2026-11-01T10:00:00+00:00","StoppedAt":null,"ProcessId":12,"Version":"1.0","Crash":null}]""";
        var runs = JsonSerializer.Deserialize<List<LifecycleRun>>(oldJson)!;
        Assert.Null(runs[0].MemoryMb);
        Assert.Equal("-", Fratak_Shop.Web.Areas.Admin.Pages.SystemPages.AccessModel.Memory(runs[0]));

        var withMemory = runs[0] with { MemoryMb = 410, PeakMemoryMb = 880, LastSeenAt = DateTimeOffset.UtcNow };
        var text = Fratak_Shop.Web.Areas.Admin.Pages.SystemPages.AccessModel.Memory(withMemory);
        Assert.Contains("بیشینه", text);
        Assert.Contains("MB", text);
    }

    [Fact]
    public void Heartbeat_never_throws()
    {
        AppLifecycle.Heartbeat();
    }

    // ═══ جمع‌آوری حافظه: Workstation هم‌زمان (حافظهٔ کمتر روی هاست اشتراکی) ═══

    [Fact]
    public void Web_project_uses_workstation_concurrent_gc()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Fratak-Shop.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var project = XDocument.Load(Path.Combine(directory!.FullName, "src", "Fratak-Shop.Web", "Fratak-Shop.Web.csproj"));
        Assert.Equal("false", project.Descendants("ServerGarbageCollection").Single().Value);
        Assert.Equal("true", project.Descendants("ConcurrentGarbageCollection").Single().Value);
    }
}
