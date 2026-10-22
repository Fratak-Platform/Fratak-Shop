namespace Fratak_Shop.Web.Helpers;

public static class ImageHelper
{
    public const string DefaultProductImage = "/assets/image/products/1.webp";
    public const string DefaultCategoryImage = "/assets/image/category/1.webp";
    public const string DefaultBlogImage = "/assets/image/blog/1.jpg";
    public const string NoImage = "/assets/image/no-image.png";

    /// <summary>
    /// Returns a resolvable URL for a product image. Falls back to DefaultProductImage.
    /// Uploaded images are expected under /uploads/products/{imageName}
    /// </summary>
    public static string ProductImageUrl(string? imageName)
    {
        if (string.IsNullOrWhiteSpace(imageName)) return DefaultProductImage;
        // If already an absolute /assets path, return as-is
        if (imageName.StartsWith("/")) return imageName;
        if (imageName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)) return "/" + imageName;
        return $"/uploads/products/{imageName}";
    }

    public static string CategoryImageUrl(string? imageName)
    {
        if (string.IsNullOrWhiteSpace(imageName)) return DefaultCategoryImage;
        if (imageName.StartsWith("/")) return imageName;
        if (imageName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)) return "/" + imageName;
        return $"/uploads/categories/{imageName}";
    }

    public static string BlogImageUrl(string? imageName)
    {
        if (string.IsNullOrWhiteSpace(imageName)) return DefaultBlogImage;
        if (imageName.StartsWith("/")) return imageName;
        if (imageName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)) return "/" + imageName;
        return $"/uploads/blogs/{imageName}";
    }

    public static string BannerImageUrl(string? imageName)
    {
        if (string.IsNullOrWhiteSpace(imageName)) return "/assets/image/heroSlider/1.webp";
        if (imageName.StartsWith("/")) return imageName;
        if (imageName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)) return "/" + imageName;
        return $"/uploads/banners/{imageName}";
    }

    public static string UserAvatarOrDefault(string? avatar)
    {
        if (string.IsNullOrWhiteSpace(avatar)) return "/assets/image/man.webp";
        if (avatar.StartsWith("/")) return avatar;
        return $"/uploads/users/{avatar}";
    }
}
