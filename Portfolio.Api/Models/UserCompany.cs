using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore; 

namespace Portfolio.Api.Models;

/// <summary>
/// Represents a relationship between a user and a company (ticker) in their portfolio.
/// Stores user-specific data such as shares, purchase price, and notes.
/// </summary>
public class UserCompany
{
    /// <summary>
    /// Primary key of the UserCompany entry.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to the owning user.
    /// </summary>
    [Required]
    public int UserId { get; set; }

    /// <summary>
    /// Foreign key to the related ticker (company).
    /// </summary>
    [Required]
    public int TickerId { get; set; }

    /// <summary>
    /// Number of shares owned by the user.
    /// </summary>
    [Precision(18, 4)]
    public decimal? Shares { get; set; }

    /// <summary>
    /// Purchase price per share (optional).
    /// </summary>
    [Precision(18, 4)]
    public decimal? PurchasePrice { get; set; }

    /// <summary>
    /// Optional personal note about the holding (e.g. investment thesis or target price).
    /// </summary>
    [MaxLength(500)]
    public string? Notes { get; set; }

    /// <summary>
    /// Navigation to the related user.
    /// </summary>
    public User User { get; set; } = default!;

    /// <summary>
    /// Navigation to the related ticker (company).
    /// </summary>
    public Ticker Ticker { get; set; } = default!;
}
