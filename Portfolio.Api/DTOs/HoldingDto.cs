namespace Portfolio.Api.DTOs;

/// <summary>
/// Represents one open portfolio holding returned to the client.
/// </summary>
public sealed class HoldingDto
{
    /// <summary>
    /// Internal ticker ID.
    /// </summary>
    public int TickerId { get; set; }

    /// <summary>
    /// Ticker symbol, for example AAPL.
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Company display name.
    /// </summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>
    /// Number of shares currently held.
    /// </summary>
    public decimal Shares { get; set; }

    /// <summary>
    /// Average purchase price in USD.
    /// </summary>
    public decimal? PurchasePriceUSD { get; set; }

    /// <summary>
    /// Latest known market price in USD.
    /// </summary>
    public decimal? CurrentPriceUSD { get; set; }

    /// <summary>
    /// Current market value in USD.
    /// </summary>
    public decimal? CurrentValueUSD => CurrentPriceUSD.HasValue
        ? Shares * CurrentPriceUSD.Value
        : null;

    /// <summary>
    /// Average buy price in USD.
    /// </summary>
    public decimal? AvgBuyPriceUSD { get; set; }

    /// <summary>
    /// Unrealized profit or loss in USD.
    /// </summary>
    public decimal? UnrealizedPLUSD { get; set; }

    /// <summary>
    /// Unrealized profit or loss as a percentage.
    /// </summary>
    public decimal? UnrealizedPLPercent { get; set; }

    /// <summary>
    /// Realized profit or loss in USD.
    /// </summary>
    public decimal? RealizedPLUSD { get; set; }

    /// <summary>
    /// Realized profit or loss as a percentage.
    /// </summary>
    public decimal? RealizedPLPercent { get; set; }
}