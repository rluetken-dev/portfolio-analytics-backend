using System.ComponentModel.DataAnnotations;

namespace Portfolio.Api.DTOs;

public sealed class LoginRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(64)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    [MaxLength(128)]
    public string Password { get; set; } = string.Empty;
}