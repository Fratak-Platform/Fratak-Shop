using System.Globalization;
using System.Net;
using System.Text;
using Fratak_Shop.Core.Services.Branding;

namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// ساخت HTML لوگو (آیکون + «بخش اول» + «بخش رنگی» + شعار) از <see cref="BrandView"/> – مرحلهٔ دوازدهم.
/// <para>
/// یک منبع برای همهٔ محل‌ها تا ظاهر سایت، پیش‌نمایش پنل و صفحهٔ تعمیرات دقیقاً یکی باشد:
/// <list type="bullet">
/// <item><see cref="Render"/>: سایت و پنل (partial «_BrandLockup») و صفحهٔ تعمیرات. اندازه‌ها با متغیرهای CSS روی همان عنصر
/// (--bl-ih، --bl-fs …) داده می‌شوند و قاعده‌های ثابت در <c>wwwroot/assets/css/brand-lockup.css</c> هستند؛ اندازهٔ موبایل با
/// media query همان فایل اعمال می‌شود (پس کش خروجی صفحه هم مشکلی ندارد).</item>
/// <item><see cref="RenderEmail"/>: سربرگ ایمیل با جدول و استایل درون‌خطی (برنامه‌های ایمیل flex و متغیر CSS را نمی‌فهمند).</item>
/// </list>
/// </para>
/// امنیت: همهٔ متن‌ها و آدرس‌ها HtmlEncode می‌شوند؛ رنگ‌ها پیش‌تر در BrandResolver فقط hex معتبر شده‌اند و عددها int هستند،
/// پس هیچ مقداری از پنل نمی‌تواند CSS یا HTML تزریق کند.
/// </summary>
public static class BrandLockupHtml
{
    /// <summary>
    /// HTML لوگو برای صفحه‌های سایت/پنل. اگر لوگو در این محل خاموش باشد رشتهٔ خالی برمی‌گردد (مگر در پیش‌نمایش پنل).
    /// مثال خروجی (کوتاه‌شده):
    /// <c>&lt;span class="bl bl-row" style="--bl-ih:44px;…"&gt;&lt;img class="bl-icon" …&gt;&lt;span class="bl-text"&gt;…&lt;/span&gt;&lt;/span&gt;</c>
    /// </summary>
    /// <param name="view">لوگوی نهایی محل.</param>
    /// <param name="cssClass">کلاس‌های اضافهٔ ظرف (مثلاً justify-center) یا null.</param>
    /// <param name="version">افزودن نسخهٔ فایل به آدرس‌های محلی (asp-append-version) یا null.</param>
    /// <param name="preview">پیش‌نمایش پنل: همهٔ بخش‌ها ساخته می‌شوند (بخش‌های خاموش با hidden) تا اسکریپت پیش‌نمایش زنده روشن/خاموششان کند.</param>
    public static string Render(BrandView view, string? cssClass = null, Func<string, string>? version = null, bool preview = false)
    {
        if (!view.Visible && !preview)
            return string.Empty;

        var sb = new StringBuilder(900);
        var layout = view.Layout switch { BrandLayout.IconEnd => "bl-row-rev", BrandLayout.IconTop => "bl-col", _ => "bl-row" };
        var align = view.Align switch { BrandAlign.Start => "bl-a-start", BrandAlign.End => "bl-a-end", _ => "bl-a-center" };
        sb.Append("<span class=\"bl bl-").Append(view.Place.ToString().ToLowerInvariant()).Append(' ').Append(layout).Append(' ').Append(align);
        if (!string.IsNullOrWhiteSpace(cssClass))
            sb.Append(' ').Append(Enc(cssClass));
        sb.Append("\" style=\"").Append(Vars(view)).Append('"');
        if (preview)
            sb.Append(" data-bl-preview=\"").Append(view.Place).Append('"').Append(view.Visible ? "" : " hidden");
        sb.Append('>');

        var site = !view.OnDark && view.Place is not (BrandPlace.Invoice or BrandPlace.Email);
        if (view.ShowIcon || preview)
        {
            var alt = view.ShowText ? "" : view.FullText;
            var width = view.IconWidth(view.IconHeight);
            AppendImg(sb, view.IconUrl, view.IconDarkUrl is null ? "bl-icon" : "bl-icon brand-logo-light", alt, view.IconHeight, width, version, lazy: false, hidden: !view.ShowIcon);
            if (view.IconDarkUrl is { } dark)
                AppendImg(sb, dark, "bl-icon brand-logo-dark", alt, view.IconHeight, width, version, lazy: true, hidden: !view.ShowIcon);
        }

        if (view.ShowText || preview)
        {
            sb.Append("<span class=\"bl-text\"").Append(view.ShowText ? "" : " hidden").Append('>');
            sb.Append("<span class=\"bl-name").Append(site && view.Color1IsDefault ? " brand-text-main" : "").Append("\">");
            sb.Append("<span class=\"bl-p1\">").Append(Enc(view.Text1)).Append("</span>");
            if (view.Text2 is not null || preview)
                sb.Append("<span class=\"bl-p2\"").Append(view.Text2 is null ? " hidden" : "").Append('>').Append(Enc(view.Text2 ?? "")).Append("</span>");
            sb.Append("</span>");
            if (view.Tagline is not null || preview)
                sb.Append("<span class=\"bl-tag").Append(site && view.TaglineColorIsDefault ? " brand-tagline" : "").Append('"')
                  .Append(view.Tagline is null ? " hidden" : "").Append('>').Append(Enc(view.Tagline ?? "")).Append("</span>");
            sb.Append("</span>");
        }

        sb.Append("</span>");
        return sb.ToString();
    }

