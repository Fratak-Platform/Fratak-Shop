using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Fratak_Shop.Core.Services.Design;

/// <summary>
/// پالت رنگ یک طرح. From/To = دو رنگ گرادیان پس‌زمینه، Ink = رنگ تیتر، SubInk = رنگ متن دوم، Accent/AccentInk = دکمه و نشان.
/// مثال: «ocean» = آبی فراتک با دکمهٔ زرد.
/// </summary>
public sealed record DesignPalette(string Key, string Title, string From, string To, string Ink, string SubInk, string Accent, string AccentInk);

/// <summary>
/// پالت‌های آمادهٔ استودیو (دور ۲۱). همه با کنتراست کافی متن روی پس‌زمینه (خوانایی در موبایل و آفتاب).
/// محل استفاده: <see cref="DesignTemplates"/>، <see cref="DesignIdeas"/> و منوی «رنگ‌ها» در ویرایشگر.
/// </summary>
public static class DesignPalettes
{
    public static readonly IReadOnlyList<DesignPalette> All =
    [
        new("ocean", "آبی فراتک", "#0ea5e9", "#1d4ed8", "#ffffff", "#e0f2fe", "#facc15", "#1e293b"),
        new("sunset", "غروب", "#fb923c", "#db2777", "#ffffff", "#ffedd5", "#ffffff", "#be185d"),
        new("forest", "سبز", "#10b981", "#065f46", "#ffffff", "#d1fae5", "#fde047", "#064e3b"),
        new("night", "شب", "#0f172a", "#334155", "#ffffff", "#cbd5e1", "#22d3ee", "#0f172a"),
        new("royal", "بنفش", "#8b5cf6", "#4338ca", "#ffffff", "#ede9fe", "#fbbf24", "#312e81"),
        new("gold", "طلایی", "#1c1917", "#44403c", "#fde68a", "#e7e5e4", "#f59e0b", "#1c1917"),
        new("festive", "جشن", "#dc2626", "#f59e0b", "#ffffff", "#fef3c7", "#ffffff", "#b91c1c"),
        new("clean", "روشن", "#f8fafc", "#e0f2fe", "#0f172a", "#475569", "#2563eb", "#ffffff"),
    ];

    /// <summary>پالت با کلید؛ ناشناخته ← اولی (آبی فراتک).</summary>
    public static DesignPalette Find(string? key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}

/// <summary>عکس داخل طرح: آدرس محلی (/uploads/…) + ابعاد واقعی (برای جا دادن بدون کشیدگی).</summary>
public sealed record DesignImage(string Url, int Width, int Height);

/// <summary>
/// محتوای یک طرح (بدون اندازه). مثال: چیدمان «sale»، پالت «festive»، تیتر «گوشی گلکسی A55»، نشان «۲۵٪ تخفیف»، قیمت «۱۸٬۹۰۰٬۰۰۰ تومان».
/// </summary>
/// <param name="Layout">split (متن + عکس)، center (وسط‌چین بدون عکس بزرگ)، sale (حراج با نشان بزرگ)، minimal (ساده)، festive (جشنواره).</param>
/// <param name="Kicker">برچسب کوچک بالای تیتر. مثال: «پیشنهاد ویژهٔ این هفته».</param>
public sealed record DesignSpec(string Layout, string Palette, string Headline, string? Subline = null, string? Badge = null, string? ButtonText = null,
    DesignImage? Image = null, string? Price = null, string? OldPrice = null, string? Kicker = null);

/// <summary>
/// سازندهٔ طرح‌های آماده به زبان ویرایشگر Fabric.js (دور ۲۱) – بدون هیچ هوش مصنوعی: چیدمان با قواعد ثابت گرافیکی
/// (حاشیهٔ امن، شبکهٔ ۱۲ ستونه، اندازهٔ متن متناسب با ارتفاع، تیتر حداکثر ۲ خط، کنتراست پالت).
/// <para>خروجی JSON است: هم ویرایشگر مرورگر (canvas.loadFromJSON) بازش می‌کند و مدیر می‌تواند هر لایه را جابه‌جا کند،
/// هم رندرکنندهٔ سرور (Web/Infrastructure/Design/DesignRenderer.cs) از روی همین JSON تصویر WebP می‌سازد.</para>
/// <para>هر شیء یک «fsRole» دارد (bg، deco، image، halo، kicker، headline، subline، price، old-price، badge-bg، badge، cta-bg، cta)
/// تا ویرایشگر بداند کدام لایه پس‌زمینه است و سرور بتواند تیتر را بخواند.</para>
/// مثال: <c>DesignTemplates.Build(spec, 1920, 640)</c> ← JSON با پس‌زمینهٔ گرادیان، عکس کالا سمت چپ و متن راست‌چین سمت راست.
/// </summary>
public static class DesignTemplates
{
    /// <summary>نسخهٔ Fabric.js که JSON برایش ساخته می‌شود (wwwroot/lib/fabric).</summary>
    public const string FabricVersion = "7.4.0";

