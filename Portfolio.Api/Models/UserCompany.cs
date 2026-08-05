using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Portfolio.Api.Models;

/// <summary>
/// Represents a user's open portfolio position for one ticker.
/// </summary>
public sealed class UserCompany
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int TickerId { get; set; }

    [Precision(18, 4)]
    public decimal Shares { get; set; }

    [Precision(18, 4)]
    public decimal? PurchasePrice { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public User User { get; set; } = default!;

    public Ticker Ticker { get; set; } = default!;
}