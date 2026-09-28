using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Faqs;

public class FaqItem : BaseEntity
{
    [Required]
    [MaxLength(500)]
    public string Question { get; set; } = null!;

    [Required]
    public string Answer { get; set; } = null!;

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
