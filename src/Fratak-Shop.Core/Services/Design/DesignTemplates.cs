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
        // دور ۲۶: پالت‌های تازه برای تنوع بیشتر (همه با کنتراست متن ≥ ۴٫۵).
        new("mint", "نعنایی", "#ecfdf5", "#a7f3d0", "#064e3b", "#065f46", "#059669", "#ffffff"),
        new("coral", "مرجانی", "#fff1f2", "#fecdd3", "#881337", "#9f1239", "#e11d48", "#ffffff"),
        new("indigo", "نیلی", "#312e81", "#1e1b4b", "#ffffff", "#c7d2fe", "#f472b6", "#1e1b4b"),
        new("sky", "آسمانی", "#f0f9ff", "#bae6fd", "#0c4a6e", "#075985", "#0284c7", "#ffffff"),
        new("candy", "شیرین", "#f472b6", "#8b5cf6", "#ffffff", "#fce7f3", "#fde047", "#4c1d95"),
        new("espresso", "قهوه‌ای", "#44281d", "#1c120c", "#fef3c7", "#e7d3c0", "#f59e0b", "#1c120c"),
    ];

    /// <summary>
    /// پالت با کلید؛ ناشناخته ← اولی (آبی فراتک). دور ۲۳: کلید رنگی «#rrggbb» (رنگ غالب عکس کالا) ← پالت خوانای ساخته‌شده با
    /// <see cref="DesignColors.FromColor"/> (کنتراست متن ≥ ۴٫۵). مثال: Find("#1d4ed8") ← پس‌زمینهٔ آبی با متن سفید.
    /// </summary>
    public static DesignPalette Find(string? key)
    {
        if (key is not null && key.StartsWith('#') && DesignColors.TryParse(key, out _))
            return DesignColors.FromColor(key);
        return All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)) ?? All[0];
    }
}

/// <summary>عکس داخل طرح: آدرس محلی (/uploads/…) + ابعاد واقعی (برای جا دادن بدون کشیدگی).</summary>
public sealed record DesignImage(string Url, int Width, int Height);

