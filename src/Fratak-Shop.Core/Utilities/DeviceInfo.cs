using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Fratak_Shop.Core.Utilities;

/// <summary>
/// Coarse device identity from the User-Agent (browser family + operating system + form factor).
/// Version numbers are ignored on purpose so browser updates don't trigger "new device" alerts.
/// </summary>
public static partial class DeviceInfo
{
    public sealed record Device(string Browser, string OperatingSystem, bool IsMobile)
    {
        public string DisplayName => $"{Browser} روی {OperatingSystem}{(IsMobile ? " (موبایل)" : string.Empty)}";

        public string Hash => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Browser}|{OperatingSystem}|{IsMobile}")));
    }

    public static Device Parse(string? userAgent)
    {
        var ua = userAgent ?? string.Empty;

        var os = ua switch
        {
            _ when ua.Contains("iPhone") || ua.Contains("iPad") || ua.Contains("iPod") => "iOS",
            _ when ua.Contains("Android") => "Android",
            _ when ua.Contains("Windows") => "Windows",
            _ when ua.Contains("CrOS") => "ChromeOS",
            _ when ua.Contains("Mac OS X") || ua.Contains("Macintosh") => "macOS",
            _ when ua.Contains("Linux") => "Linux",
            _ => "سیستم نامشخص",
        };

        var browser = ua switch
        {
            _ when ua.Contains("Edg/") || ua.Contains("EdgA/") || ua.Contains("EdgiOS/") => "Edge",
            _ when ua.Contains("OPR/") || ua.Contains("Opera") => "Opera",
            _ when ua.Contains("SamsungBrowser") => "Samsung Internet",
            _ when ua.Contains("YaBrowser") => "Yandex",
            _ when ua.Contains("Firefox/") || ua.Contains("FxiOS/") => "Firefox",
            _ when ua.Contains("Chrome/") || ua.Contains("CriOS/") => "Chrome",
            _ when ua.Contains("Safari/") => "Safari",
            _ when ua.Length == 0 => "مرورگر نامشخص",
            _ => ShortName(ua),
        };

        var mobile = ua.Contains("Mobile") || ua.Contains("Android") || ua.Contains("iPhone");
        return new Device(browser, os, mobile);
    }

    private static string ShortName(string ua)
    {
        var match = ProductToken().Match(ua);
        return match.Success ? match.Groups[1].Value : "مرورگر نامشخص";
    }

    [GeneratedRegex(@"^([A-Za-z][A-Za-z\-]{1,30})/", RegexOptions.CultureInvariant)]
    private static partial Regex ProductToken();
}
