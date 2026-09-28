using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Products;

public class ProductImage : BaseEntity
{
    public Guid ProductId { get; set; }

    /// <summary>
    /// File name of the image stored on the server.
    /// </summary>
    [Required]
    [MaxLength(250)]
    public string ImageName { get; set; } = null!;

    [MaxLength(250)]
    public string? AltText { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsPrimary { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;
}
