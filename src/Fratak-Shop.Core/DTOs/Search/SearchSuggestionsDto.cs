namespace Fratak_Shop.Core.DTOs.Search;

/// <summary>Instant suggestions of the header search box.</summary>
public class SearchSuggestionsDto
{
    public string Query { get; set; } = string.Empty;
    public List<ProductSuggestion> Products { get; set; } = [];
    public List<LinkSuggestion> Categories { get; set; } = [];
    public List<LinkSuggestion> Brands { get; set; } = [];

    /// <summary>Number of matching products (for "show all N results").</summary>
    public int TotalProducts { get; set; }
}

public record ProductSuggestion(string Name, string Slug, string? ImageName, decimal Price, decimal FinalPrice, bool IsAvailable);

public record LinkSuggestion(string Name, string Slug);
