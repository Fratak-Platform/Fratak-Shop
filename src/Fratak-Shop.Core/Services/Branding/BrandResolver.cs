using Fratak_Shop.Core.DTOs.Settings;

namespace Fratak_Shop.Core.Services.Branding;

/// <summary>
/// پیش‌فرض‌های هویت بصری که از لایهٔ وب (BrandIdentity) داده می‌شوند؛ Core به wwwroot دسترسی ندارد.
/// مثال: Mark = "/assets/brand/mark.svg"، Text1 = «فراتک»، Text2 = «شاپ»، Navy = "#0B1927"، Blue = "#0069FD".
/// </summary>
public sealed record BrandDefaults(string Mark, string MarkOnDark, string MarkPng, string Text1, string Text2, string Navy, string Blue);

/// <summary>
/// لوگوی آمادهٔ نمایش برای یک محل؛ همهٔ مقدارها نهایی، در محدوده و امن‌اند (رنگ‌ها hex، آدرس‌ها / یا http(s)).
/// partial «_BrandLockup»، قالب ایمیل و صفحهٔ تعمیرات فقط همین را می‌خوانند و هیچ منطقی تکرار نمی‌شود.
/// </summary>
public sealed record BrandView
{
    public required BrandPlace Place { get; init; }

    /// <summary>لوگو در این محل نمایش داده شود (فقط محل‌های CanHide می‌توانند false باشند).</summary>
    public required bool Visible { get; init; }

    /// <summary>زمینهٔ همیشه تیره (پنل، فوتر).</summary>
    public required bool OnDark { get; init; }

    /// <summary>اندازه‌های جدای موبایل معنا دارد.</summary>
    public required bool Responsive { get; init; }

    public required bool ShowIcon { get; init; }

    public required bool ShowText { get; init; }

    public required bool ShowTagline { get; init; }

    /// <summary>آیکونی که در این محل نمایش داده می‌شود (برای فاکتور/ایمیل نسخهٔ PNG، برای زمینهٔ تیره نسخهٔ تیره).</summary>
    public required string IconUrl { get; init; }

    /// <summary>آیکون جایگزین برای حالت شب سایت (فقط محل‌های روشن سایت و فقط وقتی با آیکون اصلی فرق دارد).</summary>
    public string? IconDarkUrl { get; init; }

    /// <summary>آیکون پیش‌فرض برند است (نسبت ابعادش معلوم است و عرض رزرو می‌شود تا صفحه نپرد).</summary>
    public required bool IsDefaultIcon { get; init; }

    public required int IconHeight { get; init; }
    public required int IconHeightMobile { get; init; }
    public required int FontSize { get; init; }
    public required int FontSizeMobile { get; init; }
    public required int FontWeight { get; init; }
    public required double LetterSpacing { get; init; }
    public required int IconGap { get; init; }
    public required int IconGapMobile { get; init; }
    public required int PartGap { get; init; }
    public required int PartGapMobile { get; init; }
    public required int TaglineGap { get; init; }
    public required int TaglineSize { get; init; }
    public required int TaglineSizeMobile { get; init; }
    public required int TextOffsetY { get; init; }
    public required BrandLayout Layout { get; init; }
    public required BrandAlign Align { get; init; }

    /// <summary>بخش اول متن. مثال: «فراتک».</summary>
    public required string Text1 { get; init; }

    /// <summary>بخش رنگی یا null. مثال: «شاپ».</summary>
    public string? Text2 { get; init; }

    /// <summary>شعار (فقط وقتی ShowTagline) یا null.</summary>
    public string? Tagline { get; init; }

    public required string Color1 { get; init; }
    public required string Color2 { get; init; }
    public required string TaglineColor { get; init; }

    /// <summary>رنگ بخش اول پیش‌فرض است (در حالت شب سایت سفید می‌شود؛ رنگ دلخواه مدیر دست نمی‌خورد).</summary>
    public required bool Color1IsDefault { get; init; }

    /// <summary>رنگ شعار پیش‌فرض است (در حالت شب سایت روشن‌تر می‌شود).</summary>
    public required bool TaglineColorIsDefault { get; init; }

    /// <summary>متن کامل برای alt و aria-label. مثال: «فراتک شاپ».</summary>
    public string FullText => Text2 is null ? Text1 : Text1 + " " + Text2;

    /// <summary>نسبت عرض به ارتفاع نشان پیش‌فرض برند (viewBox فایل mark.svg = ۲۴۰٫۹ × ۱۹۲٫۹).</summary>
    public const double MarkAspectRatio = 240.9 / 192.9;

    /// <summary>عرض رزرو آیکون پیش‌فرض یا null (آیکون اختصاصی؛ عرضش از خود تصویر می‌آید). مثال: ارتفاع ۴۰ → ۵۰.</summary>
    public int? IconWidth(int height) => IsDefaultIcon ? (int)Math.Round(height * MarkAspectRatio) : null;
}