    /// <summary>نام قلم در ویرایشگر و سرور (Vazirmatn با ارقام فارسی).</summary>
    public const string Font = "Vazirmatn";

    /// <summary>چیدمان‌های آماده برای منوی «قالب‌ها».</summary>
    public static readonly IReadOnlyList<(string Key, string Title)> Layouts =
    [
        ("split", "متن + عکس کالا"),
        ("sale", "حراج با نشان تخفیف"),
        ("center", "وسط‌چین"),
        ("minimal", "ساده و شیک"),
        ("festive", "جشنواره"),
    ];

    // ضریب ارتفاع خط Fabric: lineHeight(1.16) × _fontSizeMult(1.13).
    private const double LineFactor = 1.16 * 1.13;

    /// <summary>ساخت JSON کامل طرح در اندازهٔ داده‌شده. transparent = بدون پس‌زمینه (کارت تبلیغاتی).</summary>
    public static string Build(DesignSpec spec, int width, int height, bool transparent = false)
    {
        var palette = DesignPalettes.Find(spec.Palette);
        var objects = new JsonArray();
        if (transparent)
            BuildCutout(objects, spec, palette, width, height);
        else if (width >= height * 1.6)
            BuildWide(objects, spec, palette, width, height);
        else
            BuildTall(objects, spec, palette, width, height);

        var root = new JsonObject
        {
            ["version"] = FabricVersion,
            ["objects"] = objects,
            ["background"] = transparent ? "" : palette.To,
            ["fsWidth"] = width,
            ["fsHeight"] = height,
        };
        return root.ToJsonString();
    }

    /// <summary>طرح خالی (فقط پس‌زمینهٔ سفید یا شفاف) برای «طرح تازه».</summary>
    public static string Blank(int width, int height, bool transparent = false)
    {
        var objects = new JsonArray();
        if (!transparent)
            objects.Add(Rect("bg", 0, 0, width, height, "#ffffff", selectable: false));
        return new JsonObject
        {
            ["version"] = FabricVersion,
            ["objects"] = objects,
            ["background"] = transparent ? "" : "#ffffff",
            ["fsWidth"] = width,
            ["fsHeight"] = height,
        }.ToJsonString();
    }

    // ───────────────────────────── چیدمان افقی (اسلایدر، بنر عریض، کاور) ─────────────────────────────

    private static void BuildWide(JsonArray o, DesignSpec spec, DesignPalette p, int w, int h)
    {
        var m = Math.Max(24, Math.Round(Math.Min(h * 0.12, w * 0.05)));
        o.Add(Background(p, w, h, spec.Layout));
        Decorations(o, p, w, h, spec.Layout);

        var hasImage = spec.Image is not null && spec.Layout != "center";
        var textLeft = hasImage ? w * 0.46 : w * 0.15;
        var textWidth = hasImage ? w - m - textLeft : w * 0.7;
        var align = hasImage ? "right" : "center";

        if (hasImage)
        {
            var area = (X: m, Y: m * 0.55, W: w * 0.46 - m * 1.2, H: h - m * 1.1);
            var r = Math.Min(area.W, area.H) * 0.5;
            o.Add(Circle("halo", area.X + area.W / 2 - r, area.Y + area.H / 2 - r, r, p.Ink, 0.14));
            o.Add(Image(spec.Image!, area.X, area.Y, area.W, area.H));
        }

        var stack = TextStack(spec, p, textLeft, textWidth, h, w, align, wide: true);
        var top = Math.Max(m * 0.6, (h - stack.Height) / 2);
        foreach (var item in stack.Items)
            o.Add(Offset(item, top));

        if (!string.IsNullOrWhiteSpace(spec.Badge))
        {
            var r = h * (spec.Layout == "sale" ? 0.17 : 0.13);
            var cx = hasImage ? m + r * 0.9 : w - m - r * 2;
            AddBadge(o, spec.Badge!, p, cx, m * 0.45, r);
        }
    }

