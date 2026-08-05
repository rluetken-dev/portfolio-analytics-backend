namespace Portfolio.Api.Models;

/// <summary>
/// Represents one buy or sell transaction for a user's portfolio.
/// </summary>
public sealed class UserCompanyTransaction
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int TickerId { get; set; }

    /// <summary>
    /// Number of shares. Positive values represent buys, negative values represent sells.
    /// </summary>
    public int Shares { get; set; }

    public decimal? Price { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Ticker? Ticker { get; set; }
}