/// <summary>
/// ساخت لوگوی نهایی هر محل از تنظیمات فروشگاه + JSON استودیوی لوگو + پیش‌فرض‌های برند.
/// <para>ترتیب اولویت هر مقدار: تنظیم همان محل ← تنظیم کلی برند (BrandText*, LogoUrl …) ← پیش‌فرض محل.</para>
/// <para>
/// اندازه‌های خالی «خودکار» و متناسب با ارتفاع آیکون‌اند (همان فرمول مرحلهٔ دهم):
/// قلم ≈ ۰٫۶۵ × آیکون (حداقل ۱۲)، فاصلهٔ آیکون تا متن = بیشترینِ ۶ و آیکون÷۵، فاصلهٔ دو بخش ≈ ۰٫۲۸ × قلم،
/// شعار ≈ ۰٫۴۲ × قلم (حداقل ۱۰). مثال: آیکون ۴۴ → قلم ۲۹، فاصله ۸، فاصلهٔ دو بخش ۸، شعار ۱۲.
/// </para>
/// خالص و بدون خطا: حتی اگر JSON در پایگاه داده دستی خراب شده باشد، همه چیز در محدوده نگه داشته می‌شود.
/// </summary>
public static class BrandResolver
{
    /// <summary>متن پیش‌فرض بخش رنگی که یعنی «بخش دوم نداشته باش».</summary>
    public const string NoAccent = "-";

