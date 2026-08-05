namespace Portfolio.Api.DTOs;

/// <summary>
/// Represents an aggregated portfolio summary for the authenticated user.
/// </summary>
public sealed class PortfolioSummaryDto
{
    /// <summary>
    /// Current available cash balance in USD.
    /// </summary>
    public decimal CashBalance { get; set; }

    /// <summary>
    /// Current market value of all open holdings in USD.
    /// </summary>
    public decimal PortfolioValue { get; set; }

    /// <summary>
    /// Combined cash and portfolio value in USD.
    /// </summary>
    public decimal TotalValue => CashBalance + PortfolioValue;

    /// <summary>
    /// Current open holdings.
    /// </summary>
    public List<HoldingDto> Holdings { get; set; } = new();

    /// <summary>
    /// Total realized profit or loss in USD.
    /// </summary>
    public decimal? RealizedPLTotalUSD { get; set; }

    /// <summary>
    /// Total unrealized profit or loss in USD.
    /// </summary>
    public decimal? UnrealizedPLTotalUSD { get; set; }

    /// <summary>
    /// Combined realized and unrealized profit or loss in USD.
    /// </summary>
    public decimal? TotalProfitLossUSD { get; set; }

    /// <summary>
    /// Combined portfolio profit or loss as a percentage.
    /// </summary>
    public decimal? TotalProfitLossPercent { get; set; }
}