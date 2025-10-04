using System.ComponentModel.DataAnnotations;

namespace Portfolio.Api.DTOs;

/// <summary>
/// Request DTO for updating an existing user-company (portfolio) entry.
/// </summary>
public class UpdateUserCompanyDto
{
    /// <summary>
    /// Number of shares owned by the user.
    /// </summary>
    [Range(0, double.MaxValue, ErrorMessage = "Shares must be a non-negative number.")]
    public decimal? Shares { get; set; }

    /// <summary>
    /// Average purchase price per share.
    /// </summary>
    [Range(0, double.MaxValue, ErrorMessage = "Purchase price must be a non-negative number.")]
    public decimal? PurchasePrice { get; set; }

    /// <summary>
    /// Optional note or comment for this investment.
    /// </summary>
    [MaxLength(500)]
    public string? Notes { get; set; }
}
