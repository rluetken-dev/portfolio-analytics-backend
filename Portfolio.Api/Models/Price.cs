namespace Portfolio.Api.Models;

/// <summary>
/// Represents one end-of-day price record for a tradable instrument (e.g., stock or ETF).
/// 
/// Why store daily OHLC data?
/// - Enables candlestick charts and volatility calculations.
/// - Daily data is small enough to persist locally (SQLite) yet useful for most portfolio analytics.
/// 
/// Notes:
/// - We use <see cref="DateOnly"/> for the trading day. EF Core (7/8+) maps this cleanly to TEXT in SQLite.
/// - A "Source" column tracks which external API the record came from (useful if you switch providers later).
/// - "CreatedUtc" / "UpdatedUtc" capture when the record was saved/modified (audit/debugging).
/// </summary>
public class Price
{
    /// <summary>
    /// Surrogate primary key (auto-increment by the database).
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to the related ticker.
    /// This keeps the schema normalized instead of repeating symbols.
    /// </summary>
    public int TickerId { get; set; }

    /// <summary>
    /// Navigation property back to the parent ticker.
    /// </summary>
    public Ticker Ticker { get; set; } = default!;

    /// <summary>
    /// Trading day of this record (no time-of-day component).
    /// We explicitly avoid DateTime to prevent ambiguity with time zones.
    /// </summary>
    public DateOnly TradingDate { get; set; }

    /// <summary>Opening price of the day.</summary>
    public decimal Open { get; set; }

    /// <summary>Highest price of the day.</summary>
    public decimal High { get; set; }

    /// <summary>Lowest price of the day.</summary>
    public decimal Low { get; set; }

    /// <summary>Closing price of the day.</summary>
    public decimal Close { get; set; }

    /// <summary>
    /// Adjusted close price (after splits/dividends).
    /// Essential for accurate P&amp;L calculations.
    /// </summary>
    public decimal AdjustedClose { get; set; }

    /// <summary>Daily trading volume.</summary>
    public long Volume { get; set; }

    /// <summary>
    /// Name of the data provider (e.g., "alpha_vantage").
    /// Useful when combining multiple sources.
    /// </summary>
    public string Source { get; set; } = "alpha_vantage";

    /// <summary>UTC timestamp when the record was created.</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp when the record was last updated.</summary>
    public DateTime? UpdatedUtc { get; set; }
}
