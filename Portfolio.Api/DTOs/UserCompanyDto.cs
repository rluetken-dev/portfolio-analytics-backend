namespace Portfolio.Api.DTOs;

/// <summary>
/// Represents one portfolio entry returned to the client.
/// </summary>
public sealed class UserCompanyDto
{
    /// <summary>
    /// Portfolio entry ID.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Related ticker ID.
    /// </summary>
    public int TickerId { get; set; }

    /// <summary>
    /// Ticker symbol, for example AAPL.
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Company display name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Sector classification.
    /// </summary>
    public string? Sector { get; set; }

    /// <summary>
    /// Number of shares currently owned.
    /// </summary>
    public decimal Shares { get; set; }

    /// <summary>
    /// Average purchase price per share.
    /// </summary>
    public decimal? PurchasePrice { get; set; }

    /// <summary>
    /// Optional user note.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Date of the most recent stored price record for this ticker.
    /// </summary>
    public DateTime? LastPriceUpdate { get; set; }
}