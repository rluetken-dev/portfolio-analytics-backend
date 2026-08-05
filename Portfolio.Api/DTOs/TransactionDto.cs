namespace Portfolio.Api.DTOs;

/// <summary>
/// Represents one portfolio transaction returned to the client.
/// </summary>
public sealed class TransactionDto
{
    /// <summary>
    /// UTC timestamp when the transaction was recorded.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Number of shares. Positive values represent buys, negative values represent sells.
    /// </summary>
    public int Shares { get; set; }

    /// <summary>
    /// Transaction price per share.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Optional user note.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Ticker symbol.
    /// </summary>
    public string? Symbol { get; set; }

    /// <summary>
    /// Company display name.
    /// </summary>
    public string? CompanyName { get; set; }

    /// <summary>
    /// Derived transaction type for client display.
    /// </summary>
    public string Type => Shares > 0 ? "Buy" : "Sell";

    /// <summary>
    /// Absolute transaction value in USD.
    /// </summary>
    public decimal? TotalUSD => Price.HasValue ? Price * Math.Abs(Shares) : null;
}