/// <summary>
/// محتوای یک طرح (بدون اندازه). مثال: چیدمان «sale»، پالت «festive»، تیتر «گوشی گلکسی A55»، نشان «۲۵٪ تخفیف»، قیمت «۱۸٬۹۰۰٬۰۰۰ تومان».
/// </summary>
/// <param name="Layout">split (متن + عکس)، center (وسط‌چین بدون عکس بزرگ)، sale (حراج با نشان بزرگ)، minimal (ساده)، festive (جشنواره).</param>
/// <param name="Kicker">برچسب کوچک بالای تیتر. مثال: «پیشنهاد ویژهٔ این هفته».</param>
/// <param name="Gallery">دور ۲۶: چند عکس (کالاهای یک دسته) برای چیدمان «collage»؛ نبود ← همان Image.</param>
public sealed record DesignSpec(string Layout, string Palette, string Headline, string? Subline = null, string? Badge = null, string? ButtonText = null,
    DesignImage? Image = null, string? Price = null, string? OldPrice = null, string? Kicker = null, IReadOnlyList<DesignImage>? Gallery = null);

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
        // دور ۲۶
        ("photo", "عکس تمام‌قاب (دسته/کمپین)"),
        ("collage", "چند کالا کنار هم"),
        ("pattern", "طرح هندسی بدون عکس"),
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

    /// <summary>
    /// دور ۲۶ – بنر پهن و کوتاه با ۸ چیدمان. قاعدهٔ کلی (راست‌چین): متن سمت راست، تصویر/عنصر بصری سمت چپ؛ تیتر دست‌کم ۲۰٪ ارتفاع
    /// (در موبایل هم خوانا، چون نسخهٔ موبایل جدا نداریم). هر شیء برچسب حرکت (fsAnim) دارد تا نسخهٔ زندهٔ بنر عنصرها را وارد/خارج کند.
    /// </summary>
    private static void BuildWide(JsonArray o, DesignSpec spec, DesignPalette p, int w, int h)
    {
        var m = Math.Max(20, Math.Round(Math.Min(h * 0.13, w * 0.045)));
        var layout = spec.Layout;
        var gallery = (spec.Gallery ?? Array.Empty<DesignImage>()).Where(g => g.Width > 0 && g.Height > 0).Take(3).ToList();
        if (gallery.Count == 0 && spec.Image is not null)
            gallery.Add(spec.Image);
        if (layout == "photo" && spec.Image is null)
            layout = "pattern";
        if (layout == "collage" && gallery.Count == 0)
            layout = "pattern";

        var visualShare = layout switch
        {
            "center" => 0.0,
            "collage" => 0.5,
            "photo" => 0.0,
            "sale" => 0.44,
            "pattern" or "festive" when gallery.Count == 0 => 0.0,
            _ => gallery.Count > 0 ? 0.44 : 0.0,
        };
        var textOnSide = visualShare > 0 || layout == "photo";

        // پس‌زمینه
        if (layout == "photo")
            PhotoBackground(o, spec.Image!, p, w, h);
        else
        {
            o.Add(Background(p, w, h, layout));
            Decorations(o, p, w, h, layout, visualShare);
        }

        // عنصر بصری سمت چپ
        var area = (X: m * 0.8, Y: m * 0.45, W: w * visualShare - m * 1.1, H: h - m * 0.9);
        switch (layout)
        {
            case "collage":
                Collage(o, gallery, p, area.X, area.Y, area.W, area.H);
                break;
            case "sale":
                SaleBurst(o, spec, p, area, gallery.FirstOrDefault(), h);
                break;
            case "photo" or "center":
                break;
            default:
                if (visualShare > 0 && gallery.Count > 0)
                {
                    var r = Math.Min(area.W, area.H * 1.25) * 0.52;
                    var blob = Circle("halo", area.X + area.W / 2 - r, area.Y + area.H / 2 - r, r, layout == "minimal" ? p.SubInk : p.Accent, layout == "minimal" ? 0.08 : 0.22);
                    Anim(blob, "pop", 0.05);
                    o.Add(blob);
                    var ring = Circle("deco", area.X + area.W / 2 - r * 1.18, area.Y + area.H / 2 - r * 1.18, r * 1.18, "transparent", 1);
                    ring["stroke"] = p.Ink;
                    ring["strokeWidth"] = Math.Max(2, Math.Round(h * 0.006));
                    ring["opacity"] = 0.18;
                    Anim(ring, "drift", 0.1);
                    o.Add(ring);
                    var img = Image(gallery[0], area.X, area.Y, area.W, area.H);
                    Anim(img, "slide-l", 0.2);
                    o.Add(img);
                }
                break;
        }

        // ستون متن (اگر بلند شد، همه با هم کوچک می‌شوند تا در ارتفاع جا شوند)
        var textLeft = textOnSide ? (layout == "photo" ? w * 0.4 : w * visualShare + m * 0.3) : w * 0.14;
        var textWidth = textOnSide ? w - m - textLeft : w * 0.72;
        var align = textOnSide ? "right" : "center";
        var available = h - m * 1.1;
        Stack stack = TextStack(spec, p, textLeft, textWidth, h, w, align, wide: true, scale: 1, onPhoto: layout == "photo");
        for (var scale = 0.92; stack.Height > available && scale > 0.5; scale *= 0.92)
            stack = TextStack(spec, p, textLeft, textWidth, h, w, align, wide: true, scale: scale, onPhoto: layout == "photo");
        var top = Math.Max(m * 0.55, (h - stack.Height) / 2);
        foreach (var item in stack.Items)
            o.Add(Offset(item, top));

        if (!string.IsNullOrWhiteSpace(spec.Badge) && layout != "sale")
        {
            var r = h * 0.15;
            var cx = textOnSide && layout != "photo" ? Math.Max(m * 0.4, w * visualShare - r * 1.6) : w - m * 0.5 - r * 2;
            AddBadge(o, spec.Badge!, p, cx, m * 0.35, r);
        }
    }

    /// <summary>عکس تمام‌قاب (cover) + پردهٔ تیرهٔ گرادیانی سمت متن تا متن سفید روی هر عکسی خوانا باشد.</summary>
    private static void PhotoBackground(JsonArray o, DesignImage image, DesignPalette p, int w, int h)
    {
        o.Add(Rect("bg", 0, 0, w, h, "#0f172a", selectable: false));
        var iw = Math.Max(1, image.Width);
        var ih = Math.Max(1, image.Height);
        var scale = Math.Max((double)w / iw, (double)h / ih);
        var photo = Base("Image", "image", (w - iw * scale) / 2, (h - ih * scale) / 2);
        photo["src"] = image.Url;
        photo["width"] = iw;
        photo["height"] = ih;
        photo["scaleX"] = Math.Round(scale, 5);
        photo["scaleY"] = Math.Round(scale, 5);
        photo["strokeWidth"] = 0;
        Anim(photo, "zoom", 0);
        o.Add(photo);
        var scrim = Rect("scrim", 0, 0, w, h, "#000000", selectable: false);
        scrim["fill"] = new JsonObject
        {
            ["type"] = "linear",
            ["gradientUnits"] = "pixels",
            ["coords"] = new JsonObject { ["x1"] = w, ["y1"] = 0, ["x2"] = w * 0.25, ["y2"] = 0 },
            ["colorStops"] = new JsonArray
            {
                new JsonObject { ["offset"] = 0, ["color"] = "rgba(2,6,23,0.86)" },
                new JsonObject { ["offset"] = 0.55, ["color"] = "rgba(2,6,23,0.55)" },
                new JsonObject { ["offset"] = 1, ["color"] = "rgba(2,6,23,0)" },
            },
            ["offsetX"] = 0,
            ["offsetY"] = 0,
        };
        o.Add(scrim);
        // نوار رنگی باریک پالت در لبهٔ راست (هویت رنگی بنر)
        o.Add(Rect("deco", w - Math.Round(h * 0.03), 0, Math.Round(h * 0.03), h, p.Accent));
    }

    /// <summary>تا ۳ کارت سفید گردگوشه با سایه، کمی چرخیده، هر کدام یک عکس کالا (برای بنر دسته).</summary>
    private static void Collage(JsonArray o, List<DesignImage> images, DesignPalette p, double x, double y, double areaW, double areaH)
    {
        var count = images.Count;
        var cardH = areaH * 0.86;
        var cardW = Math.Min(cardH * 0.82, areaW / (count == 1 ? 1.2 : count * 0.86));
        var angles = count switch { 1 => new[] { -3.0 }, 2 => new[] { -5.0, 4.0 }, _ => new[] { -7.0, 0.0, 6.0 } };
        var step = count == 1 ? 0 : (areaW - cardW) / (count - 1);
        for (var i = 0; i < count; i++)
        {
            var cx = x + (count == 1 ? (areaW - cardW) / 2 : step * i);
            var cy = y + (areaH - cardH) / 2 + (i == 1 ? -areaH * 0.03 : areaH * 0.02);
            var card = Rect("card", cx, cy, cardW, cardH, "#ffffff");
            card["rx"] = Math.Round(cardW * 0.08);
            card["ry"] = Math.Round(cardW * 0.08);
            card["angle"] = angles[i];
            card["shadow"] = Shadow("rgba(15,23,42,0.22)", Math.Round(cardW * 0.08), Math.Round(cardW * 0.03));
            Anim(card, "rise", 0.15 + i * 0.15);
            o.Add(card);
            var pad = cardW * 0.1;
            // عکس داخل کارت (بدون چرخش تا رندر سرور و HTML یکی باشند؛ چرخش کم کارت به چشم نمی‌آید)
            var img = Image(images[i], cx + pad, cy + pad, cardW - pad * 2, cardH - pad * 2, shadow: false);
            Anim(img, "rise", 0.2 + i * 0.15);
            o.Add(img);
        }
        var dot = Circle("deco", x + areaW * 0.02, y + areaH * 0.72, areaH * 0.09, p.Accent, 0.9);
        Anim(dot, "float", 0.6);
        o.Add(dot);
    }

    /// <summary>
    /// چیدمان حراج: دایرهٔ بزرگ رنگ تأکیدی با درصد درشت (از نشان؛ مثل «۲۵٪») نیمه‌بیرون از قاب + عکس کالا روی آن.
    /// نشان بدون عدد ← همان متن نشان. بدون نشان ← فقط عکس.
    /// </summary>
    private static void SaleBurst(JsonArray o, DesignSpec spec, DesignPalette p, (double X, double Y, double W, double H) area, DesignImage? image, int h)
    {
        var r = h * 0.62;
        var burst = Circle("badge-bg", area.X - r * 0.55, h / 2.0 - r, r, p.Accent, 1);
        Anim(burst, "pop", 0.05);
        o.Add(burst);
        if (!string.IsNullOrWhiteSpace(spec.Badge))
        {
            var match = System.Text.RegularExpressions.Regex.Match(spec.Badge!, @"[۰-۹0-9]{1,2}\s*[٪%]", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(50));
            var big = match.Success ? match.Value.Replace(" ", "", StringComparison.Ordinal) : spec.Badge!;
            var rest = match.Success ? spec.Badge!.Replace(match.Value, "", StringComparison.Ordinal).Trim() : null;
            var textW = r * 1.05;
            var left = area.X - r * 0.55 + r - textW / 2 + r * 0.22;
            var (fs, lines) = Fit(big, textW, h * (match.Success ? 0.36 : 0.16), h * 0.1, match.Success ? 1 : 2);
            var top = h / 2.0 - (lines * fs * LineFactor + (rest is null ? 0 : h * 0.09)) / 2;
            var number = Text("badge", big, left, top, textW, fs, 900, p.AccentInk, "center");
            Anim(number, "pop", 0.3);
            o.Add(number);
            if (!string.IsNullOrWhiteSpace(rest))
            {
                var label = Text("badge", rest!, left, top + lines * fs * LineFactor - h * 0.02, textW, Math.Round(h * 0.075), 800, p.AccentInk, "center");
                Anim(label, "rise", 0.45);
                o.Add(label);
            }
        }
        if (image is not null)
        {
            var imgArea = (X: area.X + area.W * 0.42, Y: area.Y + area.H * 0.06, W: area.W * 0.58, H: area.H * 0.88);
            var img = Image(image, imgArea.X, imgArea.Y, imgArea.W, imgArea.H);
            Anim(img, "slide-l", 0.35);
            o.Add(img);
        }
    }

    /// <summary>برچسب حرکت شیء برای نسخهٔ زنده (DesignLive): نوع حرکت و تأخیر به ثانیه. رندر تصویر و ویرایشگر نادیده‌اش می‌گیرند.</summary>
    private static JsonObject Anim(JsonObject item, string kind, double delay)
    {
        item["fsAnim"] = kind;
        item["fsAnimDelay"] = Math.Round(delay, 2);
        return item;
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

    /// <summary>
    /// ستون متن: برچسب (کپسول رنگی)، تیتر، متن دوم، قیمت و دکمه (با «←»)، با فاصلهٔ متناسب. top هر شیء نسبی است (بعداً جابه‌جا می‌شود).
    /// دور ۲۶: در بنر پهن تیتر تا ۲۲٪ ارتفاع (خوانا در موبایل)؛ <paramref name="scale"/> برای جا شدن ستون بلند در ارتفاع کم.
    /// <paramref name="onPhoto"/> = روی عکس تمام‌قاب ← متن سفید.
    /// </summary>
    private static Stack TextStack(DesignSpec spec, DesignPalette p, double left, double width, int h, int w, string align, bool wide, double scale = 1, bool onPhoto = false)
    {
        var unit = (wide ? h : Math.Min(w, h * 0.75)) * scale;
        var items = new List<JsonObject>();
        var y = 0.0;
        var gap = unit * 0.04;
        var ink = onPhoto ? "#ffffff" : p.Ink;
        var subInk = onPhoto ? "#e2e8f0" : p.SubInk;

        if (!string.IsNullOrWhiteSpace(spec.Kicker))
        {
            var fs = Math.Round(unit * (wide ? 0.062 : 0.045));
            var kw = Math.Min(width, EstimateWidth(spec.Kicker!, fs) + fs * 1.8);
            var kh = Math.Round(fs * 1.9);
            var kx = align switch { "right" => left + width - kw, "center" => left + (width - kw) / 2, _ => left };
            var pill = Rect("kicker-bg", kx, y, kw, kh, onPhoto ? p.Accent : p.Ink);
            pill["rx"] = Math.Round(kh / 2);
            pill["ry"] = Math.Round(kh / 2);
            pill["opacity"] = onPhoto ? 1 : 0.12;
            Anim(pill, "rise", 0.05);
            items.Add(pill);
            var t = Text("kicker", spec.Kicker!, kx, y + (kh - fs * LineFactor) / 2, kw, fs, 700, onPhoto ? p.AccentInk : ink, "center");
            Anim(t, "rise", 0.1);
            items.Add(t);
            y += kh + gap * 0.8;
        }

        var maxHead = unit * (spec.Layout == "minimal" ? 0.17 : wide ? 0.22 : 0.11);
        var (headFs, headLines) = Fit(spec.Headline, width, maxHead, unit * 0.09, 2);
        var head = Text("headline", spec.Headline, left, y, width, headFs, spec.Layout == "minimal" ? 800 : 900, ink, align);
        if (onPhoto)
            head["shadow"] = Shadow("rgba(0,0,0,0.35)", Math.Round(headFs * 0.2), Math.Round(headFs * 0.04));
        Anim(head, "slide-r", 0.2);
        items.Add(head);
        y += headLines * headFs * LineFactor + gap;

        if (!string.IsNullOrWhiteSpace(spec.Subline))
        {
            var (fs, lines) = Fit(spec.Subline!, width, unit * (wide ? 0.075 : 0.058), unit * 0.05, wide ? 1 : 2);
            var sub = Text("subline", spec.Subline!, left, y, width, fs, 500, subInk, align);
            Anim(sub, "rise", 0.4);
            items.Add(sub);
            y += lines * fs * LineFactor + gap;
        }

        if (!string.IsNullOrWhiteSpace(spec.Price))
        {
            var fs = Math.Round(unit * (wide ? 0.09 : 0.075));
            if (!string.IsNullOrWhiteSpace(spec.OldPrice))
            {
                var ofs = Math.Round(fs * 0.58);
                var old = Text("old-price", spec.OldPrice!, left, y, width, ofs, 400, subInk, align);
                old["linethrough"] = true;
                old["opacity"] = 0.85;
                Anim(old, "rise", 0.5);
                items.Add(old);
                y += ofs * LineFactor;
            }
            var price = Text("price", spec.Price!, left, y, width, fs, 800, ink, align);
            Anim(price, "pop", 0.55);
            items.Add(price);
            y += fs * LineFactor + gap;
        }

        if (!string.IsNullOrWhiteSpace(spec.ButtonText))
        {
            var label = spec.ButtonText!.Trim();
            if (wide && !label.Contains('←'))
                label += "  ←";
            var fs = Math.Round(unit * (wide ? 0.068 : 0.045));
            var bh = Math.Round(fs * 2.25);
            var bw = Math.Min(width, Math.Round(EstimateWidth(label, fs) + fs * 2.6));
            var bx = align switch
            {
                "right" => left + width - bw,
                "center" => left + (width - bw) / 2,
                _ => left,
            };
            y += gap * 0.3;
            var bg = Rect("cta-bg", bx, y, bw, bh, p.Accent);
            bg["rx"] = Math.Round(bh / 2);
            bg["ry"] = Math.Round(bh / 2);
            bg["shadow"] = Shadow("rgba(0,0,0,0.2)", Math.Round(bh * 0.35), Math.Round(bh * 0.12));
            Anim(bg, "shine", 0.7);
            items.Add(bg);
            var cta = Text("cta", label, bx, y + (bh - fs * LineFactor) / 2, bw, fs, 800, p.AccentInk, "center");
            Anim(cta, "pop", 0.75);
            items.Add(cta);
            y += bh;
        }
        return new Stack(items, y);
    }

    private static void AddBadge(JsonArray o, string text, DesignPalette p, double left, double top, double r)
    {
        var circle = Circle("badge-bg", left, top, r, p.Accent, 1);
        circle["shadow"] = Shadow("rgba(0,0,0,0.22)", Math.Round(r * 0.25), Math.Round(r * 0.08));
        Anim(circle, "pulse", 0.6);
        o.Add(circle);
        var inner = r * 1.5;
        var (fs, lines) = Fit(text, inner, r * 0.42, r * 0.22, 2);
        var th = lines * fs * LineFactor;
        o.Add(Anim(Text("badge", text, left + r - inner / 2, top + r - th / 2, inner, fs, 900, p.AccentInk, "center"), "pulse", 0.6));
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

    /// <summary>
    /// تزئین پس‌زمینه بر اساس چیدمان (موقعیت‌ها ثابت‌اند؛ هر بار همان طرح، نه تصادفی).
    /// pattern ← نوارهای مورب و شبکهٔ نقطه؛ festive ← کاغذرنگی؛ بقیه ← دو دایرهٔ محو. visualShare = سهم ناحیهٔ تصویر از عرض.
    /// </summary>
    private static void Decorations(JsonArray o, DesignPalette p, int w, int h, string layout, double visualShare = 0)
    {
        if (layout == "minimal")
        {
            var bar = Rect("deco", w - Math.Round(h * 0.035), h * 0.2, Math.Round(h * 0.012), h * 0.6, p.Accent);
            bar["rx"] = Math.Round(h * 0.006);
            Anim(bar, "rise", 0);
            o.Add(bar);
            return;
        }
        var s = Math.Min(w, h);
        var c1 = Circle("deco", -s * 0.25, -s * 0.35, s * 0.6, p.Ink, 0.07);
        Anim(c1, "drift", 0);
        o.Add(c1);
        var c2 = Circle("deco", w - s * 0.55, h - s * 0.45, s * 0.45, p.Ink, 0.06);
        Anim(c2, "drift", 0.4);
        o.Add(c2);
        if (layout == "pattern")
        {
            // نوارهای مورب پهن در نیمهٔ چپ + شبکهٔ نقطه‌ها: پس‌زمینهٔ زنده بدون نیاز به عکس.
            var span = visualShare > 0 ? w * visualShare : w * 0.4;
            for (var i = 0; i < 6; i++)
            {
                var stripe = Rect("deco", span * (i * 0.2) - h * 0.2, -h * 0.3, h * 0.12, h * 1.8, i % 2 == 0 ? p.Accent : p.Ink);
                stripe["angle"] = -24;
                stripe["opacity"] = i % 2 == 0 ? 0.16 : 0.07;
                Anim(stripe, "drift", 0.1 * i);
                o.Add(stripe);
            }
            for (var row = 0; row < 3; row++)
                for (var col = 0; col < 5; col++)
                    o.Add(Circle("deco", w * 0.86 - col * h * 0.08, h * 0.12 + row * h * 0.08, h * 0.012, p.Ink, 0.25));
        }
        if (layout == "festive")
        {
            // کاغذرنگی (دایره و مستطیل چرخیده) با رنگ تأکیدی و متن.
            double[][] dots = [[0.06, 0.15, 0.018], [0.2, 0.82, 0.012], [0.38, 0.1, 0.014], [0.6, 0.88, 0.02], [0.76, 0.1, 0.012], [0.92, 0.62, 0.016], [0.5, 0.2, 0.01]];
            var index = 0;
            foreach (var d in dots)
            {
                var piece = index % 2 == 0
                    ? Circle("deco", w * d[0], h * d[1], s * d[2] * 2, p.Accent, 0.9)
                    : Rect("deco", w * d[0], h * d[1], s * d[2] * 3, s * d[2] * 1.4, p.Ink);
                if (index % 2 == 1)
                {
                    piece["angle"] = 25 + index * 15;
                    piece["opacity"] = 0.5;
                }
                Anim(piece, "float", 0.1 * index);
                o.Add(piece);
                index++;
            }
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
