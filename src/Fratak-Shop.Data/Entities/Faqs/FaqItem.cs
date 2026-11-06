using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Faqs;

public class FaqItem : BaseEntity
{
    public Guid? CategoryId { get; set; }

    [Required]
    [MaxLength(500)]
    public string Question { get; set; } = null!;

    [Required]
    public string Answer { get; set; } = null!;

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    [ForeignKey(nameof(CategoryId))]
    public FaqCategory? Category { get; set; }
}
