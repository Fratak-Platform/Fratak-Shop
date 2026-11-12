using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Faqs;

/// <summary>Group of frequently asked questions (orders, payment, delivery, ...).</summary>
public class FaqCategory : BaseEntity
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    [Required]
    [MaxLength(200)]
    public string Slug { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(50)]
    public string? IconKey { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    public List<FaqItem> Items { get; set; } = [];
}
