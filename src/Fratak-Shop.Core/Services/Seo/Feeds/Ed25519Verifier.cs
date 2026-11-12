using System.Numerics;
using System.Security.Cryptography;

namespace Fratak_Shop.Core.Services.Seo.Feeds;

/// <summary>
/// بررسی امضای Ed25519 (RFC 8032، بخش ۵.۱.۷) با کد مدیریت‌شدهٔ خالص.
/// <para>چرا خودمان؟ دات‌نت تا امروز Ed25519 داخلی ندارد و ترب توکن‌هایش را با EdDSA (ed25519) امضا می‌کند؛ افزودن کتابخانهٔ
/// رمزنگاری بومی (libsodium) به هاست ویندوزی ریسک دارد. فقط «بررسی» لازم است (نه امضا) و هر درخواست ترب یک بار بررسی می‌شود،
/// پس سرعت BigInteger (چند میلی‌ثانیه) کاملاً کافی است.</para>
/// <para>درستی با بردارهای آزمون رسمی RFC 8032 در تست‌ها سنجیده می‌شود (Ed25519VerifierTests).</para>
/// <para>کجا استفاده می‌شود؟ <see cref="TorobTokenValidator"/> برای API محصولات ترب.</para>
/// </summary>
public static class Ed25519Verifier
{
    private static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
    private static readonly BigInteger L = BigInteger.Pow(2, 252) + BigInteger.Parse("27742317777372353535851937790883648493");
    private static readonly BigInteger D = Mod(-121665 * Inverse(121666));
    private static readonly BigInteger SqrtM1 = BigInteger.ModPow(2, (P - 1) / 4, P);
    private static readonly Point BasePoint = CreateBase();

    private readonly record struct Point(BigInteger X, BigInteger Y, BigInteger Z, BigInteger T);

    /// <summary>
    /// امضا معتبر است؟ کلید ۳۲ بایت، امضا ۶۴ بایت. ورودی نامعتبر ← false (هرگز استثنا نمی‌دهد).
    /// </summary>
    public static bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
    {
        if (publicKey.Length != 32 || signature.Length != 64)
            return false;
        var a = Decode(publicKey);
        var r = Decode(signature[..32]);
        if (a is null || r is null)
            return false;
        var s = new BigInteger(signature[32..], isUnsigned: true, isBigEndian: false);
        if (s >= L)
            return false; // جلوگیری از امضای «انعطاف‌پذیر» (malleability)

        var buffer = new byte[64 + message.Length];
        signature[..32].CopyTo(buffer);
        publicKey.CopyTo(buffer.AsSpan(32));
        message.CopyTo(buffer.AsSpan(64));
        var k = new BigInteger(SHA512.HashData(buffer), isUnsigned: true, isBigEndian: false) % L;

        var left = Multiply(s, BasePoint);
        var right = Add(r.Value, Multiply(k, a.Value));
        return Equal(left, right);
    }

    /// <summary>
    /// کلید عمومی ۳۲ بایتی از PEM/Base64 قالب SubjectPublicKeyInfo (همان متنی که ترب منتشر کرده). خراب ← null.
    /// مثال: «MCowBQYDK2VwAyEAt6Mu4T0pBORY11W+QeM35UsmLO3vsf+6yKpFDEImFk0=» ← ۳۲ بایت انتهایی.
    /// </summary>
    public static byte[]? PublicKeyFromSpki(string? pemOrBase64)
    {
        if (string.IsNullOrWhiteSpace(pemOrBase64))
            return null;
        var text = string.Concat(pemOrBase64.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("-----", StringComparison.Ordinal)));
        try
        {
            var der = Convert.FromBase64String(text);
            if (der.Length == 32)
                return der;
            // 30 2a 30 05 06 03 2b 65 70 03 21 00 + کلید (OID 1.3.101.112 = Ed25519)
            byte[] prefix = [0x30, 0x2a, 0x30, 0x05, 0x06, 0x03, 0x2b, 0x65, 0x70, 0x03, 0x21, 0x00];
            return der.Length == 44 && der.AsSpan(0, 12).SequenceEqual(prefix) ? der[12..] : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static Point CreateBase()
    {
        var y = Mod(4 * Inverse(5));
        var x = RecoverX(y, 0)!.Value;
        return new Point(x, y, 1, Mod(x * y));
    }

    private static Point? Decode(ReadOnlySpan<byte> bytes)
    {
        var y = new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
        var sign = (int)(y >> 255);
        y &= (BigInteger.One << 255) - 1;
        var x = RecoverX(y, sign);
        return x is null ? null : new Point(x.Value, y, 1, Mod(x.Value * y));
    }

    private static BigInteger? RecoverX(BigInteger y, int sign)
    {
        if (y >= P)
            return null;
        var x2 = Mod((y * y - 1) * Inverse(D * y * y + 1));
        if (x2.IsZero)
            return sign == 0 ? BigInteger.Zero : null;
        var x = BigInteger.ModPow(x2, (P + 3) / 8, P);
        if (!Mod(x * x - x2).IsZero)
            x = Mod(x * SqrtM1);
        if (!Mod(x * x - x2).IsZero)
            return null;
        if ((int)(x & 1) != sign)
            x = P - x;
        return x;
    }

    private static Point Add(Point p, Point q)
    {
        var a = Mod((p.Y - p.X) * (q.Y - q.X));
        var b = Mod((p.Y + p.X) * (q.Y + q.X));
        var c = Mod(2 * p.T * q.T * D);
        var d = Mod(2 * p.Z * q.Z);
        var e = b - a;
        var f = d - c;
        var g = d + c;
        var h = b + a;
        return new Point(Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h));
    }

    private static Point Multiply(BigInteger scalar, Point point)
    {
        var result = new Point(0, 1, 1, 0);
        while (scalar > 0)
        {
            if (!scalar.IsEven)
                result = Add(result, point);
            point = Add(point, point);
            scalar >>= 1;
        }

        return result;
    }

    private static bool Equal(Point p, Point q) =>
        Mod(p.X * q.Z - q.X * p.Z).IsZero && Mod(p.Y * q.Z - q.Y * p.Z).IsZero;

    private static BigInteger Inverse(BigInteger value) => BigInteger.ModPow(Mod(value), P - 2, P);

    private static BigInteger Mod(BigInteger value)
    {
        var result = value % P;
        return result.Sign < 0 ? result + P : result;
    }
}
