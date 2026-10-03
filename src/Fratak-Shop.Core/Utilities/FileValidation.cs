using System.Drawing;
using Microsoft.AspNetCore.Http;

namespace Fratak_Shop.Core.Utilities;

public static class FileValidation
{
    private const int MaxWidth = 10000;
    private const int MaxHeight = 10000;
    private const long MaxPixels = 40_000_000;

    private static readonly HashSet<string> ValidFileExtensions =
    [
        ".mp4", ".mp3", ".zip", ".rar",
        ".wav", ".docx", ".mmf", ".m4a",
        ".ogg", ".doc", ".pdf", ".txt",
        ".xls", ".xla", ".xlsx",
        ".ppt", ".pptx", ".gif",
        ".jpg", ".jpeg", ".png",
        ".tif", ".wmv", ".bmp",
        ".wmf", ".log"
    ];

    private static readonly Dictionary<string, ImageDefinition> Images =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = new(
                "image/jpeg",
                [0xFF, 0xD8, 0xFF]),

            [".jpeg"] = new(
                "image/jpeg",
                [0xFF, 0xD8, 0xFF]),

            [".png"] = new(
                "image/png",
                [
                    0x89, 0x50, 0x4E, 0x47,
                    0x0D, 0x0A, 0x1A, 0x0A
                ]),

            [".bmp"] = new(
                "image/bmp",
                "BM"u8.ToArray())
        };

    public static bool IsValidFile(this IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return false;

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        return ValidFileExtensions.Contains(extension);
    }

    public static bool IsValidImageFile(this IFormFile? imageFile)
    {
        if (imageFile == null || imageFile.Length == 0)
            return false;

        var extension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();

        if (!Images.TryGetValue(extension, out var image))
            return false;

        if (!string.Equals(
                image.MimeType,
                imageFile.ContentType,
                StringComparison.OrdinalIgnoreCase))
            return false;

        return HasValidHeader(imageFile, image.Header);
    }

    public static bool IsImage(this IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return false;

        try
        {
            using var stream = file.OpenReadStream();

#pragma warning disable CA1416
            using var image = Image.FromStream(
                stream,
                false,
                true);

            if (image.Width <= 0 || image.Height <= 0)
                return false;

            if (image.Width > MaxWidth)
                return false;

            if (image.Height > MaxHeight)
                return false;

            if ((long)image.Width * image.Height > MaxPixels)
                return false;
#pragma warning restore CA1416

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasValidHeader(IFormFile file, byte[] expectedHeader)
    {
        using var stream = file.OpenReadStream();

        Span<byte> buffer = stackalloc byte[expectedHeader.Length];

        if (stream.Read(buffer) != expectedHeader.Length)
            return false;

        return buffer.SequenceEqual(expectedHeader);
    }

    private sealed record ImageDefinition(
        string MimeType,
        byte[] Header);
}