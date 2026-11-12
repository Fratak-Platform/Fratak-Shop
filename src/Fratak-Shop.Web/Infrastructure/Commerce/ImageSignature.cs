using System.Numerics;
using SkiaSharp;

namespace Fratak_Shop.Web.Infrastructure.Commerce;

/// <summary>
/// دور ۲۴ (پیشنهاد ۲۹ – جست‌وجو با عکس): «اثر انگشت» سبک یک تصویر، کاملاً روی خود سرور و بدون هیچ سرویس بیرونی.
/// <para>
/// دو بخش دارد:
/// (۱) هش تفاضلی (dHash) ۶۴ بیتی: تصویر به ۹×۸ خاکستری کوچک می‌شود و برای هر ردیف «روشن‌تر بودن هر نقطه از نقطهٔ کناری» یک بیت است.
/// دو تصویر هم‌شکل فاصلهٔ همینگ کمی دارند (مثلاً کمتر از ۱۲ از ۶۴).
/// (۲) هیستوگرام رنگ ۳۲ خانه‌ای: ۱۲ فام × ۲ سطح اشباع + ۸ سطح خاکستری؛ پیکسل‌های تقریباً سفید (پس‌زمینهٔ عکس کالا) و شفاف حساب نمی‌شوند.
/// </para>
/// <para>مثال: عکس یک کتانی قرمز ← Hash=…، Colors پر در خانه‌های قرمز؛ امتیاز شباهت با عکس کاتالوگ همان کتانی معمولاً بالای ۰٫۷ است.</para>
/// <para>محل استفاده: <see cref="ImageSearchService"/> (ساخت امضای کالاها و مقایسه با عکس مشتری) و آزمون‌های واحد.</para>
/// </summary>
public static class ImageSignature
{
    /// <summary>بیشترین حجم عکس ورودی مشتری (۴ مگابایت).</summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    /// <summary>بیشترین تعداد پیکسل عکس (جلوگیری از «بمب تصویری»).</summary>
    public const long MaxPixels = 40_000_000;

    /// <summary>تعداد خانه‌های هیستوگرام رنگ.</summary>
    public const int Bins = 32;

    /// <summary>
    /// امضای یک تصویر. Hash = هش تفاضلی (dHash)؛ دور ۲۶: PHash = هش ادراکی DCT (در برابر فشرده‌سازی، تغییر اندازه و کمی برش مقاوم‌تر).
    /// </summary>
    public sealed record Signature(long Hash, byte[] Colors, long PHash = 0);

    /// <summary>
    /// دور ۲۶: امضای ذخیره‌شدهٔ یک کالا برای مقایسه: شکل همهٔ عکس‌های کالا (هر عکس یک جفت dHash/pHash) + رنگ عکس اصلی.
    /// مثال: کالایی با ۳ عکس ← Shapes با ۳ جفت؛ مشتری عکس دوم را بفرستد هم پیدا می‌شود.
    /// </summary>
    /// <param name="ProductId">کالا.</param>
    /// <param name="Shapes">جفت هش شکل هر عکس (اصلی اول).</param>
    /// <param name="Colors">هیستوگرام رنگ عکس اصلی.</param>
    /// <param name="Palettes">دور ۳۰ (#۱): هیستوگرام رنگ «هر عکس» هم‌ترتیب با Shapes (یا null برای امضای قدیمی ← Colors).</param>
    public sealed record Stored(Guid ProductId, IReadOnlyList<(long DHash, long PHash)> Shapes, byte[] Colors, IReadOnlyList<byte[]>? Palettes = null);