    // ───────────────────────────── چیدمان مربعی/عمودی (پست، استوری، نسخهٔ موبایل) ─────────────────────────────

    private static void BuildTall(JsonArray o, DesignSpec spec, DesignPalette p, int w, int h)
    {
        var m = Math.Max(24, Math.Round(w * 0.07));
        o.Add(Background(p, w, h, spec.Layout));
        Decorations(o, p, w, h, spec.Layout);

        var stack = TextStack(spec, p, m, w - m * 2, h, w, "center", wide: false);
        var top = m;
        foreach (var item in stack.Items)
            o.Add(Offset(item, top));

        if (spec.Image is not null)
        {
            var y = top + stack.Height + m * 0.4;
            var area = (X: m, Y: y, W: w - m * 2, H: h - y - m * 0.6);
            if (area.H > h * 0.18)
            {
                var r = Math.Min(area.W, area.H) * 0.5;
                o.Add(Circle("halo", area.X + area.W / 2 - r, area.Y + area.H / 2 - r, r, p.Ink, 0.14));
                o.Add(Image(spec.Image, area.X, area.Y, area.W, area.H));
            }
        }

        if (!string.IsNullOrWhiteSpace(spec.Badge))
        {
            var r = Math.Min(w, h) * 0.11;
            AddBadge(o, spec.Badge!, p, m * 0.5, h - m * 0.5 - r * 2, r);
        }
    }

    // ───────────────────────────── کارت تبلیغاتی (عکس شفاف) ─────────────────────────────

    private static void BuildCutout(JsonArray o, DesignSpec spec, DesignPalette p, int w, int h)
    {
        var m = Math.Round(Math.Min(w, h) * 0.04);
        if (spec.Image is not null)
            o.Add(Image(spec.Image, m, m, w - m * 2, h - m * 2, shadow: true));
        if (!string.IsNullOrWhiteSpace(spec.Badge))
        {
            var r = Math.Min(w, h) * 0.14;
            AddBadge(o, spec.Badge!, p, w - m - r * 2, m, r);
        }
    }

    // ───────────────────────────── اجزا ─────────────────────────────

    private sealed record Stack(List<JsonObject> Items, double Height);