    /// <summary>
    /// متغیرهای CSS لوگو. مثال: «--bl-ih:44px;--bl-ihm:36px;--bl-gap:8px;…;--bl-c1:#0B1927».
    /// همین نام‌ها را اسکریپت پیش‌نمایش پنل (admin-brand.js) هنگام تغییر فرم عوض می‌کند.
    /// </summary>
    public static string Vars(BrandView v)
    {
        var sb = new StringBuilder(400);
        Px(sb, "ih", v.IconHeight); Px(sb, "ihm", v.IconHeightMobile);
        Px(sb, "gap", v.IconGap); Px(sb, "gapm", v.IconGapMobile);
        Px(sb, "fs", v.FontSize); Px(sb, "fsm", v.FontSizeMobile);
        sb.Append("--bl-fw:").Append(v.FontWeight.ToString(CultureInfo.InvariantCulture)).Append(';');
        sb.Append("--bl-ls:").Append(BrandStyleRules.Css(v.LetterSpacing)).Append("px;");
        Px(sb, "pg", v.PartGap); Px(sb, "pgm", v.PartGapMobile);
        Px(sb, "tg", v.TaglineGap); Px(sb, "ts", v.TaglineSize); Px(sb, "tsm", v.TaglineSizeMobile);
        Px(sb, "dy", v.TextOffsetY);
        sb.Append("--bl-c1:").Append(v.Color1).Append(";--bl-c2:").Append(v.Color2).Append(";--bl-c3:").Append(v.TaglineColor);
        return sb.ToString();
    }