    /// <summary>
    /// امضای بایت‌های یک تصویر (JPEG/PNG/WebP/GIF). تصویر خراب، خیلی بزرگ یا غیرتصویری ← null (هرگز استثنا نمی‌دهد).
    /// عکس‌های بزرگ از همان ابتدا کوچک‌شده باز می‌شوند تا حافظه کم مصرف شود.
    /// </summary>
    public static Signature? Compute(byte[] data)
    {
        if (data is null || data.Length == 0 || data.Length > MaxBytes * 4)
            return null;
        try
        {
            using var stream = new SKMemoryStream(data);
            using var codec = SKCodec.Create(stream);
            if (codec is null)
                return null;
            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels)
                return null;
            using var decoded = DecodeSmall(codec);
            if (decoded is null)
                return null;
            using var bitmap = Orient(decoded, codec.EncodedOrigin);
            return Compute(bitmap);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// امضای یک تصویر بازشده. دور ۲۶: اول حاشیهٔ یک‌رنگ دور تصویر (سفید/شفاف/هر رنگ یکدست؛ مثلاً حاشیهٔ اسکرین‌شات یا
    /// پس‌زمینهٔ عکس کالا) بریده می‌شود تا «همان عکس» با حاشیهٔ متفاوت هم یکی دیده شود. بعد dHash، pHash و رنگ ساخته می‌شوند.
    /// </summary>
    public static Signature? Compute(SKBitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return null;
        var box = TrimBox(bitmap);
        if (box == new SKRectI(0, 0, bitmap.Width, bitmap.Height))
            return ComputeRaw(bitmap);
        using var trimmed = new SKBitmap();
        if (!bitmap.ExtractSubset(trimmed, box))
            return ComputeRaw(bitmap);
        return ComputeRaw(trimmed);
    }

    /// <summary>
    /// دور ۲۶ – چند امضا برای عکس مشتری: (۱) بعد از بریدن حاشیه، (۲) کل قاب، (۳) وسط تصویر (۷۰٪) برای عکسی که دور کالا شلوغ است
    /// (مثلاً عکس گوشی از روی میز). امتیاز نهایی = بهترین تطبیق میان این‌ها. تصویر نامعتبر ← فهرست خالی.
    /// </summary>
    public static List<Signature> ComputeVariants(byte[] data)
    {
        var list = new List<Signature>();
        if (data is null || data.Length == 0 || data.Length > MaxBytes * 4)
            return list;
        try
        {
            using var stream = new SKMemoryStream(data);
            using var codec = SKCodec.Create(stream);
            if (codec is null)
                return list;
            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels)
                return list;
            using var decoded = DecodeSmall(codec);
            if (decoded is null)
                return list;
            using var bitmap = Orient(decoded, codec.EncodedOrigin);
            if (Compute(bitmap) is { } trimmed)
                list.Add(trimmed);
            if (ComputeRaw(bitmap) is { } full && list.All(x => x.PHash != full.PHash || x.Hash != full.Hash))
                list.Add(full);
            var w = bitmap.Width;
            var h = bitmap.Height;
            var center = new SKRectI((int)(w * 0.15), (int)(h * 0.15), (int)Math.Ceiling(w * 0.85), (int)Math.Ceiling(h * 0.85));
            if (center.Width >= 8 && center.Height >= 8)
            {
                using var middle = new SKBitmap();
                if (bitmap.ExtractSubset(middle, center) && ComputeRaw(middle) is { } mid)
                    list.Add(mid);
            }
            // دور ۲۹ (#۱): «سوژهٔ جداشده» – زمینهٔ شلوغ عکس مشتری (میز، دست، اتاق) حذف و کالا روی زمینهٔ شفاف گذاشته می‌شود؛
            // درست مثل عکس اصلی کاتالوگ (سفید/دوربری‌شده). (نسخهٔ آینه‌ای عمداً ساخته نمی‌شود: کالای دیگری با جهت برعکس را «همان» می‌دید.)
            using var subject = Isolate(bitmap);
            if (subject is not null && Compute(subject) is { } isolated)
                list.Add(isolated);
            // دور ۳۰ (#۱): اسکرین‌شات صفحهٔ کالا/پنل ← فقط بزرگ‌ترین تکه (خود عکس کالا)، هم با حاشیه و هم بدون آن.
            if (MainPartBox(bitmap) is { } part)
            {
                using var crop = new SKBitmap();
                if (bitmap.ExtractSubset(crop, part))
                {
                    if (ComputeRaw(crop) is { } partRaw && list.All(x => x.PHash != partRaw.PHash || x.Hash != partRaw.Hash))
                        list.Add(partRaw);
                    if (Compute(crop) is { } partTrim && list.All(x => x.PHash != partTrim.PHash || x.Hash != partTrim.Hash))
                        list.Add(partTrim);
                }
            }
        }
        catch (Exception)
        {
            list.Clear();
        }
        return list;
    }