    /// <summary>ستون متن: برچسب، تیتر، متن دوم، قیمت و دکمه، با فاصلهٔ متناسب. top هر شیء نسبی است (بعداً جابه‌جا می‌شود).</summary>
    private static Stack TextStack(DesignSpec spec, DesignPalette p, double left, double width, int h, int w, string align, bool wide)
    {
        var unit = wide ? h : Math.Min(w, h * 0.75);
        var items = new List<JsonObject>();
        var y = 0.0;
        var gap = unit * 0.035;

        if (!string.IsNullOrWhiteSpace(spec.Kicker))
        {
            var fs = Math.Round(unit * (wide ? 0.05 : 0.045));
            var t = Text("kicker", spec.Kicker!, left, y, width, fs, 600, p.SubInk, align);
            items.Add(t);
            y += Height(spec.Kicker!, fs, width, 1) + gap * 0.6;
        }

        var maxHead = unit * (spec.Layout == "minimal" ? 0.11 : wide ? 0.135 : 0.11);
        var (headFs, headLines) = Fit(spec.Headline, width, maxHead, unit * 0.06, 2);
        items.Add(Text("headline", spec.Headline, left, y, width, headFs, spec.Layout == "minimal" ? 700 : 900, p.Ink, align));
        y += headLines * headFs * LineFactor + gap;

        if (!string.IsNullOrWhiteSpace(spec.Subline))
        {
            var (fs, lines) = Fit(spec.Subline!, width, unit * 0.058, unit * 0.04, 2);
            items.Add(Text("subline", spec.Subline!, left, y, width, fs, 400, p.SubInk, align));
            y += lines * fs * LineFactor + gap;
        }

        if (!string.IsNullOrWhiteSpace(spec.Price))
        {
            var fs = Math.Round(unit * 0.075);
            if (!string.IsNullOrWhiteSpace(spec.OldPrice))
            {
                var ofs = Math.Round(fs * 0.6);
                var old = Text("old-price", spec.OldPrice!, left, y, width, ofs, 400, p.SubInk, align);
                old["linethrough"] = true;
                old["opacity"] = 0.85;
                items.Add(old);
                y += ofs * LineFactor;
            }
            items.Add(Text("price", spec.Price!, left, y, width, fs, 800, p.Ink, align));
            y += fs * LineFactor + gap;
        }

        if (!string.IsNullOrWhiteSpace(spec.ButtonText))
        {
            var fs = Math.Round(unit * (wide ? 0.052 : 0.045));
            var bh = Math.Round(fs * 2.3);
            var bw = Math.Min(width, Math.Round(EstimateWidth(spec.ButtonText!, fs) + fs * 2.4));
            var bx = align switch
            {
                "right" => left + width - bw,
                "center" => left + (width - bw) / 2,
                _ => left,
            };
            y += gap * 0.4;
            var bg = Rect("cta-bg", bx, y, bw, bh, p.Accent);
            bg["rx"] = Math.Round(bh * 0.3);
            bg["ry"] = Math.Round(bh * 0.3);
            bg["shadow"] = Shadow("rgba(0,0,0,0.18)", Math.Round(bh * 0.3), Math.Round(bh * 0.12));
            items.Add(bg);
            var label = Text("cta", spec.ButtonText!, bx, y + (bh - fs * LineFactor) / 2, bw, fs, 700, p.AccentInk, "center");
            items.Add(label);
            y += bh;
        }
        return new Stack(items, y);
    }

    private static void AddBadge(JsonArray o, string text, DesignPalette p, double left, double top, double r)
    {
        var circle = Circle("badge-bg", left, top, r, p.Accent, 1);
        circle["shadow"] = Shadow("rgba(0,0,0,0.22)", Math.Round(r * 0.25), Math.Round(r * 0.08));
        o.Add(circle);
        var inner = r * 1.5;
        var (fs, lines) = Fit(text, inner, r * 0.42, r * 0.22, 2);
        var th = lines * fs * LineFactor;
        o.Add(Text("badge", text, left + r - inner / 2, top + r - th / 2, inner, fs, 900, p.AccentInk, "center"));
    }

    private static JsonObject Background(DesignPalette p, int w, int h, string layout)
    {
        var bg = Rect("bg", 0, 0, w, h, p.To, selectable: false);
        // گرادیان مورب از گوشهٔ بالا-راست (جای متن) به پایین-چپ: متن روی رنگ روشن‌تر/یکدست‌تر می‌نشیند.
        bg["fill"] = new JsonObject
        {
            ["type"] = "linear",
            ["gradientUnits"] = "pixels",
            ["coords"] = new JsonObject { ["x1"] = w, ["y1"] = 0, ["x2"] = 0, ["y2"] = h },
            ["colorStops"] = new JsonArray
            {
                new JsonObject { ["offset"] = 0, ["color"] = layout == "minimal" ? p.To : p.From },
                new JsonObject { ["offset"] = 1, ["color"] = p.To },
            },
            ["offsetX"] = 0,
            ["offsetY"] = 0,
        };
        return bg;
    }

