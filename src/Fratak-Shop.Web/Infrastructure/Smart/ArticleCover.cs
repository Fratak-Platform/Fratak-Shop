using Fratak_Shop.Web.Infrastructure.Media;
using SkiaSharp;

namespace Fratak_Shop.Web.Infrastructure.Smart;

/// <summary>
/// دور ۳۰ (#۶) – «کاور هوشمند مقاله»: تصویر بالای مقاله، حرفه‌ای و مرتبط، بدون متن (عنوان مقاله خودش در صفحه هست و متنِ داخل
/// عکس برای گوگل و صفحه‌خوان‌ها خواندنی نیست).
/// <list type="number">
/// <item><b>کالاهای مقاله</b> (تا ۳ تا): هر عکس با «دوربری مطمئن» (<see cref="SubjectMatting"/>) جدا و روی زمینهٔ گرادیانِ هم‌رنگ
/// خود کالا چیده می‌شود (کالای اصلی بزرگ و جلو، بقیه کوچک‌تر و عقب‌تر) با سایهٔ نرم و سایهٔ کف. دوربری ممکن نبود ← همان عکس در
/// قاب گرد با سایه (هرگز مستطیل سفیدِ خام وسط بنر نمی‌افتد).</item>
/// <item><b>عکس فضاسازی</b> (وقتی مقاله کالا ندارد): عکس جست‌وجوشده و اعتبارسنجی‌شده تمام‌قاب (برش «پوشاننده» از وسط)
/// با لایهٔ رنگی ملایم.</item>
/// <item>هیچ‌کدام ← گرادیان ساده با شکل‌های تزیینی (null فقط وقتی هیچ ورودی معتبری نیست).</item>
/// </list>
/// خروجی: WebP به ابعاد <see cref="Width"/>×<see cref="Height"/>. محل استفاده: SmartImageFinder.ArticleCoverAsync (مقاله‌های هوشمند و کاور مقاله‌های بدون تصویر).
/// </summary>
public static class ArticleCover
{
    /// <summary>عرض کاور (همان ابعاد بنر قبلی مقاله تا قالب صفحه عوض نشود).</summary>
    public const int Width = 1600;

    /// <summary>ارتفاع کاور.</summary>
    public const int Height = 600;

    /// <summary>بیشترین تعداد کالا روی کاور.</summary>
    public const int MaxProducts = 3;

    /// <summary>یک قطعهٔ آماده برای چیدن: تصویر (شفاف یا کامل) و اینکه دوربری شده یا نه.</summary>
    private sealed record Piece(SKBitmap Bitmap, bool Cut) : IDisposable
    {
        public void Dispose() => Bitmap.Dispose();
    }

