using System.Security.Cryptography;
using System.Text;

namespace Fratak_Shop.Core.Services.Team;

/// <summary>
/// پیاده‌سازی استاندارد «رمز یک‌بارمصرف زمان‌دار» (TOTP – RFC 6238) که برنامه‌هایی مثل Google Authenticator،
/// Microsoft Authenticator و Authy می‌سازند. هیچ سرویس بیرونی یا پولی لازم نیست؛ گوشی و سرور هر دو با یک کلید مخفی مشترک
/// و ساعت فعلی، یک عدد ۶ رقمی یکسان می‌سازند که هر ۳۰ ثانیه عوض می‌شود.
/// <para>
/// چطور کار می‌کند؟ شمارهٔ بازه = (ثانیه‌های گذشته از ۱۹۷۰) ÷ ۳۰. سپس HMAC-SHA1(کلید، شمارهٔ بازه) حساب می‌شود و
/// ۴ بایت از آن (به روش «برش پویا»ی RFC 4226) به عدد ۶ رقمی تبدیل می‌شود.
/// </para>
/// مثال (بردار آزمون رسمی RFC): کلید «12345678901234567890» در ثانیهٔ ۵۹ ← کد ۲۸۷۰۸۲.
/// محل استفاده: <see cref="StaffTotpService"/> (ورود دومرحله‌ای و تأیید دوباره کارکنان) و آزمون‌های Unit/TeamTests.
/// </summary>
public static class Totp
{
    /// <summary>طول هر بازه به ثانیه (استاندارد همهٔ برنامه‌ها).</summary>
    public const int StepSeconds = 30;

    /// <summary>تعداد رقم کد.</summary>
    public const int Digits = 6;

    /// <summary>
    /// چند بازهٔ قبل و بعد هم پذیرفته شود تا اختلاف چندثانیه‌ای ساعت گوشی و سرور مشکل نسازد.
    /// ۱ یعنی کد ۳۰ ثانیهٔ قبل و ۳۰ ثانیهٔ بعد هم قبول است (پیشنهاد خود RFC).
    /// </summary>
    public const int AllowedDrift = 1;

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>شمارهٔ بازهٔ ۳۰ثانیه‌ای برای یک لحظه. مثال: ثانیهٔ ۵۹ ← بازهٔ ۱.</summary>
    public static long StepAt(DateTimeOffset moment) => moment.ToUnixTimeSeconds() / StepSeconds;

