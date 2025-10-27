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
}

/// <summary>
/// Represents one specific holding in the user's portfolio,
/// including market value and basic performance data.
/// </summary>
public class HoldingDto
{
    /// <summary>
    /// ID of the related ticker.
    /// </summary>
    public int TickerId { get; set; }

    /// <summary>
    /// Ticker symbol (e.g. "AAPL").
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Full company name.
    /// </summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>
    /// Number of shares held by the user.
    /// </summary>
    public decimal Shares { get; set; }

    /// <summary>
    /// Average purchase price per share (in USD).
    /// </summary>
    public decimal? PurchasePriceUSD { get; set; }

    /// <summary>
    /// Latest known market price per share (in USD).
    /// </summary>
    public decimal? CurrentPriceUSD { get; set; }

    /// <summary>
    /// Total current market value of this holding (in USD).
    /// </summary>
    public decimal CurrentValueUSD => Shares * (CurrentPriceUSD ?? 0);

    /// <summary>
    /// Optional performance difference (CurrentValue - PurchaseCost).
    /// </summary>
    public decimal? ProfitLossUSD =>
        PurchasePriceUSD.HasValue ? (CurrentPriceUSD - PurchasePriceUSD) * Shares : null;
}
