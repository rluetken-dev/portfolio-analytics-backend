namespace Portfolio.Api.DTOs;

/// <summary>
/// Represents a portfolio entry for a user, including company info and investment data.
/// Combines data from the UserCompany table and the related Ticker entity.
/// </summary>
public class UserCompanyDto
{
    /// <summary>
    /// The unique ID of the portfolio entry.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The related company (ticker) ID.
    /// </summary>
    public int TickerId { get; set; }

    /// <summary>
    /// The ticker symbol (e.g., AAPL, MSFT).
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// The optional company name (e.g., "Apple Inc.").
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The sector or industry classification of the company.
    /// Pulled directly from the Tickers table.
    /// </summary>
    public string? Sector { get; set; }   // 🟢 NEW FIELD

    /// <summary>
    /// The number of shares owned by the user.
    /// </summary>
    public decimal? Shares { get; set; }

    /// <summary>
    /// The average purchase price per share.
    /// </summary>
    public decimal? PurchasePrice { get; set; }

    /// <summary>
    /// Optional notes or comments from the user.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// The date of the most recent stored price record for this ticker.
    /// Useful for checking data freshness in the frontend.
    /// </summary>
    public DateTime? LastPriceUpdate { get; set; }
}
