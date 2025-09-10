namespace Portfolio.Api.Models;

/// <summary>
/// Represents a stock ticker (e.g., AAPL, MSFT).
/// Each ticker can have many historical price records.
/// </summary>
public class Ticker
{
    public int Id { get; set; }

    /// <summary>
    /// The stock symbol, e.g. "AAPL".
    /// </summary>
    public string Symbol { get; set; } = default!;

    /// <summary>
    /// Optional display name, e.g. "Apple Inc."
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Navigation property to all related price records.
    /// </summary>
    public ICollection<Price> Prices { get; set; } = new List<Price>();
}
