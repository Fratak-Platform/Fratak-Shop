using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.DTOs.Favorites.Query;

public class FavoriteDto : BaseDto
{
    public Guid UserId { get; set; }
    public Guid ProductId { get; set; }
    public string? ProductName { get; set; }
    public string? ProductSlug { get; set; }
    public string? ProductImageName { get; set; }
    public decimal Price { get; set; }
    public decimal FinalPrice { get; set; }
    public bool HasActiveDiscount { get; set; }
    public bool IsInStock { get; set; }
}
