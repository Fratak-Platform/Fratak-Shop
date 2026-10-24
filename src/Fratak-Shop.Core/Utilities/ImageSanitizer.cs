using System.Buffers.Binary;

namespace Fratak_Shop.Core.Utilities;

/// <summary>A cleaned image ready to be stored.</summary>
public sealed record SanitizedImage(byte[] Data, string Extension, string ContentType, int Width, int Height);

/// <summary>
/// Validates customer uploaded images (JPEG, PNG, WebP) by their real content and removes
/// everything that is not needed to display them: EXIF (GPS position, camera serial, ...),
/// XMP, comments, text chunks and data appended after the image (polyglot files).
/// The EXIF orientation is kept (as a minimal EXIF block) so phone photos are not shown
/// rotated. Works on raw bytes only – no System.Drawing (Windows only) and no native code.
/// </summary>
public static class ImageSanitizer
{
    public const int MaxSide = 10_000;
    public const long MaxPixels = 40_000_000;

    public static SanitizedImage? Sanitize(byte[] data, out string? error)
    {
        error = null;
        try
        {
            SanitizedImage? result = null;
            if (data.Length > 12 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                result = Jpeg(data);
            else if (data.Length > 33 && data.AsSpan(0, 8).SequenceEqual(PngSignature))
                result = Png(data);
            else if (data.Length > 20 && data.AsSpan(0, 4).SequenceEqual("RIFF"u8) && data.AsSpan(8, 4).SequenceEqual("WEBP"u8))
                result = WebP(data);
            else
            {
                error = "فقط تصاویر JPG، PNG و WebP قابل ارسال هستند.";
                return null;
            }

            if (result is null || result.Width <= 0 || result.Height <= 0)
            {
                error = "فایل تصویر معتبر نیست.";
                return null;
            }

            if (result.Width > MaxSide || result.Height > MaxSide || (long)result.Width * result.Height > MaxPixels)
            {
                error = "ابعاد تصویر بیش از حد مجاز است.";
                return null;
            }

            return result;
        }
        catch (Exception exception) when (exception is IndexOutOfRangeException or ArgumentOutOfRangeException or ArgumentException or OverflowException)
        {
            error = "فایل تصویر معتبر نیست.";
            return null;
        }
    }

    // ── JPEG ────────────────────────────────────────────────────────────────

    private static SanitizedImage? Jpeg(byte[] data)
    {
        using var output = new MemoryStream(data.Length);
        output.Write([0xFF, 0xD8]);

        int width = 0, height = 0;
        ushort orientation = 1;
        var position = 2;

        while (position + 4 <= data.Length)
        {
            if (data[position] != 0xFF)
                return null;

            var marker = data[position + 1];
            if (marker == 0xFF)
            {
                position++; // fill byte
                continue;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                output.Write(data, position, 2);
                position += 2;
                continue;
            }

            if (marker == 0xD9)
                break;

            var length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position + 2));
            if (length < 2 || position + 2 + length > data.Length)
                return null;

            var segment = data.AsSpan(position + 4, length - 2);

            if (marker == 0xDA)
            {
                // Start of scan: the compressed data runs to the end-of-image marker.
                var end = LastIndexOfEoi(data, position + 2 + length);
                if (end < 0 || width == 0)
                    return null;

                if (orientation is > 1 and <= 8)
                    WriteOrientationExif(output, orientation);

                output.Write(data, position, end + 2 - position);
                return new SanitizedImage(output.ToArray(), ".jpg", "image/jpeg", width, height);
            }

            var keep = marker switch
            {
                0xE0 => true,                                         // APP0 JFIF
                0xE2 => segment.StartsWith("ICC_PROFILE\0"u8),        // colour profile only
                0xEE => true,                                         // APP14 Adobe (colour transform)
                >= 0xE1 and <= 0xEF => false,                         // EXIF, XMP, maker notes, ...
                0xFE => false,                                        // comment
                _ => true,                                            // tables, frame headers, DRI
            };

            if (marker == 0xE1 && segment.StartsWith("Exif\0\0"u8))
                orientation = ReadExifOrientation(segment[6..]);

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC && segment.Length >= 5)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
                width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
            }

            if (keep)
                output.Write(data, position, 2 + length);

            position += 2 + length;
        }

        return null;
    }

    private static int LastIndexOfEoi(byte[] data, int from)
    {
        for (var i = data.Length - 2; i >= from; i--)
        {
            if (data[i] == 0xFF && data[i + 1] == 0xD9)
                return i;
        }

        return -1;
    }

    private static ushort ReadExifOrientation(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8)
            return 1;

        var little = tiff[0] == (byte)'I' && tiff[1] == (byte)'I';
        if (!little && !(tiff[0] == (byte)'M' && tiff[1] == (byte)'M'))
            return 1;

        var ifd = U32(tiff, 4, little);
        if (ifd < 8 || ifd + 2 > tiff.Length)
            return 1;

        var count = U16(tiff, (int)ifd, little);
        for (var i = 0; i < count && i < 512; i++)
        {
            var entry = (int)ifd + 2 + i * 12;
            if (entry + 12 > tiff.Length)
                break;

            if (U16(tiff, entry, little) == 0x0112)
            {
                var value = U16(tiff, entry + 8, little);
                return value is >= 1 and <= 8 ? value : (ushort)1;
            }
        }

        return 1;
    }

    private static ushort U16(ReadOnlySpan<byte> span, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(span[at..]) : BinaryPrimitives.ReadUInt16BigEndian(span[at..]);

    private static uint U32(ReadOnlySpan<byte> span, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(span[at..]) : BinaryPrimitives.ReadUInt32BigEndian(span[at..]);

    /// <summary>APP1 with a TIFF header and one IFD entry: Orientation.</summary>
    private static void WriteOrientationExif(Stream output, ushort orientation)
    {
        byte[] app1 =
        [
            0xFF, 0xE1, 0x00, 0x22,                         // APP1, length 34
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'I', (byte)'I', 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, // little endian TIFF, IFD0 at 8
            0x01, 0x00,                                     // one entry
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, // tag 0x0112, SHORT, count 1
            (byte)orientation, 0x00, 0x00, 0x00,            // value
            0x00, 0x00, 0x00, 0x00,                         // no next IFD
        ];
        output.Write(app1);
    }

    // ── PNG ─────────────────────────────────────────────────────────────────

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly HashSet<string> PngKeep =
        ["IHDR", "PLTE", "IDAT", "IEND", "tRNS", "gAMA", "cHRM", "sRGB", "iCCP", "sBIT", "pHYs", "bKGD", "acTL", "fcTL", "fdAT"];

    private static SanitizedImage? Png(byte[] data)
    {
        using var output = new MemoryStream(data.Length);
        output.Write(PngSignature);

        int width = 0, height = 0;
        var position = 8;
        var first = true;

        while (position + 12 <= data.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position));
            if (length > int.MaxValue || position + 12 + (long)length > data.Length)
                return null;

            var type = System.Text.Encoding.ASCII.GetString(data, position + 4, 4);
            if (first)
            {
                if (type != "IHDR" || length < 8)
                    return null;

                width = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position + 8)), int.MaxValue);
                height = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position + 12)), int.MaxValue);
                first = false;
            }

            var total = 12 + (int)length;
            if (PngKeep.Contains(type))
                output.Write(data, position, total);

            position += total;

            if (type == "IEND")
                return new SanitizedImage(output.ToArray(), ".png", "image/png", width, height);
        }

        return null;
    }

    // ── WebP ────────────────────────────────────────────────────────────────

    private static SanitizedImage? WebP(byte[] data)
    {
        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var end = (int)Math.Min((long)riffSize + 8, data.Length);

        using var body = new MemoryStream(data.Length);
        int width = 0, height = 0;
        var position = 12;

        while (position + 8 <= end)
        {
            var fourCc = System.Text.Encoding.ASCII.GetString(data, position, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + 4));
            if (position + 8 + (long)size > end)
                return null;

            var padded = (long)size + (size & 1);

            var chunkLength = (int)Math.Min(8 + padded, end - position);
            var payload = data.AsSpan(position + 8, (int)size);

            switch (fourCc)
            {
                case "VP8X" when size >= 10:
                    width = 1 + (payload[4] | payload[5] << 8 | payload[6] << 16);
                    height = 1 + (payload[7] | payload[8] << 8 | payload[9] << 16);
                    var copy = data.AsSpan(position, chunkLength).ToArray();
                    copy[8] &= unchecked((byte)~(0x08 | 0x04)); // no EXIF / XMP flags
                    body.Write(copy);
                    break;
                case "VP8 " when size >= 10:
                    if (width == 0 && payload[3] == 0x9D && payload[4] == 0x01 && payload[5] == 0x2A)
                    {
                        width = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]) & 0x3FFF;
                        height = BinaryPrimitives.ReadUInt16LittleEndian(payload[8..]) & 0x3FFF;
                    }
                    body.Write(data, position, chunkLength);
                    break;
                case "VP8L" when size >= 5:
                    if (width == 0 && payload[0] == 0x2F)
                    {
                        var bits = BinaryPrimitives.ReadUInt32LittleEndian(payload[1..]);
                        width = (int)(bits & 0x3FFF) + 1;
                        height = (int)((bits >> 14) & 0x3FFF) + 1;
                    }
                    body.Write(data, position, chunkLength);
                    break;
                case "ALPH" or "ANIM" or "ANMF" or "ICCP":
                    body.Write(data, position, chunkLength);
                    break;
                default:
                    // EXIF, XMP and unknown chunks are dropped.
                    break;
            }

            position += chunkLength;
        }

        if (body.Length == 0)
            return null;

        using var output = new MemoryStream((int)body.Length + 12);
        output.Write("RIFF"u8);
        Span<byte> size32 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size32, (uint)(body.Length + 4));
        output.Write(size32);
        output.Write("WEBP"u8);
        body.Position = 0;
        body.CopyTo(output);

        return new SanitizedImage(output.ToArray(), ".webp", "image/webp", width, height);
    }
}
