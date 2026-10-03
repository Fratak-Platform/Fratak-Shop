namespace Fratak_Shop.Core.DTOs.Favorites.Command;

public class CreateFavoriteCommand
{
    public Guid UserId { get; set; }
    public Guid ProductId { get; set; }
}

public class EditFavoriteCommand
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ProductId { get; set; }
}
