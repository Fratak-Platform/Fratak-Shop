using Fratak_Shop.Core.Services.Orders;
using Microsoft.ML.OnnxRuntime;
using SkiaSharp;

namespace Fratak_Shop.Web.Infrastructure.Shipping;

/// <summary>
/// خواندن کد امنیتی عددی سایت رهگیری پست با یک مدل کوچک ONNX که کنار برنامه است (کاملاً آفلاین و رایگان).
/// <para>
/// <b>مدل:</b> <c>Resources/Captcha/digits.onnx</c> (حدود ۵ مگابایت) – نسخهٔ «فقط رقم» مدل متن‌باز ddddocr (مجوز MIT).
/// لایهٔ آخر مدل اصلی ۸۲۱۰ نویسه (چینی، لاتین، ...) را تشخیص می‌داد؛ فقط ستون‌های «خالی» و ۰ تا ۹ نگه داشته شده‌اند
/// تا خروجی همیشه رقم باشد و حروف شبیه (مثل O به جای 0) پیش نیاید. توضیح کامل در Resources/Captcha/README.md.
/// </para>
/// <para>
/// <b>پیش‌پردازش (همان روش آموزش مدل):</b> تصویر روی زمینهٔ سفید → خاکستری (0.299R + 0.587G + 0.114B) →
/// ارتفاع ۶۴ پیکسل با حفظ نسبت → تقسیم بر ۲۵۵ → تنسور [1, 1, 64, W] با نام «input1».
/// </para>
/// <para><b>خروجی:</b> امتیازهای [T, 1, 11] که <see cref="CtcDigitDecoder"/> به رشتهٔ ارقام تبدیل می‌کند.</para>
/// ثبت: Singleton در WebServiceCollectionExtensions؛ مدل اولین بار که لازم شد بارگذاری می‌شود (Lazy) و بین همهٔ درخواست‌ها
/// مشترک است (InferenceSession.Run هم‌زمان‌پذیر است). استفاده: <see cref="PostTrackingClient"/>.
/// </summary>
public sealed class OnnxCaptchaSolver : IPostCaptchaSolver, IDisposable
{
    /// <summary>ارتفاع ورودی مدل.</summary>
    public const int InputHeight = 64;

    /// <summary>سقف عرض بعد از تغییر اندازه؛ تصویر عجیب و خیلی پهن خوانده نمی‌شود (محافظ حافظه).</summary>
    private const int MaxWidth = 800;

    private readonly Lazy<InferenceSession?> _session;
    private readonly ILogger<OnnxCaptchaSolver> _logger;

    public OnnxCaptchaSolver(ILogger<OnnxCaptchaSolver> logger, IWebHostEnvironment? environment = null)
    {
        _logger = logger;
        ModelPath = FindModel(environment?.ContentRootPath);
        _session = new Lazy<InferenceSession?>(Load, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>مسیر پیدا شدهٔ فایل مدل (یا null). برای صفحهٔ وضعیت/تست.</summary>
    public string? ModelPath { get; }

    public bool IsAvailable => ModelPath is not null && _session.Value is not null;

    public string? Solve(byte[] image)
    {
        var session = ModelPath is null ? null : _session.Value;
        if (session is null || image.Length == 0)
            return null;
        try
        {
            var prepared = Prepare(image);
            if (prepared is null)
                return null;
            var (pixels, width) = prepared.Value;

            using var input = OrtValue.CreateTensorValueFromMemory(pixels, new long[] { 1, 1, InputHeight, width });
            using var options = new RunOptions();
            using var outputs = session.Run(options, new[] { session.InputNames[0] }, new[] { input }, new[] { session.OutputNames[0] });
            var output = outputs.First();
            var shape = output.GetTensorTypeAndShape().Shape;
            var classes = (int)shape[^1];
            var scores = output.GetTensorDataAsSpan<float>();
            if (classes <= 0 || scores.Length % classes != 0)
                return null;
            return CtcDigitDecoder.Decode(scores, scores.Length / classes, classes);
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or ArgumentException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Captcha model inference failed");
            return null;
        }
    }

    /// <summary>
    /// پیش‌پردازش تصویر به آرایهٔ float ردیفی (64×W) با مقادیر ۰ تا ۱. اگر تصویر قابل خواندن نبود null.
    /// عمومی و ایستا تا بدون مدل هم تست شود.
    /// </summary>
    public static (float[] Pixels, int Width)? Prepare(byte[] image)
    {
        using var source = SKBitmap.Decode(image);
        if (source is null || source.Width <= 0 || source.Height <= 0)
            return null;
        var width = (int)Math.Round(source.Width * (double)InputHeight / source.Height);
        if (width < 8 || width > MaxWidth)
            return null;

        // کشیدن روی زمینهٔ سفید: بخش‌های شفاف PNG سفید می‌شوند (مثل کاغذ) و تغییر اندازه با درون‌یابی خطی.
        using var target = new SKBitmap(new SKImageInfo(width, InputHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(target))
        using (var skImage = SKImage.FromBitmap(source))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawImage(skImage, new SKRect(0, 0, width, InputHeight), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        var bytes = target.GetPixelSpan();
        var pixels = new float[width * InputHeight];
        for (var i = 0; i < pixels.Length; i++)
        {
            var o = i * 4;
            var gray = 0.299f * bytes[o] + 0.587f * bytes[o + 1] + 0.114f * bytes[o + 2];
            pixels[i] = MathF.Round(gray) / 255f;
        }
        return (pixels, width);
    }

    /// <summary>
    /// جست‌وجوی فایل مدل: کنار فایل اجرایی (bin/publish) و بعد در ریشهٔ پروژه (اجرای dotnet run). پیدا نشد → null.
    /// </summary>
    private static string? FindModel(string? contentRoot)
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Resources", "Captcha", "digits.onnx"),
            Path.Combine(contentRoot ?? AppContext.BaseDirectory, "Resources", "Captcha", "digits.onnx"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private InferenceSession? Load()
    {
        if (ModelPath is null)
        {
            _logger.LogWarning("Captcha model file not found; Iran Post tracking needs Resources/Captcha/digits.onnx");
            return null;
        }
        try
        {
            using var options = new SessionOptions
            {
                // مدل کوچک است؛ یک رشته کافی است و CPU سرور برای کارهای اصلی آزاد می‌ماند.
                IntraOpNumThreads = 1,
                InterOpNumThreads = 1,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            };
            return new InferenceSession(ModelPath, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Captcha model could not be loaded from {Path}", ModelPath);
            return null;
        }
    }

    public void Dispose()
    {
        if (_session.IsValueCreated)
            _session.Value?.Dispose();
    }
}
