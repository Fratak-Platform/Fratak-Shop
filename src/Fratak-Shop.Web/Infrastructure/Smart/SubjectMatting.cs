using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Web.Infrastructure.Design;
using Microsoft.ML.OnnxRuntime;
using SkiaSharp;

namespace Fratak_Shop.Web.Infrastructure.Smart;

/// <summary>
/// دور ۳۰ (#۳) – نتیجهٔ دوربری مطمئن: PNG شفاف (بریده‌شده دور سوژه)، ابعاد، سهم سوژه و گزارش اعتبارسنجی.
/// </summary>
/// <param name="Png">عکس شفاف (فقط کادر سوژه + حاشیهٔ کوچک).</param>
/// <param name="Width">عرض PNG.</param>
/// <param name="Height">ارتفاع PNG.</param>
/// <param name="Coverage">سهم سوژه از عکس اصلی (۰ تا ۱).</param>
/// <param name="Verdict">گزارش کامل اعتبارسنجی (<see cref="CutoutMask.Check"/>).</param>
/// <param name="UsedModel">true = ماسک از مدل U²-Net آمد؛ false = فقط روش «پر کردن از لبه» (مدل نبود).</param>
public sealed record MatteResult(byte[] Png, int Width, int Height, double Coverage, MatteVerdict Verdict, bool UsedModel);

/// <summary>
/// دور ۳۰ (#۳) – «دوربری مطمئن» عکس کالا و دسته: مدل متن‌باز U²-Net کوچک (Resources/Cutout/u2netp.onnx، Apache-2.0، آفلاین و رایگان)
/// + اعتبارسنجی سخت‌گیرانه (<see cref="CutoutMask"/>) تا <b>هرگز بخشی از خود کالا پاک نشود</b>.
/// <list type="number">
/// <item>عکس به اندازهٔ کار (حداکثر <c>maxSide</c>) کوچک و روی سفید صاف می‌شود (بخش‌های نیمه‌شفاف قبلی سفید می‌شوند).</item>
/// <item>مدل روی نسخهٔ ۳۲۰×۳۲۰ اجرا و نقشهٔ احتمال سوژه به اندازهٔ کار برگردانده می‌شود.</item>
/// <item>ماسک سخت (≥ ۰٫۵) ← نگه داشتن تکهٔ اصلی و همراهانش ← پر کردن سوراخ‌های داخل کالا (مگر سوراخ واقعی هم‌رنگ زمینه).</item>
/// <item>زمینهٔ یکدست (سفید، خاکستری، رنگی): «سوژهٔ مرجع» با پر کردن از لبه ساخته و با ماسک مدل اجتماع می‌شود؛ یعنی هر
/// بخشی از کالا که مدل جا انداخته باشد برمی‌گردد (روی زمینهٔ یکدست چیزی از کالا گم نمی‌شود).</item>
/// <item>اعتبارسنجی (<see cref="CutoutMask.Check"/>): هر شک (سوژهٔ گم، مرز بی‌کنتراست، چسبیدن به لبه، مدل نامطمئن، …) ← null؛
/// یعنی عکس اصلی دست‌نخورده می‌ماند و دوربری ساخته نمی‌شود.</item>
/// <item>شفافیت نهایی (<see cref="CutoutMask.Alpha"/>) با لبهٔ نرم و «جدا کردن رنگ زمینه» از پیکسل‌های لبه (هالهٔ سفید/خاکستری نمی‌ماند).</item>
/// </list>
/// <para>مدل نبود (فایل حذف شده/هاست ناسازگار) ← فقط روی زمینهٔ یکدست با روش پر کردن از لبه و همان اعتبارسنجی.</para>
/// <para>نشست مدل یک بار (Lazy) ساخته و بین همه مشترک است؛ اجرای هم‌زمان با یک قفل یکی‌یکی می‌شود تا حافظهٔ هاست ضعیف پر نشود
/// (هر اجرا حدود ۰٫۴ ثانیه روی یک هسته).</para>
/// مثال: کتری مشکی روی میز چوبی ← مدل کتری را جدا می‌کند، کنتراست مرز ۶۰ ← PNG شفاف کتری.
/// مثال: عکس شلوغ فروشگاه که مدل سوژهٔ مشخصی در آن نمی‌بیند ← null (عکس اصلی می‌ماند).
/// </summary>
public static class SubjectMatting
{
    /// <summary>ضلع پیش‌فرض اندازهٔ کار (پیکسل).</summary>
    public const int DefaultSide = 1200;