    private static void Decorations(JsonArray o, DesignPalette p, int w, int h, string layout)
    {
        if (layout == "minimal")
            return;
        var s = Math.Min(w, h);
        o.Add(Circle("deco", -s * 0.25, -s * 0.35, s * 0.6, p.Ink, 0.07));
        o.Add(Circle("deco", w - s * 0.55, h - s * 0.45, s * 0.45, p.Ink, 0.06));
        if (layout == "festive")
        {
            // دانه‌های رنگی جشن (موقعیت ثابت = هر بار همان طرح؛ تصادفی نیست).
            double[][] dots = [[0.08, 0.15, 0.018], [0.22, 0.82, 0.012], [0.4, 0.1, 0.014], [0.62, 0.88, 0.02], [0.78, 0.12, 0.012], [0.93, 0.6, 0.016]];
            foreach (var d in dots)
                o.Add(Circle("deco", w * d[0], h * d[1], s * d[2] * 2, p.Accent, 0.9));
        }
    }

    // ───────────────────────────── اشیای Fabric ─────────────────────────────

    private static JsonObject Base(string type, string role, double left, double top)
    {
        return new JsonObject
        {
            ["type"] = type,
            ["version"] = FabricVersion,
            ["originX"] = "left",
            ["originY"] = "top",
            ["left"] = R(left),
            ["top"] = R(top),
            ["fsRole"] = role,
        };
    }

    private static JsonObject Rect(string role, double left, double top, double width, double height, string fill, bool selectable = true)
    {
        var o = Base("Rect", role, left, top);
        o["width"] = R(width);
        o["height"] = R(height);
        o["fill"] = fill;
        o["strokeWidth"] = 0;
        if (!selectable)
        {
            o["selectable"] = false;
            o["evented"] = false;
        }
        return o;
    }

    private static JsonObject Circle(string role, double left, double top, double radius, string fill, double opacity)
    {
        var o = Base("Circle", role, left, top);
        o["radius"] = R(radius);
        o["width"] = R(radius * 2);
        o["height"] = R(radius * 2);
        o["fill"] = fill;
        o["opacity"] = Math.Round(opacity, 2);
        o["strokeWidth"] = 0;
        return o;
    }

    private static JsonObject Text(string role, string text, double left, double top, double width, double fontSize, int weight, string fill, string align)
    {
        var o = Base("Textbox", role, left, top);
        o["text"] = text;
        o["width"] = R(width);
        o["fontSize"] = R(fontSize);
        o["fontFamily"] = Font;
        o["fontWeight"] = weight;
        o["fill"] = fill;
        o["textAlign"] = align;
        o["direction"] = "rtl";
        o["lineHeight"] = 1.16;
        o["strokeWidth"] = 0;
        return o;
    }

    /// <summary>عکس «جاشده» (contain) وسط ناحیه، بدون کشیدگی.</summary>
    private static JsonObject Image(DesignImage image, double x, double y, double areaW, double areaH, bool shadow = true)
    {
        var iw = Math.Max(1, image.Width);
        var ih = Math.Max(1, image.Height);
        var scale = Math.Min(areaW / iw, areaH / ih);
        var o = Base("Image", "image", x + (areaW - iw * scale) / 2, y + (areaH - ih * scale) / 2);
        o["src"] = image.Url;
        o["width"] = iw;
        o["height"] = ih;
        o["scaleX"] = Math.Round(scale, 5);
        o["scaleY"] = Math.Round(scale, 5);
        o["strokeWidth"] = 0;
        if (shadow)
            o["shadow"] = Shadow("rgba(0,0,0,0.22)", Math.Round(Math.Min(areaW, areaH) * 0.06), Math.Round(Math.Min(areaW, areaH) * 0.03));
        return o;
    }

    private static JsonObject Shadow(string color, double blur, double offsetY) => new()
    {
        ["type"] = "shadow",
        ["color"] = color,
        ["blur"] = blur,
        ["offsetX"] = 0,
        ["offsetY"] = offsetY,
    };

    private static JsonObject Offset(JsonObject item, double top)
    {
        item["top"] = R((item["top"]?.GetValue<double>() ?? 0) + top);
        return item;
    }

    private static double R(double value) => Math.Round(value, 1);

    // ───────────────────────────── اندازهٔ متن ─────────────────────────────

