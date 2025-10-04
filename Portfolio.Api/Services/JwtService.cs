using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Portfolio.Api.Models;

namespace Portfolio.Api.Services
{
    public static class JwtService
    {
        // Secret key for signing tokens (should be stored securely in appsettings.json)
        private static readonly string SecretKey = "my_ultra_secure_secret_key_1234567890!@#$";

        public static string GenerateToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(SecretKey);

            var claims = new[]
            {
                // ✅ Standard identity claims
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),

                // ✅ Custom claim (you can keep this for frontend convenience)
                new Claim("isAdmin", user.IsAdmin.ToString().ToLowerInvariant()),

                // ✅ Crucial for [Authorize(Roles = "Admin")]
                new Claim(ClaimTypes.Role, user.IsAdmin ? "Admin" : "User")
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(15),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}
