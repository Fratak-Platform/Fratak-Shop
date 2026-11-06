using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Fratak_Shop.Core.DTOs.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Infrastructure.Security;

/// <summary>Forms protected by the smart captcha (failure counters are kept per action and IP).</summary>
public static class CaptchaActions
{
    public const string Login = "login";
    public const string OtpSend = "otp-send";
    public const string OtpVerify = "otp-verify";
    public const string Register = "register";
    public const string Contact = "contact";
    public const string GiftCard = "giftcard";
    public const string TrackOrder = "track";
}

public sealed record CaptchaChallenge(string Token, string Image);

/// <summary>
/// "Smart" captcha: invisible for normal visitors and only required after repeated failures
/// (wrong password / code, many SMS requests …) from the same IP for the same form.
/// The challenge is an SVG of digits drawn as distorted strokes (no text the page could leak),
/// the answer travels inside an encrypted, 5-minute token and every token works only once.
/// </summary>
public interface ICaptchaService
{
    CaptchaChallenge Create();

    /// <summary>True when the answer matches. The token is consumed either way.</summary>
    bool Validate(string? token, string? answer);

    bool IsRequired(string action, HttpContext http, StoreSettingDto settings);

    void RecordFailure(string action, HttpContext http);

    void Reset(string action, HttpContext http);
}

public sealed class CaptchaService(IDataProtectionProvider protectionProvider, IMemoryCache cache) : ICaptchaService
{
    private const int Length = 5;
    private const string Alphabet = "23456789";
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(30);

    private readonly ITimeLimitedDataProtector _protector = protectionProvider.CreateProtector("Fratak.Captcha.v1").ToTimeLimitedDataProtector();

    public CaptchaChallenge Create()
    {
        Span<char> answer = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
            answer[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];

        var code = new string(answer);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        var token = _protector.Protect($"{nonce}|{code}", TokenLifetime);
        var svg = CaptchaImage.Render(code);
        return new CaptchaChallenge(token, "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg)));
    }

    public bool Validate(string? token, string? answer)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 1000 || string.IsNullOrWhiteSpace(answer))
            return false;

        string payload;
        try
        {
            payload = _protector.Unprotect(token);
        }
        catch (CryptographicException)
        {
            return false;
        }

        var parts = payload.Split('|');
        if (parts.Length != 2)
            return false;

        // Single use: a token is burned on its first check, right or wrong.
        var usedKey = "captcha:used:" + parts[0];
        if (cache.TryGetValue(usedKey, out _))
            return false;
        cache.Set(usedKey, true, TokenLifetime + TimeSpan.FromMinutes(1));

        var given = NormalizeDigits(answer);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(given), Encoding.ASCII.GetBytes(parts[1]));
    }

    public bool IsRequired(string action, HttpContext http, StoreSettingDto settings)
    {
        if (!settings.IsCaptchaEnabled)
            return false;

        var threshold = Math.Max(0, settings.CaptchaAfterFailedAttempts);
        return threshold == 0 || Failures(action, http) >= threshold;
    }

    public void RecordFailure(string action, HttpContext http)
    {
        var key = Key(action, http);
        var count = Failures(action, http) + 1;
        cache.Set(key, count, FailureWindow);
    }

    public void Reset(string action, HttpContext http) => cache.Remove(Key(action, http));

    private int Failures(string action, HttpContext http) => cache.TryGetValue(Key(action, http), out int count) ? count : 0;

    private static string Key(string action, HttpContext http) => $"captcha:fail:{action}:{http.Connection.RemoteIpAddress}";

    /// <summary>Persian / Arabic digits → ASCII, other characters dropped.</summary>
    public static string NormalizeDigits(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is >= '0' and <= '9')
                builder.Append(c);
            else if (c is >= '\u06F0' and <= '\u06F9')
                builder.Append((char)('0' + (c - '\u06F0')));
            else if (c is >= '\u0660' and <= '\u0669')
                builder.Append((char)('0' + (c - '\u0660')));
        }
        return builder.ToString();
    }
}

/// <summary>Draws digits as jittered, rotated strokes plus noise curves.</summary>
public static class CaptchaImage
{
    private const int Width = 160;
    private const int Height = 54;