    /// <summary>
    /// تخمین عرض متن فارسی با قلم Vazirmatn: میانگین هر حرف ≈ ۰٫۵ اندازهٔ قلم، فاصله ≈ ۰٫۲۵ (کافی برای چیدمان؛ رندر واقعی دقیق اندازه می‌گیرد).
    /// مثال: «تخفیف» با قلم ۴۰ ← حدود ۱۰۰ پیکسل.
    /// </summary>
    public static double EstimateWidth(string text, double fontSize)
    {
        double units = 0;
        foreach (var ch in text)
            units += ch == ' ' ? 0.25 : ch == '\u200c' ? 0 : char.IsDigit(ch) ? 0.55 : 0.5;
        return units * fontSize;
    }

    /// <summary>تعداد خط‌های متن در عرض داده‌شده (شکستن روی فاصله‌ها). مثال: تیتر ۴۰ حرفی با قلم ۸۰ در عرض ۹۰۰ ← ۲ خط.</summary>
    public static int EstimateLines(string text, double fontSize, double width)
    {
        var lines = 1;
        var current = 0.0;
        var space = 0.25 * fontSize;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var ww = EstimateWidth(word, fontSize);
            if (current > 0 && current + space + ww > width)
            {
                lines++;
                current = ww;
            }
            else
            {
                current += (current > 0 ? space : 0) + ww;
            }
        }
        return lines;
    }

    /// <summary>
    /// بزرگ‌ترین قلمی که متن را در حداکثر maxLines خط جا می‌دهد (از max پایین می‌آید تا min).
    /// مثال: تیتر کوتاه «حراج یلدا» ← همان max؛ نام کالای ۶۰ حرفی ← قلم کوچک‌تر تا ۲ خط شود.
    /// </summary>
    public static (double FontSize, int Lines) Fit(string text, double width, double max, double min, int maxLines)
    {
        var fs = Math.Round(max);
        while (fs > min && EstimateLines(text, fs, width) > maxLines)
            fs = Math.Round(fs * 0.94);
        fs = Math.Max(Math.Round(min), fs);
        return (fs, Math.Max(1, Math.Min(EstimateLines(text, fs, width), 4)));
    }

    private static double Height(string text, double fs, double width, int maxLines) =>
        Math.Min(maxLines, EstimateLines(text, fs, width)) * fs * LineFactor;

    // ───────────────────────────── کمکی متن ─────────────────────────────

    /// <summary>ارقام لاتین ← فارسی. مثال: «25%» ← «۲۵٪».</summary>
    public static string FaDigits(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
            sb.Append(ch switch
            {
                >= '0' and <= '9' => (char)('۰' + (ch - '0')),
                '%' => '٪',
                ',' => '٬',
                _ => ch,
            });
        return sb.ToString();
    }

    /// <summary>قیمت تومان خوانا با ارقام فارسی. مثال: 18900000 ← «۱۸٬۹۰۰٬۰۰۰ تومان».</summary>
    public static string Toman(decimal value) => FaDigits(decimal.Round(value).ToString("#,0", CultureInfo.InvariantCulture)) + " تومان";

    /// <summary>
    /// کوتاه کردن نام طولانی کالا روی مرز کلمه (تیتر بنر باید در یک نگاه خوانده شود).
    /// مثال: «گوشی موبایل سامسونگ مدل Galaxy A55 5G ظرفیت ۲۵۶ گیگابایت رم ۸ گیگابایت» (max ۴۰) ← «گوشی موبایل سامسونگ مدل Galaxy A55 5G».
    /// </summary>
    public static string Shorten(string text, int max)
    {
        text = string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length <= max)
            return text;
        var cut = text.LastIndexOf(' ', Math.Min(max, text.Length - 1));
        var result = (cut > max / 2 ? text[..cut] : text[..max]).TrimEnd('،', ',', '-', '–', ' ', '|');
        // حرف ربط/اضافهٔ آویزان در انتها زشت است.
        foreach (var tail in new[] { " و", " با", " مدل", " ظرفیت", " برای", " به", " از" })
            if (result.EndsWith(tail, StringComparison.Ordinal))
                result = result[..^tail.Length];
        return result;
    }
}
