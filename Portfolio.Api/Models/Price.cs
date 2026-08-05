namespace Portfolio.Api.Models;

/// <summary>
/// Represents one end-of-day OHLCV price record for a tradable instrument.
/// </summary>
public sealed class Price
{
    /// <summary>
    /// Surrogate primary key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Related ticker ID.
    /// </summary>
    public int TickerId { get; set; }

    /// <summary>
    /// Related ticker.
    /// </summary>
    public Ticker Ticker { get; set; } = default!;

    /// <summary>
    /// Trading day without time-of-day information.
    /// </summary>
    public DateOnly TradingDate { get; set; }

    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal AdjustedClose { get; set; }
    public long Volume { get; set; }

    /// <summary>
    /// Data provider identifier.
    /// </summary>
    public string Source { get; set; } = "alpha_vantage";

    /// <summary>
    /// UTC timestamp when the record was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when the record was last updated.
    /// </summary>
    public DateTime? UpdatedUtc { get; set; }
}