    /// <summary>
    /// سربرگ ایمیل (جدول؛ بدون flex و متغیر CSS). آدرس آیکون با <paramref name="baseUrl"/> مطلق می‌شود چون ایمیل نشانی نسبی نمی‌فهمد.
    /// مثال: baseUrl = "https://fratak.ir" و آیکون "/assets/brand/mark-512.png" → https://fratak.ir/assets/brand/mark-512.png.
    /// </summary>
    public static string RenderEmail(BrandView view, string baseUrl)
    {
        if (!view.Visible)
            return string.Empty;

        var iconUrl = Absolute(view.IconUrl, baseUrl);
        var width = view.IconWidth(view.IconHeight);
        var icon = view.ShowIcon
            ? $"<img src=\"{Enc(iconUrl)}\" alt=\"{Enc(view.ShowText ? "" : view.FullText)}\" height=\"{view.IconHeight}\"{(width is { } w ? $" width=\"{w}\"" : "")} style=\"display:block;height:{view.IconHeight}px;width:auto;border:0;outline:none\">"
            : "";
        var text = new StringBuilder();
        if (view.ShowText)
        {
            var spacing = BrandStyleRules.Css(view.LetterSpacing);
            text.Append($"<div style=\"font-size:{view.FontSize}px;font-weight:{view.FontWeight};letter-spacing:{spacing}px;line-height:1.2;white-space:nowrap;color:{view.Color1};position:relative;top:{view.TextOffsetY}px\">");
            text.Append(Enc(view.Text1));
            if (view.Text2 is not null)
                text.Append($"<span style=\"color:{view.Color2};padding-right:{view.PartGap}px\">{Enc(view.Text2)}</span>");
            text.Append("</div>");
            if (view.Tagline is not null)
                text.Append($"<div style=\"font-size:{view.TaglineSize}px;color:{view.TaglineColor};line-height:1.4;padding-top:{view.TaglineGap}px\">{Enc(view.Tagline)}</div>");
        }

        var valign = view.Align switch { BrandAlign.Start => "top", BrandAlign.End => "bottom", _ => "middle" };
        var halign = view.Align switch { BrandAlign.Start => "right", BrandAlign.End => "left", _ => "center" };
        if (view.Layout == BrandLayout.IconTop || icon.Length == 0 || text.Length == 0)
        {
            var spacer = icon.Length > 0 && text.Length > 0 ? $"<div style=\"height:{view.IconGap}px;line-height:{view.IconGap}px;font-size:0\">&nbsp;</div>" : "";
            return $"<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" align=\"{halign}\" style=\"margin:0 auto\"><tr><td align=\"{halign}\" dir=\"rtl\">"
                   + (icon.Length > 0 ? $"<div style=\"display:inline-block\">{icon}</div>" : "") + spacer + text + "</td></tr></table>";
        }

        var iconCell = $"<td valign=\"{valign}\" style=\"padding:0\">{icon}</td>";
        var gapCell = $"<td style=\"width:{view.IconGap}px;font-size:0;line-height:0\">&nbsp;</td>";
        var textCell = $"<td valign=\"{valign}\" dir=\"rtl\" style=\"padding:0;text-align:right\">{text}</td>";
        // جدول راست‌به‌چپ است: «آیکون ابتدا» یعنی اولین خانه (سمت راست).
        var cells = view.Layout == BrandLayout.IconEnd ? textCell + gapCell + iconCell : iconCell + gapCell + textCell;
        return $"<table role=\"presentation\" dir=\"rtl\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" align=\"center\" style=\"margin:0 auto\"><tr>{cells}</tr></table>";
    }

    /// <summary>آدرس نسبی → مطلق با نشانی سایت. مثال: ("/a.png", "https://x.ir/") → https://x.ir/a.png؛ آدرس http(s) دست نمی‌خورد.</summary>
    public static string Absolute(string url, string baseUrl) =>
        url.StartsWith('/') && !url.StartsWith("//") ? baseUrl.TrimEnd('/') + url : url;

    private static void AppendImg(StringBuilder sb, string url, string cssClass, string alt, int height, int? width, Func<string, string>? version, bool lazy, bool hidden)
    {
        var src = version is not null && url.StartsWith('/') && !url.StartsWith("//") ? version(url) : url;
        sb.Append("<img class=\"").Append(cssClass).Append("\" src=\"").Append(Enc(src)).Append("\" alt=\"").Append(Enc(alt))
          .Append("\" height=\"").Append(height.ToString(CultureInfo.InvariantCulture)).Append('"');
        if (width is { } w)
            sb.Append(" width=\"").Append(w.ToString(CultureInfo.InvariantCulture)).Append('"');
        sb.Append(lazy ? " loading=\"lazy\"" : "").Append(" decoding=\"async\"").Append(hidden ? " hidden" : "").Append('>');
    }

    private static void Px(StringBuilder sb, string name, int value) =>
        sb.Append("--bl-").Append(name).Append(':').Append(value.ToString(CultureInfo.InvariantCulture)).Append("px;");

    private static string Enc(string value) => WebUtility.HtmlEncode(value);
}
