using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Fratak_Shop.Data.Entities.Design;

namespace Fratak_Shop.Core.Services.Design;

/// <summary>
/// دور ۲۶ – «بنر زنده» (به‌جای GIF): همان طرح فابریک به HTML/CSS سبک تبدیل می‌شود تا عنصرها (تیتر، عکس، دکمه، نشان…) نرم وارد/خارج شوند.
/// <list type="bullet">
/// <item><b>چرا HTML و نه GIF؟</b> GIF حجیم (چند مگابایت)، ۲۵۶ رنگ و تار است؛ این نسخه چند کیلوبایت است، متن واقعی دارد (سئو و خوانایی)
/// و روی هر اندازهٔ صفحه تیز می‌ماند.</item>
/// <item><b>اندازه:</b> قاب با <c>aspect-ratio</c> و <c>container-type:inline-size</c>؛ موقعیت‌ها درصدی و اندازهٔ قلم با واحد <c>cqw</c>
/// (درصد عرض قاب) ← دقیقاً همان چیدمان طرح در هر عرضی.</item>
/// <item><b>امنیت:</b> متن‌ها رمزگذاری HTML می‌شوند؛ رنگ فقط hex/rgb(a)؛ عکس فقط از <c>/uploads/…</c> با پسوند تصویر؛ عدد‌ها با فرهنگ ثابت.
/// هیچ ورودی آزاد کاربر مستقیم در HTML نمی‌رود. شیء ناشناخته (گروه، مسیر، …) ← null ← سایت همان تصویر WebP را نشان می‌دهد.</item>
/// <item><b>حرکت:</b> برچسب <c>fsAnim</c> هر شیء (rise, slide-r, slide-l, pop, float, pulse, shine, drift, zoom) + تأخیر <c>fsAnimDelay</c>.
/// «نرم» = یک‌بار ورود؛ «چرخشی» = ورود، مکث، خروج و تکرار هر ۹ ثانیه. کاربرانی که «کاهش حرکت» را روشن کرده‌اند حرکتی نمی‌بینند (CSS).</item>
/// </list>
/// <para>محل استفاده: DesignService (انتشار/ذخیرهٔ بنر ← Banner.LiveHtml) و partial «_BannerArt» در صفحهٔ نخست و صفحهٔ دسته.</para>
/// </summary>
public static class DesignLive
{
    /// <summary>بیشترین تعداد شیء طرح برای نسخهٔ زنده (بیشتر ← همان تصویر ثابت).</summary>
    public const int MaxObjects = 80;

    /// <summary>بیشترین طول JSON ورودی.</summary>
    public const int MaxJsonLength = 1_000_000;

    /// <summary>نوع‌های حرکت مجاز (هر چیز دیگر نادیده گرفته می‌شود).</summary>
    public static readonly IReadOnlySet<string> Animations = new HashSet<string>(StringComparer.Ordinal)
    {
        "rise", "slide-r", "slide-l", "pop", "float", "pulse", "shine", "drift", "zoom",
    };