    /// <summary>
    /// دور ۲۹ (#۱) – رنگ‌های «زمینه» از روی حاشیهٔ تصویر: حاشیهٔ ۳ نقطه‌ای نسخهٔ ۶۴×۶۴ به خانه‌های رنگی درشت تقسیم می‌شود و
    /// تا ۳ رنگ پرتکرار (هر کدام دست‌کم ۸٪ حاشیه) زمینه حساب می‌شوند. اگر این رنگ‌ها کمتر از نیمی از حاشیه را بپوشانند (زمینهٔ خیلی شلوغ)
    /// ← null (جدا کردن سوژه مطمئن نیست). مثال: عکس لیوان روی میز چوبی کرم ← [کرم، قهوه‌ای روشن].
    /// </summary>
    public static List<SKColor>? BackgroundPalette(SKBitmap small)
    {
        var w = small.Width;
        var h = small.Height;
        if (w < 16 || h < 16)
            return null;
        var buckets = new Dictionary<int, (long R, long G, long B, int N)>();
        var ring = 0;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (x >= 3 && x < w - 3 && y >= 3 && y < h - 3)
                    continue;
                var c = small.GetPixel(x, y);
                ring++;
                // شفاف = سفید (مثل بقیهٔ امضا).
                var (r, g, b) = c.Alpha < 128 ? (255, 255, 255) : ((int)c.Red, (int)c.Green, (int)c.Blue);
                var key = (r >> 5) << 6 | (g >> 5) << 3 | (b >> 5);
                buckets.TryGetValue(key, out var acc);
                buckets[key] = (acc.R + r, acc.G + g, acc.B + b, acc.N + 1);
            }
        }
        var top = buckets.Values.Where(v => v.N >= ring * 0.08).OrderByDescending(v => v.N).Take(3).ToList();
        if (top.Count == 0 || top.Sum(v => v.N) < ring * 0.5)
            return null;
        return top.Select(v => new SKColor((byte)(v.R / v.N), (byte)(v.G / v.N), (byte)(v.B / v.N))).ToList();
    }

    /// <summary>فاصلهٔ رنگ تا نزدیک‌ترین رنگ زمینه (اقلیدسی RGB). پیکسل شفاف ← ۰ (زمینه).</summary>
    private static double BackgroundDistance(SKColor c, IReadOnlyList<SKColor> palette)
    {
        if (c.Alpha < 128)
            return 0;
        var best = double.MaxValue;
        foreach (var p in palette)
        {
            double dr = c.Red - p.Red, dg = c.Green - p.Green, db = c.Blue - p.Blue;
            var d = Math.Sqrt(dr * dr + dg * dg + db * db);
            if (d < best)
                best = d;
        }
        return best;
    }

    /// <summary>کمترین فاصلهٔ رنگ از زمینه برای «سوژه» بودن یک پیکسل.</summary>
    public const double SubjectDistance = 56;

    /// <summary>
    /// دور ۲۹ (#۱) – کادر سوژهٔ اصلی: پیکسل‌هایی که رنگشان از همهٔ رنگ‌های زمینه دور است «سوژه»اند؛ کادر = صدک ۲ تا ۹۸ مختصات آن‌ها
    /// به‌اضافهٔ ۴٪ حاشیه (نویز پراکنده کادر را بزرگ نمی‌کند). سوژهٔ خیلی کوچک (کمتر از ۳٪) یا تمام‌قاب (بیش از ۹۰٪) یا زمینهٔ نامطمئن ← null.
    /// مثال: عکس ۴۰۰×۳۰۰ قوری سفید وسط میز قهوه‌ای ← کادر دور قوری.
    /// </summary>
    public static SKRectI? SubjectBox(SKBitmap bitmap)
    {
        if (bitmap.Width < 16 || bitmap.Height < 16)
            return null;
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
        using var small = bitmap.Resize(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Unpremul), sampling);
        if (small is null || BackgroundPalette(small) is not { } palette)
            return null;
        var xs = new List<int>();
        var ys = new List<int>();
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                if (BackgroundDistance(small.GetPixel(x, y), palette) > SubjectDistance)
                {
                    xs.Add(x);
                    ys.Add(y);
                }
            }
        }
        var share = xs.Count / (64.0 * 64.0);
        if (share < 0.03 || share > 0.9)
            return null;
        xs.Sort();
        ys.Sort();
        int Pick(List<int> v, double q) => v[Math.Clamp((int)Math.Round(q * (v.Count - 1)), 0, v.Count - 1)];
        double left = Pick(xs, 0.02), right = Pick(xs, 0.98) + 1, top = Pick(ys, 0.02), bottom = Pick(ys, 0.98) + 1;
        var margin = 64 * 0.04;
        var sx = bitmap.Width / 64.0;
        var sy = bitmap.Height / 64.0;
        var box = new SKRectI(
            Math.Max(0, (int)Math.Floor((left - margin) * sx)), Math.Max(0, (int)Math.Floor((top - margin) * sy)),
            Math.Min(bitmap.Width, (int)Math.Ceiling((right + margin) * sx)), Math.Min(bitmap.Height, (int)Math.Ceiling((bottom + margin) * sy)));
        return box.Width >= 8 && box.Height >= 8 ? box : null;
    }

    /// <summary>
    /// دور ۲۹ (#۱) – سوژهٔ جداشده: برش به <see cref="SubjectBox"/> و شفاف کردن پیکسل‌های هم‌رنگ زمینه (حداکثر ۱۶۰ نقطه در ضلع بزرگ).
    /// نتیجه شبیه عکس اصلی کاتالوگ (کالا روی زمینهٔ سفید/شفاف) است. سوژهٔ نامطمئن ← null. خروجی را فراخواننده Dispose می‌کند.
    /// </summary>
    public static SKBitmap? Isolate(SKBitmap bitmap)
    {
        if (SubjectBox(bitmap) is not { } box)
            return null;
        using var crop = new SKBitmap();
        if (!bitmap.ExtractSubset(crop, box))
            return null;
        var scale = Math.Min(1.0, 160.0 / Math.Max(box.Width, box.Height));
        var info = new SKImageInfo(Math.Max(8, (int)Math.Round(box.Width * scale)), Math.Max(8, (int)Math.Round(box.Height * scale)),
            SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var result = crop.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        if (result is null)
            return null;
        // زمینه از حاشیهٔ تصویر کامل (نه برش) حدس زده می‌شود.
        using var small = bitmap.Resize(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        var palette = small is null ? null : BackgroundPalette(small);
        if (palette is null)
        {
            result.Dispose();
            return null;
        }
        for (var y = 0; y < result.Height; y++)
        {
            for (var x = 0; x < result.Width; x++)
            {
                if (BackgroundDistance(result.GetPixel(x, y), palette) <= SubjectDistance)
                    result.SetPixel(x, y, SKColors.Transparent);
            }
        }
        return result;
    }

    /// <summary>
    /// دور ۲۶: کادر محتوای واقعی تصویر بعد از کنار گذاشتن حاشیهٔ یکدست. رنگ پس‌زمینه از ۴ گوشه حدس زده می‌شود (اگر گوشه‌ها
    /// هم‌رنگ نباشند یعنی حاشیه‌ای نیست ← کل تصویر). پیکسل شفاف مثل سفید حساب می‌شود. کادر خیلی کوچک (کمتر از ۴٪) ← کل تصویر.
    /// مثال: کالای قرمز وسط بوم سفید ۲۰۰×۲۰۰ ← فقط کادر دور کالا.
    /// </summary>
    public static SKRectI TrimBox(SKBitmap bitmap)
    {
        var w = bitmap.Width;
        var h = bitmap.Height;
        var all = new SKRectI(0, 0, w, h);
        if (w < 8 || h < 8)
            return all;
        var corners = new[] { Flat(bitmap.GetPixel(0, 0)), Flat(bitmap.GetPixel(w - 1, 0)), Flat(bitmap.GetPixel(0, h - 1)), Flat(bitmap.GetPixel(w - 1, h - 1)) };
        var bg = corners[0];
        if (corners.Any(c => Distance(c, bg) > 60))
            return all;
        // دور ۳۰ (#۱): تکه‌های کوچکِ جدا در نوار لبه (لوگوی کم‌رنگ فروشگاه روی نسخهٔ نمایشی، مهر/آدرس سایت) جزو کادر نیستند؛
        // وگرنه عکسِ «همان کالا با لوگو» کادر دیگری می‌گرفت و هشش با عکس کاتالوگ نمی‌خواند.
        var foreground = new bool[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                foreground[y * w + x] = Distance(Flat(bitmap.GetPixel(x, y)), bg) > 48;
        var stray = Fratak_Shop.Core.Services.Smart.ImageCleanup.StrayIslands(foreground, w, h);
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (!foreground[y * w + x] || stray[y * w + x])
                    continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        if (maxX < 0)
            return all;
        var box = new SKRectI(Math.Max(0, minX - 1), Math.Max(0, minY - 1), Math.Min(w, maxX + 2), Math.Min(h, maxY + 2));
        if ((long)box.Width * box.Height < (long)w * h * 0.04 || box.Width < 8 || box.Height < 8)
            return all;
        return box;
    }

    /// <summary>
    /// دور ۲۶: هش ادراکی (pHash): تصویر ۳۲×۳۲ خاکستری ← تبدیل کسینوسی (DCT) ← ۸×۸ فرکانس پایین؛ هر بیت = بزرگ‌تر از میانه.
    /// برخلاف dHash، فشرده‌سازی JPEG/WebP و تغییر اندازه تقریباً عوضش نمی‌کند. مثال: همان عکس با کیفیت ۵۰٪ ← فاصلهٔ همینگ معمولاً کمتر از ۶.
    /// </summary>
    public static long PerceptualHash(SKBitmap bitmap)
    {
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        using var small = bitmap.Resize(new SKImageInfo(32, 32, SKColorType.Rgba8888, SKAlphaType.Unpremul), sampling);
        if (small is null)
            return 0;
        const int n = 32;
        var pixels = new double[n, n];
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
                pixels[y, x] = Gray(small.GetPixel(x, y));
        // DCT جداپذیر: اول ردیف‌ها بعد ستون‌ها؛ فقط ۸ ضریب اول لازم است.
        var rows = new double[n, 8];
        for (var y = 0; y < n; y++)
            for (var u = 0; u < 8; u++)
            {
                double sum = 0;
                for (var x = 0; x < n; x++)
                    sum += pixels[y, x] * Cos[u, x];
                rows[y, u] = sum;
            }
        var coeffs = new double[64];
        for (var v = 0; v < 8; v++)
            for (var u = 0; u < 8; u++)
            {
                double sum = 0;
                for (var y = 0; y < n; y++)
                    sum += rows[y, u] * Cos[v, y];
                coeffs[v * 8 + u] = sum;
            }
        // میانه بدون ضریب DC (روشنایی کل تصویر)
        var sorted = coeffs.Skip(1).OrderBy(c => c).ToArray();
        var median = (sorted[31] + sorted[32]) / 2;
        long hash = 0;
        for (var i = 0; i < 64; i++)
        {
            if (coeffs[i] > median)
                hash |= 1L << i;
        }
        return hash;
    }

    private static readonly double[,] Cos = BuildCos();

    private static double[,] BuildCos()
    {
        var table = new double[8, 32];
        for (var u = 0; u < 8; u++)
            for (var x = 0; x < 32; x++)
                table[u, x] = Math.Cos((2 * x + 1) * u * Math.PI / 64);
        return table;
    }

    /// <summary>
    /// دور ۲۶: امتیاز شباهت با امضای ذخیره‌شدهٔ یک کالا (۰ تا ۱): بهترین تطبیق میان همهٔ عکس‌های کالا و همهٔ نسخه‌های عکس مشتری.
    /// شکل (۶۵٪) = ۶۰٪ pHash + ۴۰٪ dHash (هر کدام با سقف فاصلهٔ ۴۰ بیت) و رنگ (۳۵٪) از عکس اصلی.
    /// مثال: همان عکس ← حدود ۱؛ دو عکس بی‌ربط ← حدود ۰٫۳.
    /// </summary>
    public static double Score(IReadOnlyList<Signature> queries, Stored candidate)
    {
        // (ثابت‌ها: ShapeFloor و ColorOnlyCap پایین همین کلاس.)
        double best = 0;
        foreach (var query in queries)
        {
            var primaryColor = ColorSimilarity(query.Colors, candidate.Colors);
            for (var index = 0; index < candidate.Shapes.Count; index++)
            {
                var (dHash, pHash) = candidate.Shapes[index];
                // دور ۳۰ (#۱): رنگ همان عکسی که شکلش مقایسه می‌شود (نه فقط عکس اصلی)؛ عکس دوم کالا هم امتیاز رنگ کامل می‌گیرد.
                var palette = candidate.Palettes is { } palettes && index < palettes.Count && palettes[index] is { Length: Bins } own ? own : null;
                var color = palette is null ? primaryColor : Math.Max(primaryColor, ColorSimilarity(query.Colors, palette));
                var shapeD = Math.Clamp(1 - Hamming(query.Hash, dHash) / 40.0, 0, 1);
                var shapeP = Math.Clamp(1 - Hamming(query.PHash, pHash) / 40.0, 0, 1);
                var score = 0.65 * (0.6 * shapeP + 0.4 * shapeD) + 0.35 * color;
                // دور ۲۹ (#۱): شکل کاملاً متفاوت (یکی از دو هش در حد تصادفی) ← فقط هم‌رنگ است، نه همان کالا؛ هرگز نشان داده نمی‌شود.
                // (امضای قدیمی بدون pHash فقط با dHash سنجیده می‌شود.)
                if ((pHash == 0 ? shapeD : Math.Min(shapeD, shapeP)) < ShapeFloor)
                    score = Math.Min(score, ColorOnlyCap);
                // دور ۳۰ (#۱): «همان عکس» (هر دو هش تقریباً یکسان و هر دو پرمعنا) همیشه پیدا می‌شود، حتی اگر رنگ‌ها کمی فرق کنند
                // (لوگوی کم‌رنگ فروشگاه، فشرده‌سازی، زمینهٔ سفیدشده).
                if (IsExact(query, dHash, pHash))
                    score = Math.Max(score, ExactMatchScore);
                if (score > best)
                    best = score;
            }
        }
        return best;
    }

    /// <summary>دور ۳۰ (#۱): امتیاز تضمینی «همان عکس».</summary>
    public const double ExactMatchScore = 0.96;

    /// <summary>دور ۳۰ (#۱): بیشترین فاصلهٔ pHash برای «همان عکس».</summary>
    public const int ExactPHashDistance = 6;

    /// <summary>دور ۳۰ (#۱): بیشترین فاصلهٔ dHash برای «همان عکس».</summary>
    public const int ExactDHashDistance = 8;

    /// <summary>
    /// دور ۳۰ (#۱) – «همان عکس»؟ هر دو هش نزدیک (pHash ≤ ۶ و dHash ≤ ۸ بیت) و هر دو «پرمعنا»: هشِ تقریباً همه‌صفر/همه‌یک مال
    /// تصویر یکدست یا شیب ساده است و دو تصویر بی‌ربطِ ساده را یکی می‌دید (مثلاً دو شیب رنگی متفاوت)؛ آن‌ها فقط با امتیاز عادی سنجیده می‌شوند.
    /// مثال: عکس کالا که مشتری از خود سایت ذخیره کرده (با لوگوی کم‌رنگ و JPEG) ← true؛ شیب آبی و شیب زرد ← false.
    /// </summary>
    public static bool IsExact(Signature query, long dHash, long pHash) =>
        pHash != 0 && Informative(query.PHash) && Informative(pHash) && Informative(query.Hash) && Informative(dHash)
        && Hamming(query.PHash, pHash) <= ExactPHashDistance && Hamming(query.Hash, dHash) <= ExactDHashDistance;

    /// <summary>هش پرمعنا: بین ۱۰ و ۵۴ بیتِ یک (نه یکدست، نه شیب ساده).</summary>
    private static bool Informative(long hash)
    {
        var bits = BitOperations.PopCount((ulong)hash);
        return bits is >= 10 and <= 54;
    }

    /// <summary>
    /// دور ۲۹ (#۱): کمترین شباهت هر هش شکل (۰٫۲۵ = فاصلهٔ ۳۰ بیت از ۶۴؛ دو عکس بی‌ربط معمولاً حدود ۳۲ بیت فاصله دارند).
    /// </summary>
    public const double ShapeFloor = 0.25;

    /// <summary>دور ۲۹ (#۱): سقف امتیاز عکسی که فقط رنگش شبیه است (کمتر از حد «شاید» ۰٫۴۷ ← نشان داده نمی‌شود).</summary>
    public const double ColorOnlyCap = 0.44;

    /// <summary>
    /// دور ۲۶: بسته‌بندی جفت هش‌های چند عکس در یک آرایهٔ بایت (هر عکس ۱۶ بایت؛ حداکثر <see cref="MaxShapes"/> عکس) برای ستون Hashes.
    /// </summary>
    public static byte[] Pack(IEnumerable<(long DHash, long PHash)> shapes)
    {
        var list = shapes.Take(MaxShapes).ToList();
        var bytes = new byte[list.Count * 16];
        for (var i = 0; i < list.Count; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 16, 8), list[i].DHash);
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 16 + 8, 8), list[i].PHash);
        }
        return bytes;
    }

    /// <summary>باز کردن <see cref="Pack"/>. ورودی خالی/خراب ← فهرست خالی.</summary>
    public static List<(long DHash, long PHash)> Unpack(byte[]? bytes)
    {
        var list = new List<(long, long)>();
        if (bytes is null)
            return list;
        for (var i = 0; i + 16 <= bytes.Length && list.Count < MaxShapes; i += 16)
            list.Add((BitConverter.ToInt64(bytes, i), BitConverter.ToInt64(bytes, i + 8)));
        return list;
    }

    /// <summary>
    /// بیشترین تعداد «شکل» ذخیره‌شدهٔ هر کالا. دور ۳۰ (#۱): ۲۴ = تا ۸ عکس × ۳ نسخه (بدون حاشیه، کل قاب، وسط ۹۰٪) – ستون ۵۱۲ بایتی.
    /// </summary>
    public const int MaxShapes = 24;

    /// <summary>دور ۳۰ (#۱): بیشترین عکس هر کالا که امضا می‌گیرد (عکس بدون زمینه + عکس اصلی زمینه‌دار + بقیهٔ گالری).</summary>
    public const int MaxImages = 8;

    /// <summary>
    /// دور ۳۰ (#۱) – نسخه‌های ذخیره‌شدهٔ «یک عکس کاتالوگ»: بدون حاشیه، کل قاب (اگر فرق داشت) و وسط ۹۰٪ (اگر فرق داشت).
    /// چرا؟ مشتری ممکن است همان عکس را با برش کمی متفاوت بفرستد (اسکرین‌شات، ذخیره از صفحهٔ کالا با لوگوی کم‌رنگ، برش گوشی)؛
    /// هر سه نسخه ذخیره می‌شوند تا «همان عکس» همیشه پیدا شود. فایل خراب/غیرتصویر ← فهرست خالی.
    /// مثال: کالای قرمز وسط بوم سفید ← [کادر کالا، کل بوم، وسط بوم].
    /// </summary>
    public static List<Signature> ComputeStored(byte[] data)
    {
        var list = new List<Signature>();
        if (data is null || data.Length == 0 || data.Length > MaxBytes * 4)
            return list;
        try
        {
            using var stream = new SKMemoryStream(data);
            using var codec = SKCodec.Create(stream);
            if (codec is null)
                return list;
            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels)
                return list;
            using var decoded = DecodeSmall(codec);
            if (decoded is null)
                return list;
            using var bitmap = Orient(decoded, codec.EncodedOrigin);
            void Add(Signature? signature)
            {
                if (signature is not null && list.All(x => x.Hash != signature.Hash || x.PHash != signature.PHash))
                    list.Add(signature);
            }
            Add(Compute(bitmap));
            Add(ComputeRaw(bitmap));
            var w = bitmap.Width;
            var h = bitmap.Height;
            var inner = new SKRectI((int)(w * 0.05), (int)(h * 0.05), (int)Math.Ceiling(w * 0.95), (int)Math.Ceiling(h * 0.95));
            if (inner.Width >= 8 && inner.Height >= 8)
            {
                using var middle = new SKBitmap();
                if (bitmap.ExtractSubset(middle, inner))
                    Add(ComputeRaw(middle));
            }
        }
        catch (Exception)
        {
            list.Clear();
        }
        return list;
    }

    /// <summary>
    /// دور ۳۰ (#۱) – «بزرگ‌ترین تکهٔ تصویر»: در اسکرین‌شات صفحهٔ کالا عکس کالا بزرگ‌ترین تکهٔ پیوستهٔ غیرزمینه است و نوشته‌ها،
    /// دکمه‌ها و نوار بالای گوشی تکه‌های کوچک جدا هستند. کادر همان تکه (اگر با کل محتوا فرق داشت) برگردانده می‌شود؛ وگرنه null.
    /// مثال: اسکرین‌شات با عکس کالا وسط و متن زیرش ← فقط کادر عکس کالا.
    /// </summary>
    public static SKRectI? MainPartBox(SKBitmap bitmap)
    {
        var w = bitmap.Width;
        var h = bitmap.Height;
        if (w < 16 || h < 16)
            return null;
        var bg = Flat(bitmap.GetPixel(0, 0));
        var mask = new bool[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                mask[y * w + x] = Distance(Flat(bitmap.GetPixel(x, y)), bg) > 48;
        // یک پیکسل گسترش تا اجزای خود کالا (مثلاً بند و بدنهٔ ساعت) یکی شوند ولی متن جدا بماند.
        var grown = new bool[mask.Length];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                grown[y * w + x] = mask[y * w + x]
                    || (x > 0 && mask[y * w + x - 1]) || (x < w - 1 && mask[y * w + x + 1])
                    || (y > 0 && mask[(y - 1) * w + x]) || (y < h - 1 && mask[(y + 1) * w + x]);
        if (Fratak_Shop.Core.Services.Smart.ImageCleanup.LargestPartBox(grown, w, h) is not { } part)
            return null;
        var box = new SKRectI(part.MinX, part.MinY, part.MaxX + 1, part.MaxY + 1);
        var trim = TrimBox(bitmap);
        // تکهٔ خیلی کوچک (نویز) یا تقریباً همان کادر کل محتوا ← نسخهٔ تازه‌ای نیست.
        if (box.Width < 12 || box.Height < 12 || (long)box.Width * box.Height < (long)w * h * 0.06)
            return null;
        if ((long)box.Width * box.Height > (long)trim.Width * trim.Height * 0.9)
            return null;
        return box;
    }

    /// <summary>
    /// دور ۳۰ (#۱): بسته‌بندی هیستوگرام رنگ هر عکس (هر کدام <see cref="Bins"/> بایت؛ حداکثر <see cref="MaxShapes"/>) برای ستون Palettes.
    /// هیستوگرام با طول اشتباه صفر ذخیره می‌شود تا ترتیب با Shapes به هم نخورد.
    /// </summary>
    public static byte[] PackPalettes(IEnumerable<byte[]?> palettes)
    {
        var list = palettes.Take(MaxShapes).ToList();
        var bytes = new byte[list.Count * Bins];
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is { Length: Bins } palette)
                palette.CopyTo(bytes, i * Bins);
        }
        return bytes;
    }

    /// <summary>باز کردن <see cref="PackPalettes"/>. ورودی خالی/خراب ← null.</summary>
    public static List<byte[]>? UnpackPalettes(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0 || bytes.Length % Bins != 0)
            return null;
        var list = new List<byte[]>();
        for (var i = 0; i + Bins <= bytes.Length && list.Count < MaxShapes; i += Bins)
            list.Add(bytes.AsSpan(i, Bins).ToArray());
        return list;
    }

    /// <summary>
    /// دور ۲۶: اعمال جهت EXIF (عکس گوشی معمولاً چرخیده ذخیره می‌شود و فقط برچسب جهت دارد). بدون این، عکس عمودی گوشی
    /// خوابیده دیده می‌شد و هیچ کالایی پیدا نمی‌شد. خروجی همیشه بیت‌مپ تازه است (ورودی دست نمی‌خورد).
    /// </summary>
    public static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        var w = source.Width;
        var h = source.Height;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? h : w, swap ? w : h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => SKMatrix.Identity,
        };
        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return result;
    }

    /// <summary>رنگ با شفافیت ← رنگ روی زمینهٔ سفید (برای حدس پس‌زمینه).</summary>
    private static (int R, int G, int B) Flat(SKColor c)
    {
        var a = c.Alpha / 255.0;
        return ((int)(c.Red * a + 255 * (1 - a)), (int)(c.Green * a + 255 * (1 - a)), (int)(c.Blue * a + 255 * (1 - a)));
    }

    private static int Distance((int R, int G, int B) a, (int R, int G, int B) b) =>
        Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    /// <summary>امضای خام (بدون بریدن حاشیه).</summary>
    private static Signature? ComputeRaw(SKBitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return null;
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        using var tiny = bitmap.Resize(new SKImageInfo(9, 8, SKColorType.Rgba8888, SKAlphaType.Unpremul), sampling);
        using var small = bitmap.Resize(new SKImageInfo(32, 32, SKColorType.Rgba8888, SKAlphaType.Unpremul), sampling);
        if (tiny is null || small is null)
            return null;

        long hash = 0;
        var bit = 0;
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                if (Gray(tiny.GetPixel(x, y)) > Gray(tiny.GetPixel(x + 1, y)))
                    hash |= 1L << bit;
                bit++;
            }
        }

        var counts = new int[Bins];
        var total = 0;
        for (var y = 0; y < 32; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                var bin = Bin(small.GetPixel(x, y));
                if (bin < 0)
                    continue;
                counts[bin]++;
                total++;
            }
        }
        return new Signature(hash, Normalize(counts, total), PerceptualHash(bitmap));
    }

    /// <summary>
    /// خانهٔ هیستوگرام یک رنگ؛ ‎-1 یعنی حساب نشود (شفاف یا تقریباً سفید).
    /// مثال: قرمز خالص ← ۱ (فام ۰، اشباع زیاد)؛ خاکستری میانه ← خانه‌ای بین ۲۴ تا ۳۱؛ سفید ← ‎-1.
    /// </summary>
    public static int Bin(SKColor color)
    {
        if (color.Alpha < 128)
            return -1;
        color.ToHsv(out var h, out var s, out var v);
        if (v > 92 && s < 8)
            return -1;
        if (v < 18 || s < 15)
            return 24 + Math.Clamp((int)(v / 12.5f), 0, 7);
        var hue = Math.Clamp((int)(h / 30f), 0, 11);
        return hue * 2 + (s < 50 ? 0 : 1);
    }

    /// <summary>
    /// شمارش‌ها ← بایت‌ها (مجموع حدود ۱۰۲۰، هر خانه حداکثر ۲۵۵). مثال: همه در یک خانه ← آن خانه ۲۵۵، بقیه ۰. total=0 ← همه صفر.
    /// </summary>
    public static byte[] Normalize(int[] counts, int total)
    {
        var result = new byte[Bins];
        if (total <= 0)
            return result;
        for (var i = 0; i < Bins && i < counts.Length; i++)
            result[i] = (byte)Math.Clamp((int)Math.Round(counts[i] * 1020.0 / total), 0, 255);
        return result;
    }

    /// <summary>فاصلهٔ همینگ دو هش (۰ تا ۶۴). مثال: (0، 0) ← 0؛ (0، ‎-1) ← 64.</summary>
    public static int Hamming(long a, long b) => BitOperations.PopCount((ulong)(a ^ b));

    /// <summary>
    /// شباهت رنگ با «اشتراک هیستوگرام» (۰ تا ۱). مثال: دو آرایهٔ یکسان غیرصفر ← 1؛ بدون خانهٔ مشترک ← 0؛ آرایهٔ تمام‌صفر ← 0.
    /// </summary>
    public static double ColorSimilarity(byte[] a, byte[] b)
    {
        if (a is null || b is null || a.Length != b.Length)
            return 0;
        int shared = 0, sumA = 0, sumB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            shared += Math.Min(a[i], b[i]);
            sumA += a[i];
            sumB += b[i];
        }
        var denominator = Math.Max(sumA, sumB);
        return denominator == 0 ? 0 : (double)shared / denominator;
    }

    /// <summary>
    /// امتیاز نهایی شباهت (۰ تا ۱): ۵۵٪ شکل + ۴۵٪ رنگ. مثال: همان تصویر ← 1.
    /// </summary>
    public static double Score(Signature query, long hash, byte[] colors) =>
        0.55 * (1 - Hamming(query.Hash, hash) / 64.0) + 0.45 * ColorSimilarity(query.Colors, colors);

    // دور ۳۰ (#۱): پیکسل نیمه‌شفاف (لبهٔ عکس بدون زمینه) با سفید ترکیب می‌شود – درست مثل مرورگر که عکس را روی سفید می‌کشد؛
    // قبلاً لبه‌ها سیاه/سفید پرشی بودند و هش عکس شفاف با عکس ذخیره‌شده از سایت کمی فرق می‌کرد.
    private static float Gray(SKColor c)
    {
        var (r, g, b) = Flat(c);
        return 0.299f * r + 0.587f * g + 0.114f * b;
    }

    /// <summary>باز کردن کوچک‌شده (تا حدود ۲۵۶ نقطه در ضلع بزرگ؛ JPEG مستقیم با ۱/۲ تا ۱/۸ باز می‌شود).</summary>
    private static SKBitmap? DecodeSmall(SKCodec codec)
    {
        var info = codec.Info;
        var longest = Math.Max(info.Width, info.Height);
        if (longest > 512)
        {
            var dims = codec.GetScaledDimensions(256f / longest);
            if (dims.Width > 0 && dims.Height > 0 && dims.Width < info.Width)
            {
                var scaled = new SKImageInfo(dims.Width, dims.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                var bitmap = new SKBitmap(scaled);
                var result = codec.GetPixels(scaled, bitmap.GetPixels());
                if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput)
                    return bitmap;
                bitmap.Dispose();
            }
        }
        var full = new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var decoded = new SKBitmap(full);
        var status = codec.GetPixels(full, decoded.GetPixels());
        if (status is SKCodecResult.Success or SKCodecResult.IncompleteInput)
            return decoded;
        decoded.Dispose();
        return null;
    }
}
