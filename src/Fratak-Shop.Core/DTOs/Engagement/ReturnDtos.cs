using Fratak_Shop.Data.Entities.Orders;

namespace Fratak_Shop.Core.DTOs.Engagement;

public sealed record ReturnReasonDto(Guid Id, string Title, string? Hint, bool RequiresImage);

/// <summary>What of an order can still be returned.</summary>
public sealed class ReturnEligibilityDto
{
    public bool IsEligible { get; init; }
    public string? Reason { get; init; }
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = null!;
    public DateTimeOffset? DeliveredAt { get; init; }
    public DateTimeOffset? Deadline { get; init; }
    public IReadOnlyList<ReturnableItemDto> Items { get; init; } = [];
}

public sealed record ReturnableItemDto(Guid OrderItemId, Guid? ProductId, string ProductName, string? ProductSlug, string? ImageName,
    int OrderedQuantity, int ReturnableQuantity, decimal UnitRefundAmount);

public sealed class CreateReturnCommand
{
    public Guid UserId { get; set; }
    public string OrderNumber { get; set; } = null!;
    public Guid ReasonId { get; set; }
    public string? Description { get; set; }
    public Dictionary<Guid, int> Quantities { get; set; } = [];

    /// <summary>Already validated and stored images (file name, content type, size).</summary>
    public List<(string FileName, string ContentType, int SizeBytes)> Images { get; set; } = [];
}

public sealed class ReturnRequestDto
{
    public Guid Id { get; init; }
    public string RequestNumber { get; init; } = null!;
    public string OrderNumber { get; init; } = null!;
    public ReturnStatus Status { get; init; }
    public string ReasonTitle { get; init; } = null!;
    public string? Description { get; init; }
    public decimal RequestedAmount { get; init; }
    public decimal? ApprovedAmount { get; init; }
    public decimal RefundedAmount { get; init; }
    public string? AdminNote { get; init; }
    public string? ReturnTrackingCode { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public DateTimeOffset? ReceivedAt { get; init; }
    public DateTimeOffset? RefundedAt { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
    public IReadOnlyList<ReturnRequestItemDto> Items { get; init; } = [];
    public IReadOnlyList<string> Images { get; init; } = [];

    public bool CanCancel => Status == ReturnStatus.Pending;
    public bool CanSetTrackingCode => Status == ReturnStatus.Approved;
}

public sealed record ReturnRequestItemDto(string ProductName, string? ProductSlug, int Quantity, decimal UnitRefundAmount);