    private static readonly Regex HexColor = new("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    private static readonly Regex RgbColor = new("^rgba?\\(\\s*\\d{1,3}\\s*,\\s*\\d{1,3}\\s*,\\s*\\d{1,3}\\s*(?:,\\s*(?:0|1|0?\\.\\d{1,4}|1\\.0+)\\s*)?\\)\\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    private static readonly Regex LocalImage = new("^/uploads/[a-z0-9][a-z0-9\\-_/]{0,200}\\.(?:webp|png|jpg|jpeg)\\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    /// <summary>
    /// ساخت HTML زندهٔ بنر. motion = None، JSON خراب/بزرگ، اندازهٔ نامعتبر یا شیء پشتیبانی‌نشده ← null.
    /// مثال خروجی (کوتاه‌شده): <c>&lt;div class="fsb fsb-soft" style="aspect-ratio:1920/420"&gt;&lt;div class="fsb-o" style="left:55%;top:20%;width:40%"&gt;…</c>
    /// </summary>
    public static string? Html(string? json, BannerMotion motion)
    {
        if (motion == BannerMotion.None || string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonLength)
            return null;
        JsonObject root;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject parsed)
                return null;
            root = parsed;
        }
        catch (JsonException)
        {
            return null;
        }

        var width = Number(root["fsWidth"]) ?? Number(root["width"]) ?? 0;
        var height = Number(root["fsHeight"]) ?? Number(root["height"]) ?? 0;
        if (width is < 100 or > 4000 || height is < 50 or > 4000)
            return null;
        if (root["objects"] is not JsonArray objects || objects.Count == 0 || objects.Count > MaxObjects)
            return null;

        var html = new StringBuilder(4096);
        html.Append("<div class=\"fsb ").Append(motion == BannerMotion.Loop ? "fsb-loop" : "fsb-soft").Append("\" style=\"aspect-ratio:")
            .Append(N(width)).Append('/').Append(N(height));
        if (Color(Text(root["background"])) is { } background)
            html.Append(";background:").Append(background);
        html.Append("\">");
        foreach (var node in objects)
        {
            if (node is not JsonObject item)
                return null;
            if (item["visible"] is JsonValue visible && visible.TryGetValue<bool>(out var shown) && !shown)
                continue;
            var part = Object(item, width, height);
            if (part is null)
                return null;
            html.Append(part);
        }
        html.Append("</div>");
        return html.ToString();
    }

