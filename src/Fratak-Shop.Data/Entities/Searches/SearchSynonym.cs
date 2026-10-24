using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Searches;

/// <summary>
/// Group of words that mean the same thing for the search (e.g. هندزفری، هدفون، ایرپاد).
/// A query containing any of them also matches the others.
/// </summary>
public class SearchSynonym : BaseEntity
{
    /// <summary>Comma-separated words / phrases.</summary>
    [Required]
    [MaxLength(1000)]
    public string Terms { get; set; } = null!;

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Aggregated search statistics per normalized query: drives "popular searches" and
/// shows the admin what customers look for (and what returns nothing).
/// </summary>
public class SearchQueryLog : BaseEntity
{
    /// <summary>Normalized query (Persian letters unified, lower case, single spaces).</summary>
    [Required]
    [MaxLength(100)]
    public string NormalizedQuery { get; set; } = null!;

    /// <summary>Last raw form that was searched (shown to customers).</summary>
    [Required]
    [MaxLength(100)]
    public string Query { get; set; } = null!;

    public int SearchCount { get; set; }

    public int LastResultCount { get; set; }

    public DateTimeOffset LastSearchedAt { get; set; }

    /// <summary>Admin: always show in popular searches.</summary>
    public bool IsPinned { get; set; }

    /// <summary>Admin: never show in popular searches.</summary>
    public bool IsHidden { get; set; }
}