    /// <summary>
    /// ساخت کاور. <paramref name="productImages"/> = بایت عکس کالاها (اصلی اول)؛ <paramref name="backdrop"/> = عکس فضاسازی (اختیاری).
    /// ورودی خالی یا خراب ← کاور گرادیانی؛ خطای غیرمنتظره ← null (مقاله بدون کاور می‌ماند و دفعهٔ بعد دوباره تلاش می‌شود).
    /// </summary>
    public static byte[]? Compose(IReadOnlyList<byte[]> productImages, byte[]? backdrop)
    {
        var pieces = new List<Piece>();
        SKBitmap? photo = null;
        try
        {
            // ۱) دوربری‌ها پیش از گرفتن نوبت رمزگذار (کار سنگین مدل جدا از صف WebP).
            foreach (var bytes in (productImages ?? Array.Empty<byte[]>()).Where(b => b is { Length: > 0 }).Take(MaxProducts))
            {
                if (LoadPiece(bytes) is { } piece)
                    pieces.Add(piece);
            }
            if (pieces.Count == 0 && backdrop is { Length: > 0 })
                photo = Decode(backdrop);

            if (!WebPEncoder.Gate.Wait(TimeSpan.FromSeconds(60)))
                return null;
            try
            {
                return Render(pieces, photo);
            }
            finally
            {
                WebPEncoder.Gate.Release();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            foreach (var piece in pieces)
                piece.Dispose();
            photo?.Dispose();
        }
    }

    /// <summary>
    /// رنگ اصلی کالا (میانگین پیکسل‌های پررنگ و غیرخاکستری سوژه). کالای سیاه/سفید/خاکستری ← آبی ملایم پیش‌فرض.
    /// مثال: کتری قرمز ← قرمز؛ هدفون مشکی ← (۷۰، ۱۱۰، ۲۰۰).
    /// </summary>
    public static SKColor Accent(SKBitmap bitmap)
    {
        long r = 0, g = 0, b = 0, n = 0;
        var stepX = Math.Max(1, bitmap.Width / 48);
        var stepY = Math.Max(1, bitmap.Height / 48);
        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.Alpha < 200)
                    continue;
                c.ToHsv(out _, out var s, out var v);
                if (s < 25 || v < 20 || v > 97)
                    continue;
                r += c.Red;
                g += c.Green;
                b += c.Blue;
                n++;
            }
        }
        return n < 12 ? new SKColor(70, 110, 200) : new SKColor((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }

    // ───────────────────────────── داخلی ─────────────────────────────

    private static Piece? LoadPiece(byte[] bytes)
    {
        using var decoded = Decode(bytes);
        if (decoded is null || decoded.Width < 120 || decoded.Height < 120)
            return null;
        // عکس شفاف (عکس اصلی دوربری‌شدهٔ کالا) همان‌طور استفاده می‌شود؛ بقیه دوربری مطمئن می‌شوند.
        if (HasTransparency(decoded))
            return new Piece(decoded.Copy(), true);
        if (SubjectMatting.Cutout(decoded, 900) is { } cut && SKBitmap.Decode(cut.Png) is { } png)
            return new Piece(png, true);
        return new Piece(decoded.Copy(), false);
    }

    private static SKBitmap? Decode(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || (long)codec.Info.Width * codec.Info.Height > 25_000_000)
            return null;
        var decoded = SKBitmap.Decode(codec);
        if (decoded is null)
            return null;
        var oriented = MediaEndpoints.Orient(decoded, codec.EncodedOrigin);
        if (oriented is null)
            return decoded;
        decoded.Dispose();
        return oriented;
    }

    private static bool HasTransparency(SKBitmap bitmap)
    {
        if (bitmap.AlphaType == SKAlphaType.Opaque)
            return false;
        var transparent = 0;
        var total = 0;
        var step = Math.Max(1, Math.Min(bitmap.Width, bitmap.Height) / 40);
        for (var x = 0; x < bitmap.Width; x += step)
        {
            foreach (var y in new[] { 0, bitmap.Height - 1 })
            {
                total++;
                if (bitmap.GetPixel(x, y).Alpha < 16)
                    transparent++;
            }
        }
        return total > 0 && transparent > total * 0.6;
    }

    private static byte[]? Render(IReadOnlyList<Piece> pieces, SKBitmap? photo)
    {
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null)
            return null;
        var canvas = surface.Canvas;
        var accent = pieces.Count > 0 ? Accent(pieces[0].Bitmap) : photo is not null ? Accent(photo) : new SKColor(70, 110, 200);

        if (pieces.Count == 0 && photo is not null)
            DrawPhoto(canvas, photo, accent);
        else
            DrawBackground(canvas, accent);

        if (pieces.Count > 0)
            DrawProducts(canvas, pieces);

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Webp, WebPEncoder.Quality);
        return data?.ToArray();
    }

    /// <summary>زمینهٔ گرادیانی روشن از رنگ کالا + دو دایرهٔ نرم و نوار مورب کم‌رنگ (حس «بنر حرفه‌ای» بدون شلوغی).</summary>
    private static void DrawBackground(SKCanvas canvas, SKColor accent)
    {
        var light = Mix(accent, SKColors.White, 0.88f);
        var mid = Mix(accent, SKColors.White, 0.62f);
        using (var paint = new SKPaint { IsAntialias = true })
        {
            paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(Width, Height),
                new[] { light, mid }, null, SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, Width, Height, paint);
        }
        using (var glow = new SKPaint { IsAntialias = true })
        {
            glow.Shader = SKShader.CreateRadialGradient(new SKPoint(Width * 0.66f, Height * 0.52f), Height * 0.78f,
                new[] { SKColors.White.WithAlpha(210), SKColors.White.WithAlpha(0) }, null, SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, Width, Height, glow);
        }
        using var shape = new SKPaint { IsAntialias = true, Color = Mix(accent, SKColors.White, 0.35f).WithAlpha(46) };
        canvas.DrawCircle(Width * 0.12f, Height * 0.18f, Height * 0.42f, shape);
        canvas.DrawCircle(Width * 0.93f, Height * 0.95f, Height * 0.36f, shape);
        using var band = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(60) };
        using var path = new SKPath();
        path.MoveTo(Width * 0.30f, Height);
        path.LineTo(Width * 0.46f, 0);
        path.LineTo(Width * 0.54f, 0);
        path.LineTo(Width * 0.38f, Height);
        path.Close();
        canvas.DrawPath(path, band);
    }

    /// <summary>عکس فضاسازی تمام‌قاب (برش پوشاننده از وسط) + لایهٔ رنگی ملایم از پایین.</summary>
    private static void DrawPhoto(SKCanvas canvas, SKBitmap photo, SKColor accent)
    {
        var scale = Math.Max(Width / (float)photo.Width, Height / (float)photo.Height);
        var w = photo.Width * scale;
        var h = photo.Height * scale;
        using (var image = SKImage.FromBitmap(photo))
            canvas.DrawImage(image, SKRect.Create((Width - w) / 2, (Height - h) / 2, w, h), new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var veil = new SKPaint { IsAntialias = true };
        veil.Shader = SKShader.CreateLinearGradient(new SKPoint(0, Height), new SKPoint(0, Height * 0.35f),
            new[] { Mix(accent, SKColors.Black, 0.55f).WithAlpha(120), SKColors.Transparent }, null, SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, Width, Height, veil);
    }

    /// <summary>چیدن کالاها: یکی ← وسط‌راست؛ دو ← کنار هم؛ سه ← اصلی وسط و دو کالای کوچک‌تر عقب‌تر.</summary>
    private static void DrawProducts(SKCanvas canvas, IReadOnlyList<Piece> pieces)
    {
        // (مرکز x، ارتفاع نسبی) – عقبی‌ها اول کشیده می‌شوند تا کالای اصلی جلو بیفتد.
        var slots = pieces.Count switch
        {
            1 => new (float X, float H)[] { (0.62f, 0.80f) },
            2 => new (float X, float H)[] { (0.50f, 0.74f), (0.74f, 0.64f) },
            _ => new (float X, float H)[] { (0.62f, 0.80f), (0.40f, 0.60f), (0.84f, 0.60f) },
        };
        // ترتیب کشیدن: اول کالاهای فرعی، آخر کالای اصلی (تا جلو بیفتد).
        var count = Math.Min(pieces.Count, slots.Length);
        var order = Enumerable.Range(1, count - 1).Append(0).ToList();
        foreach (var index in order)
        {
            var piece = pieces[index];
            var (cx, rh) = slots[index];
            var maxH = Height * rh;
            var maxW = Width * (pieces.Count == 1 ? 0.5f : 0.3f);
            var fit = Math.Min(maxH / piece.Bitmap.Height, maxW / piece.Bitmap.Width);
            var w = piece.Bitmap.Width * fit;
            var h = piece.Bitmap.Height * fit;
            var bottom = Height * 0.9f;
            var rect = SKRect.Create(Width * cx - w / 2, bottom - h, w, h);

            // سایهٔ کف (بیضی محو زیر کالا).
            using (var floor = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 38), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 14) })
                canvas.DrawOval(new SKRect(rect.Left + w * 0.12f, bottom - 10, rect.Right - w * 0.12f, bottom + 16), floor);

            using var image = SKImage.FromBitmap(piece.Bitmap);
            if (piece.Cut)
            {
                using var shadow = new SKPaint { ImageFilter = SKImageFilter.CreateDropShadow(0, 14, 18, 18, new SKColor(0, 0, 0, 60)) };
                canvas.DrawImage(image, rect, new SKSamplingOptions(SKCubicResampler.Mitchell), shadow);
            }
            else
            {
                // عکس دوربری‌نشده ← قاب گرد سفید با سایه (نه مستطیل خام).
                var frame = new SKRoundRect(SKRect.Inflate(rect, 10, 10), 22);
                using (var card = new SKPaint { IsAntialias = true, Color = SKColors.White, ImageFilter = SKImageFilter.CreateDropShadow(0, 12, 20, 20, new SKColor(0, 0, 0, 55)) })
                    canvas.DrawRoundRect(frame, card);
                canvas.Save();
                canvas.ClipRoundRect(new SKRoundRect(rect, 16), antialias: true);
                canvas.DrawImage(image, rect, new SKSamplingOptions(SKCubicResampler.Mitchell));
                canvas.Restore();
            }
        }
    }

    private static SKColor Mix(SKColor a, SKColor b, float t) => new(
        (byte)Math.Clamp(a.Red + (b.Red - a.Red) * t, 0, 255),
        (byte)Math.Clamp(a.Green + (b.Green - a.Green) * t, 0, 255),
        (byte)Math.Clamp(a.Blue + (b.Blue - a.Blue) * t, 0, 255));
}
