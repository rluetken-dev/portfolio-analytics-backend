namespace Portfolio.Api.Models;

/// <summary>
/// Represents a tradable instrument such as a stock or ETF.
/// </summary>
public sealed class Ticker
{
    public int Id { get; set; }

    /// <summary>
    /// Ticker symbol, for example AAPL.
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Display name, for example Apple Inc.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Sector classification.
    /// </summary>
    public string? Sector { get; set; }

    /// <summary>
    /// UTC timestamp of the most recent successful price refresh.
    /// </summary>
    public DateTime? LastPriceUpdate { get; set; }

    public ICollection<Price> Prices { get; set; } = new List<Price>();

    public ICollection<UserCompany> UserCompanies { get; set; } = new List<UserCompany>();
}