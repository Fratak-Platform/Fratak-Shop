namespace Fratak_Shop.Core.Services.Media;

/// <summary>
/// File storage configuration. <see cref="RootPath"/> points at the web root
/// (wwwroot) and every upload lands under <see cref="PublicBasePath"/>, so the
/// stored file name can be converted to a public URL without extra mapping.
/// </summary>
public class MediaOptions
{
    /// <summary>مسیر ریشه فایل‌های عمومی؛ در نبود مقدار، wwwroot پوشه جاری استفاده می‌شود.</summary>
    public string? RootPath { get; set; }

    /// <summary>مسیر عمومی فایل‌ها نسبت به ریشه سایت.</summary>
    public string PublicBasePath { get; set; } = "/uploads";

    /// <summary>حداکثر حجم هر فایل بر حسب مگابایت.</summary>
    public int MaxFileSizeMegabytes { get; set; } = 8;
}

/// <summary>پوشه‌های استاندارد آپلود، تا نام پوشه در کد پراکنده نشود.</summary>
public static class MediaFolders
{
    public const string Products = "products";
    public const string Categories = "categories";
    public const string Blog = "blog";
    public const string Banners = "banners";
    public const string Avatars = "avatars";
    public const string BankTransfers = "bank-transfers";
    public const string Seo = "seo";
    public const string Pages = "pages";
    public const string Social = "social";
}
