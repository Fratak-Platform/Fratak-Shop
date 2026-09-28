using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Favorites;

public class Favorite : BaseEntity
{
    public Guid UserId { get; set; }

    public Guid ProductId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;
}
