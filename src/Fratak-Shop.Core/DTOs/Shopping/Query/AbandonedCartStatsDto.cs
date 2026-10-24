namespace Fratak_Shop.Core.DTOs.Shopping.Query;

/// <summary>Abandoned-cart figures for the management reports (admin panel later).</summary>
public class AbandonedCartStatsDto
{
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }

    /// <summary>All detected abandoned carts in the period.</summary>
    public int TotalCount { get; set; }

    /// <summary>Value of all abandoned carts (final item prices when detected).</summary>
    public decimal TotalAmount { get; set; }

    public int OpenCount { get; set; }
    public decimal OpenAmount { get; set; }

    public int RecoveredCount { get; set; }

    /// <summary>Payable amount of the orders that recovered a cart.</summary>
    public decimal RecoveredAmount { get; set; }

    public int ExpiredCount { get; set; }
    public decimal LostAmount { get; set; }

    public int ClearedCount { get; set; }

    public int RemindersSent { get; set; }
    public int CouponsIssued { get; set; }

    /// <summary>Recovered / closed carts, percent.</summary>
    public decimal RecoveryRate { get; set; }
}

/// <summary>Result of one run of the abandoned-cart job.</summary>
public record AbandonedCartRunResult(int Detected, int RemindersSent, int Closed);
