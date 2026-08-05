using System.ComponentModel.DataAnnotations;

namespace Portfolio.Api.DTOs;

/// <summary>
/// Request DTO for adding or updating a company position in the user's portfolio.
/// </summary>
public sealed class CreateUserCompanyDto
{
    /// <summary>
    /// Existing ticker ID. Optional when a symbol is provided.
    /// </summary>
    public int? TickerId { get; set; }

    /// <summary>
    /// Ticker symbol, for example AAPL.
    /// </summary>
    [Required]
    [MinLength(1)]
    [MaxLength(16)]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Number of shares to add. Positive values buy shares, negative values sell shares.
    /// </summary>
    [Range(-1_000_000, 1_000_000, ErrorMessage = "Shares must be within a valid range.")]
    public int Shares { get; set; }

    /// <summary>
    /// Price per share. If omitted, the backend may use the current market price or zero depending on the workflow.
    /// </summary>
    [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "Purchase price must be non-negative.")]
    public decimal? PurchasePrice { get; set; }

    /// <summary>
    /// Optional user note.
    /// </summary>
    [MaxLength(500)]
    public string? Notes { get; set; }
}