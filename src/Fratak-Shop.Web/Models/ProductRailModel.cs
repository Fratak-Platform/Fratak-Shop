using Fratak_Shop.Core.DTOs.Products.Query;

namespace Fratak_Shop.Web.Models;

/// <summary>A titled product slider (related, bought together, personal suggestions, ...).</summary>
public record ProductRailModel(string Title, IReadOnlyList<ProductFilterData> Products, string? Subtitle = null);
