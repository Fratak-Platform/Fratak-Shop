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
public sealed record DesignImage(string Url, int Width, int Height)
{
    /// <summary>
    /// دور ۲۷: عکس «دوربری‌شده» کالاست (زمینهٔ سفید شفاف شده و حاشیه بریده)؟ بله ← کالا آزاد روی رنگ/عکس بنر می‌نشیند (با سایه)؛
    /// نه ← داخل قاب گردگوشهٔ سفید (عکس محیطی کالا را نمی‌شود بی‌قاب روی رنگ گذاشت).
    /// </summary>
    public bool Cutout { get; init; }
}

/// <summary>
/// محتوای یک طرح (بدون اندازه). مثال: چیدمان «sale»، پالت «festive»، تیتر «گوشی گلکسی A55»، نشان «۲۵٪ تخفیف»، قیمت «۱۸٬۹۰۰٬۰۰۰ تومان».
/// </summary>
/// <param name="Layout">split (متن + عکس)، center (وسط‌چین بدون عکس بزرگ)، sale (حراج با نشان بزرگ)، minimal (ساده)، festive (جشنواره).</param>
/// <param name="Kicker">برچسب کوچک بالای تیتر. مثال: «پیشنهاد ویژهٔ این هفته».</param>
/// <param name="Gallery">دور ۲۶: چند عکس (کالاهای یک دسته) برای چیدمان «collage»؛ نبود ← همان Image.</param>
/// <param name="Photo">
/// دور ۲۷: عکس «فضاسازی» (عکس واقعی محیط؛ مثل آشپزخانهٔ رنگی برای «لوازم آشپزخانه») برای پس‌زمینهٔ چیدمان‌های photo/duo/festive.
/// جدا از عکس کالاست تا کالاهای دوربری‌شده روی همین عکس بنشینند. نبود ← چیدمان‌های عکس‌دار از Image (اگر دوربری‌شده نباشد) یا رنگ استفاده می‌کنند.
/// </param>
public sealed record DesignSpec(string Layout, string Palette, string Headline, string? Subline = null, string? Badge = null, string? ButtonText = null,
    DesignImage? Image = null, string? Price = null, string? OldPrice = null, string? Kicker = null, IReadOnlyList<DesignImage>? Gallery = null,
    DesignImage? Photo = null);

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
        ("photo", "عکس فضاسازی تمام‌قاب + کالا"),
        ("collage", "کاشی‌های شیشه‌ای چند کالا"),
        ("pattern", "طرح هندسی بدون عکس"),
        // دور ۲۷
        ("showcase", "ویترین: کالاهای دوربری‌شده روی سکو"),
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
    /// دور ۲۷ – بازطراحی کامل بنر پهن و کوتاه. قاعدهٔ کلی (راست‌چین): متن سمت راست، «صحنهٔ تصویری» سمت چپ.
    /// <list type="bullet">
    /// <item><b>عکس واقعی همیشه:</b> عکس کالا (ترجیحاً «دوربری‌شده» = بدون زمینهٔ سفید) روی هالهٔ نور، دیسک رنگی یا سکوی نمایش با سایهٔ تماس زیر کالا؛
    /// عکس فضاسازی دسته به‌صورت تمام‌قاب با پردهٔ رنگی پالت. عکسِ زمینه‌دار (دوربری‌نشدنی) داخل قاب گردگوشه می‌نشیند، نه خام روی رنگ.</item>
    /// <item><b>حرکت:</b> هر شیء برچسب fsAnim (ورود پله‌ای، معلق ماندن کالا، Ken Burns عکس، درخشش، چشمک ستاره‌ها) و fsDepth (پارالاکس) دارد.</item>
    /// <item><b>بی‌نقص در هر حالت:</b> هر چیدمانی بدون عکس هم خراب نمی‌شود (به چیدمان مناسب برمی‌گردد) و تیتر همیشه دست‌کم ۲۰٪ ارتفاع است
    /// (در موبایل هم خوانا، چون نسخهٔ موبایل جدا نداریم).</item>
    /// </list>
    /// </summary>
    private static void BuildWide(JsonArray o, DesignSpec spec, DesignPalette p, int w, int h)
    {
        var m = Math.Max(20, Math.Round(Math.Min(h * 0.13, w * 0.045)));
        var pics = Pictures(spec);
        var photo = ValidImage(spec.Photo);
        var layout = Layouts.Any(l => l.Key == spec.Layout) ? spec.Layout : "center";

        // ── انتخاب نهایی چیدمان بر اساس عکس‌های موجود (هیچ چیدمانی خالی و بی‌معنا ساخته نمی‌شود) ──
        if (layout == "photo")
        {
            // سازگاری با قبل: عکس فضاسازی در Image (نه دوربری‌شده) ← همان پس‌زمینه می‌شود.
            if (photo is null && spec.Image is { Cutout: false } legacy && ValidImage(legacy) is not null)
            {
                photo = legacy;
                pics = pics.Where(x => x.Url != legacy.Url).ToList();
            }
            if (photo is null)
                layout = pics.Count > 0 ? "showcase" : "pattern";
        }
        if (layout == "collage" && pics.Count < 2)
            layout = pics.Count == 1 ? "showcase" : "pattern";
        if (layout == "showcase" && pics.Count >= 2 && pics.All(x => !x.Cutout))
            layout = "collage"; // چند عکسِ زمینه‌دار روی سکو زشت است ← کاشی‌ها
        if (layout == "sale" && string.IsNullOrWhiteSpace(spec.Badge))
            layout = pics.Count > 0 ? "split" : "pattern";
        if (layout == "showcase" && pics.Count == 0)
            layout = "pattern"; // ویترین بدون کالا معنا ندارد

        var share = layout switch
        {
            "split" or "festive" when pics.Count > 0 => 0.46,
            "showcase" when pics.Count > 0 => 0.5,
            "collage" => 0.52,
            "sale" => 0.48,
            "minimal" when pics.Count > 0 => 0.42,
            _ => 0.0,
        };
        var onPhoto = layout == "photo";
        var textOnSide = share > 0 || onPhoto;
        var textLeft = onPhoto ? w * (pics.Any(x => x.Cutout) ? 0.5 : 0.44) : share > 0 ? w * share + m * 0.3 : w * 0.14;
        var textWidth = textOnSide ? w - m - textLeft : w * 0.72;

        // ── پس‌زمینه ──
        if (onPhoto)
            PhotoScene(o, photo!, p, w, h, textLeft);
        else
        {
            o.Add(Background(p, w, h, layout));
            Lights(o, p, w, h, layout);
            Decorations(o, p, w, h, layout, share);
        }

        // ── صحنهٔ تصویری سمت چپ ──
        var area = (X: m * 0.6, Y: h * 0.06, W: w * share - m * 0.9, H: h * 0.88);
        switch (layout)
        {
            case "photo":
                var floating = pics.Where(x => x.Cutout).Take(2).ToList();
                if (floating.Count > 0)
                    Showcase(o, floating, p, (w * 0.25, h * 0.08, textLeft - w * 0.25 - m * 0.4, h * 0.86), podium: false);
                break;
            case "showcase":
                Showcase(o, pics, p, area, podium: true);
                break;
            case "collage":
                Collage(o, pics, p, area);
                break;
            case "sale":
                SaleBurst(o, spec, p, area, pics.FirstOrDefault(), h);
                break;
            case "festive" when pics.Count(x => x.Cutout) >= 2:
                // جشن با چند کالای دوربری‌شده ← ویترین روی سکو (بین کاغذرنگی‌ها)
                Showcase(o, pics.Where(x => x.Cutout).ToList(), p, area, podium: true);
                break;
            case "split" or "festive" or "minimal" when pics.Count > 0:
                Spotlight(o, pics[0], p, area, minimal: layout == "minimal");
                break;
            case "center":
                SideProducts(o, pics.Where(x => x.Cutout).Take(2).ToList(), w, h, m);
                break;
        }

        // ── درخشش گذرا روی کل بنر (فقط نسخهٔ زنده؛ در تصویر ثابت دیده نمی‌شود) ──
        if (layout != "minimal")
        {
            var sweep = Rect("deco", 0, 0, w, h, "transparent", selectable: false);
            Anim(sweep, "sweep", 1.6);
            o.Add(sweep);
        }

        // ── ستون متن (اگر بلند شد، همه با هم کوچک می‌شوند تا در ارتفاع جا شوند) ──
        var align = textOnSide ? "right" : "center";
        var available = h - m * 1.1;
        var stack = TextStack(spec, p, textLeft, textWidth, h, w, align, wide: true, scale: 1, onPhoto: onPhoto);
        for (var scale = 0.92; stack.Height > available && scale > 0.5; scale *= 0.92)
            stack = TextStack(spec, p, textLeft, textWidth, h, w, align, wide: true, scale: scale, onPhoto: onPhoto);
        var top = Math.Max(m * 0.55, (h - stack.Height) / 2);
        foreach (var item in stack.Items)
            o.Add(Offset(item, top));

        if (!string.IsNullOrWhiteSpace(spec.Badge) && layout != "sale")
        {
            var r = h * 0.15;
            var left = onPhoto ? m * 0.5 : share > 0 ? Math.Max(m * 0.4, w * share - r * 1.7) : w - m * 0.5 - r * 2;
            AddBadge(o, spec.Badge!, p, left, m * 0.35, r);
        }
    }

    /// <summary>عکس‌های قابل‌استفادهٔ طرح: گالری (حداکثر ۳) یا همان عکس اصلی؛ عکس با ابعاد نامعتبر کنار می‌رود.</summary>
    private static List<DesignImage> Pictures(DesignSpec spec)
    {
        var list = (spec.Gallery ?? Array.Empty<DesignImage>()).Select(ValidImage).Where(x => x is not null).Select(x => x!)
            .DistinctBy(x => x.Url).Take(3).ToList();
        if (list.Count == 0 && ValidImage(spec.Image) is { } single)
            list.Add(single);
        // عکس اصلی (کالای قهرمان) اول باشد تا بزرگ‌ترین جای صحنه را بگیرد.
        var index = spec.Image is { } hero ? list.FindIndex(x => x.Url == hero.Url) : -1;
        if (index > 0)
        {
            var item = list[index];
            list.RemoveAt(index);
            list.Insert(0, item);
        }
        return list;
    }

    private static DesignImage? ValidImage(DesignImage? image) =>
        image is { Width: > 0, Height: > 0 } && !string.IsNullOrWhiteSpace(image.Url) ? image : null;

    /// <summary>
    /// دور ۲۷ – صحنهٔ «عکس فضاسازی»: عکس واقعی تمام‌قاب (cover) با حرکت آرام Ken Burns + پردهٔ گرادیانی <b>به رنگ پالت</b> از سمت متن
    /// (متن با رنگ‌های خود پالت خوانا می‌ماند و بنر هویت رنگی دارد) + سایهٔ ملایم پایین و نوار رنگی باریک لبهٔ راست.
    /// </summary>
    private static void PhotoScene(JsonArray o, DesignImage image, DesignPalette p, int w, int h, double textLeft)
    {
        o.Add(Rect("bg", 0, 0, w, h, p.To, selectable: false));
        var iw = Math.Max(1, image.Width);
        var ih = Math.Max(1, image.Height);
        // کمی بزرگ‌تر از قاب تا حرکت Ken Burns لبهٔ خالی نشان ندهد.
        var scale = Math.Max((double)w / iw, (double)h / ih) * 1.04;
        var photo = Base("Image", "image", (w - iw * scale) / 2, (h - ih * scale) / 2);
        photo["src"] = image.Url;
        photo["width"] = iw;
        photo["height"] = ih;
        photo["scaleX"] = Math.Round(scale, 5);
        photo["scaleY"] = Math.Round(scale, 5);
        photo["strokeWidth"] = 0;
        photo["fsPhoto"] = true;
        Anim(photo, "kenburns", 0);
        Depth(photo, 1);
        o.Add(photo);

        // پرده: از لبهٔ راست (رنگ پالت تقریباً کدر) تا کمی چپ‌تر از شروع متن (کاملاً شفاف).
        var fadeEnd = Math.Max(0, textLeft - w * 0.2);
        var scrim = Rect("scrim", 0, 0, w, h, "#000000", selectable: false);
        scrim["fill"] = new JsonObject
        {
            ["type"] = "linear",
            ["gradientUnits"] = "pixels",
            ["coords"] = new JsonObject { ["x1"] = w, ["y1"] = 0, ["x2"] = R(fadeEnd), ["y2"] = 0 },
            ["colorStops"] = new JsonArray
            {
                new JsonObject { ["offset"] = 0, ["color"] = Rgba(p.To, 0.96) },
                new JsonObject { ["offset"] = 0.5, ["color"] = Rgba(p.To, 0.86) },
                new JsonObject { ["offset"] = 0.78, ["color"] = Rgba(p.To, 0.45) },
                new JsonObject { ["offset"] = 1, ["color"] = Rgba(p.To, 0) },
            },
            ["offsetX"] = 0,
            ["offsetY"] = 0,
        };
        o.Add(scrim);
        // سایهٔ پایین (عمق و جدا شدن کالاها از عکس)
        var vignette = Rect("scrim", 0, h * 0.55, w, h * 0.45, "#000000", selectable: false);
        vignette["fill"] = new JsonObject
        {
            ["type"] = "linear",
            ["gradientUnits"] = "pixels",
            ["coords"] = new JsonObject { ["x1"] = 0, ["y1"] = R(h * 0.45), ["x2"] = 0, ["y2"] = 0 },
            ["colorStops"] = new JsonArray
            {
                new JsonObject { ["offset"] = 0, ["color"] = "rgba(2,6,23,0.35)" },
                new JsonObject { ["offset"] = 1, ["color"] = "rgba(2,6,23,0)" },
            },
            ["offsetX"] = 0,
            ["offsetY"] = 0,
        };
        o.Add(vignette);
        var bar = Rect("deco", w - Math.Round(h * 0.03), 0, Math.Round(h * 0.03), h, p.Accent);
        Anim(bar, "rise", 0);
        o.Add(bar);
    }

    /// <summary>
    /// دور ۲۷ – نورپردازی پس‌زمینه: دو هالهٔ نرم (گرادیان شعاعی) یکی به رنگ تأکیدی بالا-چپ و یکی روشن پایین-راست؛
    /// گرادیان تخت را «حرفه‌ای» و عمق‌دار می‌کند. چیدمان ساده (minimal) نور ندارد.
    /// </summary>
    private static void Lights(JsonArray o, DesignPalette p, int w, int h, string layout)
    {
        if (layout == "minimal")
            return;
        var s = Math.Max(w, h);
        o.Add(Depth(Anim(Glow("deco", w * 0.12, -h * 0.1, s * 0.32, p.Accent, 0.32), "breathe", 0), 1));
        o.Add(Depth(Anim(Glow("deco", w * 0.9, h * 1.05, s * 0.28, "#ffffff", 0.16), "breathe", 1.5), 1));
    }

    /// <summary>
    /// دور ۲۷ – «نورافکن» یک کالا: هالهٔ نور، دیسک رنگی گرادیانی، حلقهٔ نازک، ستاره‌های چشمک‌زن، سایهٔ تماس و خود کالا که معلق می‌ماند.
    /// minimal ← فقط سایه و حلقهٔ کم‌رنگ (شیک و خلوت).
    /// </summary>
    private static void Spotlight(JsonArray o, DesignImage image, DesignPalette p, (double X, double Y, double W, double H) area, bool minimal)
    {
        var cx = area.X + area.W / 2;
        var cy = area.Y + area.H / 2;
        var discR = Math.Min(area.W * 0.44, area.H * 0.5);
        if (!minimal)
        {
            o.Add(Depth(Anim(Glow("halo", cx, cy, discR * 1.9, p.Accent, 0.5), "breathe", 0), 1));
            var disc = Circle("halo", cx - discR, cy - discR, discR, p.Accent, 1);
            disc["fill"] = Linear(discR * 2, discR * 2, DesignColors.Mix(p.Accent, "#ffffff", 0.18), DesignColors.Mix(p.Accent, p.To, 0.5));
            disc["opacity"] = 0.95;
            o.Add(Depth(Anim(disc, "pop", 0.05), 1));
            Sparkles(o, p, cx, cy, discR * 1.25, 3);
        }
        var ring = Circle("deco", cx - discR * 1.18, cy - discR * 1.18, discR * 1.18, "transparent", 1);
        ring["stroke"] = minimal ? p.SubInk : p.Ink;
        ring["strokeWidth"] = Math.Max(2, Math.Round(area.H * 0.008));
        ring["opacity"] = minimal ? 0.25 : 0.2;
        o.Add(Depth(Anim(ring, "breathe", 0.3), 1));
        Product(o, image, area.X, area.Y, area.W, area.H, 0.25, 3, center: true);
    }

    /// <summary>
    /// دور ۲۷ – «ویترین»: کالای اصلی بزرگ وسط و تا دو کالای کوچک‌تر پشت سرش، روی سکوی بیضی رنگی (یا بدون سکو روی عکس فضاسازی)،
    /// همه با سایهٔ تماس و حرکت معلق با تأخیر متفاوت (حس زنده و سه‌بعدی).
    /// </summary>
    private static void Showcase(JsonArray o, List<DesignImage> images, DesignPalette p, (double X, double Y, double W, double H) area, bool podium)
    {
        if (images.Count == 0 || area.W <= 10 || area.H <= 10)
            return;
        var cx = area.X + area.W / 2;
        var ground = area.Y + area.H * (podium ? 0.92 : 0.98);
        if (podium)
        {
            o.Add(Depth(Anim(Glow("halo", cx, area.Y + area.H * 0.45, Math.Min(area.W * 0.6, area.H * 1.1), p.Accent, 0.55), "breathe", 0), 1));
            var pr = Math.Min(area.W * 0.46, area.H * 1.4);
            var stage = Circle("deco", cx - pr, ground - pr * 0.16, pr, p.Accent, 1);
            stage["scaleY"] = 0.16;
            stage["fill"] = Linear(pr * 2, pr * 2, DesignColors.Mix(p.Accent, "#ffffff", 0.35), DesignColors.Mix(p.Accent, "#000000", 0.2), vertical: true);
            stage["shadow"] = Shadow("rgba(2,6,23,0.3)", Math.Round(area.H * 0.06), Math.Round(area.H * 0.03));
            o.Add(Depth(Anim(stage, "rise", 0.05), 2));
            Sparkles(o, p, cx, area.Y + area.H * 0.42, Math.Min(area.W * 0.5, area.H * 0.9), 4);
        }
        // کالاهای کناری (پشت کالای اصلی)
        var sideH = area.H * (podium ? 0.6 : 0.62);
        var sideW = area.W * 0.36;
        var sides = images.Skip(1).Take(2).ToList();
        for (var i = 0; i < sides.Count; i++)
        {
            var x = i == 0 ? area.X : area.X + area.W - sideW;
            Product(o, sides[i], x, ground - sideH - area.H * 0.02, sideW, sideH, 0.45 + i * 0.15, 2);
        }
        var heroW = area.W * (sides.Count > 0 ? 0.5 : 0.8);
        var heroH = area.H * (podium ? 0.84 : 0.9);
        Product(o, images[0], cx - heroW / 2, ground - heroH, heroW, heroH, 0.25, 3);
    }

    /// <summary>
    /// دور ۲۷ – کاشی‌های «شیشه‌ای» (به‌جای کارت سفید قدیمی): تا ۳ کاشی گردگوشهٔ نیمه‌شفاف با کمی چرخش، کاشی وسط بالاتر،
    /// هر کدام یک کالا (دوربری‌شده آزاد با سایه؛ زمینه‌دار با حاشیهٔ کم). چرخش کاشی و عکس دور یک مرکز حساب می‌شود (ویرایشگر، تصویر ثابت و
    /// نسخهٔ زنده یکسان‌اند).
    /// </summary>
    private static void Collage(JsonArray o, List<DesignImage> images, DesignPalette p, (double X, double Y, double W, double H) area)
    {
        var count = Math.Min(3, images.Count);
        if (count == 0)
            return;
        var lightInk = DesignColors.TryParse(p.Ink, out var inkRgb) && DesignColors.Luminance(inkRgb) > 0.5;
        var tileH = area.H * 0.82;
        var gap = area.W * 0.035;
        var tileW = Math.Min(tileH * 0.86, (area.W - gap * (count - 1)) / count);
        var total = tileW * count + gap * (count - 1);
        var startX = area.X + (area.W - total) / 2;
        var angles = count switch { 1 => new[] { -3.0 }, 2 => new[] { -4.0, 4.0 }, _ => new[] { -6.0, 0.0, 6.0 } };
        o.Add(Depth(Anim(Glow("halo", area.X + area.W / 2, area.Y + area.H / 2, Math.Min(area.W * 0.55, area.H * 1.2), p.Accent, 0.45), "breathe", 0), 1));
        for (var i = 0; i < count; i++)
        {
            var raised = count == 3 && i == 1;
            var x = startX + i * (tileW + gap);
            var y = area.Y + (area.H - tileH) / 2 + (raised ? -area.H * 0.04 : area.H * 0.03);
            var c = (X: x + tileW / 2, Y: y + tileH / 2);
            var angle = angles[i];
            var delay = 0.15 + i * 0.15;

            var tile = Rect("card", 0, 0, tileW, tileH, lightInk ? "rgba(255,255,255,0.14)" : "rgba(255,255,255,0.78)");
            tile["rx"] = Math.Round(tileW * 0.1);
            tile["ry"] = Math.Round(tileW * 0.1);
            tile["stroke"] = lightInk ? "rgba(255,255,255,0.42)" : "rgba(255,255,255,0.95)";
            tile["strokeWidth"] = Math.Max(1, Math.Round(tileW * 0.012));
            tile["shadow"] = Shadow("rgba(2,6,23,0.22)", Math.Round(tileW * 0.12), Math.Round(tileW * 0.05));
            Place(tile, c, -tileW / 2, -tileH / 2, angle);
            o.Add(Depth(Anim(tile, "hero", delay), raised ? 3 : 2));

            var pad = tileW * (images[i].Cutout ? 0.1 : 0.06);
            var iw = Math.Max(1, images[i].Width);
            var ih = Math.Max(1, images[i].Height);
            var scale = Math.Min((tileW - pad * 2) / iw, (tileH - pad * 2) / ih);
            var img = ImageObject(images[i], 0, 0, scale);
            if (images[i].Cutout)
                img["shadow"] = Shadow("rgba(2,6,23,0.28)", Math.Round(tileW * 0.06), Math.Round(tileW * 0.03));
            Place(img, c, -iw * scale / 2, -ih * scale / 2, angle);
            o.Add(Depth(Anim(img, "hero", delay), raised ? 3 : 2));
        }
    }

    /// <summary>
    /// چیدمان حراج: دایرهٔ بزرگ رنگ تأکیدی با درصد درشت (از نشان؛ مثل «۲۵٪») نیمه‌بیرون از قاب + هالهٔ تپنده + کالا با سایه روی آن.
    /// نشان بدون عدد ← همان متن نشان.
    /// </summary>
    private static void SaleBurst(JsonArray o, DesignSpec spec, DesignPalette p, (double X, double Y, double W, double H) area, DesignImage? image, int h)
    {
        var r = h * 0.62;
        var burstLeft = area.X - r * 0.55;
        o.Add(Depth(Anim(Glow("halo", burstLeft + r, h / 2.0, r * 1.45, p.Accent, 0.5), "breathe", 0), 1));
        var burst = Circle("badge-bg", burstLeft, h / 2.0 - r, r, p.Accent, 1);
        burst["fill"] = Linear(r * 2, r * 2, DesignColors.Mix(p.Accent, "#ffffff", 0.12), DesignColors.Mix(p.Accent, "#000000", 0.12));
        burst["shadow"] = Shadow("rgba(2,6,23,0.25)", Math.Round(r * 0.12), Math.Round(r * 0.04));
        o.Add(Depth(Anim(burst, "pop", 0.05), 2));
        var ring = Circle("deco", burstLeft - r * 0.08, h / 2.0 - r * 1.08, r * 1.08, "transparent", 1);
        ring["stroke"] = p.Accent;
        ring["strokeWidth"] = Math.Max(2, Math.Round(h * 0.01));
        ring["opacity"] = 0.45;
        o.Add(Depth(Anim(ring, "breathe", 0.2), 1));
        if (!string.IsNullOrWhiteSpace(spec.Badge))
        {
            var match = System.Text.RegularExpressions.Regex.Match(spec.Badge!, @"[۰-۹0-9]{1,2}\s*[٪%]", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(50));
            var big = match.Success ? match.Value.Replace(" ", "", StringComparison.Ordinal) : spec.Badge!;
            var rest = match.Success ? spec.Badge!.Replace(match.Value, "", StringComparison.Ordinal).Trim() : null;
            var textW = r * 1.05;
            var left = burstLeft + r - textW / 2 + r * 0.22;
            var (fs, lines) = Fit(big, textW, h * (match.Success ? 0.36 : 0.16), h * 0.1, match.Success ? 1 : 2);
            var top = h / 2.0 - (lines * fs * LineFactor + (string.IsNullOrWhiteSpace(rest) ? 0 : h * 0.09)) / 2;
            var number = Text("badge", big, left, top, textW, fs, 900, p.AccentInk, "center");
            o.Add(Depth(Anim(number, "pop", 0.3), 2));
            if (!string.IsNullOrWhiteSpace(rest))
            {
                var label = Text("badge", rest!, left, top + lines * fs * LineFactor - h * 0.02, textW, Math.Round(h * 0.075), 800, p.AccentInk, "center");
                o.Add(Depth(Anim(label, "rise", 0.45), 2));
            }
        }
        if (image is not null)
            Product(o, image, area.X + area.W * 0.4, area.Y + area.H * 0.04, area.W * 0.6, area.H * 0.92, 0.4, 3);
    }

    /// <summary>دور ۲۷ – چیدمان وسط‌چین: کالای دوربری‌شده در دو لبهٔ بنر (قرینه) تا بنر بدون عکس نماند.</summary>
    private static void SideProducts(JsonArray o, List<DesignImage> images, int w, int h, double m)
    {
        var zoneW = w * 0.14 - m * 0.3;
        if (images.Count == 0 || zoneW < h * 0.25)
            return;
        Product(o, images[0], m * 0.3, h * 0.1, zoneW, h * 0.8, 0.3, 3);
        if (images.Count > 1)
            Product(o, images[1], w - m * 0.3 - zoneW, h * 0.1, zoneW, h * 0.8, 0.45, 3);
    }

    /// <summary>
    /// دور ۲۷ – گذاشتن یک کالا در ناحیه (contain، پایین‌چین یا وسط‌چین) با حرکت «hero» (ورود + معلق ماندن).
    /// دوربری‌شده ← آزاد با سایهٔ نرم و سایهٔ تماس بیضی زیرش؛ زمینه‌دار ← داخل قاب گردگوشهٔ سفید با سایه (هر دو با هم حرکت می‌کنند).
    /// </summary>
    private static void Product(JsonArray o, DesignImage image, double x, double y, double areaW, double areaH, double delay, int depth, bool center = false)
    {
        if (areaW <= 4 || areaH <= 4)
            return;
        var iw = Math.Max(1, image.Width);
        var ih = Math.Max(1, image.Height);
        if (image.Cutout)
        {
            var scale = Math.Min(areaW / iw, areaH / ih);
            var pw = iw * scale;
            var ph = ih * scale;
            var px = x + (areaW - pw) / 2;
            var py = center ? y + (areaH - ph) / 2 : y + areaH - ph;
            var contact = Glow("shadow", px + pw / 2, py + ph * 0.985, pw * 0.46, "#020617", 0.42, scaleY: 0.12);
            o.Add(Depth(Anim(contact, "shadow", delay), depth));
            var img = ImageObject(image, px, py, scale);
            img["shadow"] = Shadow("rgba(2,6,23,0.25)", Math.Round(Math.Min(pw, ph) * 0.06), Math.Round(Math.Min(pw, ph) * 0.03));
            o.Add(Depth(Anim(img, "hero", delay), depth));
            return;
        }
        var pad = Math.Min(areaW, areaH) * 0.045;
        var inner = Math.Min((areaW - pad * 2) / iw, (areaH - pad * 2) / ih);
        var cw = iw * inner + pad * 2;
        var ch = ih * inner + pad * 2;
        var cx = x + (areaW - cw) / 2;
        var cy = center ? y + (areaH - ch) / 2 : y + areaH - ch;
        var card = Rect("card", cx, cy, cw, ch, "#ffffff");
        card["rx"] = Math.Round(Math.Min(cw, ch) * 0.07);
        card["ry"] = Math.Round(Math.Min(cw, ch) * 0.07);
        card["shadow"] = Shadow("rgba(2,6,23,0.25)", Math.Round(Math.Min(cw, ch) * 0.09), Math.Round(Math.Min(cw, ch) * 0.04));
        o.Add(Depth(Anim(card, "hero", delay), depth));
        o.Add(Depth(Anim(ImageObject(image, cx + pad, cy + pad, inner), "hero", delay), depth));
    }

    /// <summary>ستاره‌های ریز چشمک‌زن دور صحنه (موقعیت ثابت؛ هر بار همان طرح).</summary>
    private static void Sparkles(JsonArray o, DesignPalette p, double cx, double cy, double radius, int count)
    {
        double[] angles = [215, 330, 70, 150];
        for (var i = 0; i < Math.Min(count, angles.Length); i++)
        {
            var a = angles[i] * Math.PI / 180;
            var r = Math.Max(3, radius * (i % 2 == 0 ? 0.045 : 0.03));
            var dot = Circle("deco", cx + Math.Cos(a) * radius - r, cy - Math.Sin(a) * radius - r, r, i % 2 == 0 ? "#ffffff" : p.Accent, 0.9);
            o.Add(Depth(Anim(dot, "twinkle", 0.4 + i * 0.35), 2));
        }
    }

    /// <summary>برچسب حرکت شیء برای نسخهٔ زنده (DesignLive): نوع حرکت و تأخیر به ثانیه. رندر تصویر و ویرایشگر نادیده‌اش می‌گیرند.</summary>
    private static JsonObject Anim(JsonObject item, string kind, double delay)
    {
        item["fsAnim"] = kind;
        item["fsAnimDelay"] = Math.Round(delay, 2);
        return item;
    }

    /// <summary>دور ۲۷ – عمق پارالاکس (۱ = دور/پس‌زمینه، ۳ = نزدیک/کالا): با حرکت موس یا اسکرول لایه‌ها با سرعت متفاوت جابه‌جا می‌شوند.</summary>
    private static JsonObject Depth(JsonObject item, int depth)
    {
        item["fsDepth"] = Math.Clamp(depth, 1, 3);
        return item;
    }

    // ───────────────────────────── چیدمان مربعی/عمودی (پست، استوری، نسخهٔ موبایل) ─────────────────────────────

    private static void BuildTall(JsonArray o, DesignSpec spec, DesignPalette p, int w, int h)
    {
        var m = Math.Max(24, Math.Round(w * 0.07));
        var layout = Layouts.Any(l => l.Key == spec.Layout) ? spec.Layout : "center";
        o.Add(Background(p, w, h, layout));
        Lights(o, p, w, h, layout);
        Decorations(o, p, w, h, layout);

        var stack = TextStack(spec, p, m, w - m * 2, h, w, "center", wide: false);
        var top = m;
        foreach (var item in stack.Items)
            o.Add(Offset(item, top));

        var pics = Pictures(spec);
        if (pics.Count > 0 && layout is not ("pattern" or "center"))
        {
            var y = top + stack.Height + m * 0.4;
            var area = (X: m, Y: y, W: w - m * 2, H: h - y - m * 0.6);
            if (area.H > h * 0.18)
            {
                if (pics.Count >= 2 && pics.Any(x => x.Cutout))
                    Showcase(o, pics, p, area, podium: true);
                else
                    Spotlight(o, pics[0], p, area, minimal: layout == "minimal");
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
        if (ValidImage(spec.Image) is { } image)
            o.Add(Image(image, m, m, w - m * 2, h - m * 2, shadow: true));
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
    /// <paramref name="onPhoto"/> = روی عکس فضاسازی (دور ۲۷: پرده به رنگ پالت است، پس متن همان رنگ پالت را می‌گیرد؛ متن روشن سایهٔ نرم می‌گیرد).
    /// </summary>
    private static Stack TextStack(DesignSpec spec, DesignPalette p, double left, double width, int h, int w, string align, bool wide, double scale = 1, bool onPhoto = false)
    {
        var unit = (wide ? h : Math.Min(w, h * 0.75)) * scale;
        var items = new List<JsonObject>();
        var y = 0.0;
        var gap = unit * 0.04;
        var ink = p.Ink;
        var subInk = p.SubInk;
        var lightInk = DesignColors.TryParse(ink, out var inkRgb) && DesignColors.Luminance(inkRgb) > 0.5;

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
        if (onPhoto && lightInk)
            head["shadow"] = Shadow("rgba(0,0,0,0.35)", Math.Round(headFs * 0.2), Math.Round(headFs * 0.04));
        // دور ۲۷: تیتر با «پرده‌برداری» از راست به چپ (جهت خواندن فارسی) ظاهر می‌شود.
        Anim(head, "reveal", 0.2);
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
    /// pattern ← نوارهای مورب، مربع‌های گردگوشهٔ چرخان و شبکهٔ نقطه؛ festive ← کاغذرنگی و ریسه؛ بقیه ← دو دایرهٔ محو.
    /// visualShare = سهم ناحیهٔ تصویر از عرض.
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
        var c1 = Circle("deco", -s * 0.25, -s * 0.35, s * 0.6, p.Ink, 0.06);
        o.Add(Depth(Anim(c1, "drift", 0), 1));
        var c2 = Circle("deco", w - s * 0.55, h - s * 0.45, s * 0.45, p.Ink, 0.05);
        o.Add(Depth(Anim(c2, "drift", 0.4), 1));
        if (layout == "pattern")
        {
            // نوارهای مورب پهن در نیمهٔ چپ + مربع‌های گردگوشهٔ چرخان + شبکهٔ نقطه‌ها: پس‌زمینهٔ زنده بدون نیاز به عکس.
            var span = visualShare > 0 ? w * visualShare : w * 0.4;
            for (var i = 0; i < 6; i++)
            {
                var stripe = Rect("deco", span * (i * 0.2) - h * 0.2, -h * 0.3, h * 0.12, h * 1.8, i % 2 == 0 ? p.Accent : p.Ink);
                stripe["angle"] = -24;
                stripe["opacity"] = i % 2 == 0 ? 0.16 : 0.07;
                o.Add(Depth(Anim(stripe, "drift", 0.1 * i), 1));
            }
            double[][] squares = [[0.05, 0.18, 0.62, 0.16], [0.19, 0.55, 0.42, 0.1], [0.9, -0.2, 0.5, 0.08]];
            var index = 0;
            foreach (var q in squares)
            {
                var size = h * q[2];
                var square = Rect("deco", w * q[0], h * q[1], size, size, index % 2 == 0 ? p.Accent : p.Ink);
                square["rx"] = Math.Round(size * 0.22);
                square["ry"] = Math.Round(size * 0.22);
                square["opacity"] = q[3];
                square["angle"] = 18 + index * 14;
                o.Add(Depth(Anim(square, "spin", index * 0.5), index == 1 ? 2 : 1));
                index++;
            }
            for (var row = 0; row < 3; row++)
                for (var col = 0; col < 5; col++)
                    o.Add(Circle("deco", w * 0.86 - col * h * 0.08, h * 0.12 + row * h * 0.08, h * 0.012, p.Ink, 0.25));
        }
        if (layout == "festive")
        {
            // کاغذرنگی (دایره و مستطیل چرخیده) با رنگ تأکیدی و متن + ریسه‌های باریک مورب.
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
                o.Add(Depth(Anim(piece, index % 3 == 0 ? "twinkle" : "float", 0.1 * index), index % 2 == 0 ? 2 : 1));
                index++;
            }
            double[][] streamers = [[0.3, -0.1, 28], [0.7, -0.15, -22], [0.55, 0.75, 35]];
            foreach (var st in streamers)
            {
                var streamer = Rect("deco", w * st[0], h * st[1], h * 0.018, h * 0.5, p.Accent);
                streamer["rx"] = Math.Round(h * 0.009);
                streamer["angle"] = st[2];
                streamer["opacity"] = 0.55;
                o.Add(Depth(Anim(streamer, "drift", 0.3), 1));
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

    /// <summary>دور ۲۷ – شیء عکس با مقیاس مشخص (گوشهٔ بالا-چپ در x,y).</summary>
    private static JsonObject ImageObject(DesignImage image, double x, double y, double scale)
    {
        var o = Base("Image", "image", x, y);
        o["src"] = image.Url;
        o["width"] = Math.Max(1, image.Width);
        o["height"] = Math.Max(1, image.Height);
        o["scaleX"] = Math.Round(scale, 5);
        o["scaleY"] = Math.Round(scale, 5);
        o["strokeWidth"] = 0;
        if (image.Cutout)
            o["fsCutout"] = true;
        return o;
    }

    /// <summary>
    /// دور ۲۷ – هالهٔ نور: دایره با گرادیان شعاعی از رنگ (با شفافیت alpha) تا کاملاً شفاف. <paramref name="scaleY"/> کوچک ← بیضی پهن
    /// (سایهٔ تماس زیر کالا). مثال: Glow("halo", 400, 200, 300, "#facc15", 0.5) ← درخشش زرد نرم دور نقطهٔ (۴۰۰، ۲۰۰).
    /// </summary>
    private static JsonObject Glow(string role, double cx, double cy, double radius, string color, double alpha, double scaleY = 1)
    {
        radius = Math.Max(1, radius);
        var g = Circle(role, cx - radius, cy - radius * scaleY, radius, color, 1);
        var r = R(radius);
        g["fill"] = new JsonObject
        {
            ["type"] = "radial",
            ["gradientUnits"] = "pixels",
            ["coords"] = new JsonObject { ["x1"] = r, ["y1"] = r, ["r1"] = 0, ["x2"] = r, ["y2"] = r, ["r2"] = r },
            ["colorStops"] = new JsonArray
            {
                new JsonObject { ["offset"] = 0, ["color"] = Rgba(color, alpha) },
                new JsonObject { ["offset"] = 0.55, ["color"] = Rgba(color, alpha * 0.4) },
                new JsonObject { ["offset"] = 1, ["color"] = Rgba(color, 0) },
            },
            ["offsetX"] = 0,
            ["offsetY"] = 0,
        };
        if (Math.Abs(scaleY - 1) > 0.0001)
            g["scaleY"] = Math.Round(scaleY, 4);
        return g;
    }

    /// <summary>دور ۲۷ – گرادیان خطی دو رنگ برای شیء w×h: مورب (بالا-راست ← پایین-چپ) یا عمودی (بالا ← پایین).</summary>
    private static JsonObject Linear(double w, double h, string from, string to, bool vertical = false) => new()
    {
        ["type"] = "linear",
        ["gradientUnits"] = "pixels",
        ["coords"] = vertical
            ? new JsonObject { ["x1"] = 0, ["y1"] = 0, ["x2"] = 0, ["y2"] = R(h) }
            : new JsonObject { ["x1"] = R(w), ["y1"] = 0, ["x2"] = 0, ["y2"] = R(h) },
        ["colorStops"] = new JsonArray
        {
            new JsonObject { ["offset"] = 0, ["color"] = from },
            new JsonObject { ["offset"] = 1, ["color"] = to },
        },
        ["offsetX"] = 0,
        ["offsetY"] = 0,
    };

    /// <summary>
    /// دور ۲۷ – چرخاندن شیء دور یک مرکز مشترک: (dx, dy) = فاصلهٔ گوشهٔ بالا-چپ شیء از مرکز پیش از چرخش.
    /// فابریک، رندر سرور و نسخهٔ زنده همه دور گوشهٔ بالا-چپ می‌چرخانند؛ پس گوشه را خودمان جابه‌جا می‌کنیم تا کاشی و عکسش روی هم بمانند.
    /// </summary>
    private static void Place(JsonObject item, (double X, double Y) center, double dx, double dy, double angle)
    {
        var rad = angle * Math.PI / 180;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        item["left"] = R(center.X + dx * cos - dy * sin);
        item["top"] = R(center.Y + dx * sin + dy * cos);
        if (Math.Abs(angle) > 0.001)
            item["angle"] = Math.Round(angle, 2);
    }

    /// <summary>رنگ hex ← «rgba(r,g,b,a)» با فرهنگ ثابت. مثال: Rgba("#ff0000", 0.5) ← «rgba(255,0,0,0.5)».</summary>
    public static string Rgba(string hex, double alpha)
    {
        if (!DesignColors.TryParse(hex, out var c))
            c = (0, 0, 0);
        return string.Create(CultureInfo.InvariantCulture, $"rgba({c.R},{c.G},{c.B},{Math.Round(Math.Clamp(alpha, 0, 1), 3)})");
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
