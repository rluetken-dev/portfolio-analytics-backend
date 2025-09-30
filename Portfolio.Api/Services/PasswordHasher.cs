using System.Security.Cryptography;
using System.Text;

namespace Portfolio.Api.Services
{
    public static class PasswordHasher
    {
        // Hash a plain text password using SHA256
        public static string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(password);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        // Verify a password against an existing hash
        public static bool VerifyPassword(string password, string storedHash)
        {
            var hashOfInput = HashPassword(password);
            return hashOfInput == storedHash;
        }
    }
}