    private static readonly object RunLock = new();
    private static readonly Lazy<string?> ModelFile = new(FindModel, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<InferenceSession?> Session = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>آخرین خطای بارگذاری مدل (برای صفحهٔ سلامت/گزارش). null = خطایی نبوده.</summary>
    public static string? LoadError { get; private set; }

    /// <summary>مسیر فایل مدل (یا null اگر کنار برنامه نیست).</summary>
    public static string? ModelPath => ModelFile.Value;

    /// <summary>آیا مدل آمادهٔ اجراست؟ (فایل هست و بارگذاری شد.)</summary>
    public static bool ModelAvailable => ModelFile.Value is not null && Session.Value is not null;

    /// <summary>
    /// دوربری مطمئن یک عکس. null = دوربری مطمئن ممکن نبود (عکس اصلی را دست نزنید).
    /// </summary>
    /// <param name="source">عکس (هر قالب رنگ؛ جهت‌گیری EXIF باید از قبل اعمال شده باشد).</param>
    /// <param name="maxSide">بیشترین ضلع اندازهٔ کار و خروجی.</param>
    /// <param name="useModel">false = فقط روش پر کردن از لبه (برای آزمون یا وقتی صراحتاً مدل نمی‌خواهیم).</param>
    public static MatteResult? Cutout(SKBitmap source, int maxSide = DefaultSide, bool useModel = true) =>
        Cutout(source, maxSide, useModel, out _);

    /// <summary>همان <see cref="Cutout(SKBitmap,int,bool)"/> با گزارش اعتبارسنجی حتی وقتی رد شد (برای ثبت دلیل).</summary>
    public static MatteResult? Cutout(SKBitmap source, int maxSide, bool useModel, out MatteVerdict? verdict)
    {
        ArgumentNullException.ThrowIfNull(source);
        verdict = null;
        if (source.Width < 16 || source.Height < 16)
            return null;
        maxSide = Math.Clamp(maxSide, 64, 2000);
        var work = Flatten(source, maxSide);
        if (work is null)
            return null;
        var (rgb, width, height) = work.Value;

        // ۱) نقشهٔ احتمال سوژه از مدل (اگر هست).
        float[]? soft = null;
        if (useModel && RunModel(rgb, width, height) is { } raw)
            soft = CutoutMask.Resize(CutoutMask.Normalize(raw), CutoutMask.ModelSide, CutoutMask.ModelSide, width, height);

        // ۲) زمینه و «سوژهٔ مرجع» (فقط روی زمینهٔ یکدست قابل اعتماد است).
        var (background, plain) = CutoutMask.BorderColor(rgb, width, height);
        bool[]? reference = null;
        if (plain)
        {
            var flood = CutoutMask.KeepMain(CutoutMask.FloodSubject(rgb, width, height, background), width, height);
            var share = Share(flood);
            // مرجع بی‌معنا (تقریباً هیچ یا تقریباً همه) کنار می‌رود.
            if (share is >= 0.02 and <= 0.92)
                reference = CutoutMask.FillHoles(flood, rgb, width, height, background);
        }

        bool[] mask;
        if (soft is not null)
        {
            mask = CutoutMask.FillHoles(CutoutMask.KeepMain(CutoutMask.Threshold(soft), width, height), rgb, width, height, plain ? background : null);
            if (reference is not null)
            {
                // اجتماع فقط وقتی مرجع «هم‌اندازهٔ معقول» ماسک مدل است؛ مرجعِ خیلی بزرگ‌تر یعنی سایه/گرادیان زمینه هم جزو آن شده
                // و اجتماع، زمینه را سوژه می‌کرد. در آن حالت Check با Recall تصمیم می‌گیرد (احتیاط: رد).
                var modelShare = Share(mask);
                var referenceShare = Share(reference);
                if (referenceShare <= Math.Max(modelShare * 1.6, modelShare + 0.08))
                {
                    mask = CutoutMask.KeepMain(CutoutMask.Union(mask, reference), width, height);
                    reference = null; // بعد از اجتماع، همهٔ مرجع داخل ماسک است.
                }
            }
        }
        else if (reference is not null)
        {
            mask = reference;
            reference = null;
        }
        else
        {
            verdict = new MatteVerdict(false, 0, 0, 1, 1, 0, 1, 0, "مدل دوربری در دسترس نیست و زمینه یکدست نیست");
            return null;
        }

        verdict = CutoutMask.Check(mask, soft, rgb, width, height, reference);
        if (!verdict.Accept)
            return null;

        var alpha = CutoutMask.Alpha(mask, soft, width, height);
        var png = Compose(rgb, alpha, width, height, plain ? background : null, out var outWidth, out var outHeight);
        return png is null ? null : new MatteResult(png, outWidth, outHeight, verdict.Coverage, verdict, soft is not null);
    }

    /// <summary>
    /// همان دوربری با خروجی سازگار با کد قدیمی (<see cref="CutoutResult"/>): برای بنرها و ابزار «دوربری» استودیو.
    /// </summary>
    public static CutoutResult? CutoutCompatible(SKBitmap source, int maxSide = DefaultSide) =>
        Cutout(source, maxSide) is { } result ? new CutoutResult(result.Png, result.Width, result.Height, result.Coverage) : null;

    /// <summary>
    /// دوربری مطمئن از بایت‌های فایل (JPEG/PNG/WebP؛ جهت EXIF اعمال می‌شود). فایل خراب/خیلی بزرگ (بیش از ۴۰ مگاپیکسل) ← null.
    /// </summary>
    public static MatteResult? Cutout(byte[] data, int maxSide, out MatteVerdict? verdict)
    {
        verdict = null;
        if (data is null || data.Length == 0)
            return null;
        try
        {
            using var skData = SKData.CreateCopy(data);
            using var codec = SKCodec.Create(skData);
            if (codec is null || (long)codec.Info.Width * codec.Info.Height > 40_000_000)
                return null;
            using var decoded = SKBitmap.Decode(codec);
            if (decoded is null)
                return null;
            using var oriented = Fratak_Shop.Web.Infrastructure.Media.MediaEndpoints.Orient(decoded, codec.EncodedOrigin);
            return Cutout(oriented ?? decoded, maxSide, true, out verdict);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// دور ۳۰ (#۵) – عکس «تمیز روی سفید خالص» از نتیجهٔ دوربری: بوم سفید به ابعاد خواسته‌شده و سوژه وسط آن (حداکثر fill از هر ضلع).
    /// زمینهٔ قبلی (لوگو، سایهٔ کثیف، خاکستری) کاملاً حذف می‌شود و همهٔ تصویرهای دسته یک‌شکل و شبیه آیکن دیده می‌شوند.
    /// مثال: کتری ۴۰۰×۶۰۰ روی بوم ۱۲۰۰×۹۰۰ با fill=۰٫۸۴ ← کتری ۵۰۴×۷۵۶ وسط بوم سفید.
    /// </summary>
    public static byte[]? OnWhite(MatteResult cutout, int width, int height, double fill = 0.84)
    {
        ArgumentNullException.ThrowIfNull(cutout);
        if (width < 16 || height < 16)
            return null;
        using var piece = SKBitmap.Decode(cutout.Png);
        if (piece is null)
            return null;
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null)
            return null;
        surface.Canvas.Clear(SKColors.White);
        var scale = Math.Min(width * fill / piece.Width, height * fill / piece.Height);
        var dw = (float)(piece.Width * scale);
        var dh = (float)(piece.Height * scale);
        using (var image = SKImage.FromBitmap(piece))
            surface.Canvas.DrawImage(image, SKRect.Create((width - dw) / 2, (height - dh) / 2, dw, dh), new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray();
    }

    /// <summary>
    /// دور ۳۰ (#۱۰) – فقط «ماسک سوژه» روی یک نسخهٔ کوچک RGB (برای پیدا کردن لوگو/نوشتهٔ بیرون از کالا در <see cref="ImageCleanup.Plan(ReadOnlySpan{byte},int,int,bool[],float[])"/>).
    /// ماسکی که اعتبارسنجی را رد کند یا نبود مدل ← null (یعنی فقط روش قدیمی نوار لبه استفاده می‌شود).
    /// </summary>
    public static (bool[] Mask, float[] Soft)? SubjectMask(byte[] rgb, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        if (width < 16 || height < 16 || rgb.Length < width * height * 3)
            return null;
        if (RunModel(rgb, width, height) is not { } raw)
            return null;
        var soft = CutoutMask.Resize(CutoutMask.Normalize(raw), CutoutMask.ModelSide, CutoutMask.ModelSide, width, height);
        var (background, plain) = CutoutMask.BorderColor(rgb, width, height);
        var mask = CutoutMask.FillHoles(CutoutMask.KeepMain(CutoutMask.Threshold(soft), width, height), rgb, width, height, plain ? background : null);
        return CutoutMask.Check(mask, soft, rgb, width, height).Accept ? (mask, soft) : null;
    }

    // ───────────────────────────── داخلی ─────────────────────────────

    /// <summary>
    /// کوچک کردن تا ضلع maxSide و صاف کردن روی سفید (RGB ۳ بایتی ردیفی). عکس خراب ← null.
    /// </summary>
    internal static (byte[] Rgb, int Width, int Height)? Flatten(SKBitmap source, int maxSide)
    {
        var scale = Math.Min(1.0, maxSide / (double)Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null)
            return null;
        surface.Canvas.Clear(SKColors.White);
        using (var image = SKImage.FromBitmap(source))
        {
            if (image is null)
                return null;
            surface.Canvas.DrawImage(image, SKRect.Create(0, 0, width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        }
        using var snapshot = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(snapshot);
        if (bitmap is null)
            return null;
        var pixels = bitmap.GetPixelSpan();
        var rgb = new byte[width * height * 3];
        var info = bitmap.Info;
        var bgra = info.ColorType == SKColorType.Bgra8888;
        for (var i = 0; i < width * height; i++)
        {
            var o = i * 4;
            rgb[i * 3] = bgra ? pixels[o + 2] : pixels[o];
            rgb[i * 3 + 1] = pixels[o + 1];
            rgb[i * 3 + 2] = bgra ? pixels[o] : pixels[o + 2];
        }
        return (rgb, width, height);
    }

    /// <summary>
    /// اجرای مدل روی نسخهٔ ۳۲۰×۳۲۰ (پیش‌پردازش rembg: تقسیم بر بیشینه، میانگین/انحراف ImageNet، ترتیب CHW). خطا/نبود مدل ← null.
    /// </summary>
    private static float[]? RunModel(byte[] rgb, int width, int height)
    {
        var session = ModelFile.Value is null ? null : Session.Value;
        if (session is null)
            return null;
        const int side = CutoutMask.ModelSide;
        var input = new float[3 * side * side];
        // نمونه‌برداری دوخطی ساده به ۳۲۰×۳۲۰ (کشیده؛ همان کاری که مدل با آن آموزش دیده).
        var plane = side * side;
        float max;
        var resized = new float[plane * 3];
        for (var y = 0; y < side; y++)
        {
            var fy = Math.Clamp((y + 0.5) * height / side - 0.5, 0, height - 1);
            var y0 = (int)fy;
            var y1 = Math.Min(height - 1, y0 + 1);
            var ty = (float)(fy - y0);
            for (var x = 0; x < side; x++)
            {
                var fx = Math.Clamp((x + 0.5) * width / side - 0.5, 0, width - 1);
                var x0 = (int)fx;
                var x1 = Math.Min(width - 1, x0 + 1);
                var tx = (float)(fx - x0);
                for (var c = 0; c < 3; c++)
                {
                    float a = rgb[(y0 * width + x0) * 3 + c], b = rgb[(y0 * width + x1) * 3 + c];
                    float d = rgb[(y1 * width + x0) * 3 + c], e = rgb[(y1 * width + x1) * 3 + c];
                    var v = ((a * (1 - tx) + b * tx) * (1 - ty) + (d * (1 - tx) + e * tx) * ty) / 255f;
                    resized[c * plane + y * side + x] = v;
                }
            }
        }
        // rembg: تقسیم بر بیشینهٔ کل تصویر (عکس تیره هم به بازهٔ کامل برسد).
        max = 1e-6f;
        foreach (var v in resized)
            if (v > max) max = v;
        float[] mean = [0.485f, 0.456f, 0.406f];
        float[] std = [0.229f, 0.224f, 0.225f];
        for (var c = 0; c < 3; c++)
            for (var i = 0; i < plane; i++)
                input[c * plane + i] = (resized[c * plane + i] / max - mean[c]) / std[c];

        lock (RunLock)
        {
            try
            {
                using var tensor = OrtValue.CreateTensorValueFromMemory(input, new long[] { 1, 3, side, side });
                using var options = new RunOptions();
                using var outputs = session.Run(options, new[] { session.InputNames[0] }, new[] { tensor }, new[] { session.OutputNames[0] });
                var output = outputs.First().GetTensorDataAsSpan<float>();
                return output.Length == plane ? output.ToArray() : null;
            }
            catch (Exception ex) when (ex is OnnxRuntimeException or ArgumentException or InvalidOperationException)
            {
                LoadError = ex.Message;
                return null;
            }
        }
    }

    /// <summary>
    /// ساخت PNG شفاف از پیکسل‌ها و شفافیت: فقط کادر سوژه (+۲٪ حاشیه). روی پیکسل‌های لبه رنگ زمینه «جدا» می‌شود:
    /// رنگ دیده‌شده = a·رنگ کالا + (1−a)·زمینه ← رنگ کالا = (دیده‌شده − (1−a)·زمینه) ÷ a.
    /// </summary>
    private static byte[]? Compose(byte[] rgb, byte[] alpha, int width, int height, (byte R, byte G, byte B)? background,
        out int outWidth, out int outHeight)
    {
        outWidth = outHeight = 0;
        int minX = width, minY = height, maxX = -1, maxY = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (alpha[y * width + x] < 8)
                    continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        if (maxX < 0)
            return null;
        var pad = Math.Max(2, (int)Math.Round(Math.Max(width, height) * 0.02));
        minX = Math.Max(0, minX - pad);
        minY = Math.Max(0, minY - pad);
        maxX = Math.Min(width - 1, maxX + pad);
        maxY = Math.Min(height - 1, maxY + pad);
        int cw = maxX - minX + 1, ch = maxY - minY + 1;
        var bg = background ?? ((byte)255, (byte)255, (byte)255);
        using var result = new SKBitmap(new SKImageInfo(cw, ch, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var span = result.GetPixelSpan();
        for (var y = 0; y < ch; y++)
        {
            for (var x = 0; x < cw; x++)
            {
                var i = (y + minY) * width + (x + minX);
                var o = (y * cw + x) * 4;
                var a = alpha[i];
                if (a == 0)
                {
                    span[o] = span[o + 1] = span[o + 2] = span[o + 3] = 0;
                    continue;
                }
                byte r = rgb[i * 3], g = rgb[i * 3 + 1], b = rgb[i * 3 + 2];
                if (a < 250)
                {
                    var f = Math.Max(a / 255.0, 0.25);
                    r = Unmix(r, bg.Item1, f);
                    g = Unmix(g, bg.Item2, f);
                    b = Unmix(b, bg.Item3, f);
                }
                span[o] = r;
                span[o + 1] = g;
                span[o + 2] = b;
                span[o + 3] = a;
            }
        }
        using var image = SKImage.FromBitmap(result);
        using var data = image?.Encode(SKEncodedImageFormat.Png, 100);
        if (data is null)
            return null;
        outWidth = cw;
        outHeight = ch;
        return data.ToArray();
    }

    /// <summary>جدا کردن رنگ زمینه از رنگ دیده‌شده (نتیجه بین ۰ و ۲۵۵).</summary>
    private static byte Unmix(byte seen, byte background, double alpha) =>
        (byte)Math.Clamp(Math.Round((seen - background * (1 - alpha)) / alpha), 0, 255);

    private static double Share(bool[] mask)
    {
        if (mask.Length == 0)
            return 0;
        var count = 0;
        foreach (var on in mask)
            if (on) count++;
        return count / (double)mask.Length;
    }

    private static string? FindModel()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Resources", "Cutout", "u2netp.onnx"),
            Path.Combine(Directory.GetCurrentDirectory(), "Resources", "Cutout", "u2netp.onnx"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static InferenceSession? Load()
    {
        var path = ModelFile.Value;
        if (path is null)
        {
            LoadError = "Resources/Cutout/u2netp.onnx پیدا نشد";
            return null;
        }
        try
        {
            using var options = new Microsoft.ML.OnnxRuntime.SessionOptions
            {
                // یک رشته: CPU سرور برای درخواست‌های مشتری آزاد می‌ماند (کار پس‌زمینه است، عجله ندارد).
                IntraOpNumThreads = 1,
                InterOpNumThreads = 1,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                // آرنای حافظه حافظهٔ گرفته‌شده را پس نمی‌دهد؛ روی هاست اشتراکی خاموش (همان تنظیم مدل کپچا).
                EnableCpuMemArena = false,
                EnableMemoryPattern = false,
            };
            return new InferenceSession(path, options);
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or DllNotFoundException or TypeInitializationException or BadImageFormatException or IOException)
        {
            LoadError = ex.Message;
            return null;
        }
    }
}
