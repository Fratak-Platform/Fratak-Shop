using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Products;

/// <summary>
/// Property-dictionary entry of a product. The <see cref="Key"/> is the feature
/// name and <see cref="Value"/> holds either a plain string or a serialized
/// JSON object depending on <see cref="ValueType"/>.
/// </summary>
public class ProductFeature : BaseEntity
{
    public Guid ProductId { get; set; }

    [Required]
    [MaxLength(250)]
    public string Key { get; set; } = null!;

    /// <summary>
    /// Feature value. Plain text when <see cref="ValueType"/> is
    /// <see cref="FeatureValueType.Text"/>; a serialized JSON object when it is
    /// <see cref="FeatureValueType.Object"/>.
    /// </summary>
    [Required]
    public string Value { get; set; } = null!;

    public FeatureValueType ValueType { get; set; }

    public int DisplayOrder { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;
}

public enum FeatureValueType
{
    None = 0,
    Text = 1,
    Object = 2,
}
