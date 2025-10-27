namespace Portfolio.Api.DTOs;

/// <summary>
/// Represents a summarized view of the user's entire portfolio,
/// combining cash balance and holdings information.
/// </summary>
public class PortfolioSummaryDto
{
    /// <summary>
    /// Current available cash balance for the user (in USD).
    /// </summary>
    public decimal CashBalance { get; set; }

    /// <summary>
    /// Total current value of all held stocks (in USD).
    /// </summary>
    public decimal PortfolioValue { get; set; }

    /// <summary>
    /// Combined total of cash and portfolio value.
    /// </summary>
    public decimal TotalValue => CashBalance + PortfolioValue;

    /// <summary>
    /// List of individual holdings in the user's portfolio.
    /// </summary>
    public List<HoldingDto> Holdings { get; set; } = new();

    /// <summary>
    /// Total realized profit or loss (sum of all closed transactions) in USD.
    /// </summary>
    public decimal? RealizedPLTotalUSD { get; set; }

    /// <summary>
    /// Total unrealized profit or loss (current open positions) in USD.
    /// </summary>
    public decimal? UnrealizedPLTotalUSD { get; set; }

    /// <summary>
    /// Combined profit or loss (realized + unrealized) in USD.
    /// </summary>
    public decimal? TotalProfitLossUSD { get; set; }

    /// <summary>
    /// Overall portfolio performance as a percentage.
    /// </summary>
    public decimal? TotalProfitLossPercent { get; set; }

}