    /// <summary>یک شیء ← HTML (یا null اگر پشتیبانی نشود).</summary>
    private static string? Object(JsonObject o, double canvasW, double canvasH)
    {
        var type = (Text(o["type"]) ?? "").ToLowerInvariant();
        var isText = type is "textbox" or "i-text" or "itext" or "text";
        var isCircle = type == "circle";
        if (!isText && !isCircle && type is not ("rect" or "image"))
            return null;

        var scaleX = Number(o["scaleX"]) ?? 1;
        var scaleY = Number(o["scaleY"]) ?? 1;
        if (scaleX <= 0 || scaleY <= 0 || scaleX > 100 || scaleY > 100)
            return null;
        var w = (isCircle ? (Number(o["radius"]) ?? 0) * 2 : Number(o["width"]) ?? 0) * scaleX;
        var h = (isCircle ? (Number(o["radius"]) ?? 0) * 2 : Number(o["height"]) ?? 0) * scaleY;
        if (w <= 0 || (!isText && h <= 0))
            return null;
        var left = Number(o["left"]) ?? 0;
        var top = Number(o["top"]) ?? 0;
        // نقطهٔ مبدأ فابریک ۷ پیش‌فرض «center» است؛ قالب‌های ما «left/top» می‌دهند ولی شیء ساخت ویرایشگر شاید وسط باشد.
        var originX = Text(o["originX"]) ?? "left";
        var originY = Text(o["originY"]) ?? "top";
        var textHeight = isText ? EstimateTextHeight(o) : h;
        left -= originX switch { "center" => w / 2, "right" => w, _ => 0 };
        top -= originY switch { "center" => textHeight / 2, "bottom" => textHeight, _ => 0 };

        var outer = new StringBuilder();
        outer.Append("left:").Append(P(left / canvasW)).Append(";top:").Append(P(top / canvasH)).Append(";width:").Append(P(w / canvasW));
        if (!isText)
            outer.Append(";height:").Append(P(h / canvasH));
        if (Number(o["opacity"]) is { } opacity && opacity < 1)
            outer.Append(";opacity:").Append(N(Math.Clamp(opacity, 0, 1)));
        if (Number(o["angle"]) is { } angle && Math.Abs(angle) > 0.01)
        {
            var ox = originX switch { "center" => "50%", "right" => "100%", _ => "0" };
            var oy = originY switch { "center" => "50%", "bottom" => "100%", _ => "0" };
            outer.Append(";rotate:").Append(N(angle % 360)).Append("deg;transform-origin:").Append(ox).Append(' ').Append(oy);
        }

        var anim = Text(o["fsAnim"]);
        var animClass = anim is not null && Animations.Contains(anim) ? " fsb-a-" + anim : "";
        if (animClass.Length > 0 && Number(o["fsAnimDelay"]) is { } delay && delay > 0)
            outer.Append(";--d:").Append(N(Math.Clamp(delay, 0, 4))).Append('s');

        var inner = new StringBuilder();
        string content;
        if (isText)
        {
            // متن کشیده‌شده در ویرایشگر scale می‌گیرد؛ قلم هم به همان نسبت بزرگ دیده می‌شود.
            var fontSize = (Number(o["fontSize"]) ?? 0) * scaleY;
            if (fontSize is <= 0 or > 1000)
                return null;
            var weight = Number(o["fontWeight"]) is { } wnum ? (int)Math.Clamp(wnum, 100, 900) : (Text(o["fontWeight"]) == "bold" ? 700 : 400);
            inner.Append("font-size:").Append(N(fontSize / canvasW * 100)).Append("cqw;font-weight:").Append(weight)
                .Append(";line-height:").Append(N(Math.Clamp(Number(o["lineHeight"]) ?? 1.16, 0.5, 3) * 1.13));
            if (Color(Text(o["fill"])) is { } ink)
                inner.Append(";color:").Append(ink);
            var align = Text(o["textAlign"]) switch { "center" => "center", "left" => "left", "justify" => "justify", _ => "right" };
            inner.Append(";text-align:").Append(align);
            if (o["linethrough"] is JsonValue lt && lt.TryGetValue<bool>(out var through) && through)
                inner.Append(";text-decoration:line-through");
            else if (o["underline"] is JsonValue ul && ul.TryGetValue<bool>(out var under) && under)
                inner.Append(";text-decoration:underline");
            if (Shadow(o["shadow"], canvasW) is { } textShadow)
                inner.Append(";text-shadow:").Append(textShadow);
            content = WebUtility.HtmlEncode(Text(o["text"]) ?? "");
        }
        else if (type == "image")
        {
            var src = Text(o["src"]);
            if (src is null || src.Contains("..", StringComparison.Ordinal) || !LocalImage.IsMatch(src))
                return null;
            var filter = Shadow(o["shadow"], canvasW) is { } dropShadow ? " style=\"filter:drop-shadow(" + dropShadow + ")\"" : "";
            content = "<img src=\"" + WebUtility.HtmlEncode(src) + "\" alt=\"\" loading=\"lazy\" decoding=\"async\" draggable=\"false\"" + filter + ">";
        }
        else
        {
            var fill = Fill(o["fill"], left, top, w, h);
            if (fill is not null)
                inner.Append("background:").Append(fill).Append(';');
            if (isCircle)
                inner.Append("border-radius:50%;");
            else if (Number(o["rx"]) is { } rx && rx > 0)
                inner.Append("border-radius:").Append(N(rx * scaleX / canvasW * 100)).Append("cqw;");
            if (Number(o["strokeWidth"]) is { } sw && sw > 0 && Color(Text(o["stroke"])) is { } stroke)
                inner.Append("border:").Append(N(sw / canvasW * 100)).Append("cqw solid ").Append(stroke).Append(';');
            if (Shadow(o["shadow"], canvasW) is { } boxShadow)
                inner.Append("box-shadow:").Append(boxShadow).Append(';');
            content = "";
        }

        var role = isText ? " fsb-t" : type == "image" ? " fsb-img" : "";
        return "<div class=\"fsb-o" + role + "\" style=\"" + outer + "\"><div class=\"fsb-i" + animClass + "\"" +
               (inner.Length > 0 ? " style=\"" + inner.ToString().TrimEnd(';') + "\"" : "") + ">" + content + "</div></div>";
    }

