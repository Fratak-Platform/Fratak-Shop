using System.Globalization;
using System.Text;
using System.Text.Json;
using Fratak_Shop.Web.Infrastructure.Media;
using HarfBuzzSharp;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Buffer = HarfBuzzSharp.Buffer;

namespace Fratak_Shop.Web.Infrastructure.Design;

/// <summary>
/// رندر طرح استودیو (JSON فابریک) به تصویر PNG در سرور — دور ۲۱، بدون هیچ سرویس بیرونی یا هوش مصنوعی.
/// <para>کجا لازم است؟ ایده‌های هوشمند بنر شب‌ها بدون مرورگر ساخته می‌شوند و مدیر صبح پیش‌نمایش آماده می‌بیند. در ویرایشگر،
/// تصویر نهایی را خود مرورگر (fabric) می‌سازد و می‌فرستد؛ این کلاس فقط برای کارهای پس‌زمینه است.</para>
/// <para>پشتیبانی: Rect (با گوشهٔ گرد)، Circle، Ellipse، Triangle، Line، Polygon، Path ساده (M/L/H/V/C/Q/Z)، Image (فقط فایل محلی
/// /uploads یا /assets — هرگز دانلود از اینترنت: جلوی SSRF)، Textbox/IText/Text (فارسی با HarfBuzz و چیدمان دوجهته)، Group،
/// گرادیان خطی/شعاعی، سایه، شفافیت، چرخش و قرینه.</para>
/// <para>متن: شکستن خط با اندازه‌گیری واقعی عرض (همان رفتار Textbox)، ارتفاع خط مثل fabric (۱٫۱۳ × اندازهٔ قلم × lineHeight).
/// ترتیب دوجهته: تکه‌های فارسی راست‌به‌چپ و تکه‌های لاتین/عدد چپ‌به‌راست (مثلاً «گوشی Galaxy S24 با ۲۵٪ تخفیف»).</para>
/// <para>ایمنی: هر خطای کتابخانهٔ بومی یا JSON خراب ← null (طرح بدون پیش‌نمایش می‌ماند و در ویرایشگر ساخته می‌شود)؛
/// سقف ۸ مگاپیکسل؛ از همان دروازهٔ «یک تصویر در هر لحظه» تبدیل WebP می‌گذرد تا حافظهٔ هاست ضعیف پر نشود.</para>
/// </summary>
public interface IDesignRenderer
{
    /// <summary>PNG طرح در ابعاد داده‌شده (یا null). مثال: <c>RenderPng(json, 1920, 640)</c>.</summary>
    byte[]? RenderPng(string json, int width, int height);
}

public sealed class DesignRenderer(IWebHostEnvironment environment, ILogger<DesignRenderer> logger) : IDesignRenderer
{
    /// <summary>ضریب ارتفاع خط fabric (_fontSizeMult) و سهم زیر خط کرسی (_fontSizeFraction).</summary>
    private const float FontSizeMult = 1.13f;
    private const float FontSizeFraction = 0.222f;
    private const int MaxObjects = 400;

    private static readonly Lazy<FontSet?> Fonts = new(LoadFonts);

    /// <summary>قلم‌ها پیدا شد و کتابخانهٔ HarfBuzz بار شد؟ (برای صفحهٔ سلامت و آزمون‌ها)</summary>
    public static bool IsAvailable => Fonts.Value is not null;

    public byte[]? RenderPng(string json, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(json) || width < 16 || height < 16 || (long)width * height > 8_000_000)
            return null;
        var fonts = Fonts.Value;
        if (fonts is null)
            return null;
        if (!WebPEncoder.Gate.Wait(TimeSpan.FromMinutes(1)))
            return null;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (surface is null)
                return null;
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            if (root.TryGetProperty("background", out var bg) && bg.ValueKind == JsonValueKind.String && ParseColor(bg.GetString()) is { } bgColor)
                canvas.Clear(bgColor);

