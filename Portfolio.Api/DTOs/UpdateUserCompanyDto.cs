using System.ComponentModel.DataAnnotations;

namespace Portfolio.Api.DTOs;

/// <summary>
/// Request DTO for updating an existing portfolio entry.
/// </summary>
public sealed class UpdateUserCompanyDto
{
    /// <summary>
    /// Number of shares currently owned.
    /// </summary>
    [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "Shares must be non-negative.")]
    public decimal? Shares { get; set; }

    /// <summary>
    /// Average purchase price per share.
    /// </summary>
    [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "Purchase price must be non-negative.")]
    public decimal? PurchasePrice { get; set; }

    /// <summary>
    /// Optional user note.
    /// </summary>
    [MaxLength(500)]
    public string? Notes { get; set; }
}