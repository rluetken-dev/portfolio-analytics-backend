namespace Portfolio.Api.Models
{
    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty; // Username of the account
        public string Password { get; set; } = string.Empty; // Plain text password (to verify against stored hash)
    }
}