    /// <summary>ارتفاع تقریبی متن (برای مبدأ «center» شیء ساخت ویرایشگر). تعداد خط از «\n» + طول تقریبی.</summary>
    private static double EstimateTextHeight(JsonObject o)
    {
        var fontSize = Number(o["fontSize"]) ?? 40;
        var width = Math.Max(1, Number(o["width"]) ?? 200);
        var text = Text(o["text"]) ?? "";
        var lines = text.Split('\n').Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length * fontSize * 0.5 / width)));
        return lines * fontSize * (Number(o["lineHeight"]) ?? 1.16) * 1.13 * (Number(o["scaleY"]) ?? 1);
    }

    /// <summary>
    /// رنگ یا گرادیان خطی فابریک ← CSS. گرادیان: جهت و جای توقف‌ها دقیقاً از مختصات فابریک (نسبت به گوشهٔ شیء) حساب می‌شود.
    /// مثال: از (w,0) تا (0,h) ← «linear-gradient(…deg, #a 0%, #b 100%)».
    /// </summary>
    private static string? Fill(JsonNode? node, double left, double top, double w, double h)
    {
        if (node is JsonValue)
            return Color(Text(node));
        if (node is not JsonObject gradient || Text(gradient["type"]) != "linear" || gradient["colorStops"] is not JsonArray stops || stops.Count is < 2 or > 10)
            return null;
        if (gradient["coords"] is not JsonObject coords)
            return null;
        var x1 = Number(coords["x1"]) ?? 0;
        var y1 = Number(coords["y1"]) ?? 0;
        var x2 = Number(coords["x2"]) ?? 0;
        var y2 = Number(coords["y2"]) ?? 0;
        if (Text(gradient["gradientUnits"]) == "percentage")
        {
            x1 *= w; x2 *= w; y1 *= h; y2 *= h;
        }
        var dx = x2 - x1;
        var dy = y2 - y1;
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
            return null;
        // زاویهٔ CSS: ۰ = رو به بالا، ۹۰ = رو به راست (محور y رو به پایین).
        var theta = Math.Atan2(dx, -dy);
        var ux = Math.Sin(theta);
        var uy = -Math.Cos(theta);
        var length = Math.Abs(w * ux) + Math.Abs(h * uy);
        double T(double x, double y) => ((x - w / 2) * ux + (y - h / 2) * uy) / length + 0.5;
        var t1 = T(x1, y1);
        var t2 = T(x2, y2);
        var css = new StringBuilder("linear-gradient(");
        css.Append(N((theta * 180 / Math.PI + 360) % 360)).Append("deg");
        foreach (var stop in stops)
        {
            if (stop is not JsonObject s || Color(Text(s["color"])) is not { } color)
                return null;
            var offset = Math.Clamp(Number(s["offset"]) ?? 0, 0, 1);
            css.Append(',').Append(color).Append(' ').Append(P(t1 + offset * (t2 - t1)));
        }
        return css.Append(')').ToString();
    }

    /// <summary>سایهٔ فابریک ← «x y blur color» با واحد cqw. مثال: {blur:20, offsetY:8, color:"rgba(0,0,0,0.2)"} ← «0cqw 0.417cqw 1.042cqw rgba(0,0,0,0.2)».</summary>
    private static string? Shadow(JsonNode? node, double canvasW)
    {
        if (node is not JsonObject shadow || Color(Text(shadow["color"])) is not { } color)
            return null;
        var blur = Math.Clamp(Number(shadow["blur"]) ?? 0, 0, 400);
        var ox = Math.Clamp(Number(shadow["offsetX"]) ?? 0, -400, 400);
        var oy = Math.Clamp(Number(shadow["offsetY"]) ?? 0, -400, 400);
        return $"{N(ox / canvasW * 100)}cqw {N(oy / canvasW * 100)}cqw {N(blur / canvasW * 100)}cqw {color}";
    }

    /// <summary>رنگ امن (hex یا rgb/rgba یا transparent)؛ هر چیز دیگر (مثل «red;background:url(…)») ← null.</summary>
    public static string? Color(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            return "transparent";
        return HexColor.IsMatch(value) || RgbColor.IsMatch(value) ? value.ToLowerInvariant() : null;
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static double? Number(JsonNode? node)
    {
        if (node is not JsonValue value)
            return null;
        if (value.TryGetValue<double>(out var d))
            return double.IsFinite(d) ? d : null;
        if (value.TryGetValue<int>(out var i))
            return i;
        if (value.TryGetValue<long>(out var l))
            return l;
        if (value.TryGetValue<decimal>(out var m))
            return (double)m;
        return value.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed) ? parsed : null;
    }

    private static string N(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);

    private static string P(double ratio) => N(ratio * 100) + "%";
}
