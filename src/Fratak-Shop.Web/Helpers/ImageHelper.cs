namespace Fratak_Shop.Web.Helpers;

/// <summary>
/// Resolves stored image names to URLs. Uploaded files live under
/// <c>/uploads/{folder}/{fileName}</c>; values that already are a path
/// (e.g. the bundled demo images under <c>/assets</c>) are returned unchanged.
/// </summary>
public static class ImageHelper
{
    public const string ProductPlaceholder = "/assets/image/products/1.webp";
    public const string CategoryPlaceholder = "/assets/image/category/1.webp";
    public const string BlogPlaceholder = "/assets/image/blog/1.jpg";
    public const string BannerPlaceholder = "/assets/image/heroSlider/1.webp";
    public const string BrandPlaceholder = "/assets/image/logo.png";

    public static string Product(string? imageName) => Resolve(imageName, "products", ProductPlaceholder);

    public static string Category(string? imageName) => Resolve(imageName, "categories", CategoryPlaceholder);

    public static string Blog(string? imageName) => Resolve(imageName, "blogs", BlogPlaceholder);

    public static string Banner(string? imageName) => Resolve(imageName, "banners", BannerPlaceholder);

    public static string Brand(string? imageName) => Resolve(imageName, "brands", BrandPlaceholder);

    private static string Resolve(string? imageName, string folder, string placeholder)
    {
        if (string.IsNullOrWhiteSpace(imageName))
            return placeholder;

        if (imageName.StartsWith('/') || imageName.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return imageName;

        return $"/uploads/{folder}/{Uri.EscapeDataString(imageName)}";
    }
}