    // Glyph strokes in a 10 × 16 box.
    private static readonly Dictionary<char, (int X, int Y)[][]> Glyphs = new()
    {
        ['2'] = [[(0, 4), (1, 1), (5, 0), (9, 1), (10, 4), (9, 7), (0, 16), (10, 16)]],
        ['3'] = [[(0, 1), (5, 0), (10, 2), (10, 6), (5, 8), (10, 10), (10, 14), (5, 16), (0, 15)], [(3, 8), (5, 8)]],
        ['4'] = [[(7, 16), (7, 0), (0, 11), (10, 11)]],
        ['5'] = [[(10, 0), (1, 0), (0, 7), (5, 6), (9, 8), (10, 12), (8, 15), (4, 16), (0, 14)]],
        ['6'] = [[(9, 1), (5, 0), (1, 3), (0, 9), (1, 14), (5, 16), (9, 14), (10, 10), (8, 7), (4, 7), (0, 10)]],
        ['7'] = [[(0, 0), (10, 0), (4, 16)], [(3, 8), (8, 8)]],
        ['8'] = [[(5, 8), (1, 6), (1, 2), (5, 0), (9, 2), (9, 6), (5, 8), (0, 11), (1, 15), (5, 16), (9, 15), (10, 11), (5, 8)]],
        ['9'] = [[(10, 6), (6, 9), (2, 9), (0, 6), (1, 2), (5, 0), (9, 1), (10, 6), (9, 12), (5, 16), (1, 15)]],
    };

    private static readonly string[] Colors = ["#0069FD", "#0f766e", "#7c3aed", "#b45309", "#be123c", "#0B1927"];

    public static string Render(string code)
    {
        var svg = new StringBuilder(4096);
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Width}\" height=\"{Height}\" viewBox=\"0 0 {Width} {Height}\">");
        svg.Append("<rect width=\"100%\" height=\"100%\" fill=\"#f8fafc\"/>");

        // Background noise curves (drawn with the same pen as the digits).
        for (var i = 0; i < 4; i++)
            svg.Append(CultureInfo.InvariantCulture,
                $"<path d=\"M{R(0, 20)} {R(5, Height - 5)} C{R(30, 70)} {R(0, Height)} {R(80, 120)} {R(0, Height)} {R(140, Width)} {R(5, Height - 5)}\" stroke=\"{Pick(Colors)}\" stroke-width=\"{R(10, 18) / 10.0}\" fill=\"none\" opacity=\"0.55\"/>");

        var step = (Width - 20) / (double)code.Length;
        for (var i = 0; i < code.Length; i++)
        {
            var x = 12 + i * step + R(-3, 3);
            var y = 10 + R(-4, 6);
            var scale = R(17, 22) / 10.0;
            var rotate = R(-25, 25);
            var skew = R(-12, 12);
            svg.Append(CultureInfo.InvariantCulture,
                $"<g transform=\"translate({x:0.#} {y}) rotate({rotate} 10 16) skewX({skew}) scale({scale:0.##})\" stroke=\"{Pick(Colors)}\" stroke-width=\"{R(14, 20) / 10.0 / scale:0.##}\" fill=\"none\" stroke-linecap=\"round\" stroke-linejoin=\"round\">");
            foreach (var stroke in Glyphs[code[i]])
            {
                svg.Append("<path d=\"");
                for (var p = 0; p < stroke.Length; p++)
                {
                    var (px, py) = stroke[p];
                    svg.Append(CultureInfo.InvariantCulture, $"{(p == 0 ? 'M' : 'L')}{px * 0.8 + R(-5, 5) / 10.0:0.#} {py * 0.8 + R(-5, 5) / 10.0:0.#} ");
                }
                svg.Append("\"/>");
            }
            svg.Append("</g>");
        }

        for (var i = 0; i < 40; i++)
            svg.Append(CultureInfo.InvariantCulture, $"<circle cx=\"{R(0, Width)}\" cy=\"{R(0, Height)}\" r=\"{R(5, 13) / 10.0}\" fill=\"{Pick(Colors)}\" opacity=\"0.5\"/>");

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static int R(int min, int max) => RandomNumberGenerator.GetInt32(min, max + 1);

    private static string Pick(string[] values) => values[RandomNumberGenerator.GetInt32(values.Length)];
}
