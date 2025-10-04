namespace Portfolio.Api.DTOs;

/// <summary>
/// Represents a portfolio entry for a user, including company info and investment data.
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
}
