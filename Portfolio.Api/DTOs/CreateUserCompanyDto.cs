using System.ComponentModel.DataAnnotations;

namespace Portfolio.Api.DTOs;

/// <summary>
/// Request DTO for adding a new company (ticker) to the user's portfolio.
/// </summary>
public class CreateUserCompanyDto
{
    /// <summary>
    /// The ID of the ticker (company) to link (optional if new ticker is created).
    /// </summary>
    public int? TickerId { get; set; }  // ← geändert: nullable und ohne [Required]

    /// <summary>
    /// The symbol of the ticker (company) to link.
    /// </summary>
    [Required]
    public string Symbol { get; set; } = string.Empty; // bleibt required

    /// <summary>
    /// Number of shares involved in the transaction.
    /// Positive = Buy, Negative = Sell.
    /// </summary>
    [Range(-1000000, 1000000, ErrorMessage = "Shares must be within a valid range.")]
    public int Shares { get; set; }

    /// <summary>
    /// Purchase price per share. If omitted or null, the current market price will be used.
    /// </summary>
    [Range(0, double.MaxValue, ErrorMessage = "Purchase price must be a non-negative number.")]
    public decimal? PurchasePrice { get; set; }

    /// <summary>
    /// Optional personal note about the investment.
    /// </summary>
    [MaxLength(500)]
    public string? Notes { get; set; }
}
