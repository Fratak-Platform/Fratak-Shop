using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Navigation;

/// <summary>
/// Social/communication channel of the shop. Rendered in the header and/or the
/// footer depending on the flags, so the admin decides where each channel appears.
/// </summary>
public class SocialLink : BaseEntity
{
    public SocialPlatform Platform { get; set; }

    [MaxLength(500)]
    public string? Url { get; set; }

    /// <summary>
    /// Overrides the default Persian title of the platform when filled.
    /// </summary>
    [MaxLength(200)]
    public string? Title { get; set; }

    public bool ShowInHeader { get; set; }

    public bool ShowInFooter { get; set; } = true;

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

public enum SocialPlatform
{
    None = 0,
    Instagram = 1,
    Telegram = 2,
    WhatsApp = 3,
    X = 4,
    LinkedIn = 5,
    YouTube = 6,
    Aparat = 7,
    Facebook = 8,
    Pinterest = 9,
    Rubika = 10,
    Bale = 11,
    Eitaa = 12,
    Phone = 13,
    Email = 14,
}
