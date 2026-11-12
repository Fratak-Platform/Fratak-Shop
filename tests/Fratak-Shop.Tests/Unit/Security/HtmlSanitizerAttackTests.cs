using AngleSharp.Html.Parser;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Tests.Unit.Security;

/// <summary>
/// شبیه‌سازی حملهٔ XSS روی پاک‌ساز HTML (متن بلاگ، توضیح محصول، پاسخ تیکت و ... از این مسیر رد می‌شوند).
/// خروجی دوباره با یک پارسر واقعی مرورگرمانند خوانده می‌شود؛ پس حمله‌های «mutation XSS» که بعد از
/// پارس دوباره زنده می‌شوند هم گرفته می‌شوند. صرفِ جست‌وجوی متنی کافی نیست، چون متنِ escape‌شده بی‌خطر است.
/// </summary>
[Trait("Area", "Security")]
public class HtmlSanitizerAttackTests
{
    // فهرست نمونه‌های رایج (OWASP XSS Filter Evasion و PortSwigger cheat sheet).
    public static TheoryData<string> Payloads => new()
    {
        "<script>alert(1)</script>",
        "<ScRiPt src=//evil.test/x.js></ScRiPt>",
        "<img src=x onerror=alert(1)>",
        "<img src=x OnErRoR=alert(1)>",
        "<svg onload=alert(1)>",
        "<svg><script>alert(1)</script></svg>",
        "<body onload=alert(1)>",
        "<a href=\"javascript:alert(1)\">x</a>",
        "<a href=\"jAvAsCrIpT:alert(1)\">x</a>",
        "<a href=\"java&#x09;script:alert(1)\">x</a>",
        "<a href=\"&#106;&#97;&#118;&#97;&#115;&#99;&#114;&#105;&#112;&#116;&#58;alert(1)\">x</a>",
        "<a href=\" javascript:alert(1)\">x</a>",
        "<a href=\"vbscript:msgbox(1)\">x</a>",
        "<a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">x</a>",
        "<iframe src=\"https://evil.test\"></iframe>",
        "<iframe srcdoc=\"<script>alert(1)</script>\"></iframe>",
        "<object data=\"javascript:alert(1)\"></object>",
        "<embed src=\"javascript:alert(1)\">",
        "<base href=\"https://evil.test/\">",
        "<meta http-equiv=\"refresh\" content=\"0;url=https://evil.test\">",
        "<link rel=stylesheet href=https://evil.test/x.css>",
        "<form action=\"javascript:alert(1)\"><button>x</button></form>",
        "<button formaction=\"javascript:alert(1)\">x</button>",
        "<input autofocus onfocus=alert(1)>",
        "<details open ontoggle=alert(1)>",
        "<div style=\"background:url(javascript:alert(1))\">x</div>",
        "<div style=\"width:expression(alert(1))\">x</div>",
        "<style>@import 'https://evil.test/x.css';</style>",
        "<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>",
        "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">",
        "<svg><a xlink:href=\"javascript:alert(1)\"><text>x</text></a></svg>",
        "<a href=\"#\" onclick=\"alert(1)\">x</a>",
        "<img src=\"x\" onerror=\"&#97;lert(1)\">",
        "<<script>script>alert(1)<</script>/script>",
        "<p>ok</p><!--<img src=x onerror=alert(1)>-->",
    };

    [Theory]
    [MemberData(nameof(Payloads))]
    public void Sanitized_html_has_no_executable_content_after_reparsing(string payload)
    {
        var clean = payload.SanitizeText();

        // دو بار پارس: یک بار خروجی، یک بار خروجیِ پاک‌سازیِ دوباره (نباید چیز خطرناکی «جهش» کند).
        AssertSafe(clean, payload);
        AssertSafe(clean.SanitizeText(), payload);
    }

    [Fact]
    public void Safe_formatting_and_links_survive()
    {
        var clean = "<p>سلام <strong>دنیا</strong> <a href=\"https://fratak.test/p/1\">لینک</a></p><ul><li>یک</li></ul>".SanitizeText();

        Assert.Contains("<strong>دنیا</strong>", clean);
        Assert.Contains("href=\"https://fratak.test/p/1\"", clean);
        Assert.Contains("<li>یک</li>", clean);
    }

    private static readonly HashSet<string> ForbiddenElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "iframe", "object", "embed", "base", "meta", "link", "style", "frame", "frameset", "applet", "form", "noscript", "math", "svg",
    };

    private static readonly string[] UrlAttributes = ["href", "src", "action", "formaction", "xlink:href", "data", "srcdoc", "background", "poster"];

    private static void AssertSafe(string html, string payload)
    {
        var document = new HtmlParser().ParseDocument($"<html><body>{html}</body></html>");
        foreach (var element in document.All)
        {
            if (element.LocalName is "html" or "head" or "body")
                continue;

            Assert.False(ForbiddenElements.Contains(element.LocalName), $"<{element.LocalName}> survived: {payload} → {html}");

            foreach (var attribute in element.Attributes)
            {
                var name = attribute.Name.ToLowerInvariant();
                Assert.False(name.StartsWith("on", StringComparison.Ordinal), $"event handler {name} survived: {payload} → {html}");
                Assert.NotEqual("srcdoc", name);

                var value = new string((attribute.Value ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).ToArray()).ToLowerInvariant();
                if (UrlAttributes.Contains(name))
                {
                    Assert.False(value.StartsWith("javascript:", StringComparison.Ordinal), $"javascript url survived: {payload} → {html}");
                    Assert.False(value.StartsWith("vbscript:", StringComparison.Ordinal), $"vbscript url survived: {payload} → {html}");
                    Assert.False(value.StartsWith("data:", StringComparison.Ordinal), $"data url survived: {payload} → {html}");
                }

                if (name == "style")
                {
                    Assert.DoesNotContain("javascript", value);
                    Assert.DoesNotContain("expression(", value);
                    Assert.DoesNotContain("@import", value);
                }
            }
        }
    }
}
