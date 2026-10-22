namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// Store-wide settings bound from the <c>Shop</c> section of appsettings.json.
/// Everything that used to be hard-coded in the template (store name, contact
/// details, shipping methods, ...) is configurable from here.
/// </summary>
public class ShopOptions
{
    public const string SectionName = "Shop";

    public string SiteName { get; set; } = "فراتک شاپ";

    public string Tagline { get; set; } = "بهترین خرید رو تجربه کن!";

    public string? SupportPhone { get; set; }

    public string? SupportEmail { get; set; }

    public string? Address { get; set; }

    public string? WorkingHours { get; set; }

    public string? InstagramUrl { get; set; }

    public string? TelegramUrl { get; set; }

    /// <summary>
    /// Orders with a payable amount equal or above this value ship for free (0 = disabled).
    /// </summary>
    public decimal FreeShippingThreshold { get; set; }

    /// <summary>
    /// Seeds demo catalog data (categories, brands, products, banners, posts, ...) on an empty database.
    /// </summary>
    public bool SeedDemoData { get; set; }

    /// <summary>
    /// Allows paying with the built-in sandbox gateway (for development / demos only).
    /// </summary>
    public bool EnableSandboxGateway { get; set; } = true;

    public List<ShippingMethodOption> ShippingMethods { get; set; } = [];
}

public class ShippingMethodOption
{
    public string Code { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public decimal Cost { get; set; }

    /// <summary>
    /// Earliest delivery day offset from today (used to build the delivery-day picker).
    /// </summary>
    public int MinDeliveryDays { get; set; } = 1;
}
