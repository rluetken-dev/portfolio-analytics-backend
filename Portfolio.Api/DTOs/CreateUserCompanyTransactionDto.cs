using System.ComponentModel.DataAnnotations;

namespace Portfolio.Api.DTOs;

/// <summary>
/// Request DTO for recording a portfolio transaction.
/// </summary>
public sealed class CreateUserCompanyTransactionDto
{
    /// <summary>
    /// Ticker symbol, for example AAPL.
    /// </summary>
    [Required]
    [MinLength(1)]
    [MaxLength(16)]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Number of shares. Positive values buy shares, negative values sell shares.
    /// </summary>
    [Range(-1_000_000, 1_000_000, ErrorMessage = "Shares must be within a valid range.")]
    public int Shares { get; set; }

    /// <summary>
    /// Transaction price per share.
    /// </summary>
    [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "Price must be non-negative.")]
    public decimal? Price { get; set; }

    /// <summary>
    /// Optional user note.
    /// </summary>
    [MaxLength(500)]
    public string? Notes { get; set; }
}