            var context = new RenderContext(fonts, environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
            try
            {
                if (root.TryGetProperty("objects", out var objects) && objects.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in objects.EnumerateArray().Take(MaxObjects))
                        DrawObject(canvas, item, context, 0);
                }
            }
            finally
            {
                foreach (var bitmap in context.Images.Values)
                    bitmap?.Dispose();
            }
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data?.ToArray();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogWarning(exception, "Design render failed");
            return null;
        }
        finally
        {
            WebPEncoder.Gate.Release();
        }
    }

    // ───────────────────────────── اشیا ─────────────────────────────

    private sealed record RenderContext(FontSet Fonts, string WebRoot)
    {
        public Dictionary<string, SKBitmap?> Images { get; } = new(StringComparer.Ordinal);
    }

    private void DrawObject(SKCanvas canvas, JsonElement o, RenderContext context, int depth)
    {
        if (o.ValueKind != JsonValueKind.Object || depth > 4)
            return;
        if (o.TryGetProperty("visible", out var visible) && visible.ValueKind == JsonValueKind.False)
            return;
        var type = Str(o, "type")?.ToLowerInvariant() ?? "";
        var opacity = Math.Clamp(Num(o, "opacity", 1), 0, 1);
        if (opacity <= 0.001)
            return;

        var strokeWidth = (float)Num(o, "strokeWidth", 0);
        var stroke = ParseColor(Str(o, "stroke"));
        if (stroke is null)
            strokeWidth = 0;
        var isText = type is "textbox" or "itext" or "i-text" or "text";

        float w, h;
        TextLayout? layout = null;
        if (isText)
        {
            layout = LayoutText(o, context.Fonts);
            w = layout.Width;
            h = layout.Height;
        }
        else
        {
            w = (float)Num(o, "width", 0) + strokeWidth;
            h = (float)Num(o, "height", 0) + strokeWidth;
        }

        canvas.Save();
        try
        {
            canvas.Translate((float)Num(o, "left", 0), (float)Num(o, "top", 0));
            canvas.RotateDegrees((float)Num(o, "angle", 0));
            var sx = (float)Num(o, "scaleX", 1) * (Bool(o, "flipX") ? -1 : 1);
            var sy = (float)Num(o, "scaleY", 1) * (Bool(o, "flipY") ? -1 : 1);
            var skewX = (float)Num(o, "skewX", 0);
            var skewY = (float)Num(o, "skewY", 0);
            // fabric: نقطهٔ مبدأ (originX/Y) جای left/top است؛ چرخش و مقیاس حول همان نقطه. پیش‌فرض fabric 7 = center.
            canvas.Scale(sx, sy);
            if (skewX != 0 || skewY != 0)
                canvas.Skew((float)Math.Tan(skewX * Math.PI / 180), (float)Math.Tan(skewY * Math.PI / 180));
            canvas.Translate(-Origin(Str(o, "originX") ?? "center", w), -Origin(Str(o, "originY") ?? "center", h));

            var needsLayer = opacity < 0.999;
            if (needsLayer)
            {
                using var layerPaint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(opacity * 255)) };
                canvas.SaveLayer(layerPaint);
            }
            using var shadow = ShadowFilter(o);
            switch (type)
            {
                case "rect":
                    DrawShape(canvas, o, w, h, strokeWidth, stroke, shadow, (c, p, inset) =>
                    {
                        var rect = new SKRect(inset, inset, w - inset, h - inset);
                        var rx = (float)Num(o, "rx", 0);
                        var ry = (float)Num(o, "ry", rx);
                        if (rx > 0 || ry > 0)
                            c.DrawRoundRect(rect, rx, ry, p);
                        else
                            c.DrawRect(rect, p);
                    });
                    break;
                case "circle":
                    DrawShape(canvas, o, w, h, strokeWidth, stroke, shadow, (c, p, _) =>
                        c.DrawCircle(w / 2, h / 2, (float)Num(o, "radius", Math.Min(w, h) / 2.0), p));
                    break;
                case "ellipse":
                    DrawShape(canvas, o, w, h, strokeWidth, stroke, shadow, (c, p, _) =>
                        c.DrawOval(w / 2, h / 2, (float)Num(o, "rx", w / 2.0), (float)Num(o, "ry", h / 2.0), p));
                    break;
                case "triangle":
                    DrawShape(canvas, o, w, h, strokeWidth, stroke, shadow, (c, p, inset) =>
                    {
                        using var path = new SKPath();
                        path.MoveTo(inset, h - inset);
                        path.LineTo(w / 2, inset);
                        path.LineTo(w - inset, h - inset);
                        path.Close();
                        c.DrawPath(path, p);
                    });
                    break;
                case "line":
                    DrawLine(canvas, o, w, h, strokeWidth, stroke, shadow);
                    break;
                case "polygon" or "polyline":
                    DrawShape(canvas, o, w, h, strokeWidth, stroke, shadow, (c, p, _) =>
                    {
                        using var path = PolygonPath(o, w, h, type == "polygon");
                        if (path is not null)
                            c.DrawPath(path, p);
                    });
                    break;
                case "path":
                    DrawShape(canvas, o, w, h, strokeWidth, stroke, shadow, (c, p, _) =>
                    {
                        using var path = SvgPath(o, w, h);
                        if (path is not null)
                            c.DrawPath(path, p);
                    });
                    break;
                case "image":
                    DrawImage(canvas, o, w, h, context, shadow);
                    break;
                case "group":
                    // فرزندان گروه نسبت به مرکز گروه جای‌گذاری شده‌اند.
                    canvas.Translate(w / 2, h / 2);
                    if (o.TryGetProperty("objects", out var children) && children.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var child in children.EnumerateArray().Take(MaxObjects))
                            DrawObject(canvas, child, context, depth + 1);
                    }
                    break;
                default:
                    if (isText && layout is not null)
                        DrawText(canvas, o, layout, strokeWidth, stroke, shadow);
                    break;
            }
            if (needsLayer)
                canvas.Restore();
        }
        finally
        {
            canvas.Restore();
            layout?.Font.Dispose();
        }
    }

    private static float Origin(string origin, float size) => origin switch
    {
        "left" or "top" => 0,
        "right" or "bottom" => size,
        _ => size / 2,
    };

    private void DrawShape(SKCanvas canvas, JsonElement o, float w, float h, float strokeWidth, SKColor? stroke, SKImageFilter? shadow,
        Action<SKCanvas, SKPaint, float> draw)
    {
        var inset = strokeWidth / 2;
        using (var fill = FillPaint(o, w, h, inset))
        {
            if (fill is not null)
            {
                fill.ImageFilter = shadow;
                draw(canvas, fill, inset);
            }
        }
        if (strokeWidth > 0 && stroke is { } strokeColor)
        {
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth, Color = strokeColor };
            draw(canvas, paint, inset);
        }
    }

    private static void DrawLine(SKCanvas canvas, JsonElement o, float w, float h, float strokeWidth, SKColor? stroke, SKImageFilter? shadow)
    {
        if (strokeWidth <= 0 || stroke is null)
            return;
        // fabric Line: x1..y2 نسبت به مرکز شیء.
        var x1 = (float)Num(o, "x1", 0) + w / 2;
        var y1 = (float)Num(o, "y1", 0) + h / 2;
        var x2 = (float)Num(o, "x2", 0) + w / 2;
        var y2 = (float)Num(o, "y2", 0) + h / 2;
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth, Color = stroke.Value, ImageFilter = shadow };
        paint.StrokeCap = Str(o, "strokeLineCap") == "round" ? SKStrokeCap.Round : SKStrokeCap.Butt;
        canvas.DrawLine(x1, y1, x2, y2, paint);
    }

    private static SKPath? PolygonPath(JsonElement o, float w, float h, bool close)
    {
        if (!o.TryGetProperty("points", out var points) || points.ValueKind != JsonValueKind.Array)
            return null;
        var offset = PathOffset(o);
        var path = new SKPath();
        var first = true;
        foreach (var point in points.EnumerateArray().Take(2000))
        {
            var x = (float)Num(point, "x", 0) - offset.X + w / 2;
            var y = (float)Num(point, "y", 0) - offset.Y + h / 2;
            if (first)
                path.MoveTo(x, y);
            else
                path.LineTo(x, y);
            first = false;
        }
        if (close)
            path.Close();
        return path;
    }

    /// <summary>مسیر fabric: آرایهٔ دستورها [["M",x,y],["L",x,y],["C",…],["Q",…],["Z"]] با مختصات مطلق و pathOffset.</summary>
    private static SKPath? SvgPath(JsonElement o, float w, float h)
    {
        if (!o.TryGetProperty("path", out var commands) || commands.ValueKind != JsonValueKind.Array)
            return null;
        var offset = PathOffset(o);
        var dx = w / 2 - offset.X;
        var dy = h / 2 - offset.Y;
        var path = new SKPath();
        float cx = 0, cy = 0;
        foreach (var command in commands.EnumerateArray().Take(5000))
        {
            if (command.ValueKind != JsonValueKind.Array || command.GetArrayLength() == 0)
                continue;
            var parts = command.EnumerateArray().ToList();
            var op = parts[0].ValueKind == JsonValueKind.String ? parts[0].GetString() : null;
            float A(int i) => i < parts.Count && parts[i].ValueKind == JsonValueKind.Number ? (float)parts[i].GetDouble() : 0f;
            switch (op)
            {
                case "M":
                    path.MoveTo(cx = A(1), cy = A(2));
                    break;
                case "L":
                    path.LineTo(cx = A(1), cy = A(2));
                    break;
                case "H":
                    path.LineTo(cx = A(1), cy);
                    break;
                case "V":
                    path.LineTo(cx, cy = A(1));
                    break;
                case "C":
                    path.CubicTo(A(1), A(2), A(3), A(4), cx = A(5), cy = A(6));
                    break;
                case "Q":
                    path.QuadTo(A(1), A(2), cx = A(3), cy = A(4));
                    break;
                case "Z" or "z":
                    path.Close();
                    break;
            }
        }
        path.Transform(SKMatrix.CreateTranslation(dx, dy));
        return path;
    }

    private static SKPoint PathOffset(JsonElement o) =>
        o.TryGetProperty("pathOffset", out var po) && po.ValueKind == JsonValueKind.Object ? new SKPoint((float)Num(po, "x", 0), (float)Num(po, "y", 0)) : SKPoint.Empty;

    private void DrawImage(SKCanvas canvas, JsonElement o, float w, float h, RenderContext context, SKImageFilter? shadow)
    {
        var bitmap = LoadImage(Str(o, "src"), context);
        if (bitmap is null || w <= 0 || h <= 0)
            return;
        var cropX = (float)Num(o, "cropX", 0);
        var cropY = (float)Num(o, "cropY", 0);
        // fabric: width/height = بخش دیده‌شدهٔ عکس اصلی (پیکسل) از نقطهٔ crop؛ مقیاس با scaleX/Y بیرون اعمال شده.
        var source = new SKRect(cropX, cropY, Math.Min(bitmap.Width, cropX + w), Math.Min(bitmap.Height, cropY + h));
        if (source.Width <= 0 || source.Height <= 0)
            return;
        using var image = SKImage.FromBitmap(bitmap);
        using var paint = new SKPaint { IsAntialias = true, ImageFilter = shadow };
        var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);
        var whole = new SKRect(-cropX, -cropY, bitmap.Width - cropX, bitmap.Height - cropY);
        if (cropX <= 0 && cropY <= 0 && w >= bitmap.Width && h >= bitmap.Height)
        {
            canvas.DrawImage(image, whole, sampling, paint);
            return;
        }
        // برش fabric: فقط بخش دیده‌شده (سایه هم داخل همان کادر می‌ماند).
        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, source.Width, source.Height));
        canvas.DrawImage(image, whole, sampling, paint);
        canvas.Restore();
    }

    /// <summary>
    /// فقط فایل‌های خود سایت: «/uploads/…» یا «/assets/…» (آدرس کامل https://… هم فقط مسیرش خوانده می‌شود). دانلود از اینترنت ممنوع.
    /// مثال: «https://shop.fratak.ir/uploads/products/a.webp» ← wwwroot/uploads/products/a.webp.
    /// </summary>
    public static string? LocalImagePath(string? src, string webRoot)
    {
        if (string.IsNullOrWhiteSpace(src) || src.Length > 1000 || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;
        string path;
        if (src.StartsWith('/') && !src.StartsWith("//", StringComparison.Ordinal))
            path = src.Split('?', '#')[0];
        else if (Uri.TryCreate(src, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            path = uri.AbsolutePath;
        else
            return null;
        path = Uri.UnescapeDataString(path);
        if (path.Contains("..", StringComparison.Ordinal) || path.Contains('\\') || path.Contains(':')
            || !(path.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)))
            return null;
        var full = Path.GetFullPath(Path.Combine(webRoot, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        var rootFull = Path.GetFullPath(webRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(rootFull, StringComparison.Ordinal) ? full : null;
    }

    private SKBitmap? LoadImage(string? src, RenderContext context)
    {
        var path = LocalImagePath(src, context.WebRoot);
        if (path is null)
            return null;
        if (context.Images.TryGetValue(path, out var cached))
            return cached;
        SKBitmap? bitmap = null;
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length <= 15 * 1024 * 1024)
            {
                bitmap = SKBitmap.Decode(path);
                if (bitmap is not null && (long)bitmap.Width * bitmap.Height > 40_000_000)
                {
                    bitmap.Dispose();
                    bitmap = null;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogDebug(exception, "Design image {Path} could not be read", path);
        }
        context.Images[path] = bitmap;
        return bitmap;
    }

    // ───────────────────────────── رنگ، گرادیان، سایه ─────────────────────────────

    private static SKPaint? FillPaint(JsonElement o, float w, float h, float inset)
    {
        if (!o.TryGetProperty("fill", out var fill))
            return new SKPaint { IsAntialias = true, Color = SKColors.Black };
        if (fill.ValueKind == JsonValueKind.String)
        {
            var color = ParseColor(fill.GetString());
            return color is null ? null : new SKPaint { IsAntialias = true, Color = color.Value };
        }
        if (fill.ValueKind != JsonValueKind.Object)
            return null;
        var shader = Gradient(fill, w, h, inset);
        return shader is null ? null : new SKPaint { IsAntialias = true, Shader = shader };
    }

    /// <summary>گرادیان fabric (linear/radial). مختصات pixels نسبت به گوشهٔ بالا-چپ شیء؛ percentage نسبت به ابعاد.</summary>
    private static SKShader? Gradient(JsonElement g, float w, float h, float inset)
    {
        if (!g.TryGetProperty("coords", out var coords) || !g.TryGetProperty("colorStops", out var stops) || stops.ValueKind != JsonValueKind.Array)
            return null;
        var percentage = Str(g, "gradientUnits") == "percentage";
        float X(string key) => (float)(Num(coords, key, 0) * (percentage ? w : 1) + Num(g, "offsetX", 0)) + inset;
        float Y(string key) => (float)(Num(coords, key, 0) * (percentage ? h : 1) + Num(g, "offsetY", 0)) + inset;
        var list = stops.EnumerateArray()
            .Select(s => (Offset: (float)Math.Clamp(Num(s, "offset", 0), 0, 1), Color: ParseColor(Str(s, "color")), Opacity: Num(s, "opacity", 1)))
            .Where(s => s.Color is not null)
            .OrderBy(s => s.Offset)
            .Take(16)
            .ToList();
        if (list.Count == 0)
            return null;
        if (list.Count == 1)
            list.Add(list[0]);
        var colors = list.Select(s => s.Color!.Value.WithAlpha((byte)Math.Round(s.Color!.Value.Alpha * Math.Clamp(s.Opacity, 0, 1)))).ToArray();
        var positions = list.Select(s => s.Offset).ToArray();
        if (Str(g, "type") == "radial")
        {
            var scale = percentage ? Math.Max(w, h) : 1;
            return SKShader.CreateTwoPointConicalGradient(new SKPoint(X("x1"), Y("y1")), (float)(Num(coords, "r1", 0) * scale),
                new SKPoint(X("x2"), Y("y2")), (float)Math.Max(0.1, Num(coords, "r2", Math.Max(w, h) / 2.0) * scale), colors, positions, SKShaderTileMode.Clamp);
        }
        return SKShader.CreateLinearGradient(new SKPoint(X("x1"), Y("y1")), new SKPoint(X("x2"), Y("y2")), colors, positions, SKShaderTileMode.Clamp);
    }

    private static SKImageFilter? ShadowFilter(JsonElement o)
    {
        if (!o.TryGetProperty("shadow", out var s) || s.ValueKind != JsonValueKind.Object)
            return null;
        var color = ParseColor(Str(s, "color"));
        if (color is null)
            return null;
        var sigma = (float)Math.Max(0, Num(s, "blur", 0)) / 2f;
        return SKImageFilter.CreateDropShadow((float)Num(s, "offsetX", 0), (float)Num(s, "offsetY", 0), sigma, sigma, color.Value);
    }

    /// <summary>
    /// رنگ CSS: «#fff»، «#ffcc00»، «#ffcc0080»، «rgb(…)»، «rgba(…)»، «transparent»، «white/black». نامعتبر یا خالی ← null.
    /// </summary>
    public static SKColor? ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim().ToLowerInvariant();
        switch (value)
        {
            case "transparent" or "none":
                return null;
            case "white":
                return SKColors.White;
            case "black":
                return SKColors.Black;
            case "red":
                return SKColors.Red;
        }
        if (value[0] == '#')
        {
            // CSS و Fabric آلفا را آخر می‌نویسند (#RRGGBBAA و #RGBA) ولی SKColor.TryParse آن را اول می‌خواند (#AARRGGBB)؛
            // پس این دو شکل را به ترتیب Skia برمی‌گردانیم. مثال: «#FF000080» ← قرمزِ نیمه‌شفاف (نه آبیِ کم‌رنگ).
            var digits = value[1..];
            if (digits.Length == 8)
                digits = digits[6..] + digits[..6];
            else if (digits.Length == 4)
                digits = digits[3..] + digits[..3];
            return SKColor.TryParse("#" + digits, out var hex) ? hex : null;
        }
        if (value.StartsWith("rgb", StringComparison.Ordinal))
        {
            var open = value.IndexOf('(');
            var close = value.IndexOf(')');
            if (open < 0 || close <= open)
                return null;
            var parts = value[(open + 1)..close].Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                return null;
            static byte Channel(string p) => (byte)Math.Clamp(Math.Round(p.EndsWith('%')
                ? double.Parse(p.TrimEnd('%'), CultureInfo.InvariantCulture) * 2.55
                : double.Parse(p, CultureInfo.InvariantCulture)), 0, 255);
            try
            {
                var alpha = parts.Length >= 4
                    ? parts[3].EndsWith('%') ? double.Parse(parts[3].TrimEnd('%'), CultureInfo.InvariantCulture) / 100 : double.Parse(parts[3], CultureInfo.InvariantCulture)
                    : 1;
                return new SKColor(Channel(parts[0]), Channel(parts[1]), Channel(parts[2]), (byte)Math.Clamp(Math.Round(alpha * 255), 0, 255));
            }
            catch (FormatException)
            {
                return null;
            }
        }
        return null;
    }

    // ───────────────────────────── متن ─────────────────────────────

    private sealed record TextRun(float X, float Width, SKShaper.Result Shape, SKFont Font);

    private sealed record TextLine(List<TextRun> Runs, float Width);

    private sealed record TextLayout(List<TextLine> Lines, float Width, float Height, float FontSize, float LineHeight, string Align, SKFont Font);

    private static TextLayout LayoutText(JsonElement o, FontSet fonts)
    {
        var text = (Str(o, "text") ?? "").Replace("\r\n", "\n", StringComparison.Ordinal);
        if (text.Length > 2000)
            text = text[..2000];
        var fontSize = (float)Math.Clamp(Num(o, "fontSize", 40), 4, 600);
        var lineHeight = (float)Math.Clamp(Num(o, "lineHeight", 1.16), 0.5, 4);
        var weight = Weight(o);
        var rtl = Str(o, "direction") != "ltr";
        var type = Str(o, "type")?.ToLowerInvariant();
        var boxWidth = (float)Num(o, "width", 0);
        var wrap = type == "textbox" && boxWidth > 0;
        var typeface = fonts.For(weight);
        var font = new SKFont(typeface, fontSize) { Subpixel = true, Edging = SKFontEdging.Antialias };
        var shaper = fonts.Shaper(weight);

        var lines = new List<TextLine>();
        foreach (var paragraph in text.Split('\n'))
        {
            if (!wrap)
            {
                lines.Add(ShapeLine(paragraph, rtl, shaper, font));
                continue;
            }
            // شکستن خط مثل Textbox: واژه‌به‌واژه تا جایی که عرض واقعی از عرض جعبه بیشتر نشود.
            var words = paragraph.Split(' ');
            var current = new StringBuilder();
            foreach (var word in words)
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && Measure(candidate, rtl, shaper, font) > boxWidth + 0.5f)
                {
                    lines.Add(ShapeLine(current.ToString(), rtl, shaper, font));
                    current.Clear().Append(word);
                }
                else
                {
                    current.Clear().Append(candidate);
                }
            }
            lines.Add(ShapeLine(current.ToString(), rtl, shaper, font));
        }
        if (lines.Count > 60)
            lines = lines.Take(60).ToList();

        var width = wrap ? boxWidth : Math.Max(1, lines.Count == 0 ? 0 : lines.Max(l => l.Width));
        var lineBox = fontSize * FontSizeMult;
        // fabric: هر خط lineBox × lineHeight، خط آخر بدون ضریب lineHeight.
        var height = lines.Count == 0 ? lineBox : lineBox * lineHeight * (lines.Count - 1) + lineBox;
        return new TextLayout(lines, width, height, fontSize, lineHeight, Str(o, "textAlign") ?? (rtl ? "right" : "left"), font);
    }

    private static float Measure(string text, bool rtl, SKShaper shaper, SKFont font) => ShapeLine(text, rtl, shaper, font).Width;

    /// <summary>یک خط با ترتیب دوجهته: تکه‌ها به ترتیب نمایش (راست‌به‌چپ: آخرین تکهٔ منطقی سمت چپ) کنار هم می‌نشینند.</summary>
    private static TextLine ShapeLine(string text, bool rtl, SKShaper shaper, SKFont font)
    {
        var runs = new List<TextRun>();
        if (text.Length == 0)
            return new TextLine(runs, 0);
        var segments = BidiSegments(text, rtl);
        if (rtl)
            segments.Reverse();
        float x = 0;
        foreach (var (segment, segmentRtl) in segments)
        {
            using var buffer = new Buffer();
            buffer.AddUtf16(segment);
            buffer.Direction = segmentRtl ? Direction.RightToLeft : Direction.LeftToRight;
            buffer.GuessSegmentProperties();
            var shaped = shaper.Shape(buffer, font);
            runs.Add(new TextRun(x, shaped.Width, shaped, font));
            x += shaped.Width;
        }
        return new TextLine(runs, x);
    }

    /// <summary>
    /// تقسیم ساده‌شدهٔ دوجهته (کافی برای تیتر بنر): حروف فارسی/عربی = راست‌به‌چپ؛ حروف لاتین و رقم‌ها (فارسی یا لاتین، همراه «٪ % , . / :»
    /// بین/کنار رقم) = چپ‌به‌راست؛ فاصله و علامت‌ها جهت همسایه‌ها را می‌گیرند و اگر دو طرف فرق داشتند، جهت پاراگراف.
    /// مثال (پاراگراف راست‌به‌چپ): «تخفیف ۲۵٪ Galaxy S24» ← [«تخفیف »، «۲۵٪»، « »، «Galaxy S24»] با جهت‌های [R، L، R، L].
    /// </summary>
    public static List<(string Text, bool Rtl)> BidiSegments(string text, bool baseRtl)
    {
        var classes = new int[text.Length]; // 1 = R، 2 = L، 0 = خنثی
        for (var i = 0; i < text.Length; i++)
            classes[i] = Classify(text[i]);
        // علامت‌های عددی کنار رقم جزو عدد (٪ بعد از عدد، جداکنندهٔ بین دو رقم).
        for (var i = 0; i < text.Length; i++)
        {
            if (classes[i] != 0)
                continue;
            var c = text[i];
            var prevDigit = i > 0 && IsDigit(text[i - 1]);
            var nextDigit = i + 1 < text.Length && IsDigit(text[i + 1]);
            if ((c is '٪' or '%' && (prevDigit || nextDigit)) || (c is ',' or '.' or '٬' or '٫' or '/' or ':' && prevDigit && nextDigit))
                classes[i] = 2;
        }
        // خنثی‌ها: اگر قوی قبلی و بعدی یکی بودند همان، وگرنه جهت پاراگراف.
        var baseClass = baseRtl ? 1 : 2;
        for (var i = 0; i < text.Length; i++)
        {
            if (classes[i] != 0)
                continue;
            var j = i;
            while (j < text.Length && classes[j] == 0)
                j++;
            var before = i > 0 ? classes[i - 1] : baseClass;
            var after = j < text.Length ? classes[j] : baseClass;
            var resolved = before == after ? before : baseClass;
            for (var k = i; k < j; k++)
                classes[k] = resolved;
            i = j - 1;
        }
        var result = new List<(string, bool)>();
        var start = 0;
        for (var i = 1; i <= text.Length; i++)
        {
            if (i == text.Length || classes[i] != classes[start])
            {
                result.Add((text[start..i], classes[start] == 1));
                start = i;
            }
        }
        return result;
    }

    private static bool IsDigit(char c) => c is (>= '0' and <= '9') or (>= '۰' and <= '۹') or (>= '٠' and <= '٩');

    private static int Classify(char c)
    {
        if (IsDigit(c))
            return 2;
        if (c is '٪' or '٬' or '٫')
            return 0; // علامت عددی: جهتش از رقم کناری می‌آید
        if (c is (>= '\u0590' and <= '\u08FF') or (>= '\uFB1D' and <= '\uFDFF') or (>= '\uFE70' and <= '\uFEFF'))
            return 1; // شامل «،» «؛» «؟» که در متن فارسی راست‌به‌چپ‌اند
        if (c == '\u200C' || c == '\u200D')
            return 1;
        return char.IsLetter(c) ? 2 : 0;
    }

    private static void DrawText(SKCanvas canvas, JsonElement o, TextLayout layout, float strokeWidth, SKColor? stroke, SKImageFilter? shadow)
    {
        using var fill = FillPaint(o, layout.Width, layout.Height, 0);
        var lineBox = layout.FontSize * FontSizeMult;
        var underline = Bool(o, "underline");
        var linethrough = Bool(o, "linethrough");
        float top = 0;
        foreach (var line in layout.Lines)
        {
            var x = layout.Align switch
            {
                "center" or "justify-center" => (layout.Width - line.Width) / 2,
                "right" or "justify-right" or "justify" => layout.Width - line.Width,
                _ => 0,
            };
            var baseline = top + lineBox - lineBox * FontSizeFraction;
            foreach (var run in line.Runs)
            {
                if (run.Shape.Codepoints.Length == 0)
                    continue;
                using var builder = new SKTextBlobBuilder();
                var buffer = builder.AllocateRawPositionedRun(run.Font, run.Shape.Codepoints.Length, null);
                var glyphs = buffer.Glyphs;
                var positions = buffer.Positions;
                for (var i = 0; i < run.Shape.Codepoints.Length; i++)
                {
                    glyphs[i] = (ushort)run.Shape.Codepoints[i];
                    positions[i] = run.Shape.Points[i];
                }
                using var blob = builder.Build();
                if (blob is null)
                    continue;
                if (fill is not null)
                {
                    fill.ImageFilter = shadow;
                    canvas.DrawText(blob, x + run.X, baseline, fill);
                }
                if (strokeWidth > 0 && stroke is { } strokeColor)
                {
                    using var strokePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth, Color = strokeColor };
                    canvas.DrawText(blob, x + run.X, baseline, strokePaint);
                }
            }
            if ((underline || linethrough) && fill is not null && line.Width > 0)
            {
                var thickness = Math.Max(1, layout.FontSize / 15);
                if (underline)
                    canvas.DrawRect(x, baseline + layout.FontSize * 0.12f, line.Width, thickness, fill);
                if (linethrough)
                    canvas.DrawRect(x, baseline - layout.FontSize * 0.32f, line.Width, thickness, fill);
            }
            top += lineBox * layout.LineHeight;
        }
    }

    private static int Weight(JsonElement o)
    {
        if (!o.TryGetProperty("fontWeight", out var weight))
            return 400;
        return weight.ValueKind switch
        {
            JsonValueKind.Number => weight.TryGetInt32(out var n) ? n : 400,
            JsonValueKind.String => weight.GetString() switch
            {
                "bold" or "bolder" => 700,
                var s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => 400,
            },
            _ => 400,
        };
    }

    // ───────────────────────────── قلم‌ها ─────────────────────────────

    /// <summary>سه وزن Vazirmatn-FD (معمولی ۴۰۰، ضخیم ۷۰۰، سیاه ۹۰۰) و شکل‌دهندهٔ HarfBuzz هر کدام.</summary>
    private sealed class FontSet
    {
        private readonly SKTypeface _regularFace;
        private readonly SKTypeface _boldFace;
        private readonly SKTypeface _blackFace;
        private readonly SKShaper _regular;
        private readonly SKShaper _bold;
        private readonly SKShaper _black;

        public FontSet(SKTypeface regular, SKTypeface bold, SKTypeface black)
        {
            _regularFace = regular;
            _boldFace = bold;
            _blackFace = black;
            _regular = new SKShaper(regular);
            _bold = ReferenceEquals(bold, regular) ? _regular : new SKShaper(bold);
            _black = ReferenceEquals(black, bold) ? _bold : new SKShaper(black);
        }

        public SKTypeface For(int weight) => weight >= 800 ? _blackFace : weight >= 600 ? _boldFace : _regularFace;

        /// <summary>
        /// شکل‌دهندهٔ همان وزن. Shape(Buffer, SKFont) بافر خودمان را می‌گیرد و فونت HarfBuzz فقط خوانده می‌شود؛ پس استفادهٔ هم‌زمان امن است.
        /// </summary>
        public SKShaper Shaper(int weight) => weight >= 800 ? _black : weight >= 600 ? _bold : _regular;
    }

    private static FontSet? LoadFonts()
    {
        try
        {
            var folder = new[] { Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts"), Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Fonts") }
                .FirstOrDefault(f => File.Exists(Path.Combine(f, "Vazirmatn-FD-Regular.ttf")));
            if (folder is null)
                return null;
            var regular = SKTypeface.FromFile(Path.Combine(folder, "Vazirmatn-FD-Regular.ttf"));
            var bold = SKTypeface.FromFile(Path.Combine(folder, "Vazirmatn-FD-Bold.ttf")) ?? regular;
            var black = SKTypeface.FromFile(Path.Combine(folder, "Vazirmatn-FD-Black.ttf")) ?? bold;
            if (regular is null)
                return null;
            var set = new FontSet(regular, bold!, black!);
            // آزمایش بار شدن کتابخانهٔ بومی HarfBuzz همین‌جا (نه وسط کار شبانه).
            using var probe = new Buffer();
            probe.AddUtf16("سلام");
            probe.GuessSegmentProperties();
            using var font = new SKFont(regular, 20);
            _ = set.Shaper(400).Shape(probe, font);
            return set;
        }
        catch (Exception)
        {
            // کتابخانهٔ بومی یا قلم در دسترس نیست ← ساخت پیش‌نمایش در سرور غیرفعال؛ ویرایشگر مرورگر همچنان کار می‌کند.
            return null;
        }
    }

    // ───────────────────────────── خواندن JSON ─────────────────────────────

    private static string? Str(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Num(JsonElement o, string name, double fallback)
    {
        if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty(name, out var v))
            return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d))
            return Math.Clamp(d, -100_000, 100_000);
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed))
            return Math.Clamp(parsed, -100_000, 100_000);
        return fallback;
    }

    private static bool Bool(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
