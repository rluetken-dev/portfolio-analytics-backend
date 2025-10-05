using System.ComponentModel.DataAnnotations;

namespace Portfolio.Api.DTOs;

/// <summary>
/// Request DTO for adding a new company (ticker) to the user's portfolio.
/// </summary>
public class CreateUserCompanyDto
{
    /// <summary>
    /// The ID of the ticker (company) to link.
    /// </summary>
    [Required]
    public int TickerId { get; set; }

    /// <summary>
    /// The symbol of the ticker (company) to link.
    /// </summary>
    [Required]
    public string? Symbol { get; set; }

    /// <summary>
    /// Number of shares owned by the user.
    /// </summary>
    [Range(0, double.MaxValue, ErrorMessage = "Shares must be a non-negative number.")]
    public decimal? Shares { get; set; }

    /// <summary>
    /// Purchase price per share.
    /// </summary>
    [Range(0, double.MaxValue, ErrorMessage = "Purchase price must be a non-negative number.")]
    public decimal? PurchasePrice { get; set; }

    /// <summary>
    /// Optional personal note about the investment.
    /// </summary>
    [MaxLength(500)]
    public string? Notes { get; set; }
}