    /// <summary>یک کلید مخفی تصادفی ۲۰ بایتی (۱۶۰ بیت، همان اندازهٔ پیشنهادی RFC) با مولد امن رمزنگاری.</summary>
    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);

    /// <summary>
    /// کد ۶ رقمی یک بازه. خروجی همیشه ۶ نویسه است (صفرهای اول حفظ می‌شوند). مثال: «004521».
    /// </summary>
    public static string Compute(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        // شمارنده باید big-endian باشد (بایت پرارزش اول) – طبق RFC 4226.
        for (var i = 7; i >= 0; i--)
        {
            counter[i] = (byte)(step & 0xFF);
            step >>= 8;
        }

        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(secret, counter, hash);

        // «برش پویا»: ۴ بیت آخر هش می‌گوید از کدام بایت ۴ بایت برداشته شود.
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    /// <summary>
    /// بررسی کد واردشده. اگر درست باشد شمارهٔ بازه‌ای که با آن جور شد برمی‌گردد (برای جلوگیری از استفادهٔ دوباره)، وگرنه null.
    /// <paramref name="lastUsedStep"/>: آخرین بازهٔ پذیرفته‌شده؛ کدهای همان بازه یا قبل‌تر رد می‌شوند.
    /// مقایسه با زمان ثابت انجام می‌شود تا از زمان پاسخ نتوان کد را حدس زد.
    /// </summary>
    public static long? Verify(byte[] secret, string? code, DateTimeOffset now, long? lastUsedStep = null)
    {
        var normalized = NormalizeCode(code);
        if (normalized is null)
            return null;

        var current = StepAt(now);
        long? matched = null;
        for (var drift = -AllowedDrift; drift <= AllowedDrift; drift++)
        {
            var step = current + drift;
            if (lastUsedStep is { } last && step <= last)
                continue;

            // همهٔ بازه‌ها بررسی می‌شوند (بدون خروج زودهنگام) تا زمان پاسخ ثابت بماند.
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Compute(secret, step)), Encoding.ASCII.GetBytes(normalized)) && matched is null)
                matched = step;
        }

        return matched;
    }

    /// <summary>
    /// کد واردشده را تمیز می‌کند: رقم‌های فارسی/عربی به انگلیسی، فاصله و خط تیره حذف. اگر ۶ رقم نشود null.
    /// مثال: «۲۸۷ ۰۸۲» ← «287082».
    /// </summary>
    public static string? NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var builder = new StringBuilder(Digits);
        foreach (var c in code)
        {
            if (c is >= '0' and <= '9')
                builder.Append(c);
            else if (c is >= '۰' and <= '۹')
                builder.Append((char)('0' + (c - '۰')));
            else if (c is >= '٠' and <= '٩')
                builder.Append((char)('0' + (c - '٠')));
            else if (c is ' ' or '-' or '\u200c')
                continue;
            else
                return null;
        }

        return builder.Length == Digits ? builder.ToString() : null;
    }

    /// <summary>کدگذاری Base32 (بدون «=») – شکلی که کاربر در برنامهٔ احراز هویت تایپ یا اسکن می‌کند. مثال: «GEZDGNBVGY3TQOJQ…».</summary>
    public static string ToBase32(ReadOnlySpan<byte> data)
    {
        var builder = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                builder.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
            builder.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        return builder.ToString();
    }

    /// <summary>بازگرداندن Base32 به بایت؛ فاصله، «=» و حروف کوچک پذیرفته می‌شوند. ورودی نامعتبر ← null.</summary>
    public static byte[]? FromBase32(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var output = new List<byte>(text.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var raw in text)
        {
            if (raw is ' ' or '=' or '-')
                continue;
            var index = Base32Alphabet.IndexOf(char.ToUpperInvariant(raw));
            if (index < 0)
                return null;
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return output.Count == 0 ? null : output.ToArray();
    }

    /// <summary>
    /// آدرس «otpauth://» که داخل QR قرار می‌گیرد و برنامه‌های احراز هویت می‌فهمند.
    /// مثال: otpauth://totp/Fratak:09120000009?secret=ABC…&amp;issuer=Fratak&amp;digits=6&amp;period=30
    /// </summary>
    public static string BuildUri(string issuer, string account, string base32Secret)
    {
        var label = Uri.EscapeDataString(issuer) + ":" + Uri.EscapeDataString(account);
        return $"otpauth://totp/{label}?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
    }

    /// <summary>
    /// ساخت کدهای بازیابی (برای روزی که گوشی در دسترس نیست). هر کد ۱۰ نویسه به شکل «ab3k9-x7m2q»
    /// از حروف و رقم‌های بی‌ابهام (بدون 0/o و 1/l) است.
    /// </summary>
    public static List<string> NewRecoveryCodes(int count = 10)
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
        var codes = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var chars = new char[11];
            for (var j = 0; j < 11; j++)
                chars[j] = j == 5 ? '-' : alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            codes.Add(new string(chars));
        }

        return codes;
    }

    /// <summary>
    /// شکل یکسان کد بازیابی برای مقایسه: حروف کوچک، بدون فاصله و خط تیره. اگر شکل کد بازیابی نداشته باشد null.
    /// مثال: « AB3K9-X7M2Q » ← «ab3k9x7m2q».
    /// </summary>
    public static string? NormalizeRecoveryCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;
        var cleaned = new string(code.Where(c => c is not (' ' or '-')).Select(char.ToLowerInvariant).ToArray());
        return cleaned.Length == 10 && cleaned.All(char.IsAsciiLetterOrDigit) ? cleaned : null;
    }

    /// <summary>هش SHA-256 کد بازیابی (فقط هش ذخیره می‌شود). ورودی باید قبلاً با NormalizeRecoveryCode تمیز شده باشد.</summary>
    public static string HashRecoveryCode(string normalizedCode) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes("fratak-totp-recovery:" + normalizedCode)));
}
