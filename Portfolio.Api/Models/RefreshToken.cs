namespace Portfolio.Api.Models;

public sealed class RefreshToken
{
    public int Id { get; set; }

    public required string Token { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public bool IsActive => RevokedAt == null && ExpiresAt > DateTime.UtcNow;

    public int UserId { get; set; }

    public required User User { get; set; }
}