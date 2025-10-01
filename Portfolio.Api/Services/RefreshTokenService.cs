using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Portfolio.Api.Models;
using Portfolio.Api.Data;

namespace Portfolio.Api.Services
{
    public static class RefreshTokenService
    {
        // Generate a new refresh token for a given user
        public static RefreshToken GenerateRefreshToken(User user)
        {
            // Generate a secure random token string
            var randomNumber = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomNumber);
            }
            var tokenString = Convert.ToBase64String(randomNumber);

            return new RefreshToken
            {
                Token = tokenString,                   // random secure string
                ExpiresAt = DateTime.UtcNow.AddDays(7), // valid for 7 days
                UserId = user.Id,                      // link to the user
                User = user
            };
        }

        // Generate + save refresh token to the database
        public static async Task<RefreshToken> GenerateAndSaveAsync(User user, AppDbContext context)
        {
            var refreshToken = GenerateRefreshToken(user);

            // Save to database
            context.RefreshTokens.Add(refreshToken);
            await context.SaveChangesAsync();

            return refreshToken;
        }
    }
}
