using System.Security.Cryptography;
using Portfolio.Api.Data;
using Portfolio.Api.Models;

namespace Portfolio.Api.Services;

public static class RefreshTokenService
{
    private const int TokenSizeBytes = 32;
    private const int RefreshTokenLifetimeDays = 7;

    public static RefreshToken GenerateRefreshToken(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenSizeBytes));

        return new RefreshToken
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddDays(RefreshTokenLifetimeDays),
            UserId = user.Id,
            User = user
        };
    }

    public static async Task<RefreshToken> GenerateAndSaveAsync(
        User user,
        AppDbContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(context);

        RefreshToken refreshToken = GenerateRefreshToken(user);

        context.RefreshTokens.Add(refreshToken);
        await context.SaveChangesAsync(ct);

        return refreshToken;
    }
}