    public static BrandView Resolve(BrandPlace place, StoreSettingDto settings, BrandStyleConfig config, BrandDefaults defaults)
    {
        var info = BrandStyleRules.Info(place);
        var s = config.For(place);

        // ── آیکون ──────────────────────────────────────────────
        var globalCustom = BrandStyleRules.Asset(settings.LogoUrl);
        var globalLight = globalCustom ?? defaults.Mark;
        var globalDark = BrandStyleRules.Asset(settings.LogoDarkUrl) ?? (globalCustom ?? defaults.MarkOnDark);
        var placeLight = BrandStyleRules.Asset(s.IconUrl);
        var placeDark = BrandStyleRules.Asset(s.IconDarkUrl);
        var light = placeLight ?? globalLight;
        var dark = placeDark ?? placeLight ?? globalDark;
        var isDefault = placeLight is null && globalCustom is null;

        string icon;
        string? darkAlternate = null;
        if (info.Raster)
        {
            // PDF فاکتور (html2canvas) و برنامه‌های ایمیل با SVG مشکل دارند: نسخهٔ PNG برند، یا تصویر مدیر اگر خودش SVG نیست.
            // مثال: لوگوی سراسری «/uploads/logo.svg» → فاکتور و ایمیل همان mark-512.png برند را می‌گیرند.
            var custom = new[] { placeLight, globalCustom }.FirstOrDefault(url => url is not null && !IsSvg(url));
            icon = custom ?? defaults.MarkPng;
            isDefault = custom is null;
        }
        else if (info.OnDark)
        {
            icon = dark;
        }
        else
        {
            icon = light;
            darkAlternate = string.Equals(dark, light, StringComparison.Ordinal) ? null : dark;
        }

        // ── نمایش ─────────────────────────────────────────────
        var legacyText = place switch
        {
            BrandPlace.Header or BrandPlace.MobileMenu => settings.BrandTextInHeader,
            BrandPlace.Auth => settings.BrandTextInAuth,
            BrandPlace.Admin => settings.BrandTextInAdmin,
            BrandPlace.Invoice => settings.BrandTextInInvoice,
            _ => info.ShowTextDefault,
        };
        var showIcon = s.ShowIcon ?? true;
        var showText = s.ShowText ?? legacyText;
        if (!showIcon && !showText)
            showIcon = true;

        // ── متن ────────────────────────────────────────────────
        var text1 = Clip(s.Text1, BrandStyleRules.MaxTextLength) ?? Clip(settings.BrandTextPrimary, BrandStyleRules.MaxTextLength) ?? defaults.Text1;
        var accentRaw = Clip(s.Text2, BrandStyleRules.MaxTextLength) ?? Clip(settings.BrandTextAccent, BrandStyleRules.MaxTextLength);
        var text2 = accentRaw is null ? defaults.Text2 : accentRaw == NoAccent ? null : accentRaw;
        var tagline = Clip(s.Tagline, BrandStyleRules.MaxTaglineLength) ?? Clip(config.Tagline, BrandStyleRules.MaxTaglineLength)
                      ?? Clip(settings.Tagline, BrandStyleRules.MaxTaglineLength);
        var showTagline = showText && tagline is not null && (s.ShowTagline ?? info.ShowTaglineDefault);

        // ── اندازه‌ها ──────────────────────────────────────────
        var ih = Clamp(s.IconHeight ?? info.IconHeight, BrandStyleRules.MinIcon, BrandStyleRules.MaxIcon);
        var ihm = info.Responsive ? Clamp(s.IconHeightMobile ?? ih, BrandStyleRules.MinIcon, BrandStyleRules.MaxIcon) : ih;
        var fs = Clamp(s.FontSize ?? Math.Max(12, Round(ih * 0.65)), BrandStyleRules.MinFont, BrandStyleRules.MaxFont);
        var fsm = info.Responsive
            ? Clamp(s.FontSizeMobile ?? Round(fs * (double)ihm / ih), BrandStyleRules.MinFont, BrandStyleRules.MaxFont)
            : fs;
        var weight = s.FontWeight is { } w && w is >= 100 and <= 900 ? w / 100 * 100 : 800;
        var spacing = s.LetterSpacing is { } ls && !double.IsNaN(ls) && !double.IsInfinity(ls)
            ? Math.Round(Math.Clamp(ls, -BrandStyleRules.MaxLetterSpacing, BrandStyleRules.MaxLetterSpacing), 1)
            : 0;
        var gap = Clamp(s.IconGap ?? Math.Max(6, ih / 5), 0, BrandStyleRules.MaxGap);
        var gapM = Clamp(s.IconGap ?? Math.Max(6, ihm / 5), 0, BrandStyleRules.MaxGap);
        var partGap = Clamp(s.PartGap ?? Round(fs * 0.28), 0, BrandStyleRules.MaxGap);
        var partGapM = Clamp(s.PartGap ?? Round(fsm * 0.28), 0, BrandStyleRules.MaxGap);
        var taglineSize = Clamp(s.TaglineSize ?? Math.Max(10, Round(fs * 0.42)), BrandStyleRules.MinTagline, BrandStyleRules.MaxTagline);
        var taglineSizeM = Clamp(s.TaglineSize ?? Math.Max(10, Round(fsm * 0.42)), BrandStyleRules.MinTagline, BrandStyleRules.MaxTagline);
        var taglineGap = Clamp(s.TaglineGap ?? Math.Max(2, Round(fs * 0.15)), 0, BrandStyleRules.MaxGap);
        var offset = Clamp(s.TextOffsetY ?? 0, -BrandStyleRules.MaxOffset, BrandStyleRules.MaxOffset);
        var layout = s.Layout is { } l && Enum.IsDefined(l) ? l : info.LayoutDefault;
        var align = s.Align is { } a && Enum.IsDefined(a) ? a : BrandAlign.Center;

        // ── رنگ‌ها ─────────────────────────────────────────────
        var placeColor1 = BrandStyleRules.Hex(s.Color1);
        var color1 = placeColor1 ?? (info.OnDark ? "#FFFFFF" : BrandStyleRules.Hex(settings.BrandTextColor) ?? defaults.Navy);
        var color2 = BrandStyleRules.Hex(s.Color2) ?? BrandStyleRules.Hex(settings.BrandAccentColor) ?? defaults.Blue;
        var placeTaglineColor = BrandStyleRules.Hex(s.TaglineColor);
        var taglineColor = placeTaglineColor ?? (info.OnDark ? "#94A3B8" : BrandStyleRules.Hex(config.TaglineColor) ?? "#64748B");

        return new BrandView
        {
            Place = place,
            Visible = !info.CanHide || (s.Visible ?? true),
            OnDark = info.OnDark,
            Responsive = info.Responsive,
            ShowIcon = showIcon,
            ShowText = showText,
            ShowTagline = showTagline,
            IconUrl = icon,
            IconDarkUrl = darkAlternate,
            IsDefaultIcon = isDefault,
            IconHeight = ih,
            IconHeightMobile = ihm,
            FontSize = fs,
            FontSizeMobile = fsm,
            FontWeight = weight,
            LetterSpacing = spacing,
            IconGap = gap,
            IconGapMobile = gapM,
            PartGap = partGap,
            PartGapMobile = partGapM,
            TaglineGap = taglineGap,
            TaglineSize = taglineSize,
            TaglineSizeMobile = taglineSizeM,
            TextOffsetY = offset,
            Layout = layout,
            Align = align,
            Text1 = text1,
            Text2 = text2,
            Tagline = showTagline ? tagline : null,
            Color1 = color1,
            Color2 = color2,
            TaglineColor = taglineColor,
            Color1IsDefault = placeColor1 is null && (info.OnDark || BrandStyleRules.Hex(settings.BrandTextColor) is null),
            TaglineColorIsDefault = placeTaglineColor is null && (info.OnDark || BrandStyleRules.Hex(config.TaglineColor) is null),
        };
    }

    /// <summary>آدرس به فایل SVG اشاره می‌کند (برای هشدار پنل: ایمیل و PDF فاکتور به PNG نیاز دارند).</summary>
    public static bool IsSvg(string url)
    {
        var path = url.Split('?', '#')[0];
        return path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>متن تمیز: خالی = null، بلندتر از سقف بریده می‌شود، &lt; و &gt; و { } حذف (دفاع دوم پس از اعتبارسنجی).</summary>
    private static string? Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var text = new string(value.Trim().Where(c => c is not ('<' or '>' or '{' or '}') && !char.IsControl(c)).ToArray());
        if (text.Length == 0)
            return null;
        return text.Length > max ? text[..max] : text;
    }

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
}
