using System.Globalization;

namespace Fratak_Shop.Core.Services.Design;

/// <summary>
/// ابزار رنگ طرح‌ها (دور ۲۳): نسبت کنتراست WCAG، انتخاب رنگ متن خوانا و ساخت پالت از رنگ غالب عکس کالا.
/// <para>چرا؟ «رنگ از عکس کالا» زیباست ولی اگر متن سفید روی زرد روشن بنشیند خوانده نمی‌شود؛ این کلاس رنگ را آن‌قدر تیره/روشن می‌کند
/// که کنتراست متن با هر دو سر گرادیان دست‌کم ۴٫۵ (استاندارد AA متن معمولی) شود.</para>
/// <para>مثال: FromColor("#facc15") (زرد) ← متن تیره (#0f172a) روی زرد؛ FromColor("#60a5fa") (آبی روشن) ← آبی تیره‌تر با متن سفید.</para>
/// <para>محل استفاده: DesignPalettes.Find (کلید «#rrggbb»)، DesignBrief (نسخهٔ «رنگ عکس کالا») و آزمون کنتراست همهٔ پالت‌ها.</para>
/// </summary>
public static class DesignColors
{
    /// <summary>رنگ متن تیرهٔ استاندارد طرح‌ها.</summary>
    public const string DarkInk = "#0f172a";

    /// <summary>رنگ متن روشن.</summary>
    public const string LightInk = "#ffffff";

    /// <summary>کمترین کنتراست قابل قبول متن روی پس‌زمینه (WCAG AA).</summary>
    public const double MinContrast = 4.5;

    /// <summary>خواندن «#rgb» یا «#rrggbb». مثال: «#fff» ← (255,255,255)؛ «red» ← false.</summary>
    public static bool TryParse(string? hex, out (int R, int G, int B) rgb)
    {
        rgb = default;
        if (string.IsNullOrWhiteSpace(hex))
            return false;
        var value = hex.Trim();
        if (!value.StartsWith('#'))
            return false;
        value = value[1..];
        if (value.Length == 3)
            value = string.Concat(value.Select(c => new string(c, 2)));
        if (value.Length != 6 || !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number))
            return false;
        rgb = ((number >> 16) & 0xFF, (number >> 8) & 0xFF, number & 0xFF);
        return true;
    }

    /// <summary>رنگ به «#rrggbb» (حروف کوچک).</summary>
    public static string ToHex((int R, int G, int B) rgb) =>
        $"#{Math.Clamp(rgb.R, 0, 255):x2}{Math.Clamp(rgb.G, 0, 255):x2}{Math.Clamp(rgb.B, 0, 255):x2}";

    /// <summary>روشنایی نسبی WCAG (۰ = سیاه، ۱ = سفید).</summary>
    public static double Luminance((int R, int G, int B) rgb)
    {
        static double Channel(int c)
        {
            var v = c / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(rgb.R) + 0.7152 * Channel(rgb.G) + 0.0722 * Channel(rgb.B);
    }

    /// <summary>
    /// نسبت کنتراست WCAG بین دو رنگ (۱ تا ۲۱). رنگ نامعتبر ← ۱. مثال: سفید/سیاه ← ۲۱؛ سفید/سفید ← ۱.
    /// </summary>
    public static double Contrast(string? a, string? b)
    {
        if (!TryParse(a, out var x) || !TryParse(b, out var y))
            return 1;
        var l1 = Luminance(x);
        var l2 = Luminance(y);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    /// <summary>متن خواناتر روی این پس‌زمینه: سفید یا سرمه‌ای تیره. مثال: زرد ← تیره؛ سرمه‌ای ← سفید.</summary>
    public static string BestInk(string background) =>
        Contrast(LightInk, background) >= Contrast(DarkInk, background) ? LightInk : DarkInk;

    /// <summary>ترکیب دو رنگ (t=0 ← a، t=1 ← b). مثال: Mix("#000000", "#ffffff", 0.5) ← «#808080».</summary>
    public static string Mix(string a, string b, double t)
    {
        if (!TryParse(a, out var x) || !TryParse(b, out var y))
            return a;
        t = Math.Clamp(t, 0, 1);
        return ToHex(((int)Math.Round(x.R + (y.R - x.R) * t), (int)Math.Round(x.G + (y.G - x.G) * t), (int)Math.Round(x.B + (y.B - x.B) * t)));
    }

    /// <summary>
    /// کمترین کنتراست متن اصلی پالت با دو سر گرادیان (معیار «خوانایی» پالت). مثال: پالت ocean ← حدود ۳٫۰.
    /// </summary>
    public static double MinTextContrast(DesignPalette palette) =>
        Math.Min(Contrast(palette.Ink, palette.From), Contrast(palette.Ink, palette.To));

    /// <summary>
    /// پالت خوانا از یک رنگ (معمولاً رنگ غالب عکس کالا). رنگ نامعتبر ← پالت پیش‌فرض.
    /// <list type="number">
    /// <item>رنگ متن (سفید/تیره) با بیشترین کنتراست انتخاب می‌شود.</item>
    /// <item>رنگ پایه آن‌قدر تیره (برای متن سفید) یا روشن (برای متن تیره) می‌شود که کنتراست ≥ ۴٫۵ شود.</item>
    /// <item>سر دوم گرادیان از همان سمت (تیره‌تر/روشن‌تر) ساخته می‌شود تا کنتراست آن هم کمتر نشود.</item>
    /// </list>
    /// </summary>
    public static DesignPalette FromColor(string? hex)
    {
        if (!TryParse(hex, out var rgb))
            return DesignPalettes.All[0];
        var baseHex = ToHex(rgb);
        var ink = BestInk(baseHex);
        var towards = ink == LightInk ? "#000000" : "#ffffff";
        var to = baseHex;
        for (var step = 1; step <= 20 && Contrast(ink, to) < MinContrast; step++)
            to = Mix(baseHex, towards, step * 0.05);
        var from = Mix(to, towards, ink == LightInk ? 0.25 : 0.35);
        // گرادیان: روشن‌تر/تیره‌تر بسته به جهت؛ هر دو سر ≥ ۴٫۵ با متن.
        var (start, end) = ink == LightInk ? (to, from) : (from, to);
        var subInk = Mix(ink, end, 0.18);
        var accent = ink == LightInk ? "#facc15" : DarkInk;
        var accentInk = BestInk(accent);
        return new DesignPalette(baseHex, "رنگ عکس کالا", start, end, ink, subInk, accent, accentInk);
    }
}
