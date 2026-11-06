using Fratak_Shop.Data.Entities.Products;

namespace Fratak_Shop.Core.DTOs.Engagement;

/// <summary>Active «خبرم کن» requests of the current customer for one product.</summary>
public sealed record ProductAlertState(bool BackInStock, bool PriceDrop, decimal? TargetPrice);

public sealed class ProductAlertDto
{
    public Guid Id { get; init; }
    public ProductAlertType Type { get; init; }
    public bool IsActive { get; init; }
    public decimal PriceAtSubscribe { get; init; }
    public decimal? TargetPrice { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? NotifiedAt { get; init; }
    public decimal? NotifiedPrice { get; init; }

    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = null!;
    public string ProductSlug { get; init; } = null!;
    public string? ImageName { get; init; }
    public decimal CurrentPrice { get; init; }
    public decimal CurrentFinalPrice { get; init; }
    public bool IsAvailable { get; init; }
    public bool ProductIsActive { get; init; }
}

/// <summary>Price chart of a product (one point per change, plus today).</summary>
public sealed class PriceHistoryDto
{
    public IReadOnlyList<PricePoint> Points { get; init; } = [];
    public decimal Current { get; init; }
    public decimal Lowest { get; init; }
    public decimal Highest { get; init; }
    public int Days { get; init; }

    /// <summary>A chart needs at least two different moments.</summary>
    public bool HasData => Points.Count >= 2;
}

public sealed record PricePoint(DateTimeOffset At, decimal FinalPrice, decimal Price, bool IsInStock);
